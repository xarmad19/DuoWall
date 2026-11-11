using DuoWall.Core.Engine;
using DuoWall.Core.Gallery;
using DuoWall.Core.Models;
using DuoWall.Core.Storage;

namespace DuoWall.Core.Services;

/// <summary>
/// Coordinates local wallpaper library management, online downloading,
/// local image imports, thumbnail cache cleanup, and placement profile keys.
/// </summary>
public sealed class WallpaperLibraryService
{
    private readonly DuoWallDatabase _db;
    private readonly IWallpaperSourceProvider _provider;

    public WallpaperLibraryService(DuoWallDatabase db, IWallpaperSourceProvider provider)
    {
        _db = db;
        _provider = provider;
    }

    public async Task<WallpaperItem> ImportLocalImageAsync(string sourceFilePath, string downloadDirectory)
    {
        if (!File.Exists(sourceFilePath))
            throw new FileNotFoundException("Local image file not found.", sourceFilePath);

        var (w, h) = SkiaWallpaperCompositor.ReadImageDimensions(sourceFilePath);
        long size = new FileInfo(sourceFilePath).Length;

        Directory.CreateDirectory(downloadDirectory);
        string destPath = sourceFilePath;
        if (!Path.GetFullPath(sourceFilePath).StartsWith(Path.GetFullPath(downloadDirectory), StringComparison.OrdinalIgnoreCase))
        {
            string fileName = Path.GetFileName(sourceFilePath);
            destPath = Path.Combine(downloadDirectory, fileName);
            if (!File.Exists(destPath))
                await Task.Run(() => File.Copy(sourceFilePath, destPath, overwrite: false));
        }

        string id = $"local:{Path.GetFileName(destPath)}:{size}";
        var item = new WallpaperItem
        {
            Id = id,
            FileName = Path.GetFileName(destPath),
            Title = Path.GetFileNameWithoutExtension(destPath).Replace('-', ' ').Replace('_', ' '),
            Description = $"Local image ({w} × {h})",
            SourceProvider = "Local File",
            Width = w,
            Height = h,
            FileSizeBytes = size,
            LocalOriginalPath = destPath,
            DownloadedUtc = DateTime.UtcNow,
            Tags = new List<string> { "Local", MonitorInfo.FormatAspectRatio(w, h) }
        };

        _db.UpsertWallpaper(item);
        return item;
    }

    public async Task<DownloadWallpaperResult> EnsureLocalOriginalAsync(
        WallpaperItem item,
        string downloadDirectory,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (item.IsDownloaded && !string.IsNullOrWhiteSpace(item.LocalOriginalPath))
        {
            var (w, h) = SkiaWallpaperCompositor.ReadImageDimensions(item.LocalOriginalPath);
            item.Width = w;
            item.Height = h;
            item.FileSizeBytes = new FileInfo(item.LocalOriginalPath).Length;
            _db.UpsertWallpaper(item);

            return new DownloadWallpaperResult
            {
                Success = true,
                LocalFilePath = item.LocalOriginalPath,
                Width = w,
                Height = h,
                FileSizeBytes = item.FileSizeBytes
            };
        }

        // Check if file already exists in download folder from earlier run
        string candidatePath = Path.Combine(downloadDirectory, Path.GetFileName(item.FileName));
        if (!string.IsNullOrWhiteSpace(item.FileName) && File.Exists(candidatePath))
        {
            var (w, h) = SkiaWallpaperCompositor.ReadImageDimensions(candidatePath);
            long size = new FileInfo(candidatePath).Length;
            item.LocalOriginalPath = candidatePath;
            item.Width = w;
            item.Height = h;
            item.FileSizeBytes = size;
            item.DownloadedUtc ??= File.GetCreationTimeUtc(candidatePath);
            _db.UpsertWallpaper(item);

            return new DownloadWallpaperResult
            {
                Success = true,
                LocalFilePath = candidatePath,
                Width = w,
                Height = h,
                FileSizeBytes = size
            };
        }

        var result = await _provider.DownloadHighResAsync(item, downloadDirectory, progress, cancellationToken);
        if (result.Success)
        {
            _db.UpsertWallpaper(item);
        }
        return result;
    }

    public static string BuildProfileKey(string wallpaperId, string topologySignature) =>
        $"{wallpaperId}@@{topologySignature}";

    public static long GetDirectorySizeBytes(string path)
    {
        if (!Directory.Exists(path)) return 0;
        long total = 0;
        foreach (var file in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
        {
            try { total += new FileInfo(file).Length; } catch { }
        }
        return total;
    }

    public static void ClearDirectory(string path)
    {
        if (!Directory.Exists(path)) return;
        foreach (var file in Directory.EnumerateFiles(path))
        {
            try { File.Delete(file); } catch { }
        }
    }

    public static void EnforceCacheLimit(string cacheDirectory, int maxMb)
    {
        if (!Directory.Exists(cacheDirectory) || maxMb <= 0) return;
        long maxBytes = (long)maxMb * 1024 * 1024;
        var files = new DirectoryInfo(cacheDirectory)
            .GetFiles()
            .OrderByDescending(f => f.LastAccessTimeUtc)
            .ToList();

        long running = 0;
        foreach (var f in files)
        {
            running += f.Length;
            if (running > maxBytes)
            {
                try { f.Delete(); } catch { }
            }
        }
    }
}
