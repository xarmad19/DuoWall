using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using DuoWall.Core.Models;
using LibVLCSharp.Shared;

namespace DuoWall.App.Windows;

/// <summary>
/// Renders a single smooth looping video wallpaper (4K AV1, VP9, HEVC, or H.264) across connected displays,
/// mapped onto the exact per-monitor Placement Studio <see cref="CompositionPlan"/> geometry
/// (Zoom, Pan, Crop, Flip H/V, Bezel offsets, Fit/Fill/Span, and Gap Background Color).
/// </summary>
public partial class PanoramicVideoBackdropWindow : Window
{
    private static readonly Lazy<LibVLC?> PlaybackVlc = new(() =>
    {
        try
        {
            VideoFrameExtractor.InitializeLibVlcCore();
            return new LibVLC("--no-audio", "--no-osd", "--no-video-title-show", "--avcodec-hw=none", "--quiet");
        }
        catch
        {
            return null;
        }
    });

    private IntPtr _hwnd = IntPtr.Zero;
    private string? _currentSource;
    private bool _loopEnabled = true;
    private bool _userPaused;
    private bool _systemPaused;
    private CompositionPlan? _lastPlan;
    private string _gapColorHex = "#0D1017";
    private double _winDipLeft;
    private double _winDipTop;

    // LibVLC double-buffered offscreen video surface
    private LibVLCSharp.Shared.Media? _vlcMedia;
    private LibVLCSharp.Shared.MediaPlayer? _vlcPlayer;
    private readonly object _bufferLock = new();
    private IntPtr _writePtr = IntPtr.Zero;
    private IntPtr _readyPtr = IntPtr.Zero;
    private int _visibleWidth;
    private int _visibleHeight;
    private int _stride;
    private int _allocLines;
    private WriteableBitmap? _videoBitmap;
    private int _uiRenderQueued;
    private bool _usingFallbackMediaElement;

    public bool IsEffectivelyPlaying { get; private set; }
    public string StatusSummary { get; private set; } = "Idle";

    public PanoramicVideoBackdropWindow()
    {
        InitializeComponent();
        Closed += (_, _) => StopAndDisposeVlc();
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        _hwnd = new WindowInteropHelper(this).Handle;
        GlassBackdropHelper.ConfigureDesktopOverlayWindow(_hwnd, excludeFromAeroPeek: false);
        GlassBackdropHelper.SetClickThrough(_hwnd, true);
        GlassBackdropHelper.SendToBottomZOrder(_hwnd);
    }

    public void Configure(
        double dipLeft,
        double dipTop,
        double dipWidth,
        double dipHeight,
        string? videoPath,
        bool loopEnabled,
        CompositionPlan? plan = null,
        string? backgroundColorHex = null)
    {
        _winDipLeft = dipLeft;
        _winDipTop = dipTop;
        Left = dipLeft;
        Top = dipTop;
        Width = Math.Max(320, dipWidth);
        Height = Math.Max(200, dipHeight);
        _loopEnabled = loopEnabled;
        _lastPlan = plan;
        _gapColorHex = string.IsNullOrWhiteSpace(backgroundColorHex) ? "#0D1017" : backgroundColorHex;

        Color bg = ParseColor(_gapColorHex);
        GapBackgroundLayer.Background = new SolidColorBrush(bg);
        RootGrid.Background = new SolidColorBrush(bg);

        if (!string.IsNullOrWhiteSpace(videoPath) && File.Exists(videoPath))
        {
            if (!string.Equals(_currentSource, videoPath, StringComparison.OrdinalIgnoreCase))
            {
                _currentSource = videoPath;
                LoadVideoSource(videoPath);
            }

            RebuildMonitorVideoSlices();
            ApplyPlaybackState("Ready");
        }
        else
        {
            _currentSource = null;
            StopPlaybackCleanly();
            MonitorSliceCanvas.Children.Clear();
            IsEffectivelyPlaying = false;
            StatusSummary = "No video file selected";
        }

        if (_hwnd != IntPtr.Zero)
            GlassBackdropHelper.SendToBottomZOrder(_hwnd);
    }

    public void SetSystemPause(bool paused, string reason)
    {
        _systemPaused = paused;
        ApplyPlaybackState(reason);
    }

    public void ToggleUserPause()
    {
        _userPaused = !_userPaused;
        ApplyPlaybackState(_userPaused ? "Paused by user" : "Looping continuously");
    }

