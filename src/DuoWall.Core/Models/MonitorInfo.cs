namespace DuoWall.Core.Models;

/// <summary>
/// Represents an integer pixel rectangle in Windows virtual desktop coordinates.
/// Supports negative coordinates for displays positioned left of or above the primary display.
/// </summary>
public readonly record struct PixelRect(int X, int Y, int Width, int Height)
{
    public int Right => X + Width;
    public int Bottom => Y + Height;
    public bool IsEmpty => Width <= 0 || Height <= 0;
    public double AspectRatio => Height > 0 ? (double)Width / Height : 1.0;

    public override string ToString() => $"({X}, {Y}, {Width}x{Height})";
}

/// <summary>
/// Represents a floating-point rectangle in normalized, physical (mm), or source-image space.
/// </summary>
public readonly record struct RectD(double X, double Y, double Width, double Height)
{
    public double Right => X + Width;
    public double Bottom => Y + Height;
    public double CenterX => X + Width * 0.5;
    public double CenterY => Y + Height * 0.5;
    public bool IsEmpty => Width <= 0 || Height <= 0;
}

/// <summary>
/// Orientation of a connected display as reported by Windows display settings.
/// </summary>
public enum DisplayOrientation
{
    Landscape = 0,
    Portrait = 1,
    LandscapeFlipped = 2,
    PortraitFlipped = 3
}

/// <summary>
/// Complete metadata and physical/virtual geometry for a connected display.
/// </summary>
public sealed class MonitorInfo
{
    public string Id { get; set; } = string.Empty;
    public string DeviceName { get; set; } = string.Empty;
    public string DeviceId { get; set; } = string.Empty;
    public string FriendlyName { get; set; } = "Display";
    public string Manufacturer { get; set; } = string.Empty;
    public PixelRect Bounds { get; set; }
    public PixelRect WorkingArea { get; set; }
    public int NativeWidth { get; set; }
    public int NativeHeight { get; set; }
    public DisplayOrientation Orientation { get; set; } = DisplayOrientation.Landscape;
    public bool IsPrimary { get; set; }
    public uint DpiX { get; set; } = 96;
    public uint DpiY { get; set; } = 96;
    public double ScalePercent { get; set; } = 100.0;
    public int RefreshRateHz { get; set; } = 60;

    /// <summary>
    /// Physical active panel width in millimeters (from EDID, or estimated if unavailable).
    /// </summary>
    public double PhysicalWidthMm { get; set; }

    /// <summary>
    /// Physical active panel height in millimeters (from EDID, or estimated if unavailable).
    /// </summary>
    public double PhysicalHeightMm { get; set; }

    /// <summary>
    /// True if physical dimensions came from hardware EDID; false if estimated from pixels/DPI.
    /// </summary>
    public bool HasHardwarePhysicalDimensions { get; set; }

    /// <summary>
    /// Optional user calibration offset in millimeters for physical alignment mode.
    /// </summary>
    public double UserOffsetMmX { get; set; }

    /// <summary>
    /// Optional vertical desk alignment offset in millimeters for physical alignment mode.
    /// </summary>
    public double UserOffsetMmY { get; set; }

    /// <summary>
    /// Optional bezel widths in millimeters (used when bezel compensation is enabled).
    /// </summary>
    public double BezelLeftMm { get; set; } = 7.0;
    public double BezelRightMm { get; set; } = 7.0;
    public double BezelTopMm { get; set; } = 7.0;
    public double BezelBottomMm { get; set; } = 12.0;

    public bool IsIncluded { get; set; } = true;
    public bool IsConnected { get; set; } = true;

    public int PixelWidth => Bounds.Width;
    public int PixelHeight => Bounds.Height;

    public double EffectivePhysicalWidthMm =>
        PhysicalWidthMm > 10 ? PhysicalWidthMm : EstimateWidthMm();

    public double EffectivePhysicalHeightMm =>
        PhysicalHeightMm > 10 ? PhysicalHeightMm : EstimateHeightMm();

