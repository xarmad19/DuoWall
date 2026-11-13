using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DuoWall.App.Services;
using DuoWall.Core.Models;
using DuoWall.Core.Packages;
using DuoWall.Core.Storage;
using DuoWall.Core.Theming;
using DuoWall.Core.Widgets;
using Microsoft.Win32;

namespace DuoWall.App.ViewModels;

public sealed partial class ProfilesViewModel : ObservableObject
{
    private readonly DuoWallDatabase _db;
    private readonly WallpaperColorAnalyzer _colorAnalyzer;
    private readonly WidgetPackageService _packageService;
    private readonly DesktopOverlayCoordinator _coordinator;
    private readonly Func<string> _getCurrentTopologySignature;
    private readonly Action<DesktopProfile> _onActiveProfileChanged;
    private readonly Action<GlassmorphismStyle> _onGlassStyleSuggested;
    private readonly Action<string, bool> _setStatus;

    private AppSettings _settings = new();
    private WallpaperColorPalette? _lastPalette;
    private bool _suppressEditorSync;

    public ProfilesViewModel(
        DuoWallDatabase db,
        WallpaperColorAnalyzer colorAnalyzer,
        WidgetPackageService packageService,
        DesktopOverlayCoordinator coordinator,
        Func<string> getCurrentTopologySignature,
        Action<DesktopProfile> onActiveProfileChanged,
        Action<GlassmorphismStyle> onGlassStyleSuggested,
        Action<string, bool> setStatus)
    {
        _db = db;
        _colorAnalyzer = colorAnalyzer;
        _packageService = packageService;
        _coordinator = coordinator;
        _getCurrentTopologySignature = getCurrentTopologySignature;
        _onActiveProfileChanged = onActiveProfileChanged;
        _onGlassStyleSuggested = onGlassStyleSuggested;
        _setStatus = setStatus;
    }

    // ==================== PROFILES & AUTO-SWITCHING ====================
    public ObservableCollection<DesktopProfile> Profiles { get; } = new();
    public ObservableCollection<ProfileTrigger> SelectedProfileTriggers { get; } = new();
    public IReadOnlyList<ProfileTriggerType> TriggerTypes { get; } = Enum.GetValues<ProfileTriggerType>();
    public IReadOnlyList<TriggerPowerCondition> PowerConditions { get; } = Enum.GetValues<TriggerPowerCondition>();
    public IReadOnlyList<string> GlassPresetNames { get; } = GlassmorphismStyle.BuiltInPresets.Select(p => p.Name).ToArray();
    public IReadOnlyList<WallpaperColorAdaptationMode> AdaptationModes { get; } = Enum.GetValues<WallpaperColorAdaptationMode>();

    [ObservableProperty] private DesktopProfile? _selectedProfile;
    [ObservableProperty] private ProfileTrigger? _selectedTrigger;

    [ObservableProperty] private bool _enableAutomaticSwitching;
    [ObservableProperty] private bool _lockCurrentProfileManually;
    [ObservableProperty] private string _activeProfileTitle = "Work Profile";
    [ObservableProperty] private string _autoSwitchStatusText = "Manual profile selection active";

    // Selected Profile metadata
    [ObservableProperty] private string _editName = string.Empty;
    [ObservableProperty] private string _editDescription = string.Empty;
    [ObservableProperty] private bool _editIsEnabled;
    [ObservableProperty] private int _editPriority = 20;
    [ObservableProperty] private string _editGlassPresetName = "Frosted Glass";
    [ObservableProperty] private double _editBrightness = 1.0;
    [ObservableProperty] private bool _editMinimalAnimations;

    // Trigger rule builder inputs
    [ObservableProperty] private ProfileTriggerType _newTriggerType = ProfileTriggerType.TimeOfDay;
    [ObservableProperty] private string _newTriggerStart = "09:00";
    [ObservableProperty] private string _newTriggerEnd = "18:00";
    [ObservableProperty] private TriggerPowerCondition _newTriggerPower = TriggerPowerCondition.Any;
    [ObservableProperty] private string _newTriggerTopologyPattern = string.Empty;
    [ObservableProperty] private bool _dayMon = true;
    [ObservableProperty] private bool _dayTue = true;
    [ObservableProperty] private bool _dayWed = true;
    [ObservableProperty] private bool _dayThu = true;
    [ObservableProperty] private bool _dayFri = true;
    [ObservableProperty] private bool _daySat;
    [ObservableProperty] private bool _daySun;

