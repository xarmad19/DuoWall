using System.Net.Http;
using System.Text.Json;

namespace DuoWall.Core.Services;

public sealed class ReleaseCheckResult
{
    public bool Success { get; init; }
    public bool UpdateAvailable { get; init; }
    public string CurrentVersion { get; init; } = "2.0.0";
    public string LatestVersion { get; init; } = "2.0.0";
    public string? ReleasePageUrl { get; init; }
    public string? ReleaseNotesSummary { get; init; }
    public string Message { get; init; } = string.Empty;
}

/// <summary>
/// Checks official GitHub Releases for newer DuoWall versions.
/// Never automatically downloads or executes remote binaries; notifies the user
/// with the official release URL and SHA-256 verification guidance.
/// </summary>
public sealed class GitHubReleaseService : IDisposable
{
    public const string DefaultRepoSlug = "xarmad19/DuoWall";
    private readonly HttpClient _http;
    private readonly bool _ownsHttp;

    public GitHubReleaseService(HttpClient? httpClient = null)
    {
        if (httpClient != null)
        {
            _http = httpClient;
            _ownsHttp = false;
        }
        else
        {
            _http = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
            _http.DefaultRequestHeaders.UserAgent.ParseAdd("DuoWall-Updater/2.0");
            _http.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
            _ownsHttp = true;
        }
    }

    public async Task<ReleaseCheckResult> CheckLatestReleaseAsync(
        string currentVersion = "2.0.0",
        string? repoSlug = null,
        CancellationToken cancellationToken = default)
    {
        string slug = string.IsNullOrWhiteSpace(repoSlug) ? DefaultRepoSlug : repoSlug.Trim();
        string apiUrl = $"https://api.github.com/repos/{slug}/releases/latest";

        try
        {
            using var resp = await _http.GetAsync(apiUrl, cancellationToken);
            if (resp.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                return new ReleaseCheckResult
                {
                    Success = true,
                    UpdateAvailable = false,
                    CurrentVersion = currentVersion,
                    LatestVersion = currentVersion,
                    ReleasePageUrl = $"https://github.com/{slug}/releases",
                    Message = $"DuoWall v{currentVersion} is running. No published GitHub releases were found yet at github.com/{slug}/releases."
                };
            }

            resp.EnsureSuccessStatusCode();
            string json = await resp.Content.ReadAsStringAsync(cancellationToken);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            string tag = (root.TryGetProperty("tag_name", out var tEl) ? tEl.GetString() : null) ?? currentVersion;
            string cleanLatest = tag.Trim().TrimStart('v', 'V');
            string htmlUrl = (root.TryGetProperty("html_url", out var uEl) ? uEl.GetString() : null)
                             ?? $"https://github.com/{slug}/releases";
            string body = (root.TryGetProperty("body", out var bEl) ? bEl.GetString() : null) ?? string.Empty;

            bool hasNewer = Version.TryParse(cleanLatest, out var latestVer) &&
                            Version.TryParse(currentVersion.TrimStart('v', 'V'), out var curVer) &&
                            latestVer > curVer;

            return new ReleaseCheckResult
            {
                Success = true,
                UpdateAvailable = hasNewer,
                CurrentVersion = currentVersion,
                LatestVersion = cleanLatest,
                ReleasePageUrl = htmlUrl,
                ReleaseNotesSummary = body.Length > 260 ? body[..260] + "..." : body,
                Message = hasNewer
                    ? $"New release v{cleanLatest} is available on GitHub Releases! Click Open Release Page to download and verify SHA256SUMS.txt."
                    : $"DuoWall v{currentVersion} is up to date (latest release: v{cleanLatest})."
            };
        }
        catch (Exception ex)
        {
            return new ReleaseCheckResult
            {
                Success = false,
                CurrentVersion = currentVersion,
                LatestVersion = currentVersion,
                ReleasePageUrl = $"https://github.com/{slug}/releases",
                Message = $"Could not check GitHub Releases: {ex.Message}"
            };
        }
    }

    public void Dispose()
    {
        if (_ownsHttp) _http.Dispose();
    }
}

