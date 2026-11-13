using DuoWall.Core.Theming;
using SkiaSharp;
using Xunit;

namespace DuoWall.Tests;

public sealed class ColorAnalyzerTests
{
    private readonly WallpaperColorAnalyzer _analyzer = new();

    [Fact]
    public void AnalyzeBitmap_DarkBlueWallpaper_GeneratesMidnightDarkPaletteWithReadableContrast()
    {
        using var bmp = new SKBitmap(200, 100);
        using (var canvas = new SKCanvas(bmp))
        {
            canvas.Clear(new SKColor(12, 22, 56));
            canvas.DrawRect(40, 20, 60, 60, new SKPaint { Color = new SKColor(56, 189, 248) });
        }

        var palette = _analyzer.AnalyzeBitmap(bmp, "blue.png");

        Assert.True(palette.IsDarkWallpaper);
        Assert.Equal(ColorTemperatureKind.Cool, palette.Temperature);
        Assert.NotNull(palette.SuggestedStyle);
        double ratio = WallpaperColorAnalyzer.ContrastRatio(
            palette.SuggestedStyle.TextPrimaryHex,
            palette.SuggestedStyle.BackgroundTintHex);
        Assert.True(ratio >= 4.5, $"Expected contrast >= 4.5, got {ratio:0.00}");
    }

    [Fact]
    public void AnalyzeBitmap_WarmBrightWallpaper_MaintainsReadableTypographyContrast()
    {
        using var bmp = new SKBitmap(200, 100);
        using (var canvas = new SKCanvas(bmp))
        {
            canvas.Clear(new SKColor(248, 232, 200));
            canvas.DrawRect(20, 20, 80, 50, new SKPaint { Color = new SKColor(234, 88, 12) });
        }

        var palette = _analyzer.AnalyzeBitmap(bmp, "warm.png");

        Assert.False(palette.IsDarkWallpaper);
        Assert.Equal(ColorTemperatureKind.Warm, palette.Temperature);
        double ratio = WallpaperColorAnalyzer.ContrastRatio(
            palette.SuggestedStyle.TextPrimaryHex,
            palette.SuggestedStyle.BackgroundTintHex);
        Assert.True(ratio >= 4.5);
    }

    [Fact]
    public void BuiltInGlassPresets_IncludeAllEightRequiredPresets()
    {
        Assert.Equal(8, GlassmorphismStyle.BuiltInPresets.Count);
        var names = GlassmorphismStyle.BuiltInPresets.Select(p => p.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        Assert.Contains("Frosted Glass", names);
        Assert.Contains("Dark Glass", names);
        Assert.Contains("Light Glass", names);
        Assert.Contains("Crystal", names);
        Assert.Contains("Midnight Glass", names);
        Assert.Contains("Aurora", names);
        Assert.Contains("Neon Glass", names);
        Assert.Contains("Minimal Glass", names);
    }
}

