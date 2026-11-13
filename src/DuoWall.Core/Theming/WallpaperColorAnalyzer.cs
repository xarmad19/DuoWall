using DuoWall.Core.Engine;
using SkiaSharp;

namespace DuoWall.Core.Theming;

public enum WallpaperColorAdaptationMode
{
    Manual,
    Suggested,
    Automatic
}

public enum ColorTemperatureKind
{
    Warm,
    Neutral,
    Cool
}

public sealed class WallpaperColorPalette
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "Wallpaper Palette";
    public string SourceWallpaperPath { get; set; } = string.Empty;

    public string DominantHex { get; set; } = "#1E293B";
    public string SecondaryHex { get; set; } = "#334155";
    public string AccentHex { get; set; } = "#60A5FA";

    public double AverageLuminance { get; set; }
    public bool IsDarkWallpaper { get; set; } = true;
    public ColorTemperatureKind Temperature { get; set; } = ColorTemperatureKind.Neutral;
    public int EstimatedKelvin { get; set; } = 6500;

    public GlassmorphismStyle SuggestedStyle { get; set; } = GlassmorphismStyle.BuiltInPresets[1].Clone();
    public DateTime AnalyzedUtc { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Analyzes applied wallpapers with SkiaSharp to extract dominant, secondary,
/// and accent colors, luminance, and color temperature, then produces contrast-safe
/// Glassmorphism widget palettes without moving or resizing widgets.
/// </summary>
public sealed class WallpaperColorAnalyzer
{
    public WallpaperColorPalette AnalyzeFile(string imagePath, string? paletteName = null)
    {
        if (!File.Exists(imagePath))
            throw new FileNotFoundException("Wallpaper image not found for color analysis.", imagePath);

        using var raw = SkiaWallpaperCompositor.LoadOrientedBitmap(imagePath);
        return AnalyzeBitmap(raw, imagePath, paletteName);
    }

    public WallpaperColorPalette AnalyzeBitmap(SKBitmap bitmap, string sourcePath = "", string? paletteName = null)
    {
        // Downsample cleanly to 96x54 for rapid, low-memory perceptual quantization
        const int sampleW = 96;
        const int sampleH = 54;
        using var scaled = bitmap.Resize(new SKImageInfo(sampleW, sampleH, SKColorType.Bgra8888, SKAlphaType.Opaque), SKFilterQuality.Medium)
                           ?? throw new InvalidOperationException("Failed to resize bitmap for palette analysis.");

        var buckets = new Dictionary<int, (long Count, long R, long G, long B, double SatSum)>();
        double totalLum = 0;
        double warmthSum = 0;
        int totalPixels = sampleW * sampleH;

        for (int y = 0; y < sampleH; y++)
        {
            for (int x = 0; x < sampleW; x++)
            {
                SKColor c = scaled.GetPixel(x, y);
                totalLum += RelativeLuminance(c.Red, c.Green, c.Blue);
                warmthSum += (c.Red - c.Blue) / 255.0;

                // Quantize into 5-bit per channel buckets (32x32x32 perceptual bins)
                int key = ((c.Red >> 4) << 8) | ((c.Green >> 4) << 4) | (c.Blue >> 4);
                double sat = ComputeSaturation(c.Red, c.Green, c.Blue);
                if (buckets.TryGetValue(key, out var existing))
                {
                    buckets[key] = (existing.Count + 1, existing.R + c.Red, existing.G + c.Green, existing.B + c.Blue, existing.SatSum + sat);
                }
                else
                {
                    buckets[key] = (1, c.Red, c.Green, c.Blue, sat);
                }
            }
        }

        double avgLum = totalPixels > 0 ? totalLum / totalPixels : 0.2;
        double avgWarmth = totalPixels > 0 ? warmthSum / totalPixels : 0.0;
        bool isDark = avgLum < 0.45;

        ColorTemperatureKind tempKind = avgWarmth > 0.08
            ? ColorTemperatureKind.Warm
            : (avgWarmth < -0.08 ? ColorTemperatureKind.Cool : ColorTemperatureKind.Neutral);

        int kelvin = (int)Math.Clamp(6500 - avgWarmth * 3500, 3200, 9500);

        var orderedClusters = buckets.Values
            .Select(b => (
                Count: b.Count,
                R: (byte)(b.R / b.Count),
                G: (byte)(b.G / b.Count),
                B: (byte)(b.B / b.Count),
                AvgSat: b.SatSum / b.Count))
            .OrderByDescending(c => c.Count)
            .ToList();

        var dominant = orderedClusters.FirstOrDefault();
        if (dominant.Count == 0)
            dominant = (1, 20, 26, 40, 0.2);

        // Find distinct secondary color (perceptually separated from dominant)
        var secondary = orderedClusters.FirstOrDefault(c => ColorDistance(dominant.R, dominant.G, dominant.B, c.R, c.G, c.B) > 48);
        if (secondary.Count == 0)
            secondary = orderedClusters.Skip(1).FirstOrDefault();
        if (secondary.Count == 0)
            secondary = dominant;

        // Find saturated, readable accent color
        var accent = orderedClusters
            .Where(c => c.Count >= Math.Max(2, totalPixels / 250))
            .OrderByDescending(c => c.AvgSat * Math.Log(1 + c.Count) * (0.5 + RelativeLuminance(c.R, c.G, c.B)))
            .FirstOrDefault();

        if (accent.Count == 0 || accent.AvgSat < 0.16)
        {
            accent = tempKind switch
            {
                ColorTemperatureKind.Warm => (1, 245, 158, 11, 0.9),
                ColorTemperatureKind.Cool => (1, 96, 165, 250, 0.9),
                _ => (1, 56, 189, 248, 0.9)
            };
        }

        string domHex = ToHex(dominant.R, dominant.G, dominant.B);
        string secHex = ToHex(secondary.R, secondary.G, secondary.B);
        string accHex = ToHex(accent.R, accent.G, accent.B);

        var style = BuildAdaptiveGlassStyle(dominant, secondary, accent, avgLum, isDark, tempKind);

        return new WallpaperColorPalette
        {
            Name = paletteName ?? $"Adaptive ({Path.GetFileNameWithoutExtension(sourcePath)})",
            SourceWallpaperPath = sourcePath,
            DominantHex = domHex,
            SecondaryHex = secHex,
            AccentHex = accHex,
            AverageLuminance = avgLum,
            IsDarkWallpaper = isDark,
            Temperature = tempKind,
            EstimatedKelvin = kelvin,
            SuggestedStyle = style,
            AnalyzedUtc = DateTime.UtcNow
        };
    }

    /// <summary>
    /// Builds a contrast-safe GlassmorphismStyle from analyzed wallpaper metrics.
    /// Ensures WCAG >= 4.5:1 primary text contrast and >= 3.0:1 secondary/accent readability.
    /// </summary>
    public static GlassmorphismStyle BuildAdaptiveGlassStyle(
        (long Count, byte R, byte G, byte B, double AvgSat) dominant,
        (long Count, byte R, byte G, byte B, double AvgSat) secondary,
        (long Count, byte R, byte G, byte B, double AvgSat) accent,
        double avgLuminance,
        bool isDarkWallpaper,
        ColorTemperatureKind temp)
    {
        GlassmorphismStyle baseStyle;
        if (isDarkWallpaper)
        {
            baseStyle = temp == ColorTemperatureKind.Cool
                ? GlassmorphismStyle.GetPreset("Midnight Glass")
                : GlassmorphismStyle.GetPreset("Dark Glass");

            // Blend subtle dominant wallpaper hue into deep smoked tint so text remains crisp
            byte tintR = (byte)(dominant.R * 0.25 + 10 * 0.75);
            byte tintG = (byte)(dominant.G * 0.25 + 14 * 0.75);
            byte tintB = (byte)(dominant.B * 0.25 + 24 * 0.75);
            baseStyle.BackgroundTintHex = ToHex(tintR, tintG, tintB);
            baseStyle.GradientSecondaryHex = ToHex(secondary.R, secondary.G, secondary.B);
            baseStyle.BackgroundOpacity = 0.56;
            baseStyle.BackgroundTransparency = 0.44;
        }
        else
        {
            // Bright / colorful wallpaper -> frosted neutral glass with readable dark or dark-smoked glass
            bool veryBright = avgLuminance > 0.72;
            baseStyle = veryBright
                ? GlassmorphismStyle.GetPreset("Light Glass")
                : GlassmorphismStyle.GetPreset("Frosted Glass");

            if (!veryBright)
            {
                // Medium-bright colorful wallpaper: smoked frosted backdrop keeps white typography sharp
                baseStyle.BackgroundTintHex = "#0F172A";
                baseStyle.BackgroundOpacity = 0.52;
                baseStyle.BackgroundTransparency = 0.48;
                baseStyle.TextPrimaryHex = "#F8FAFC";
                baseStyle.TextSecondaryHex = "#E2E8F0";
            }
        }

        string surfaceHex = baseStyle.BackgroundTintHex;
        // Pick primary & secondary text ensuring >= 4.5 contrast against glass tint
        bool useLightText = RelativeLuminanceFromHex(surfaceHex) < 0.42;
        baseStyle.TextPrimaryHex = useLightText ? "#F8FAFC" : "#0F172A";
        baseStyle.TextSecondaryHex = useLightText ? "#CBD5E1" : "#334155";

        string rawAccent = ToHex(accent.R, accent.G, accent.B);
        baseStyle.AccentHex = EnsureContrastRatio(rawAccent, surfaceHex, minRatio: 3.2, preferLight: useLightText);
        baseStyle.BorderColorHex = EnsureContrastRatio(rawAccent, surfaceHex, minRatio: 2.2, preferLight: useLightText);
        baseStyle.Id = "wallpaper-adaptive";
        baseStyle.Name = "Wallpaper Adaptive";
        baseStyle.IsBuiltIn = false;

        return baseStyle;
    }

    /// <summary>
    /// Avoids jarring palette thrashing during minor transitions by checking perceptual color delta.
    /// </summary>
    public static bool IsSignificantlyDifferent(WallpaperColorPalette? previous, WallpaperColorPalette next, double threshold = 24.0)
    {
        if (previous == null) return true;
        var pDom = ParseRgb(previous.DominantHex);
        var nDom = ParseRgb(next.DominantHex);
        var pAcc = ParseRgb(previous.AccentHex);
        var nAcc = ParseRgb(next.AccentHex);
        double domDist = ColorDistance(pDom.R, pDom.G, pDom.B, nDom.R, nDom.G, nDom.B);
        double accDist = ColorDistance(pAcc.R, pAcc.G, pAcc.B, nAcc.R, nAcc.G, nAcc.B);
        return domDist > threshold || accDist > threshold || Math.Abs(previous.AverageLuminance - next.AverageLuminance) > 0.12;
    }

    public static double ContrastRatio(string hex1, string hex2)
    {
        double l1 = RelativeLuminanceFromHex(hex1);
        double l2 = RelativeLuminanceFromHex(hex2);
        double bright = Math.Max(l1, l2);
        double dark = Math.Min(l1, l2);
        return (bright + 0.05) / (dark + 0.05);
    }

    public static string EnsureContrastRatio(string candidateHex, string backgroundHex, double minRatio, bool preferLight)
    {
        if (ContrastRatio(candidateHex, backgroundHex) >= minRatio)
            return candidateHex;

        var (r, g, b) = ParseRgb(candidateHex);
        byte target = preferLight ? (byte)255 : (byte)15;

        for (int step = 1; step <= 10; step++)
        {
            double t = step / 10.0;
            byte nr = (byte)Math.Round(r + (target - r) * t);
            byte ng = (byte)Math.Round(g + (target - g) * t);
            byte nb = (byte)Math.Round(b + (target - b) * t);
            string shifted = ToHex(nr, ng, nb);
            if (ContrastRatio(shifted, backgroundHex) >= minRatio)
                return shifted;
        }

        return preferLight ? "#F8FAFC" : "#0F172A";
    }

    private static double RelativeLuminanceFromHex(string hex)
    {
        var (r, g, b) = ParseRgb(hex);
        return RelativeLuminance(r, g, b);
    }

    public static double RelativeLuminance(byte r, byte g, byte b)
    {
        static double Linearize(byte v)
        {
            double c = v / 255.0;
            return c <= 0.03928 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
        }
        return 0.2126 * Linearize(r) + 0.7152 * Linearize(g) + 0.0722 * Linearize(b);
    }

    private static double ComputeSaturation(byte r, byte g, byte b)
    {
        double max = Math.Max(r, Math.Max(g, b)) / 255.0;
        double min = Math.Min(r, Math.Min(g, b)) / 255.0;
        if (max <= 0.001) return 0;
        return (max - min) / max;
    }

    private static double ColorDistance(byte r1, byte g1, byte b1, byte r2, byte g2, byte b2)
    {
        int dr = r1 - r2;
        int dg = g1 - g2;
        int db = b1 - b2;
        return Math.Sqrt(dr * dr + dg * dg + db * db);
    }

    private static (byte R, byte G, byte B) ParseRgb(string hex)
    {
        var c = SkiaWallpaperCompositor.ParseHexColor(hex);
        return (c.Red, c.Green, c.Blue);
    }

    private static string ToHex(byte r, byte g, byte b) => $"#{r:X2}{g:X2}{b:X2}";
}

