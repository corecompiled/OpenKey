using System.Net;
using System.Text;
using OpenKey.Core.Updates;
using Xunit;

namespace OpenKey.Core.Tests;

public sealed class UpdateCheckerTests
{
    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly HttpStatusCode _status;
        private readonly string _body;
        private readonly Exception? _throw;

        public StubHandler(string body, HttpStatusCode status = HttpStatusCode.OK, Exception? toThrow = null)
        {
            _body = body;
            _status = status;
            _throw = toThrow;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            if (_throw is not null) throw _throw;
            return Task.FromResult(new HttpResponseMessage(_status)
            {
                Content = new StringContent(_body, Encoding.UTF8, "application/json"),
            });
        }
    }

    private static GitHubUpdateChecker Checker(string body, HttpStatusCode status = HttpStatusCode.OK, Exception? toThrow = null) =>
        new(new HttpClient(new StubHandler(body, status, toThrow)));

    private static string Release(string tag, bool draft = false, bool prerelease = false) =>
        $$"""{"tag_name":"{{tag}}","html_url":"https://example.com/{{tag}}","draft":{{(draft ? "true" : "false")}},"prerelease":{{(prerelease ? "true" : "false")}}}""";

    [Fact]
    public async Task ReportsANewerRelease()
    {
        var found = await Checker(Release("v0.4.0")).CheckAsync("0.3.0", CancellationToken.None);

        Assert.NotNull(found);
        Assert.Equal("0.4.0", found!.Version);
    }

    [Fact]
    public async Task SaysNothingWhenAlreadyCurrent()
    {
        Assert.Null(await Checker(Release("v0.3.0")).CheckAsync("0.3.0", CancellationToken.None));
    }

    [Fact]
    public async Task SaysNothingWhenRunningAheadOfTheRelease()
    {
        Assert.Null(await Checker(Release("v0.2.1")).CheckAsync("0.3.0", CancellationToken.None));
    }

    [Fact]
    public async Task IgnoresDraftsAndPrereleases()
    {
        Assert.Null(await Checker(Release("v9.9.9", draft: true)).CheckAsync("0.3.0", CancellationToken.None));
        Assert.Null(await Checker(Release("v9.9.9", prerelease: true)).CheckAsync("0.3.0", CancellationToken.None));
    }

    [Theory]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.Forbidden)]          // rate limited
    [InlineData(HttpStatusCode.InternalServerError)]
    public async Task StaysSilentOnAnyHttpFailure(HttpStatusCode status)
    {
        Assert.Null(await Checker("{}", status).CheckAsync("0.3.0", CancellationToken.None));
    }

    [Fact]
    public async Task StaysSilentWhenOffline()
    {
        // A failed check is not worth a word on screen, let alone an error card.
        var checker = Checker("", toThrow: new HttpRequestException("no network"));

        Assert.Null(await checker.CheckAsync("0.3.0", CancellationToken.None));
    }

    [Fact]
    public async Task StaysSilentOnNonsenseResponses()
    {
        Assert.Null(await Checker("not json").CheckAsync("0.3.0", CancellationToken.None));
        Assert.Null(await Checker("""{"tag_name":""}""").CheckAsync("0.3.0", CancellationToken.None));
        Assert.Null(await Checker("""{"tag_name":"banana"}""").CheckAsync("0.3.0", CancellationToken.None));
    }

    [Theory]
    [InlineData("0.10.0", "0.9.0", true)]   // numeric, not alphabetical
    [InlineData("0.9.0", "0.10.0", false)]
    [InlineData("1.0.0", "0.99.99", true)]
    [InlineData("v0.4.0", "0.4.0", false)]
    // Prerelease suffixes are stripped, so 0.4.0 and 0.4.0-beta compare equal rather than the
    // former being treated as newer. OpenKey never publishes prereleases, so the case cannot
    // arise in practice; full semver ordering would be complexity with no caller.
    [InlineData("0.4.0", "0.4.0-beta", false)]
    public void ComparesReleaseNumbersNotStrings(string candidate, string current, bool expected)
    {
        Assert.Equal(expected, GitHubUpdateChecker.IsNewer(candidate, current));
    }

    [Fact]
    public void AnUnparseableVersionNeverCountsAsNewer()
    {
        // A bad comparison must not nag someone about an update that isn't real.
        Assert.False(GitHubUpdateChecker.IsNewer("not-a-version", "0.3.0"));
        Assert.False(GitHubUpdateChecker.IsNewer("0.4.0", "not-a-version"));
    }

    [Theory]
    [InlineData("v1.2.3", "1.2.3")]
    [InlineData("1.2.3+abc123", "1.2.3")]
    [InlineData("V1.2.3-beta", "1.2.3")]
    public void TagsAreNormalisedBeforeComparing(string tag, string expected)
    {
        Assert.Equal(expected, GitHubUpdateChecker.Normalize(tag));
    }
}
