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

        HttpResponseMessage resp;
        try
        {
            resp = await _http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
        }
        catch (Exception ex) when (IsNetwork(ex))
        {
            throw new ChatException(ChatErrorKind.NetworkDown, "Network unreachable.", null, ex);
        }

        await using var stream = await resp.Content.ReadAsStreamAsync(ct);
        if (!resp.IsSuccessStatusCode)
        {
            var body = await ReadBodyAsync(stream, ct);
            throw MapHttpError(resp, body);
        }

        using var doc = await JsonDocument.ParseAsync(stream, default, ct);
        var data = doc.RootElement.GetProperty("data");

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

    public async IAsyncEnumerable<ChatChunk> StreamChatAsync(
        ChatRequest request,
        [EnumeratorCancellation] CancellationToken ct)
    {
        var body = new
        {
            model = request.Model,
            messages = request.Messages.Select(m => new { role = m.Role, content = m.Content }).ToArray(),
            stream = true,
            max_tokens = request.MaxTokens ?? 2048,
            temperature = request.Temperature,
        };

        using var req = new HttpRequestMessage(HttpMethod.Post, $"{BaseUrl}/chat/completions")
        {
            Content = JsonContent.Create(body, options: JsonOpts),
        };
        ApplyHeaders(req);

        HttpResponseMessage resp;
        try
        {
            resp = await _http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
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

        while (true)
        {
            string? line;
            try
            {
                line = await reader.ReadLineAsync(ct);
            }
            catch (Exception ex) when (IsNetwork(ex))
            {
                throw new ChatException(ChatErrorKind.NetworkDown, "Connection lost mid-stream.", null, ex);
            }

            if (line is null) break;
            if (line.Length == 0) continue;
            if (line.StartsWith(':')) continue;
            if (!line.StartsWith("data: ", StringComparison.Ordinal)) continue;

            var payload = line.Substring("data: ".Length);
            if (payload == "[DONE]")
            {
                yield return new ChatChunk(string.Empty, IsFinal: true, FinishReason: null);
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
        var s = v.ValueKind == JsonValueKind.String ? v.GetString() : v.ToString();
        return s == "0" || s == "0.0" || s == "0.00";
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
            HttpStatusCode.Unauthorized                                                  => ChatErrorKind.AuthFailure,
            HttpStatusCode.Forbidden                                                     => ChatErrorKind.AuthFailure,
            HttpStatusCode.PaymentRequired                                               => ChatErrorKind.QuotaExhausted,
            HttpStatusCode.RequestTimeout                                                => ChatErrorKind.TransientServer,
            HttpStatusCode.TooManyRequests                                               => ChatErrorKind.TransientRateLimit,
            _ when status >= 500 && status < 600                                          => ChatErrorKind.TransientServer,
            _                                                                             => ChatErrorKind.MalformedResponse,
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
