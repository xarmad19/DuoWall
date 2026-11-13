using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using DuoWall.Core.Packages;
using DuoWall.Core.Services;
using DuoWall.Core.Theming;
using DuoWall.Core.Widgets;
using SkiaSharp;

namespace DuoWall.App.Controls;

/// <summary>
/// Renders a crisp scaled live widget preview for Marketplace cards using the actual
/// WidgetContentControl + GlassSurfaceControl pipeline, displaying real frosted blur on
/// Glassmorphism items and sharp transparent/flat styling on Rainmeter-inspired skins.
/// </summary>
public sealed class MarketplacePreviewHost : Grid
{
    public static readonly DependencyProperty EntryProperty = DependencyProperty.Register(
        nameof(Entry),
        typeof(MarketplaceCatalogEntry),
        typeof(MarketplacePreviewHost),
        new PropertyMetadata(null, OnVisualInputsChanged));

    public static readonly DependencyProperty BackdropImageProperty = DependencyProperty.Register(
        nameof(BackdropImage),
        typeof(ImageSource),
        typeof(MarketplacePreviewHost),
        new PropertyMetadata(null, OnVisualInputsChanged));

    private static BitmapSource? _cachedSharpRef;
    private static readonly Dictionary<int, BitmapSource> _blurredCache = new();

    private readonly Image _bgImage = new() { Stretch = Stretch.UniformToFill, Opacity = 0.94 };
    private readonly Viewbox _scaler = new() { Stretch = Stretch.Uniform, Margin = new Thickness(12, 8, 12, 8) };
    private readonly WidgetContentControl _previewWidget = new() { Width = 310, Height = 165, IsHitTestVisible = false };

    public MarketplaceCatalogEntry? Entry
    {
        get => (MarketplaceCatalogEntry?)GetValue(EntryProperty);
        set => SetValue(EntryProperty, value);
    }

    public ImageSource? BackdropImage
    {
        get => (ImageSource?)GetValue(BackdropImageProperty);
        set => SetValue(BackdropImageProperty, value);
    }

    public MarketplacePreviewHost()
    {
        ClipToBounds = true;
        Background = new SolidColorBrush(Color.FromRgb(15, 23, 42));
        _scaler.Child = _previewWidget;
        Children.Add(_bgImage);
        Children.Add(_scaler);
    }

    private static void OnVisualInputsChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is MarketplacePreviewHost host)
            host.RefreshPreview();
    }

    private void RefreshPreview()
    {
        _bgImage.Source = BackdropImage;
        if (Entry == null) return;

        var m = Entry.Manifest;
        var style = m.GlassStyle ?? GlassmorphismStyle.BuiltInPresets[0];
        var instance = m.WidgetTemplate != null
            ? m.WidgetTemplate.Clone(newInstanceId: false)
            : new WidgetInstance
            {
                InstanceId = $"mkt-{m.Id}",
                PackageId = m.Id,
                Kind = m.WidgetKind,
                Title = m.Name,
                Width = 310,
                Height = 165,
                UseGlobalGlassTheme = false,
                GlassStyle = style,
                Config = new Dictionary<string, string>(m.DefaultSettings)
            };

        var sampleWx = new WeatherSnapshot
        {
            CityDisplay = "Lahore, PK",
            Temperature = 26,
            ApparentTemperature = 27,
            HumidityPercent = 44,
            WindSpeedKmh = 9,
            ConditionText = "Clear Sky",
            ConditionIcon = "☀",
            UnitSymbol = "°C",
            ProviderName = "Open-Meteo"
        };

        var sampleStats = new Windows.TelemetrySnapshot
        {
            CpuUsagePercent = 18,
            RamUsedGb = 8.4,
            RamTotalGb = 16.0,
            RamUsagePercent = 52,
            NetworkDownKbps = 145.2,
            NetworkUpKbps = 34.8,
            GpuName = "Graphics Adapter",
            CpuTemperatureC = 54,
            ThermalStatusNote = "Preview",
            SystemUptime = TimeSpan.FromHours(4).Add(TimeSpan.FromMinutes(22)),
            Drives = new List<Windows.DriveTelemetry> { new() { Name = "C:", UsedGb = 210, TotalGb = 500 } }
        };

        var sampleTodos = new List<TodoTaskItem>
        {
            new() { Text = "Review dual-monitor seam alignment", IsCompleted = true },
            new() { Text = "Customize Rainmeter / Glass theme", IsCompleted = false }
        };

        ImageSource? cardBackdrop = style.SurfaceTheme == WidgetSurfaceTheme.Glassmorphism && style.EnableBackdropBlur
            ? GetOrBuildBlurredThumb(BackdropImage as BitmapSource, style.BackgroundBlurIntensity)
            : null;

        _previewWidget.Bind(
            instance,
            style,
            isHardwareBackdropActive: false,
            fallbackBackdropSource: cardBackdrop,
            weather: sampleWx,
            telemetry: sampleStats,
            todos: sampleTodos,
            videoPlaying: true,
            videoStatus: "Looping across displays");
    }

    private static BitmapSource? GetOrBuildBlurredThumb(BitmapSource? src, double blurIntensity)
    {
        if (src == null) return null;
        if (!ReferenceEquals(_cachedSharpRef, src))
        {
            _cachedSharpRef = src;
            _blurredCache.Clear();
        }

        int bucket = (int)Math.Clamp(Math.Round(blurIntensity / 4.0) * 4, 0, 40);
        if (_blurredCache.TryGetValue(bucket, out var hit))
            return hit;

        try
        {
            const int tw = 310;
            const int th = 165;
            int srcW = Math.Max(1, src.PixelWidth);
            int srcH = Math.Max(1, src.PixelHeight);

            var bgra = src.Format == PixelFormats.Pbgra32 || src.Format == PixelFormats.Bgra32
                ? src
                : new FormatConvertedBitmap(src, PixelFormats.Pbgra32, null, 0);

            int stride = srcW * 4;
            byte[] pixels = new byte[stride * srcH];
            bgra.CopyPixels(pixels, stride, 0);

            using var rawSk = new SKBitmap(srcW, srcH, SKColorType.Bgra8888, SKAlphaType.Premul);
            System.Runtime.InteropServices.Marshal.Copy(pixels, 0, rawSk.GetPixels(), pixels.Length);

            using var outSk = new SKBitmap(tw, th, SKColorType.Bgra8888, SKAlphaType.Premul);
            using (var c = new SKCanvas(outSk))
            using (var p = new SKPaint
            {
                IsAntialias = true,
                FilterQuality = SKFilterQuality.Low,
                ImageFilter = bucket > 1 ? SKImageFilter.CreateBlur(bucket * 0.35f, bucket * 0.35f, SKShaderTileMode.Clamp) : null
            })
            {
                c.DrawBitmap(rawSk, new SKRect(srcW * 0.15f, srcH * 0.15f, srcW * 0.85f, srcH * 0.85f), new SKRect(0, 0, tw, th), p);
                c.Flush();
            }

            var frozen = BitmapSource.Create(tw, th, 96, 96, PixelFormats.Pbgra32, null, outSk.GetPixels(), outSk.RowBytes * th, outSk.RowBytes);
            frozen.Freeze();
            _blurredCache[bucket] = frozen;
            return frozen;
        }
        catch
        {
            return src;
        }
    }
}
