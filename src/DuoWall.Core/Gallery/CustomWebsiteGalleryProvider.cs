using System.Net;
using System.Net.Http.Headers;
using System.Text.RegularExpressions;
using DuoWall.Core.Engine;
using DuoWall.Core.Models;
using DuoWall.Core.Services;

namespace DuoWall.Core.Gallery;

/// <summary>
/// Fetches and browses wallpapers from a user-linked external wallpaper website URL.
/// Extracts high-resolution image links (.jpg, .png, .webp) and OpenGraph/gallery thumbnails
/// using standard HTTP GET requests without bypassing authentication or anti-bot checks.
/// </summary>
public sealed partial class CustomWebsiteGalleryProvider : IWallpaperSourceProvider, IDisposable
{
    private readonly HttpClient _http;
    private readonly bool _ownsHttp;
    private readonly SemaphoreSlim _thumbThrottle = new(4, 4);
    private string _customSiteUrl = string.Empty;
    private List<WallpaperItem>? _cachedItems;
    private string _cachedForUrl = string.Empty;

    public string ProviderName => "Custom Website";

    public string CustomSiteUrl
    {
        get => _customSiteUrl;
        set
        {
            string normalized = NormalizeUrl(value);
            if (!string.Equals(_customSiteUrl, normalized, StringComparison.OrdinalIgnoreCase))
            {
                _customSiteUrl = normalized;
                _cachedItems = null;
            }
        }
    }

