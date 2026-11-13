using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using LibVLCSharp.Shared;

namespace DuoWall.App.Windows;

public sealed class VideoFrameResult
{
    public string? StillImagePath { get; init; }
    public int Width { get; init; }
    public int Height { get; init; }
}

/// <summary>
/// Extracts a high-resolution crisp still PNG frame from local video wallpapers (AV1/VP9/HEVC/H.264)
/// using LibVLC offscreen decoding first, with Windows Shell and MediaPlayer fallbacks.
/// </summary>
public static class VideoFrameExtractor
{
    private static readonly Lazy<LibVLC?> ExtractorVlc = new(() =>
    {
        try
        {
            InitializeLibVlcCore();
            return new LibVLC("--no-audio", "--no-osd", "--no-video-title-show", "--avcodec-hw=none", "--quiet");
        }
        catch
        {
            return null;
        }
    });

    public static bool InitializeLibVlcCore()
    {
        try
        {
            string? searchDirs = AppContext.GetData("NATIVE_DLL_SEARCH_DIRECTORIES") as string;
            if (!string.IsNullOrWhiteSpace(searchDirs))
            {
                foreach (string dir in searchDirs.Split(';', StringSplitOptions.RemoveEmptyEntries))
                {
                    string vlcSub = Path.Combine(dir.Trim(), "libvlc", "win-x64");
                    if (File.Exists(Path.Combine(vlcSub, "libvlc.dll")))
                    {
                        LibVLCSharp.Shared.Core.Initialize(vlcSub);
                        return true;
                    }
                }
            }

            string localVlc = Path.Combine(AppContext.BaseDirectory, "libvlc", "win-x64");
            if (File.Exists(Path.Combine(localVlc, "libvlc.dll")))
            {
                LibVLCSharp.Shared.Core.Initialize(localVlc);
                return true;
            }

            LibVLCSharp.Shared.Core.Initialize();
            return true;
        }
        catch
        {
            return false;
        }
    }

    [ComImport]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    [Guid("BCC18B79-BA16-442F-80C4-8A59C30C463B")]
    private interface IShellItemImageFactory
    {
        [PreserveSig]
        int GetImage(SIZE size, int flags, out IntPtr phbm);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SIZE
    {
        public int cx;
        public int cy;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = true)]
    private static extern int SHCreateItemFromParsingName(
        string pszPath,
        IntPtr pbc,
        [MarshalAs(UnmanagedType.LPStruct)] Guid riid,
        [MarshalAs(UnmanagedType.Interface)] out IShellItemImageFactory? ppv);

    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeleteObject(IntPtr hObject);

    public static async Task<VideoFrameResult> ExtractStillFrameAsync(string videoPath)
    {
        if (string.IsNullOrWhiteSpace(videoPath) || !File.Exists(videoPath))
            return new VideoFrameResult();

        string stillPath = Path.ChangeExtension(videoPath, ".still.png");
        if (File.Exists(stillPath) && new FileInfo(stillPath).Length > 1024)
        {
            var dims = TryReadPngDimensions(stillPath);
            if (dims.Width > 0 && dims.Height > 0)
            {
                return new VideoFrameResult
                {
                    StillImagePath = stillPath,
                    Width = dims.Width,
                    Height = dims.Height
                };
            }
        }

        // Primary: Extract crisp frame natively from any codec (4K AV1, VP9, HEVC, H.264) via LibVLC offscreen decode
        var vlcRes = await Task.Run(() => ExtractViaLibVlc(videoPath, stillPath));
        if (!string.IsNullOrWhiteSpace(vlcRes.StillImagePath) && File.Exists(vlcRes.StillImagePath))
        {
            return vlcRes;
        }

        // Fallback 1: WPF MediaPlayer for compatible codecs
        var mediaRes = await ExtractViaMediaPlayerAsync(videoPath, stillPath);
        if (!string.IsNullOrWhiteSpace(mediaRes.StillImagePath) && File.Exists(mediaRes.StillImagePath))
        {
            return mediaRes;
        }

        // Fallback 2: Windows Shell IShellItemImageFactory
        var shellRes = TryExtractViaShellThumbnail(videoPath, stillPath);
        if (!string.IsNullOrWhiteSpace(shellRes.StillImagePath) && File.Exists(shellRes.StillImagePath))
        {
            return shellRes;
        }

        // Fallback 3: Sidecar YouTube poster image if present
        string thumbFallback = Path.ChangeExtension(videoPath, ".thumb.jpg");
        if (File.Exists(thumbFallback))
        {
            var d = TryReadPngDimensions(thumbFallback);
            return new VideoFrameResult
            {
                StillImagePath = thumbFallback,
                Width = d.Width > 0 ? d.Width : 1920,
                Height = d.Height > 0 ? d.Height : 1080
            };
        }

        return new VideoFrameResult();
    }