    private void LoadVideoSource(string videoPath)
    {
        StopPlaybackCleanly();

        var vlc = PlaybackVlc.Value;
        if (vlc != null)
        {
            try
            {
                _usingFallbackMediaElement = false;
                VideoPlayer.Source = null;

                _vlcMedia = new LibVLCSharp.Shared.Media(vlc, videoPath, FromType.FromPath);
                _vlcMedia.AddOption(":avcodec-hw=none");
                _vlcPlayer = new LibVLCSharp.Shared.MediaPlayer(_vlcMedia) { Mute = true };

                _vlcPlayer.SetVideoFormatCallbacks(OnVlcVideoFormat, null);
                _vlcPlayer.SetVideoCallbacks(OnVlcLock, null, OnVlcDisplay);
                _vlcPlayer.EndReached += OnVlcEndReached;
                _vlcPlayer.EncounteredError += OnVlcEncounteredError;
                return;
            }
            catch
            {
                StopAndDisposeVlc();
            }
        }

        // Fallback to WPF MediaElement using safe encoded file URI (prevents '#' truncation)
        _usingFallbackMediaElement = true;
        VideoPlayer.Source = new UriBuilder("file", string.Empty, 0, videoPath).Uri;
    }

    private uint OnVlcVideoFormat(ref IntPtr opaque, IntPtr chroma, ref uint w, ref uint h, ref uint pitches, ref uint lines)
    {
        byte[] rv32 = System.Text.Encoding.ASCII.GetBytes("RV32");
        Marshal.Copy(rv32, 0, chroma, 4);

        lock (_bufferLock)
        {
            if (_visibleWidth == 0 || _visibleHeight == 0)
            {
                _visibleWidth = (int)w;
                _visibleHeight = (int)h;
            }
            else
            {
                _visibleWidth = Math.Min(_visibleWidth, (int)w);
                _visibleHeight = Math.Min(_visibleHeight, (int)h);
            }

            int newStride = (int)w * 4;
            int neededLines = (int)Math.Max(lines, h);
            pitches = (uint)newStride;
            lines = (uint)neededLines;

            if (newStride != _stride || neededLines != _allocLines || _writePtr == IntPtr.Zero || _readyPtr == IntPtr.Zero)
            {
                if (_writePtr != IntPtr.Zero) Marshal.FreeHGlobal(_writePtr);
                if (_readyPtr != IntPtr.Zero) Marshal.FreeHGlobal(_readyPtr);
                _stride = newStride;
                _allocLines = neededLines;
                int bytes = _stride * _allocLines;
                _writePtr = Marshal.AllocHGlobal(bytes);
                _readyPtr = Marshal.AllocHGlobal(bytes);
            }
        }

        return 1;
    }

    private IntPtr OnVlcLock(IntPtr opaque, IntPtr planes)
    {
        lock (_bufferLock)
        {
            Marshal.WriteIntPtr(planes, _writePtr);
        }
        return IntPtr.Zero;
    }

    private void OnVlcDisplay(IntPtr opaque, IntPtr picture)
    {
        lock (_bufferLock)
        {
            (_writePtr, _readyPtr) = (_readyPtr, _writePtr);
        }

        if (Interlocked.CompareExchange(ref _uiRenderQueued, 1, 0) == 0)
        {
            Dispatcher.BeginInvoke(DispatcherPriority.Render, new Action(PresentLatestVlcFrame));
        }
    }

    private unsafe void PresentLatestVlcFrame()
    {
        try
        {
            int w, h, srcStride;
            lock (_bufferLock)
            {
                w = _visibleWidth;
                h = _visibleHeight;
                srcStride = _stride;
                if (w <= 0 || h <= 0 || _readyPtr == IntPtr.Zero || srcStride <= 0)
                    return;

                bool recreated = _videoBitmap == null ||
                                 _videoBitmap.PixelWidth != w ||
                                 _videoBitmap.PixelHeight != h;

                if (recreated)
                {
                    _videoBitmap = new WriteableBitmap(w, h, 96, 96, PixelFormats.Bgr32, null);
                }

                _videoBitmap!.Lock();
                try
                {
                    byte* dst = (byte*)_videoBitmap.BackBuffer;
                    int dstStride = _videoBitmap.BackBufferStride;
                    byte* src = (byte*)_readyPtr;
                    int copyRowBytes = Math.Min(srcStride, dstStride);

                    if (srcStride == dstStride)
                    {
                        Buffer.MemoryCopy(src, dst, (long)dstStride * h, (long)srcStride * h);
                    }
                    else
                    {
                        for (int y = 0; y < h; y++)
                        {
                            Buffer.MemoryCopy(src + y * srcStride, dst + y * dstStride, dstStride, copyRowBytes);
                        }
                    }
                    _videoBitmap.AddDirtyRect(new Int32Rect(0, 0, w, h));
                }
                finally
                {
                    _videoBitmap.Unlock();
                }

                if (recreated)
                    RebuildMonitorVideoSlices();
            }
        }
        catch { }
        finally
        {
            Interlocked.Exchange(ref _uiRenderQueued, 0);
        }
    }

