using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.RegularExpressions;
using DuoWall.Core.Engine;
using DuoWall.Core.Models;

namespace DuoWall.Core.Gallery;

/// <summary>
/// Legitimate, modular client for https://ultrawidewallpapers.net.
/// Uses the site's public gallery index, gallery_load.php pagination,
/// public category/resolution landing pages, wallpaper detail pages, and public image endpoints.
/// Respects Club/vault member gates and never bypasses authentication or access controls.
/// </summary>
public sealed partial class UltraWideWallpapersProvider : IWallpaperSourceProvider, IDisposable
{
    public const string BaseUrl = "https://ultrawidewallpapers.net";

    private readonly HttpClient _http;
    private readonly bool _ownsHttp;
    private readonly SemaphoreSlim _thumbThrottle = new(4, 4);
    private readonly SemaphoreSlim _indexLock = new(1, 1);

    private Dictionary<string, List<string>>? _cachedTagIndex;
    private List<string>? _availableTags;
    private List<WallpaperItem>? _popularItemsCache;

    public string ProviderName => "UltraWideWallpapers.net";

    public static readonly IReadOnlyList<string> BuiltInFilterCategories = new[]
    {
        "All",
        "32:9",
        "21:9",
        "16:9",
        "4K",
        "5K",
        "8K",
        "Dual Monitor",
        "Nature",
        "Abstract",
        "Space",
        "Architecture",
        "Technology",
        "OLED",
        "Dark",
        "Cyberpunk",
        "Fantasy",
        "Steampunk",
        "Animals",
        "Vehicles",
        "Retro",
        "Surreal",
        "Pixel Art"
    };

    private static readonly Dictionary<string, string> CategoryToSlugMap = new(StringComparer.OrdinalIgnoreCase)
    {
        ["32:9"] = "32-9-wallpapers",
        ["21:9"] = "21-9-wallpapers",
        ["16:9"] = "16-9-wallpapers",
        ["4K"] = "4k-wallpapers",
        ["5K"] = "5120x1440-wallpapers",
        ["8K"] = "8k-wallpapers",
        ["Dual Monitor"] = "dual-monitor-wallpapers"
    };

