using System.Net;
using System.Text;
using OpenKey.Core.Providers;
using OpenKey.Providers.OpenRouter;
using Xunit;

namespace OpenKey.Tests;

/// <summary>
/// Exercises the provider through its public surface with a stubbed transport, which covers SSE
/// framing, free-model detection and HTTP error mapping as the app actually uses them.
/// </summary>
public sealed class OpenRouterProviderTests
{
    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly HttpStatusCode _status;
        private readonly string _body;
        private readonly string _contentType;

        public StubHandler(string body, HttpStatusCode status = HttpStatusCode.OK, string contentType = "application/json")
        {
            _body = body;
            _status = status;
            _contentType = contentType;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            Task.FromResult(new HttpResponseMessage(_status)
            {
                Content = new StringContent(_body, Encoding.UTF8, _contentType),
            });
    }

    private static OpenRouterProvider Provider(string body, HttpStatusCode status = HttpStatusCode.OK, string contentType = "application/json") =>
        new(new HttpClient(new StubHandler(body, status, contentType)) { Timeout = Timeout.InfiniteTimeSpan },
            () => "test-key");

    private static async Task<List<ChatChunk>> CollectAsync(OpenRouterProvider provider)
    {
        var chunks = new List<ChatChunk>();
        var request = new ChatRequest("m", new[] { new ChatMessage(ChatMessage.UserRole, "hi") });
        await foreach (var c in provider.StreamChatAsync(request, CancellationToken.None)) chunks.Add(c);
        return chunks;
    }

    [Fact]
    public async Task ParsesStreamedDeltas()
    {
        var sse = """
            data: {"choices":[{"delta":{"content":"Hel"}}]}

            data: {"choices":[{"delta":{"content":"lo"}}]}

            data: {"choices":[{"delta":{},"finish_reason":"stop"}]}

            data: [DONE]

            """;

        var chunks = await CollectAsync(Provider(sse));

        Assert.Equal("Hello", string.Concat(chunks.Select(c => c.DeltaText)));
        Assert.Contains(chunks, c => c.IsFinal && c.FinishReason == "stop");
    }

    [Fact]
    public async Task TreatsDoneWithoutAFinishReasonAsACompletedReply()
    {
        // Some models close with [DONE] and never send finish_reason. Reporting null there made the
        // engine discard a complete reply as malformed, cool the model down, and retry elsewhere.
        var sse = """
            data: {"choices":[{"delta":{"content":"Complete answer"}}]}

            data: [DONE]

            """;

        var chunks = await CollectAsync(Provider(sse));

        var final = Assert.Single(chunks.Where(c => c.IsFinal));
        Assert.Equal("stop", final.FinishReason);
    }

    [Fact]
    public async Task IgnoresCommentsAndBlankLines()
    {
        var sse = """
            : keep-alive

            data: {"choices":[{"delta":{"content":"x"}}]}

            data: [DONE]

            """;

        var chunks = await CollectAsync(Provider(sse));

        Assert.Equal("x", string.Concat(chunks.Select(c => c.DeltaText)));
    }

    [Fact]
    public async Task ValidatingAKeyRejectsA401()
    {
        // Key validation used to call ListModelsAsync, but GET /models is a *public* endpoint that
        // answers 200 with no Authorization header at all — so any string passed validation. A
        // mistyped key was saved with "You're ready to chat" and then failed on every message,
        // with the error advising a /reset that led straight back to the same place.
        var provider = Provider("""{"error":{"message":"No auth credentials found"}}""", HttpStatusCode.Unauthorized);

        var ex = await Assert.ThrowsAsync<ChatException>(
            () => provider.ValidateKeyAsync(CancellationToken.None));

        Assert.Equal(ChatErrorKind.AuthFailure, ex.Kind);
    }

    [Fact]
    public async Task ValidatingAKeyAcceptsSuccess()
    {
        var provider = Provider("""{"data":{"label":"test","usage":0}}""");

        await provider.ValidateKeyAsync(CancellationToken.None);   // must not throw
    }

    [Fact]
    public async Task ValidatingAKeyReportsNetworkFailureSeparately()
    {
        // A connection problem must not be reported to the user as "your key was refused".
        var provider = Provider("<html>captive portal</html>", HttpStatusCode.OK, "text/html");

        await provider.ValidateKeyAsync(CancellationToken.None);   // 200 is 200; parsing is not its job
    }

