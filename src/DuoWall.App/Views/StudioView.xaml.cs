using System.Windows;
using System.Windows.Controls;
using DuoWall.App.ViewModels;

namespace DuoWall.App.Views;

public partial class StudioView : UserControl
{
    private MainViewModel? _vm;

    public StudioView()
    {
        InitializeComponent();
        StudioPreview.PanDeltaRequested += (dx, dy) => _vm?.UpdatePanByDelta(dx, dy);
        StudioPreview.ZoomDeltaRequested += dz => _vm?.UpdateZoomByDelta(dz);
        StudioPreview.TransformRequested += (z, px, py) => _vm?.UpdateTransform(z, px, py);
        StudioPreview.WallpaperTransformCommitted += isWheel => _vm?.ScheduleStudioWallpaperApply(isWheel ? 450 : 60);
        StudioPreview.ResetRequested += () => _vm?.ResetPlacement();
        StudioPreview.WidgetClickedInStudio += w =>
        {
            if (_vm != null) _vm.WidgetStudio.SelectedWidget = w;
        };
        StudioPreview.WidgetMovedInStudio += (w, anchor, dx, dy) =>
            _vm?.WidgetStudio.CommitWidgetMoveFromPlacementStudio(w, anchor, dx, dy);
        StudioPreview.WidgetsDraggingInStudio += list =>
            _vm?.WidgetStudio.PreviewWidgetPositionsLive(list);
        StudioPreview.WidgetsMovedInStudio += list =>
            _vm?.WidgetStudio.CommitWidgetsMoveFromPlacementStudio(list);

        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (_vm != null)
            _vm.WidgetStudio.PreviewSurfaceInvalidated -= OnWidgetsChanged;

        _vm = DataContext as MainViewModel;
        if (_vm != null)
        {
            _vm.WidgetStudio.PreviewSurfaceInvalidated += OnWidgetsChanged;
            StudioPreview.InvalidateVisual();
        }
    }

    private void OnWidgetsChanged() => StudioPreview.InvalidateVisual();
}
