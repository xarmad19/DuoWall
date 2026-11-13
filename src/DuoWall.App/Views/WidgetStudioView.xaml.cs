using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using DuoWall.App.Services;
using DuoWall.App.ViewModels;
using DuoWall.Core.Theming;
using DuoWall.Core.Widgets;

namespace DuoWall.App.Views;

public partial class WidgetStudioView : UserControl
{
    private WidgetStudioViewModel? _vm;
    private DesktopOverlayCoordinator? _coordinator;

    public WidgetStudioView()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => AttachViewModel();
        ActiveWidgetsListBox.PreviewMouseLeftButtonUp += ActiveWidgetsListBox_PreviewMouseLeftButtonUp;
    }

    public void AttachCoordinator(DesktopOverlayCoordinator coordinator)
    {
        _coordinator = coordinator;
        RefreshLivePreviews();
    }

    private void AttachViewModel()
    {
        if (_vm != null)
            _vm.PreviewSurfaceInvalidated -= RefreshLivePreviews;

        _vm = DataContext as WidgetStudioViewModel;
        if (_vm != null)
        {
            _vm.PreviewSurfaceInvalidated += RefreshLivePreviews;
            RefreshLivePreviews();
        }
    }

    private void ActiveWidgetsListBox_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_vm == null) return;

        // Do not intercept inline Edit/Delete button clicks inside the row
        DependencyObject? dep = e.OriginalSource as DependencyObject;
        while (dep != null && !ReferenceEquals(dep, ActiveWidgetsListBox))
        {
            if (dep is ButtonBase) return;
            if (dep is ListBoxItem item && item.DataContext is WidgetInstance clickedWidget)
            {
                _vm.SelectedWidget = clickedWidget;
                _vm.IsWidgetConfigOpen = true;
                return;
            }
            dep = dep is Visual or System.Windows.Media.Media3D.Visual3D
                ? VisualTreeHelper.GetParent(dep)
                : LogicalTreeHelper.GetParent(dep);
        }
    }

    public void RefreshLivePreviews()
    {
        if (_vm == null) return;

        var globalGlass = _vm.LiveGlassStyle;
        var wx = _coordinator?.LatestWeather;
        var wxStatus = _coordinator?.WeatherStatus;
        var telemetry = _coordinator?.LatestTelemetry;
        var todos = _coordinator != null ? new List<TodoTaskItem>(_coordinator.Todos) : new List<TodoTaskItem>();

        var selected = _vm.SelectedWidget ?? new WidgetInstance
        {
            Kind = WidgetKind.DigitalClock,
            Title = "Digital Clock",
            UseGlobalGlassTheme = true,
            GlassStyle = globalGlass
        };

        GlassmorphismStyle selectedGlass = selected.UseGlobalGlassTheme ? globalGlass : selected.GlassStyle;
        // Generate real Skia blurred wallpaper slice matching the selected widget's live BlurIntensity
        ImageSource? selectedBlurSlice = _vm.CreateStudioBlurredWidgetSlice(
            selected.X, selected.Y, selected.Width, selected.Height, selectedGlass);

        StudioSelectedWidgetPreview.Bind(
            selected,
            selectedGlass,
            isHardwareBackdropActive: false,
            fallbackBackdropSource: selectedBlurSlice,
            weather: wx,
            weatherStatus: wxStatus,
            telemetry: telemetry,
            todos: todos,
            videoPlaying: false,
            videoStatus: "Preview");

        var sampleClock = new WidgetInstance
        {
            InstanceId = "preview-clock",
            Kind = WidgetKind.DigitalClock,
            Title = "Glass Clock Sample",
            UseGlobalGlassTheme = true,
            GlassStyle = globalGlass
        };
        ImageSource? clockSlice = _vm.CreateStudioBlurredWidgetSlice(420, 140, 390, 155, globalGlass);
        GlassPreviewClockSample.Bind(sampleClock, globalGlass, false, clockSlice, wx, wxStatus, telemetry, todos);

        var sampleStats = new WidgetInstance
        {
            InstanceId = "preview-stats",
            Kind = WidgetKind.SystemStats,
            Title = "Telemetry HUD Sample",
            UseGlobalGlassTheme = true,
            GlassStyle = globalGlass,
            Config = new Dictionary<string, string> { ["StatsLayout"] = "Detailed" }
        };
        ImageSource? statsSlice = _vm.CreateStudioBlurredWidgetSlice(420, 320, 390, 220, globalGlass);
        GlassPreviewStatsSample.Bind(sampleStats, globalGlass, false, statsSlice, wx, wxStatus, telemetry, todos);
    }
}
