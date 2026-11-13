using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using DuoWall.App.Helpers;
using DuoWall.Core.Engine;
using DuoWall.Core.Models;
using DuoWall.Core.Widgets;

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

    public static readonly DependencyProperty WidgetsProperty =
        DependencyProperty.Register(nameof(Widgets), typeof(IEnumerable<WidgetInstance>), typeof(MultiMonitorPreviewControl),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty ShowWidgetsProperty =
        DependencyProperty.Register(nameof(ShowWidgets), typeof(bool), typeof(MultiMonitorPreviewControl),
            new FrameworkPropertyMetadata(true, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty IsWidgetEditModeProperty =
        DependencyProperty.Register(nameof(IsWidgetEditMode), typeof(bool), typeof(MultiMonitorPreviewControl),
            new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty SelectedWidgetIdProperty =
        DependencyProperty.Register(nameof(SelectedWidgetId), typeof(string), typeof(MultiMonitorPreviewControl),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty EnableSnapToGridProperty =
        DependencyProperty.Register(nameof(EnableSnapToGrid), typeof(bool), typeof(MultiMonitorPreviewControl),
            new FrameworkPropertyMetadata(true, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty GridSizePxProperty =
        DependencyProperty.Register(nameof(GridSizePx), typeof(int), typeof(MultiMonitorPreviewControl),
            new FrameworkPropertyMetadata(16, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty IsBoxSelectModeProperty =
        DependencyProperty.Register(nameof(IsBoxSelectMode), typeof(bool), typeof(MultiMonitorPreviewControl),
            new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty CurrentZoomProperty =
        DependencyProperty.Register(nameof(CurrentZoom), typeof(double), typeof(MultiMonitorPreviewControl),
            new FrameworkPropertyMetadata(1.0));

    public static readonly DependencyProperty CurrentPanXProperty =
        DependencyProperty.Register(nameof(CurrentPanX), typeof(double), typeof(MultiMonitorPreviewControl),
            new FrameworkPropertyMetadata(0.0));

    public static readonly DependencyProperty CurrentPanYProperty =
        DependencyProperty.Register(nameof(CurrentPanY), typeof(double), typeof(MultiMonitorPreviewControl),
            new FrameworkPropertyMetadata(0.0));

    public static readonly DependencyProperty CurrentFocalXProperty =
        DependencyProperty.Register(nameof(CurrentFocalX), typeof(double), typeof(MultiMonitorPreviewControl),
            new FrameworkPropertyMetadata(0.5));

    public static readonly DependencyProperty CurrentFocalYProperty =
        DependencyProperty.Register(nameof(CurrentFocalY), typeof(double), typeof(MultiMonitorPreviewControl),
            new FrameworkPropertyMetadata(0.5));

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

    public IEnumerable<WidgetInstance>? Widgets
    {
        get => (IEnumerable<WidgetInstance>?)GetValue(WidgetsProperty);
        set => SetValue(WidgetsProperty, value);
    }

    public bool ShowWidgets
    {
        get => (bool)GetValue(ShowWidgetsProperty);
        set => SetValue(ShowWidgetsProperty, value);
    }

    public bool IsWidgetEditMode
    {
        get => (bool)GetValue(IsWidgetEditModeProperty);
        set => SetValue(IsWidgetEditModeProperty, value);
    }

    public string? SelectedWidgetId
    {
        get => (string?)GetValue(SelectedWidgetIdProperty);
        set => SetValue(SelectedWidgetIdProperty, value);
    }

    public bool EnableSnapToGrid
    {
        get => (bool)GetValue(EnableSnapToGridProperty);
        set => SetValue(EnableSnapToGridProperty, value);
    }

    public int GridSizePx
    {
        get => (int)GetValue(GridSizePxProperty);
        set => SetValue(GridSizePxProperty, value);
    }

    public bool IsBoxSelectMode
    {
        get => (bool)GetValue(IsBoxSelectModeProperty);
        set => SetValue(IsBoxSelectModeProperty, value);
    }

    public double CurrentZoom
    {
        get => (double)GetValue(CurrentZoomProperty);
        set => SetValue(CurrentZoomProperty, value);
    }

    public double CurrentPanX
    {
        get => (double)GetValue(CurrentPanXProperty);
        set => SetValue(CurrentPanXProperty, value);
    }

    public double CurrentPanY
    {
        get => (double)GetValue(CurrentPanYProperty);
        set => SetValue(CurrentPanYProperty, value);
    }

    public double CurrentFocalX
    {
        get => (double)GetValue(CurrentFocalXProperty);
        set => SetValue(CurrentFocalXProperty, value);
    }

    public double CurrentFocalY
    {
        get => (double)GetValue(CurrentFocalYProperty);
        set => SetValue(CurrentFocalYProperty, value);
    }

    public event Action<double, double>? PanDeltaRequested;
    public event Action<double>? ZoomDeltaRequested;
    public event Action<double, double, double>? TransformRequested;
    public event Action<bool>? WallpaperTransformCommitted;
    public event Action? ResetRequested;
    public event Action<string>? MonitorClicked;
    public event Action<WidgetInstance, string, double, double>? WidgetMovedInStudio;
    public event Action<IReadOnlyList<WidgetInstance>>? WidgetsDraggingInStudio;
    public event Action<IReadOnlyList<WidgetInstance>>? WidgetsMovedInStudio;
    public event Action<WidgetInstance>? WidgetClickedInStudio;

    private enum WallpaperInteractionKind { None, Move, Resize }

    private sealed record WidgetDragSnapshot(WidgetInstance Widget, Rect StartScreenRect);

    private readonly Dictionary<string, BitmapSource> _loadedBitmaps = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<(BitmapSource Source, bool FlipH, bool FlipV), BitmapSource> _flippedBitmaps = new();
    private readonly Dictionary<string, Rect> _monitorScreenRects = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<(WidgetInstance Widget, Rect ScreenRect)> _widgetScreenRects = new();
    private readonly HashSet<string> _selectedWidgetIds = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<(WallpaperResizeAnchor Anchor, Rect HitRect, Cursor HandleCursor)> _wallpaperHandles = new();

    private bool _isDragging;
    private bool _didMoveDuringDrag;
    private bool _wallpaperChangedDuringDrag;
    private bool _isWallpaperSelected;
    private bool _isMarqueeSelecting;
    private Point _marqueeStart;
    private Point _marqueeEnd;
    private HashSet<string> _marqueeBaseSelection = new(StringComparer.OrdinalIgnoreCase);

    private WidgetInstance? _draggingWidget;
    private WidgetInstance? _collapseToSingleWidgetOnClick;
    private Rect _leadWidgetStartRect;
    private readonly List<WidgetDragSnapshot> _dragGroupStartSnapshots = new();

    private WallpaperInteractionKind _wallpaperDragKind = WallpaperInteractionKind.None;
    private WallpaperResizeAnchor _activeResizeAnchor;
    private RectD _resizeStartImgRect;
    private double _resizeStartZoom;
    private double _resizeStartFocalX;
    private double _resizeStartFocalY;
    private Rect _lastWallpaperScreenRect;

    private Point _dragStartPoint;
    private Point _lastDragPoint;
    private double _lastViewportScale = 1.0;

    public MultiMonitorPreviewControl()
    {
        ClipToBounds = true;
        Focusable = true;
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

        if (_loadedBitmaps.Count > 4)
        {
            _loadedBitmaps.Clear();
            _flippedBitmaps.Clear();
        }

        bool changed = false;
        foreach (string p in paths)
        {
            if (_loadedBitmaps.ContainsKey(p))
                continue;

            var bmp = await ImageCacheHelper.LoadBitmapAsync(
                p,
                decodePixelWidth: 2800,
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

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Key == Key.Escape && IsInteractive)
        {
            _selectedWidgetIds.Clear();
            SelectedWidgetId = null;
            _isWallpaperSelected = false;
            InvalidateVisual();
            e.Handled = true;
        }
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        if (!IsInteractive) return;
        Focus();

        if (e.ClickCount == 2)
        {
            Point dblPos = e.GetPosition(this);
            bool onWidget = ShowWidgets && _widgetScreenRects.Any(x => x.ScreenRect.Contains(dblPos));
            if (!onWidget)
            {
                ResetRequested?.Invoke();
                WallpaperTransformCommitted?.Invoke(false);
                return;
            }
        }

        Point pos = e.GetPosition(this);
        _didMoveDuringDrag = false;
        _wallpaperChangedDuringDrag = false;
        _collapseToSingleWidgetOnClick = null;
        _draggingWidget = null;
        _dragGroupStartSnapshots.Clear();
        _isMarqueeSelecting = false;
        _wallpaperDragKind = WallpaperInteractionKind.None;

        // 1. Wallpaper resize handles take top priority when wallpaper is already selected
        if (_isWallpaperSelected && !IsBoxSelectMode && Plan != null && Plan.Mode != WallpaperPlacementMode.Individual)
        {
            foreach (var handle in _wallpaperHandles)
            {
                if (handle.HitRect.Contains(pos))
                {
                    _wallpaperDragKind = WallpaperInteractionKind.Resize;
                    _activeResizeAnchor = handle.Anchor;
                    _resizeStartImgRect = Plan.ImageCanvasRect;
                    _resizeStartZoom = CurrentZoom;
                    _resizeStartFocalX = CurrentFocalX;
                    _resizeStartFocalY = CurrentFocalY;
                    _dragStartPoint = pos;
                    _lastDragPoint = pos;
                    _isDragging = true;
                    CaptureMouse();
                    return;
                }
            }
        }

        // 2. Widget single or multi-select + group drag inside Placement Studio
        if (ShowWidgets && !IsBoxSelectMode)
        {
            for (int i = _widgetScreenRects.Count - 1; i >= 0; i--)
            {
                var (wInst, wRect) = _widgetScreenRects[i];
                if (!wRect.Contains(pos))
                    continue;

                bool modifierHeld = (Keyboard.Modifiers & (ModifierKeys.Control | ModifierKeys.Shift)) != 0;
                _isWallpaperSelected = false;

                if (modifierHeld)
                {
                    if (_selectedWidgetIds.Contains(wInst.InstanceId))
                        _selectedWidgetIds.Remove(wInst.InstanceId);
                    else
                        _selectedWidgetIds.Add(wInst.InstanceId);

                    SelectedWidgetId = _selectedWidgetIds.Contains(wInst.InstanceId)
                        ? wInst.InstanceId
                        : _selectedWidgetIds.LastOrDefault();
                    if (SelectedWidgetId != null)
                    {
                        var sel = _widgetScreenRects.FirstOrDefault(x => string.Equals(x.Widget.InstanceId, SelectedWidgetId, StringComparison.OrdinalIgnoreCase)).Widget;
                        if (sel != null) WidgetClickedInStudio?.Invoke(sel);
                    }
                    InvalidateVisual();
                }
                else if (_selectedWidgetIds.Contains(wInst.InstanceId) && _selectedWidgetIds.Count > 1)
                {
                    // Keep multi-selection active so dragging immediately moves all selected widgets together
                    _collapseToSingleWidgetOnClick = wInst;
                    SelectedWidgetId = wInst.InstanceId;
                    WidgetClickedInStudio?.Invoke(wInst);
                    InvalidateVisual();
                }
                else
                {
                    _selectedWidgetIds.Clear();
                    _selectedWidgetIds.Add(wInst.InstanceId);
                    SelectedWidgetId = wInst.InstanceId;
                    WidgetClickedInStudio?.Invoke(wInst);
                    InvalidateVisual();
                }

                if (_selectedWidgetIds.Contains(wInst.InstanceId))
                {
                    _draggingWidget = wInst;
                    _leadWidgetStartRect = wRect;
                    foreach (var pair in _widgetScreenRects)
                    {
                        if (_selectedWidgetIds.Contains(pair.Widget.InstanceId))
                            _dragGroupStartSnapshots.Add(new WidgetDragSnapshot(pair.Widget, pair.ScreenRect));
                    }
                    _dragStartPoint = pos;
                    _lastDragPoint = pos;
                    _isDragging = true;
                    CaptureMouse();
                }
                return;
            }
        }

        // 3. Marquee / Rubber-band box selection for multi-selecting widgets
        bool insideAnyMonitor = _monitorScreenRects.Values.Any(r => r.Contains(pos));
        if (ShowWidgets && (IsBoxSelectMode || (Keyboard.Modifiers & ModifierKeys.Shift) != 0 || !insideAnyMonitor))
        {
            _isWallpaperSelected = false;
            _isMarqueeSelecting = true;
            _marqueeStart = pos;
            _marqueeEnd = pos;
            bool addToExisting = (Keyboard.Modifiers & ModifierKeys.Control) != 0;
            _marqueeBaseSelection = addToExisting
                ? new HashSet<string>(_selectedWidgetIds, StringComparer.OrdinalIgnoreCase)
                : new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            if (!addToExisting)
            {
                _selectedWidgetIds.Clear();
                SelectedWidgetId = null;
            }

            _dragStartPoint = pos;
            _isDragging = true;
            CaptureMouse();
            InvalidateVisual();
            return;
        }

        // 4. Clicked wallpaper inside monitor(s): select wallpaper (show transform box + resize handles) and start move
        _selectedWidgetIds.Clear();
        SelectedWidgetId = null;
        _isWallpaperSelected = true;
        _wallpaperDragKind = WallpaperInteractionKind.Move;

        foreach (var kv in _monitorScreenRects)
        {
            if (kv.Value.Contains(pos))
            {
                SelectedMonitorId = kv.Key;
                MonitorClicked?.Invoke(kv.Key);
                break;
            }
        }

        _dragStartPoint = pos;
        _lastDragPoint = pos;
        _isDragging = true;
        CaptureMouse();
        InvalidateVisual();
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        Point pos = e.GetPosition(this);

        if (!_isDragging)
        {
            UpdateHoverCursor(pos);
            return;
        }

        if (!IsInteractive || Plan == null || _lastViewportScale <= 0)
            return;

        if (_isMarqueeSelecting)
        {
            _marqueeEnd = pos;
            var box = new Rect(_marqueeStart, _marqueeEnd);
            _selectedWidgetIds.Clear();
            foreach (string id in _marqueeBaseSelection)
                _selectedWidgetIds.Add(id);
            foreach (var (w, wRect) in _widgetScreenRects)
            {
                if (box.IntersectsWith(wRect))
                    _selectedWidgetIds.Add(w.InstanceId);
            }
            SelectedWidgetId = _selectedWidgetIds.LastOrDefault();
            InvalidateVisual();
            return;
        }

        if (_draggingWidget != null && _dragGroupStartSnapshots.Count > 0)
        {
            double rawDxScreen = pos.X - _dragStartPoint.X;
            double rawDyScreen = pos.Y - _dragStartPoint.Y;
            if (Math.Abs(rawDxScreen) > 2 || Math.Abs(rawDyScreen) > 2)
                _didMoveDuringDrag = true;

            double leadCandX = _leadWidgetStartRect.X + rawDxScreen;
            double leadCandY = _leadWidgetStartRect.Y + rawDyScreen;
            Point leadCenter = new(leadCandX + _leadWidgetStartRect.Width * 0.5, leadCandY + _leadWidgetStartRect.Height * 0.5);
            var leadSlice = FindTargetSliceForScreenPoint(leadCenter) ?? Plan.Slices[0];
            if (!_monitorScreenRects.TryGetValue(leadSlice.Monitor.Id, out var leadMRect) || leadMRect.Width <= 1 || leadMRect.Height <= 1)
                return;

            double leadSx = Math.Max(96, leadSlice.Monitor.DpiX) / 96.0;
            double leadSy = Math.Max(96, leadSlice.Monitor.DpiY) / 96.0;
            double leadMonDipW = leadSlice.Monitor.Bounds.Width / leadSx;
            double leadMonDipH = leadSlice.Monitor.Bounds.Height / leadSy;

            double leadDipX = Math.Clamp(((leadCandX - leadMRect.X) / leadMRect.Width) * leadMonDipW, 0, Math.Max(0, leadMonDipW - 48));
            double leadDipY = Math.Clamp(((leadCandY - leadMRect.Y) / leadMRect.Height) * leadMonDipH, 0, Math.Max(0, leadMonDipH - 32));

            if (EnableSnapToGrid && GridSizePx > 1)
            {
                leadDipX = Math.Round(leadDipX / GridSizePx) * GridSizePx;
                leadDipY = Math.Round(leadDipY / GridSizePx) * GridSizePx;
            }

            double snappedLeadScreenX = leadMRect.X + (leadDipX / leadMonDipW) * leadMRect.Width;
            double snappedLeadScreenY = leadMRect.Y + (leadDipY / leadMonDipH) * leadMRect.Height;
            double effDxScreen = snappedLeadScreenX - _leadWidgetStartRect.X;
            double effDyScreen = snappedLeadScreenY - _leadWidgetStartRect.Y;

            var movedList = new List<WidgetInstance>(_dragGroupStartSnapshots.Count);
            foreach (var snap in _dragGroupStartSnapshots)
            {
                double sxScreen = snap.StartScreenRect.X + effDxScreen;
                double syScreen = snap.StartScreenRect.Y + effDyScreen;
                Point wCenter = new(sxScreen + snap.StartScreenRect.Width * 0.5, syScreen + snap.StartScreenRect.Height * 0.5);
                var slice = FindTargetSliceForScreenPoint(wCenter) ?? leadSlice;
                if (!_monitorScreenRects.TryGetValue(slice.Monitor.Id, out var mRect) || mRect.Width <= 1 || mRect.Height <= 1)
                    continue;

                double sx = Math.Max(96, slice.Monitor.DpiX) / 96.0;
                double sy = Math.Max(96, slice.Monitor.DpiY) / 96.0;
                double monDipW = slice.Monitor.Bounds.Width / sx;
                double monDipH = slice.Monitor.Bounds.Height / sy;

                double dipX = Math.Clamp(((sxScreen - mRect.X) / mRect.Width) * monDipW, 0, Math.Max(0, monDipW - 48));
                double dipY = Math.Clamp(((syScreen - mRect.Y) / mRect.Height) * monDipH, 0, Math.Max(0, monDipH - 32));

                snap.Widget.TargetMonitorAnchor = ResolveAnchorLabel(slice.Monitor, Plan.Slices);
                snap.Widget.X = Math.Round(dipX, 1);
                snap.Widget.Y = Math.Round(dipY, 1);
                movedList.Add(snap.Widget);
            }

            InvalidateVisual();
            if (movedList.Count > 0)
            {
                WidgetsDraggingInStudio?.Invoke(movedList);
                if (movedList.Count == 1)
                    WidgetMovedInStudio?.Invoke(movedList[0], movedList[0].TargetMonitorAnchor, movedList[0].X, movedList[0].Y);
            }
            return;
        }

        if (_wallpaperDragKind == WallpaperInteractionKind.Resize)
        {
            double dxCanvas = (pos.X - _dragStartPoint.X) / _lastViewportScale;
            double dyCanvas = (pos.Y - _dragStartPoint.Y) / _lastViewportScale;
            if (Math.Abs(pos.X - _dragStartPoint.X) > 1.5 || Math.Abs(pos.Y - _dragStartPoint.Y) > 1.5)
                _wallpaperChangedDuringDrag = true;

            var (newZoom, newPanX, newPanY) = WallpaperGeometryEngine.ComputeResizedTransform(
                _resizeStartImgRect,
                Plan.CombinedCanvasWidth,
                Plan.CombinedCanvasHeight,
                _resizeStartZoom,
                _resizeStartFocalX,
                _resizeStartFocalY,
                dxCanvas,
                dyCanvas,
                _activeResizeAnchor);

            if (TransformRequested != null)
                TransformRequested.Invoke(newZoom, newPanX, newPanY);
            else
                ZoomDeltaRequested?.Invoke(newZoom - CurrentZoom);

            return;
        }

        double dxScreen = pos.X - _lastDragPoint.X;
        double dyScreen = pos.Y - _lastDragPoint.Y;
        _lastDragPoint = pos;

        if (Math.Abs(dxScreen) < 0.01 && Math.Abs(dyScreen) < 0.01)
            return;

        _wallpaperChangedDuringDrag = true;
        double deltaCanvasX = dxScreen / _lastViewportScale;
        double deltaCanvasY = dyScreen / _lastViewportScale;

        double normDx = Plan.CombinedCanvasWidth > 0 ? deltaCanvasX / Plan.CombinedCanvasWidth : 0;
        double normDy = Plan.CombinedCanvasHeight > 0 ? deltaCanvasY / Plan.CombinedCanvasHeight : 0;

        PanDeltaRequested?.Invoke(normDx, normDy);
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        if (!_isDragging) return;

        bool wasMarquee = _isMarqueeSelecting;
        var draggedLead = _draggingWidget;
        var movedGroup = _dragGroupStartSnapshots.Select(s => s.Widget).ToList();
        var collapseTarget = _collapseToSingleWidgetOnClick;
        bool moved = _didMoveDuringDrag;
        bool wallpaperChanged = _wallpaperChangedDuringDrag;

        _isDragging = false;
        _isMarqueeSelecting = false;
        _draggingWidget = null;
        _collapseToSingleWidgetOnClick = null;
        _dragGroupStartSnapshots.Clear();
        _wallpaperDragKind = WallpaperInteractionKind.None;
        _didMoveDuringDrag = false;
        _wallpaperChangedDuringDrag = false;
        ReleaseMouseCapture();

        if (wasMarquee)
        {
            if (SelectedWidgetId != null)
            {
                var sel = _widgetScreenRects.FirstOrDefault(x => string.Equals(x.Widget.InstanceId, SelectedWidgetId, StringComparison.OrdinalIgnoreCase)).Widget;
                if (sel != null) WidgetClickedInStudio?.Invoke(sel);
            }
            InvalidateVisual();
            return;
        }

        if (draggedLead != null)
        {
            if (moved && movedGroup.Count > 0)
            {
                WidgetsMovedInStudio?.Invoke(movedGroup);
            }
            else if (!moved && collapseTarget != null)
            {
                _selectedWidgetIds.Clear();
                _selectedWidgetIds.Add(collapseTarget.InstanceId);
                SelectedWidgetId = collapseTarget.InstanceId;
                WidgetClickedInStudio?.Invoke(collapseTarget);
                InvalidateVisual();
            }
            return;
        }

        if (wallpaperChanged)
        {
            WallpaperTransformCommitted?.Invoke(false);
        }
    }

    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        base.OnMouseWheel(e);
        if (!IsInteractive) return;
        double delta = e.Delta > 0 ? 0.08 : -0.08;
        ZoomDeltaRequested?.Invoke(delta);
        WallpaperTransformCommitted?.Invoke(true);
        e.Handled = true;
    }

    private void UpdateHoverCursor(Point pos)
    {
        if (!IsInteractive)
        {
            Cursor = Cursors.Arrow;
            return;
        }

        if (_isWallpaperSelected && !IsBoxSelectMode)
        {
            foreach (var h in _wallpaperHandles)
            {
                if (h.HitRect.Contains(pos))
                {
                    Cursor = h.HandleCursor;
                    return;
                }
            }
        }

        if (ShowWidgets && !IsBoxSelectMode && _widgetScreenRects.Any(x => x.ScreenRect.Contains(pos)))
        {
            Cursor = Cursors.SizeAll;
            return;
        }

        if (IsBoxSelectMode || (Keyboard.Modifiers & ModifierKeys.Shift) != 0)
        {
            Cursor = Cursors.Cross;
            return;
        }

        Cursor = _isWallpaperSelected && _monitorScreenRects.Values.Any(r => r.Contains(pos))
            ? Cursors.SizeAll
            : Cursors.Hand;
    }

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        _monitorScreenRects.Clear();
        _widgetScreenRects.Clear();
        _wallpaperHandles.Clear();
        _lastWallpaperScreenRect = Rect.Empty;

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

            BitmapSource? rawBmp = ResolveBitmap(slice.SourceImagePath);
            BitmapSource? bmp = rawBmp != null
                ? GetOrientedPreviewBitmap(rawBmp, slice.FlipHorizontal, slice.FlipVertical)
                : null;
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

            if (ShowWidgets && IsWidgetEditMode && EnableSnapToGrid && GridSizePx > 1)
            {
                double sx = Math.Max(96, slice.Monitor.DpiX) / 96.0;
                double sy = Math.Max(96, slice.Monitor.DpiY) / 96.0;
                double monDipW = slice.Monitor.Bounds.Width / sx;
                double monDipH = slice.Monitor.Bounds.Height / sy;
                int visualStep = GridSizePx;
                while ((visualStep / monDipW) * mRect.Width < 10 && visualStep < 256)
                    visualStep *= 2;

                var snapPen = new Pen(new SolidColorBrush(Color.FromArgb(42, 96, 165, 250)), 0.7);
                for (double gxDip = visualStep; gxDip < monDipW; gxDip += visualStep)
                {
                    double xPx = mRect.X + (gxDip / monDipW) * mRect.Width;
                    dc.DrawLine(snapPen, new Point(xPx, mRect.Y), new Point(xPx, mRect.Bottom));
                }
                for (double gyDip = visualStep; gyDip < monDipH; gyDip += visualStep)
                {
                    double yPx = mRect.Y + (gyDip / monDipH) * mRect.Height;
                    dc.DrawLine(snapPen, new Point(mRect.X, yPx), new Point(mRect.Right, yPx));
                }
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

        if (ShowWidgets && Widgets != null)
        {
            DrawWidgetOverlays(dc, plan, dpiScale);
        }

        // Draw selected wallpaper transform frame & 8 resize handles
        if (IsInteractive && _isWallpaperSelected && plan.Mode != WallpaperPlacementMode.Individual &&
            plan.ImageCanvasRect.Width > 1 && plan.ImageCanvasRect.Height > 1)
        {
            _lastWallpaperScreenRect = new Rect(
                originX + plan.ImageCanvasRect.X * scale,
                originY + plan.ImageCanvasRect.Y * scale,
                plan.ImageCanvasRect.Width * scale,
                plan.ImageCanvasRect.Height * scale);
            DrawWallpaperSelectionAndHandles(dc, _lastWallpaperScreenRect, w, h, dpiScale);
        }

        // Draw active marquee box when multi-selecting widgets
        if (_isMarqueeSelecting)
        {
            var mBox = new Rect(_marqueeStart, _marqueeEnd);
            dc.DrawRectangle(
                new SolidColorBrush(Color.FromArgb(42, 56, 189, 248)),
                new Pen(new SolidColorBrush(Color.FromArgb(220, 96, 165, 250)), 1.3) { DashStyle = new DashStyle(new double[] { 4, 3 }, 0) },
                mBox);
        }

        if (IsInteractive && ShowWidgets && _selectedWidgetIds.Count > 1)
        {
            var selFt = CreateText($"✓ {_selectedWidgetIds.Count} Widgets Selected · Drag any to move group (Esc clears)", 10.5, Colors.White, dpiScale, FontWeights.SemiBold);
            double bw = selFt.Width + 16;
            double bh = selFt.Height + 8;
            double bx = Math.Max(8, (w - bw) * 0.5);
            dc.DrawRoundedRectangle(new SolidColorBrush(Color.FromArgb(215, 2, 132, 199)), new Pen(new SolidColorBrush(Color.FromRgb(125, 211, 252)), 1), new Rect(bx, 6, bw, bh), 6, 6);
            dc.DrawText(selFt, new Point(bx + 8, 10));
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

    private BitmapSource GetOrientedPreviewBitmap(BitmapSource source, bool flipH, bool flipV)
    {
        if (!flipH && !flipV)
            return source;

        var key = (source, flipH, flipV);
        if (_flippedBitmaps.TryGetValue(key, out var cached))
            return cached;

        var transformed = new TransformedBitmap(source, new ScaleTransform(flipH ? -1 : 1, flipV ? -1 : 1));
        transformed.Freeze();
        _flippedBitmaps[key] = transformed;
        return transformed;
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
            if (!string.IsNullOrWhiteSpace(hex))
            {
                string norm = hex.Trim();
                if (!norm.StartsWith('#'))
                    norm = "#" + norm;
                if (ColorConverter.ConvertFromString(norm) is Color c)
                    return c;
            }
        }
        catch { }
        return Color.FromRgb(13, 16, 23);
    }

    private void DrawWidgetOverlays(DrawingContext dc, CompositionPlan plan, double dpiScale)
    {
        if (Widgets == null) return;

        foreach (var w in Widgets)
        {
            if (!w.IsVisible) continue;
            var slice = ResolveMonitorSliceForAnchor(w.TargetMonitorAnchor, plan.Slices);
            if (slice == null || !_monitorScreenRects.TryGetValue(slice.Monitor.Id, out var mRect))
                continue;

            double sx = Math.Max(96, slice.Monitor.DpiX) / 96.0;
            double sy = Math.Max(96, slice.Monitor.DpiY) / 96.0;
            double monDipW = slice.Monitor.Bounds.Width / sx;
            double monDipH = slice.Monitor.Bounds.Height / sy;
            if (monDipW <= 1 || monDipH <= 1) continue;

            double rx = mRect.X + (w.X / monDipW) * mRect.Width;
            double ry = mRect.Y + (w.Y / monDipH) * mRect.Height;
            double rw = Math.Max(22, (w.Width / monDipW) * mRect.Width);
            double rh = Math.Max(14, (w.Height / monDipH) * mRect.Height);

            var wRect = new Rect(rx, ry, rw, rh);
            _widgetScreenRects.Add((w, wRect));

            bool isSelected = _selectedWidgetIds.Contains(w.InstanceId) ||
                string.Equals(SelectedWidgetId, w.InstanceId, StringComparison.OrdinalIgnoreCase);
            Color fillCol = isSelected
                ? Color.FromArgb(165, 30, 58, 138)
                : (IsWidgetEditMode ? Color.FromArgb(145, 15, 23, 42) : Color.FromArgb(105, 15, 23, 42));
            Color strokeCol = isSelected
                ? Color.FromRgb(96, 165, 250)
                : (IsWidgetEditMode ? Color.FromRgb(52, 211, 153) : Color.FromArgb(185, 226, 232, 240));

            var pen = new Pen(new SolidColorBrush(strokeCol), isSelected ? 2.0 : 1.1);
            if (!isSelected)
                pen.DashStyle = new DashStyle(new double[] { 3, 2 }, 0);

            dc.DrawRoundedRectangle(new SolidColorBrush(fillCol), pen, wRect, 5, 5);

            if (isSelected && IsInteractive)
            {
                var dotBrush = new SolidColorBrush(Color.FromRgb(147, 197, 253));
                dc.DrawEllipse(dotBrush, null, new Point(wRect.Left, wRect.Top), 2.5, 2.5);
                dc.DrawEllipse(dotBrush, null, new Point(wRect.Right, wRect.Top), 2.5, 2.5);
                dc.DrawEllipse(dotBrush, null, new Point(wRect.Left, wRect.Bottom), 2.5, 2.5);
                dc.DrawEllipse(dotBrush, null, new Point(wRect.Right, wRect.Bottom), 2.5, 2.5);
            }

            if (rw > 34 && rh > 16)
            {
                dc.PushClip(new RectangleGeometry(new Rect(wRect.X + 3, wRect.Y + 2, Math.Max(1, wRect.Width - 6), Math.Max(1, wRect.Height - 4)), 3, 3));
                var ft = CreateText(w.Title, 9.5, Colors.White, dpiScale, FontWeights.SemiBold);
                dc.DrawText(ft, new Point(wRect.X + 5, wRect.Y + 3));
                dc.Pop();
            }
        }
    }

    private MonitorSlicePlan? FindTargetSliceForScreenPoint(Point screenPos)
    {
        if (Plan == null) return null;
        foreach (var slice in Plan.Slices)
        {
            if (_monitorScreenRects.TryGetValue(slice.Monitor.Id, out var r) && r.Contains(screenPos))
                return slice;
        }

        // Nearest monitor center fallback if dragging over bezel gap
        return Plan.Slices
            .OrderBy(s =>
            {
                if (!_monitorScreenRects.TryGetValue(s.Monitor.Id, out var r)) return double.MaxValue;
                double cx = r.X + r.Width * 0.5;
                double cy = r.Y + r.Height * 0.5;
                return (cx - screenPos.X) * (cx - screenPos.X) + (cy - screenPos.Y) * (cy - screenPos.Y);
            })
            .FirstOrDefault();
    }

    private static MonitorSlicePlan? ResolveMonitorSliceForAnchor(string anchor, IReadOnlyList<MonitorSlicePlan> slices)
    {
        if (slices.Count == 0) return null;
        if (string.Equals(anchor, "LEFT", StringComparison.OrdinalIgnoreCase))
            return slices.OrderBy(s => s.Monitor.Bounds.X).First();
        if (string.Equals(anchor, "RIGHT", StringComparison.OrdinalIgnoreCase))
            return slices.OrderByDescending(s => s.Monitor.Bounds.X).First();

        var byId = slices.FirstOrDefault(s => string.Equals(s.Monitor.Id, anchor, StringComparison.OrdinalIgnoreCase));
        if (byId != null) return byId;

        return slices.FirstOrDefault(s => s.Monitor.IsPrimary) ?? slices[0];
    }

    private void DrawWallpaperSelectionAndHandles(DrawingContext dc, Rect r, double viewW, double viewH, double dpiScale)
    {
        var framePen = new Pen(new SolidColorBrush(Color.FromArgb(225, 56, 189, 248)), 1.6)
        {
            DashStyle = new DashStyle(new double[] { 5, 3 }, 0)
        };
        dc.DrawRectangle(null, framePen, r);

        double cx = Math.Clamp(r.X + r.Width * 0.5, 10, Math.Max(10, viewW - 10));
        double cy = Math.Clamp(r.Y + r.Height * 0.5, 10, Math.Max(10, viewH - 10));

        var points = new (WallpaperResizeAnchor Anchor, Point Pt, Cursor Cur)[]
        {
            (WallpaperResizeAnchor.TopLeft, new Point(r.Left, r.Top), Cursors.SizeNWSE),
            (WallpaperResizeAnchor.Top, new Point(cx, r.Top), Cursors.SizeNS),
            (WallpaperResizeAnchor.TopRight, new Point(r.Right, r.Top), Cursors.SizeNESW),
            (WallpaperResizeAnchor.Right, new Point(r.Right, cy), Cursors.SizeWE),
            (WallpaperResizeAnchor.BottomRight, new Point(r.Right, r.Bottom), Cursors.SizeNWSE),
            (WallpaperResizeAnchor.Bottom, new Point(cx, r.Bottom), Cursors.SizeNS),
            (WallpaperResizeAnchor.BottomLeft, new Point(r.Left, r.Bottom), Cursors.SizeNESW),
            (WallpaperResizeAnchor.Left, new Point(r.Left, cy), Cursors.SizeWE),
        };

        var fill = Brushes.White;
        var stroke = new Pen(new SolidColorBrush(Color.FromRgb(2, 132, 199)), 1.5);
        foreach (var (anchor, pt, cur) in points)
        {
            var drawRect = new Rect(pt.X - 4.5, pt.Y - 4.5, 9, 9);
            var hitRect = new Rect(pt.X - 8.5, pt.Y - 8.5, 17, 17);
            _wallpaperHandles.Add((anchor, hitRect, cur));
            dc.DrawRoundedRectangle(fill, stroke, drawRect, 2, 2);
        }

        var tipFt = CreateText("Wallpaper Selected · Drag to move · Drag handles to resize", 10, Colors.White, dpiScale, FontWeights.SemiBold);
        double bw = tipFt.Width + 14;
        double bh = tipFt.Height + 6;
        dc.DrawRoundedRectangle(new SolidColorBrush(Color.FromArgb(200, 15, 23, 42)), new Pen(new SolidColorBrush(Color.FromArgb(140, 56, 189, 248)), 0.9), new Rect(8, 6, bw, bh), 5, 5);
        dc.DrawText(tipFt, new Point(15, 9));
    }

    private static string ResolveAnchorLabel(MonitorInfo mon, IReadOnlyList<MonitorSlicePlan> slices)
    {
        if (mon.IsPrimary) return "PRIMARY";
        if (slices.Count >= 2)
        {
            var left = slices.OrderBy(s => s.Monitor.Bounds.X).First().Monitor;
            if (string.Equals(left.Id, mon.Id, StringComparison.OrdinalIgnoreCase)) return "LEFT";
            var right = slices.OrderByDescending(s => s.Monitor.Bounds.X).First().Monitor;
            if (string.Equals(right.Id, mon.Id, StringComparison.OrdinalIgnoreCase)) return "RIGHT";
        }
        return mon.Id;
    }
}
