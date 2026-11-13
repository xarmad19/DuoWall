using System;
using System.Collections.Generic;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DuoWall.App.Windows;
using DuoWall.Core.Models;
using DuoWall.Core.Storage;
using DuoWall.Core.Theming;

namespace DuoWall.App.ViewModels;

public sealed partial class TaskbarTweaksViewModel : ObservableObject
{
    private readonly DuoWallDatabase _db;
    private readonly TaskbarTweaksService _service;
    private readonly Func<GlassmorphismStyle> _getLiveGlassStyle;
    private readonly Action<string, bool> _setStatus;

    private AppSettings _settings = new();
    private bool _suppressPush;

    public TaskbarTweaksViewModel(
        DuoWallDatabase db,
        TaskbarTweaksService service,
        Func<GlassmorphismStyle> getLiveGlassStyle,
        Action<string, bool> setStatus)
    {
        _db = db;
        _service = service;
        _getLiveGlassStyle = getLiveGlassStyle;
        _setStatus = setStatus;
    }

    public IReadOnlyList<TaskbarDisplayMode> DisplayTargetOptions { get; } = Enum.GetValues<TaskbarDisplayMode>();

    [ObservableProperty] private bool _enableTaskbarGlass;
    [ObservableProperty] private bool _makeTaskbarInvisible;
    [ObservableProperty] private bool _centerTaskbarIcons;
    [ObservableProperty] private bool _enableTaskbarGlassDock = true;
    [ObservableProperty] private TaskbarDisplayMode _taskbarDisplayTarget = TaskbarDisplayMode.Both;
    [ObservableProperty] private bool _autoHideTaskbar;
    [ObservableProperty] private double _taskbarAcrylicOpacity = 0.62;
    [ObservableProperty] private double _taskbarDockExtraWidthPx = 16.0;
    [ObservableProperty] private double _taskbarDockHeightPx = 36.0;
    [ObservableProperty] private string _activeGlassSummary = "Frosted Glass";

    public void Initialize(AppSettings settings)
    {
        _settings = settings;
        _suppressPush = true;
        EnableTaskbarGlass = settings.EnableTaskbarGlass;
        MakeTaskbarInvisible = settings.MakeTaskbarInvisible;
        CenterTaskbarIcons = settings.CenterTaskbarIcons;
        EnableTaskbarGlassDock = settings.EnableTaskbarGlassDock;
        TaskbarDisplayTarget = settings.TaskbarDisplayTarget;
        AutoHideTaskbar = settings.AutoHideTaskbar || TaskbarTweaksService.GetTaskbarAutoHideState();
        TaskbarAcrylicOpacity = Math.Clamp(settings.TaskbarAcrylicOpacity, 0.05, 0.95);
        TaskbarDockExtraWidthPx = Math.Clamp(settings.TaskbarDockExtraWidthPx, -16.0, 240.0);
        TaskbarDockHeightPx = Math.Clamp(settings.TaskbarDockHeightPx, 26.0, 76.0);
        ActiveGlassSummary = _getLiveGlassStyle().Name;
        _suppressPush = false;

        if (_service.IsAnyTweakActive || EnableTaskbarGlass || MakeTaskbarInvisible || CenterTaskbarIcons)
        {
            _service.UpdateConfiguration(_settings, _getLiveGlassStyle(), applyAutoHideFlag: false);
        }
    }

    public void RefreshGlassThemeChanged()
    {
        var style = _getLiveGlassStyle();
        ActiveGlassSummary = $"{style.Name} (Radius {style.CornerRadius:0}px)";
        if (EnableTaskbarGlass || (CenterTaskbarIcons && EnableTaskbarGlassDock))
        {
            _service.UpdateConfiguration(_settings, style, applyAutoHideFlag: false);
        }
    }

    [RelayCommand]
    private void ApplyMacOsGlassDockPreset()
    {
        _suppressPush = true;
        MakeTaskbarInvisible = true;
        CenterTaskbarIcons = true;
        EnableTaskbarGlassDock = true;
        EnableTaskbarGlass = false;
        _suppressPush = false;
        PushSettings(applyAutoHide: false);
        _setStatus("Enabled macOS-style Centered Glass Dock using your widget Glass & CornerRadius settings.", false);
    }

    [RelayCommand]
    private void ApplyFullGlassTaskbarPreset()
    {
        _suppressPush = true;
        EnableTaskbarGlass = true;
        MakeTaskbarInvisible = false;
        EnableTaskbarGlassDock = false;
        _suppressPush = false;
        PushSettings(applyAutoHide: false);
        _setStatus("Applied Universal Glass acrylic theme across your Windows taskbar.", false);
    }

    [RelayCommand]
    private void RestoreDefaultTaskbar()
    {
        _suppressPush = true;
        EnableTaskbarGlass = false;
        MakeTaskbarInvisible = false;
        CenterTaskbarIcons = false;
        EnableTaskbarGlassDock = false;
        TaskbarDisplayTarget = TaskbarDisplayMode.Both;
        AutoHideTaskbar = false;
        _suppressPush = false;

        _settings.EnableTaskbarGlass = false;
        _settings.MakeTaskbarInvisible = false;
        _settings.CenterTaskbarIcons = false;
        _settings.EnableTaskbarGlassDock = false;
        _settings.TaskbarDisplayTarget = TaskbarDisplayMode.Both;
        _settings.AutoHideTaskbar = false;
        _db.SaveSettings(_settings);

        _service.RestoreVisualTaskbarDefaults(restoreAutoHide: true);
        _setStatus("Restored default Windows taskbar appearance, alignment, and display visibility.", false);
    }

    partial void OnEnableTaskbarGlassChanged(bool value) => PushSettings(false);
    partial void OnMakeTaskbarInvisibleChanged(bool value) => PushSettings(false);
    partial void OnCenterTaskbarIconsChanged(bool value) => PushSettings(false);
    partial void OnEnableTaskbarGlassDockChanged(bool value) => PushSettings(false);
    partial void OnTaskbarDisplayTargetChanged(TaskbarDisplayMode value) => PushSettings(false);
    partial void OnAutoHideTaskbarChanged(bool value) => PushSettings(true);
    partial void OnTaskbarAcrylicOpacityChanged(double value) => PushSettings(false);
    partial void OnTaskbarDockExtraWidthPxChanged(double value) => PushSettings(false);
    partial void OnTaskbarDockHeightPxChanged(double value) => PushSettings(false);

    private void PushSettings(bool applyAutoHide)
    {
        if (_suppressPush) return;

        _settings.EnableTaskbarGlass = EnableTaskbarGlass;
        _settings.MakeTaskbarInvisible = MakeTaskbarInvisible;
        _settings.CenterTaskbarIcons = CenterTaskbarIcons;
        _settings.EnableTaskbarGlassDock = EnableTaskbarGlassDock;
        _settings.TaskbarDisplayTarget = TaskbarDisplayTarget;
        _settings.AutoHideTaskbar = AutoHideTaskbar;
        _settings.TaskbarAcrylicOpacity = Math.Clamp(TaskbarAcrylicOpacity, 0.05, 0.95);
        _settings.TaskbarDockExtraWidthPx = Math.Clamp(TaskbarDockExtraWidthPx, -16.0, 240.0);
        _settings.TaskbarDockHeightPx = Math.Clamp(TaskbarDockHeightPx, 26.0, 76.0);
        _db.SaveSettings(_settings);

        var style = _getLiveGlassStyle();
        ActiveGlassSummary = $"{style.Name} (Radius {style.CornerRadius:0}px)";
        _service.UpdateConfiguration(_settings, style, applyAutoHideFlag: applyAutoHide);
    }
}

