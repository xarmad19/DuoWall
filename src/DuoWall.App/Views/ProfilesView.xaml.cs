using System.ComponentModel;
using System.Windows.Controls;
using System.Windows.Media;
using DuoWall.App.Controls;
using DuoWall.App.ViewModels;
using DuoWall.Core.Theming;

namespace DuoWall.App.Views;

public partial class ProfilesView : UserControl
{
    private ProfilesViewModel? _vm;

    public ProfilesView()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => AttachVm();
    }

    private void AttachVm()
    {
        if (_vm != null)
        {
            _vm.PropertyChanged -= OnVmPropertyChanged;
        }

        _vm = DataContext as ProfilesViewModel;
        if (_vm != null)
        {
            _vm.PropertyChanged += OnVmPropertyChanged;
            UpdateSuggestedPreview();
        }
    }

    private void OnVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(ProfilesViewModel.CurrentPalette) or null)
        {
            UpdateSuggestedPreview();
        }
    }

    private void UpdateSuggestedPreview()
    {
        if (_vm == null) return;
        var style = _vm.CurrentPalette?.SuggestedStyle ?? GlassmorphismStyle.BuiltInPresets[1];
        SuggestedGlassPreview.GlassStyle = style;
        SuggestedGlassPreview.ApplyVisualStyle();
        PreviewTitleText.Foreground = new SolidColorBrush(GlassSurfaceControl.ParseHex(style.TextPrimaryHex, Colors.White));
        PreviewSubText.Foreground = new SolidColorBrush(GlassSurfaceControl.ParseHex(style.TextSecondaryHex, Colors.LightGray));
    }
}

