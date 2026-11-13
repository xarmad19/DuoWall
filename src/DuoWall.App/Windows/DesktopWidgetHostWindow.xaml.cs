using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using DuoWall.Core.Services;
using DuoWall.Core.Theming;
using DuoWall.Core.Widgets;

namespace DuoWall.App.Windows;

/// <summary>
/// Per-pixel alpha layered desktop widget host window supporting live responsive resizing,
/// live snap-to-grid dragging across monitors, and strict lock-mode enforcement.
/// </summary>
public partial class DesktopWidgetHostWindow : Window
{
    public const double OuterShadowMargin = 18.0;

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X;
        public int Y;
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetCursorPos(out POINT lpPoint);

    private IntPtr _hwnd = IntPtr.Zero;
    private WidgetInstance _widget = new();
    private GlassmorphismStyle _effectiveStyle = GlassmorphismStyle.BuiltInPresets[0].Clone();
    private bool _isEditMode;
    private bool _snapToGrid = true;
    private int _gridSizePx = 16;

    // Live manual drag state enabling true smooth Snap-To-Grid on desktop
    private bool _isDraggingWindow;
    private double _dragOffsetDipX;
    private double _dragOffsetDipY;

    public event Action<WidgetInstance, double, double, double, double>? BoundsCommitted;

    public DesktopWidgetHostWindow()
    {
        InitializeComponent();
        MouseMove += OnHostMouseMove;
        PreviewMouseLeftButtonUp += OnHostMouseUp;
    }

    public Controls.WidgetContentControl ContentControl => WidgetContent;
    public string InstanceId => _widget.InstanceId;

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        _hwnd = new WindowInteropHelper(this).Handle;