    private void OnVlcEndReached(object? sender, EventArgs e)
    {
        ThreadPool.QueueUserWorkItem(_ =>
        {
            if (_loopEnabled && !_userPaused && !_systemPaused && !string.IsNullOrEmpty(_currentSource))
            {
                try
                {
                    _vlcPlayer?.Stop();
                    _vlcPlayer?.Play();
                    Dispatcher.BeginInvoke(() => IsEffectivelyPlaying = true);
                }
                catch { }
            }
            else
            {
                Dispatcher.BeginInvoke(() =>
                {
                    IsEffectivelyPlaying = false;
                    StatusSummary = "Ended";
                });
            }
        });
    }

    private void OnVlcEncounteredError(object? sender, EventArgs e)
    {
        Dispatcher.BeginInvoke(() =>
        {
            IsEffectivelyPlaying = false;
            StatusSummary = "LibVLC decode error";
        });
    }

    private void RebuildMonitorVideoSlices()
    {
        MonitorSliceCanvas.Children.Clear();
        bool useBitmapBrush = !_usingFallbackMediaElement && _videoBitmap != null;

        Brush CreateSliceBrush(Rect? viewbox)
        {
            if (useBitmapBrush)
            {
                return new ImageBrush(_videoBitmap)
                {
                    Viewbox = viewbox ?? new Rect(0, 0, 1, 1),
                    ViewboxUnits = BrushMappingMode.RelativeToBoundingBox,
                    Stretch = viewbox.HasValue ? Stretch.Fill : Stretch.UniformToFill
                };
            }

            return new VisualBrush(VideoPlayer)
            {
                Viewbox = viewbox ?? new Rect(0, 0, 1, 1),
                ViewboxUnits = BrushMappingMode.RelativeToBoundingBox,
                Stretch = viewbox.HasValue ? Stretch.Fill : Stretch.UniformToFill
            };
        }

        if (_lastPlan == null || _lastPlan.Slices.Count == 0 || _lastPlan.SourceWidth <= 0 || _lastPlan.SourceHeight <= 0)
        {
            var rect = new Rectangle
            {
                Width = Width,
                Height = Height,
                Fill = CreateSliceBrush(null)
            };
            Canvas.SetLeft(rect, 0);
            Canvas.SetTop(rect, 0);
            MonitorSliceCanvas.Children.Add(rect);
            return;
        }

        foreach (var slice in _lastPlan.Slices)
        {
            var m = slice.Monitor;
            double sx = Math.Max(96, m.DpiX) / 96.0;
            double sy = Math.Max(96, m.DpiY) / 96.0;
            double monDipLeft = m.Bounds.X / sx - _winDipLeft;
            double monDipTop = m.Bounds.Y / sy - _winDipTop;
            double monDipW = m.Bounds.Width / sx;
            double monDipH = m.Bounds.Height / sy;

            var monClip = new Border
            {
                Width = monDipW,
                Height = monDipH,
                ClipToBounds = true,
                Background = Brushes.Transparent
            };

            if (!slice.DestinationPixelRect.IsEmpty && !slice.SourcePixelRect.IsEmpty && slice.OutputWidth > 0 && slice.OutputHeight > 0)
            {
                double vx = Math.Clamp(slice.SourcePixelRect.X / _lastPlan.SourceWidth, 0.0, 1.0);
                double vy = Math.Clamp(slice.SourcePixelRect.Y / _lastPlan.SourceHeight, 0.0, 1.0);
                double vw = Math.Clamp(slice.SourcePixelRect.Width / _lastPlan.SourceWidth, 0.0001, 1.0 - vx);
                double vh = Math.Clamp(slice.SourcePixelRect.Height / _lastPlan.SourceHeight, 0.0001, 1.0 - vy);

                double dstX = (slice.DestinationPixelRect.X / slice.OutputWidth) * monDipW;
                double dstY = (slice.DestinationPixelRect.Y / slice.OutputHeight) * monDipH;
                double dstW = (slice.DestinationPixelRect.Width / slice.OutputWidth) * monDipW;
                double dstH = (slice.DestinationPixelRect.Height / slice.OutputHeight) * monDipH;

                var videoRect = new Rectangle
                {
                    Width = Math.Max(1, dstW),
                    Height = Math.Max(1, dstH),
                    Fill = CreateSliceBrush(new Rect(vx, vy, vw, vh)),
                    RenderTransformOrigin = new Point(0.5, 0.5),
                    RenderTransform = (slice.FlipHorizontal || slice.FlipVertical)
                        ? new ScaleTransform(slice.FlipHorizontal ? -1 : 1, slice.FlipVertical ? -1 : 1)
                        : Transform.Identity
                };

                var monCanvas = new Canvas { Width = monDipW, Height = monDipH };
                Canvas.SetLeft(videoRect, dstX);
                Canvas.SetTop(videoRect, dstY);
                monCanvas.Children.Add(videoRect);
                monClip.Child = monCanvas;
            }

            Canvas.SetLeft(monClip, monDipLeft);
            Canvas.SetTop(monClip, monDipTop);
            MonitorSliceCanvas.Children.Add(monClip);
        }
    }