    private static VideoFrameResult ExtractViaLibVlc(string videoPath, string outStillPath)
    {
        var vlc = ExtractorVlc.Value;
        if (vlc == null) return new VideoFrameResult();

        IntPtr frameBuffer = IntPtr.Zero;
        object bufLock = new();
        int visibleW = 0, visibleH = 0, stride = 0, allocLines = 0;
        int frameCount = 0;
        bool savedBestFrame = false;
        byte[]? capturedPixels = null;
        int capturedW = 0, capturedH = 0, capturedStride = 0;
        var doneEvent = new ManualResetEventSlim(false);

        try
        {
            using var media = new Media(vlc, videoPath, FromType.FromPath);
            media.AddOption(":avcodec-hw=none");
            using var mp = new LibVLCSharp.Shared.MediaPlayer(media) { Mute = true };

            mp.SetVideoFormatCallbacks(
                (ref IntPtr opaque, IntPtr chroma, ref uint w, ref uint h, ref uint pitches, ref uint lines) =>
                {
                    byte[] rv32 = System.Text.Encoding.ASCII.GetBytes("RV32");
                    Marshal.Copy(rv32, 0, chroma, 4);

                    // Keep initial visible dimensions before macroblock 16px height alignment (e.g. 2160 vs 2176)
                    if (visibleW == 0)
                    {
                        visibleW = (int)w;
                        visibleH = (int)h;
                    }
                    else
                    {
                        visibleW = Math.Min(visibleW, (int)w);
                        visibleH = Math.Min(visibleH, (int)h);
                    }

                    int s = (int)w * 4;
                    pitches = (uint)s;
                    if (lines < h) lines = h;

                    lock (bufLock)
                    {
                        stride = s;
                        allocLines = (int)Math.Max(lines, h);
                        if (frameBuffer != IntPtr.Zero) Marshal.FreeHGlobal(frameBuffer);
                        frameBuffer = Marshal.AllocHGlobal(stride * allocLines);
                    }
                    return 1;
                },
                null);

            mp.SetVideoCallbacks(
                (IntPtr opaque, IntPtr planes) =>
                {
                    lock (bufLock)
                    {
                        Marshal.WriteIntPtr(planes, frameBuffer);
                    }
                    return IntPtr.Zero;
                },
                null,
                (IntPtr opaque, IntPtr picture) =>
                {
                    int n = Interlocked.Increment(ref frameCount);
                    if (savedBestFrame || n < 3 && frameBuffer == IntPtr.Zero)
                        return;

                    // Pick frame ~4th (or first non-black frame up to frame 10)
                    lock (bufLock)
                    {
                        if (savedBestFrame || frameBuffer == IntPtr.Zero || visibleW <= 0 || visibleH <= 0)
                            return;

                        int byteLen = stride * visibleH;
                        byte[] probe = new byte[Math.Min(4096, byteLen)];
                        Marshal.Copy(frameBuffer, probe, 0, probe.Length);
                        long sum = 0;
                        for (int i = 0; i < probe.Length; i += 4)
                            sum += probe[i] + probe[i + 1] + probe[i + 2];

                        if ((n >= 4 && sum > 500) || n >= 12)
                        {
                            capturedPixels = new byte[byteLen];
                            Marshal.Copy(frameBuffer, capturedPixels, 0, byteLen);
                            capturedW = visibleW;
                            capturedH = visibleH;
                            capturedStride = stride;
                            savedBestFrame = true;
                            doneEvent.Set();
                        }
                    }
                });

            mp.Play();
            // Nudge forward slightly so we avoid potential black fade-in at 0.00s
            Thread.Sleep(150);
            try { if (mp.Length > 2000) mp.Time = 600; } catch { }

            doneEvent.Wait(4500);
            try { mp.Stop(); } catch { }

            if (capturedPixels != null && capturedW > 0 && capturedH > 0)
            {
                var bmp = BitmapSource.Create(
                    capturedW, capturedH, 96, 96,
                    PixelFormats.Bgr32, null, capturedPixels, capturedStride);

                // Downscale softly if wider than 2560px so Placement Studio renders smoothly
                BitmapSource finalBmp = bmp;
                if (capturedW > 2560)
                {
                    double scale = 2560.0 / capturedW;
                    finalBmp = new TransformedBitmap(bmp, new ScaleTransform(scale, scale));
                }

                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(finalBmp));
                using (var fs = File.Create(outStillPath))
                    encoder.Save(fs);

                return new VideoFrameResult
                {
                    StillImagePath = outStillPath,
                    Width = finalBmp.PixelWidth,
                    Height = finalBmp.PixelHeight
                };
            }
        }
        catch
        {
            return new VideoFrameResult();
        }
        finally
        {
            lock (bufLock)
            {
                if (frameBuffer != IntPtr.Zero)
                {
                    Marshal.FreeHGlobal(frameBuffer);
                    frameBuffer = IntPtr.Zero;
                }
            }
        }

