using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DuoWall.App.Helpers;
using DuoWall.App.Services;
using DuoWall.App.Windows;
using DuoWall.Core.Models;
using DuoWall.Core.Packages;
using DuoWall.Core.Storage;
using DuoWall.Core.Theming;
using DuoWall.Core.Widgets;
using Microsoft.Win32;

namespace DuoWall.App.ViewModels;

public sealed partial class WidgetStudioViewModel : ObservableObject
{
    private readonly DuoWallDatabase _db;
    private readonly WidgetPackageService _packageService;
    private readonly LocalWidgetMarketplaceProvider _marketplaceProvider;
    private readonly DesktopOverlayCoordinator _coordinator;
    private readonly Action<string, bool> _setStatus;
    private readonly Action _saveSettingsAndRefreshProfiles;
    private readonly WallpaperColorAnalyzer _colorAnalyzer = new();

    private AppSettings _settings = new();
    private DesktopProfile? _activeProfile;
    private bool _suppressGlassPush;
    private bool _suppressWidgetPush;

    public WidgetStudioViewModel(
        DuoWallDatabase db,
        WidgetPackageService packageService,
        LocalWidgetMarketplaceProvider marketplaceProvider,
        DesktopOverlayCoordinator coordinator,
        Action<string, bool> setStatus,
        Action saveSettingsAndRefreshProfiles)
    {
        _db = db;
        _packageService = packageService;
        _marketplaceProvider = marketplaceProvider;
        _coordinator = coordinator;
        _setStatus = setStatus;
        _saveSettingsAndRefreshProfiles = saveSettingsAndRefreshProfiles;

        var zones = new List<string> { "Local", "UTC" };
        zones.AddRange(TimeZoneInfo.GetSystemTimeZones()
            .Select(t => t.Id)
            .Where(id => !string.Equals(id, "UTC", StringComparison.OrdinalIgnoreCase)));
        TimeZoneOptions = zones;

        var preferred = new[] { "Segoe UI Variable Display", "Segoe UI Light", "Segoe UI", "Bahnschrift", "Cascadia Code", "Consolas", "Georgia", "Impact" };
        var systemFonts = System.Windows.Media.Fonts.SystemFontFamilies
            .Select(f => f.Source)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(s => s, StringComparer.OrdinalIgnoreCase);
        FontFamilyOptions = preferred.Concat(systemFonts).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    public event Action? PreviewSurfaceInvalidated;
    public event Action<string>? AccentColorChanged;

    // ==================== STUDIO TABS & LOCK / DESKTOP OVERLAY STATE ====================
    [ObservableProperty] private int _studioSectionIndex; // 0 = Widgets & Config, 1 = Glass & Surface Engine, 2 = Widget Marketplace
    [ObservableProperty] private bool _enableDesktopWidgets = true;
    [ObservableProperty] private bool _isDesktopEditMode;
    [ObservableProperty] private string _layoutLockLabel = "Layout LOCKED (Drag Disabled)";
    [ObservableProperty] private bool _showWidgetsInPlacementStudio = true;
    [ObservableProperty] private bool _enableSnapToGrid = true;
    [ObservableProperty] private int _gridSizePx = 16;
    [ObservableProperty] private string _activeProfileBadge = "Work Profile";
    [ObservableProperty] private ImageSource? _sharpPreviewBackdrop;

    // 3a Universal Accent Color & Wallpaper Extraction
    public IReadOnlyList<string> PresetAccentSwatches { get; } = new[]
    {
        "#FF38BDF8", "#FFA855F7", "#FF10B981", "#FFF59E0B", "#FFEF4444", "#FFEC4899", "#FF6366F1", "#FFFFFFFF"
    };
    [ObservableProperty] private string _widgetAccentColorHex = "#FF38BDF8";
    [ObservableProperty] private bool _autoAccentFromWallpaper = true;

    // ==================== ACTIVE PROFILE WIDGETS & CONFIG WINDOW ====================
    public ObservableCollection<WidgetInstance> Widgets { get; } = new();
    public IReadOnlyList<WidgetKind> AvailableWidgetKinds { get; } =
        Enum.GetValues<WidgetKind>().Where(k =>
            k != WidgetKind.AmbientGeometry &&
            k != WidgetKind.PanoramicVideo &&
            k != WidgetKind.CustomDeclarative).ToList();
    public IReadOnlyList<string> MonitorAnchors { get; } = new[] { "PRIMARY", "LEFT", "RIGHT" };
    public IReadOnlyList<WidgetSurfaceTheme> SurfaceThemes { get; } = Enum.GetValues<WidgetSurfaceTheme>();

    [ObservableProperty] private WidgetKind _newWidgetKind = WidgetKind.DigitalClock;
    [ObservableProperty] private WidgetInstance? _selectedWidget;
    [ObservableProperty] private string? _selectedWidgetId;
    [ObservableProperty] private bool _hasSelectedWidget;
    [ObservableProperty] private bool _isWidgetConfigOpen;

    // Visibility switches per configured widget kind
    [ObservableProperty] private bool _isConfigWeather;
    [ObservableProperty] private bool _isConfigClock;
    [ObservableProperty] private bool _isConfigAnalogClock;
    [ObservableProperty] private bool _isConfigSystemStats;
    [ObservableProperty] private bool _isConfigFocusTimer;
    [ObservableProperty] private bool _isConfigShortcutBar;
    [ObservableProperty] private bool _isConfigGenericOrCustom;

    // Common selected widget fields
    [ObservableProperty] private string _selectedWidgetTitle = string.Empty;
    [ObservableProperty] private string _selectedWidgetAnchor = "PRIMARY";
    [ObservableProperty] private double _selectedWidgetX = 40;
    [ObservableProperty] private double _selectedWidgetY = 40;
    [ObservableProperty] private double _selectedWidgetWidth = 340;
    [ObservableProperty] private double _selectedWidgetHeight = 160;
    [ObservableProperty] private bool _selectedWidgetVisible = true;
    [ObservableProperty] private bool _selectedWidgetClickThrough;
    [ObservableProperty] private bool _selectedWidgetMinimalAnim;
    [ObservableProperty] private bool _selectedWidgetUseGlobalGlass = true;
    [ObservableProperty] private double _selectedWidgetBrightness = 1.0;
    [ObservableProperty] private GlassmorphismStyle? _selectedWidgetPresetOverride;

    // Weather Configuration (#4)
    public IReadOnlyList<string> WeatherProviderOptions { get; } = new[]
    {
        "OpenMeteo (Free Keyless)",
        "OpenWeatherMap (API Key)",
        "WeatherAPI.com (API Key)"
    };
    public IReadOnlyList<string> WeatherUnitOptions { get; } = new[] { "metric (°C)", "imperial (°F)" };
    public IReadOnlyList<string> WeatherIconThemeOptions { get; } = new[] { "Animated Minimal", "Minimal Mono", "Classic Emoji" };
    [ObservableProperty] private string _configWeatherCity = "Lahore";
    [ObservableProperty] private string _configWeatherProvider = "OpenMeteo (Free Keyless)";
    [ObservableProperty] private string _configWeatherApiKey = string.Empty;
    [ObservableProperty] private string _configWeatherUnit = "metric (°C)";
    [ObservableProperty] private string _configWeatherIconTheme = "Animated Minimal";
    [ObservableProperty] private bool _configWeatherShowFeelsLike = true;
    [ObservableProperty] private bool _configWeatherShowHumidity = true;
    [ObservableProperty] private bool _configWeatherShowWind = true;
    [ObservableProperty] private bool _configWeatherShowProvider = true;

    // All Clock Configuration Options (#3h Typographic Clock + Digital/Analog)
    public IReadOnlyList<string> ClockModeOptions { get; } = new[] { "Digital Clock", "Analog Clock" };
    public IReadOnlyList<string> TimeZoneOptions { get; }
    public IReadOnlyList<string> DateFormatOptions { get; } = new[]
    {
        "dddd, MMM d yyyy", "ddd dd MMM", "yyyy-MM-dd", "MMMM dd, yyyy", "dd/MM/yyyy"
    };
    public IReadOnlyList<string> FontFamilyOptions { get; }
    public IReadOnlyList<string> AlignmentOptions { get; } = new[] { "Left", "Center", "Right" };
    public IReadOnlyList<string> VerticalAlignmentOptions { get; } = new[] { "Top", "Center", "Bottom" };
    public IReadOnlyList<string> AnalogDialOptions { get; } = new[] { "Minimal Ticks", "Numbers (12/3/6/9)", "Clean Dots" };
    public IReadOnlyList<string> ClockLayoutOptions { get; } = new[]
    {
        "Stacked (Header -> Time -> Date)",
        "Typographic Clock (Spaced Day -> Time -> Date)",
        "Split Horizontal (Time | Day & Date)",
        "Date Top -> Hero Time -> Day Footer",
        "Wide Split Header (Day Left / Date Right -> Hero Time)"
    };
    public IReadOnlyList<string> DayPlacementOptions { get; } = new[] { "Top", "Above Date", "Bottom", "Left of Time", "Right of Time", "Hidden" };
    public IReadOnlyList<string> DatePlacementOptions { get; } = new[] { "Bottom", "Top", "Inline with Day", "Hidden" };
    public IReadOnlyList<string> DayCaseStyleOptions { get; } = new[] { "Spaced Uppercase", "Uppercase", "Title Case", "Short Upper" };
    public IReadOnlyList<string> TimeFontWeightOptions { get; } = new[] { "Thin", "Light", "Normal", "SemiBold", "Bold", "Black" };

    [ObservableProperty] private string _configClockMode = "Digital Clock";
    [ObservableProperty] private string _configClockLayout = "Stacked (Header -> Time -> Date)";
    [ObservableProperty] private bool _configFormat24H;
    [ObservableProperty] private bool _configShowAmPm = true;
    [ObservableProperty] private bool _configShowSeconds;
    [ObservableProperty] private bool _configShowDay = true;
    [ObservableProperty] private string _configDayPlacement = "Top";
    [ObservableProperty] private string _configDayCaseStyle = "Spaced Uppercase";
    [ObservableProperty] private bool _configShowDate = true;
    [ObservableProperty] private string _configDatePlacement = "Bottom";
    [ObservableProperty] private string _configDateFormat = "dddd, MMM d yyyy";
    [ObservableProperty] private bool _configShowGreeting = true;
    [ObservableProperty] private string _configCustomGreeting = string.Empty;
    [ObservableProperty] private string _configTimeZoneId = "Local";
    [ObservableProperty] private string _configFontFamily = "Segoe UI Variable Display";
    [ObservableProperty] private string _configTimeFontFamily = string.Empty;
    [ObservableProperty] private string _configDayFontFamily = string.Empty;
    [ObservableProperty] private string _configDateFontFamily = string.Empty;
    [ObservableProperty] private string _configGreetingFontFamily = string.Empty;
    [ObservableProperty] private string _configTimeFontWeight = "Bold";
    [ObservableProperty] private double _configClockTimeBaseSize = 40.0;
    [ObservableProperty] private double _configFontScale = 1.0;
    [ObservableProperty] private double _configTimeScale = 1.0;
    [ObservableProperty] private double _configDayScale = 1.0;
    [ObservableProperty] private double _configDateScale = 1.0;
    [ObservableProperty] private double _configGreetingScale = 1.0;
    [ObservableProperty] private string _configAlignment = "Left";
    [ObservableProperty] private string _configVerticalAlignment = "Center";
    [ObservableProperty] private double _configClockTextShadow = 0.45;
    [ObservableProperty] private string _configClockTextPrimaryHex = string.Empty;
    [ObservableProperty] private string _configClockAccentHex = string.Empty;
    [ObservableProperty] private bool _configTimeUseAccent;
    [ObservableProperty] private string _configTimeColorHex = string.Empty;
    [ObservableProperty] private bool _configDayUseAccent = true;
    [ObservableProperty] private string _configDayColorHex = string.Empty;
    [ObservableProperty] private bool _configDateUseAccent;
    [ObservableProperty] private string _configDateColorHex = string.Empty;
    [ObservableProperty] private bool _configGreetingUseAccent;
    [ObservableProperty] private string _configGreetingColorHex = string.Empty;
    [ObservableProperty] private string _configAnalogDialStyle = "Minimal Ticks";

    // App & Web Shortcut / Dock Bar Configuration (#3g)
    public ObservableCollection<ShortcutDockItem> ShortcutItems { get; } = new();
    public IReadOnlyList<string> ShortcutLayoutOptions { get; } = new[] { "Horizontal", "Vertical" };
    public IReadOnlyList<string> ShortcutIconModes { get; } = new[] { "Auto", "Initials", "CustomIcon", "GlassGlyph" };
    public IReadOnlyList<string> ShortcutGlyphKeys => IconExtractorHelper.AvailableGlyphKeys;

    [ObservableProperty] private ShortcutDockItem? _selectedShortcutItem;
    [ObservableProperty] private string _configShortcutLayout = "Horizontal";
    [ObservableProperty] private double _configShortcutIconScale = 1.0;
    [ObservableProperty] private string _editShortcutLabel = "YouTube";
    [ObservableProperty] private string _editShortcutTarget = "https://www.youtube.com";
    [ObservableProperty] private string _editShortcutIconMode = "Initials";
    [ObservableProperty] private string _editShortcutInitials = "YT";
    [ObservableProperty] private string _editShortcutGlyphKey = "Globe";
    [ObservableProperty] private string _editShortcutColorHex = "#FF38BDF8";
    [ObservableProperty] private string _editShortcutCustomIconPath = string.Empty;
    private bool _suppressShortcutSync;

    // Focus Timer duration presets (#3d)
    public IReadOnlyList<string> FocusDurationPresetLabels { get; } = new[] { "15", "25", "45", "60", "90" };

    // Selectable System Telemetry Metrics (#6)
    public IReadOnlyList<string> StatsLayoutOptions { get; } = new[] { "Detailed", "Compact", "MinimalHUD" };
    [ObservableProperty] private string _configStatsLayout = "Detailed";
    [ObservableProperty] private bool _configStatsShowCpu = true;
    [ObservableProperty] private bool _configStatsShowRam = true;
    [ObservableProperty] private bool _configStatsShowNet = true;
    [ObservableProperty] private bool _configStatsShowDisk = true;
    [ObservableProperty] private bool _configStatsShowAllDrives;
    [ObservableProperty] private bool _configStatsShowGpu = true;
    [ObservableProperty] private bool _configStatsShowTemps = true;
    [ObservableProperty] private bool _configStatsShowUptime;
    [ObservableProperty] private bool _configStatsShowPower;

    // Focus Timer & Custom
    [ObservableProperty] private int _widgetFocusWorkMinutes = 25;
    [ObservableProperty] private int _widgetFocusBreakMinutes = 5;
    [ObservableProperty] private string _widgetCustomSubtitle = string.Empty;

    // Panoramic Video controls
    [ObservableProperty] private string _panoramicVideoPath = string.Empty;
    [ObservableProperty] private bool _loopPanoramicVideo = true;
    [ObservableProperty] private bool _pauseVideoOnBattery;
    [ObservableProperty] private bool _pauseVideoOnFullScreen = true;

    // ==================== CENTRALIZED SURFACE & GLASSMORPHISM ENGINE ====================
    public ObservableCollection<GlassmorphismStyle> GlassPresets { get; } = new();
    public ObservableCollection<GlassmorphismStyle> NonGlassRainmeterPresets { get; } = new();
    public ObservableCollection<GlassmorphismStyle> AllSelectableStylePresets { get; } = new();
    public IReadOnlyList<GlassAppearanceVariant> AppearanceVariants { get; } = Enum.GetValues<GlassAppearanceVariant>();
    public IReadOnlyList<string> QuickGlassSwatches { get; } = new[]
    {
        "#FFFFFF", "#F1F5F9", "#0F172A", "#1E293B", "#071029",
        "#0EA5E9", "#6366F1", "#EC4899", "#00F0FF", "#10B981"
    };

    [ObservableProperty] private GlassmorphismStyle? _selectedGlassPreset;
    [ObservableProperty] private string _customGlassPresetName = "My Custom Glass";

    [ObservableProperty] private WidgetSurfaceTheme _glassSurfaceTheme = WidgetSurfaceTheme.Glassmorphism;
    [ObservableProperty] private double _glassBackgroundTransparency = 0.72;
    [ObservableProperty] private string _glassBackgroundTintHex = "#F1F5F9";
    [ObservableProperty] private string _glassGradientSecondaryHex = "#CBD5E1";
    [ObservableProperty] private double _glassBackgroundOpacity = 0.22;
    [ObservableProperty] private double _glassBackgroundBlurIntensity = 26.0;
    [ObservableProperty] private bool _glassEnableBackdropBlur = true;
    [ObservableProperty] private double _glassBorderTransparency = 0.50;
    [ObservableProperty] private double _glassBorderThickness = 1.2;
    [ObservableProperty] private string _glassBorderColorHex = "#FFFFFF";
    [ObservableProperty] private double _glassCornerRadius = 18.0;
    [ObservableProperty] private double _glassInnerShadowIntensity = 0.25;
    [ObservableProperty] private double _glassOuterShadowIntensity = 0.45;
    [ObservableProperty] private double _glassGlowIntensity = 0.12;
    [ObservableProperty] private double _glassHighlightIntensity = 0.45;
    [ObservableProperty] private GlassAppearanceVariant _glassAppearanceVariant = GlassAppearanceVariant.Frosted;
    [ObservableProperty] private string _glassTextPrimaryHex = "#FFFFFF";
    [ObservableProperty] private string _glassTextSecondaryHex = "#E2E8F0";
    [ObservableProperty] private string _glassAccentHex = "#60A5FA";

    public GlassmorphismStyle LiveGlassStyle { get; private set; } = GlassmorphismStyle.BuiltInPresets[0].Clone();

    // ==================== MARKETPLACE & .DWWP PACKAGES ====================
    private readonly List<MarketplaceCatalogEntry> _allMarketplaceEntries = new();
    public ObservableCollection<MarketplaceCatalogEntry> FilteredMarketplaceEntries { get; } = new();
    public IReadOnlyList<string> MarketplaceCategories { get; } = new[]
    {
        "All", "Rainmeter Inspired", "Time & Date", "Weather", "Productivity", "Monitoring", "Ambient", "Imported Packages"
    };
    public IReadOnlyList<string> MarketplaceSortModes { get; } = new[]
    {
        "Featured", "Popular", "Highest Rated", "Recently Added"
    };

    [ObservableProperty] private string _marketplaceSearchText = string.Empty;
    [ObservableProperty] private string _selectedMarketplaceCategory = "All";
    [ObservableProperty] private string _selectedMarketplaceSort = "Featured";
    [ObservableProperty] private MarketplaceCatalogEntry? _selectedMarketplaceEntry;
    [ObservableProperty] private string _packageWarningsBanner = string.Empty;

    public async Task InitializeAsync(AppSettings settings, DesktopProfile? activeProfile)
    {
        _settings = settings;
        EnableDesktopWidgets = settings.EnableDesktopWidgets;
        EnableSnapToGrid = settings.EnableWidgetSnapToGrid;
        GridSizePx = Math.Clamp(settings.WidgetGridSizePx, 4, 64);
        AutoAccentFromWallpaper = settings.AutoWidgetAccentFromWallpaper;
        WidgetAccentColorHex = string.IsNullOrWhiteSpace(settings.WidgetAccentColorHex) ? "#FF38BDF8" : settings.WidgetAccentColorHex;
        LoadGlassPresets();

        var initialStyle = AllSelectableStylePresets.FirstOrDefault(p =>
            string.Equals(p.Id, settings.ActiveGlassPresetId, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(p.Name, settings.ActiveGlassPresetId, StringComparison.OrdinalIgnoreCase))
            ?? GlassmorphismStyle.BuiltInPresets[0];

        SelectedGlassPreset = initialStyle;
        ApplyStyleToEditorSliders(initialStyle.Clone());
        if (!string.IsNullOrWhiteSpace(WidgetAccentColorHex))
        {
            GlassAccentHex = WidgetAccentColorHex;
            LiveGlassStyle.AccentHex = WidgetAccentColorHex;
        }
        BindActiveProfile(activeProfile);
        RefreshWallpaperPreviewBackdrop(settings.LastAppliedImagePath);
        await ReloadMarketplaceAsync();
    }

    public void BindActiveProfile(DesktopProfile? profile)
    {
        _activeProfile = profile;
        ActiveProfileBadge = profile?.Name ?? "No Profile";

        Widgets.Clear();
        if (profile != null)
        {
            foreach (var w in profile.Widgets)
                Widgets.Add(w);

            PanoramicVideoPath = profile.PanoramicVideoPath ?? string.Empty;
            LoopPanoramicVideo = profile.LoopPanoramicVideo;
            PauseVideoOnBattery = profile.PauseVideoOnBattery;
            PauseVideoOnFullScreen = profile.PauseVideoOnFullScreen;

            var presetMatch = AllSelectableStylePresets.FirstOrDefault(p =>
                string.Equals(p.Name, profile.GlassPresetName, StringComparison.OrdinalIgnoreCase));
            if (presetMatch != null)
                SelectedGlassPreset = presetMatch;
        }

        SelectedWidget = Widgets.FirstOrDefault();
        RefreshMarketplaceDesktopPresence();
        PreviewSurfaceInvalidated?.Invoke();
    }

    public void ApplyGlobalGlassFromExternal(GlassmorphismStyle style, bool persistToProfile = true)
    {
        ApplyStyleToEditorSliders(style.Clone());
        _coordinator.UpdateGlobalGlassLive(LiveGlassStyle, _activeProfile?.WidgetBrightness ?? 1.0);
        if (persistToProfile && _activeProfile != null)
        {
            _activeProfile.GlassPresetName = style.Name;
            _db.SaveDesktopProfile(_activeProfile);
        }
        PreviewSurfaceInvalidated?.Invoke();
    }

    public void RefreshWallpaperPreviewBackdrop(string? wallpaperPath)
    {
        var primary = _coordinator.ResolveTargetMonitor("PRIMARY");
        SharpPreviewBackdrop = _coordinator.WallpaperSampler.GetSharpPreviewBackdrop(primary, wallpaperPath);
        PreviewSurfaceInvalidated?.Invoke();
    }

    public ImageSource? CreateStudioBlurredWidgetSlice(double dipX, double dipY, double width, double height, GlassmorphismStyle style)
    {
        if (style.SurfaceTheme != WidgetSurfaceTheme.Glassmorphism || !style.EnableBackdropBlur)
            return null;

        var mon = _coordinator.ResolveTargetMonitor(SelectedWidget?.TargetMonitorAnchor ?? "PRIMARY");
        return _coordinator.WallpaperSampler.CreateBlurredCropSlice(
            mon,
            _settings.LastAppliedImagePath,
            dipX,
            dipY,
            width,
            height,
            style.BackgroundBlurIntensity,
            addFrostGrain: style.BackgroundBlurIntensity > 1.5);
    }

    public void NotifyDesktopWidgetBoundsChanged(WidgetInstance updated)
    {
        if (SelectedWidget != null && string.Equals(SelectedWidget.InstanceId, updated.InstanceId, StringComparison.OrdinalIgnoreCase))
        {
            _suppressWidgetPush = true;
            SelectedWidgetAnchor = updated.TargetMonitorAnchor;
            SelectedWidgetX = updated.X;
            SelectedWidgetY = updated.Y;
            SelectedWidgetWidth = updated.Width;
            SelectedWidgetHeight = updated.Height;
            _suppressWidgetPush = false;
        }

        if (_activeProfile != null)
            _db.SaveDesktopProfile(_activeProfile);

        PreviewSurfaceInvalidated?.Invoke();
    }

    public void PreviewWidgetPositionsLive(IReadOnlyList<WidgetInstance> widgets)
    {
        if (widgets == null || widgets.Count == 0) return;
        if (SelectedWidget != null)
        {
            var match = widgets.FirstOrDefault(w => string.Equals(w.InstanceId, SelectedWidget.InstanceId, StringComparison.OrdinalIgnoreCase));
            if (match != null)
            {
                _suppressWidgetPush = true;
                SelectedWidgetAnchor = match.TargetMonitorAnchor;
                SelectedWidgetX = match.X;
                SelectedWidgetY = match.Y;
                _suppressWidgetPush = false;
            }
        }
        _coordinator.UpdateWidgetPositionsLive(widgets);
    }

    public void CommitWidgetsMoveFromPlacementStudio(IReadOnlyList<WidgetInstance> widgets)
    {
        if (widgets == null || widgets.Count == 0) return;

        if (SelectedWidget != null)
        {
            var match = widgets.FirstOrDefault(w => string.Equals(w.InstanceId, SelectedWidget.InstanceId, StringComparison.OrdinalIgnoreCase));
            if (match != null)
            {
                _suppressWidgetPush = true;
                SelectedWidgetAnchor = match.TargetMonitorAnchor;
                SelectedWidgetX = match.X;
                SelectedWidgetY = match.Y;
                _suppressWidgetPush = false;
            }
        }

        if (_activeProfile != null)
            _db.SaveDesktopProfile(_activeProfile);

        _coordinator.RebuildOverlays();
        PreviewSurfaceInvalidated?.Invoke();
    }

    public void CommitWidgetMoveFromPlacementStudio(WidgetInstance widget, string anchor, double dipX, double dipY)
    {
        widget.TargetMonitorAnchor = anchor;
        widget.X = dipX;
        widget.Y = dipY;
        CommitWidgetsMoveFromPlacementStudio(new[] { widget });
    }

    // ==================== WIDGET LIST, INLINE EDIT / DELETE & CONFIG MODAL COMMANDS ====================

    [RelayCommand]
    private void ToggleLayoutLock()
    {
        IsDesktopEditMode = !IsDesktopEditMode;
    }

    [RelayCommand]
    private void SelectAccentColor(string? hex)
    {
        if (string.IsNullOrWhiteSpace(hex)) return;
        AutoAccentFromWallpaper = false;
        _settings.AutoWidgetAccentFromWallpaper = false;
        ApplyUniversalAccentHex(hex.Trim());
        _setStatus($"Applied universal widget accent color {hex.Trim()}.", false);
    }

    [RelayCommand]
    private void PickAccentFromWallpaper()
    {
        string? imgPath = !string.IsNullOrWhiteSpace(_settings.ActiveLiveVideoStillPath) && File.Exists(_settings.ActiveLiveVideoStillPath)
            ? _settings.ActiveLiveVideoStillPath
            : _settings.LastAppliedImagePath;
        if (string.IsNullOrWhiteSpace(imgPath) || !File.Exists(imgPath))
        {
            _setStatus("Apply a wallpaper first to automatically match its dominant accent color.", true);
            return;
        }

        var palette = _colorAnalyzer.AnalyzeFile(imgPath);
        string chosen = palette.SuggestedStyle.AccentHex;
        AutoAccentFromWallpaper = true;
        _settings.AutoWidgetAccentFromWallpaper = true;
        ApplyUniversalAccentHex(chosen);
        _setStatus($"Matched universal accent color {chosen} from your active wallpaper.", false);
    }

    public void SyncWallpaperAccentIfAuto(string? wallpaperPath)
    {
        if (!AutoAccentFromWallpaper || string.IsNullOrWhiteSpace(wallpaperPath) || !File.Exists(wallpaperPath))
            return;

        try
        {
            var palette = _colorAnalyzer.AnalyzeFile(wallpaperPath);
            ApplyUniversalAccentHex(palette.SuggestedStyle.AccentHex);
        }
        catch { }
    }

    private void ApplyUniversalAccentHex(string hex)
    {
        WidgetAccentColorHex = hex;
        _settings.WidgetAccentColorHex = hex;
        _db.SaveSettings(_settings);

        GlassAccentHex = hex;
        LiveGlassStyle.AccentHex = hex;
        if (_activeProfile != null)
        {
            foreach (var w in _activeProfile.Widgets.Where(x => x.UseGlobalGlassTheme))
                w.GlassStyle.AccentHex = hex;
            _db.SaveDesktopProfile(_activeProfile);
        }

        _coordinator.UpdateGlobalGlassLive(LiveGlassStyle, _activeProfile?.WidgetBrightness ?? 1.0);
        AccentColorChanged?.Invoke(hex);
        PreviewSurfaceInvalidated?.Invoke();
    }

    partial void OnWidgetAccentColorHexChanged(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Trim().Length < 7) return;
        _settings.WidgetAccentColorHex = value.Trim();
        GlassAccentHex = value.Trim();
        LiveGlassStyle.AccentHex = value.Trim();
        _db.SaveSettings(_settings);
        _coordinator.UpdateGlobalGlassLive(LiveGlassStyle, _activeProfile?.WidgetBrightness ?? 1.0);
        AccentColorChanged?.Invoke(value.Trim());
        PreviewSurfaceInvalidated?.Invoke();
    }

    partial void OnAutoAccentFromWallpaperChanged(bool value)
    {
        _settings.AutoWidgetAccentFromWallpaper = value;
        _db.SaveSettings(_settings);
        if (value) SyncWallpaperAccentIfAuto(_settings.LastAppliedImagePath);
    }

    [RelayCommand]
    private void ApplyFocusPreset(string? minutesStr)
    {
        if (int.TryParse(minutesStr, out int mins) && mins >= 1 && mins <= 180)
        {
            WidgetFocusWorkMinutes = mins;
            _setStatus($"Focus timer set to {mins} minutes.", false);
        }
    }

    [RelayCommand]
    private void AddShortcutApp()
    {
        var picker = new InstalledAppPickerWindow
        {
            Owner = Application.Current?.MainWindow
        };
        if (picker.ShowDialog() != true || picker.SelectedApp == null) return;

        string name = string.IsNullOrWhiteSpace(picker.SelectedApp.DisplayName)
            ? "App"
            : picker.SelectedApp.DisplayName.Trim();
        string initials = name.Length >= 2 ? name[..2].ToUpperInvariant() : "AP";
        var item = new ShortcutDockItem
        {
            Label = name,
            TargetPathOrUrl = picker.SelectedApp.LaunchTarget,
            IconMode = "Auto",
            Initials = initials,
            GlyphKey = "Settings",
            CustomColorHex = WidgetAccentColorHex
        };
        ShortcutItems.Add(item);
        SelectedShortcutItem = item;
        SaveShortcutItemsToSelectedWidget();
        _setStatus($"Added installed app '{name}' to Shortcut Dock.", false);
    }

    [RelayCommand]
    private void AddShortcutWebsite()
    {
        string target = string.IsNullOrWhiteSpace(EditShortcutTarget) ? "https://www.youtube.com" : EditShortcutTarget.Trim();
        if (!target.Contains("://", StringComparison.Ordinal) && target.Contains('.'))
            target = "https://" + target;
        string label = string.IsNullOrWhiteSpace(EditShortcutLabel) ? "Website" : EditShortcutLabel.Trim();
        string initials = string.IsNullOrWhiteSpace(EditShortcutInitials)
            ? (label.Length >= 2 ? label[..2].ToUpperInvariant() : "WB")
            : EditShortcutInitials.Trim().ToUpperInvariant();

        var item = new ShortcutDockItem
        {
            Label = label,
            TargetPathOrUrl = target,
            IconMode = string.IsNullOrWhiteSpace(EditShortcutIconMode) ? "GlassGlyph" : EditShortcutIconMode,
            Initials = initials,
            GlyphKey = string.IsNullOrWhiteSpace(EditShortcutGlyphKey) ? "Globe" : EditShortcutGlyphKey,
            CustomColorHex = string.IsNullOrWhiteSpace(EditShortcutColorHex) ? WidgetAccentColorHex : EditShortcutColorHex,
            CustomIconPath = string.Equals(EditShortcutIconMode, "CustomIcon", StringComparison.OrdinalIgnoreCase)
                ? EditShortcutCustomIconPath.Trim()
                : string.Empty
        };
        ShortcutItems.Add(item);
        SelectedShortcutItem = item;
        SaveShortcutItemsToSelectedWidget();
    }

    [RelayCommand]
    private void AddShortcutFolder()
    {
        var dlg = new OpenFolderDialog
        {
            Title = "Choose Folder to Add as Desktop Shortcut Icon"
        };
        if (dlg.ShowDialog() != true || string.IsNullOrWhiteSpace(dlg.FolderName)) return;

        string folderName = Path.GetFileName(dlg.FolderName.TrimEnd('\\', '/'));
        if (string.IsNullOrWhiteSpace(folderName)) folderName = dlg.FolderName;
        string initials = folderName.Length >= 2 ? folderName[..2].ToUpperInvariant() : "FD";

        var item = new ShortcutDockItem
        {
            Label = folderName,
            TargetPathOrUrl = dlg.FolderName,
            IconMode = "Auto",
            Initials = initials,
            GlyphKey = "Folder",
            CustomColorHex = WidgetAccentColorHex
        };
        ShortcutItems.Add(item);
        SelectedShortcutItem = item;
        SaveShortcutItemsToSelectedWidget();
    }

    [RelayCommand]
    private void UploadShortcutCustomIcon()
    {
        if (SelectedShortcutItem == null && ShortcutItems.Count > 0)
            SelectedShortcutItem = ShortcutItems[0];

        var dlg = new OpenFileDialog
        {
            Title = "Upload Custom Shortcut Icon",
            Filter = "Icon & Image Files (*.png;*.jpg;*.jpeg;*.ico;*.webp;*.bmp)|*.png;*.jpg;*.jpeg;*.ico;*.webp;*.bmp"
        };
        if (dlg.ShowDialog() != true) return;

        try
        {
            string iconsDir = Path.Combine(AppSettings.GetAppDataRoot(), "ShortcutIcons");
            Directory.CreateDirectory(iconsDir);
            string ext = Path.GetExtension(dlg.FileName);
            string dest = Path.Combine(iconsDir, $"icon_{Guid.NewGuid():N}{ext}");
            File.Copy(dlg.FileName, dest, overwrite: true);

            if (IconExtractorHelper.LoadCustomIconImage(dest) == null)
            {
                try { File.Delete(dest); } catch { }
                _setStatus("The selected file could not be decoded as a valid icon or image.", true);
                return;
            }

            _suppressShortcutSync = true;
            EditShortcutCustomIconPath = dest;
            EditShortcutIconMode = "CustomIcon";
            _suppressShortcutSync = false;

            if (SelectedShortcutItem != null)
            {
                UpdateSelectedShortcutItem();
                _setStatus($"Uploaded custom icon for '{SelectedShortcutItem.Label}'.", false);
            }
            else
            {
                _setStatus("Custom icon uploaded — click + Add Website, + Add App, or + Add Folder to create a shortcut tile.", false);
            }
        }
        catch (Exception ex)
        {
            _suppressShortcutSync = false;
            _setStatus($"Could not load custom icon: {ex.Message}", true);
        }
    }

    [RelayCommand]
    private void DeleteShortcutItem(ShortcutDockItem? item)
    {
        item ??= SelectedShortcutItem;
        if (item == null) return;
        ShortcutItems.Remove(item);
        SelectedShortcutItem = ShortcutItems.FirstOrDefault();
        SaveShortcutItemsToSelectedWidget();
    }

    [RelayCommand]
    private void UpdateSelectedShortcutItem()
    {
        if (SelectedShortcutItem == null) return;
        SelectedShortcutItem.Label = string.IsNullOrWhiteSpace(EditShortcutLabel) ? "Shortcut" : EditShortcutLabel.Trim();
        SelectedShortcutItem.TargetPathOrUrl = EditShortcutTarget.Trim();
        SelectedShortcutItem.IconMode = EditShortcutIconMode;
        SelectedShortcutItem.Initials = string.IsNullOrWhiteSpace(EditShortcutInitials) ? "SC" : EditShortcutInitials.Trim().ToUpperInvariant();
        SelectedShortcutItem.GlyphKey = EditShortcutGlyphKey;
        SelectedShortcutItem.CustomColorHex = string.IsNullOrWhiteSpace(EditShortcutColorHex) ? WidgetAccentColorHex : EditShortcutColorHex.Trim();
        SelectedShortcutItem.CustomIconPath = EditShortcutCustomIconPath.Trim();

        SaveShortcutItemsToSelectedWidget();
    }

    private void SaveShortcutItemsToSelectedWidget()
    {
        if (SelectedWidget == null || SelectedWidget.Kind != WidgetKind.ShortcutBar) return;
        ShortcutDockConfigHelper.SaveItems(SelectedWidget.Config, ShortcutItems);
        PersistAndPushProfile();
    }

    public void RestoreLayoutPresetBackup(DuoWallLayoutPresetBackup backup)
    {
        if (_activeProfile == null) return;
        _activeProfile.Widgets = backup.Widgets.Select(w => w.Clone()).ToList();
        Widgets.Clear();
        foreach (var w in _activeProfile.Widgets)
            Widgets.Add(w);

        if (!string.IsNullOrWhiteSpace(backup.WidgetAccentColorHex))
        {
            AutoAccentFromWallpaper = backup.AutoWidgetAccentFromWallpaper;
            WidgetAccentColorHex = backup.WidgetAccentColorHex;
        }

        SelectedWidget = Widgets.FirstOrDefault();
        PersistAndPushProfile();
    }

    [RelayCommand]
    private void ApplyMinimalTransparentClock()
    {
        if (SelectedWidget == null) return;
        var mondStyle = GlassmorphismStyle.GetPreset("Mond Transparent Serif");
        SelectedWidget.UseGlobalGlassTheme = false;
        SelectedWidgetUseGlobalGlass = false;
        SelectedWidget.GlassStyle = mondStyle;
        SelectedWidgetPresetOverride = AllSelectableStylePresets.FirstOrDefault(p => p.Id == mondStyle.Id) ?? mondStyle;
        ConfigClockLayout = "Typographic Clock (Spaced Day -> Time -> Date)";
        ConfigFontFamily = "Bahnschrift";
        ConfigAlignment = "Center";
        ConfigClockTimeBaseSize = 64.0;
        if (SelectedWidgetWidth < 460) { SelectedWidgetWidth = 520; SelectedWidgetHeight = 220; }
        SyncSelectedWidgetFields();
        _setStatus("Switched selected clock to borderless Typographic Clock mode (no glass box).", false);
    }

    [RelayCommand]
    private void ConfigureWidget(WidgetInstance? widget)
    {
        if (widget == null) return;
        SelectedWidget = widget;
        IsWidgetConfigOpen = true;
    }

    [RelayCommand]
    private void CloseWidgetConfig()
    {
        SyncSelectedWidgetFields();
        PersistAndPushProfile();
        IsWidgetConfigOpen = false;
    }

    [RelayCommand]
    private void AddWidget()
    {
        if (_activeProfile == null)
        {
            _setStatus("No active desktop profile selected.", true);
            return;
        }

        var w = new WidgetInstance
        {
            InstanceId = Guid.NewGuid().ToString("N")[..8],
            PackageId = $"builtin.{NewWidgetKind.ToString().ToLowerInvariant()}",
            Kind = NewWidgetKind,
            Title = BuildDefaultWidgetTitle(NewWidgetKind),
            TargetMonitorAnchor = "PRIMARY",
            X = 48 + (_activeProfile.Widgets.Count % 4) * 26,
            Y = 48 + (_activeProfile.Widgets.Count % 4) * 26,
            Width = NewWidgetKind == WidgetKind.AnalogClock ? 220 : (NewWidgetKind == WidgetKind.SystemStats ? 360 : 340),
            Height = NewWidgetKind == WidgetKind.CalendarAgenda ? 330 : NewWidgetKind == WidgetKind.ShortcutBar ? 96 : (NewWidgetKind is WidgetKind.TodoList or WidgetKind.SystemStats ? 245 : 170),
            UseGlobalGlassTheme = true,
            GlassStyle = LiveGlassStyle.Clone()
        };

        _activeProfile.Widgets.Add(w);
        Widgets.Add(w);
        SelectedWidget = w;
        IsWidgetConfigOpen = true;
        PersistAndPushProfile();
        _setStatus($"Added '{w.Title}' widget — configure its options in the open panel.", false);
    }

    [RelayCommand]
    private void DeleteWidget(WidgetInstance? widget)
    {
        widget ??= SelectedWidget;
        if (_activeProfile == null || widget == null) return;

        _activeProfile.Widgets.RemoveAll(w => w.InstanceId == widget.InstanceId);
        Widgets.Remove(widget);
        if (SelectedWidget?.InstanceId == widget.InstanceId)
        {
            SelectedWidget = Widgets.FirstOrDefault();
            IsWidgetConfigOpen = false;
        }

        PersistAndPushProfile();
        _setStatus($"Deleted '{widget.Title}' widget from layout.", false);
    }

    [RelayCommand]
    private void DuplicateWidget(WidgetInstance? widget)
    {
        widget ??= SelectedWidget;
        if (_activeProfile == null || widget == null) return;

        var copy = widget.Clone(newInstanceId: true);
        copy.Title += " (Copy)";
        copy.X += 28;
        copy.Y += 28;
        _activeProfile.Widgets.Add(copy);
        Widgets.Add(copy);
        SelectedWidget = copy;
        PersistAndPushProfile();
    }

    [RelayCommand]
    private async Task SaveAndTestWeatherAsync()
    {
        SyncSelectedWidgetFields();
        _settings.WeatherCity = ConfigWeatherCity.Trim();
        _settings.WeatherUseFahrenheit = ConfigWeatherUnit.Contains("imperial", StringComparison.OrdinalIgnoreCase);
        if (!string.IsNullOrWhiteSpace(ConfigWeatherApiKey))
            _settings.CustomWeatherApiKey = ConfigWeatherApiKey.Trim();
        _db.SaveSettings(_settings);

        PersistAndPushProfile();
        await _coordinator.RefreshWeatherAsync();
        PreviewSurfaceInvalidated?.Invoke();

        if (_coordinator.LatestWeather is { } wx)
        {
            _setStatus($"Weather synced for {wx.CityDisplay} ({wx.Temperature:0.#}{wx.UnitSymbol} via {wx.ProviderName}).", false);
        }
        else
        {
            _setStatus(_coordinator.WeatherStatus, true);
        }
    }

    [RelayCommand]
    private void BrowsePanoramicVideo()
    {
        if (_activeProfile == null) return;
        var dlg = new OpenFileDialog
        {
            Title = "Select Panoramic Wallpaper Video",
            Filter = "Video Files (*.mp4;*.webm;*.mov;*.wmv)|*.mp4;*.webm;*.mov;*.wmv"
        };
        if (dlg.ShowDialog() != true) return;

        PanoramicVideoPath = dlg.FileName;
        _activeProfile.PanoramicVideoPath = dlg.FileName;
        PersistAndPushProfile();
        _setStatus($"Assigned panoramic video: {Path.GetFileName(dlg.FileName)}", false);
    }

    [RelayCommand]
    private void ClearPanoramicVideo()
    {
        if (_activeProfile == null) return;
        PanoramicVideoPath = string.Empty;
        _activeProfile.PanoramicVideoPath = null;
        PersistAndPushProfile();
        _setStatus("Cleared panoramic video backdrop.", false);
    }

    // ==================== GLASSMORPHISM & RAINMETER SURFACE ENGINE COMMANDS ====================

    [RelayCommand]
    private void SelectGlassPreset(GlassmorphismStyle? preset)
    {
        if (preset == null) return;
        SelectedGlassPreset = preset;
    }

    [RelayCommand]
    private void ApplyGlassTintSwatch(string? hex)
    {
        if (!string.IsNullOrWhiteSpace(hex))
            GlassBackgroundTintHex = hex;
    }

    [RelayCommand]
    private void SaveCustomGlassPreset()
    {
        string cleanName = string.IsNullOrWhiteSpace(CustomGlassPresetName)
            ? $"Custom Glass {DateTime.Now:HHmm}"
            : CustomGlassPresetName.Trim();

        var custom = LiveGlassStyle.Clone(
            newId: $"custom-{Guid.NewGuid().ToString("N")[..8]}",
            newName: cleanName,
            isBuiltIn: false);

        _db.SaveCustomGlassPreset(custom);
        LoadGlassPresets();
        SelectedGlassPreset = GlassPresets.FirstOrDefault(p => p.Id == custom.Id) ?? custom;
        _setStatus($"Saved custom preset '{custom.Name}'.", false);
    }

    [RelayCommand]
    private void DeleteCustomGlassPreset()
    {
        if (SelectedGlassPreset == null || SelectedGlassPreset.IsBuiltIn)
        {
            _setStatus("Built-in presets cannot be deleted.", true);
            return;
        }

        string name = SelectedGlassPreset.Name;
        _db.DeleteCustomGlassPreset(SelectedGlassPreset.Id);
        LoadGlassPresets();
        SelectedGlassPreset = GlassPresets.FirstOrDefault();
        _setStatus($"Deleted custom preset '{name}'.", false);
    }

    [RelayCommand]
    private void ApplyGlassToSelectedWidget()
    {
        if (SelectedWidget == null || _activeProfile == null) return;
        SelectedWidget.UseGlobalGlassTheme = false;
        SelectedWidgetUseGlobalGlass = false;
        SelectedWidget.GlassStyle = LiveGlassStyle.Clone();
        PersistAndPushProfile();
        _setStatus($"Applied '{LiveGlassStyle.Name}' surface style to '{SelectedWidget.Title}'.", false);
    }

    [RelayCommand]
    private void ApplyGlassToAllGlobalWidgets()
    {
        if (_activeProfile == null) return;
        _settings.ActiveGlassPresetId = LiveGlassStyle.Id;
        _activeProfile.GlassPresetName = LiveGlassStyle.Name;
        foreach (var w in _activeProfile.Widgets.Where(x => x.UseGlobalGlassTheme))
        {
            w.GlassStyle = LiveGlassStyle.Clone();
        }
        PersistAndPushProfile();
        _saveSettingsAndRefreshProfiles();
        _setStatus($"Applied '{LiveGlassStyle.Name}' surface theme globally across shared widgets.", false);
    }

    // ==================== MARKETPLACE & .DWWP PACKAGES ====================

    [RelayCommand]
    private async Task ReloadMarketplaceAsync()
    {
        var builtInCatalog = await _marketplaceProvider.GetCatalogAsync();
        var installed = _db.LoadInstalledPackages();
        var installedIds = new HashSet<string>(installed.Select(i => i.PackageId), StringComparer.OrdinalIgnoreCase);

        _allMarketplaceEntries.Clear();
        foreach (var entry in builtInCatalog)
        {
            entry.IsInstalled = entry.Manifest.Id.StartsWith("builtin.", StringComparison.OrdinalIgnoreCase) ||
                                entry.Manifest.Id.StartsWith("rainmeter.", StringComparison.OrdinalIgnoreCase) ||
                                installedIds.Contains(entry.Manifest.Id);
            _allMarketplaceEntries.Add(entry);
        }

        foreach (var rec in installed)
        {
            if (_allMarketplaceEntries.Any(e => string.Equals(e.Manifest.Id, rec.PackageId, StringComparison.OrdinalIgnoreCase)))
                continue;

            try
            {
                var manifest = JsonSerializer.Deserialize<DualWallWidgetPackageManifest>(
                    rec.ManifestJson,
                    new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
                if (manifest != null)
                {
                    _allMarketplaceEntries.Add(new MarketplaceCatalogEntry
                    {
                        Manifest = manifest,
                        IsFeatured = false,
                        IsInstalled = true,
                        SourceBadge = "Imported (.dwwp)"
                    });
                }
            }
            catch { }
        }

        ApplyMarketplaceFilter();
        RefreshMarketplaceDesktopPresence();
    }

    private WidgetInstance? FindMatchingDesktopWidget(MarketplaceCatalogEntry entry)
    {
        if (_activeProfile == null) return null;
        var exact = _activeProfile.Widgets.FirstOrDefault(w =>
            string.Equals(w.PackageId, entry.Manifest.Id, StringComparison.OrdinalIgnoreCase));
        if (exact != null) return exact;

        if (entry.Manifest.PackageType == PackageKind.Widget && !entry.Manifest.Id.StartsWith("rainmeter.", StringComparison.OrdinalIgnoreCase))
        {
            return _activeProfile.Widgets.FirstOrDefault(w => w.Kind == entry.Manifest.WidgetKind);
        }
        return null;
    }

    private void RefreshMarketplaceDesktopPresence()
    {
        foreach (var entry in _allMarketplaceEntries)
        {
            entry.IsOnDesktop = entry.Manifest.PackageType == PackageKind.Widget && FindMatchingDesktopWidget(entry) != null;
        }
    }

    [RelayCommand]
    private void EditMarketplaceWidget(MarketplaceCatalogEntry? entry)
    {
        if (entry == null) return;
        var match = FindMatchingDesktopWidget(entry);
        if (match == null)
        {
            _setStatus($"'{entry.Manifest.Name}' is not on the desktop yet — click Add to Desktop first.", true);
            return;
        }

        SelectedWidget = match;
        StudioSectionIndex = 0; // Widgets on Screen
        IsWidgetConfigOpen = true;
    }

    [RelayCommand]
    private void AddMarketplaceEntryToDesktop(MarketplaceCatalogEntry? entry)
    {
        entry ??= SelectedMarketplaceEntry;
        if (entry == null || _activeProfile == null) return;

        var m = entry.Manifest;
        if (m.PackageType == PackageKind.GlassPreset && m.GlassStyle != null)
        {
            ApplyGlobalGlassFromExternal(m.GlassStyle);
            _setStatus($"Applied skin '{m.Name}'.", false);
            return;
        }

        if (m.PackageType == PackageKind.ColorPalette && m.ColorPalette != null)
        {
            _db.SaveColorPalette(m.ColorPalette);
            ApplyGlobalGlassFromExternal(m.ColorPalette.SuggestedStyle);
            _setStatus($"Applied color palette '{m.Name}'.", false);
            return;
        }

        if (m.PackageType == PackageKind.DesktopProfile && m.DesktopProfile != null)
        {
            var importedProf = m.DesktopProfile.Clone();
            _db.SaveDesktopProfile(importedProf);
            _saveSettingsAndRefreshProfiles();
            _setStatus($"Imported desktop profile '{importedProf.Name}'.", false);
            return;
        }

        WidgetInstance widget = m.WidgetTemplate != null
            ? m.WidgetTemplate.Clone(newInstanceId: true)
            : new WidgetInstance
            {
                InstanceId = Guid.NewGuid().ToString("N")[..8],
                PackageId = m.Id,
                Kind = m.WidgetKind,
                Title = m.Name,
                TargetMonitorAnchor = "PRIMARY",
                X = 48 + (_activeProfile.Widgets.Count % 4) * 26,
                Y = 48 + (_activeProfile.Widgets.Count % 4) * 26,
                Width = 340,
                Height = m.WidgetKind == WidgetKind.CalendarAgenda ? 330 : m.WidgetKind == WidgetKind.ShortcutBar ? 96 : (m.WidgetKind == WidgetKind.SystemStats ? 245 : 180),
                UseGlobalGlassTheme = false,
                GlassStyle = (m.GlassStyle ?? LiveGlassStyle).Clone(),
                Config = new Dictionary<string, string>(m.DefaultSettings, StringComparer.OrdinalIgnoreCase)
            };

        _activeProfile.Widgets.Add(widget);
        Widgets.Add(widget);
        SelectedWidget = widget;
        PersistAndPushProfile();
        _setStatus($"Added '{m.Name}' ({widget.GlassStyle.SurfaceTheme}) to desktop layout.", false);
    }

    [RelayCommand]
    private async Task UninstallMarketplacePackageAsync(MarketplaceCatalogEntry? entry)
    {
        entry ??= SelectedMarketplaceEntry;
        if (entry == null) return;

        if (entry.Manifest.Id.StartsWith("builtin.", StringComparison.OrdinalIgnoreCase) ||
            entry.Manifest.Id.StartsWith("rainmeter.", StringComparison.OrdinalIgnoreCase))
        {
            _setStatus("Built-in catalog templates stay available; delete active instances from your desktop widget list.", false);
            return;
        }

        var installedRec = _db.LoadInstalledPackages()
            .FirstOrDefault(r => string.Equals(r.PackageId, entry.Manifest.Id, StringComparison.OrdinalIgnoreCase));
        if (installedRec != null)
        {
            WidgetPackageService.RemovePackageAssets(installedRec);
            _db.RemoveInstalledPackage(installedRec.PackageId);
        }

        if (_activeProfile != null)
        {
            _activeProfile.Widgets.RemoveAll(w => string.Equals(w.PackageId, entry.Manifest.Id, StringComparison.OrdinalIgnoreCase));
            Widgets.Clear();
            foreach (var w in _activeProfile.Widgets) Widgets.Add(w);
            PersistAndPushProfile();
        }

        await ReloadMarketplaceAsync();
        _setStatus($"Uninstalled package '{entry.Manifest.Name}' and cleaned all assets.", false);
    }

    [RelayCommand]
    private async Task ImportPackageAsync()
    {
        var dlg = new OpenFileDialog
        {
            Title = "Import DualWall Widget / Skin / Profile Package",
            Filter = "DualWall Package (*.dwwp)|*.dwwp|All Files (*.*)|*.*"
        };
        if (dlg.ShowDialog() != true) return;

        var knownIds = _allMarketplaceEntries.Select(e => e.Manifest.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var result = await _packageService.ImportPackageAsync(dlg.FileName, _settings.PackagesDirectory, knownIds);
        if (!result.Success || result.Manifest == null || result.InstalledRecord == null)
        {
            PackageWarningsBanner = result.ErrorMessage ?? "Package validation failed.";
            _setStatus($"Import rejected: {PackageWarningsBanner}", true);
            return;
        }

        _db.SaveInstalledPackage(result.InstalledRecord);
        PackageWarningsBanner = result.Warnings.Count > 0 ? string.Join(" | ", result.Warnings) : string.Empty;

        var m = result.Manifest;
        if (m.PackageType == PackageKind.GlassPreset && m.GlassStyle != null)
        {
            var importedGlass = m.GlassStyle.Clone(newId: m.Id, newName: m.Name, isBuiltIn: false);
            _db.SaveCustomGlassPreset(importedGlass);
            LoadGlassPresets();
        }
        else if (m.PackageType == PackageKind.DesktopProfile && m.DesktopProfile != null)
            _db.SaveDesktopProfile(m.DesktopProfile);
        else if (m.PackageType == PackageKind.ColorPalette && m.ColorPalette != null)
            _db.SaveColorPalette(m.ColorPalette);

        await ReloadMarketplaceAsync();
        _saveSettingsAndRefreshProfiles();
        _setStatus($"Imported sandboxed package '{m.Name}' v{m.Version} by {m.Author}.", false);
    }

    [RelayCommand]
    private async Task ExportSelectedWidgetPackageAsync()
    {
        if (SelectedWidget == null) return;
        var dlg = new SaveFileDialog
        {
            Title = "Export Widget Package",
            Filter = "DualWall Package (*.dwwp)|*.dwwp",
            FileName = $"{SanitizeFileName(SelectedWidget.Title)}.widget.dwwp"
        };
        if (dlg.ShowDialog() != true) return;

        var manifest = new DualWallWidgetPackageManifest
        {
            Id = $"user.widget.{Guid.NewGuid().ToString("N")[..8]}",
            Name = SelectedWidget.Title,
            Author = Environment.UserName,
            Description = $"Exported {SelectedWidget.Kind} widget configuration and skin.",
            Category = "Imported Packages",
            PackageType = PackageKind.Widget,
            WidgetKind = SelectedWidget.Kind,
            GlassStyle = (SelectedWidget.UseGlobalGlassTheme ? LiveGlassStyle : SelectedWidget.GlassStyle).Clone(),
            WidgetTemplate = SelectedWidget.Clone(),
            DefaultSettings = new Dictionary<string, string>(SelectedWidget.Config)
        };

        await _packageService.ExportPackageAsync(manifest, dlg.FileName);
        _setStatus($"Exported widget package to '{Path.GetFileName(dlg.FileName)}'.", false);
    }

    [RelayCommand]
    private async Task ExportGlassSkinPackageAsync()
    {
        var dlg = new SaveFileDialog
        {
            Title = "Export Glassmorphism Skin Package",
            Filter = "DualWall Package (*.dwwp)|*.dwwp",
            FileName = $"{SanitizeFileName(LiveGlassStyle.Name)}.glass.dwwp"
        };
        if (dlg.ShowDialog() != true) return;

        var manifest = new DualWallWidgetPackageManifest
        {
            Id = $"user.glass.{Guid.NewGuid().ToString("N")[..8]}",
            Name = LiveGlassStyle.Name,
            Author = Environment.UserName,
            Description = $"Declarative {LiveGlassStyle.SurfaceTheme} skin preset ({LiveGlassStyle.AppearanceVariant}).",
            Category = "Imported Packages",
            PackageType = PackageKind.GlassPreset,
            GlassStyle = LiveGlassStyle.Clone()
        };

        await _packageService.ExportPackageAsync(manifest, dlg.FileName);
        _setStatus($"Exported skin package '{Path.GetFileName(dlg.FileName)}'.", false);
    }

    [RelayCommand]
    private async Task ExportDesktopProfilePackageAsync()
    {
        if (_activeProfile == null) return;
        var dlg = new SaveFileDialog
        {
            Title = "Export Complete Desktop Profile Package",
            Filter = "DualWall Package (*.dwwp)|*.dwwp",
            FileName = $"{SanitizeFileName(_activeProfile.Name)}.profile.dwwp"
        };
        if (dlg.ShowDialog() != true) return;

        var manifest = new DualWallWidgetPackageManifest
        {
            Id = $"user.profile.{Guid.NewGuid().ToString("N")[..8]}",
            Name = _activeProfile.Name,
            Author = Environment.UserName,
            Description = _activeProfile.Description,
            Category = "Imported Packages",
            PackageType = PackageKind.DesktopProfile,
            GlassStyle = LiveGlassStyle.Clone(),
            DesktopProfile = _activeProfile.Clone()
        };

        await _packageService.ExportPackageAsync(manifest, dlg.FileName);
        _setStatus($"Exported complete desktop profile '{_activeProfile.Name}'.", false);
    }

    // ==================== PROPERTY CHANGE SYNC ====================

    partial void OnEnableDesktopWidgetsChanged(bool value)
    {
        _settings.EnableDesktopWidgets = value;
        _db.SaveSettings(_settings);
        _coordinator.RebuildOverlays();
    }

    partial void OnIsDesktopEditModeChanged(bool value)
    {
        LayoutLockLabel = value ? "Layout UNLOCKED (Drag/Resize On)" : "Layout LOCKED (Protected)";
        _coordinator.SetEditMode(value);
        PreviewSurfaceInvalidated?.Invoke();
    }

    partial void OnEnableSnapToGridChanged(bool value)
    {
        _settings.EnableWidgetSnapToGrid = value;
        _db.SaveSettings(_settings);
        _coordinator.UpdateGridOptions(value, GridSizePx);
        PreviewSurfaceInvalidated?.Invoke();
    }

    partial void OnGridSizePxChanged(int value)
    {
        int clamped = Math.Clamp(value, 4, 64);
        _settings.WidgetGridSizePx = clamped;
        _db.SaveSettings(_settings);
        _coordinator.UpdateGridOptions(EnableSnapToGrid, clamped);
        PreviewSurfaceInvalidated?.Invoke();
    }

    partial void OnLoopPanoramicVideoChanged(bool value)
    {
        if (_activeProfile != null) { _activeProfile.LoopPanoramicVideo = value; PersistAndPushProfile(); }
    }

    partial void OnPauseVideoOnBatteryChanged(bool value)
    {
        if (_activeProfile != null) { _activeProfile.PauseVideoOnBattery = value; PersistAndPushProfile(); }
    }

    partial void OnPauseVideoOnFullScreenChanged(bool value)
    {
        if (_activeProfile != null) { _activeProfile.PauseVideoOnFullScreen = value; PersistAndPushProfile(); }
    }

    partial void OnSelectedWidgetChanged(WidgetInstance? value)
    {
        HasSelectedWidget = value != null;
        SelectedWidgetId = value?.InstanceId;
        if (value == null) return;

        _suppressWidgetPush = true;
        UpdateKindVisibilityFlags(value.Kind);

        SelectedWidgetTitle = value.Title;
        SelectedWidgetAnchor = value.TargetMonitorAnchor;
        SelectedWidgetX = value.X;
        SelectedWidgetY = value.Y;
        SelectedWidgetWidth = value.Width;
        SelectedWidgetHeight = value.Height;
        SelectedWidgetVisible = value.IsVisible;
        SelectedWidgetClickThrough = value.IsClickThrough;
        SelectedWidgetMinimalAnim = value.MinimalAnimations;
        SelectedWidgetUseGlobalGlass = value.UseGlobalGlassTheme;
        SelectedWidgetBrightness = value.BrightnessMultiplier;
        SelectedWidgetPresetOverride = AllSelectableStylePresets.FirstOrDefault(p =>
            string.Equals(p.Name, value.GlassStyle.Name, StringComparison.OrdinalIgnoreCase)) ?? SelectedGlassPreset;

        // Weather
        ConfigWeatherCity = GetCfg(value, "City", _settings.WeatherCity);
        ConfigWeatherProvider = GetCfg(value, "Provider", string.IsNullOrWhiteSpace(_settings.CustomWeatherApiKey) ? "OpenMeteo (Free Keyless)" : "OpenWeatherMap (API Key)");
        ConfigWeatherApiKey = GetCfg(value, "ApiKey", _settings.CustomWeatherApiKey ?? string.Empty);
        ConfigWeatherUnit = string.Equals(GetCfg(value, "Units", _settings.WeatherUseFahrenheit ? "imperial" : "metric"), "imperial", StringComparison.OrdinalIgnoreCase)
            ? "imperial (°F)" : "metric (°C)";
        ConfigWeatherIconTheme = GetCfg(value, "WeatherIconTheme", "Animated Minimal");
        ConfigWeatherShowFeelsLike = GetBoolCfg(value, "WeatherShowFeelsLike", true);
        ConfigWeatherShowHumidity = GetBoolCfg(value, "WeatherShowHumidity", true);
        ConfigWeatherShowWind = GetBoolCfg(value, "WeatherShowWind", true);
        ConfigWeatherShowProvider = GetBoolCfg(value, "WeatherShowProvider", true);

        // All Clock Options (#3h Typographic Clock)
        ConfigClockMode = value.Kind == WidgetKind.AnalogClock ? "Analog Clock" : "Digital Clock";
        string loadedLayout = GetCfg(value, "ClockLayout", "Stacked (Header -> Time -> Date)");
        ConfigClockLayout = loadedLayout.Contains("Mond", StringComparison.OrdinalIgnoreCase)
            ? "Typographic Clock (Spaced Day -> Time -> Date)"
            : loadedLayout;
        ConfigFormat24H = GetBoolCfg(value, "Format24H", false);
        ConfigShowAmPm = GetBoolCfg(value, "ShowAmPm", true);
        ConfigShowSeconds = GetBoolCfg(value, "ShowSeconds", false);
        ConfigShowDay = GetBoolCfg(value, "ShowDay", true);
        ConfigDayPlacement = GetCfg(value, "DayPlacement", "Top");
        ConfigDayCaseStyle = GetCfg(value, "DayCaseStyle", "Spaced Uppercase");
        ConfigShowDate = GetBoolCfg(value, "ShowDate", true);
        ConfigDatePlacement = GetCfg(value, "DatePlacement", "Bottom");
        ConfigDateFormat = GetCfg(value, "DateFormat", "dddd, MMM d yyyy");
        ConfigShowGreeting = GetBoolCfg(value, "ShowGreeting", true);
        ConfigCustomGreeting = GetCfg(value, "CustomGreeting", string.Empty);
        ConfigTimeZoneId = GetCfg(value, "TimeZoneId", "Local");
        ConfigFontFamily = GetCfg(value, "FontFamily", "Segoe UI Variable Display");
        ConfigTimeFontFamily = GetCfg(value, "TimeFontFamily", ConfigFontFamily);
        ConfigDayFontFamily = GetCfg(value, "DayFontFamily", ConfigFontFamily);
        ConfigDateFontFamily = GetCfg(value, "DateFontFamily", ConfigFontFamily);
        ConfigGreetingFontFamily = GetCfg(value, "GreetingFontFamily", ConfigFontFamily);
        ConfigTimeFontWeight = GetCfg(value, "TimeFontWeight", "Bold");
        ConfigClockTimeBaseSize = double.TryParse(GetCfg(value, "ClockTimeBaseSize", "40"), NumberStyles.Float, CultureInfo.InvariantCulture, out double bsz) ? bsz : 40.0;
        ConfigFontScale = double.TryParse(GetCfg(value, "FontScale", "1.0"), NumberStyles.Float, CultureInfo.InvariantCulture, out double fs) ? fs : 1.0;
        ConfigTimeScale = double.TryParse(GetCfg(value, "TimeScale", "1.0"), NumberStyles.Float, CultureInfo.InvariantCulture, out double ts) ? ts : 1.0;
        ConfigDayScale = double.TryParse(GetCfg(value, "DayScale", "1.0"), NumberStyles.Float, CultureInfo.InvariantCulture, out double ds) ? ds : 1.0;
        ConfigDateScale = double.TryParse(GetCfg(value, "DateScale", "1.0"), NumberStyles.Float, CultureInfo.InvariantCulture, out double dts) ? dts : 1.0;
        ConfigGreetingScale = double.TryParse(GetCfg(value, "GreetingScale", "1.0"), NumberStyles.Float, CultureInfo.InvariantCulture, out double gs) ? gs : 1.0;
        ConfigAlignment = GetCfg(value, "Alignment", "Left");
        ConfigVerticalAlignment = GetCfg(value, "VerticalAlignment", "Center");
        ConfigClockTextShadow = double.TryParse(GetCfg(value, "ClockTextShadow", "0.45"), NumberStyles.Float, CultureInfo.InvariantCulture, out double shw) ? shw : 0.45;
        ConfigClockTextPrimaryHex = GetCfg(value, "ClockTextPrimaryHex", string.Empty);
        ConfigClockAccentHex = GetCfg(value, "ClockAccentHex", string.Empty);
        ConfigTimeUseAccent = GetBoolCfg(value, "TimeUseAccent", false);
        ConfigTimeColorHex = GetCfg(value, "TimeColorHex", string.Empty);
        ConfigDayUseAccent = GetBoolCfg(value, "DayUseAccent", true);
        ConfigDayColorHex = GetCfg(value, "DayColorHex", string.Empty);
        ConfigDateUseAccent = GetBoolCfg(value, "DateUseAccent", false);
        ConfigDateColorHex = GetCfg(value, "DateColorHex", string.Empty);
        ConfigGreetingUseAccent = GetBoolCfg(value, "GreetingUseAccent", false);
        ConfigGreetingColorHex = GetCfg(value, "GreetingColorHex", string.Empty);
        ConfigAnalogDialStyle = GetCfg(value, "AnalogDialStyle", "Minimal Ticks");

        // Shortcut / Dock Bar
        ConfigShortcutLayout = GetCfg(value, "ShortcutLayout", "Horizontal");
        ConfigShortcutIconScale = double.TryParse(GetCfg(value, "ShortcutIconScale", "1.00"), NumberStyles.Float, CultureInfo.InvariantCulture, out double scScale)
            ? Math.Clamp(scScale, 0.45, 2.50)
            : 1.00;
        ShortcutItems.Clear();
        if (value.Kind == WidgetKind.ShortcutBar)
        {
            foreach (var s in ShortcutDockConfigHelper.ParseItems(value.Config))
                ShortcutItems.Add(s);
            SelectedShortcutItem = ShortcutItems.FirstOrDefault();
        }

        // System Telemetry Selectable Metrics
        ConfigStatsLayout = GetCfg(value, "StatsLayout", GetCfg(value, "Mode", "Detailed"));
        ConfigStatsShowCpu = GetBoolCfg(value, "ShowCpu", true);
        ConfigStatsShowRam = GetBoolCfg(value, "ShowRam", true);
        ConfigStatsShowNet = GetBoolCfg(value, "ShowNet", true);
        ConfigStatsShowDisk = GetBoolCfg(value, "ShowDisk", true);
        ConfigStatsShowAllDrives = GetBoolCfg(value, "ShowAllDrives", false);
        ConfigStatsShowGpu = GetBoolCfg(value, "ShowGpu", true);
        ConfigStatsShowTemps = GetBoolCfg(value, "ShowTemps", true);
        ConfigStatsShowUptime = GetBoolCfg(value, "ShowUptime", false);
        ConfigStatsShowPower = GetBoolCfg(value, "ShowPower", false);

        WidgetFocusWorkMinutes = int.TryParse(GetCfg(value, "WorkMinutes", "25"), out int wm) ? wm : 25;
        WidgetFocusBreakMinutes = int.TryParse(GetCfg(value, "BreakMinutes", "5"), out int bm) ? bm : 5;
        WidgetCustomSubtitle = GetCfg(value, "Subtitle", string.Empty);

        _suppressWidgetPush = false;
        PreviewSurfaceInvalidated?.Invoke();
    }

    private void UpdateKindVisibilityFlags(WidgetKind kind)
    {
        IsConfigWeather = kind == WidgetKind.Weather;
        IsConfigClock = kind is WidgetKind.DigitalClock or WidgetKind.AnalogClock;
        IsConfigAnalogClock = kind == WidgetKind.AnalogClock;
        IsConfigSystemStats = kind == WidgetKind.SystemStats;
        IsConfigFocusTimer = kind == WidgetKind.FocusTimer;
        IsConfigShortcutBar = kind == WidgetKind.ShortcutBar;
        IsConfigGenericOrCustom = kind is WidgetKind.CustomDeclarative or WidgetKind.CalendarAgenda or WidgetKind.TodoList or WidgetKind.PanoramicVideo;
    }

    partial void OnSelectedShortcutItemChanged(ShortcutDockItem? value)
    {
        if (value == null) return;
        _suppressShortcutSync = true;
        EditShortcutLabel = value.Label;
        EditShortcutTarget = value.TargetPathOrUrl;
        EditShortcutIconMode = value.IconMode;
        EditShortcutInitials = value.Initials;
        EditShortcutGlyphKey = value.GlyphKey;
        EditShortcutColorHex = value.CustomColorHex;
        EditShortcutCustomIconPath = value.CustomIconPath;
        _suppressShortcutSync = false;
    }

    partial void OnEditShortcutIconModeChanged(string value)
    {
        if (!_suppressShortcutSync && SelectedShortcutItem != null)
            UpdateSelectedShortcutItem();
    }

    partial void OnConfigShortcutLayoutChanged(string value) => SyncSelectedWidgetFields();
    partial void OnConfigShortcutIconScaleChanged(double value) => SyncSelectedWidgetFields();

    partial void OnSelectedWidgetPresetOverrideChanged(GlassmorphismStyle? value)
    {
        if (_suppressWidgetPush || SelectedWidget == null || value == null) return;
        SelectedWidget.UseGlobalGlassTheme = false;
        SelectedWidgetUseGlobalGlass = false;
        SelectedWidget.GlassStyle = value.Clone();
        PersistAndPushProfile();
    }

    partial void OnSelectedWidgetTitleChanged(string value) => SyncSelectedWidgetFields();
    partial void OnSelectedWidgetAnchorChanged(string value) => SyncSelectedWidgetFields();
    partial void OnSelectedWidgetXChanged(double value) => SyncSelectedWidgetFields();
    partial void OnSelectedWidgetYChanged(double value) => SyncSelectedWidgetFields();
    partial void OnSelectedWidgetWidthChanged(double value) => SyncSelectedWidgetFields();
    partial void OnSelectedWidgetHeightChanged(double value) => SyncSelectedWidgetFields();
    partial void OnSelectedWidgetVisibleChanged(bool value) => SyncSelectedWidgetFields();
    partial void OnSelectedWidgetClickThroughChanged(bool value) => SyncSelectedWidgetFields();
    partial void OnSelectedWidgetMinimalAnimChanged(bool value) => SyncSelectedWidgetFields();
    partial void OnSelectedWidgetUseGlobalGlassChanged(bool value) => SyncSelectedWidgetFields();
    partial void OnSelectedWidgetBrightnessChanged(double value) => SyncSelectedWidgetFields();

    partial void OnConfigWeatherCityChanged(string value) => SyncSelectedWidgetFields();
    partial void OnConfigWeatherProviderChanged(string value) => SyncSelectedWidgetFields();
    partial void OnConfigWeatherApiKeyChanged(string value) => SyncSelectedWidgetFields();
    partial void OnConfigWeatherUnitChanged(string value) => SyncSelectedWidgetFields();
    partial void OnConfigWeatherIconThemeChanged(string value) => SyncSelectedWidgetFields();
    partial void OnConfigWeatherShowFeelsLikeChanged(bool value) => SyncSelectedWidgetFields();
    partial void OnConfigWeatherShowHumidityChanged(bool value) => SyncSelectedWidgetFields();
    partial void OnConfigWeatherShowWindChanged(bool value) => SyncSelectedWidgetFields();
    partial void OnConfigWeatherShowProviderChanged(bool value) => SyncSelectedWidgetFields();

    partial void OnConfigClockModeChanged(string value)
    {
        if (_suppressWidgetPush || SelectedWidget == null) return;
        SelectedWidget.Kind = value.Contains("Analog", StringComparison.OrdinalIgnoreCase)
            ? WidgetKind.AnalogClock
            : WidgetKind.DigitalClock;
        UpdateKindVisibilityFlags(SelectedWidget.Kind);
        PersistAndPushProfile();
    }
    partial void OnConfigClockLayoutChanged(string value) => SyncSelectedWidgetFields();
    partial void OnConfigFormat24HChanged(bool value) => SyncSelectedWidgetFields();
    partial void OnConfigShowAmPmChanged(bool value) => SyncSelectedWidgetFields();
    partial void OnConfigShowSecondsChanged(bool value) => SyncSelectedWidgetFields();
    partial void OnConfigShowDayChanged(bool value) => SyncSelectedWidgetFields();
    partial void OnConfigDayPlacementChanged(string value) => SyncSelectedWidgetFields();
    partial void OnConfigDayCaseStyleChanged(string value) => SyncSelectedWidgetFields();
    partial void OnConfigShowDateChanged(bool value) => SyncSelectedWidgetFields();
    partial void OnConfigDatePlacementChanged(string value) => SyncSelectedWidgetFields();
    partial void OnConfigDateFormatChanged(string value) => SyncSelectedWidgetFields();
    partial void OnConfigShowGreetingChanged(bool value) => SyncSelectedWidgetFields();
    partial void OnConfigCustomGreetingChanged(string value) => SyncSelectedWidgetFields();
    partial void OnConfigTimeZoneIdChanged(string value) => SyncSelectedWidgetFields();
    partial void OnConfigFontFamilyChanged(string value)
    {
        if (!_suppressWidgetPush)
        {
            ConfigTimeFontFamily = value;
            ConfigDayFontFamily = value;
            ConfigDateFontFamily = value;
            ConfigGreetingFontFamily = value;
        }
        SyncSelectedWidgetFields();
    }
    partial void OnConfigTimeFontFamilyChanged(string value) => SyncSelectedWidgetFields();
    partial void OnConfigDayFontFamilyChanged(string value) => SyncSelectedWidgetFields();
    partial void OnConfigDateFontFamilyChanged(string value) => SyncSelectedWidgetFields();
    partial void OnConfigGreetingFontFamilyChanged(string value) => SyncSelectedWidgetFields();
    partial void OnConfigTimeFontWeightChanged(string value) => SyncSelectedWidgetFields();
    partial void OnConfigClockTimeBaseSizeChanged(double value) => SyncSelectedWidgetFields();
    partial void OnConfigFontScaleChanged(double value) => SyncSelectedWidgetFields();
    partial void OnConfigTimeScaleChanged(double value) => SyncSelectedWidgetFields();
    partial void OnConfigDayScaleChanged(double value) => SyncSelectedWidgetFields();
    partial void OnConfigDateScaleChanged(double value) => SyncSelectedWidgetFields();
    partial void OnConfigGreetingScaleChanged(double value) => SyncSelectedWidgetFields();
    partial void OnConfigAlignmentChanged(string value) => SyncSelectedWidgetFields();
    partial void OnConfigVerticalAlignmentChanged(string value) => SyncSelectedWidgetFields();
    partial void OnConfigClockTextShadowChanged(double value) => SyncSelectedWidgetFields();
    partial void OnConfigClockTextPrimaryHexChanged(string value) => SyncSelectedWidgetFields();
    partial void OnConfigClockAccentHexChanged(string value) => SyncSelectedWidgetFields();
    partial void OnConfigTimeUseAccentChanged(bool value) => SyncSelectedWidgetFields();
    partial void OnConfigTimeColorHexChanged(string value) => SyncSelectedWidgetFields();
    partial void OnConfigDayUseAccentChanged(bool value) => SyncSelectedWidgetFields();
    partial void OnConfigDayColorHexChanged(string value) => SyncSelectedWidgetFields();
    partial void OnConfigDateUseAccentChanged(bool value) => SyncSelectedWidgetFields();
    partial void OnConfigDateColorHexChanged(string value) => SyncSelectedWidgetFields();
    partial void OnConfigGreetingUseAccentChanged(bool value) => SyncSelectedWidgetFields();
    partial void OnConfigGreetingColorHexChanged(string value) => SyncSelectedWidgetFields();
    partial void OnConfigAnalogDialStyleChanged(string value) => SyncSelectedWidgetFields();

    partial void OnConfigStatsLayoutChanged(string value) => SyncSelectedWidgetFields();
    partial void OnConfigStatsShowCpuChanged(bool value) => SyncSelectedWidgetFields();
    partial void OnConfigStatsShowRamChanged(bool value) => SyncSelectedWidgetFields();
    partial void OnConfigStatsShowNetChanged(bool value) => SyncSelectedWidgetFields();
    partial void OnConfigStatsShowDiskChanged(bool value) => SyncSelectedWidgetFields();
    partial void OnConfigStatsShowAllDrivesChanged(bool value) => SyncSelectedWidgetFields();
    partial void OnConfigStatsShowGpuChanged(bool value) => SyncSelectedWidgetFields();
    partial void OnConfigStatsShowTempsChanged(bool value) => SyncSelectedWidgetFields();
    partial void OnConfigStatsShowUptimeChanged(bool value) => SyncSelectedWidgetFields();
    partial void OnConfigStatsShowPowerChanged(bool value) => SyncSelectedWidgetFields();

    partial void OnWidgetFocusWorkMinutesChanged(int value) => SyncSelectedWidgetFields();
    partial void OnWidgetFocusBreakMinutesChanged(int value) => SyncSelectedWidgetFields();
    partial void OnWidgetCustomSubtitleChanged(string value) => SyncSelectedWidgetFields();

    partial void OnSelectedGlassPresetChanged(GlassmorphismStyle? value)
    {
        if (value == null) return;
        ApplyStyleToEditorSliders(value.Clone());
        _coordinator.UpdateGlobalGlassLive(LiveGlassStyle, _activeProfile?.WidgetBrightness ?? 1.0);
        PreviewSurfaceInvalidated?.Invoke();
    }

    partial void OnGlassSurfaceThemeChanged(WidgetSurfaceTheme value) => PushGlassSlidersLive();
    partial void OnGlassBackgroundTransparencyChanged(double value) => PushGlassSlidersLive();
    partial void OnGlassBackgroundTintHexChanged(string value) => PushGlassSlidersLive();
    partial void OnGlassGradientSecondaryHexChanged(string value) => PushGlassSlidersLive();
    partial void OnGlassBackgroundOpacityChanged(double value) => PushGlassSlidersLive();
    partial void OnGlassBackgroundBlurIntensityChanged(double value) => PushGlassSlidersLive();
    partial void OnGlassEnableBackdropBlurChanged(bool value) => PushGlassSlidersLive();
    partial void OnGlassBorderTransparencyChanged(double value) => PushGlassSlidersLive();
    partial void OnGlassBorderThicknessChanged(double value) => PushGlassSlidersLive();
    partial void OnGlassBorderColorHexChanged(string value) => PushGlassSlidersLive();
    partial void OnGlassCornerRadiusChanged(double value) => PushGlassSlidersLive();
    partial void OnGlassInnerShadowIntensityChanged(double value) => PushGlassSlidersLive();
    partial void OnGlassOuterShadowIntensityChanged(double value) => PushGlassSlidersLive();
    partial void OnGlassGlowIntensityChanged(double value) => PushGlassSlidersLive();
    partial void OnGlassHighlightIntensityChanged(double value) => PushGlassSlidersLive();
    partial void OnGlassAppearanceVariantChanged(GlassAppearanceVariant value) => PushGlassSlidersLive();
    partial void OnGlassTextPrimaryHexChanged(string value) => PushGlassSlidersLive();
    partial void OnGlassTextSecondaryHexChanged(string value) => PushGlassSlidersLive();
    partial void OnGlassAccentHexChanged(string value) => PushGlassSlidersLive();

    partial void OnMarketplaceSearchTextChanged(string value) => ApplyMarketplaceFilter();
    partial void OnSelectedMarketplaceCategoryChanged(string value) => ApplyMarketplaceFilter();
    partial void OnSelectedMarketplaceSortChanged(string value) => ApplyMarketplaceFilter();

    private void SyncSelectedWidgetFields()
    {
        if (_suppressWidgetPush || SelectedWidget == null) return;

        SelectedWidget.Title = SelectedWidgetTitle;
        SelectedWidget.TargetMonitorAnchor = SelectedWidgetAnchor;
        SelectedWidget.X = SelectedWidgetX;
        SelectedWidget.Y = SelectedWidgetY;
        SelectedWidget.Width = Math.Clamp(SelectedWidgetWidth, 140, 3840);
        SelectedWidget.Height = Math.Clamp(SelectedWidgetHeight, 70, 2160);
        SelectedWidget.IsVisible = SelectedWidgetVisible;
        SelectedWidget.IsClickThrough = SelectedWidgetClickThrough;
        SelectedWidget.MinimalAnimations = SelectedWidgetMinimalAnim;
        SelectedWidget.UseGlobalGlassTheme = SelectedWidgetUseGlobalGlass;
        SelectedWidget.BrightnessMultiplier = SelectedWidgetBrightness;

        var c = SelectedWidget.Config;
        c["City"] = ConfigWeatherCity.Trim();
        c["Provider"] = ConfigWeatherProvider;
        c["ApiKey"] = ConfigWeatherApiKey.Trim();
        c["Units"] = ConfigWeatherUnit.Contains("imperial", StringComparison.OrdinalIgnoreCase) ? "imperial" : "metric";
        c["WeatherIconTheme"] = ConfigWeatherIconTheme;
        c["WeatherShowFeelsLike"] = ConfigWeatherShowFeelsLike ? "true" : "false";
        c["WeatherShowHumidity"] = ConfigWeatherShowHumidity ? "true" : "false";
        c["WeatherShowWind"] = ConfigWeatherShowWind ? "true" : "false";
        c["WeatherShowProvider"] = ConfigWeatherShowProvider ? "true" : "false";

        c["ClockLayout"] = ConfigClockLayout;
        c["Format24H"] = ConfigFormat24H ? "true" : "false";
        c["ShowAmPm"] = ConfigShowAmPm ? "true" : "false";
        c["ShowSeconds"] = ConfigShowSeconds ? "true" : "false";
        c["ShowDay"] = ConfigShowDay ? "true" : "false";
        c["DayPlacement"] = ConfigDayPlacement;
        c["DayCaseStyle"] = ConfigDayCaseStyle;
        c["ShowDate"] = ConfigShowDate ? "true" : "false";
        c["DatePlacement"] = ConfigDatePlacement;
        c["DateFormat"] = ConfigDateFormat;
        c["ShowGreeting"] = ConfigShowGreeting ? "true" : "false";
        c["CustomGreeting"] = ConfigCustomGreeting;
        c["TimeZoneId"] = ConfigTimeZoneId;
        c["FontFamily"] = ConfigFontFamily;
        c["TimeFontFamily"] = ConfigTimeFontFamily;
        c["DayFontFamily"] = ConfigDayFontFamily;
        c["DateFontFamily"] = ConfigDateFontFamily;
        c["GreetingFontFamily"] = ConfigGreetingFontFamily;
        c["TimeFontWeight"] = ConfigTimeFontWeight;
        c["ClockTimeBaseSize"] = Math.Clamp(ConfigClockTimeBaseSize, 16, 220).ToString("0", CultureInfo.InvariantCulture);
        c["FontScale"] = Math.Clamp(ConfigFontScale, 0.45, 5.0).ToString("0.00", CultureInfo.InvariantCulture);
        c["TimeScale"] = Math.Clamp(ConfigTimeScale, 0.45, 3.0).ToString("0.00", CultureInfo.InvariantCulture);
        c["DayScale"] = Math.Clamp(ConfigDayScale, 0.45, 3.0).ToString("0.00", CultureInfo.InvariantCulture);
        c["DateScale"] = Math.Clamp(ConfigDateScale, 0.45, 3.0).ToString("0.00", CultureInfo.InvariantCulture);
        c["GreetingScale"] = Math.Clamp(ConfigGreetingScale, 0.45, 3.0).ToString("0.00", CultureInfo.InvariantCulture);
        c["Alignment"] = ConfigAlignment;
        c["VerticalAlignment"] = ConfigVerticalAlignment;
        c["ClockTextShadow"] = Math.Clamp(ConfigClockTextShadow, 0.0, 1.0).ToString("0.00", CultureInfo.InvariantCulture);
        c["ClockTextPrimaryHex"] = ConfigClockTextPrimaryHex.Trim();
        c["ClockAccentHex"] = ConfigClockAccentHex.Trim();
        c["TimeUseAccent"] = ConfigTimeUseAccent ? "true" : "false";
        c["TimeColorHex"] = ConfigTimeColorHex.Trim();
        c["DayUseAccent"] = ConfigDayUseAccent ? "true" : "false";
        c["DayColorHex"] = ConfigDayColorHex.Trim();
        c["DateUseAccent"] = ConfigDateUseAccent ? "true" : "false";
        c["DateColorHex"] = ConfigDateColorHex.Trim();
        c["GreetingUseAccent"] = ConfigGreetingUseAccent ? "true" : "false";
        c["GreetingColorHex"] = ConfigGreetingColorHex.Trim();
        c["AnalogDialStyle"] = ConfigAnalogDialStyle;

        c["ShortcutLayout"] = ConfigShortcutLayout;
        c["ShortcutIconScale"] = Math.Clamp(ConfigShortcutIconScale, 0.45, 2.50).ToString("0.00", CultureInfo.InvariantCulture);
        if (SelectedWidget.Kind == WidgetKind.ShortcutBar)
            ShortcutDockConfigHelper.SaveItems(c, ShortcutItems);

        c["StatsLayout"] = ConfigStatsLayout;
        c["ShowCpu"] = ConfigStatsShowCpu ? "true" : "false";
        c["ShowRam"] = ConfigStatsShowRam ? "true" : "false";
        c["ShowNet"] = ConfigStatsShowNet ? "true" : "false";
        c["ShowDisk"] = ConfigStatsShowDisk ? "true" : "false";
        c["ShowAllDrives"] = ConfigStatsShowAllDrives ? "true" : "false";
        c["ShowGpu"] = ConfigStatsShowGpu ? "true" : "false";
        c["ShowTemps"] = ConfigStatsShowTemps ? "true" : "false";
        c["ShowUptime"] = ConfigStatsShowUptime ? "true" : "false";
        c["ShowPower"] = ConfigStatsShowPower ? "true" : "false";

        c["WorkMinutes"] = Math.Clamp(WidgetFocusWorkMinutes, 1, 180).ToString(CultureInfo.InvariantCulture);
        c["BreakMinutes"] = Math.Clamp(WidgetFocusBreakMinutes, 1, 60).ToString(CultureInfo.InvariantCulture);
        if (!string.IsNullOrWhiteSpace(WidgetCustomSubtitle))
            c["Subtitle"] = WidgetCustomSubtitle;

        PersistAndPushProfile();
    }

    private void PersistAndPushProfile()
    {
        if (_activeProfile != null)
            _db.SaveDesktopProfile(_activeProfile);
        _coordinator.RebuildOverlays();
        RefreshMarketplaceDesktopPresence();
        PreviewSurfaceInvalidated?.Invoke();
    }

    private void LoadGlassPresets()
    {
        GlassPresets.Clear();
        NonGlassRainmeterPresets.Clear();
        AllSelectableStylePresets.Clear();

        foreach (var builtIn in GlassmorphismStyle.BuiltInPresets)
        {
            GlassPresets.Add(builtIn.Clone());
            AllSelectableStylePresets.Add(builtIn.Clone());
        }
        foreach (var custom in _db.LoadCustomGlassPresets())
        {
            GlassPresets.Add(custom);
            AllSelectableStylePresets.Add(custom);
        }
        foreach (var rm in GlassmorphismStyle.RainmeterStylePresets)
        {
            NonGlassRainmeterPresets.Add(rm.Clone());
            AllSelectableStylePresets.Add(rm.Clone());
        }
    }

    private void ApplyStyleToEditorSliders(GlassmorphismStyle style)
    {
        _suppressGlassPush = true;
        LiveGlassStyle = style.Clone();
        GlassSurfaceTheme = style.SurfaceTheme;
        GlassBackgroundTransparency = style.BackgroundTransparency;
        GlassBackgroundTintHex = style.BackgroundTintHex;
        GlassGradientSecondaryHex = style.GradientSecondaryHex;
        GlassBackgroundOpacity = style.BackgroundOpacity;
        GlassBackgroundBlurIntensity = style.BackgroundBlurIntensity;
        GlassEnableBackdropBlur = style.EnableBackdropBlur;
        GlassBorderTransparency = style.BorderTransparency;
        GlassBorderThickness = style.BorderThickness;
        GlassBorderColorHex = style.BorderColorHex;
        GlassCornerRadius = style.CornerRadius;
        GlassInnerShadowIntensity = style.InnerShadowIntensity;
        GlassOuterShadowIntensity = style.OuterShadowIntensity;
        GlassGlowIntensity = style.GlowIntensity;
        GlassHighlightIntensity = style.HighlightIntensity;
        GlassAppearanceVariant = style.AppearanceVariant;
        GlassTextPrimaryHex = style.TextPrimaryHex;
        GlassTextSecondaryHex = style.TextSecondaryHex;
        GlassAccentHex = style.AccentHex;
        _suppressGlassPush = false;
    }

    private void PushGlassSlidersLive()
    {
        if (_suppressGlassPush) return;

        LiveGlassStyle.SurfaceTheme = GlassSurfaceTheme;
        LiveGlassStyle.BackgroundTransparency = GlassBackgroundTransparency;
        LiveGlassStyle.BackgroundTintHex = GlassBackgroundTintHex;
        LiveGlassStyle.GradientSecondaryHex = GlassGradientSecondaryHex;
        LiveGlassStyle.BackgroundOpacity = GlassBackgroundOpacity;
        LiveGlassStyle.BackgroundBlurIntensity = GlassBackgroundBlurIntensity;
        LiveGlassStyle.EnableBackdropBlur = GlassEnableBackdropBlur;
        LiveGlassStyle.BorderTransparency = GlassBorderTransparency;
        LiveGlassStyle.BorderThickness = GlassBorderThickness;
        LiveGlassStyle.BorderColorHex = GlassBorderColorHex;
        LiveGlassStyle.CornerRadius = GlassCornerRadius;
        LiveGlassStyle.InnerShadowIntensity = GlassInnerShadowIntensity;
        LiveGlassStyle.OuterShadowIntensity = GlassOuterShadowIntensity;
        LiveGlassStyle.GlowIntensity = GlassGlowIntensity;
        LiveGlassStyle.HighlightIntensity = GlassHighlightIntensity;
        LiveGlassStyle.AppearanceVariant = GlassAppearanceVariant;
        LiveGlassStyle.TextPrimaryHex = GlassTextPrimaryHex;
        LiveGlassStyle.TextSecondaryHex = GlassTextSecondaryHex;
        LiveGlassStyle.AccentHex = GlassAccentHex;

        _coordinator.UpdateGlobalGlassLive(LiveGlassStyle, _activeProfile?.WidgetBrightness ?? 1.0);
        PreviewSurfaceInvalidated?.Invoke();
    }

    private void ApplyMarketplaceFilter()
    {
        IEnumerable<MarketplaceCatalogEntry> q = _allMarketplaceEntries;

        if (!string.Equals(SelectedMarketplaceCategory, "All", StringComparison.OrdinalIgnoreCase))
        {
            if (string.Equals(SelectedMarketplaceCategory, "Imported Packages", StringComparison.OrdinalIgnoreCase))
                q = q.Where(e => !e.Manifest.Id.StartsWith("builtin.", StringComparison.OrdinalIgnoreCase) &&
                                 !e.Manifest.Id.StartsWith("rainmeter.", StringComparison.OrdinalIgnoreCase));
            else
                q = q.Where(e => string.Equals(e.Manifest.Category, SelectedMarketplaceCategory, StringComparison.OrdinalIgnoreCase));
        }

        if (!string.IsNullOrWhiteSpace(MarketplaceSearchText))
        {
            string s = MarketplaceSearchText.Trim();
            q = q.Where(e =>
                e.Manifest.Name.Contains(s, StringComparison.OrdinalIgnoreCase) ||
                e.Manifest.Description.Contains(s, StringComparison.OrdinalIgnoreCase) ||
                e.Manifest.Author.Contains(s, StringComparison.OrdinalIgnoreCase) ||
                e.Manifest.Tags.Any(t => t.Contains(s, StringComparison.OrdinalIgnoreCase)));
        }

        q = SelectedMarketplaceSort switch
        {
            "Popular" => q.OrderByDescending(e => e.Manifest.DownloadCount),
            "Highest Rated" => q.OrderByDescending(e => e.Manifest.Rating),
            "Recently Added" => q.OrderByDescending(e => e.Manifest.PublishedUtc),
            _ => q.OrderByDescending(e => e.IsFeatured).ThenByDescending(e => e.Manifest.Rating)
        };

        FilteredMarketplaceEntries.Clear();
        foreach (var item in q)
            FilteredMarketplaceEntries.Add(item);

        SelectedMarketplaceEntry ??= FilteredMarketplaceEntries.FirstOrDefault();
    }

    private static string GetCfg(WidgetInstance w, string key, string fallback) =>
        w.Config.TryGetValue(key, out var val) && !string.IsNullOrWhiteSpace(val) ? val : fallback;

    private static bool GetBoolCfg(WidgetInstance w, string key, bool fallback) =>
        w.Config.TryGetValue(key, out var val) && bool.TryParse(val, out bool b) ? b : fallback;

    private static string BuildDefaultWidgetTitle(WidgetKind kind) => kind switch
    {
        WidgetKind.DigitalClock => "Digital Clock",
        WidgetKind.AnalogClock => "Analog Timepiece",
        WidgetKind.Weather => "Weather Forecast",
        WidgetKind.CalendarAgenda => "Today's Calendar",
        WidgetKind.TodoList => "To-Do Checklist",
        WidgetKind.FocusTimer => "Focus Timer",
        WidgetKind.SystemStats => "System Telemetry",
        WidgetKind.ShortcutBar => "Quick Shortcut Dock",
        WidgetKind.PanoramicVideo => "Panoramic Video",
        _ => "Custom Widget"
    };

    private static string SanitizeFileName(string name)
    {
        foreach (char c in Path.GetInvalidFileNameChars())
            name = name.Replace(c, '_');
        return string.IsNullOrWhiteSpace(name) ? "duowall-export" : name.Trim();
    }
}
