using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using DuoWall.App.Services;
using DuoWall.App.ViewModels;
using Wpf.Ui.Controls;

namespace DuoWall.App;

public partial class MainWindow : FluentWindow
{
    public MainWindow()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        UpdateVisiblePage(0);
    }

    public void AttachCoordinator(DesktopOverlayCoordinator coordinator)
    {
        ViewWidgetStudio.AttachCoordinator(coordinator);
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.OldValue is INotifyPropertyChanged oldVm)
            oldVm.PropertyChanged -= ViewModel_PropertyChanged;
        if (e.NewValue is INotifyPropertyChanged newVm)
            newVm.PropertyChanged += ViewModel_PropertyChanged;
        if (DataContext is MainViewModel vm)
            UpdateVisiblePage(vm.CurrentPageIndex);
    }

    private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.CurrentPageIndex) && DataContext is MainViewModel vm)
        {
            UpdateVisiblePage(vm.CurrentPageIndex);
        }
    }

    private void NavButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is RadioButton rb && int.TryParse(rb.Tag?.ToString(), out int idx) && DataContext is MainViewModel vm)
        {
            vm.CurrentPageIndex = idx;
            UpdateVisiblePage(idx);
        }
    }

    private void UpdateVisiblePage(int index)
    {
        ViewHome.Visibility = index == 0 ? Visibility.Visible : Visibility.Collapsed;
        ViewGallery.Visibility = index == 1 ? Visibility.Visible : Visibility.Collapsed;
        ViewStudio.Visibility = index == 2 ? Visibility.Visible : Visibility.Collapsed;
        ViewLibrary.Visibility = index == 3 ? Visibility.Visible : Visibility.Collapsed;
        ViewMonitors.Visibility = index == 4 ? Visibility.Visible : Visibility.Collapsed;
        ViewSettings.Visibility = index == 5 ? Visibility.Visible : Visibility.Collapsed;
        ViewWidgetStudio.Visibility = index == 6 ? Visibility.Visible : Visibility.Collapsed;
        ViewProfiles.Visibility = index == 7 ? Visibility.Visible : Visibility.Collapsed;
        ViewTaskbarTweaks.Visibility = index == 9 ? Visibility.Visible : Visibility.Collapsed;
    }
}
