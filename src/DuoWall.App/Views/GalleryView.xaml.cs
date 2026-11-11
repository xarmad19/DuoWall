using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using DuoWall.App.ViewModels;

namespace DuoWall.App.Views;

public partial class GalleryView : UserControl
{
    public GalleryView()
    {
        InitializeComponent();
    }

    private void Card_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.DataContext is WallpaperCardViewModel card && DataContext is MainViewModel vm)
        {
            _ = vm.OpenDetailAsync(card);
        }
    }

    private async void LoadMore_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel vm && !vm.IsLoadingGallery && vm.HasMoreGalleryItems)
            await vm.LoadGalleryAsync(reset: false);
    }

    private async void GalleryScroll_Changed(object sender, ScrollChangedEventArgs e)
    {
        if (sender is ScrollViewer sv && DataContext is MainViewModel vm)
        {
            if (sv.ScrollableHeight > 0 &&
                sv.VerticalOffset >= sv.ScrollableHeight - 280 &&
                !vm.IsLoadingGallery &&
                vm.HasMoreGalleryItems)
            {
                await vm.LoadGalleryAsync(reset: false);
            }
        }
    }
}

