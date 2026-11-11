using System.Windows.Controls;
using DuoWall.App.ViewModels;

namespace DuoWall.App.Views;

public partial class StudioView : UserControl
{
    public StudioView()
    {
        InitializeComponent();
        StudioPreview.PanDeltaRequested += (dx, dy) => (DataContext as MainViewModel)?.UpdatePanByDelta(dx, dy);
        StudioPreview.ZoomDeltaRequested += dz => (DataContext as MainViewModel)?.UpdateZoomByDelta(dz);
        StudioPreview.ResetRequested += () => (DataContext as MainViewModel)?.ResetPlacement();
    }
}

