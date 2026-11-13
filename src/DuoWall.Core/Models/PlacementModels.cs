namespace DuoWall.Core.Models;

/// <summary>
/// Supported wallpaper placement modes across connected displays.
/// </summary>
public enum WallpaperPlacementMode
{
    PanoramicSpan,
    Fill,
    Fit,
    Stretch,
    Center,
    Individual
}

/// <summary>
/// Coordinate space used to map monitors onto the combined panoramic desktop canvas.
/// </summary>
public enum GeometryCoordinateSpace
{
    /// <summary>
    /// Maps strictly onto Windows virtual desktop pixel coordinates.
    /// Best when monitors match PPI or pixel-exact alignment is desired.
    /// </summary>
    VirtualPixels,

    /// <summary>
    /// Maps onto physical monitor dimensions in millimeters (using EDID physical size + desk offset).
    /// Eliminates scale jumps and horizon steps between physically different monitors (e.g. 24" vs 27").
    /// </summary>
    PhysicalDimensions
}

/// <summary>
/// Preset vertical alignment helper for physical monitor desk placement.
/// </summary>
public enum PhysicalVerticalAlignment
{
    Custom,
    AlignTop,
    AlignCenter,
    AlignBottom
}

/// <summary>
/// Per-monitor wallpaper override when using Individual mode.
/// </summary>
public sealed class PerMonitorAssignment
{
    public string MonitorId { get; set; } = string.Empty;
    public string ImagePath { get; set; } = string.Empty;
    public string? WallpaperId { get; set; }
    public WallpaperPlacementMode SubMode { get; set; } = WallpaperPlacementMode.Fill;
    public double Zoom { get; set; } = 1.0;
    public double PanX { get; set; }
    public double PanY { get; set; }
    public double FocalX { get; set; } = 0.5;
    public double FocalY { get; set; } = 0.5;
    public bool FlipHorizontal { get; set; }
    public bool FlipVertical { get; set; }

    public PerMonitorAssignment Clone() => new()
    {
        MonitorId = MonitorId,
        ImagePath = ImagePath,
        WallpaperId = WallpaperId,
        SubMode = SubMode,
        Zoom = Zoom,
        PanX = PanX,
        PanY = PanY,
        FocalX = FocalX,
        FocalY = FocalY,
        FlipHorizontal = FlipHorizontal,
        FlipVertical = FlipVertical
    };
}

/// <summary>
/// Complete user positioning and composition settings for a wallpaper.
/// </summary>
public sealed class PlacementSettings
{
    public WallpaperPlacementMode Mode { get; set; } = WallpaperPlacementMode.PanoramicSpan;
    public GeometryCoordinateSpace CoordinateSpace { get; set; } = GeometryCoordinateSpace.PhysicalDimensions;
    public PhysicalVerticalAlignment PhysicalAlignment { get; set; } = PhysicalVerticalAlignment.AlignBottom;

    /// <summary>
    /// Zoom factor relative to automatic base scaling (1.0 = auto, > 1.0 zooms in, &lt; 1.0 zooms out).
    /// </summary>
    public double Zoom { get; set; } = 1.0;

    /// <summary>
    /// Horizontal pan offset in normalized canvas units (-1.0 to +1.0 = +/- one full canvas width).
    /// Positive moves the image to the right.
    /// </summary>
    public double PanX { get; set; }

    /// <summary>
    /// Vertical pan offset in normalized canvas units (-1.0 to +1.0 = +/- one full canvas height).
    /// Positive moves the image downward.
    /// </summary>
    public double PanY { get; set; }

    /// <summary>
    /// Focal anchor inside the source image (0.0 = left/top, 0.5 = center, 1.0 = right/bottom).
    /// </summary>
    public double FocalX { get; set; } = 0.5;
    public double FocalY { get; set; } = 0.5;

    /// <summary>
    /// Optional crop fine-tuning inset fractions (0.0 to 0.45 each) trimmed from source edges before scaling.
    /// </summary>
    public double CropLeft { get; set; }
    public double CropTop { get; set; }
    public double CropRight { get; set; }
    public double CropBottom { get; set; }

    /// <summary>
    /// Mirror source image horizontally before slicing.
    /// </summary>
    public bool FlipHorizontal { get; set; }

    /// <summary>
    /// Mirror source image vertically before slicing.
    /// </summary>
    public bool FlipVertical { get; set; }

    /// <summary>
    /// When true in PhysicalDimensions mode, accounts for physical bezel spacing between adjacent displays.
    /// </summary>
    public bool EnableBezelCompensation { get; set; } = true;

    /// <summary>
    /// Background color used for uncovered areas when panning leaves a gap, or in Fit/Center/zoomed-out modes.
    /// </summary>
    public string BackgroundColorHex { get; set; } = "#0D1017";

    /// <summary>
    /// Per-monitor overrides used when Mode == Individual.
    /// </summary>
    public Dictionary<string, PerMonitorAssignment> PerMonitorAssignments { get; set; } = new();

