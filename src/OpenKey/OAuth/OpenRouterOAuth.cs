using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using OpenKey.Core.Providers;

namespace OpenKey.OAuth;

public sealed class OpenRouterOAuth
{
    private const string AuthBase = "https://openrouter.ai/auth";
    private const string ExchangeUrl = "https://openrouter.ai/api/v1/auth/keys";
    private const int CallbackPort = 3000;
    private const string CallbackUrl = "http://localhost:3000/callback";
    private const string ListenerPrefix = "http://localhost:3000/callback/";
    private static readonly TimeSpan CallbackTimeout = TimeSpan.FromMinutes(5);

    private readonly HttpClient _http;

    public OpenRouterOAuth(HttpClient http) => _http = http;

    public async Task<string> AcquireKeyAsync(Action<string> onAuthUrl, CancellationToken ct)
    {
        var pkce = PkceCodes.Generate();

        // Fixed callback URL per OpenRouter docs (recommended for local-first apps).
        // Varying the callback per attempt causes "Failed to create or update app" 409s server-side.
        using var listener = new HttpListener();
        listener.Prefixes.Add(ListenerPrefix);
        try
        {
            listener.Start();
        }
        catch (HttpListenerException ex)
        {
            throw new OAuthPortInUseException(CallbackPort, ex);
        }

        var authUrl =
            $"{AuthBase}?callback_url={Uri.EscapeDataString(CallbackUrl)}" +
            $"&code_challenge={Uri.EscapeDataString(pkce.CodeChallenge)}" +
            $"&code_challenge_method={PkceCodes.ChallengeMethod}";

        onAuthUrl(authUrl);
        TryOpenBrowser(authUrl);

        string code;
        try
        {
            code = await WaitForCallbackAsync(listener, ct);
        }
        finally
        {
            try { listener.Stop(); } catch { /* best-effort */ }
        }

        return await ExchangeCodeAsync(code, pkce.CodeVerifier, ct);
    }

    private static async Task<string> WaitForCallbackAsync(HttpListener listener, CancellationToken ct)
    {
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(CallbackTimeout);

        var getCtxTask = listener.GetContextAsync();
        var tcs = new TaskCompletionSource();
        using (timeoutCts.Token.Register(() => tcs.TrySetResult()))
        {
            var winner = await Task.WhenAny(getCtxTask, tcs.Task);
            if (winner != getCtxTask)
            {
                ct.ThrowIfCancellationRequested();
                throw new ChatException(
                    ChatErrorKind.TransientServer,
                    "Timed out waiting for browser sign-in (5 min).");
            }
        }

        HttpListenerContext ctx;
        try
        {
            ctx = await getCtxTask;
        }
        catch (HttpListenerException) when (ct.IsCancellationRequested)
        {
            throw new OperationCanceledException(ct);
        }
        catch (ObjectDisposedException) when (ct.IsCancellationRequested)
        {
            throw new OperationCanceledException(ct);
        }
        var code = ctx.Request.QueryString["code"];
        var error = ctx.Request.QueryString["error"];

        await WriteResponseAsync(ctx.Response, code, error);

        if (!string.IsNullOrEmpty(error))
            throw new ChatException(ChatErrorKind.AuthFailure, $"OAuth declined: {error}");
        if (string.IsNullOrEmpty(code))
            throw new ChatException(ChatErrorKind.MalformedResponse, "Callback missing `code` parameter.");

        return code;
    }

