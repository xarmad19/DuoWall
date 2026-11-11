using DuoWall.Core.Models;

namespace DuoWall.Core.Gallery;

public sealed class GalleryPageResult
{
    public IReadOnlyList<WallpaperItem> Items { get; init; } = Array.Empty<WallpaperItem>();
    public bool HasMore { get; init; }
    public int TotalKnownCount { get; init; }
    public IReadOnlyList<string> AvailableTags { get; init; } = Array.Empty<string>();
    public string? StatusMessage { get; init; }
}

public sealed class DownloadWallpaperResult
{
    public bool Success { get; init; }
    public string? LocalFilePath { get; init; }
    public int Width { get; init; }
    public int Height { get; init; }
    public long FileSizeBytes { get; init; }
    public bool RequiresAuthentication { get; init; }
    public string? ErrorMessage { get; init; }
    public string? AuthActionUrl { get; init; }
}

/// <summary>
/// Modular abstraction for online wallpaper providers so site changes remain isolated.
/// </summary>
public interface IWallpaperSourceProvider
{
    string ProviderName { get; }

    Task<GalleryPageResult> QueryGalleryAsync(GalleryQuery query, CancellationToken cancellationToken = default);

    Task<WallpaperItem> EnrichDetailsAsync(WallpaperItem item, CancellationToken cancellationToken = default);

    Task<DownloadWallpaperResult> DownloadHighResAsync(
        WallpaperItem item,
        string destinationDirectory,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default);

    Task<string?> GetOrCacheThumbnailAsync(
        WallpaperItem item,
        string cacheDirectory,
        CancellationToken cancellationToken = default);
}
