using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using DuoWall.App.Helpers;
using DuoWall.Core.Models;

namespace DuoWall.App.Controls;

/// <summary>
/// Interactive proportional multi-monitor preview surface.
/// Renders each connected display at its true relative resolution/physical geometry,
/// projects the CompositionPlan slices identically to the Skia compositor,
/// and supports drag-to-pan, mouse-wheel zoom, and monitor selection.
/// </summary>
public sealed class MultiMonitorPreviewControl : FrameworkElement
{
    public static readonly DependencyProperty PlanProperty =
        DependencyProperty.Register(nameof(Plan), typeof(CompositionPlan), typeof(MultiMonitorPreviewControl),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender, OnSourceChanged));

    public static readonly DependencyProperty PreviewImageSourceProperty =
        DependencyProperty.Register(nameof(PreviewImageSource), typeof(string), typeof(MultiMonitorPreviewControl),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender, OnSourceChanged));

    public static readonly DependencyProperty BackgroundColorHexProperty =
        DependencyProperty.Register(nameof(BackgroundColorHex), typeof(string), typeof(MultiMonitorPreviewControl),
            new FrameworkPropertyMetadata("#0D1017", FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty ShowMonitorLabelsProperty =
        DependencyProperty.Register(nameof(ShowMonitorLabels), typeof(bool), typeof(MultiMonitorPreviewControl),
            new FrameworkPropertyMetadata(true, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty IsInteractiveProperty =
        DependencyProperty.Register(nameof(IsInteractive), typeof(bool), typeof(MultiMonitorPreviewControl),
            new FrameworkPropertyMetadata(true));

    public static readonly DependencyProperty SelectedMonitorIdProperty =
        DependencyProperty.Register(nameof(SelectedMonitorId), typeof(string), typeof(MultiMonitorPreviewControl),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty DiskCacheDirectoryProperty =
        DependencyProperty.Register(nameof(DiskCacheDirectory), typeof(string), typeof(MultiMonitorPreviewControl),
            new FrameworkPropertyMetadata(null));

    public CompositionPlan? Plan
    {
        get => (CompositionPlan?)GetValue(PlanProperty);
        set => SetValue(PlanProperty, value);
    }

    public string? PreviewImageSource
    {
        get => (string?)GetValue(PreviewImageSourceProperty);
        set => SetValue(PreviewImageSourceProperty, value);
    }

    public string BackgroundColorHex
    {
        get => (string)GetValue(BackgroundColorHexProperty);
        set => SetValue(BackgroundColorHexProperty, value);
    }

    public bool ShowMonitorLabels
    {
        get => (bool)GetValue(ShowMonitorLabelsProperty);
        set => SetValue(ShowMonitorLabelsProperty, value);
    }

    public bool IsInteractive
    {
        get => (bool)GetValue(IsInteractiveProperty);
        set => SetValue(IsInteractiveProperty, value);
    }

    public string? SelectedMonitorId
    {
        get => (string?)GetValue(SelectedMonitorIdProperty);
        set => SetValue(SelectedMonitorIdProperty, value);
    }

    public string? DiskCacheDirectory
    {
        get => (string?)GetValue(DiskCacheDirectoryProperty);
        set => SetValue(DiskCacheDirectoryProperty, value);
    }

    public event Action<double, double>? PanDeltaRequested;
    public event Action<double>? ZoomDeltaRequested;
    public event Action? ResetRequested;
    public event Action<string>? MonitorClicked;

    private readonly Dictionary<string, BitmapSource> _loadedBitmaps = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Rect> _monitorScreenRects = new(StringComparer.OrdinalIgnoreCase);

    private bool _isDragging;
    private Point _lastDragPoint;
    private double _lastViewportScale = 1.0;

    public MultiMonitorPreviewControl()
    {
        ClipToBounds = true;
        Cursor = Cursors.Hand;
    }

    private static async void OnSourceChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is MultiMonitorPreviewControl ctrl)
            await ctrl.RefreshBitmapsAsync();
    }

    private async Task RefreshBitmapsAsync()
    {
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (!string.IsNullOrWhiteSpace(PreviewImageSource))
            paths.Add(PreviewImageSource);

        if (Plan != null)
        {
            foreach (var s in Plan.Slices)
            {
                if (!string.IsNullOrWhiteSpace(s.SourceImagePath))
                    paths.Add(s.SourceImagePath);
            }
        }

        bool changed = false;
        foreach (string p in paths)
        {
            if (_loadedBitmaps.ContainsKey(p))
                continue;

            var bmp = await ImageCacheHelper.LoadBitmapAsync(
                p,
                decodePixelWidth: 2200,
                diskCacheDirectory: DiskCacheDirectory);

            if (bmp != null)
            {
                _loadedBitmaps[p] = bmp;
                changed = true;
            }
        }

        if (changed)
            InvalidateVisual();
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        if (!IsInteractive) return;

        if (e.ClickCount == 2)
        {
            ResetRequested?.Invoke();
            return;
        }

        Point pos = e.GetPosition(this);
        foreach (var kv in _monitorScreenRects)
        {
            if (kv.Value.Contains(pos))
            {
                SelectedMonitorId = kv.Key;
                MonitorClicked?.Invoke(kv.Key);
                break;
            }
        }

        _isDragging = true;
        _lastDragPoint = pos;
        CaptureMouse();
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (!_isDragging || !IsInteractive || Plan == null || _lastViewportScale <= 0)
            return;

        Point pos = e.GetPosition(this);
        double dxScreen = pos.X - _lastDragPoint.X;
        double dyScreen = pos.Y - _lastDragPoint.Y;
        _lastDragPoint = pos;

        if (Math.Abs(dxScreen) < 0.01 && Math.Abs(dyScreen) < 0.01)
            return;

        double deltaCanvasX = dxScreen / _lastViewportScale;
        double deltaCanvasY = dyScreen / _lastViewportScale;

        double normDx = Plan.CombinedCanvasWidth > 0 ? deltaCanvasX / Plan.CombinedCanvasWidth : 0;
        double normDy = Plan.CombinedCanvasHeight > 0 ? deltaCanvasY / Plan.CombinedCanvasHeight : 0;

        PanDeltaRequested?.Invoke(normDx, normDy);
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        if (_isDragging)
        {
            _isDragging = false;
            ReleaseMouseCapture();
        }
    }

    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        base.OnMouseWheel(e);
        if (!IsInteractive) return;
        double delta = e.Delta > 0 ? 0.08 : -0.08;
        ZoomDeltaRequested?.Invoke(delta);
        e.Handled = true;
    }

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        _monitorScreenRects.Clear();

        double w = ActualWidth;
        double h = ActualHeight;
        if (w <= 4 || h <= 4) return;

        // Studio backdrop
        var backdropBrush = new SolidColorBrush(Color.FromRgb(13, 16, 24));
        dc.DrawRoundedRectangle(backdropBrush, new Pen(new SolidColorBrush(Color.FromArgb(40, 255, 255, 255)), 1),
            new Rect(0, 0, w, h), 10, 10);

        // Subtle alignment grid lines in studio background
        var gridPen = new Pen(new SolidColorBrush(Color.FromArgb(14, 255, 255, 255)), 1);
        for (double gx = 32; gx < w; gx += 32)
            dc.DrawLine(gridPen, new Point(gx, 0), new Point(gx, h));
        for (double gy = 32; gy < h; gy += 32)
            dc.DrawLine(gridPen, new Point(0, gy), new Point(w, gy));

        var plan = Plan;
        if (plan == null || plan.Slices.Count == 0 || plan.CombinedCanvasWidth <= 0 || plan.CombinedCanvasHeight <= 0)
        {
            DrawCenteredMessage(dc, "Select a wallpaper or connect displays to preview", w, h);
            return;
        }

        double pad = ShowMonitorLabels ? 24.0 : 14.0;
        double availW = Math.Max(20, w - pad * 2);
        double availH = Math.Max(20, h - pad * 2);

        double scale = Math.Min(availW / plan.CombinedCanvasWidth, availH / plan.CombinedCanvasHeight);
        _lastViewportScale = scale;

        double totalDrawW = plan.CombinedCanvasWidth * scale;
        double totalDrawH = plan.CombinedCanvasHeight * scale;
        double originX = (w - totalDrawW) * 0.5;
        double originY = (h - totalDrawH) * 0.5;

        Color bgColor = ParseWpfColor(BackgroundColorHex);
        var monBgBrush = new SolidColorBrush(bgColor);

        // Draw subtle dashed bounding box showing total combined virtual/physical desktop
        var boundsPen = new Pen(new SolidColorBrush(Color.FromArgb(55, 96, 165, 250)), 1)
        {
            DashStyle = new DashStyle(new double[] { 4, 4 }, 0)
        };
        dc.DrawRectangle(null, boundsPen, new Rect(originX, originY, totalDrawW, totalDrawH));

        double dpiScale = VisualTreeHelper.GetDpi(this).PixelsPerDip;

        foreach (var slice in plan.Slices)
        {
            var mRect = new Rect(
                originX + slice.CanvasRect.X * scale,
                originY + slice.CanvasRect.Y * scale,
                Math.Max(2, slice.CanvasRect.Width * scale),
                Math.Max(2, slice.CanvasRect.Height * scale));

            _monitorScreenRects[slice.Monitor.Id] = mRect;

            // Subtle monitor bezel shadow
            dc.DrawRoundedRectangle(
                new SolidColorBrush(Color.FromArgb(110, 0, 0, 0)),
                null,
                new Rect(mRect.X + 2, mRect.Y + 4, mRect.Width, mRect.Height),
                4, 4);

            dc.PushClip(new RectangleGeometry(mRect));
            dc.DrawRectangle(monBgBrush, null, mRect);

            BitmapSource? bmp = ResolveBitmap(slice.SourceImagePath);
            if (bmp != null && !slice.DestinationPixelRect.IsEmpty && !slice.SourcePixelRect.IsEmpty)
            {
                double srcTotalW = plan.Mode == WallpaperPlacementMode.Individual ? bmp.PixelWidth : plan.SourceWidth;
                double srcTotalH = plan.Mode == WallpaperPlacementMode.Individual ? bmp.PixelHeight : plan.SourceHeight;

                // Map SourcePixelRect into normalized [0..1] viewbox of the loaded preview bitmap
                double vx = Math.Clamp(slice.SourcePixelRect.X / srcTotalW, 0.0, 1.0);
                double vy = Math.Clamp(slice.SourcePixelRect.Y / srcTotalH, 0.0, 1.0);
                double vw = Math.Clamp(slice.SourcePixelRect.Width / srcTotalW, 0.0001, 1.0 - vx);
                double vh = Math.Clamp(slice.SourcePixelRect.Height / srcTotalH, 0.0001, 1.0 - vy);

                double dstX = mRect.X + (slice.DestinationPixelRect.X / slice.OutputWidth) * mRect.Width;
                double dstY = mRect.Y + (slice.DestinationPixelRect.Y / slice.OutputHeight) * mRect.Height;
                double dstW = (slice.DestinationPixelRect.Width / slice.OutputWidth) * mRect.Width;
                double dstH = (slice.DestinationPixelRect.Height / slice.OutputHeight) * mRect.Height;

                var brush = new ImageBrush(bmp)
                {
                    Viewbox = new Rect(vx, vy, vw, vh),
                    ViewboxUnits = BrushMappingMode.RelativeToBoundingBox,
                    Stretch = Stretch.Fill
                };
                RenderOptions.SetBitmapScalingMode(brush, BitmapScalingMode.HighQuality);
                dc.DrawRectangle(brush, null, new Rect(dstX, dstY, dstW, dstH));
            }

            dc.Pop();

            // Monitor bezel frame
            bool isSelected = string.Equals(SelectedMonitorId, slice.Monitor.Id, StringComparison.OrdinalIgnoreCase);
            Color borderColor = isSelected
                ? Color.FromRgb(96, 165, 250)
                : (slice.Monitor.IsPrimary ? Color.FromArgb(210, 74, 158, 255) : Color.FromArgb(170, 148, 163, 184));
            double borderThickness = isSelected ? 2.2 : 1.5;

            dc.DrawRectangle(null, new Pen(new SolidColorBrush(borderColor), borderThickness), mRect);

            if (ShowMonitorLabels && mRect.Width > 75 && mRect.Height > 45)
            {
                DrawMonitorBadge(dc, slice, mRect, dpiScale);
            }
        }

        // Bottom info badge showing coordinate space & physical/virtual dimensions
        if (ShowMonitorLabels)
        {
            string spaceInfo = plan.CoordinateSpace == GeometryCoordinateSpace.PhysicalDimensions
                ? $"Physical Geometry: {plan.CombinedCanvasWidth:0} × {plan.CombinedCanvasHeight:0} mm  ·  Virtual: {plan.VirtualDesktopWidth} × {plan.VirtualDesktopHeight} px"
                : $"Virtual Pixel Geometry: {plan.VirtualDesktopWidth} × {plan.VirtualDesktopHeight} px";
            var ft = CreateText(spaceInfo, 11, Color.FromArgb(185, 203, 213, 225), dpiScale);
            dc.DrawText(ft, new Point(12, h - ft.Height - 5));
        }
    }

    private BitmapSource? ResolveBitmap(string? slicePath)
    {
        if (!string.IsNullOrWhiteSpace(slicePath) && _loadedBitmaps.TryGetValue(slicePath, out var sliceBmp))
            return sliceBmp;
        if (!string.IsNullOrWhiteSpace(PreviewImageSource) && _loadedBitmaps.TryGetValue(PreviewImageSource, out var previewBmp))
            return previewBmp;
        return _loadedBitmaps.Values.FirstOrDefault();
    }

    private static void DrawMonitorBadge(DrawingContext dc, MonitorSlicePlan slice, Rect mRect, double dpiScale)
    {
        var m = slice.Monitor;
        string title = m.IsPrimary ? $"★ {m.DisplayLabel}" : m.DisplayLabel;
        string details = $"{m.ResolutionLabel} · {m.PhysicalDiagonalInches:0.0}\" · ({m.Bounds.X},{m.Bounds.Y})";

        var ftTitle = CreateText(title, 11.5, Colors.White, dpiScale, FontWeights.SemiBold);
        var ftDet = CreateText(details, 10, Color.FromRgb(203, 213, 225), dpiScale);

        double pillW = Math.Min(mRect.Width - 12, Math.Max(ftTitle.Width, ftDet.Width) + 16);
        double pillH = ftTitle.Height + ftDet.Height + 10;
        double px = mRect.X + 8;
        double py = mRect.Bottom - pillH - 8;

        if (py < mRect.Y + 4) return;

        dc.DrawRoundedRectangle(
            new SolidColorBrush(Color.FromArgb(190, 12, 16, 26)),
            new Pen(new SolidColorBrush(Color.FromArgb(75, 255, 255, 255)), 0.8),
            new Rect(px, py, pillW, pillH), 6, 6);

        dc.PushClip(new RectangleGeometry(new Rect(px + 4, py + 2, pillW - 8, pillH - 4)));
        dc.DrawText(ftTitle, new Point(px + 8, py + 4));
        dc.DrawText(ftDet, new Point(px + 8, py + 4 + ftTitle.Height));
        dc.Pop();

        if (slice.HasResolutionWarning)
        {
            var warnFt = CreateText($"Upscale {slice.MaxUpscaleRatio:0.00}×", 10, Color.FromRgb(253, 224, 71), dpiScale, FontWeights.SemiBold);
            double wx = mRect.Right - warnFt.Width - 20;
            double wy = mRect.Y + 8;
            if (wx > mRect.X + 4)
            {
                dc.DrawRoundedRectangle(
                    new SolidColorBrush(Color.FromArgb(200, 120, 53, 15)),
                    new Pen(new SolidColorBrush(Color.FromRgb(251, 191, 36)), 0.8),
                    new Rect(wx, wy, warnFt.Width + 12, warnFt.Height + 6), 5, 5);
                dc.DrawText(warnFt, new Point(wx + 6, wy + 3));
            }
        }
    }

    private void DrawCenteredMessage(DrawingContext dc, string text, double w, double h)
    {
        double dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        var ft = CreateText(text, 13, Color.FromRgb(148, 163, 184), dpi);
        dc.DrawText(ft, new Point((w - ft.Width) * 0.5, (h - ft.Height) * 0.5));
    }

    private static FormattedText CreateText(string text, double size, Color color, double dpiScale, FontWeight? weight = null)
    {
        var typeface = new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, weight ?? FontWeights.Normal, FontStretches.Normal);
        return new FormattedText(
            text,
            CultureInfo.CurrentUICulture,
            FlowDirection.LeftToRight,
            typeface,
            size,
            new SolidColorBrush(color),
            dpiScale);
    }

    private static Color ParseWpfColor(string? hex)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(hex) && ColorConverter.ConvertFromString(hex) is Color c)
                return c;
        }
        catch { }
        return Color.FromRgb(13, 16, 23);
    }
}