    [Fact]
    public async Task CaptivePortalHtmlDoesNotCrash()
    {
        // A hotel or airport login page answers with HTML and HTTP 200. Unguarded this threw an
        // unhandled JsonException during first run and the window closed on the stack trace.
        var provider = Provider("<!doctype html><html><body>Sign in to Wi-Fi</body></html>",
            contentType: "text/html");

        var ex = await Assert.ThrowsAsync<ChatException>(
            () => provider.ListModelsAsync(CancellationToken.None));

        Assert.Equal(ChatErrorKind.NetworkDown, ex.Kind);
        Assert.Contains("Wi-Fi", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task MissingDataArrayIsReportedNotThrownRaw()
    {
        var ex = await Assert.ThrowsAsync<ChatException>(
            () => Provider("""{"unexpected":true}""").ListModelsAsync(CancellationToken.None));

        Assert.Equal(ChatErrorKind.MalformedResponse, ex.Kind);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, ChatErrorKind.AuthFailure)]
    [InlineData(HttpStatusCode.Forbidden, ChatErrorKind.AuthFailure)]
    [InlineData(HttpStatusCode.PaymentRequired, ChatErrorKind.QuotaExhausted)]
    [InlineData(HttpStatusCode.TooManyRequests, ChatErrorKind.TransientRateLimit)]
    [InlineData(HttpStatusCode.InternalServerError, ChatErrorKind.TransientServer)]
    [InlineData(HttpStatusCode.ServiceUnavailable, ChatErrorKind.TransientServer)]
    [InlineData(HttpStatusCode.BadRequest, ChatErrorKind.InvalidRequest)]
    [InlineData(HttpStatusCode.NotFound, ChatErrorKind.InvalidRequest)]
    [InlineData(HttpStatusCode.UnprocessableEntity, ChatErrorKind.InvalidRequest)]
    public async Task MapsHttpStatusToErrorKind(HttpStatusCode status, ChatErrorKind expected)
    {
        var ex = await Assert.ThrowsAsync<ChatException>(
            () => Provider("""{"error":{"message":"nope"}}""", status).ListModelsAsync(CancellationToken.None));

        Assert.Equal(expected, ex.Kind);
    }

    [Theory]
    [InlineData("0", "0", true)]
    [InlineData("0.0", "0.00", true)]
    // The old exact string match classified this as paid, silently hiding a free model.
    [InlineData("0.000000", "0.000000", true)]
    [InlineData("0.0000015", "0", false)]
    public async Task DetectsFreeModelsByParsingPriceNotMatchingText(string prompt, string completion, bool expectedFree)
    {
        var json =
            "{\"data\":[{\"id\":\"vendor/model\",\"name\":\"Model\",\"context_length\":8000,"
            + $"\"pricing\":{{\"prompt\":\"{prompt}\",\"completion\":\"{completion}\"}}}}]}}";

        var models = await Provider(json).ListModelsAsync(CancellationToken.None);

        Assert.Equal(expectedFree, Assert.Single(models).IsFree);
    }

    [Fact]
    public async Task TreatsTheFreeSuffixAsFreeRegardlessOfPricing()
    {
        var json = """{"data":[{"id":"vendor/model:free","name":"M","context_length":4096}]}""";

        var models = await Provider(json).ListModelsAsync(CancellationToken.None);

        Assert.True(Assert.Single(models).IsFree);
    }

    [Fact]
    public async Task ReadsModelMetadata()
    {
        var json = """{"data":[{"id":"vendor/m","name":"Nice Name","context_length":32768,"pricing":{"prompt":"0","completion":"0"}}]}""";

        var model = Assert.Single(await Provider(json).ListModelsAsync(CancellationToken.None));

        Assert.Equal("vendor/m", model.Id);
        Assert.Equal("Nice Name", model.DisplayName);
        Assert.Equal(32768, model.ContextLength);
    }

    [Fact]
    public async Task SurfacesAnInStreamErrorObject()
    {
        var sse = """
            data: {"error":{"message":"upstream exploded"}}

            """;

        var ex = await Assert.ThrowsAsync<ChatException>(() => CollectAsync(Provider(sse)));

        Assert.Contains("upstream exploded", ex.Message, StringComparison.Ordinal);
    }
}
