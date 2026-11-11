namespace DuoWall.Core.Models;

public enum AppThemeMode
{
    Dark,
    Light
}

public enum OutputImageFormat
{
    PngLossless,
    JpegHighQuality
}

/// <summary>
/// Persisted application settings and preferences.
/// </summary>
public sealed class AppSettings
{
    public AppThemeMode Theme { get; set; } = AppThemeMode.Dark;
    public string DownloadDirectory { get; set; } = GetDefaultDownloadDirectory();
    public string GeneratedDirectory { get; set; } = GetDefaultGeneratedDirectory();
    public string ThumbnailCacheDirectory { get; set; } = GetDefaultThumbnailCacheDirectory();

    public bool AutoApplyOnWindowsStartup { get; set; }
    public bool AutoReapplyOnDisplayChange { get; set; } = true;
    public bool MinimizeToTrayOnStartupApply { get; set; } = true;

    public GeometryCoordinateSpace DefaultCoordinateSpace { get; set; } = GeometryCoordinateSpace.PhysicalDimensions;
    public WallpaperPlacementMode DefaultPlacementMode { get; set; } = WallpaperPlacementMode.PanoramicSpan;
    public PhysicalVerticalAlignment DefaultPhysicalAlignment { get; set; } = PhysicalVerticalAlignment.AlignBottom;
    public bool DefaultBezelCompensation { get; set; } = true;

    public OutputImageFormat OutputFormat { get; set; } = OutputImageFormat.PngLossless;
    public int MaxThumbnailCacheMb { get; set; } = 500;
    public bool ShowMonitorLabelsInPreview { get; set; } = true;

    public string? LastAppliedWallpaperId { get; set; }
    public string? LastAppliedImagePath { get; set; }
    public string? LastAppliedTopologySignature { get; set; }

    public static string GetAppDataRoot() =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DuoWallBySRMD");

    public static string GetDefaultDownloadDirectory() =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "DuoWall Wallpapers");

    public static string GetDefaultGeneratedDirectory() =>
        Path.Combine(GetAppDataRoot(), "Generated");

    public static string GetDefaultThumbnailCacheDirectory() =>
        Path.Combine(GetAppDataRoot(), "ThumbCache");

    public static string GetDatabasePath() =>
        Path.Combine(GetAppDataRoot(), "duowall.db");
}

/// <summary>
/// Snapshot of the previous Windows wallpaper configuration for clean restoration.
/// </summary>
public sealed class WindowsWallpaperBackup
{
    public DateTime CapturedUtc { get; set; } = DateTime.UtcNow;
    public int DesktopPosition { get; set; }
    public uint BackgroundColor { get; set; }
    public Dictionary<string, string> MonitorWallpapers { get; set; } = new();
}
