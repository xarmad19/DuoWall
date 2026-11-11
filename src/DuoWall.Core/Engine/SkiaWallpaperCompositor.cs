using System.Security.Cryptography;
using System.Text;
using DuoWall.Core.Models;
using SkiaSharp;

namespace DuoWall.Core.Engine;

/// <summary>
/// High-precision SkiaSharp compositor that renders individual monitor wallpapers
/// directly from the full-resolution source image with EXIF orientation support and
/// high-quality cubic resampling.
/// </summary>
public sealed class SkiaWallpaperCompositor
{
    /// <summary>
    /// Reads exact image pixel dimensions (accounting for EXIF orientation) without keeping pixels in memory.
    /// </summary>
    public static (int Width, int Height) ReadImageDimensions(string imagePath)
    {
        if (!File.Exists(imagePath))
            throw new FileNotFoundException("Wallpaper source image not found.", imagePath);

        using var stream = File.OpenRead(imagePath);
        using var codec = SKCodec.Create(stream);
        if (codec == null)
            throw new InvalidDataException($"Unsupported or corrupted image file: {imagePath}");

        var info = codec.Info;
        return IsOrientationTransposed(codec.EncodedOrigin)
            ? (info.Height, info.Width)
            : (info.Width, info.Height);
    }

    /// <summary>
    /// Loads a decoded SKBitmap with EXIF orientation normalized upright.
    /// </summary>
    public static SKBitmap LoadOrientedBitmap(string imagePath)
    {
        if (!File.Exists(imagePath))
            throw new FileNotFoundException("Wallpaper source image not found.", imagePath);

        using var stream = File.OpenRead(imagePath);
        using var codec = SKCodec.Create(stream);
        if (codec == null)
            throw new InvalidDataException($"Unsupported or corrupted image file: {imagePath}");

        var info = new SKImageInfo(codec.Info.Width, codec.Info.Height, SKColorType.Bgra8888, SKAlphaType.Premul);
        var bitmap = new SKBitmap(info);
        var result = codec.GetPixels(info, bitmap.GetPixels());
        if (result != SKCodecResult.Success && result != SKCodecResult.IncompleteInput)
        {
            bitmap.Dispose();
            throw new InvalidDataException($"Failed to decode image pixels ({result}): {imagePath}");
        }

        return ApplyExifOrientation(bitmap, codec.EncodedOrigin);
    }

