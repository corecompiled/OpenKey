using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using System.Text.Json;
using OpenKey.Core.Providers;

namespace OpenKey.Providers.OpenRouter;

public sealed class OpenRouterProvider : IChatProvider
{
    private const string BaseUrl = "https://openrouter.ai/api/v1";
    private const string RefererHeader = "https://openkey.local";
    private const string TitleHeader = "OpenKey";

    // Streaming needs per-read deadlines, not one deadline for the whole response. HttpClient.Timeout
    // covers reading the body even under ResponseHeadersRead, so a single blanket value aborts long
    // but perfectly healthy replies. These bound how long we wait for the *next* byte instead, which
    // is what actually distinguishes a slow model from a dead connection.
    private static readonly TimeSpan FirstTokenTimeout = TimeSpan.FromSeconds(45);
    private static readonly TimeSpan StallTimeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan ModelListTimeout = TimeSpan.FromSeconds(30);

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private readonly HttpClient _http;
    private readonly Func<string?> _keyProvider;

    public OpenRouterProvider(HttpClient http, Func<string?> keyProvider)
    {
        _http = http;
        _keyProvider = keyProvider;
    }

    public string Id => "openrouter";
    public string DisplayName => "OpenRouter";

    public async Task<IReadOnlyList<ModelInfo>> ListModelsAsync(CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, $"{BaseUrl}/models");
        ApplyHeaders(req);

        // HttpClient.Timeout is infinite so it can't abort long streams; this call is not a stream,
        // so it carries its own deadline.
        using var listCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        listCts.CancelAfter(ModelListTimeout);