    private async Task<string> ExchangeCodeAsync(string code, string verifier, CancellationToken ct)
    {
        // One retry on HTTP 409 with a short backoff. The 409 is documented as a possible
        // conflict on auth-code create/update; usually transient.
        for (int attempt = 1; attempt <= 2; attempt++)
        {
            HttpResponseMessage resp;
            try
            {
                resp = await PostExchangeAsync(code, verifier, ct);
            }
            catch (Exception ex) when (ex is HttpRequestException or SocketException or IOException)
            {
                throw new ChatException(ChatErrorKind.NetworkDown, "Network unreachable.", null, ex);
            }

            var text = await resp.Content.ReadAsStringAsync(ct);
            var status = (int)resp.StatusCode;

            if (resp.IsSuccessStatusCode)
                return ParseKey(text);

            if (status == 409 && attempt == 1)
            {
                await Task.Delay(750, ct);
                continue;
            }

            var msg = ExtractError(text) ?? resp.ReasonPhrase ?? "exchange failed";
            var kind = resp.StatusCode switch
            {
                HttpStatusCode.Unauthorized                        => ChatErrorKind.AuthFailure,
                HttpStatusCode.Forbidden                           => ChatErrorKind.AuthFailure,
                HttpStatusCode.BadRequest                          => ChatErrorKind.MalformedResponse,
                HttpStatusCode.Conflict                            => ChatErrorKind.TransientServer,
                _ when status >= 500 && status < 600               => ChatErrorKind.TransientServer,
                _                                                   => ChatErrorKind.MalformedResponse,
            };
            throw new ChatException(kind, $"HTTP {status}: {msg}");
        }

        // Unreachable — the loop above either returns, retries, or throws.
        throw new ChatException(ChatErrorKind.TransientServer, "OAuth exchange exhausted retries.");
    }

    private Task<HttpResponseMessage> PostExchangeAsync(string code, string verifier, CancellationToken ct)
    {
        var body = new KeyExchangeRequest(code, verifier, PkceCodes.ChallengeMethod);
        return _http.PostAsJsonAsync(ExchangeUrl, body, OAuthJsonContext.Default.KeyExchangeRequest, ct);
    }

    private static string ParseKey(string text)
    {
        try
        {
            using var doc = JsonDocument.Parse(text);
            if (doc.RootElement.TryGetProperty("key", out var k) && k.ValueKind == JsonValueKind.String)
            {
                var key = k.GetString();
                if (!string.IsNullOrEmpty(key)) return key!;
            }
            throw new ChatException(ChatErrorKind.MalformedResponse, "Exchange response missing `key`.");
        }
        catch (JsonException ex)
        {
            throw new ChatException(ChatErrorKind.MalformedResponse, "Could not parse exchange response.", null, ex);
        }
    }

    private static void TryOpenBrowser(string url)
    {
        try
        {
            var psi = new ProcessStartInfo(url) { UseShellExecute = true };
            Process.Start(psi);
        }
        catch
        {
            // Caller already printed the URL via onAuthUrl. User can copy-paste manually.
        }
    }

    private static async Task WriteResponseAsync(HttpListenerResponse resp, string? code, string? error)
    {
        var (status, title, body) = string.IsNullOrEmpty(code) || !string.IsNullOrEmpty(error)
            ? (400, "Sign-in failed", $"<p>OpenRouter reported an error: <code>{Escape(error ?? "no code")}</code></p><p>Close this tab and return to OpenKey.</p>")
            : (200, "Signed in", "<p>You're signed in. Return to OpenKey — this tab can be closed.</p>");

        var html = $"<!doctype html><meta charset=utf-8><title>OpenKey — {title}</title>" +
                   "<style>body{font-family:system-ui,sans-serif;max-width:480px;margin:4rem auto;padding:0 1rem;color:#222}h1{font-size:1.4rem}code{background:#eee;padding:.1rem .3rem;border-radius:3px}</style>" +
                   $"<h1>OpenKey — {title}</h1>{body}";

        var bytes = Encoding.UTF8.GetBytes(html);
        resp.StatusCode = status;
        resp.ContentType = "text/html; charset=utf-8";
        resp.ContentLength64 = bytes.Length;
        try
        {
            await resp.OutputStream.WriteAsync(bytes);
            resp.OutputStream.Close();
        }
        catch (HttpListenerException) { /* browser closed early */ }
        catch (IOException) { /* same */ }
    }

    private static string Escape(string s) =>
        s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");

    private static string? ExtractError(string body)
    {
        if (string.IsNullOrWhiteSpace(body)) return null;
        try
        {
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.TryGetProperty("error", out var err))
            {
                if (err.ValueKind == JsonValueKind.String) return err.GetString();
                if (err.ValueKind == JsonValueKind.Object
                    && err.TryGetProperty("message", out var m)
                    && m.ValueKind == JsonValueKind.String)
                    return m.GetString();
            }
        }
        catch (JsonException) { }
        return body.Length > 200 ? body[..200] : body;
    }
}