    // ==================== WALLPAPER-AWARE COLOR ADAPTATION ====================
    public ObservableCollection<WallpaperColorPalette> SavedPalettes { get; } = new();

    [ObservableProperty] private WallpaperColorAdaptationMode _colorAdaptationMode = WallpaperColorAdaptationMode.Suggested;
    [ObservableProperty] private WallpaperColorPalette? _currentPalette;
    [ObservableProperty] private string _paletteDominantHex = "#1E293B";
    [ObservableProperty] private string _paletteSecondaryHex = "#334155";
    [ObservableProperty] private string _paletteAccentHex = "#60A5FA";
    [ObservableProperty] private string _paletteAnalysisSummary = "Apply or analyze a wallpaper to extract ambient palette & WCAG contrast.";
    [ObservableProperty] private string _paletteContrastBadge = "WCAG AA Contrast Verified";
    [ObservableProperty] private string _customThemeSaveName = "Wallpaper Match Theme";

    public void Initialize(AppSettings settings)
    {
        _settings = settings;
        EnableAutomaticSwitching = settings.EnableAutomaticProfileSwitching;
        LockCurrentProfileManually = !string.IsNullOrWhiteSpace(settings.ManualProfileOverrideId);
        ColorAdaptationMode = settings.ColorAdaptationMode;

        ReloadProfilesAndPalettes();

        if (!string.IsNullOrWhiteSpace(settings.LastAppliedImagePath) && File.Exists(settings.LastAppliedImagePath))
        {
            AnalyzeWallpaperPath(settings.LastAppliedImagePath, applyAccordingToMode: false);
        }
    }

    public DesktopProfile? GetActiveProfile()
    {
        return Profiles.FirstOrDefault(p => string.Equals(p.ProfileId, _settings.ActiveDesktopProfileId, StringComparison.OrdinalIgnoreCase))
               ?? Profiles.FirstOrDefault();
    }

    public void ReloadProfilesAndPalettes()
    {
        string prevSel = SelectedProfile?.ProfileId ?? _settings.ActiveDesktopProfileId;
        Profiles.Clear();
        foreach (var p in _db.LoadDesktopProfilesEnsureDefaults())
            Profiles.Add(p);

        SelectedProfile = Profiles.FirstOrDefault(p => string.Equals(p.ProfileId, prevSel, StringComparison.OrdinalIgnoreCase))
                          ?? Profiles.FirstOrDefault();

        var active = GetActiveProfile();
        ActiveProfileTitle = active?.Name ?? "Default";

        SavedPalettes.Clear();
        foreach (var pal in _db.LoadColorPalettes())
            SavedPalettes.Add(pal);
    }

    public void HandleWallpaperApplied(string? imagePath)
    {
        if (string.IsNullOrWhiteSpace(imagePath) || !File.Exists(imagePath))
        {
            return;
        }

        AnalyzeWallpaperPath(imagePath, applyAccordingToMode: true);
    }

    public void HandleAutoProfileSwitched(DesktopProfile profile, string reason)
    {
        ActiveProfileTitle = profile.Name;
        AutoSwitchStatusText = $"Auto-active: {profile.Name} ({reason})";
        var inList = Profiles.FirstOrDefault(p => p.ProfileId == profile.ProfileId);
        if (inList != null)
        {
            SelectedProfile = inList;
        }
        _onActiveProfileChanged(profile);
    }

