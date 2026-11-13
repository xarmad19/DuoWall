namespace DuoWall.Core.Models;

/// <summary>
/// Represents a wallpaper item either from UltraWideWallpapers.net or the local library.
/// </summary>
public sealed class WallpaperItem
{
    /// <summary>
    /// Unique identifier (e.g. "uwshot-6300.jpg" or "local:<sha256>").
    /// </summary>
    public string Id { get; set; } = string.Empty;

    public string FileName { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string SourceProvider { get; set; } = "UltraWideWallpapers";

    public string SourcePageUrl { get; set; } = string.Empty;
    public string ThumbnailUrl { get; set; } = string.Empty;
    public string PreviewUrl { get; set; } = string.Empty;
    public string HighResUrl { get; set; } = string.Empty;

    public int Width { get; set; }
    public int Height { get; set; }
    public long FileSizeBytes { get; set; }

    public List<string> Tags { get; set; } = new();
    public List<string> Categories { get; set; } = new();

    public bool RequiresAuthentication { get; set; }
    public string? AccessRestrictionReason { get; set; }

    public string? LocalOriginalPath { get; set; }
    public string? LocalThumbnailPath { get; set; }

    public bool IsFavorite { get; set; }
    public bool IsDownloaded => !string.IsNullOrWhiteSpace(LocalOriginalPath) && File.Exists(LocalOriginalPath);

    public DateTime? DownloadedUtc { get; set; }
    public DateTime? LastAppliedUtc { get; set; }
    public int SequenceNumber { get; set; }

    public string ResolutionLabel =>
        Width > 0 && Height > 0 ? $"{Width} × {Height}" : "Original High-Res";

    public string AspectRatioLabel =>
        Width > 0 && Height > 0 ? MonitorInfo.FormatAspectRatio(Width, Height) : "32:9";

    public string FileSizeLabel
    {
        get
        {
            if (FileSizeBytes <= 0) return "—";
            if (FileSizeBytes < 1024) return $"{FileSizeBytes} B";
            if (FileSizeBytes < 1024 * 1024) return $"{FileSizeBytes / 1024.0:0.0} KB";
            return $"{FileSizeBytes / (1024.0 * 1024.0):0.00} MB";
        }
    }

    public string TagsSummary => Tags.Count > 0 ? string.Join(" · ", Tags.Take(5)) : "Ultrawide";

    public static bool IsVideoPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return false;
        string ext = Path.GetExtension(path);
        return ext.Equals(".mp4", StringComparison.OrdinalIgnoreCase)
            || ext.Equals(".webm", StringComparison.OrdinalIgnoreCase)
            || ext.Equals(".mov", StringComparison.OrdinalIgnoreCase)
            || ext.Equals(".wmv", StringComparison.OrdinalIgnoreCase)
            || ext.Equals(".mkv", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Resolves the highest-resolution available preview source.
    /// Avoids letting a cached low-res thumbnail (e.g. 400px grid cache) shadow a high-res PreviewUrl (1700px+).
    /// </summary>
    public static string ResolveBestPreviewSource(WallpaperItem model, Func<string, bool>? fileExists = null)
    {
        fileExists ??= File.Exists;
        if (!string.IsNullOrWhiteSpace(model.LocalOriginalPath)
            && !IsVideoPath(model.LocalOriginalPath)
            && fileExists(model.LocalOriginalPath))
        {
            return model.LocalOriginalPath;
        }

        if (!string.IsNullOrWhiteSpace(model.PreviewUrl) && !IsVideoPath(model.PreviewUrl))
            return model.PreviewUrl;

        if (!string.IsNullOrWhiteSpace(model.LocalThumbnailPath) && fileExists(model.LocalThumbnailPath))
            return model.LocalThumbnailPath;

        return model.ThumbnailUrl ?? string.Empty;
    }
}

/// <summary>
/// Query criteria for gallery filtering and pagination.
/// </summary>
public sealed class GalleryQuery
{
    public string SearchText { get; set; } = string.Empty;
    public string CategoryOrResolutionFilter { get; set; } = "All";
    public List<string> SelectedTags { get; set; } = new();
    public GallerySortMode SortMode { get; set; } = GallerySortMode.Recent;
    public int Offset { get; set; }
    public int Limit { get; set; } = 24;
    public bool FavoritesOnly { get; set; }
}

public enum GallerySortMode
{
    Recent,
    Popular,
    Oldest,
    Shuffle
}
