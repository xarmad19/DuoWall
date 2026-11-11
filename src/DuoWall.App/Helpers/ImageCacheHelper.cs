using System.Collections.Concurrent;
using System.IO;
using System.Net.Http;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace DuoWall.App.Helpers;

/// <summary>
/// Thread-safe, non-blocking BitmapSource loader for local files and cached remote previews.
/// </summary>
public static class ImageCacheHelper
{
    private static readonly ConcurrentDictionary<string, BitmapSource> MemoryCache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly HttpClient SharedHttp = CreateHttp();

    private static HttpClient CreateHttp()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(25) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) DuoWall/1.0");
        return client;
    }

    public static async Task<BitmapSource?> LoadBitmapAsync(
        string pathOrUrl,
        int decodePixelWidth = 0,
        string? referrer = null,
        string? diskCacheDirectory = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(pathOrUrl))
            return null;

        string cacheKey = $"{pathOrUrl}|{decodePixelWidth}";
        if (MemoryCache.TryGetValue(cacheKey, out var cached))
            return cached;

        try
        {
            BitmapSource? bmp;
            if (pathOrUrl.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                pathOrUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                byte[]? bytes = null;
                string? diskPath = null;

                if (!string.IsNullOrWhiteSpace(diskCacheDirectory))
                {
                    Directory.CreateDirectory(diskCacheDirectory);
                    string safe = string.Concat(pathOrUrl.Select(c => char.IsLetterOrDigit(c) ? c : '_'));
                    if (safe.Length > 80) safe = safe[^80..];
                    diskPath = Path.Combine(diskCacheDirectory, safe + ".cache");
                    if (File.Exists(diskPath) && new FileInfo(diskPath).Length > 256)
                    {
                        bytes = await File.ReadAllBytesAsync(diskPath, cancellationToken).ConfigureAwait(false);
                    }
                }

                if (bytes == null)
                {
                    using var req = new HttpRequestMessage(HttpMethod.Get, pathOrUrl);
                    req.Headers.Referrer = new Uri(referrer ?? "https://ultrawidewallpapers.net/gallery");
                    using var resp = await SharedHttp.SendAsync(req, cancellationToken).ConfigureAwait(false);
                    if (!resp.IsSuccessStatusCode)
                        return null;
                    string? mt = resp.Content.Headers.ContentType?.MediaType;
                    if (!string.IsNullOrWhiteSpace(mt) && !mt.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
                        return null;

                    bytes = await resp.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
                    if (bytes.Length < 256)
                        return null;

                    if (!string.IsNullOrWhiteSpace(diskPath))
                    {
                        try { await File.WriteAllBytesAsync(diskPath, bytes, cancellationToken).ConfigureAwait(false); }
                        catch { }
                    }
                }

                bmp = await Task.Run(() => DecodeFromBytes(bytes, decodePixelWidth), cancellationToken).ConfigureAwait(false);
            }
            else
            {
                if (!File.Exists(pathOrUrl))
                    return null;
                bmp = await Task.Run(() => DecodeFromFile(pathOrUrl, decodePixelWidth), cancellationToken).ConfigureAwait(false);
            }

            if (bmp != null)
            {
                if (MemoryCache.Count > 120)
                    MemoryCache.Clear();
                MemoryCache[cacheKey] = bmp;
            }
            return bmp;
        }
        catch
        {
            return null;
        }
    }

    public static void ClearMemoryCache() => MemoryCache.Clear();

    private static BitmapSource DecodeFromFile(string path, int decodePixelWidth)
    {
        var bi = new BitmapImage();
        bi.BeginInit();
        bi.CacheOption = BitmapCacheOption.OnLoad;
        bi.UriSource = new Uri(path, UriKind.Absolute);
        if (decodePixelWidth > 0)
            bi.DecodePixelWidth = decodePixelWidth;
        bi.EndInit();
        bi.Freeze();
        return bi;
    }

    private static BitmapSource DecodeFromBytes(byte[] bytes, int decodePixelWidth)
    {
        using var ms = new MemoryStream(bytes);
        var bi = new BitmapImage();
        bi.BeginInit();
        bi.CacheOption = BitmapCacheOption.OnLoad;
        bi.StreamSource = ms;
        if (decodePixelWidth > 0)
            bi.DecodePixelWidth = decodePixelWidth;
        bi.EndInit();
        bi.Freeze();
        return bi;
    }
}