    private void ApplyPlaybackState(string reason)
    {
        if (string.IsNullOrEmpty(_currentSource))
        {
            IsEffectivelyPlaying = false;
            StatusSummary = "No video selected";
            return;
        }

        if (_userPaused || _systemPaused)
        {
            if (!_usingFallbackMediaElement && _vlcPlayer != null)
            {
                if (_vlcPlayer.CanPause && _vlcPlayer.IsPlaying) _vlcPlayer.Pause();
            }
            else
            {
                VideoPlayer.Pause();
            }

            IsEffectivelyPlaying = false;
            StatusSummary = string.IsNullOrWhiteSpace(reason) ? "Paused" : reason;
        }
        else
        {
            if (!_usingFallbackMediaElement && _vlcPlayer != null)
            {
                if (!_vlcPlayer.IsPlaying) _vlcPlayer.Play();
            }
            else
            {
                VideoPlayer.Play();
            }

            IsEffectivelyPlaying = true;
            StatusSummary = _loopEnabled ? "Looping across monitors" : "Playing once";
        }
    }

    private void StopPlaybackCleanly()
    {
        StopAndDisposeVlc();
        try
        {
            VideoPlayer.Stop();
            VideoPlayer.Source = null;
        }
        catch { }
    }

    private void StopAndDisposeVlc()
    {
        if (_vlcPlayer != null)
        {
            try
            {
                _vlcPlayer.EndReached -= OnVlcEndReached;
                _vlcPlayer.EncounteredError -= OnVlcEncounteredError;
                _vlcPlayer.Stop();
                _vlcPlayer.Dispose();
            }
            catch { }
            _vlcPlayer = null;
        }

        if (_vlcMedia != null)
        {
            try { _vlcMedia.Dispose(); } catch { }
            _vlcMedia = null;
        }

        lock (_bufferLock)
        {
            if (_writePtr != IntPtr.Zero) { Marshal.FreeHGlobal(_writePtr); _writePtr = IntPtr.Zero; }
            if (_readyPtr != IntPtr.Zero) { Marshal.FreeHGlobal(_readyPtr); _readyPtr = IntPtr.Zero; }
            _visibleWidth = 0;
            _visibleHeight = 0;
            _stride = 0;
            _allocLines = 0;
            _videoBitmap = null;
        }
    }

    private void VideoPlayer_OnMediaOpened(object sender, RoutedEventArgs e)
    {
        if (VideoPlayer.NaturalVideoWidth > 0 && VideoPlayer.NaturalVideoHeight > 0)
        {
            VideoPlayer.Width = VideoPlayer.NaturalVideoWidth;
            VideoPlayer.Height = VideoPlayer.NaturalVideoHeight;
        }
    }

    private void VideoPlayer_OnMediaEnded(object sender, RoutedEventArgs e)
    {
        if (_loopEnabled && !_userPaused && !_systemPaused && !string.IsNullOrEmpty(_currentSource))
        {
            VideoPlayer.Position = TimeSpan.Zero;
            VideoPlayer.Play();
            IsEffectivelyPlaying = true;
        }
        else
        {
            IsEffectivelyPlaying = false;
            StatusSummary = "Ended";
        }
    }

    private void VideoPlayer_OnMediaFailed(object? sender, ExceptionRoutedEventArgs e)
    {
        IsEffectivelyPlaying = false;
        StatusSummary = $"Codec/video error: {e.ErrorException.Message}";
    }

    private static Color ParseColor(string hex)
    {
        try
        {
            if (ColorConverter.ConvertFromString(hex) is Color c) return c;
        }
        catch { }
        return Color.FromRgb(13, 16, 23);
    }
}
