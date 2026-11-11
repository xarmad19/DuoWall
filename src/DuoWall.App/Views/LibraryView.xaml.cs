using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using DuoWall.App.ViewModels;

namespace DuoWall.App.Views;

public partial class LibraryView : UserControl
{
    public LibraryView()
    {
        InitializeComponent();
    }

    private void Tab_Click(object sender, RoutedEventArgs e)
    {
        if (sender is RadioButton rb && rb.Tag is string tab && DataContext is MainViewModel vm)
            vm.SelectedLibraryTab = tab;
    }

    private void Card_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.DataContext is WallpaperCardViewModel card && DataContext is MainViewModel vm)
            _ = vm.OpenDetailAsync(card);
    }
}