    private void AnalyzeWallpaperPath(string imagePath, bool applyAccordingToMode)
    {
        try
        {
            var palette = _colorAnalyzer.AnalyzeFile(imagePath, Path.GetFileNameWithoutExtension(imagePath));
            bool significant = WallpaperColorAnalyzer.IsSignificantlyDifferent(_lastPalette, palette);
            _lastPalette = palette;

            CurrentPalette = palette;
            PaletteDominantHex = palette.DominantHex;
            PaletteSecondaryHex = palette.SecondaryHex;
            PaletteAccentHex = palette.AccentHex;

            double ratio = WallpaperColorAnalyzer.ContrastRatio(
                palette.SuggestedStyle.TextPrimaryHex,
                palette.SuggestedStyle.BackgroundTintHex);

            PaletteAnalysisSummary =
                $"{(palette.IsDarkWallpaper ? "Dark" : "Light")} backdrop • Lum {palette.AverageLuminance:0.00} • {palette.Temperature} ({palette.EstimatedKelvin}K)";
            PaletteContrastBadge = $"Text Contrast {ratio:0.0}:1  •  Suggested: {palette.SuggestedStyle.Name}";

            if (applyAccordingToMode && ColorAdaptationMode == WallpaperColorAdaptationMode.Automatic && significant)
            {
                _onGlassStyleSuggested(palette.SuggestedStyle);
                _setStatus($"Wallpaper-aware style adapted ({palette.Temperature}, {palette.EstimatedKelvin}K) without moving widgets.", false);
            }
            else if (applyAccordingToMode && ColorAdaptationMode == WallpaperColorAdaptationMode.Suggested)
            {
                _setStatus("Wallpaper palette analyzed — preview & apply suggested widget colors in Desktop Profiles.", false);
            }
        }
        catch (Exception ex)
        {
            PaletteAnalysisSummary = $"Color analysis skipped: {ex.Message}";
        }
    }

    // ==================== PROFILE COMMANDS ====================

    [RelayCommand]
    private void ActivateSelectedProfile()
    {
        if (SelectedProfile == null) return;
        SaveSelectedProfileChanges(silent: true);

        _settings.ActiveDesktopProfileId = SelectedProfile.ProfileId;
        if (LockCurrentProfileManually)
            _settings.ManualProfileOverrideId = SelectedProfile.ProfileId;
        _db.SaveSettings(_settings);

        ActiveProfileTitle = SelectedProfile.Name;
        AutoSwitchStatusText = LockCurrentProfileManually
            ? $"Locked manually to {SelectedProfile.Name}"
            : $"Active profile: {SelectedProfile.Name}";

        _onActiveProfileChanged(SelectedProfile);
        _setStatus($"Activated desktop profile '{SelectedProfile.Name}'.", false);
    }

    [RelayCommand]
    private void CreateCustomProfile()
    {
        var profile = new DesktopProfile
        {
            ProfileId = $"custom-{Guid.NewGuid().ToString("N")[..8]}",
            Name = $"Custom Layout {Profiles.Count + 1}",
            Description = "User-defined multi-monitor widget layout and automation triggers.",
            IsBuiltIn = false,
            IsEnabled = false,
            Priority = 50,
            GlassPresetName = "Frosted Glass",
            Widgets = SelectedProfile?.Widgets.Select(w => w.Clone(newInstanceId: true)).ToList() ?? new List<WidgetInstance>()
        };

        _db.SaveDesktopProfile(profile);
        ReloadProfilesAndPalettes();
        SelectedProfile = Profiles.FirstOrDefault(p => p.ProfileId == profile.ProfileId);
        _setStatus($"Created custom profile '{profile.Name}'.", false);
    }

    [RelayCommand]
    private void DeleteSelectedProfile()
    {
        if (SelectedProfile == null || SelectedProfile.IsBuiltIn)
        {
            _setStatus("Built-in profile presets cannot be deleted (disable auto-switching instead).", true);
            return;
        }

        string name = SelectedProfile.Name;
        _db.DeleteDesktopProfile(SelectedProfile.ProfileId);
        ReloadProfilesAndPalettes();
        _setStatus($"Deleted custom profile '{name}'.", false);
    }

    [RelayCommand]
    private void SaveSelectedProfile() => SaveSelectedProfileChanges(silent: false);

    [RelayCommand]
    private void UseCurrentMonitorTopology()
    {
        NewTriggerTopologyPattern = _getCurrentTopologySignature();
    }

