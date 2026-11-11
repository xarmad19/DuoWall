using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using DuoWall.App.ViewModels;

namespace DuoWall.App.Views;

public partial class HomeView : UserControl
{
    public HomeView()
    {
        InitializeComponent();
        HomePreview.PanDeltaRequested += (dx, dy) => (DataContext as MainViewModel)?.UpdatePanByDelta(dx, dy);
        HomePreview.ZoomDeltaRequested += dz => (DataContext as MainViewModel)?.UpdateZoomByDelta(dz);
        HomePreview.ResetRequested += () => (DataContext as MainViewModel)?.ResetPlacement();
    }

    private void OpenStudio_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel vm) vm.CurrentPageIndex = 2;
    }

    private void OpenGallery_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel vm) vm.CurrentPageIndex = 1;
    }

    private void OpenLibrary_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel vm) vm.CurrentPageIndex = 3;
    }

    private void RecentCard_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.DataContext is WallpaperCardViewModel card && DataContext is MainViewModel vm)
        {
            vm.StageWallpaper(card, loadProfile: true);
            _ = card.EnsurePreviewLoadedAsync(vm.Settings.ThumbnailCacheDirectory);
        }
    }
}