    public void ResetPositioning()
    {
        Zoom = 1.0;
        PanX = 0.0;
        PanY = 0.0;
        FocalX = 0.5;
        FocalY = 0.5;
        CropLeft = 0.0;
        CropTop = 0.0;
        CropRight = 0.0;
        CropBottom = 0.0;
        FlipHorizontal = false;
        FlipVertical = false;
    }

    public PlacementSettings Clone()
    {
        var copy = new PlacementSettings
        {
            Mode = Mode,
            CoordinateSpace = CoordinateSpace,
            PhysicalAlignment = PhysicalAlignment,
            Zoom = Zoom,
            PanX = PanX,
            PanY = PanY,
            FocalX = FocalX,
            FocalY = FocalY,
            CropLeft = CropLeft,
            CropTop = CropTop,
            CropRight = CropRight,
            CropBottom = CropBottom,
            FlipHorizontal = FlipHorizontal,
            FlipVertical = FlipVertical,
            EnableBezelCompensation = EnableBezelCompensation,
            BackgroundColorHex = BackgroundColorHex,
            PerMonitorAssignments = new Dictionary<string, PerMonitorAssignment>()
        };
        foreach (var kv in PerMonitorAssignments)
        {
            copy.PerMonitorAssignments[kv.Key] = kv.Value.Clone();
        }
        return copy;
    }
}

/// <summary>
/// Calculated projection of a single monitor inside the unified desktop geometry and source image.
/// </summary>
public sealed class MonitorSlicePlan
{
    public required MonitorInfo Monitor { get; init; }

    /// <summary>
    /// Monitor bounds normalized to the combined canvas coordinate system (pixels or physical mm).
    /// </summary>
    public RectD CanvasRect { get; init; }

    /// <summary>
    /// Target rectangle inside the monitor's output pixel buffer (usually (0, 0, Width, Height),
    /// or clipped sub-region when Fit/Center/zoomed-out leaves background margin).
    /// </summary>
    public RectD DestinationPixelRect { get; init; }

    /// <summary>
    /// Exact floating-point rectangle sampled from the source image in pixels.
    /// </summary>
    public RectD SourcePixelRect { get; init; }

    /// <summary>
    /// Source image path used for this monitor (supports Individual mode).
    /// </summary>
    public string SourceImagePath { get; init; } = string.Empty;

    public bool FlipHorizontal { get; init; }
    public bool FlipVertical { get; init; }

    /// <summary>
    /// Output width in pixels (equals Monitor.PixelWidth).
    /// </summary>
    public int OutputWidth => Monitor.PixelWidth;

    /// <summary>
    /// Output height in pixels (equals Monitor.PixelHeight).
    /// </summary>
    public int OutputHeight => Monitor.PixelHeight;

    /// <summary>
    /// Horizontal upscaling ratio (> 1.0 means source pixels are stretched up to fit monitor).
    /// </summary>
    public double UpscaleRatioX =>
        SourcePixelRect.Width > 0 ? DestinationPixelRect.Width / SourcePixelRect.Width : 1.0;

    /// <summary>
    /// Vertical upscaling ratio (> 1.0 means source pixels are stretched up to fit monitor).
    /// </summary>
    public double UpscaleRatioY =>
        SourcePixelRect.Height > 0 ? DestinationPixelRect.Height / SourcePixelRect.Height : 1.0;

    public double MaxUpscaleRatio => Math.Max(UpscaleRatioX, UpscaleRatioY);

    public bool HasResolutionWarning => MaxUpscaleRatio > 1.20;
}

/// <summary>
/// Complete multi-monitor composition plan produced by the geometry engine.
/// Used identically by both the interactive preview and the Skia renderer to guarantee 1:1 fidelity.
/// </summary>
public sealed class CompositionPlan
{
    public int SourceWidth { get; init; }
    public int SourceHeight { get; init; }
    public double CombinedCanvasWidth { get; init; }
    public double CombinedCanvasHeight { get; init; }
    public GeometryCoordinateSpace CoordinateSpace { get; init; }
    public WallpaperPlacementMode Mode { get; init; }
    public bool FlipHorizontal { get; init; }
    public bool FlipVertical { get; init; }

    /// <summary>
    /// Total virtual pixel bounding width across all included monitors.
    /// </summary>
    public int VirtualDesktopWidth { get; init; }

    /// <summary>
    /// Total virtual pixel bounding height across all included monitors.
    /// </summary>
    public int VirtualDesktopHeight { get; init; }

    /// <summary>
    /// Normalized virtual canvas rect where the image is placed in combined space.
    /// Useful for rendering smooth live previews including out-of-bounds background fill.
    /// </summary>
    public RectD ImageCanvasRect { get; init; }

    public IReadOnlyList<MonitorSlicePlan> Slices { get; init; } = Array.Empty<MonitorSlicePlan>();

    public double MaxUpscaleRatio =>
        Slices.Count == 0 ? 1.0 : Slices.Max(s => s.MaxUpscaleRatio);

    public bool HasInsufficientResolution => Slices.Any(s => s.HasResolutionWarning);

    public string? ResolutionAdvisory { get; init; }
}