    [RelayCommand]
    private void AddTriggerRule()
    {
        if (SelectedProfile == null) return;

        TimeSpan start = TimeSpan.TryParseExact(NewTriggerStart, @"hh\:mm", CultureInfo.InvariantCulture, out var st) ? st : TimeSpan.FromHours(9);
        TimeSpan end = TimeSpan.TryParseExact(NewTriggerEnd, @"hh\:mm", CultureInfo.InvariantCulture, out var en) ? en : TimeSpan.FromHours(18);

        var days = new List<DayOfWeek>();
        if (DayMon) days.Add(DayOfWeek.Monday);
        if (DayTue) days.Add(DayOfWeek.Tuesday);
        if (DayWed) days.Add(DayOfWeek.Wednesday);
        if (DayThu) days.Add(DayOfWeek.Thursday);
        if (DayFri) days.Add(DayOfWeek.Friday);
        if (DaySat) days.Add(DayOfWeek.Saturday);
        if (DaySun) days.Add(DayOfWeek.Sunday);

        var trigger = new ProfileTrigger
        {
            IsEnabled = true,
            TriggerType = NewTriggerType,
            StartTime = start,
            EndTime = end,
            DaysOfWeek = days,
            PowerCondition = NewTriggerPower,
            MonitorTopologyPattern = string.IsNullOrWhiteSpace(NewTriggerTopologyPattern) ? null : NewTriggerTopologyPattern.Trim()
        };

        SelectedProfile.Triggers.Add(trigger);
        SelectedProfileTriggers.Add(trigger);
        _db.SaveDesktopProfile(SelectedProfile);
        _setStatus($"Added {trigger.TriggerType} rule to '{SelectedProfile.Name}'.", false);
    }

    [RelayCommand]
    private void RemoveSelectedTrigger()
    {
        if (SelectedProfile == null || SelectedTrigger == null) return;
        SelectedProfile.Triggers.RemoveAll(t => t.Id == SelectedTrigger.Id);
        SelectedProfileTriggers.Remove(SelectedTrigger);
        _db.SaveDesktopProfile(SelectedProfile);
        SelectedTrigger = SelectedProfileTriggers.FirstOrDefault();
    }

    // ==================== COLOR ADAPTATION COMMANDS ====================

    [RelayCommand]
    private void AnalyzeCurrentWallpaperNow()
    {
        if (string.IsNullOrWhiteSpace(_settings.LastAppliedImagePath) || !File.Exists(_settings.LastAppliedImagePath))
        {
            _setStatus("Apply or stage a wallpaper first so DuoWall can analyze its colors.", true);
            return;
        }

        AnalyzeWallpaperPath(_settings.LastAppliedImagePath, applyAccordingToMode: false);
        _setStatus("Analyzed current wallpaper palette & luminance.", false);
    }

    [RelayCommand]
    private void ApplySuggestedPaletteToWidgets()
    {
        if (CurrentPalette?.SuggestedStyle == null)
        {
            _setStatus("No analyzed wallpaper palette yet.", true);
            return;
        }

        _onGlassStyleSuggested(CurrentPalette.SuggestedStyle.Clone());
        _setStatus("Applied wallpaper-adapted Glass colors without altering widget positions or sizes.", false);
    }

    [RelayCommand]
    private void SaveCurrentPaletteAsCustomTheme()
    {
        if (CurrentPalette == null)
        {
            _setStatus("Analyze a wallpaper first before saving a palette theme.", true);
            return;
        }

        var copy = new WallpaperColorPalette
        {
            Id = $"pal-{Guid.NewGuid().ToString("N")[..8]}",
            Name = string.IsNullOrWhiteSpace(CustomThemeSaveName) ? CurrentPalette.Name : CustomThemeSaveName.Trim(),
            SourceWallpaperPath = CurrentPalette.SourceWallpaperPath,
            DominantHex = CurrentPalette.DominantHex,
            SecondaryHex = CurrentPalette.SecondaryHex,
            AccentHex = CurrentPalette.AccentHex,
            AverageLuminance = CurrentPalette.AverageLuminance,
            IsDarkWallpaper = CurrentPalette.IsDarkWallpaper,
            Temperature = CurrentPalette.Temperature,
            EstimatedKelvin = CurrentPalette.EstimatedKelvin,
            SuggestedStyle = CurrentPalette.SuggestedStyle.Clone(
                newId: $"pal-glass-{Guid.NewGuid().ToString("N")[..6]}",
                newName: string.IsNullOrWhiteSpace(CustomThemeSaveName) ? "Wallpaper Match" : CustomThemeSaveName.Trim(),
                isBuiltIn: false),
            AnalyzedUtc = DateTime.UtcNow
        };

        _db.SaveColorPalette(copy);
        _db.SaveCustomGlassPreset(copy.SuggestedStyle);
        SavedPalettes.Insert(0, copy);
        _setStatus($"Saved custom wallpaper color theme '{copy.Name}'.", false);
    }

