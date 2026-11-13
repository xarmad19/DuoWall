using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using SkiaSharp;

namespace DuoWall.App.Helpers;

/// <summary>
/// Extracts frozen WPF BitmapSource icons from local executables, folders, or custom user images
/// via Win32 Shell32 without WinForms dependencies, plus crisp vector glyphs for Glassmorphism docks.
/// </summary>
public static class IconExtractorHelper
{
    private static readonly ConcurrentDictionary<string, ImageSource?> IconCache = new(StringComparer.OrdinalIgnoreCase);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr ExtractAssociatedIcon(IntPtr hInst, StringBuilder lpIconPath, out ushort lpiIcon);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr SHGetFileInfo(string pszPath, uint dwFileAttributes, ref SHFILEINFO psfi, uint cbSizeFileInfo, uint uFlags);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr SHGetFileInfo(IntPtr pidl, uint dwFileAttributes, ref SHFILEINFO psfi, uint cbSizeFileInfo, uint uFlags);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHParseDisplayName(string pszName, IntPtr pbc, out IntPtr ppidl, uint sfgaoIn, out uint psfgaoOut);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyIcon(IntPtr hIcon);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct SHFILEINFO
    {
        public IntPtr hIcon;
        public int iIcon;
        public uint dwAttributes;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string szDisplayName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)]
        public string szTypeName;
    }

    private const uint SHGFI_ICON = 0x000000100;
    private const uint SHGFI_PIDL = 0x000000008;
    private const uint SHGFI_LARGEICON = 0x000000000;
    private const uint FILE_ATTRIBUTE_DIRECTORY = 0x00000010;

    public static ImageSource? LoadCustomIconImage(string? imagePath)
    {
        if (string.IsNullOrWhiteSpace(imagePath) || !File.Exists(imagePath))
            return null;

        long writeTicks;
        try { writeTicks = File.GetLastWriteTimeUtc(imagePath).Ticks; }
        catch { writeTicks = 0; }

        string key = $"custom:{imagePath}:{writeTicks}";
        if (IconCache.TryGetValue(key, out var cached) && cached != null)
            return cached;

        ImageSource? loaded = TryDecodeCustomIconImage(imagePath);
        if (loaded != null)
            IconCache[key] = loaded;

        return loaded;
    }

    private static ImageSource? TryDecodeCustomIconImage(string imagePath)
    {
        byte[] bytes;
        try { bytes = File.ReadAllBytes(imagePath); }
        catch { return null; }

        if (bytes.Length == 0) return null;

        // Primary path: WPF BitmapDecoder (supports PNG, JPG, BMP, GIF, and multi-resolution ICO cleanly)
        try
        {
            using var ms = new MemoryStream(bytes, writable: false);
            var decoder = BitmapDecoder.Create(ms, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
            BitmapFrame? bestFrame = decoder.Frames
                .Where(f => f.PixelWidth > 0 && f.PixelHeight > 0)
                .OrderByDescending(f => f.PixelWidth <= 256 ? f.PixelWidth : 256 - (f.PixelWidth / 256))
                .FirstOrDefault();

            if (bestFrame != null)
            {
                BitmapSource result = bestFrame;
                int maxDim = Math.Max(bestFrame.PixelWidth, bestFrame.PixelHeight);
                if (maxDim > 128)
                {
                    double scale = 96.0 / maxDim;
                    result = new TransformedBitmap(bestFrame, new ScaleTransform(scale, scale));
                }
                if (result.CanFreeze) result.Freeze();
                return result;
            }
        }
        catch { }

        // Fallback path: SkiaSharp decode (supports WEBP and non-WIC image variants)
        try
        {
            using var skBmp = SKBitmap.Decode(bytes);
            if (skBmp != null && skBmp.Width > 0 && skBmp.Height > 0)
            {
                int maxDim = Math.Max(skBmp.Width, skBmp.Height);
                SKBitmap target = skBmp;
                SKBitmap? resized = null;
                if (maxDim > 128)
                {
                    double scale = 96.0 / maxDim;
                    int nw = Math.Max(1, (int)Math.Round(skBmp.Width * scale));
                    int nh = Math.Max(1, (int)Math.Round(skBmp.Height * scale));
                    resized = skBmp.Resize(new SKImageInfo(nw, nh), SKFilterQuality.High);
                    if (resized != null) target = resized;
                }

                try
                {
                    using var skImg = SKImage.FromBitmap(target);
                    using var encoded = skImg.Encode(SKEncodedImageFormat.Png, 100);
                    if (encoded != null)
                    {
                        using var pngStream = new MemoryStream(encoded.ToArray(), writable: false);
                        var frame = BitmapFrame.Create(pngStream, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
                        if (frame.CanFreeze) frame.Freeze();
                        return frame;
                    }
                }
                finally
                {
                    resized?.Dispose();
                }
            }
        }
        catch { }

        return null;
    }

    public static ImageSource? TryExtractAppIcon(string targetPathOrUrl)
    {
        if (string.IsNullOrWhiteSpace(targetPathOrUrl))
            return null;

        if (IconCache.TryGetValue(targetPathOrUrl, out var cached))
            return cached;

        ImageSource? result = null;
        try
        {
            string resolved = Environment.ExpandEnvironmentVariables(targetPathOrUrl.Trim().Trim('"'));
            if (resolved.StartsWith("shell:", StringComparison.OrdinalIgnoreCase))
            {
                if (SHParseDisplayName(resolved, IntPtr.Zero, out IntPtr pidl, 0, out _) == 0 && pidl != IntPtr.Zero)
                {
                    try
                    {
                        var shfi = new SHFILEINFO();
                        IntPtr res = SHGetFileInfo(pidl, 0, ref shfi, (uint)Marshal.SizeOf<SHFILEINFO>(), SHGFI_PIDL | SHGFI_ICON | SHGFI_LARGEICON);
                        if (res != IntPtr.Zero && shfi.hIcon != IntPtr.Zero)
                        {
                            try
                            {
                                var bs = Imaging.CreateBitmapSourceFromHIcon(shfi.hIcon, Int32Rect.Empty, BitmapSizeOptions.FromWidthAndHeight(64, 64));
                                bs.Freeze();
                                result = bs;
                            }
                            finally { DestroyIcon(shfi.hIcon); }
                        }
                    }
                    finally
                    {
                        Marshal.FreeCoTaskMem(pidl);
                    }
                }
                if (result != null)
                    IconCache[targetPathOrUrl] = result;
                return result;
            }

            if (Directory.Exists(resolved))
            {
                var shfi = new SHFILEINFO();
                IntPtr res = SHGetFileInfo(resolved, FILE_ATTRIBUTE_DIRECTORY, ref shfi, (uint)Marshal.SizeOf<SHFILEINFO>(), SHGFI_ICON | SHGFI_LARGEICON);
                if (res != IntPtr.Zero && shfi.hIcon != IntPtr.Zero)
                {
                    try
                    {
                        var bs = Imaging.CreateBitmapSourceFromHIcon(shfi.hIcon, Int32Rect.Empty, BitmapSizeOptions.FromWidthAndHeight(48, 48));
                        bs.Freeze();
                        result = bs;
                    }
                    finally
                    {
                        DestroyIcon(shfi.hIcon);
                    }
                }
                IconCache[targetPathOrUrl] = result;
                return result;
            }

            if (!File.Exists(resolved))
            {
                string sys = Path.Combine(Environment.SystemDirectory, resolved);
                if (File.Exists(sys))
                    resolved = sys;
                else
                {
                    string? pathVar = Environment.GetEnvironmentVariable("PATH");
                    if (!string.IsNullOrWhiteSpace(pathVar))
                    {
                        foreach (var dir in pathVar.Split(';'))
                        {
                            if (string.IsNullOrWhiteSpace(dir)) continue;
                            string cand = Path.Combine(dir.Trim(), resolved);
                            if (File.Exists(cand)) { resolved = cand; break; }
                        }
                    }
                }
            }

            if (File.Exists(resolved))
            {
                // Prefer SHGetFileInfo first so .lnk Start Menu shortcuts keep their custom icon rather than generic stub
                var shfi = new SHFILEINFO();
                IntPtr res = SHGetFileInfo(resolved, 0, ref shfi, (uint)Marshal.SizeOf<SHFILEINFO>(), SHGFI_ICON | SHGFI_LARGEICON);
                if (res != IntPtr.Zero && shfi.hIcon != IntPtr.Zero)
                {
                    try
                    {
                        var bs = Imaging.CreateBitmapSourceFromHIcon(
                            shfi.hIcon,
                            Int32Rect.Empty,
                            BitmapSizeOptions.FromWidthAndHeight(48, 48));
                        bs.Freeze();
                        result = bs;
                    }
                    finally
                    {
                        DestroyIcon(shfi.hIcon);
                    }
                }

                if (result == null)
                {
                    var sb = new StringBuilder(resolved, 260);
                    IntPtr hIcon = ExtractAssociatedIcon(IntPtr.Zero, sb, out _);
                    if (hIcon != IntPtr.Zero)
                    {
                        try
                        {
                            var bs = Imaging.CreateBitmapSourceFromHIcon(
                                hIcon,
                                Int32Rect.Empty,
                                BitmapSizeOptions.FromWidthAndHeight(48, 48));
                            bs.Freeze();
                            result = bs;
                        }
                        finally
                        {
                            DestroyIcon(hIcon);
                        }
                    }
                }
            }
        }
        catch { }

        IconCache[targetPathOrUrl] = result;
        return result;
    }

    public static readonly IReadOnlyList<string> AvailableGlyphKeys = new[]
    {
        "Globe", "Play", "Code", "Terminal", "Folder", "Document", "Mail", "Music", "Settings", "Star"
    };

    public static string GetVectorGlyphData(string glyphKey) => glyphKey switch
    {
        "Play" => "M8,5 L19,12 L8,19 Z",
        "Code" => "M9.4,16.6 L4.8,12 L9.4,7.4 L8,6 L2,12 L8,18 Z M14.6,16.6 L19.2,12 L14.6,7.4 L16,6 L22,12 L16,18 Z",
        "Terminal" => "M4,6 L20,6 A2,2 0 0 1 22,8 L22,18 A2,2 0 0 1 20,20 L4,20 A2,2 0 0 1 2,18 L2,8 A2,2 0 0 1 4,6 Z M6,10 L10,13 L6,16 M12,16 L17,16",
        "Folder" => "M10,4 L12,6 L20,6 A2,2 0 0 1 22,8 L22,18 A2,2 0 0 1 20,20 L4,20 A2,2 0 0 1 2,18 L2,6 A2,2 0 0 1 4,4 Z",
        "Document" => "M6,2 L14,2 L20,8 L20,20 A2,2 0 0 1 18,22 L6,22 A2,2 0 0 1 4,20 L4,4 A2,2 0 0 1 6,2 Z M14,2 L14,8 L20,8",
        "Mail" => "M4,6 L20,6 A2,2 0 0 1 22,8 L22,16 A2,2 0 0 1 20,18 L4,18 A2,2 0 0 1 2,16 L2,8 A2,2 0 0 1 4,6 Z M2,8 L12,13 L22,8",
        "Music" => "M9,18 L9,6 L20,4 L20,16 M9,18 A3,3 0 1 1 6,15 A3,3 0 0 1 9,18 Z M20,16 A3,3 0 1 1 17,13 A3,3 0 0 1 20,16 Z",
        "Settings" => "M12,8 A4,4 0 1 0 12,16 A4,4 0 1 0 12,8 Z M12,2 L13.5,5 M12,19 L13.5,22 M4.9,4.9 L7,7 M17,17 L19.1,19.1 M2,12 L5,12 M19,12 L22,12 M4.9,19.1 L7,17 M17,7 L19.1,4.9",
        "Star" => "M12,2 L14.9,8.6 L22,9.3 L16.6,14 L18.2,21 L12,17.3 L5.8,21 L7.4,14 L2,9.3 L9.1,8.6 Z",
        _ => "M12,2 A10,10 0 1 0 12,22 A10,10 0 1 0 12,2 Z M2,12 L22,12 M12,2 A15.3,15.3 0 0 1 16,12 A15.3,15.3 0 0 1 12,22 A15.3,15.3 0 0 1 8,12 A15.3,15.3 0 0 1 12,2 Z"
    };
}
