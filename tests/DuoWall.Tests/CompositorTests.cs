using DuoWall.Core.Engine;
using DuoWall.Core.Models;
using SkiaSharp;
using Xunit;

namespace DuoWall.Tests;

public sealed class CompositorTests : IDisposable
{
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), $"duowall_test_{Guid.NewGuid():N}");

    public CompositorTests()
    {
        Directory.CreateDirectory(_tempDir);
    }

    [Fact]
    public async Task RenderMonitorWallpapers_ProducesExactCrossMonitorSeamAlignment()
    {
        // Create a 3840x1080 test image where left half is pure Red (255,0,0) and right half is pure Blue (0,0,255)
        // with a crisp horizontal white band (255,255,255) through the middle (Y = 440..640)
        string srcPath = Path.Combine(_tempDir, "seam_source.png");
        using (var bmp = new SKBitmap(3840, 1080))
        using (var canvas = new SKCanvas(bmp))
        {
            canvas.DrawRect(0, 0, 1920, 1080, new SKPaint { Color = SKColors.Red });
            canvas.DrawRect(1920, 0, 1920, 1080, new SKPaint { Color = SKColors.Blue });
            canvas.DrawRect(0, 440, 3840, 200, new SKPaint { Color = SKColors.White });
            using var img = SKImage.FromBitmap(bmp);
            using var data = img.Encode(SKEncodedImageFormat.Png, 100);
            await File.WriteAllBytesAsync(srcPath, data.ToArray());
        }

        var monitors = new[]
        {
            new MonitorInfo { Id = "L", Bounds = new PixelRect(-1920, 0, 1920, 1080), IsConnected = true, IsIncluded = true },
            new MonitorInfo { Id = "R", Bounds = new PixelRect(0, 0, 1920, 1080), IsPrimary = true, IsConnected = true, IsIncluded = true }
        };

        var engine = new WallpaperGeometryEngine();
        var plan = engine.ComputePlan(monitors, 3840, 1080, srcPath, new PlacementSettings
        {
            Mode = WallpaperPlacementMode.PanoramicSpan,
            CoordinateSpace = GeometryCoordinateSpace.VirtualPixels
        });

        var compositor = new SkiaWallpaperCompositor();
        string outDir = Path.Combine(_tempDir, "out");
        var files = await compositor.RenderMonitorWallpapersAsync(plan, outDir, "#000000", OutputImageFormat.PngLossless);

        Assert.Equal(2, files.Count);
        using var leftOut = SKBitmap.Decode(files["L"]);
        using var rightOut = SKBitmap.Decode(files["R"]);

        Assert.Equal(1920, leftOut.Width);
        Assert.Equal(1080, leftOut.Height);
        Assert.Equal(1920, rightOut.Width);
        Assert.Equal(1080, rightOut.Height);

        // Left monitor top-right pixel must be Red, Right monitor top-left pixel must be Blue
        var lTop = leftOut.GetPixel(1918, 100);
        var rTop = rightOut.GetPixel(1, 100);
        Assert.True(lTop.Red > 240 && lTop.Blue < 15);
        Assert.True(rTop.Blue > 240 && rTop.Red < 15);

        // Both monitors have exact white band at Y=540 right across the seam
        var lMid = leftOut.GetPixel(1919, 540);
        var rMid = rightOut.GetPixel(0, 540);
        Assert.True(lMid.Red > 240 && lMid.Green > 240 && lMid.Blue > 240);
        Assert.True(rMid.Red > 240 && rMid.Green > 240 && rMid.Blue > 240);

        // Original source file must remain untouched
        Assert.True(File.Exists(srcPath));
        var (sw, sh) = SkiaWallpaperCompositor.ReadImageDimensions(srcPath);
        Assert.Equal((3840, 1080), (sw, sh));
    }

    [Fact]
    public async Task RenderMonitorWallpapers_FlipAndPanGapFill_WorkAccurately()
    {
        // Quadrant image: TL=Red, TR=Blue, BL=Green, BR=Yellow
        string srcPath = Path.Combine(_tempDir, "quad.png");
        using (var bmp = new SKBitmap(3840, 1080))
        using (var canvas = new SKCanvas(bmp))
        {
            canvas.DrawRect(0, 0, 1920, 540, new SKPaint { Color = SKColors.Red });
            canvas.DrawRect(1920, 0, 1920, 540, new SKPaint { Color = SKColors.Blue });
            canvas.DrawRect(0, 540, 1920, 540, new SKPaint { Color = SKColors.Lime });
            canvas.DrawRect(1920, 540, 1920, 540, new SKPaint { Color = SKColors.Yellow });
            using var img = SKImage.FromBitmap(bmp);
            using var data = img.Encode(SKEncodedImageFormat.Png, 100);
            await File.WriteAllBytesAsync(srcPath, data.ToArray());
        }

        var monitors = new[]
        {
            new MonitorInfo { Id = "L", Bounds = new PixelRect(-1920, 0, 1920, 1080), IsConnected = true, IsIncluded = true },
            new MonitorInfo { Id = "R", Bounds = new PixelRect(0, 0, 1920, 1080), IsPrimary = true, IsConnected = true, IsIncluded = true }
        };

        var engine = new WallpaperGeometryEngine();
        var compositor = new SkiaWallpaperCompositor();

        // 1. Test FlipHorizontal + FlipVertical
        var flippedPlan = engine.ComputePlan(monitors, 3840, 1080, srcPath, new PlacementSettings
        {
            Mode = WallpaperPlacementMode.PanoramicSpan,
            CoordinateSpace = GeometryCoordinateSpace.VirtualPixels,
            FlipHorizontal = true,
            FlipVertical = true
        });
        var flippedFiles = await compositor.RenderMonitorWallpapersAsync(
            flippedPlan, Path.Combine(_tempDir, "flipped"), "#000000", OutputImageFormat.PngLossless);

        using (var leftBmp = SKBitmap.Decode(flippedFiles["L"]))
        using (var rightBmp = SKBitmap.Decode(flippedFiles["R"]))
        {
            // TL of Left was BR (Yellow: R>240, G>240, B<15)
            var lTop = leftBmp.GetPixel(100, 100);
            Assert.True(lTop.Red > 240 && lTop.Green > 240 && lTop.Blue < 15);
            // TL of Right was BL (Lime: R<15, G>240, B<15)
            var rTop = rightBmp.GetPixel(100, 100);
            Assert.True(rTop.Red < 15 && rTop.Green > 240 && rTop.Blue < 15);
        }

        // 2. Test Pan leaving a gap filled with selected plain background color #33CC66
        var gapPlan = engine.ComputePlan(monitors, 3840, 1080, srcPath, new PlacementSettings
        {
            Mode = WallpaperPlacementMode.PanoramicSpan,
            CoordinateSpace = GeometryCoordinateSpace.VirtualPixels,
            PanX = 0.25, // shifts 960px right -> pixels 0..960 of Left monitor are uncovered gap
            BackgroundColorHex = "#33CC66"
        });
        var gapFiles = await compositor.RenderMonitorWallpapersAsync(
            gapPlan, Path.Combine(_tempDir, "gap"), "#33CC66", OutputImageFormat.PngLossless);

        using (var gapLeftBmp = SKBitmap.Decode(gapFiles["L"]))
        {
            var gapPx = gapLeftBmp.GetPixel(100, 200);
            Assert.Equal(0x33, gapPx.Red);
            Assert.Equal(0xCC, gapPx.Green);
            Assert.Equal(0x66, gapPx.Blue);

            // Wallpaper starts at X=960 -> pixel at X=1200 is Red (from TL)
            var wpPx = gapLeftBmp.GetPixel(1200, 200);
            Assert.True(wpPx.Red > 240 && wpPx.Green < 15 && wpPx.Blue < 15);
        }
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch { }
    }
}