        return new VideoFrameResult();
    }

    private static async Task<VideoFrameResult> ExtractViaMediaPlayerAsync(string videoPath, string outStillPath)
    {
        if (Application.Current?.Dispatcher == null)
            return new VideoFrameResult();

        var tcs = new TaskCompletionSource<VideoFrameResult>();

        await Application.Current.Dispatcher.InvokeAsync(async () =>
        {
            System.Windows.Media.MediaPlayer? player = null;
            try
            {
                player = new System.Windows.Media.MediaPlayer
                {
                    IsMuted = true,
                    ScrubbingEnabled = true
                };

                var openedTcs = new TaskCompletionSource<bool>();
                player.MediaOpened += (_, _) => openedTcs.TrySetResult(true);
                player.MediaFailed += (_, _) => openedTcs.TrySetResult(false);

                player.Open(new UriBuilder("file", string.Empty, 0, videoPath).Uri);

                var timeoutTask = Task.Delay(2200);
                var finished = await Task.WhenAny(openedTcs.Task, timeoutTask);
                if (finished != openedTcs.Task || !openedTcs.Task.Result)
                {
                    tcs.TrySetResult(new VideoFrameResult());
                    return;
                }

                int vw = player.NaturalVideoWidth > 0 ? player.NaturalVideoWidth : 1920;
                int vh = player.NaturalVideoHeight > 0 ? player.NaturalVideoHeight : 1080;

                if (player.NaturalDuration.HasTimeSpan)
                {
                    var dur = player.NaturalDuration.TimeSpan;
                    player.Position = TimeSpan.FromSeconds(Math.Clamp(dur.TotalSeconds * 0.15, 0.4, 3.0));
                }
                player.Play();
                await Task.Delay(280);
                player.Pause();
                await Task.Delay(100);

                int outW = Math.Min(vw, 2560);
                int outH = Math.Max(64, (int)Math.Round(vh * (outW / (double)vw)));

                var dv = new DrawingVisual();
                using (var dc = dv.RenderOpen())
                {
                    dc.DrawVideo(player, new Rect(0, 0, outW, outH));
                }

                var rtb = new RenderTargetBitmap(outW, outH, 96, 96, PixelFormats.Pbgra32);
                rtb.Render(dv);

                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(rtb));
                await using (var fs = File.Create(outStillPath))
                {
                    encoder.Save(fs);
                }

                tcs.TrySetResult(new VideoFrameResult
                {
                    StillImagePath = outStillPath,
                    Width = outW,
                    Height = outH
                });
            }
            catch
            {
                tcs.TrySetResult(new VideoFrameResult());
            }
            finally
            {
                try { player?.Close(); } catch { }
            }
        }, DispatcherPriority.Background);

        return await tcs.Task;
    }

    private static VideoFrameResult TryExtractViaShellThumbnail(string videoPath, string outStillPath)
    {
        try
        {
            Guid iid = new("BCC18B79-BA16-442F-80C4-8A59C30C463B");
            if (SHCreateItemFromParsingName(videoPath, IntPtr.Zero, iid, out var factory) != 0 || factory == null)
                return new VideoFrameResult();

            try
            {
                int hr = factory.GetImage(new SIZE { cx = 1920, cy = 1080 }, 0x09, out IntPtr hBitmap);
                if (hr != 0 || hBitmap == IntPtr.Zero)
                    return new VideoFrameResult();

                try
                {
                    var bmpSource = Imaging.CreateBitmapSourceFromHBitmap(
                        hBitmap,
                        IntPtr.Zero,
                        Int32Rect.Empty,
                        BitmapSizeOptions.FromEmptyOptions());

                    var encoder = new PngBitmapEncoder();
                    encoder.Frames.Add(BitmapFrame.Create(bmpSource));
                    using (var fs = File.Create(outStillPath))
                        encoder.Save(fs);

                    return new VideoFrameResult
                    {
                        StillImagePath = outStillPath,
                        Width = bmpSource.PixelWidth,
                        Height = bmpSource.PixelHeight
                    };
                }
                finally
                {
                    DeleteObject(hBitmap);
                }
            }
            finally
            {
                Marshal.ReleaseComObject(factory);
            }
        }
        catch
        {
            return new VideoFrameResult();
        }
    }

    private static (int Width, int Height) TryReadPngDimensions(string imagePath)
    {
        try
        {
            using var fs = File.OpenRead(imagePath);
            var decoder = BitmapDecoder.Create(fs, BitmapCreateOptions.DelayCreation, BitmapCacheOption.None);
            if (decoder.Frames.Count > 0)
                return (decoder.Frames[0].PixelWidth, decoder.Frames[0].PixelHeight);
        }
        catch { }
        return (0, 0);
    }
}