    public CustomWebsiteGalleryProvider(HttpClient? httpClient = null)
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
                Timeout = TimeSpan.FromSeconds(20)
            };
            _http.DefaultRequestHeaders.UserAgent.Clear();
            _http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("DuoWall", "2.0"));
            _ownsHttp = true;
        }
    }

    public static string NormalizeUrl(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return string.Empty;

        string trimmed = raw.Trim();
        if (!trimmed.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
            !trimmed.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            trimmed = "https://" + trimmed;
        }

        if (Uri.TryCreate(trimmed, UriKind.Absolute, out var uri) &&
            (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
        {
            return uri.AbsoluteUri;
        }

        return string.Empty;
    }

    public async Task<GalleryPageResult> QueryGalleryAsync(GalleryQuery query, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_customSiteUrl) || !Uri.TryCreate(_customSiteUrl, UriKind.Absolute, out var baseUri))
        {
            return new GalleryPageResult
            {
                StatusMessage = "Enter a valid website URL (e.g. https://ultrawidewallpapers.net) and click Load Site."
            };
        }

        try
        {
            if (_cachedItems == null || !string.Equals(_cachedForUrl, _customSiteUrl, StringComparison.OrdinalIgnoreCase))
            {
                string targetUrl = string.IsNullOrWhiteSpace(query.SearchText)
                    ? baseUri.AbsoluteUri
                    : baseUri.AbsoluteUri;

                using var response = await _http.GetAsync(targetUrl, cancellationToken);
                if (!response.IsSuccessStatusCode)
                {
                    return new GalleryPageResult
                    {
                        StatusMessage = $"Site returned HTTP {(int)response.StatusCode} ({response.ReasonPhrase})."
                    };
                }

                string html = await response.Content.ReadAsStringAsync(cancellationToken);
                _cachedItems = ExtractWallpapersFromHtml(html, baseUri);
                _cachedForUrl = _customSiteUrl;
            }

            IEnumerable<WallpaperItem> filtered = _cachedItems;
            if (!string.IsNullOrWhiteSpace(query.SearchText))
            {
                string term = query.SearchText.Trim();
                filtered = filtered.Where(i =>
                    i.Title.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                    i.FileName.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                    i.Tags.Any(t => t.Contains(term, StringComparison.OrdinalIgnoreCase)));
            }

            var ordered = filtered.ToList();
            int skip = Math.Max(0, query.Offset);
            int take = Math.Max(1, query.Limit);
            var pageItems = ordered.Skip(skip).Take(take).ToList();

            return new GalleryPageResult
            {
                Items = pageItems,
                HasMore = skip + pageItems.Count < ordered.Count,
                TotalKnownCount = ordered.Count,
                AvailableTags = new[] { "All", "Custom Site", "32:9", "21:9", "4K" },
                StatusMessage = ordered.Count == 0
                    ? $"No public direct image URLs were found on {baseUri.Host}. Try linking an image gallery or RSS page."
                    : $"Loaded {ordered.Count} wallpapers from {baseUri.Host}."
            };
        }
        catch (Exception ex)
        {
            return new GalleryPageResult
            {
                StatusMessage = $"Could not load custom site: {ex.Message}"
            };
        }
    }

    public static List<WallpaperItem> ExtractWallpapersFromHtml(string html, Uri baseUri)
    {
        var results = new List<WallpaperItem>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void TryAddImage(string rawUrl, string? titleHint)
        {
            if (string.IsNullOrWhiteSpace(rawUrl) || rawUrl.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
                return;

            string decoded = WebUtility.HtmlDecode(rawUrl.Trim());
            if (!Uri.TryCreate(baseUri, decoded, out var resolved))
                return;
            if (resolved.Scheme != Uri.UriSchemeHttp && resolved.Scheme != Uri.UriSchemeHttps)
                return;

            string abs = resolved.AbsoluteUri;
            string lower = abs.ToLowerInvariant();
            if (lower.Contains("favicon") || lower.Contains("/logo") || lower.Contains("sprite") ||
                lower.Contains("avatar") || lower.EndsWith(".svg") || lower.EndsWith(".ico") ||
                lower.EndsWith(".gif"))
            {
                return;
            }

            bool looksLikeImage = lower.Contains(".jpg") || lower.Contains(".jpeg") ||
                                  lower.Contains(".png") || lower.Contains(".webp");
            if (!looksLikeImage)
                return;

            if (!seen.Add(abs))
                return;

            string fileName = Path.GetFileName(resolved.LocalPath);
            if (string.IsNullOrWhiteSpace(fileName))
                fileName = $"wallpaper_{results.Count + 1}.jpg";

            string title = !string.IsNullOrWhiteSpace(titleHint)
                ? WebUtility.HtmlDecode(titleHint.Trim())
                : Path.GetFileNameWithoutExtension(fileName).Replace('-', ' ').Replace('_', ' ');
            if (string.IsNullOrWhiteSpace(title))
                title = $"Wallpaper {results.Count + 1}";

            results.Add(new WallpaperItem
            {
                Id = $"site:{baseUri.Host}:{abs.GetHashCode():X8}:{fileName}",
                FileName = fileName,
                Title = title,
                Description = $"From {baseUri.Host}",
                SourceProvider = $"Custom Site ({baseUri.Host})",
                SourcePageUrl = baseUri.AbsoluteUri,
                ThumbnailUrl = abs,
                PreviewUrl = abs,
                HighResUrl = abs,
                Tags = new List<string> { "Custom Site", baseUri.Host }
            });
        }

        // 1. Direct <a href="...jpg|png|webp"> links (often full-res targets)
        foreach (Match m in AnchorImageRegex().Matches(html))
        {
            TryAddImage(m.Groups["url"].Value, m.Groups["text"].Value);
        }

        // 2. <img src / data-src / data-original / alt>
        foreach (Match m in ImgTagRegex().Matches(html))
        {
            string tag = m.Value;
            string alt = AltRegex().Match(tag).Groups["val"].Value;
            string dataSrc = DataSrcRegex().Match(tag).Groups["val"].Value;
            string src = SrcRegex().Match(tag).Groups["val"].Value;
            if (!string.IsNullOrWhiteSpace(dataSrc))
                TryAddImage(dataSrc, alt);
            if (!string.IsNullOrWhiteSpace(src))
                TryAddImage(src, alt);
        }

        // 3. Direct image URLs in JSON/CSS/scripts inside page
        foreach (Match m in RawImageUrlRegex().Matches(html))
        {
            TryAddImage(m.Groups["url"].Value, null);
        }

        return results;
    }

    public Task<WallpaperItem> EnrichDetailsAsync(WallpaperItem item, CancellationToken cancellationToken = default)
        => Task.FromResult(item);

    public async Task<DownloadWallpaperResult> DownloadHighResAsync(
        WallpaperItem item,
        string destinationDirectory,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(destinationDirectory);
        string url = !string.IsNullOrWhiteSpace(item.HighResUrl) ? item.HighResUrl : item.PreviewUrl;
        if (string.IsNullOrWhiteSpace(url))
        {
            return new DownloadWallpaperResult { Success = false, ErrorMessage = "No downloadable image URL found." };
        }

        try
        {
            string cleanName = YouTubeWallpaperService.SanitizeName(Path.GetFileNameWithoutExtension(item.FileName));
            string ext = Path.GetExtension(item.FileName);
            if (string.IsNullOrWhiteSpace(ext) || ext.Length > 5) ext = ".jpg";
            string destPath = Path.Combine(destinationDirectory, $"{cleanName}_{Math.Abs(url.GetHashCode()):X6}{ext}");

            byte[] bytes = await _http.GetByteArrayAsync(url, cancellationToken);
            if (bytes.Length < 1024)
            {
                return new DownloadWallpaperResult { Success = false, ErrorMessage = "Remote file was empty or blocked." };
            }

            await File.WriteAllBytesAsync(destPath, bytes, cancellationToken);
            progress?.Report(1.0);

            var (w, h) = SkiaWallpaperCompositor.ReadImageDimensions(destPath);
            return new DownloadWallpaperResult
            {
                Success = true,
                LocalFilePath = destPath,
                Width = w,
                Height = h,
                FileSizeBytes = bytes.Length
            };
        }
        catch (Exception ex)
        {
            return new DownloadWallpaperResult
            {
                Success = false,
                ErrorMessage = $"Download failed: {ex.Message}"
            };
        }
    }

    public async Task<string?> GetOrCacheThumbnailAsync(
        WallpaperItem item,
        string cacheDirectory,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(item.ThumbnailUrl))
            return null;

        Directory.CreateDirectory(cacheDirectory);
        string cachePath = Path.Combine(cacheDirectory, $"site_{Math.Abs(item.ThumbnailUrl.GetHashCode()):X8}.jpg");
        if (File.Exists(cachePath) && new FileInfo(cachePath).Length > 512)
            return cachePath;

        await _thumbThrottle.WaitAsync(cancellationToken);
        try
        {
            if (File.Exists(cachePath) && new FileInfo(cachePath).Length > 512)
                return cachePath;

            byte[] bytes = await _http.GetByteArrayAsync(item.ThumbnailUrl, cancellationToken);
            if (bytes.Length < 512) return null;
            await File.WriteAllBytesAsync(cachePath, bytes, cancellationToken);
            return cachePath;
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

    [GeneratedRegex("""<a\b[^>]*href\s*=\s*["'](?<url>[^"']+\.(?:jpg|jpeg|png|webp)(?:\?[^"']*)?)["'][^>]*>(?<text>[^<]*)</a>""", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex AnchorImageRegex();

    [GeneratedRegex("""<img\b[^>]*>""", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex ImgTagRegex();

    [GeneratedRegex("""alt\s*=\s*["'](?<val>[^"']*)["']""", RegexOptions.IgnoreCase)]
    private static partial Regex AltRegex();

    [GeneratedRegex("""(?:data-src|data-original|data-lazy-src)\s*=\s*["'](?<val>[^"']+)["']""", RegexOptions.IgnoreCase)]
    private static partial Regex DataSrcRegex();

    [GeneratedRegex("""\bsrc\s*=\s*["'](?<val>[^"']+)["']""", RegexOptions.IgnoreCase)]
    private static partial Regex SrcRegex();

    [GeneratedRegex("""(?<url>https?://[^\s"'<>()]+\.(?:jpg|jpeg|png|webp))""", RegexOptions.IgnoreCase)]
    private static partial Regex RawImageUrlRegex();

    public void Dispose()
    {
        if (_ownsHttp)
            _http.Dispose();
    }
}