    private static readonly Dictionary<string, string> CategoryToTagMap = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Nature"] = "Landscape",
        ["Abstract"] = "Abstract",
        ["Space"] = "Space",
        ["Architecture"] = "Architecture",
        ["Technology"] = "Sci-fi",
        ["OLED"] = "OLED",
        ["Dark"] = "Dark",
        ["Cyberpunk"] = "Cyberpunk",
        ["Fantasy"] = "Fantasy",
        ["Steampunk"] = "Steampunk",
        ["Animals"] = "Animals",
        ["Vehicles"] = "Vehicles",
        ["Retro"] = "Retro",
        ["Surreal"] = "Surreal",
        ["Pixel Art"] = "Pixel Art",
        ["16:9"] = "Crops great @ 16:9"
    };

    public UltraWideWallpapersProvider(HttpClient? httpClient = null)
    {
        if (httpClient != null)
        {
            _http = httpClient;
            _ownsHttp = false;
        }
        else
        {
            var handler = new HttpClientHandler
            {
                AutomaticDecompression = DecompressionMethods.All,
                AllowAutoRedirect = true
            };
            _http = new HttpClient(handler)
            {
                Timeout = TimeSpan.FromSeconds(30)
            };
            _http.DefaultRequestHeaders.UserAgent.ParseAdd(
                "Mozilla/5.0 (Windows NT 10.0; Win64; x64) DuoWall/1.0 (+https://ultrawidewallpapers.net)");
            _ownsHttp = true;
        }
    }

    public async Task<GalleryPageResult> QueryGalleryAsync(
        GalleryQuery query,
        CancellationToken cancellationToken = default)
    {
        await EnsureTagIndexLoadedAsync(cancellationToken).ConfigureAwait(false);

        if (query.SortMode == GallerySortMode.Popular &&
            string.IsNullOrWhiteSpace(query.SearchText) &&
            query.SelectedTags.Count == 0 &&
            (string.IsNullOrWhiteSpace(query.CategoryOrResolutionFilter) ||
             query.CategoryOrResolutionFilter.Equals("All", StringComparison.OrdinalIgnoreCase)))
        {
            var popular = await GetPopularWallpapersAsync(cancellationToken).ConfigureAwait(false);
            var paged = popular.Skip(query.Offset).Take(query.Limit).ToList();
            return new GalleryPageResult
            {
                Items = paged,
                HasMore = query.Offset + paged.Count < popular.Count,
                TotalKnownCount = popular.Count,
                AvailableTags = _availableTags ?? new List<string>()
            };
        }

        if (!string.IsNullOrWhiteSpace(query.CategoryOrResolutionFilter) &&
            CategoryToSlugMap.TryGetValue(query.CategoryOrResolutionFilter, out string? slug) &&
            string.IsNullOrWhiteSpace(query.SearchText) &&
            query.SelectedTags.Count == 0 &&
            query.SortMode == GallerySortMode.Recent)
        {
            var slugItems = await FetchLandingPageAsync(slug, cancellationToken).ConfigureAwait(false);
            if (query.Offset < slugItems.Count)
            {
                var slice = slugItems.Skip(query.Offset).Take(query.Limit).ToList();
                return new GalleryPageResult
                {
                    Items = slice,
                    HasMore = query.Offset + slice.Count < slugItems.Count || query.CategoryOrResolutionFilter == "32:9",
                    TotalKnownCount = _cachedTagIndex?.Count ?? slugItems.Count,
                    AvailableTags = _availableTags ?? new List<string>()
                };
            }
            if (!string.Equals(query.CategoryOrResolutionFilter, "32:9", StringComparison.OrdinalIgnoreCase))
            {
                return new GalleryPageResult
                {
                    Items = Array.Empty<WallpaperItem>(),
                    HasMore = false,
                    TotalKnownCount = slugItems.Count,
                    AvailableTags = _availableTags ?? new List<string>()
                };
            }
        }

        var combinedTags = new List<string>(query.SelectedTags);
        if (!string.IsNullOrWhiteSpace(query.CategoryOrResolutionFilter) &&
            CategoryToTagMap.TryGetValue(query.CategoryOrResolutionFilter, out string? mappedTag) &&
            !combinedTags.Contains(mappedTag, StringComparer.OrdinalIgnoreCase))
        {
            combinedTags.Add(mappedTag);
        }

        string sortParam = query.SortMode switch
        {
            GallerySortMode.Oldest => "old",
            GallerySortMode.Shuffle => "shuffle",
            _ => "recent"
        };

        var urlBuilder = new List<string>
        {
            $"offset={Math.Max(0, query.Offset)}",
            $"limit={Math.Clamp(query.Limit, 1, 60)}",
            $"sort={sortParam}"
        };

        if (combinedTags.Count > 0)
            urlBuilder.Add($"tag={Uri.EscapeDataString(string.Join(",", combinedTags))}");

        if (!string.IsNullOrWhiteSpace(query.SearchText))
            urlBuilder.Add($"search={Uri.EscapeDataString(query.SearchText.Trim())}");

        string endpoint = $"{BaseUrl}/gallery_load.php?{string.Join("&", urlBuilder)}";
        using var req = new HttpRequestMessage(HttpMethod.Get, endpoint);
        req.Headers.Referrer = new Uri($"{BaseUrl}/gallery");

        using var resp = await _http.SendAsync(req, cancellationToken).ConfigureAwait(false);
        resp.EnsureSuccessStatusCode();
        string html = await resp.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

        var items = ParseGalleryTilesHtml(html, _cachedTagIndex);

        return new GalleryPageResult
        {
            Items = items,
            HasMore = items.Count >= query.Limit,
            TotalKnownCount = _cachedTagIndex?.Count ?? items.Count,
            AvailableTags = _availableTags ?? new List<string>()
        };
    }

    public async Task<WallpaperItem> EnrichDetailsAsync(
        WallpaperItem item,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(item.FileName))
            return item;

        string detailUrl = $"{BaseUrl}/wallpaper?image={Uri.EscapeDataString(item.FileName)}";
        using var req = new HttpRequestMessage(HttpMethod.Get, detailUrl);
        req.Headers.Referrer = new Uri($"{BaseUrl}/gallery");

        using var resp = await _http.SendAsync(req, cancellationToken).ConfigureAwait(false);
        if (resp.StatusCode == HttpStatusCode.Unauthorized || resp.StatusCode == HttpStatusCode.Forbidden)
        {
            item.RequiresAuthentication = true;
            item.AccessRestrictionReason = "Authentication required to view this wallpaper on UltraWideWallpapers.net.";
            return item;
        }

        resp.EnsureSuccessStatusCode();
        string html = await resp.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        ParseDetailPageHtml(html, item);
        return item;
    }

    public async Task<DownloadWallpaperResult> DownloadHighResAsync(
        WallpaperItem item,
        string destinationDirectory,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(item.FileName))
        {
            return new DownloadWallpaperResult
            {
                Success = false,
                ErrorMessage = "Missing wallpaper filename."
            };
        }

        if (item.RequiresAuthentication ||
            item.FileName.StartsWith("vault-", StringComparison.OrdinalIgnoreCase))
        {
            return new DownloadWallpaperResult
            {
                Success = false,
                RequiresAuthentication = true,
                AuthActionUrl = string.IsNullOrWhiteSpace(item.SourcePageUrl)
                    ? $"{BaseUrl}/wallpaper?image={Uri.EscapeDataString(item.FileName)}"
                    : item.SourcePageUrl,
                ErrorMessage = "This wallpaper is a Club/Member-only release on UltraWideWallpapers.net. Please sign in on the website to complete an authorized download."
            };
        }

        Directory.CreateDirectory(destinationDirectory);

        string highResUrl = !string.IsNullOrWhiteSpace(item.HighResUrl)
            ? item.HighResUrl
            : $"{BaseUrl}/wallpapers/329/highres/{Uri.EscapeDataString(item.FileName)}";

        string referrer = !string.IsNullOrWhiteSpace(item.SourcePageUrl)
            ? item.SourcePageUrl
            : $"{BaseUrl}/wallpaper?image={Uri.EscapeDataString(item.FileName)}";

        using var req = new HttpRequestMessage(HttpMethod.Get, highResUrl);
        req.Headers.Referrer = new Uri(referrer);
        req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("image/*"));

        using var resp = await _http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false);

        if (resp.StatusCode == HttpStatusCode.Unauthorized || resp.StatusCode == HttpStatusCode.Forbidden)
        {
            return new DownloadWallpaperResult
            {
                Success = false,
                RequiresAuthentication = true,
                AuthActionUrl = referrer,
                ErrorMessage = $"Access denied (HTTP {(int)resp.StatusCode}). This wallpaper may require signing in on UltraWideWallpapers.net."
            };
        }

        if (!resp.IsSuccessStatusCode)
        {
            return new DownloadWallpaperResult
            {
                Success = false,
                ErrorMessage = $"Download failed with HTTP {(int)resp.StatusCode} ({resp.ReasonPhrase})."
            };
        }

        string? contentType = resp.Content.Headers.ContentType?.MediaType;
        if (!string.IsNullOrWhiteSpace(contentType) &&
            !contentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
        {
            return new DownloadWallpaperResult
            {
                Success = false,
                RequiresAuthentication = true,
                AuthActionUrl = referrer,
                ErrorMessage = $"Expected an image response but received '{contentType}'. Website authentication or verification may be required."
            };
        }

        string safeName = Path.GetFileName(item.FileName);
        string targetPath = Path.Combine(destinationDirectory, safeName);
        string tempPath = targetPath + ".downloading";

        try
        {
            long? totalBytes = resp.Content.Headers.ContentLength;
            await using (var remoteStream = await resp.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false))
            await using (var fileStream = File.Create(tempPath))
            {
                byte[] buffer = new byte[81920];
                long downloaded = 0;
                int read;
                while ((read = await remoteStream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
                {
                    await fileStream.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
                    downloaded += read;
                    if (totalBytes.HasValue && totalBytes.Value > 0)
                        progress?.Report((double)downloaded / totalBytes.Value);
                }
            }

            if (File.Exists(targetPath))
                File.Delete(targetPath);
            File.Move(tempPath, targetPath);

            var (w, h) = SkiaWallpaperCompositor.ReadImageDimensions(targetPath);
            long size = new FileInfo(targetPath).Length;

            item.LocalOriginalPath = targetPath;
            item.Width = w;
            item.Height = h;
            item.FileSizeBytes = size;
            item.DownloadedUtc = DateTime.UtcNow;

            return new DownloadWallpaperResult
            {
                Success = true,
                LocalFilePath = targetPath,
                Width = w,
                Height = h,
                FileSizeBytes = size
            };
        }
        catch (Exception ex)
        {
            if (File.Exists(tempPath))
            {
                try { File.Delete(tempPath); } catch { }
            }
            return new DownloadWallpaperResult
            {
                Success = false,
                ErrorMessage = $"Failed saving wallpaper: {ex.Message}"
            };
        }
    }

    public async Task<string?> GetOrCacheThumbnailAsync(
        WallpaperItem item,
        string cacheDirectory,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(item.FileName) || string.IsNullOrWhiteSpace(item.ThumbnailUrl))
            return null;

        Directory.CreateDirectory(cacheDirectory);
        string safeName = Path.GetFileName(item.FileName);
        string localPath = Path.Combine(cacheDirectory, safeName);

        if (File.Exists(localPath) && new FileInfo(localPath).Length > 256)
        {
            item.LocalThumbnailPath = localPath;
            return localPath;
        }

        await _thumbThrottle.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (File.Exists(localPath) && new FileInfo(localPath).Length > 256)
            {
                item.LocalThumbnailPath = localPath;
                return localPath;
            }

            using var req = new HttpRequestMessage(HttpMethod.Get, item.ThumbnailUrl);
            req.Headers.Referrer = new Uri($"{BaseUrl}/gallery");
            using var resp = await _http.SendAsync(req, cancellationToken).ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode)
                return null;

            string? mediaType = resp.Content.Headers.ContentType?.MediaType;
            if (!string.IsNullOrWhiteSpace(mediaType) &&
                !mediaType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
                return null;

            byte[] data = await resp.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
            if (data.Length < 256)
                return null;

            string tempPath = localPath + ".tmp";
            await File.WriteAllBytesAsync(tempPath, data, cancellationToken).ConfigureAwait(false);
            File.Move(tempPath, localPath, overwrite: true);

            item.LocalThumbnailPath = localPath;
            return localPath;
        }
        catch
        {
            return null;
        }
        finally
        {
            _thumbThrottle.Release();
        }
    }

    private async Task EnsureTagIndexLoadedAsync(CancellationToken cancellationToken)
    {
        if (_cachedTagIndex != null)
            return;

        await _indexLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_cachedTagIndex != null)
                return;

            using var req = new HttpRequestMessage(HttpMethod.Get, $"{BaseUrl}/gallery");
            using var resp = await _http.SendAsync(req, cancellationToken).ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode)
                return;

            string html = await resp.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            var (index, tags) = ParseImageTagsFromGalleryHtml(html);
            _cachedTagIndex = index;
            _availableTags = tags;
        }
        catch
        {
            _cachedTagIndex = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            _availableTags = new List<string>();
        }
        finally
        {
            _indexLock.Release();
        }
    }

    private async Task<List<WallpaperItem>> GetPopularWallpapersAsync(CancellationToken cancellationToken)
    {
        if (_popularItemsCache != null && _popularItemsCache.Count > 0)
            return _popularItemsCache;

        var results = new List<WallpaperItem>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (string page in new[] { "/wallpaperstats", "/" })
        {
            try
            {
                using var resp = await _http.GetAsync($"{BaseUrl}{page}", cancellationToken).ConfigureAwait(false);
                if (!resp.IsSuccessStatusCode) continue;
                string html = await resp.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                foreach (var item in ParseLinksAndGalleryItems(html, _cachedTagIndex))
                {
                    if (seen.Add(item.FileName))
                        results.Add(item);
                }
            }
            catch { }
        }

        _popularItemsCache = results;
        return results;
    }

    private async Task<List<WallpaperItem>> FetchLandingPageAsync(string slug, CancellationToken cancellationToken)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, $"{BaseUrl}/{slug}");
        req.Headers.Referrer = new Uri($"{BaseUrl}/gallery");
        using var resp = await _http.SendAsync(req, cancellationToken).ConfigureAwait(false);
        resp.EnsureSuccessStatusCode();
        string html = await resp.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        var items = ParseGalleryTilesHtml(html, _cachedTagIndex);
        return items.Count > 0 ? items : ParseLinksAndGalleryItems(html, _cachedTagIndex);
    }

    public static (Dictionary<string, List<string>> Index, List<string> SortedTags) ParseImageTagsFromGalleryHtml(string html)
    {
        var index = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        var tagFreq = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        var match = ImageTagsRegex().Match(html);
        if (match.Success)
        {
            try
            {
                var parsed = JsonSerializer.Deserialize<Dictionary<string, List<string>>>(match.Groups[1].Value);
                if (parsed != null)
                {
                    foreach (var kv in parsed)
                    {
                        index[kv.Key] = kv.Value;
                        foreach (var t in kv.Value)
                        {
                            tagFreq[t] = tagFreq.TryGetValue(t, out int c) ? c + 1 : 1;
                        }
                    }
                }
            }
            catch { }
        }

        var sortedTags = tagFreq
            .OrderByDescending(kv => kv.Value)
            .ThenBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase)
            .Select(kv => kv.Key)
            .ToList();

        return (index, sortedTags);
    }

    public static List<WallpaperItem> ParseGalleryTilesHtml(
        string html,
        IReadOnlyDictionary<string, List<string>>? tagIndex = null)
    {
        var list = new List<WallpaperItem>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (Match m in GalleryItemRegex().Matches(html))
        {
            string fn = WebUtility.HtmlDecode(m.Groups[1].Value).Trim();
            if (string.IsNullOrWhiteSpace(fn) || !seen.Add(fn))
                continue;

            string block = m.Value;
            string alt = string.Empty;
            var altMatch = ImgAltRegex().Match(block);
            if (altMatch.Success)
                alt = WebUtility.HtmlDecode(altMatch.Groups[1].Value).Trim();

            list.Add(BuildItemFromFilename(fn, alt, tagIndex));
        }

        return list;
    }

    public static List<WallpaperItem> ParseLinksAndGalleryItems(
        string html,
        IReadOnlyDictionary<string, List<string>>? tagIndex = null)
    {
        var list = new List<WallpaperItem>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var item in ParseGalleryTilesHtml(html, tagIndex))
        {
            if (seen.Add(item.FileName))
                list.Add(item);
        }

        foreach (Match m in WallpaperLinkRegex().Matches(html))
        {
            string fn = Uri.UnescapeDataString(m.Groups[1].Value).Trim();
            if (string.IsNullOrWhiteSpace(fn) || !seen.Add(fn))
                continue;

            list.Add(BuildItemFromFilename(fn, string.Empty, tagIndex));
        }

        return list;
    }

    public static void ParseDetailPageHtml(string html, WallpaperItem item)
    {
        var h1Match = H1Regex().Match(html);
        if (h1Match.Success)
        {
            string h1 = WebUtility.HtmlDecode(StripHtml(h1Match.Groups[1].Value)).Trim();
            if (!string.IsNullOrWhiteSpace(h1))
                item.Title = h1;
        }

        var descMatch = WpDescRegex().Match(html);
        if (descMatch.Success)
        {
            item.Description = WebUtility.HtmlDecode(StripHtml(descMatch.Groups[1].Value)).Trim();
        }

        var tags = new List<string>();
        foreach (Match tm in WpTagsAnchorRegex().Matches(html))
        {
            string t = WebUtility.HtmlDecode(StripHtml(tm.Groups[1].Value)).Trim();
            if (!string.IsNullOrWhiteSpace(t) && !tags.Contains(t, StringComparer.OrdinalIgnoreCase))
                tags.Add(t);
        }
        if (tags.Count > 0)
            item.Tags = tags;

        var dimMatch = ImgWidthHeightRegex().Match(html);
        if (dimMatch.Success &&
            int.TryParse(dimMatch.Groups[1].Value, out int pw) &&
            int.TryParse(dimMatch.Groups[2].Value, out int ph) &&
            pw > 0 && ph > 0)
        {
            if (!item.IsDownloaded)
            {
                double ratio = (double)pw / ph;
                if (Math.Abs(ratio - 32.0 / 9.0) < 0.03)
                {
                    item.Width = 7680;
                    item.Height = 2160;
                }
                else if (Math.Abs(ratio - 32.0 / 9.0) < 0.18)
                {
                    item.Width = 7680;
                    item.Height = (int)Math.Round(7680 / ratio);
                }
                else
                {
                    item.Width = pw;
                    item.Height = ph;
                }
            }
        }

        var jsonLdMatch = ContentUrlRegex().Match(html);
        if (jsonLdMatch.Success)
        {
            item.HighResUrl = jsonLdMatch.Groups[1].Value.Replace("\\/", "/");
        }

        if (html.Contains("Club drop", StringComparison.OrdinalIgnoreCase) ||
            html.Contains("vault_download.php", StringComparison.OrdinalIgnoreCase))
        {
            item.RequiresAuthentication = true;
            item.AccessRestrictionReason = "Club/Member wallpaper — sign in on UltraWideWallpapers.net to download.";
        }
    }

    private static WallpaperItem BuildItemFromFilename(
        string fn,
        string altDescription,
        IReadOnlyDictionary<string, List<string>>? tagIndex)
    {
        bool isVault = fn.StartsWith("vault-", StringComparison.OrdinalIgnoreCase);
        string numPart = FilenameNumRegex().Match(fn) is { Success: true } nm
            ? nm.Groups[1].Value
            : Path.GetFileNameWithoutExtension(fn);

        int.TryParse(numPart, out int seq);

        string title = !string.IsNullOrWhiteSpace(altDescription)
            ? BuildShortTitleFromAlt(altDescription, numPart)
            : $"Ultrawide Wallpaper #{numPart}";

        var tags = tagIndex != null && tagIndex.TryGetValue(fn, out var foundTags)
            ? new List<string>(foundTags)
            : new List<string>();

        string encFn = Uri.EscapeDataString(fn);
        return new WallpaperItem
        {
            Id = fn,
            FileName = fn,
            Title = title,
            Description = altDescription,
            SourceProvider = "UltraWideWallpapers.net",
            SourcePageUrl = $"{BaseUrl}/wallpaper?image={encFn}",
            ThumbnailUrl = isVault
                ? $"{BaseUrl}/vault_image.php?image={encFn}&width=400"
                : $"{BaseUrl}/resizecachethumbs.php?image={encFn}&width=400",
            PreviewUrl = isVault
                ? $"{BaseUrl}/vault_image.php?image={encFn}&width=1700"
                : $"{BaseUrl}/resizecache.php?image={encFn}&width=1700",
            HighResUrl = isVault ? string.Empty : $"{BaseUrl}/wallpapers/329/highres/{encFn}",
            Tags = tags,
            RequiresAuthentication = isVault,
            AccessRestrictionReason = isVault
                ? "Club / Member-only release on UltraWideWallpapers.net."
                : null,
            SequenceNumber = seq,
            Width = 7680,
            Height = 2160
        };
    }

    private static string BuildShortTitleFromAlt(string alt, string numPart)
    {
        var parts = alt.TrimStart('#', ' ').Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0)
            return $"Ultrawide Wallpaper #{numPart}";

        string joined = string.Join(", ", parts.Take(2));
        if (joined.Length > 48)
            joined = joined[..45].TrimEnd() + "...";
        return char.ToUpperInvariant(joined[0]) + joined[1..];
    }

    private static string StripHtml(string input) => HtmlTagRegex().Replace(input, string.Empty);

    public void Dispose()
    {
        if (_ownsHttp)
            _http.Dispose();
        _thumbThrottle.Dispose();
        _indexLock.Dispose();
    }

    [GeneratedRegex(@"const\s+imageTags\s*=\s*(\{.*?\});", RegexOptions.Singleline)]
    private static partial Regex ImageTagsRegex();

    [GeneratedRegex(@"<div[^>]*class=""gallery-item""[^>]*data-filename=""([^""]+)""[^>]*>.*?</div>", RegexOptions.Singleline | RegexOptions.IgnoreCase)]
    private static partial Regex GalleryItemRegex();

    [GeneratedRegex(@"alt=""([^""]*)""", RegexOptions.IgnoreCase)]
    private static partial Regex ImgAltRegex();

    [GeneratedRegex(@"wallpaper\?image=([^""'&\s>]+)", RegexOptions.IgnoreCase)]
    private static partial Regex WallpaperLinkRegex();

    [GeneratedRegex(@"<h1[^>]*>(.*?)</h1>", RegexOptions.Singleline | RegexOptions.IgnoreCase)]
    private static partial Regex H1Regex();

    [GeneratedRegex(@"<p[^>]*class=""wp-desc""[^>]*>(.*?)</p>", RegexOptions.Singleline | RegexOptions.IgnoreCase)]
    private static partial Regex WpDescRegex();

    [GeneratedRegex(@"<a[^>]*href=""gallery\?tags=[^""]*""[^>]*>(.*?)</a>", RegexOptions.Singleline | RegexOptions.IgnoreCase)]
    private static partial Regex WpTagsAnchorRegex();

    [GeneratedRegex(@"resizecache\.php\?image=[^>]*width=""(\d+)""\s+height=""(\d+)""", RegexOptions.IgnoreCase)]
    private static partial Regex ImgWidthHeightRegex();

    [GeneratedRegex(@"""contentUrl""\s*:\s*""([^""]+)""", RegexOptions.IgnoreCase)]
    private static partial Regex ContentUrlRegex();

    [GeneratedRegex(@"(\d+)")]
    private static partial Regex FilenameNumRegex();

    [GeneratedRegex(@"<[^>]+>")]
    private static partial Regex HtmlTagRegex();
}