        HttpResponseMessage resp;
        try
        {
            resp = await _http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, listCts.Token);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new ChatException(ChatErrorKind.NetworkDown, "OpenRouter didn't respond in time.");
        }
        catch (Exception ex) when (IsNetwork(ex))
        {
            throw new ChatException(ChatErrorKind.NetworkDown, "Network unreachable.", null, ex);
        }

        await using var stream = await resp.Content.ReadAsStreamAsync(listCts.Token);
        if (!resp.IsSuccessStatusCode)
        {
            var body = await ReadBodyAsync(stream, listCts.Token);
            throw MapHttpError(resp, body);
        }

        // A captive portal (hotel, airport, café) answers any request with its own login page and
        // an HTTP 200. Unguarded, that lands here as an unhandled JsonException during first run
        // and takes the whole window down with it.
        JsonDocument doc;
        try
        {
            doc = await JsonDocument.ParseAsync(stream, default, listCts.Token);
        }
        catch (JsonException ex)
        {
            throw new ChatException(
                ChatErrorKind.NetworkDown,
                "Got a reply from the network, but it wasn't from OpenRouter. "
                    + "If you're on public Wi-Fi you may still need to sign in to it.",
                null,
                ex);
        }

        using (doc)
        {
        if (!doc.RootElement.TryGetProperty("data", out var data)
            || data.ValueKind != JsonValueKind.Array)
        {
            throw new ChatException(
                ChatErrorKind.MalformedResponse,
                "OpenRouter's model list came back in a format OpenKey doesn't recognise.");
        }

        var list = new List<ModelInfo>(capacity: data.GetArrayLength());
        foreach (var m in data.EnumerateArray())
        {
            var id = m.GetProperty("id").GetString();
            if (string.IsNullOrEmpty(id)) continue;

            var name = m.TryGetProperty("name", out var n) ? (n.GetString() ?? id) : id;

            int context = 0;
            if (m.TryGetProperty("context_length", out var cl) && cl.ValueKind == JsonValueKind.Number)
                context = cl.GetInt32();

            bool isFree = IsFreeModel(m, id);

            list.Add(new ModelInfo(id, name, context, isFree));
        }

        return list;
        }
    }

    public async IAsyncEnumerable<ChatChunk> StreamChatAsync(
        ChatRequest request,
        [EnumeratorCancellation] CancellationToken ct)
    {
        var body = new ChatCompletionRequest(
            Model: request.Model,
            Messages: request.Messages.Select(m => new WireMessage(m.Role, m.Content)).ToArray(),
            Stream: true,
            MaxTokens: request.MaxTokens ?? 2048,
            Temperature: request.Temperature);

        using var req = new HttpRequestMessage(HttpMethod.Post, $"{BaseUrl}/chat/completions")
        {
            Content = JsonContent.Create(body, OpenRouterJsonContext.Default.ChatCompletionRequest),
        };
        ApplyHeaders(req);

        HttpResponseMessage resp;
        try
        {
            // Deadline covers only getting response headers back. Once the stream is open the
            // per-read deadlines below take over, so a slow-but-alive model is never cut off.
            using var connectCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            connectCts.CancelAfter(FirstTokenTimeout);
            resp = await _http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, connectCts.Token);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new ChatException(ChatErrorKind.TransientServer, "The model didn't accept the request in time.");
        }
        catch (Exception ex) when (IsNetwork(ex))
        {
            throw new ChatException(ChatErrorKind.NetworkDown, "Network unreachable.", null, ex);
        }

        await using var stream = await resp.Content.ReadAsStreamAsync(ct);

        if (!resp.IsSuccessStatusCode)
        {
            var errBody = await ReadBodyAsync(stream, ct);
            throw MapHttpError(resp, errBody);
        }

        using var reader = new StreamReader(stream);

        // Some models never emit a `finish_reason` chunk and simply close with [DONE]. Remember the
        // last one seen so a complete reply isn't reported as unfinished, discarded, and retried.
        string? lastFinishReason = null;

        var sawAnyData = false;

        while (true)
        {
            string? line;

            // Bound the wait for the *next* line, not the whole response.
            using var readCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            readCts.CancelAfter(sawAnyData ? StallTimeout : FirstTokenTimeout);

            try
            {
                line = await reader.ReadLineAsync(readCts.Token);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                // Our deadline fired, not the user's cancellation. Transient, so rotation moves on
                // to a model that is actually producing output.
                throw new ChatException(
                    ChatErrorKind.TransientServer,
                    sawAnyData
                        ? "The model stopped part-way through its reply."
                        : "The model didn't start replying in time.");
            }
            catch (Exception ex) when (IsNetwork(ex))
            {
                throw new ChatException(ChatErrorKind.NetworkDown, "Connection lost mid-stream.", null, ex);
            }

            sawAnyData = true;

            if (line is null) break;
            if (line.Length == 0) continue;
            if (line.StartsWith(':')) continue;
            if (!line.StartsWith("data: ", StringComparison.Ordinal)) continue;

            var payload = line.Substring("data: ".Length);
            if (payload == "[DONE]")
            {
                // "stop" is the correct default: the server closed the stream cleanly, which is
                // exactly what a normal completion looks like.
                yield return new ChatChunk(
                    string.Empty, IsFinal: true, FinishReason: lastFinishReason ?? "stop");
                yield break;
            }

            ChatChunk? parsed;
            try
            {
                parsed = ParseChunk(payload);
            }
            catch (JsonException ex)
            {
                throw new ChatException(ChatErrorKind.MalformedResponse, "Bad SSE chunk.", null, ex);
            }

            if (parsed is null) continue;
            if (parsed.FinishReason is not null) lastFinishReason = parsed.FinishReason;
            yield return parsed;
            if (parsed.IsFinal) yield break;
        }
    }

    private static ChatChunk? ParseChunk(string payload)
    {
        using var doc = JsonDocument.Parse(payload);

        if (doc.RootElement.TryGetProperty("error", out var errEl))
        {
            var msg = errEl.TryGetProperty("message", out var mEl) ? mEl.GetString() : "Unknown error";
            throw new ChatException(ChatErrorKind.MalformedResponse, msg ?? "Unknown error");
        }

        if (!doc.RootElement.TryGetProperty("choices", out var choices) || choices.GetArrayLength() == 0)
            return null;

        var choice = choices[0];
        string? text = null;
        if (choice.TryGetProperty("delta", out var delta)
            && delta.TryGetProperty("content", out var c)
            && c.ValueKind == JsonValueKind.String)
        {
            text = c.GetString();
        }

        string? finish = null;
        if (choice.TryGetProperty("finish_reason", out var f) && f.ValueKind == JsonValueKind.String)
            finish = f.GetString();

        if (string.IsNullOrEmpty(text) && finish is null) return null;

        return new ChatChunk(text ?? string.Empty, IsFinal: finish is not null, FinishReason: finish);
    }

    private void ApplyHeaders(HttpRequestMessage req)
    {
        var key = _keyProvider();
        if (!string.IsNullOrEmpty(key))
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);

        req.Headers.TryAddWithoutValidation("HTTP-Referer", RefererHeader);
        req.Headers.TryAddWithoutValidation("X-Title", TitleHeader);
    }

    private static bool IsFreeModel(JsonElement model, string id)
    {
        if (id.EndsWith(":free", StringComparison.Ordinal)) return true;
        if (!model.TryGetProperty("pricing", out var p)) return false;
        return IsZero(p, "prompt") && IsZero(p, "completion");
    }

    private static bool IsZero(JsonElement pricing, string field)
    {
        if (!pricing.TryGetProperty(field, out var v)) return false;

        // Parse rather than string-match: exact comparison against "0"/"0.0"/"0.00" silently
        // classified a free model as paid the moment OpenRouter formatted it as "0.000000".
        if (v.ValueKind == JsonValueKind.Number) return v.GetDouble() == 0d;

        var s = v.ValueKind == JsonValueKind.String ? v.GetString() : v.ToString();
        return double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var d)
            && d == 0d;
    }

    private static async Task<string> ReadBodyAsync(Stream stream, CancellationToken ct)
    {
        using var reader = new StreamReader(stream);
        return await reader.ReadToEndAsync(ct);
    }

    private static ChatException MapHttpError(HttpResponseMessage resp, string body)
    {
        var message = ExtractErrorMessage(body) ?? resp.ReasonPhrase ?? "HTTP error";
        string? retryAfter = resp.Headers.TryGetValues("Retry-After", out var ra) ? ra.FirstOrDefault() : null;

        var status = (int)resp.StatusCode;
        var kind = resp.StatusCode switch
        {
            HttpStatusCode.Unauthorized         => ChatErrorKind.AuthFailure,
            HttpStatusCode.Forbidden            => ChatErrorKind.AuthFailure,
            HttpStatusCode.PaymentRequired      => ChatErrorKind.QuotaExhausted,
            HttpStatusCode.RequestTimeout       => ChatErrorKind.TransientServer,
            HttpStatusCode.TooManyRequests      => ChatErrorKind.TransientRateLimit,
            // 400/404/422 mean the request is wrong (context overflow, unknown model). Retrying it
            // unchanged on five other models cannot work and cools all of them down on the way.
            HttpStatusCode.BadRequest           => ChatErrorKind.InvalidRequest,
            HttpStatusCode.NotFound             => ChatErrorKind.InvalidRequest,
            HttpStatusCode.UnprocessableEntity  => ChatErrorKind.InvalidRequest,
            _ when status >= 500 && status < 600 => ChatErrorKind.TransientServer,
            _                                   => ChatErrorKind.MalformedResponse,
        };

        return new ChatException(kind, $"HTTP {status}: {message}", retryAfter);
    }

    private static string? ExtractErrorMessage(string body)
    {
        if (string.IsNullOrWhiteSpace(body)) return null;
        try
        {
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.TryGetProperty("error", out var err))
            {
                if (err.ValueKind == JsonValueKind.Object
                    && err.TryGetProperty("message", out var m)
                    && m.ValueKind == JsonValueKind.String)
                {
                    return m.GetString();
                }
                if (err.ValueKind == JsonValueKind.String) return err.GetString();
            }
        }
        catch (JsonException)
        {
            // body is not JSON
        }
        return body.Length > 200 ? body[..200] : body;
    }

    private static bool IsNetwork(Exception ex)
    {
        if (ex is HttpRequestException or SocketException or IOException) return true;
        if (ex is TaskCanceledException tce && !tce.CancellationToken.IsCancellationRequested) return true;
        return false;
    }
}