        GlassBackdropHelper.ConfigureDesktopOverlayWindow(_hwnd, excludeFromAeroPeek: false);
        GlassBackdropHelper.SetClickThrough(_hwnd, _widget.IsClickThrough && !_isEditMode);
        GlassBackdropHelper.SendToBottomZOrder(_hwnd);
    }

    public void SetGridOptions(bool snapEnabled, int gridSizePx)
    {
        _snapToGrid = snapEnabled;
        _gridSizePx = Math.Clamp(gridSizePx, 2, 128);
    }

    public void ApplyWidgetState(
        WidgetInstance widget,
        GlassmorphismStyle effectiveStyle,
        double dipLeft,
        double dipTop,
        bool isEditMode,
        bool snapToGrid,
        int gridSizePx,
        ImageSource? wallpaperBackdropSlice,
        WeatherSnapshot? weather,
        string? weatherStatus,
        TelemetrySnapshot? telemetry,
        List<TodoTaskItem> todos,
        bool videoPlaying,
        string videoStatus,
        List<CalendarAgendaEntry>? calendarEntries = null)
    {
        _widget = widget;
        _effectiveStyle = effectiveStyle;
        _isEditMode = isEditMode;
        _snapToGrid = snapToGrid;
        _gridSizePx = Math.Clamp(gridSizePx, 2, 128);

        double minW = widget.Kind == WidgetKind.CalendarAgenda ? 295 : 140;
        double minH = widget.Kind == WidgetKind.CalendarAgenda ? 305 : 70;
        double cardW = Math.Clamp(widget.Width, minW, 3840);
        double cardH = Math.Clamp(widget.Height, minH, 2160);

        Left = dipLeft - OuterShadowMargin;
        Top = dipTop - OuterShadowMargin;
        Width = cardW + OuterShadowMargin * 2;
        Height = cardH + OuterShadowMargin * 2;

        double radius = effectiveStyle.SurfaceTheme == WidgetSurfaceTheme.TransparentMinimal
            ? 8
            : Math.Clamp(effectiveStyle.CornerRadius, 2, 40);
        EditChromeBorder.CornerRadius = new CornerRadius(radius);
        EditBadgeText.Text = snapToGrid
            ? $"{widget.Title} · Snap {_gridSizePx}px"
            : $"{widget.Title} · Drag / Resize";
        EditChromeBorder.Visibility = isEditMode ? Visibility.Visible : Visibility.Collapsed;
        ResizeThumb.Visibility = isEditMode ? Visibility.Visible : Visibility.Collapsed;

        WidgetContent.Bind(
            widget,
            effectiveStyle,
            isHardwareBackdropActive: false,
            fallbackBackdropSource: wallpaperBackdropSlice,
            weather: weather,
            weatherStatus: weatherStatus,
            telemetry: telemetry,
            todos: todos,
            videoPlaying: videoPlaying,
            videoStatus: videoStatus,
            calendarEntries: calendarEntries);

        if (_hwnd != IntPtr.Zero)
        {
            GlassBackdropHelper.SetClickThrough(_hwnd, widget.IsClickThrough && !isEditMode);
            GlassBackdropHelper.SendToBottomZOrder(_hwnd);
        }
    }

    public void UpdateLiveGlass(GlassmorphismStyle effectiveStyle, double brightnessMultiplier, ImageSource? wallpaperBackdropSlice)
    {
        _effectiveStyle = effectiveStyle;
        _widget.BrightnessMultiplier = brightnessMultiplier;

        double radius = effectiveStyle.SurfaceTheme == WidgetSurfaceTheme.TransparentMinimal
            ? 8
            : Math.Clamp(effectiveStyle.CornerRadius, 2, 40);
        EditChromeBorder.CornerRadius = new CornerRadius(radius);
        WidgetContent.UpdateGlass(effectiveStyle, brightnessMultiplier, false, wallpaperBackdropSlice);
    }

    public void SetEditMode(bool enabled)
    {
        _isEditMode = enabled;
        EditChromeBorder.Visibility = enabled ? Visibility.Visible : Visibility.Collapsed;
        ResizeThumb.Visibility = enabled ? Visibility.Visible : Visibility.Collapsed;

        if (_hwnd != IntPtr.Zero)
        {
            GlassBackdropHelper.SetClickThrough(_hwnd, _widget.IsClickThrough && !enabled);
        }
    }

    private void OnPreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (!_isEditMode)
        {
            return;
        }

        if (e.OriginalSource is DependencyObject dep && IsInteractiveChild(dep))
        {
            return;
        }

        if (GetCursorPos(out var pt))
        {
            double dpiScale = GetWindowDpiScale();
            double cursorDipX = pt.X / dpiScale;
            double cursorDipY = pt.Y / dpiScale;

            _dragOffsetDipX = cursorDipX - Left;
            _dragOffsetDipY = cursorDipY - Top;
            _isDraggingWindow = true;
            CaptureMouse();
            e.Handled = true;
        }
    }

    private void OnHostMouseMove(object sender, MouseEventArgs e)
    {
        if (!_isDraggingWindow || !_isEditMode) return;
        if (!GetCursorPos(out var pt)) return;

        double dpiScale = GetWindowDpiScale();
        double rawCardLeft = (pt.X / dpiScale) - _dragOffsetDipX + OuterShadowMargin;
        double rawCardTop = (pt.Y / dpiScale) - _dragOffsetDipY + OuterShadowMargin;

        if (_snapToGrid && _gridSizePx > 1)
        {
            rawCardLeft = Math.Round(rawCardLeft / _gridSizePx) * _gridSizePx;
            rawCardTop = Math.Round(rawCardTop / _gridSizePx) * _gridSizePx;
        }

        Left = rawCardLeft - OuterShadowMargin;
        Top = rawCardTop - OuterShadowMargin;
    }

    private void OnHostMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (!_isDraggingWindow) return;
        _isDraggingWindow = false;
        ReleaseMouseCapture();

        BoundsCommitted?.Invoke(
            _widget,
            Left + OuterShadowMargin,
            Top + OuterShadowMargin,
            Width - OuterShadowMargin * 2,
            Height - OuterShadowMargin * 2);
    }

    private void ResizeThumb_OnDragDelta(object sender, DragDeltaEventArgs e)
    {
        if (!_isEditMode) return;

        double rawCardW = (Width - OuterShadowMargin * 2) + e.HorizontalChange;
        double rawCardH = (Height - OuterShadowMargin * 2) + e.VerticalChange;

        if (_snapToGrid && _gridSizePx > 1)
        {
            rawCardW = Math.Round(rawCardW / _gridSizePx) * _gridSizePx;
            rawCardH = Math.Round(rawCardH / _gridSizePx) * _gridSizePx;
        }

        double minW = _widget.Kind == WidgetKind.CalendarAgenda ? 295 : 140;
        double minH = _widget.Kind == WidgetKind.CalendarAgenda ? 305 : 70;
        double cardW = Math.Clamp(rawCardW, minW, 3840);
        double cardH = Math.Clamp(rawCardH, minH, 2160);

        Width = cardW + OuterShadowMargin * 2;
        Height = cardH + OuterShadowMargin * 2;

        // Requirement #2: Dynamically rescale widget contents & typography while resizing live!
        WidgetContent.UpdateDimensions(cardW, cardH);
    }

    private void ResizeThumb_OnDragCompleted(object sender, DragCompletedEventArgs e)
    {
        if (!_isEditMode) return;
        BoundsCommitted?.Invoke(
            _widget,
            Left + OuterShadowMargin,
            Top + OuterShadowMargin,
            Width - OuterShadowMargin * 2,
            Height - OuterShadowMargin * 2);
    }

    private double GetWindowDpiScale()
    {
        var m = PresentationSource.FromVisual(this)?.CompositionTarget?.TransformToDevice;
        return m.HasValue && m.Value.M11 > 0.1 ? m.Value.M11 : 1.0;
    }

    private static bool IsInteractiveChild(DependencyObject? node)
    {
        while (node != null)
        {
            if (node is ButtonBase
                or System.Windows.Controls.TextBox
                or Thumb)
            {
                return true;
            }
            node = node is Visual or System.Windows.Media.Media3D.Visual3D
                ? VisualTreeHelper.GetParent(node)
                : LogicalTreeHelper.GetParent(node);
        }

        return false;
    }
}
