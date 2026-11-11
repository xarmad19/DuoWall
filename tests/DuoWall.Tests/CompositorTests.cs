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

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch { }
    }
}
