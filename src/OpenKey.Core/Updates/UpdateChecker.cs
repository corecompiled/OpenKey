using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace OpenKey.Core.Updates;

/// <summary>A newer release than the one running.</summary>
public sealed record UpdateInfo(string Version, string Url);

public interface IUpdateChecker
{
    /// <summary>
    /// The newest release if it is newer than <paramref name="currentVersion"/>, otherwise null.
    /// Never throws: a failed check is not worth a word on screen, let alone an error.
    /// </summary>
    Task<UpdateInfo?> CheckAsync(string currentVersion, CancellationToken ct);
}

/// <summary>
/// Asks GitHub whether a newer release exists.
/// <para>
/// <b>Notify only — OpenKey never downloads or installs anything by itself.</b> A tool that
/// replaces its own binary is a tool people are right to distrust, and it fights the "one file you
/// can copy anywhere" model. That is a standing project rule, not a default.
/// </para>
/// <para>
/// This is the only request OpenKey makes to anywhere other than OpenRouter, which is why it is
/// switchable off in <c>config.json</c> and documented in SECURITY.md. It sends no identifiers and
/// no usage data — it is an unauthenticated GET of a public page, the same one a browser would
/// fetch. But it does reveal that someone launched the app, so declaring it plainly and letting
/// people decline is the honest treatment.
/// </para>
/// </summary>
public sealed class GitHubUpdateChecker : IUpdateChecker
{
    private const string LatestReleaseUrl =
        "https://api.github.com/repos/corecompiled/OpenKey/releases/latest";

    /// <summary>Short: this runs at launch and must never delay the app becoming usable.</summary>
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    private readonly HttpClient _http;

    public GitHubUpdateChecker(HttpClient http) => _http = http;

    public async Task<UpdateInfo?> CheckAsync(string currentVersion, CancellationToken ct)
    {
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, LatestReleaseUrl);
            req.Headers.TryAddWithoutValidation("Accept", "application/vnd.github+json");
            req.Headers.TryAddWithoutValidation("User-Agent", "OpenKey");

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(Timeout);

            using var resp = await _http.SendAsync(req, cts.Token);
            if (!resp.IsSuccessStatusCode) return null;

            var release = await resp.Content.ReadFromJsonAsync(
                UpdateJsonContext.Default.GitHubRelease, cts.Token);

            if (release?.TagName is not { Length: > 0 } tag) return null;
            if (release.Draft || release.Prerelease) return null;

            return IsNewer(tag, currentVersion)
                ? new UpdateInfo(Normalize(tag), release.HtmlUrl ?? "https://github.com/corecompiled/OpenKey/releases")
                : null;
        }
        catch (Exception)
        {
            // Offline, rate-limited, captive portal, malformed response — none of it matters
            // enough to tell the user about. Silence is the correct outcome.
            return null;
        }
    }

    internal static string Normalize(string version) =>
        version.TrimStart('v', 'V').Split('-', '+')[0].Trim();

    /// <summary>
    /// Compares release numbers, not strings: "0.10.0" is newer than "0.9.0" even though it sorts
    /// earlier alphabetically. Anything unparseable is treated as "not newer" — a bad comparison
    /// should never nag someone about an update that isn't real.
    /// </summary>
    internal static bool IsNewer(string candidate, string current)
    {
        if (!Version.TryParse(Pad(Normalize(candidate)), out var a)) return false;
        if (!Version.TryParse(Pad(Normalize(current)), out var b)) return false;
        return a > b;
    }

    // Version.TryParse rejects a bare "1"; pad to at least major.minor.
    private static string Pad(string v) => v.Count(c => c == '.') switch
    {
        0 => v + ".0.0",
        1 => v + ".0",
        _ => v,
    };
}

internal sealed record GitHubRelease(
    [property: JsonPropertyName("tag_name")] string? TagName,
    [property: JsonPropertyName("html_url")] string? HtmlUrl,
    [property: JsonPropertyName("draft")] bool Draft,
    [property: JsonPropertyName("prerelease")] bool Prerelease);

[JsonSourceGenerationOptions(PropertyNameCaseInsensitive = true)]
[JsonSerializable(typeof(GitHubRelease))]
internal sealed partial class UpdateJsonContext : System.Text.Json.Serialization.JsonSerializerContext;