    /// <summary>
    /// Generates one output wallpaper file per monitor defined in the CompositionPlan.
    /// Never modifies or overwrites the original source wallpaper file.
    /// </summary>
    public async Task<IReadOnlyDictionary<string, string>> RenderMonitorWallpapersAsync(
        CompositionPlan plan,
        string outputDirectory,
        string backgroundColorHex,
        OutputImageFormat outputFormat,
        CancellationToken cancellationToken = default)
    {
        return await Task.Run(() =>
        {
            Directory.CreateDirectory(outputDirectory);
            var bgColor = ParseHexColor(backgroundColorHex);
            var generated = new Dictionary<string, string>();
            var decodedSources = new Dictionary<string, SKImage>(StringComparer.OrdinalIgnoreCase);

            try
            {
                string ext = outputFormat == OutputImageFormat.PngLossless ? "png" : "jpg";
                var skFormat = outputFormat == OutputImageFormat.PngLossless
                    ? SKEncodedImageFormat.Png
                    : SKEncodedImageFormat.Jpeg;
                int quality = outputFormat == OutputImageFormat.PngLossless ? 100 : 96;

                foreach (var slice in plan.Slices)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    SKImage? srcImage = null;
                    if (!slice.DestinationPixelRect.IsEmpty &&
                        !slice.SourcePixelRect.IsEmpty &&
                        !string.IsNullOrWhiteSpace(slice.SourceImagePath) &&
                        File.Exists(slice.SourceImagePath))
                    {
                        if (!decodedSources.TryGetValue(slice.SourceImagePath, out srcImage))
                        {
                            using var bmp = LoadOrientedBitmap(slice.SourceImagePath);
                            srcImage = SKImage.FromBitmap(bmp);
                            decodedSources[slice.SourceImagePath] = srcImage;
                        }
                    }

                    using var surface = SKSurface.Create(new SKImageInfo(
                        Math.Max(1, slice.OutputWidth),
                        Math.Max(1, slice.OutputHeight),
                        SKColorType.Bgra8888,
                        SKAlphaType.Opaque));

                    var canvas = surface.Canvas;
                    canvas.Clear(bgColor);

                    if (srcImage != null && !slice.DestinationPixelRect.IsEmpty && !slice.SourcePixelRect.IsEmpty)
                    {
                        var srcRect = new SKRect(
                            (float)slice.SourcePixelRect.X,
                            (float)slice.SourcePixelRect.Y,
                            (float)slice.SourcePixelRect.Right,
                            (float)slice.SourcePixelRect.Bottom);

                        var dstRect = new SKRect(
                            (float)slice.DestinationPixelRect.X,
                            (float)slice.DestinationPixelRect.Y,
                            (float)slice.DestinationPixelRect.Right,
                            (float)slice.DestinationPixelRect.Bottom);

                        using var paint = new SKPaint
                        {
                            IsAntialias = true,
                            FilterQuality = SKFilterQuality.High
                        };
                        canvas.DrawImage(srcImage, srcRect, dstRect, paint);
                    }

                    canvas.Flush();

                    string safeId = SanitizeFileName(slice.Monitor.Id);
                    string hash = ComputeShortHash($"{slice.Monitor.Id}|{slice.OutputWidth}x{slice.OutputHeight}|{DateTime.UtcNow.Ticks}");
                    string outPath = Path.Combine(outputDirectory, $"duowall_{safeId}_{hash}.{ext}");

                    using var snapshot = surface.Snapshot();
                    using var encoded = snapshot.Encode(skFormat, quality);
                    using var fs = File.Create(outPath);
                    encoded.SaveTo(fs);

                    generated[slice.Monitor.Id] = outPath;
                }

                // Clean up older generated files (> 30) so disk usage remains bounded
                CleanupOldGeneratedFiles(outputDirectory, generated.Values);

                return (IReadOnlyDictionary<string, string>)generated;
            }
            finally
            {
                foreach (var img in decodedSources.Values)
                    img.Dispose();
            }
        }, cancellationToken);
    }

    public static SKColor ParseHexColor(string? hex)
    {
        if (string.IsNullOrWhiteSpace(hex))
            return new SKColor(13, 16, 23);

        string clean = hex.Trim().TrimStart('#');
        if (clean.Length == 6 &&
            byte.TryParse(clean[0..2], System.Globalization.NumberStyles.HexNumber, null, out byte r) &&
            byte.TryParse(clean[2..4], System.Globalization.NumberStyles.HexNumber, null, out byte g) &&
            byte.TryParse(clean[4..6], System.Globalization.NumberStyles.HexNumber, null, out byte b))
        {
            return new SKColor(r, g, b);
        }

        if (clean.Length == 8 &&
            byte.TryParse(clean[0..2], System.Globalization.NumberStyles.HexNumber, null, out byte a) &&
            byte.TryParse(clean[2..4], System.Globalization.NumberStyles.HexNumber, null, out byte r8) &&
            byte.TryParse(clean[4..6], System.Globalization.NumberStyles.HexNumber, null, out byte g8) &&
            byte.TryParse(clean[6..8], System.Globalization.NumberStyles.HexNumber, null, out byte b8))
        {
            return new SKColor(r8, g8, b8, a);
        }

        return new SKColor(13, 16, 23);
    }

    private static bool IsOrientationTransposed(SKEncodedOrigin origin) => origin is
        SKEncodedOrigin.LeftTop or SKEncodedOrigin.RightTop or
        SKEncodedOrigin.RightBottom or SKEncodedOrigin.LeftBottom;

    private static SKBitmap ApplyExifOrientation(SKBitmap src, SKEncodedOrigin origin)
    {
        if (origin == SKEncodedOrigin.TopLeft || origin == SKEncodedOrigin.Default)
            return src;

        bool transposed = IsOrientationTransposed(origin);
        int dstW = transposed ? src.Height : src.Width;
        int dstH = transposed ? src.Width : src.Height;

        var dst = new SKBitmap(dstW, dstH, src.ColorType, src.AlphaType);
        using (var canvas = new SKCanvas(dst))
        {
            switch (origin)
            {
                case SKEncodedOrigin.TopRight:
                    canvas.Scale(-1, 1, dstW * 0.5f, dstH * 0.5f);
                    break;
                case SKEncodedOrigin.BottomRight:
                    canvas.RotateDegrees(180, dstW * 0.5f, dstH * 0.5f);
                    break;
                case SKEncodedOrigin.BottomLeft:
                    canvas.Scale(1, -1, dstW * 0.5f, dstH * 0.5f);
                    break;
                case SKEncodedOrigin.LeftTop:
                    canvas.Translate(dstW, 0);
                    canvas.RotateDegrees(90);
                    canvas.Scale(1, -1);
                    canvas.Translate(0, -src.Height);
                    break;
                case SKEncodedOrigin.RightTop:
                    canvas.Translate(dstW, 0);
                    canvas.RotateDegrees(90);
                    break;
                case SKEncodedOrigin.RightBottom:
                    canvas.Translate(dstW, 0);
                    canvas.RotateDegrees(90);
                    canvas.Scale(-1, 1, src.Width * 0.5f, src.Height * 0.5f);
                    break;
                case SKEncodedOrigin.LeftBottom:
                    canvas.Translate(0, dstH);
                    canvas.RotateDegrees(270);
                    break;
            }
            canvas.DrawBitmap(src, 0, 0);
        }

        src.Dispose();
        return dst;
    }

    private static void CleanupOldGeneratedFiles(string outputDirectory, IEnumerable<string> keepFiles)
    {
        try
        {
            var keepSet = new HashSet<string>(keepFiles, StringComparer.OrdinalIgnoreCase);
            var oldFiles = Directory.GetFiles(outputDirectory, "duowall_*.*")
                .Where(f => !keepSet.Contains(f))
                .Select(f => new FileInfo(f))
                .OrderByDescending(f => f.LastWriteTimeUtc)
                .Skip(20);

            foreach (var f in oldFiles)
            {
                try { f.Delete(); } catch { /* ignore locked file */ }
            }
        }
        catch { /* best effort cleanup */ }
    }

    private static string SanitizeFileName(string input)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var sb = new StringBuilder(input.Length);
        foreach (char c in input)
        {
            sb.Append(invalid.Contains(c) || c == '\\' || c == '/' ? '_' : c);
        }
        return sb.ToString();
    }

    private static string ComputeShortHash(string value)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return Convert.ToHexString(bytes)[..10].ToLowerInvariant();
    }
}
