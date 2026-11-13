using System.Windows.Controls;
using System.Windows.Input;
using DuoWall.App.ViewModels;

namespace DuoWall.App.Views;

public partial class LiveWallpaperView : UserControl
{
    public LiveWallpaperView()
    {
        InitializeComponent();
    }

    private void SearchBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && DataContext is LiveWallpaperViewModel vm)
        {
            if (vm.SearchYouTubeCommand.CanExecute(null))
                vm.SearchYouTubeCommand.Execute(null);
        }
    }
}