    [RelayCommand]
    private void ApplySavedPalette(WallpaperColorPalette? palette)
    {
        if (palette == null) return;
        _onGlassStyleSuggested(palette.SuggestedStyle.Clone());
        _setStatus($"Applied saved theme '{palette.Name}' to widgets.", false);
    }

    [RelayCommand]
    private async Task ExportCurrentPalettePackageAsync()
    {
        if (CurrentPalette == null) return;
        var dlg = new SaveFileDialog
        {
            Title = "Export Wallpaper Color Theme Package",
            Filter = "DualWall Package (*.dwwp)|*.dwwp",
            FileName = $"{CustomThemeSaveName.Trim().Replace(' ', '_')}.theme.dwwp"
        };
        if (dlg.ShowDialog() != true) return;

        var manifest = new DualWallWidgetPackageManifest
        {
            Id = $"user.theme.{Guid.NewGuid().ToString("N")[..8]}",
            Name = string.IsNullOrWhiteSpace(CustomThemeSaveName) ? CurrentPalette.Name : CustomThemeSaveName.Trim(),
            Author = Environment.UserName,
            Description = $"Wallpaper-aware theme ({CurrentPalette.Temperature}, {CurrentPalette.EstimatedKelvin}K).",
            Category = "Imported Packages",
            PackageType = PackageKind.ColorPalette,
            ColorPalette = CurrentPalette,
            GlassStyle = CurrentPalette.SuggestedStyle
        };

        await _packageService.ExportPackageAsync(manifest, dlg.FileName);
        _setStatus($"Exported theme package '{Path.GetFileName(dlg.FileName)}'.", false);
    }

    // ==================== PROPERTY HOOKS ====================

    partial void OnEnableAutomaticSwitchingChanged(bool value)
    {
        _settings.EnableAutomaticProfileSwitching = value;
        _db.SaveSettings(_settings);
        AutoSwitchStatusText = value ? "Automatic rule evaluation enabled" : "Automatic switching paused";
    }

    partial void OnLockCurrentProfileManuallyChanged(bool value)
    {
        _settings.ManualProfileOverrideId = value ? _settings.ActiveDesktopProfileId : null;
        _db.SaveSettings(_settings);
        AutoSwitchStatusText = value ? $"Locked manually to {ActiveProfileTitle}" : "Manual override cleared";
    }

    partial void OnColorAdaptationModeChanged(WallpaperColorAdaptationMode value)
    {
        _settings.ColorAdaptationMode = value;
        _db.SaveSettings(_settings);
    }

    partial void OnSelectedProfileChanged(DesktopProfile? value)
    {
        if (value == null) return;
        _suppressEditorSync = true;
        EditName = value.Name;
        EditDescription = value.Description;
        EditIsEnabled = value.IsEnabled;
        EditPriority = value.Priority;
        EditGlassPresetName = value.GlassPresetName;
        EditBrightness = value.WidgetBrightness;
        EditMinimalAnimations = value.MinimalAnimations;

        SelectedProfileTriggers.Clear();
        foreach (var t in value.Triggers)
            SelectedProfileTriggers.Add(t);
        SelectedTrigger = SelectedProfileTriggers.FirstOrDefault();
        _suppressEditorSync = false;
    }

    private void SaveSelectedProfileChanges(bool silent)
    {
        if (_suppressEditorSync || SelectedProfile == null) return;

        SelectedProfile.Name = string.IsNullOrWhiteSpace(EditName) ? SelectedProfile.Name : EditName.Trim();
        SelectedProfile.Description = EditDescription;
        SelectedProfile.IsEnabled = EditIsEnabled;
        SelectedProfile.Priority = Math.Clamp(EditPriority, 1, 999);
        SelectedProfile.GlassPresetName = EditGlassPresetName;
        SelectedProfile.WidgetBrightness = Math.Clamp(EditBrightness, 0.25, 1.2);
        SelectedProfile.MinimalAnimations = EditMinimalAnimations;

        _db.SaveDesktopProfile(SelectedProfile);
        if (string.Equals(_settings.ActiveDesktopProfileId, SelectedProfile.ProfileId, StringComparison.OrdinalIgnoreCase))
        {
            _coordinator.RebuildOverlays();
        }

        if (!silent)
        {
            _setStatus($"Saved profile '{SelectedProfile.Name}' ({SelectedProfile.Triggers.Count} trigger rules).", false);
        }
    }
}
