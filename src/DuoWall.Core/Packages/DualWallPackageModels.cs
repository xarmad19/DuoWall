using System.ComponentModel;
using DuoWall.Core.Theming;
using DuoWall.Core.Widgets;

namespace DuoWall.Core.Packages;

public enum PackageKind
{
    Widget,
    WidgetSkin,
    GlassPreset,
    ColorPalette,
    Theme,
    DesktopProfile,
    VideoLayout
}

public sealed class WidgetConfigFieldSchema
{
    public string Key { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public string Type { get; set; } = "string"; // string | bool | number | color | select
    public string DefaultValue { get; set; } = string.Empty;
    public List<string> Options { get; set; } = new();
}

/// <summary>
/// Documented, versioned, strictly declarative DualWallWidgetPackage manifest (.dwwp archive).
/// Arbitrary script or native executable payloads are explicitly forbidden.
/// </summary>
public sealed class DualWallWidgetPackageManifest
{
    public const string CurrentFormatName = "DualWallWidgetPackage";
    public const string CurrentSchemaVersion = "2.0";

    public string PackageFormat { get; set; } = CurrentFormatName;
    public string SchemaVersion { get; set; } = CurrentSchemaVersion;

    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Version { get; set; } = "1.0.0";
    public string MinAppVersion { get; set; } = "2.0.0";
    public string Author { get; set; } = "SRMD";
    public string Description { get; set; } = string.Empty;
    public string Category { get; set; } = "Utilities";
    public List<string> Tags { get; set; } = new();
    public double Rating { get; set; } = 4.9;
    public int DownloadCount { get; set; }
    public DateTime PublishedUtc { get; set; } = DateTime.UtcNow;

    public PackageKind PackageType { get; set; } = PackageKind.Widget;
    public WidgetKind WidgetKind { get; set; } = WidgetKind.CustomDeclarative;
    public List<string> Dependencies { get; set; } = new();

    public List<WidgetConfigFieldSchema> ConfigSchema { get; set; } = new();
    public Dictionary<string, string> DefaultSettings { get; set; } = new();

    public GlassmorphismStyle? GlassStyle { get; set; }
    public WallpaperColorPalette? ColorPalette { get; set; }
    public DesktopProfile? DesktopProfile { get; set; }
    public WidgetInstance? WidgetTemplate { get; set; }

    public string? VideoFileRelativePath { get; set; }
    public string? PreviewImageRelativePath { get; set; }
    public List<string> Assets { get; set; } = new();
}

public sealed class InstalledPackageRecord
{
    public string PackageId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Version { get; set; } = "1.0.0";
    public string Author { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public PackageKind PackageType { get; set; }
    public bool IsBuiltIn { get; set; }
    public string InstallDirectory { get; set; } = string.Empty;
    public string ManifestJson { get; set; } = string.Empty;
    public DateTime InstalledUtc { get; set; } = DateTime.UtcNow;
}

public sealed class MarketplaceCatalogEntry : INotifyPropertyChanged
{
    private bool _isOnDesktop;

    public required DualWallWidgetPackageManifest Manifest { get; init; }
    public bool IsFeatured { get; init; }
    public bool IsInstalled { get; set; }
    public string SourceBadge { get; init; } = "Local Catalog";

    public bool IsOnDesktop
    {
        get => _isOnDesktop;
        set
        {
            if (_isOnDesktop == value) return;
            _isOnDesktop = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsOnDesktop)));
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}

public sealed class PackageImportResult
{
    public bool Success { get; init; }
    public DualWallWidgetPackageManifest? Manifest { get; init; }
    public InstalledPackageRecord? InstalledRecord { get; init; }
    public List<string> Warnings { get; init; } = new();
    public string? ErrorMessage { get; init; }
}

