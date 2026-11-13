using DuoWall.Core.Theming;

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

public enum TaskbarDisplayMode
{
    Both,
    PrimaryOnly,
    SecondaryOnly
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
    public string PackagesDirectory { get; set; } = GetDefaultPackagesDirectory();

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

    // V2.0 Widget Studio, Glassmorphism, Profiles & Weather
    public bool EnableDesktopWidgets { get; set; } = true;
    public bool EnableAutomaticProfileSwitching { get; set; }
    public string ActiveDesktopProfileId { get; set; } = "preset-work";
    public string? ManualProfileOverrideId { get; set; }
    public string ActiveGlassPresetId { get; set; } = "frosted-glass";
    public WallpaperColorAdaptationMode ColorAdaptationMode { get; set; } = WallpaperColorAdaptationMode.Suggested;

    public string WeatherCity { get; set; } = "Lahore";
    public bool WeatherUseFahrenheit { get; set; }
    public string? CustomWeatherApiKey { get; set; }
    public string GitHubRepositorySlug { get; set; } = "xarmad19/DuoWall";

    // V2.1 Live Video Wallpapers & Snap-to-Grid Widget Placement
    public string LiveWallpaperDirectory { get; set; } = GetDefaultLiveWallpaperDirectory();
    public string? ActiveLiveVideoPath { get; set; }
    public string? ActiveLiveVideoStillPath { get; set; }
    public string? ActiveLiveVideoPlacementKey { get; set; }
    public bool LoopLiveVideoWallpaper { get; set; } = true;
    public bool PauseLiveVideoOnBattery { get; set; }
    public bool PauseLiveVideoOnFullScreen { get; set; } = true;
    public bool EnableWidgetSnapToGrid { get; set; } = true;
    public int WidgetGridSizePx { get; set; } = 16;

    // V2.2 Custom Gallery Site, Universal Widget Accent, & Taskbar Tweaks
    public string PreferredGallerySource { get; set; } = "UltraWideWallpapers";
    public string? CustomGallerySiteUrl { get; set; }
    public string WidgetAccentColorHex { get; set; } = "#FF38BDF8";
    public bool AutoWidgetAccentFromWallpaper { get; set; } = true;

    public bool EnableTaskbarGlass { get; set; }
    public bool MakeTaskbarInvisible { get; set; }
    public bool CenterTaskbarIcons { get; set; }
    public bool EnableTaskbarGlassDock { get; set; } = true;
    public TaskbarDisplayMode TaskbarDisplayTarget { get; set; } = TaskbarDisplayMode.Both;
    public bool AutoHideTaskbar { get; set; }
    public double TaskbarAcrylicOpacity { get; set; } = 0.62;
    public double TaskbarDockExtraWidthPx { get; set; } = 16.0;
    public double TaskbarDockHeightPx { get; set; } = 36.0;

    public string? LastAppliedWallpaperId { get; set; }
    public string? LastAppliedImagePath { get; set; }
    public string? LastAppliedTopologySignature { get; set; }

    public static string GetAppDataRoot() =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DuoWallBySRMD");

    public static string GetDefaultDownloadDirectory() =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "DuoWall Wallpapers");

    public static string GetDefaultLiveWallpaperDirectory() =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyVideos), "DuoWall Live Wallpapers");

    public static string GetDefaultGeneratedDirectory() =>
        Path.Combine(GetAppDataRoot(), "Generated");

    public static string GetDefaultThumbnailCacheDirectory() =>
        Path.Combine(GetAppDataRoot(), "ThumbCache");

    public static string GetDefaultPackagesDirectory() =>
        Path.Combine(GetAppDataRoot(), "Packages");

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

/// <summary>
/// Portable backup file storing Placement Studio composition settings and desktop widget layouts together.
/// </summary>
public sealed class DuoWallLayoutPresetBackup
{
    public int SchemaVersion { get; set; } = 1;
    public DateTime ExportedUtc { get; set; } = DateTime.UtcNow;
    public string PresetName { get; set; } = "DuoWall Layout Backup";
    public string? WallpaperFilePath { get; set; }
    public string? LiveVideoPath { get; set; }
    public string? LiveVideoStillPath { get; set; }
    public string ActiveGlassPresetId { get; set; } = "frosted-glass";
    public string WidgetAccentColorHex { get; set; } = "#FF38BDF8";
    public bool AutoWidgetAccentFromWallpaper { get; set; } = true;
    public PlacementSettings Placement { get; set; } = new();
    public List<Widgets.WidgetInstance> Widgets { get; set; } = new();
}
