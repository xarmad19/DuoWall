using System.Windows.Controls;
using DuoWall.App.ViewModels;

namespace DuoWall.App.Views;

public partial class MonitorsView : UserControl
{
    public MonitorsView()
    {
        InitializeComponent();
        MonitorEditorPreview.PanDeltaRequested += (dx, dy) => (DataContext as MainViewModel)?.UpdatePanByDelta(dx, dy);
        MonitorEditorPreview.ZoomDeltaRequested += dz => (DataContext as MainViewModel)?.UpdateZoomByDelta(dz);
        MonitorEditorPreview.ResetRequested += () => (DataContext as MainViewModel)?.ResetPlacement();
    }
}