    public double PhysicalDiagonalInches
    {
        get
        {
            double diagMm = Math.Sqrt(
                EffectivePhysicalWidthMm * EffectivePhysicalWidthMm +
                EffectivePhysicalHeightMm * EffectivePhysicalHeightMm);
            return diagMm / 25.4;
        }
    }

    public double EffectivePpiX =>
        EffectivePhysicalWidthMm > 0 ? PixelWidth / (EffectivePhysicalWidthMm / 25.4) : 96.0;

    public double EffectivePpiY =>
        EffectivePhysicalHeightMm > 0 ? PixelHeight / (EffectivePhysicalHeightMm / 25.4) : 96.0;

    public double PixelsPerMmX => EffectivePhysicalWidthMm > 0 ? PixelWidth / EffectivePhysicalWidthMm : 3.78;
    public double PixelsPerMmY => EffectivePhysicalHeightMm > 0 ? PixelHeight / EffectivePhysicalHeightMm : 3.78;

    public string ResolutionLabel => $"{PixelWidth} × {PixelHeight}";

    public string AspectRatioLabel => FormatAspectRatio(PixelWidth, PixelHeight);

    public string PhysicalSizeLabel =>
        $"{EffectivePhysicalWidthMm / 10.0:0.0} × {EffectivePhysicalHeightMm / 10.0:0.0} cm ({PhysicalDiagonalInches:0.0}\")";

    public string DisplayLabel =>
        string.IsNullOrWhiteSpace(FriendlyName) ? DeviceName : FriendlyName;

    public MonitorInfo Clone() => new()
    {
        Id = Id,
        DeviceName = DeviceName,
        DeviceId = DeviceId,
        FriendlyName = FriendlyName,
        Manufacturer = Manufacturer,
        Bounds = Bounds,
        WorkingArea = WorkingArea,
        NativeWidth = NativeWidth,
        NativeHeight = NativeHeight,
        Orientation = Orientation,
        IsPrimary = IsPrimary,
        DpiX = DpiX,
        DpiY = DpiY,
        ScalePercent = ScalePercent,
        RefreshRateHz = RefreshRateHz,
        PhysicalWidthMm = PhysicalWidthMm,
        PhysicalHeightMm = PhysicalHeightMm,
        HasHardwarePhysicalDimensions = HasHardwarePhysicalDimensions,
        UserOffsetMmX = UserOffsetMmX,
        UserOffsetMmY = UserOffsetMmY,
        BezelLeftMm = BezelLeftMm,
        BezelRightMm = BezelRightMm,
        BezelTopMm = BezelTopMm,
        BezelBottomMm = BezelBottomMm,
        IsIncluded = IsIncluded,
        IsConnected = IsConnected
    };

    private double EstimateWidthMm()
    {
        double dpi = DpiX > 0 ? DpiX : 96.0;
        return (PixelWidth / dpi) * 25.4;
    }

    private double EstimateHeightMm()
    {
        double dpi = DpiY > 0 ? DpiY : 96.0;
        return (PixelHeight / dpi) * 25.4;
    }

    public static string FormatAspectRatio(int width, int height)
    {
        if (width <= 0 || height <= 0) return "—";
        double r = (double)width / height;
        if (Math.Abs(r - 32.0 / 9.0) < 0.08) return "32:9";
        if (Math.Abs(r - 21.0 / 9.0) < 0.08) return "21:9";
        if (Math.Abs(r - 16.0 / 9.0) < 0.04) return "16:9";
        if (Math.Abs(r - 16.0 / 10.0) < 0.04) return "16:10";
        if (Math.Abs(r - 9.0 / 16.0) < 0.04) return "9:16";
        if (Math.Abs(r - 4.0 / 3.0) < 0.04) return "4:3";
        int gcd = Gcd(width, height);
        int rw = width / gcd;
        int rh = height / gcd;
        if (rw <= 64 && rh <= 64) return $"{rw}:{rh}";
        return $"{r:0.00}:1";
    }

    private static int Gcd(int a, int b)
    {
        while (b != 0)
        {
            int t = a % b;
            a = b;
            b = t;
        }
        return Math.Max(1, a);
    }
}
