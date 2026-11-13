using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DuoWall.App.Helpers;
using DuoWall.App.Services;
using DuoWall.App.Windows;
using DuoWall.Core.Engine;
using DuoWall.Core.Gallery;
using DuoWall.Core.Models;
using DuoWall.Core.Packages;
using DuoWall.Core.Services;
using DuoWall.Core.Storage;
using DuoWall.Core.Theming;
using Microsoft.Win32;
using Wpf.Ui.Appearance;

namespace DuoWall.App.ViewModels;

public sealed partial class MainViewModel : ObservableObject, IDisposable
{
    private readonly Win32DisplayDetector _displayDetector;
    private readonly DesktopWallpaperApplier _wallpaperApplier;
    private readonly WallpaperGeometryEngine _geometryEngine;
    private readonly SkiaWallpaperCompositor _compositor;
    private readonly IWallpaperSourceProvider _galleryProvider;
    private readonly WallpaperLibraryService _libraryService;
    private readonly DuoWallDatabase _db;
    private readonly StartupAndWatcherService _watcher;
    private readonly WeatherService _weatherService;
    private readonly GitHubReleaseService _releaseService;
    private readonly DesktopOverlayCoordinator _overlayCoordinator;
    private readonly TaskbarTweaksService _taskbarTweaksService;
    private readonly CustomWebsiteGalleryProvider _customSiteProvider = new();

    private CancellationTokenSource? _galleryCts;
    private CancellationTokenSource? _searchDebounceCts;
    private string _liveTopologySignature = string.Empty;
    private bool _suppressPlanRecompute;
    private bool _suppressRgbSync;
    private bool _suppressGallerySourceSync;

    // Navigation & Banner State
    [ObservableProperty] private int _currentPageIndex;
    [ObservableProperty] private string _statusBannerMessage = string.Empty;
    [ObservableProperty] private bool _hasStatusBanner;
    [ObservableProperty] private bool _isStatusError;
    [ObservableProperty] private bool _isApplying;
    [ObservableProperty] private double _applyProgress;
    [ObservableProperty] private string _applyProgressMessage = string.Empty;

    // Monitors & Topology
    public ObservableCollection<MonitorItemViewModel> Monitors { get; } = new();
    public IReadOnlyList<string> SimulationPresets { get; } = new[]
    {
        "Live Hardware Displays",
        "Simulated: 1080p Left + 1440p Right (Offset)",
        "Simulated: 3 Monitors (Portrait + 1440p + 1080p)",
        "Simulated: Stacked Top + Bottom"
    };

    [ObservableProperty] private string _selectedSimulationPreset = "Live Hardware Displays";
    [ObservableProperty] private bool _isSimulatedTopology;
    [ObservableProperty] private string _monitorSummaryHeader = "Detecting displays...";
    [ObservableProperty] private string _combinedGeometrySummary = string.Empty;
    [ObservableProperty] private string? _selectedMonitorId;

    // Active / Staged Wallpaper & Composition Plan
    [ObservableProperty] private WallpaperCardViewModel? _stagedWallpaper;
    [ObservableProperty] private WallpaperCardViewModel? _activeAppliedWallpaper;
    [ObservableProperty] private CompositionPlan? _currentPlan;
    [ObservableProperty] private string? _resolutionAdvisory;
    [ObservableProperty] private string _stagedPreviewSource = string.Empty;

    // Placement Settings
    [ObservableProperty] private WallpaperPlacementMode _placementMode = WallpaperPlacementMode.PanoramicSpan;
    [ObservableProperty] private GeometryCoordinateSpace _coordinateSpace = GeometryCoordinateSpace.PhysicalDimensions;
    [ObservableProperty] private PhysicalVerticalAlignment _physicalAlignment = PhysicalVerticalAlignment.AlignBottom;
    [ObservableProperty] private bool _enableBezelCompensation = true;
    [ObservableProperty] private double _zoom = 1.0;
    [ObservableProperty] private double _panX;
    [ObservableProperty] private double _panY;
    [ObservableProperty] private double _focalX = 0.5;
    [ObservableProperty] private double _focalY = 0.5;
    [ObservableProperty] private double _cropLeft;
    [ObservableProperty] private double _cropTop;
    [ObservableProperty] private double _cropRight;
    [ObservableProperty] private double _cropBottom;
    [ObservableProperty] private bool _flipHorizontal;
    [ObservableProperty] private bool _flipVertical;
    [ObservableProperty] private bool _autoApplyStudioChanges = true;
    [ObservableProperty] private bool _isStudioBoxSelectMode;
    [ObservableProperty] private string _backgroundColorHex = "#0D1017";
    private CancellationTokenSource? _studioApplyDebounceCts;
    [ObservableProperty] private string _normalizedBackgroundBrushHex = "#0D1017";
    [ObservableProperty] private bool _isColorPickerOpen;
    [ObservableProperty] private byte _bgRed = 13;
    [ObservableProperty] private byte _bgGreen = 16;
    [ObservableProperty] private byte _bgBlue = 23;
    [ObservableProperty] private bool _showMonitorLabels = true;
    [ObservableProperty] private bool _isIndividualMode;

    // Gallery State
    public ObservableCollection<WallpaperCardViewModel> GalleryItems { get; } = new();
    public IReadOnlyList<string> GalleryCategories { get; } =
        UltraWideWallpapersProvider.BuiltInFilterCategories.Concat(new[] { "Favorites" }).ToArray();
    public IReadOnlyList<GallerySortMode> SortModes { get; } = Enum.GetValues<GallerySortMode>();
    public IReadOnlyList<string> GallerySourceOptions { get; } = new[] { "UltraWideWallpapers.net", "Custom Website" };

    [ObservableProperty] private string _selectedGallerySource = "UltraWideWallpapers.net";
    [ObservableProperty] private string _customGallerySiteUrl = string.Empty;
    [ObservableProperty] private string _selectedGalleryCategory = "All";
    [ObservableProperty] private GallerySortMode _selectedSortMode = GallerySortMode.Recent;
    [ObservableProperty] private string _gallerySearchText = string.Empty;
    [ObservableProperty] private bool _isLoadingGallery;
    [ObservableProperty] private bool _hasMoreGalleryItems = true;
    [ObservableProperty] private string _galleryStatusText = "Ready";
    [ObservableProperty] private WallpaperCardViewModel? _selectedDetailWallpaper;
    [ObservableProperty] private bool _isDetailOpen;
    [ObservableProperty] private bool _detailShowMultiMonitorPreview;
    [ObservableProperty] private CompositionPlan? _detailPreviewPlan;

    // Local Library State
    public ObservableCollection<WallpaperCardViewModel> LibraryItems { get; } = new();
    public ObservableCollection<WallpaperCardViewModel> RecentWallpapers { get; } = new();
    public IReadOnlyList<string> LibraryTabs { get; } = new[] { "All Downloaded", "Favorites", "Recently Used" };

    [ObservableProperty] private string _selectedLibraryTab = "All Downloaded";
    [ObservableProperty] private string _librarySearchText = string.Empty;

    // Settings State
    [ObservableProperty] private bool _isDarkTheme = true;
    [ObservableProperty] private bool _autoApplyOnStartup;
    [ObservableProperty] private bool _autoReapplyOnDisplayChange = true;
    [ObservableProperty] private string _downloadDirectory = string.Empty;
    [ObservableProperty] private OutputImageFormat _outputFormat = OutputImageFormat.PngLossless;
    [ObservableProperty] private int _maxThumbnailCacheMb = 500;
    [ObservableProperty] private string _thumbnailCacheSizeText = "0 MB";
    [ObservableProperty] private string _generatedCacheSizeText = "0 MB";
    [ObservableProperty] private bool _hasWindowsBackup;

    // V2.0 Widget Studio, Weather & Release Update Check State
    [ObservableProperty] private bool _enableDesktopWidgets = true;
    [ObservableProperty] private string _weatherCity = "Lahore";
    [ObservableProperty] private bool _weatherUseFahrenheit;
    [ObservableProperty] private string _customWeatherApiKey = string.Empty;
    [ObservableProperty] private string _gitHubRepositorySlug = GitHubReleaseService.DefaultRepoSlug;
    [ObservableProperty] private string _latestReleasePageUrl = $"https://github.com/{GitHubReleaseService.DefaultRepoSlug}/releases";
    [ObservableProperty] private bool _isVideoWallpaperStaged;
    private string? _stagedLiveVideoPath;

    public WidgetStudioViewModel WidgetStudio { get; }
    public ProfilesViewModel Profiles { get; }
    public LiveWallpaperViewModel LiveWallpaper { get; }
    public TaskbarTweaksViewModel TaskbarTweaks { get; }

    public AppSettings Settings { get; private set; }

    public IReadOnlyList<WallpaperPlacementMode> PlacementModes { get; } = Enum.GetValues<WallpaperPlacementMode>();
    public IReadOnlyList<GeometryCoordinateSpace> CoordinateSpaces { get; } = Enum.GetValues<GeometryCoordinateSpace>();
    public IReadOnlyList<PhysicalVerticalAlignment> PhysicalAlignments { get; } = Enum.GetValues<PhysicalVerticalAlignment>();
    public IReadOnlyList<string> BackgroundSwatches { get; } = new[]
    {
        "#000000", "#0D1017", "#18181B", "#1E293B", "#0F172A",
        "#14532D", "#450A0A", "#312E81", "#CBD5E1", "#FFFFFF"
    };

    private readonly Dictionary<string, PerMonitorAssignment> _individualAssignments = new(StringComparer.OrdinalIgnoreCase);
    private int _galleryOffset;

    public MainViewModel(
        Win32DisplayDetector displayDetector,
        DesktopWallpaperApplier wallpaperApplier,
        WallpaperGeometryEngine geometryEngine,
        SkiaWallpaperCompositor compositor,
        IWallpaperSourceProvider galleryProvider,
        WallpaperLibraryService libraryService,
        DuoWallDatabase db,
        StartupAndWatcherService watcher,
        WeatherService weatherService,
        WallpaperColorAnalyzer colorAnalyzer,
        WidgetPackageService packageService,
        LocalWidgetMarketplaceProvider marketplaceProvider,
        GitHubReleaseService releaseService,
        YouTubeWallpaperService youtubeService,
        TaskbarTweaksService taskbarTweaksService,
        DesktopOverlayCoordinator overlayCoordinator)
    {
        _displayDetector = displayDetector;
        _wallpaperApplier = wallpaperApplier;
        _geometryEngine = geometryEngine;
        _compositor = compositor;
        _galleryProvider = galleryProvider;
        _libraryService = libraryService;
        _db = db;
        _watcher = watcher;
        _weatherService = weatherService;
        _releaseService = releaseService;
        _taskbarTweaksService = taskbarTweaksService;
        _overlayCoordinator = overlayCoordinator;

        Settings = _db.LoadSettings();

        WidgetStudio = new WidgetStudioViewModel(
            _db,
            packageService,
            marketplaceProvider,
            _overlayCoordinator,
            SetStatus,
            () => Profiles?.ReloadProfilesAndPalettes());

        Profiles = new ProfilesViewModel(
            _db,
            colorAnalyzer,
            packageService,
            _overlayCoordinator,
            GetCurrentTopologyKey,
            profile =>
            {
                WidgetStudio.BindActiveProfile(profile);
                GlassmorphismStyle preset = GlassmorphismStyle.GetPreset(profile.GlassPresetName);
                WidgetStudio.ApplyGlobalGlassFromExternal(preset, persistToProfile: false);
                _overlayCoordinator.UpdateConfiguration(
                    Settings,
                    Monitors.Select(m => m.Model).ToList(),
                    profile,
                    WidgetStudio.LiveGlassStyle);
            },
            suggestedGlass =>
            {
                WidgetStudio.ApplyGlobalGlassFromExternal(suggestedGlass, persistToProfile: true);
            },
            SetStatus);

        LiveWallpaper = new LiveWallpaperViewModel(
            youtubeService,
            _db,
            _overlayCoordinator,
            StageVideoInPlacementStudio,
            SetStatus,
            () => CurrentPlan,
            () => BackgroundColorHex,
            RefreshLocalLibrary);

        TaskbarTweaks = new TaskbarTweaksViewModel(
            _db,
            _taskbarTweaksService,
            () => WidgetStudio.LiveGlassStyle,
            SetStatus);

        WidgetStudio.PreviewSurfaceInvalidated += () => TaskbarTweaks.RefreshGlassThemeChanged();

        _overlayCoordinator.WidgetBoundsUpdatedFromDesktop += w => WidgetStudio.NotifyDesktopWidgetBoundsChanged(w);
        _overlayCoordinator.AutoProfileActivated += (prof, res) =>
        {
            Profiles.HandleAutoProfileSwitched(prof, res.MatchReason);
            WidgetStudio.BindActiveProfile(prof);
        };
        _overlayCoordinator.StatusMessageRaised += msg => SetStatus(msg, isError: false);

        SyncPropertiesFromSettings();

        _watcher.DisplayConfigurationStabilized += async (_, _) => await OnDisplayTopologyChangedAsync();
        _watcher.StartWatching();
    }

    public async Task InitializeAsync(bool isStartupApplyMode)
    {
        RefreshHardwareMonitors();
        RefreshLocalLibrary();
        RefreshCacheMetrics();
        HasWindowsBackup = _db.LoadWindowsBackup() != null;

        // Load last applied or most recent wallpaper into studio
        var lastId = Settings.LastAppliedWallpaperId;
        WallpaperItem? lastItem = !string.IsNullOrWhiteSpace(lastId) ? _db.GetWallpaperById(lastId) : null;
        if (lastItem == null)
            lastItem = _db.GetAllLibraryWallpapers().FirstOrDefault(w => w.IsDownloaded);

        if (lastItem != null)
        {
            var card = new WallpaperCardViewModel(lastItem);
            if (!isStartupApplyMode)
            {
                await card.EnsureThumbnailLoadedAsync(_galleryProvider, Settings.ThumbnailCacheDirectory);
                await card.EnsurePreviewLoadedAsync(Settings.ThumbnailCacheDirectory);
            }
            ActiveAppliedWallpaper = card;
            StageWallpaper(card, loadProfile: true);
        }

        if (isStartupApplyMode && lastItem != null && lastItem.IsDownloaded)
        {
            await ApplyWallpaperAsync();
            ImageCacheHelper.ClearMemoryCache();
            if (!AutoReapplyOnDisplayChange)
            {
                Application.Current.Shutdown(0);
                return;
            }
        }

        Profiles.Initialize(Settings);
        var activeProfile = Profiles.GetActiveProfile();
        _overlayCoordinator.RefreshWallpaperCache(Monitors.Select(m => m.Model).ToList());
        await WidgetStudio.InitializeAsync(Settings, activeProfile);

        // Live Wallpaper tab removed per user request; clear any active live video overlay at startup
        if (!string.IsNullOrWhiteSpace(Settings.ActiveLiveVideoPath))
        {
            Settings.ActiveLiveVideoPath = null;
            _db.SaveSettings(Settings);
        }

        _overlayCoordinator.Initialize(
            Settings,
            Monitors.Select(m => m.Model).ToList(),
            activeProfile,
            WidgetStudio.LiveGlassStyle);

        WidgetStudio.RefreshWallpaperPreviewBackdrop(Settings.LastAppliedImagePath);
        TaskbarTweaks.Initialize(Settings);

        if (isStartupApplyMode)
        {
            return;
        }

        await LoadGalleryAsync(reset: true);
    }

    private void SyncPropertiesFromSettings()
    {
        _suppressPlanRecompute = true;
        IsDarkTheme = Settings.Theme == AppThemeMode.Dark;
        AutoApplyOnStartup = Settings.AutoApplyOnWindowsStartup || StartupAndWatcherService.IsStartupEnabled();
        AutoReapplyOnDisplayChange = Settings.AutoReapplyOnDisplayChange;
        DownloadDirectory = Settings.DownloadDirectory;
        OutputFormat = Settings.OutputFormat;
        MaxThumbnailCacheMb = Settings.MaxThumbnailCacheMb;
        PlacementMode = Settings.DefaultPlacementMode;
        CoordinateSpace = Settings.DefaultCoordinateSpace;
        PhysicalAlignment = Settings.DefaultPhysicalAlignment;
        EnableBezelCompensation = Settings.DefaultBezelCompensation;
        ShowMonitorLabels = Settings.ShowMonitorLabelsInPreview;

        EnableDesktopWidgets = Settings.EnableDesktopWidgets;
        WeatherCity = Settings.WeatherCity;
        WeatherUseFahrenheit = Settings.WeatherUseFahrenheit;
        CustomWeatherApiKey = Settings.CustomWeatherApiKey ?? string.Empty;
        GitHubRepositorySlug = string.IsNullOrWhiteSpace(Settings.GitHubRepositorySlug)
            ? GitHubReleaseService.DefaultRepoSlug
            : Settings.GitHubRepositorySlug;
        LatestReleasePageUrl = $"https://github.com/{GitHubRepositorySlug}/releases";

        _suppressGallerySourceSync = true;
        CustomGallerySiteUrl = Settings.CustomGallerySiteUrl ?? string.Empty;
        _customSiteProvider.CustomSiteUrl = CustomGallerySiteUrl;
        SelectedGallerySource = string.Equals(Settings.PreferredGallerySource, "CustomWebsite", StringComparison.OrdinalIgnoreCase)
            ? "Custom Website"
            : "UltraWideWallpapers.net";
        _suppressGallerySourceSync = false;

        _suppressPlanRecompute = false;
    }

    [RelayCommand]
    public void RefreshHardwareMonitors()
    {
        IReadOnlyList<MonitorInfo> detected = SelectedSimulationPreset switch
        {
            "Simulated: 1080p Left + 1440p Right (Offset)" => BuildSimulated1080Plus1440(),
            "Simulated: 3 Monitors (Portrait + 1440p + 1080p)" => BuildSimulatedThreeMonitors(),
            "Simulated: Stacked Top + Bottom" => BuildSimulatedStacked(),
            _ => _displayDetector.DetectMonitors()
        };

        IsSimulatedTopology = !SelectedSimulationPreset.StartsWith("Live", StringComparison.Ordinal);

        if (!IsSimulatedTopology)
        {
            _db.ApplyMonitorCalibrations(detected);
            _liveTopologySignature = Win32DisplayDetector.ComputeTopologySignature(detected);
        }

        Monitors.Clear();
        foreach (var m in detected)
        {
            Monitors.Add(new MonitorItemViewModel(m, OnMonitorCalibrationEdited));
        }

        if (Monitors.Count > 0 && (SelectedMonitorId == null || !Monitors.Any(m => m.Id == SelectedMonitorId)))
            SelectedMonitorId = Monitors[0].Id;

        UpdateMonitorSummaries();
        RecomputePlan();
    }

    private void OnMonitorCalibrationEdited()
    {
        if (!IsSimulatedTopology)
        {
            _db.SaveMonitorCalibrations(Monitors.Select(m => m.Model));
        }
        RecomputePlan();
    }

    private void UpdateMonitorSummaries()
    {
        var active = Monitors.Where(m => m.Model.IsConnected && m.IsIncluded).Select(m => m.Model).ToList();
        if (active.Count == 0) active = Monitors.Select(m => m.Model).ToList();

        if (active.Count == 0)
        {
            MonitorSummaryHeader = "No displays detected";
            CombinedGeometrySummary = string.Empty;
            return;
        }

        int minX = active.Min(m => m.Bounds.X);
        int minY = active.Min(m => m.Bounds.Y);
        int maxX = active.Max(m => m.Bounds.Right);
        int maxY = active.Max(m => m.Bounds.Bottom);
        int virtW = maxX - minX;
        int virtH = maxY - minY;

        MonitorSummaryHeader = $"{active.Count} {(active.Count == 1 ? "Display" : "Displays")} · {string.Join(" + ", active.Select(m => m.DisplayLabel))}";
        CombinedGeometrySummary = $"Virtual Desktop: {virtW} × {virtH} px ({MonitorInfo.FormatAspectRatio(virtW, virtH)})" +
                                  (IsSimulatedTopology ? "  [SIMULATED LAYOUT]" : string.Empty);
    }

    public void StageWallpaper(WallpaperCardViewModel card, bool loadProfile = true)
    {
        if (WallpaperCardViewModel.IsVideoFile(card.Model.LocalOriginalPath) && File.Exists(card.Model.LocalOriginalPath))
        {
            StageVideoInPlacementStudio(card.Model.LocalOriginalPath!, card.Model.LocalThumbnailPath);
            return;
        }
        IsVideoWallpaperStaged = false;
        _stagedLiveVideoPath = null;
        StageWallpaperCore(card, loadProfile);
    }

    public void StageVideoInPlacementStudio(string videoPath, string? stillImagePath)
    {
        if (string.IsNullOrWhiteSpace(videoPath) || !File.Exists(videoPath)) return;

        int vw = 3840, vh = 1600;
        if (!string.IsNullOrWhiteSpace(stillImagePath) && File.Exists(stillImagePath))
        {
            var dims = SkiaWallpaperCompositor.ReadImageDimensions(stillImagePath);
            if (dims.Width > 0 && dims.Height > 0)
            {
                vw = dims.Width;
                vh = dims.Height;
            }
        }

        string vidId = $"livevideo:{Path.GetFileName(videoPath)}";
        var item = new WallpaperItem
        {
            Id = vidId,
            Title = Path.GetFileNameWithoutExtension(videoPath),
            FileName = Path.GetFileName(videoPath),
            Width = vw,
            Height = vh,
            LocalOriginalPath = stillImagePath,
            PreviewUrl = stillImagePath ?? string.Empty
        };

        var card = new WallpaperCardViewModel(item);
        _stagedLiveVideoPath = videoPath;
        IsVideoWallpaperStaged = true;
        StageWallpaperCore(card, loadProfile: true);
        CurrentPageIndex = 2; // Placement Studio
    }

    [RelayCommand]
    private void ExitVideoStaging()
    {
        IsVideoWallpaperStaged = false;
        _stagedLiveVideoPath = null;
        if (ActiveAppliedWallpaper != null)
            StageWallpaperCore(ActiveAppliedWallpaper, loadProfile: true);
    }

    private void StageWallpaperCore(WallpaperCardViewModel card, bool loadProfile)
    {
        StagedWallpaper = card;
        StagedPreviewSource = card.BestPreviewSource;

        if (loadProfile)
        {
            string key = WallpaperLibraryService.BuildProfileKey(card.Id, GetCurrentTopologyKey());
            var saved = _db.LoadPlacementProfile(key);
            if (saved != null)
            {
                ApplyPlacementSettingsToViewModel(saved);
                return;
            }
        }

        RecomputePlan();
    }

    public void ApplyPlacementSettingsToViewModel(PlacementSettings s)
    {
        _suppressPlanRecompute = true;
        PlacementMode = s.Mode;
        CoordinateSpace = s.CoordinateSpace;
        PhysicalAlignment = s.PhysicalAlignment;
        EnableBezelCompensation = s.EnableBezelCompensation;
        Zoom = s.Zoom;
        PanX = s.PanX;
        PanY = s.PanY;
        FocalX = s.FocalX;
        FocalY = s.FocalY;
        CropLeft = s.CropLeft;
        CropTop = s.CropTop;
        CropRight = s.CropRight;
        CropBottom = s.CropBottom;
        FlipHorizontal = s.FlipHorizontal;
        FlipVertical = s.FlipVertical;
        BackgroundColorHex = s.BackgroundColorHex;
        _individualAssignments.Clear();
        foreach (var kv in s.PerMonitorAssignments)
            _individualAssignments[kv.Key] = kv.Value.Clone();
        _suppressPlanRecompute = false;
        RecomputePlan();
    }

    public PlacementSettings BuildPlacementSettings()
    {
        var ps = new PlacementSettings
        {
            Mode = PlacementMode,
            CoordinateSpace = CoordinateSpace,
            PhysicalAlignment = PhysicalAlignment,
            EnableBezelCompensation = EnableBezelCompensation,
            Zoom = Zoom,
            PanX = PanX,
            PanY = PanY,
            FocalX = FocalX,
            FocalY = FocalY,
            CropLeft = CropLeft,
            CropTop = CropTop,
            CropRight = CropRight,
            CropBottom = CropBottom,
            FlipHorizontal = FlipHorizontal,
            FlipVertical = FlipVertical,
            BackgroundColorHex = NormalizedBackgroundBrushHex
        };

        foreach (var kv in _individualAssignments)
            ps.PerMonitorAssignments[kv.Key] = kv.Value.Clone();

        return ps;
    }

    public void RecomputePlan()
    {
        if (_suppressPlanRecompute || Monitors.Count == 0)
            return;

        var monitorModels = Monitors.Select(m => m.Model).ToList();
        var placement = BuildPlacementSettings();
        IsIndividualMode = PlacementMode == WallpaperPlacementMode.Individual;

        int srcW = StagedWallpaper?.Model.Width > 0 ? StagedWallpaper.Model.Width : 7680;
        int srcH = StagedWallpaper?.Model.Height > 0 ? StagedWallpaper.Model.Height : 2160;
        string srcPath = StagedWallpaper?.BestPreviewSource ?? string.Empty;

        var perMonDims = new Dictionary<string, (int, int)>(StringComparer.OrdinalIgnoreCase);
        if (PlacementMode == WallpaperPlacementMode.Individual)
        {
            foreach (var assign in _individualAssignments.Values)
            {
                if (!string.IsNullOrWhiteSpace(assign.ImagePath) && File.Exists(assign.ImagePath))
                {
                    try { perMonDims[assign.ImagePath] = SkiaWallpaperCompositor.ReadImageDimensions(assign.ImagePath); }
                    catch { }
                }
            }
        }

        try
        {
            var plan = _geometryEngine.ComputePlan(monitorModels, srcW, srcH, srcPath, placement, perMonDims);
            CurrentPlan = plan;
            ResolutionAdvisory = plan.ResolutionAdvisory;
        }
        catch (Exception ex)
        {
            SetStatus($"Geometry error: {ex.Message}", isError: true);
        }
    }

    public void UpdatePanByDelta(double deltaNormX, double deltaNormY)
    {
        PanX = Math.Clamp(PanX + deltaNormX, -1.5, 1.5);
        PanY = Math.Clamp(PanY + deltaNormY, -1.5, 1.5);
    }

    public void UpdateZoomByDelta(double delta)
    {
        Zoom = Math.Clamp(Zoom + delta, 0.25, 4.0);
    }

    public void UpdateTransform(double zoom, double panX, double panY)
    {
        _suppressPlanRecompute = true;
        Zoom = Math.Clamp(zoom, 0.25, 4.0);
        PanX = Math.Clamp(panX, -1.5, 1.5);
        PanY = Math.Clamp(panY, -1.5, 1.5);
        _suppressPlanRecompute = false;
        RecomputePlan();
    }

    public void ScheduleStudioWallpaperApply(int delayMs = 100)
    {
        if (!AutoApplyStudioChanges || StagedWallpaper == null)
            return;
        if (!StagedWallpaper.Model.IsDownloaded && StagedWallpaper.Model.RequiresAuthentication)
            return;

        _studioApplyDebounceCts?.Cancel();
        _studioApplyDebounceCts?.Dispose();
        var cts = new CancellationTokenSource();
        _studioApplyDebounceCts = cts;

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(Math.Max(20, delayMs), cts.Token);
                if (cts.IsCancellationRequested) return;
                await System.Windows.Application.Current.Dispatcher.InvokeAsync(async () =>
                {
                    if (cts.IsCancellationRequested || IsApplying || StagedWallpaper == null)
                        return;
                    await ApplyWallpaperAsync();
                });
            }
            catch (OperationCanceledException) { }
        });
    }

    [RelayCommand]
    public void ResetPlacement()
    {
        _suppressPlanRecompute = true;
        Zoom = 1.0;
        PanX = 0.0;
        PanY = 0.0;
        FocalX = 0.5;
        FocalY = 0.5;
        CropLeft = 0.0;
        CropTop = 0.0;
        CropRight = 0.0;
        CropBottom = 0.0;
        FlipHorizontal = false;
        FlipVertical = false;
        _suppressPlanRecompute = false;
        RecomputePlan();
        SetStatus("Positioning reset to automatic fit.", isError: false);
    }

    [RelayCommand]
    public void ToggleFlipHorizontal() => FlipHorizontal = !FlipHorizontal;

    [RelayCommand]
    public void ToggleFlipVertical() => FlipVertical = !FlipVertical;

    [RelayCommand]
    public void SavePlacementPreferences()
    {
        var placement = BuildPlacementSettings();
        Settings.DefaultPlacementMode = placement.Mode;
        Settings.DefaultCoordinateSpace = placement.CoordinateSpace;
        Settings.DefaultPhysicalAlignment = placement.PhysicalAlignment;
        Settings.DefaultBezelCompensation = placement.EnableBezelCompensation;
        _db.SaveSettings(Settings);

        if (StagedWallpaper != null)
        {
            string key = WallpaperLibraryService.BuildProfileKey(StagedWallpaper.Id, GetCurrentTopologyKey());
            _db.SavePlacementProfile(key, placement);
        }

        // Export portable backup containing both Placement settings and Widgets
        var backup = new DuoWallLayoutPresetBackup
        {
            PresetName = StagedWallpaper?.Title ?? "DuoWall Layout Backup",
            WallpaperFilePath = StagedWallpaper?.Model.LocalOriginalPath ?? Settings.LastAppliedImagePath,
            LiveVideoPath = IsVideoWallpaperStaged ? _stagedLiveVideoPath : Settings.ActiveLiveVideoPath,
            LiveVideoStillPath = IsVideoWallpaperStaged ? StagedWallpaper?.Model.LocalOriginalPath : Settings.ActiveLiveVideoStillPath,
            ActiveGlassPresetId = Settings.ActiveGlassPresetId,
            WidgetAccentColorHex = WidgetStudio.WidgetAccentColorHex,
            AutoWidgetAccentFromWallpaper = WidgetStudio.AutoAccentFromWallpaper,
            Placement = placement.Clone(),
            Widgets = WidgetStudio.Widgets.Select(w => w.Clone()).ToList()
        };

        var dlg = new SaveFileDialog
        {
            Title = "Save Placement & Widgets Preset Backup",
            Filter = "DuoWall Preset Backup (*.duowall-preset;*.json)|*.duowall-preset;*.json",
            DefaultExt = ".duowall-preset",
            FileName = $"{YouTubeWallpaperService.SanitizeName(backup.PresetName)}.duowall-preset"
        };

        if (dlg.ShowDialog() == true)
        {
            string json = JsonSerializer.Serialize(backup, new JsonSerializerOptions
            {
                WriteIndented = true,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            });
            File.WriteAllText(dlg.FileName, json);
            SetStatus($"Saved preferences & exported preset backup ({backup.Widgets.Count} widget(s)) to '{Path.GetFileName(dlg.FileName)}'.", isError: false);
        }
        else
        {
            SetStatus("Placement preferences saved for this wallpaper & monitor layout.", isError: false);
        }
    }

    [RelayCommand]
    public async Task ImportLayoutPresetAsync()
    {
        var dlg = new OpenFileDialog
        {
            Title = "Import DuoWall Placement & Widgets Preset Backup",
            Filter = "DuoWall Preset Backup (*.duowall-preset;*.json)|*.duowall-preset;*.json"
        };
        if (dlg.ShowDialog() != true) return;

        try
        {
            string json = await File.ReadAllTextAsync(dlg.FileName);
            var backup = JsonSerializer.Deserialize<DuoWallLayoutPresetBackup>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });
            if (backup == null)
            {
                SetStatus("Selected preset backup file is empty or invalid.", isError: true);
                return;
            }

            if (!string.IsNullOrWhiteSpace(backup.LiveVideoPath) && File.Exists(backup.LiveVideoPath))
            {
                StageVideoInPlacementStudio(backup.LiveVideoPath, backup.LiveVideoStillPath);
            }
            else if (!string.IsNullOrWhiteSpace(backup.WallpaperFilePath) && File.Exists(backup.WallpaperFilePath) && !WallpaperCardViewModel.IsVideoFile(backup.WallpaperFilePath))
            {
                var imported = await _libraryService.ImportLocalImageAsync(backup.WallpaperFilePath, DownloadDirectory);
                var card = new WallpaperCardViewModel(imported);
                await card.EnsureThumbnailLoadedAsync(_galleryProvider, Settings.ThumbnailCacheDirectory);
                StageWallpaper(card, loadProfile: false);
            }

            ApplyPlacementSettingsToViewModel(backup.Placement);
            WidgetStudio.RestoreLayoutPresetBackup(backup);
            SetStatus($"Imported preset '{backup.PresetName}' with {backup.Widgets.Count} widget(s). Click Apply Wallpaper to push to desktop.", isError: false);
        }
        catch (Exception ex)
        {
            SetStatus($"Failed to import preset backup: {ex.Message}", isError: true);
        }
    }

    [RelayCommand]
    public void ApplyPhysicalPreset(string alignmentName)
    {
        if (Enum.TryParse<PhysicalVerticalAlignment>(alignmentName, ignoreCase: true, out var align))
        {
            _suppressPlanRecompute = true;
            foreach (var m in Monitors)
            {
                m.Model.UserOffsetMmY = 0;
                m.SyncFromModel();
            }
            PhysicalAlignment = align;
            CoordinateSpace = GeometryCoordinateSpace.PhysicalDimensions;
            _suppressPlanRecompute = false;
            OnMonitorCalibrationEdited();
            SetStatus($"Applied physical desk alignment: {align}", isError: false);
        }
    }

    [RelayCommand]
    public async Task ApplyWallpaperAsync()
    {
        if (StagedWallpaper == null)
        {
            SetStatus("Please select a wallpaper from the Gallery or My Wallpapers first.", isError: true);
            return;
        }

        if (IsSimulatedTopology)
        {
            SelectedSimulationPreset = "Live Hardware Displays";
            RefreshHardwareMonitors();
        }

        if (Monitors.Count == 0)
        {
            SetStatus("No active Windows displays detected.", isError: true);
            return;
        }

        IsApplying = true;
        ApplyProgress = 0.05;
        ApplyProgressMessage = "Preparing wallpaper...";

        try
        {
            var card = StagedWallpaper;
            var item = card.Model;

            // Live Video Wallpaper framing branch from Placement Studio
            if (IsVideoWallpaperStaged && !string.IsNullOrWhiteSpace(_stagedLiveVideoPath) && File.Exists(_stagedLiveVideoPath))
            {
                ApplyProgress = 0.45;
                ApplyProgressMessage = "Mapping live video geometry across connected displays...";
                var vidMonitors = Monitors.Select(m => m.Model).ToList();
                var vidPlacement = BuildPlacementSettings();
                string stillPath = item.LocalOriginalPath ?? string.Empty;
                int vw = item.Width > 0 ? item.Width : 3840;
                int vh = item.Height > 0 ? item.Height : 2160;
                var vidPlan = _geometryEngine.ComputePlan(vidMonitors, vw, vh, stillPath, vidPlacement);
                CurrentPlan = vidPlan;
                ResolutionAdvisory = vidPlan.ResolutionAdvisory;

                string vidProfileKey = WallpaperLibraryService.BuildProfileKey(item.Id, GetCurrentTopologyKey());
                _db.SavePlacementProfile(vidProfileKey, vidPlacement);

                Settings.ActiveLiveVideoPath = _stagedLiveVideoPath;
                Settings.ActiveLiveVideoStillPath = stillPath;
                Settings.ActiveLiveVideoPlacementKey = vidProfileKey;
                _db.SaveSettings(Settings);

                ApplyProgress = 0.90;
                ApplyProgressMessage = "Applying framed live video wallpaper...";
                _overlayCoordinator.ApplyLiveVideoWallpaper(_stagedLiveVideoPath, vidPlan, vidPlacement.BackgroundColorHex);
                LiveWallpaper.RefreshActiveVideoBanner();
                WidgetStudio.RefreshWallpaperPreviewBackdrop(stillPath ?? Settings.LastAppliedImagePath);
                if (!string.IsNullOrWhiteSpace(stillPath) && File.Exists(stillPath))
                    WidgetStudio.SyncWallpaperAccentIfAuto(stillPath);

                ApplyProgress = 1.0;
                ApplyProgressMessage = "Live Video Applied!";
                SetStatus($"Applied Live Video '{item.Title}' with your Placement Studio crop, zoom, flip & gap fill across {vidMonitors.Count} monitor(s).", isError: false);
                return;
            }

            // Static wallpaper flow: clear active video overlay so static wallpaper is clearly visible
            if (!string.IsNullOrWhiteSpace(Settings.ActiveLiveVideoPath))
            {
                Settings.ActiveLiveVideoPath = null;
                _overlayCoordinator.ApplyLiveVideoWallpaper(null, null, null);
                LiveWallpaper.RefreshActiveVideoBanner();
            }

            // Step 1 & 2: Download highest suitable original if online, never overwriting original
            ApplyProgressMessage = item.IsDownloaded
                ? "Using local high-resolution original..."
                : "Downloading full-resolution wallpaper from UltraWideWallpapers.net...";

            var progProvider = GetActiveGalleryProvider();
            var progress = new Progress<double>(p => ApplyProgress = 0.1 + p * 0.5);
            var dl = await _libraryService.EnsureLocalOriginalAsync(item, DownloadDirectory, progress, overrideProvider: progProvider);

            if (!dl.Success || string.IsNullOrWhiteSpace(dl.LocalFilePath))
            {
                if (dl.RequiresAuthentication)
                {
                    SetStatus(dl.ErrorMessage ?? "Authentication required on UltraWideWallpapers.net.", isError: true);
                    if (!string.IsNullOrWhiteSpace(dl.AuthActionUrl))
                        OpenUrlInBrowser(dl.AuthActionUrl);
                }
                else
                {
                    SetStatus(dl.ErrorMessage ?? "Failed to download wallpaper.", isError: true);
                }
                return;
            }

            card.RefreshFromModel();
            StagedPreviewSource = dl.LocalFilePath;

            // Step 3: Compute accurate geometry plan against real connected monitors & actual dimensions
            ApplyProgress = 0.65;
            ApplyProgressMessage = "Calculating multi-monitor geometry & crops...";

            var realMonitors = Monitors.Select(m => m.Model).ToList();
            var placement = BuildPlacementSettings();
            var perMonDims = new Dictionary<string, (int, int)>(StringComparer.OrdinalIgnoreCase);

            if (placement.Mode == WallpaperPlacementMode.Individual)
            {
                foreach (var assign in placement.PerMonitorAssignments.Values)
                {
                    if (!string.IsNullOrWhiteSpace(assign.ImagePath) && File.Exists(assign.ImagePath))
                        perMonDims[assign.ImagePath] = SkiaWallpaperCompositor.ReadImageDimensions(assign.ImagePath);
                }
            }

            var plan = _geometryEngine.ComputePlan(
                realMonitors,
                dl.Width,
                dl.Height,
                dl.LocalFilePath,
                placement,
                perMonDims);
            CurrentPlan = plan;
            ResolutionAdvisory = plan.ResolutionAdvisory;

            // Step 4: Render per-monitor sliced images into managed Generated directory
            ApplyProgress = 0.78;
            ApplyProgressMessage = $"Rendering {plan.Slices.Count} display-exact wallpapers...";
            var renderedFiles = await _compositor.RenderMonitorWallpapersAsync(
                plan,
                Settings.GeneratedDirectory,
                placement.BackgroundColorHex,
                OutputFormat);

            // Step 5: Capture backup before overriding Windows desktop wallpapers
            if (_db.LoadWindowsBackup() == null)
            {
                try
                {
                    var backup = _wallpaperApplier.CaptureCurrentWallpaperState();
                    _db.SaveWindowsBackup(backup);
                    HasWindowsBackup = true;
                }
                catch { /* non-fatal */ }
            }

            // Step 6: Apply per monitor via Windows IDesktopWallpaper COM API
            ApplyProgress = 0.92;
            ApplyProgressMessage = "Applying per-monitor wallpapers via Windows IDesktopWallpaper...";
            _wallpaperApplier.ApplyMonitorWallpapers(realMonitors, renderedFiles, placement.BackgroundColorHex);

            // Step 7: Persist state & update UI
            item.LastAppliedUtc = DateTime.UtcNow;
            _db.UpsertWallpaper(item);
            string profileKey = WallpaperLibraryService.BuildProfileKey(item.Id, GetCurrentTopologyKey());
            _db.SavePlacementProfile(profileKey, placement);

            Settings.LastAppliedWallpaperId = item.Id;
            Settings.LastAppliedImagePath = dl.LocalFilePath;
            Settings.LastAppliedTopologySignature = _liveTopologySignature;
            _db.SaveSettings(Settings);

            ActiveAppliedWallpaper = card;
            RefreshLocalLibrary();
            RefreshCacheMetrics();

            _overlayCoordinator.RefreshWallpaperCache(Monitors.Select(m => m.Model).ToList());
            WidgetStudio.RefreshWallpaperPreviewBackdrop(dl.LocalFilePath);
            WidgetStudio.SyncWallpaperAccentIfAuto(dl.LocalFilePath);
            Profiles.HandleWallpaperApplied(dl.LocalFilePath);
            _overlayCoordinator.UpdateGlobalGlassLive(WidgetStudio.LiveGlassStyle, Profiles.GetActiveProfile()?.WidgetBrightness ?? 1.0);

            ApplyProgress = 1.0;
            ApplyProgressMessage = "Applied!";
            SetStatus($"Applied '{item.Title}' ({dl.Width}×{dl.Height}) across {renderedFiles.Count} monitor(s) cleanly.", isError: false);
        }
        catch (Exception ex)
        {
            SetStatus($"Failed to apply wallpaper: {ex.Message}", isError: true);
        }
        finally
        {
            IsApplying = false;
        }
    }

    [RelayCommand]
    public void RestorePreviousWallpaper()
    {
        var backup = _db.LoadWindowsBackup();
        if (backup == null)
        {
            SetStatus("No previous Windows wallpaper backup has been captured yet.", isError: true);
            return;
        }

        try
        {
            _wallpaperApplier.RestoreBackup(backup);
            SetStatus("Restored previous Windows desktop wallpaper configuration.", isError: false);
        }
        catch (Exception ex)
        {
            SetStatus($"Unable to restore previous wallpaper: {ex.Message}", isError: true);
        }
    }

    [RelayCommand]
    public async Task AssignIndividualMonitorWallpaperAsync(string? monitorId)
    {
        string? targetId = monitorId ?? SelectedMonitorId;
        if (string.IsNullOrWhiteSpace(targetId)) return;

        var dlg = new OpenFileDialog
        {
            Title = "Choose Wallpaper for Selected Monitor",
            Filter = "Image Files (*.jpg;*.jpeg;*.png;*.webp;*.bmp)|*.jpg;*.jpeg;*.png;*.webp;*.bmp"
        };
        if (dlg.ShowDialog() != true)
            return;

        try
        {
            var imported = await _libraryService.ImportLocalImageAsync(dlg.FileName, DownloadDirectory);
            _individualAssignments[targetId] = new PerMonitorAssignment
            {
                MonitorId = targetId,
                ImagePath = imported.LocalOriginalPath!,
                WallpaperId = imported.Id,
                SubMode = WallpaperPlacementMode.Fill
            };
            PlacementMode = WallpaperPlacementMode.Individual;
            RefreshLocalLibrary();
            RecomputePlan();
            SetStatus($"Assigned '{imported.Title}' to selected monitor.", isError: false);
        }
        catch (Exception ex)
        {
            SetStatus($"Could not assign monitor wallpaper: {ex.Message}", isError: true);
        }
    }

    private IWallpaperSourceProvider GetActiveGalleryProvider() =>
        string.Equals(SelectedGallerySource, "Custom Website", StringComparison.OrdinalIgnoreCase)
            ? _customSiteProvider
            : _galleryProvider;

    [RelayCommand]
    public async Task LinkCustomWebsiteAsync()
    {
        string normalized = CustomWebsiteGalleryProvider.NormalizeUrl(CustomGallerySiteUrl);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            SetStatus("Please enter a valid wallpaper website URL starting with https:// or http://.", isError: true);
            return;
        }

        CustomGallerySiteUrl = normalized;
        _customSiteProvider.CustomSiteUrl = normalized;
        Settings.CustomGallerySiteUrl = normalized;
        Settings.PreferredGallerySource = "CustomWebsite";
        _db.SaveSettings(Settings);

        _suppressGallerySourceSync = true;
        SelectedGallerySource = "Custom Website";
        _suppressGallerySourceSync = false;

        await LoadGalleryAsync(reset: true);
    }

    partial void OnSelectedGallerySourceChanged(string value)
    {
        if (_suppressGallerySourceSync) return;
        bool isCustom = string.Equals(value, "Custom Website", StringComparison.OrdinalIgnoreCase);
        Settings.PreferredGallerySource = isCustom ? "CustomWebsite" : "UltraWideWallpapers";
        _db.SaveSettings(Settings);
        _ = LoadGalleryAsync(reset: true);
    }

    // ==================== GALLERY COMMANDS ====================

    [RelayCommand]
    public async Task LoadGalleryAsync(bool reset = true)
    {
        _galleryCts?.Cancel();
        _galleryCts = new CancellationTokenSource();
        var ct = _galleryCts.Token;

        if (reset)
        {
            _galleryOffset = 0;
            GalleryItems.Clear();
            HasMoreGalleryItems = true;
        }

        if (!HasMoreGalleryItems && !reset)
            return;

        var activeProvider = GetActiveGalleryProvider();
        IsLoadingGallery = true;
        GalleryStatusText = $"Loading wallpapers from {activeProvider.ProviderName}...";

        try
        {
            var favIds = _db.GetFavoriteIds();
            var localMap = _db.GetAllLibraryWallpapers()
                .ToDictionary(w => w.Id, StringComparer.OrdinalIgnoreCase);

            if (string.Equals(SelectedGalleryCategory, "Favorites", StringComparison.OrdinalIgnoreCase))
            {
                var favItems = _db.GetAllLibraryWallpapers()
                    .Where(w => w.IsFavorite)
                    .Where(w => string.IsNullOrWhiteSpace(GallerySearchText) ||
                                w.Title.Contains(GallerySearchText, StringComparison.OrdinalIgnoreCase) ||
                                w.TagsSummary.Contains(GallerySearchText, StringComparison.OrdinalIgnoreCase))
                    .ToList();

                foreach (var w in favItems)
                {
                    var card = new WallpaperCardViewModel(w);
                    GalleryItems.Add(card);
                    _ = card.EnsureThumbnailLoadedAsync(_galleryProvider, Settings.ThumbnailCacheDirectory, ct);
                }

                HasMoreGalleryItems = false;
                GalleryStatusText = $"{GalleryItems.Count} favorite wallpaper(s)";
                return;
            }

            var query = new GalleryQuery
            {
                SearchText = GallerySearchText,
                CategoryOrResolutionFilter = SelectedGalleryCategory,
                SortMode = SelectedSortMode,
                Offset = _galleryOffset,
                Limit = 24
            };

            var result = await activeProvider.QueryGalleryAsync(query, ct);
            if (ct.IsCancellationRequested) return;

            foreach (var item in result.Items)
            {
                if (favIds.Contains(item.Id))
                    item.IsFavorite = true;
                if (localMap.TryGetValue(item.Id, out var existing) && existing.IsDownloaded)
                {
                    item.LocalOriginalPath = existing.LocalOriginalPath;
                    item.Width = existing.Width > 0 ? existing.Width : item.Width;
                    item.Height = existing.Height > 0 ? existing.Height : item.Height;
                    item.FileSizeBytes = existing.FileSizeBytes;
                }

                var card = new WallpaperCardViewModel(item);
                GalleryItems.Add(card);
                _ = card.EnsureThumbnailLoadedAsync(activeProvider, Settings.ThumbnailCacheDirectory, ct);
            }

            _galleryOffset += result.Items.Count;
            HasMoreGalleryItems = result.HasMore;
            GalleryStatusText = !string.IsNullOrWhiteSpace(result.StatusMessage) && result.Items.Count == 0
                ? result.StatusMessage!
                : result.TotalKnownCount > 0
                    ? $"Showing {GalleryItems.Count} of {result.TotalKnownCount:N0} wallpapers · {activeProvider.ProviderName}"
                    : $"Showing {GalleryItems.Count} wallpapers · {activeProvider.ProviderName}";
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            GalleryStatusText = $"Could not load gallery: {ex.Message}";
            SetStatus($"Gallery load error: {ex.Message}", isError: true);
        }
        finally
        {
            IsLoadingGallery = false;
        }
    }

    [RelayCommand]
    public void SelectCategoryFilter(string category)
    {
        SelectedGalleryCategory = category;
    }

    [RelayCommand]
    public void SetBackgroundColor(string hex)
    {
        if (!string.IsNullOrWhiteSpace(hex))
            BackgroundColorHex = hex;
    }

    [RelayCommand]
    public async Task SampleEdgeBackgroundColorAsync()
    {
        if (StagedWallpaper == null) return;
        try
        {
            string? localPath = StagedWallpaper.Model.LocalOriginalPath;
            if (string.IsNullOrWhiteSpace(localPath) || !File.Exists(localPath))
            {
                localPath = StagedWallpaper.Model.LocalThumbnailPath;
                if (string.IsNullOrWhiteSpace(localPath) || !File.Exists(localPath))
                    localPath = await _galleryProvider.GetOrCacheThumbnailAsync(StagedWallpaper.Model, Settings.ThumbnailCacheDirectory);
            }

            if (!string.IsNullOrWhiteSpace(localPath) && File.Exists(localPath))
            {
                string hex = await Task.Run(() => SkiaWallpaperCompositor.SampleAverageEdgeHex(localPath));
                BackgroundColorHex = hex;
                SetStatus($"Matched plain background fill to wallpaper edge color ({hex}).", isError: false);
            }
        }
        catch (Exception ex)
        {
            SetStatus($"Could not sample wallpaper edge color: {ex.Message}", isError: true);
        }
    }

    [RelayCommand]
    public async Task OpenDetailAsync(WallpaperCardViewModel? card)
    {
        if (card == null) return;
        SelectedDetailWallpaper = card;
        IsDetailOpen = true;
        DetailShowMultiMonitorPreview = false;

        try
        {
            if (string.Equals(card.SourceProvider, "UltraWideWallpapers.net", StringComparison.OrdinalIgnoreCase))
                await _galleryProvider.EnrichDetailsAsync(card.Model);

            card.RefreshFromModel();
            await card.EnsurePreviewLoadedAsync(Settings.ThumbnailCacheDirectory);
            UpdateDetailPreviewPlan();
        }
        catch { /* best effort detail enrichment */ }
    }

    [RelayCommand]
    public void CloseDetail()
    {
        IsDetailOpen = false;
    }

    [RelayCommand]
    public async Task DownloadOnlyAsync(WallpaperCardViewModel? card)
    {
        if (card == null) return;
        card.IsDownloading = true;
        try
        {
            var prog = new Progress<double>(p => card.DownloadProgress = p);
            var activeProvider = card.SourceProvider.StartsWith("Custom Site", StringComparison.OrdinalIgnoreCase)
                ? _customSiteProvider
                : GetActiveGalleryProvider();
            var res = await _libraryService.EnsureLocalOriginalAsync(card.Model, DownloadDirectory, prog, overrideProvider: activeProvider);
            if (res.Success)
            {
                card.RefreshFromModel();
                if (StagedWallpaper != null && string.Equals(StagedWallpaper.Id, card.Id, StringComparison.OrdinalIgnoreCase))
                    StagedPreviewSource = card.BestPreviewSource;
                RefreshLocalLibrary();
                RefreshCacheMetrics();
                SetStatus($"Downloaded '{card.Title}' ({res.Width}×{res.Height}) to {DownloadDirectory}.", isError: false);
            }
            else if (res.RequiresAuthentication)
            {
                SetStatus(res.ErrorMessage ?? "Login required on UltraWideWallpapers.net.", isError: true);
                if (!string.IsNullOrWhiteSpace(res.AuthActionUrl))
                    OpenUrlInBrowser(res.AuthActionUrl);
            }
            else
            {
                SetStatus(res.ErrorMessage ?? "Download failed.", isError: true);
            }
        }
        finally
        {
            card.IsDownloading = false;
        }
    }

    [RelayCommand]
    public void SendToStudio(WallpaperCardViewModel? card)
    {
        if (card == null) return;
        StageWallpaper(card, loadProfile: true);
        _ = card.EnsurePreviewLoadedAsync(Settings.ThumbnailCacheDirectory);
        IsDetailOpen = false;
        CurrentPageIndex = 2; // Placement Studio tab
    }

    [RelayCommand]
    public async Task QuickApplyWallpaperAsync(WallpaperCardViewModel? card)
    {
        if (card == null) return;
        StageWallpaper(card, loadProfile: true);
        IsDetailOpen = false;
        await ApplyWallpaperAsync();
    }

    [RelayCommand]
    public void ToggleFavorite(WallpaperCardViewModel? card)
    {
        if (card == null) return;
        _db.SetFavorite(card.Model, !card.Model.IsFavorite);
        card.RefreshFromModel();

        // Sync matching item in Gallery / Library lists
        foreach (var c in GalleryItems.Concat(LibraryItems).Concat(RecentWallpapers))
        {
            if (string.Equals(c.Id, card.Id, StringComparison.OrdinalIgnoreCase) && !ReferenceEquals(c, card))
            {
                c.Model.IsFavorite = card.Model.IsFavorite;
                c.RefreshFromModel();
            }
        }
        RefreshLocalLibrary();
    }

    [RelayCommand]
    public void OpenSourcePage(WallpaperCardViewModel? card)
    {
        if (card == null || string.IsNullOrWhiteSpace(card.SourcePageUrl)) return;
        OpenUrlInBrowser(card.SourcePageUrl);
    }

    [RelayCommand]
    public void OpenLocalFileInExplorer(WallpaperCardViewModel? card)
    {
        if (card?.Model.LocalOriginalPath == null || !File.Exists(card.Model.LocalOriginalPath)) return;
        Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{card.Model.LocalOriginalPath}\"") { UseShellExecute = true });
    }

    // ==================== LIBRARY COMMANDS ====================

    [RelayCommand]
    public void RefreshLocalLibrary()
    {
        var all = _db.GetAllLibraryWallpapers();

        RecentWallpapers.Clear();
        foreach (var w in all.Where(x => x.LastAppliedUtc.HasValue && x.IsDownloaded)
                             .OrderByDescending(x => x.LastAppliedUtc)
                             .Take(8))
        {
            var card = new WallpaperCardViewModel(w);
            RecentWallpapers.Add(card);
            _ = card.EnsureThumbnailLoadedAsync(_galleryProvider, Settings.ThumbnailCacheDirectory);
        }

        IEnumerable<WallpaperItem> filtered = SelectedLibraryTab switch
        {
            "Favorites" => all.Where(w => w.IsFavorite),
            "Recently Used" => all.Where(w => w.LastAppliedUtc.HasValue).OrderByDescending(w => w.LastAppliedUtc),
            _ => all.Where(w => w.IsDownloaded)
        };

        if (!string.IsNullOrWhiteSpace(LibrarySearchText))
        {
            filtered = filtered.Where(w =>
                w.Title.Contains(LibrarySearchText, StringComparison.OrdinalIgnoreCase) ||
                w.FileName.Contains(LibrarySearchText, StringComparison.OrdinalIgnoreCase) ||
                w.TagsSummary.Contains(LibrarySearchText, StringComparison.OrdinalIgnoreCase));
        }

        LibraryItems.Clear();
        foreach (var w in filtered)
        {
            var card = new WallpaperCardViewModel(w);
            LibraryItems.Add(card);
            _ = card.EnsureThumbnailLoadedAsync(_galleryProvider, Settings.ThumbnailCacheDirectory);
        }
    }

    [RelayCommand]
    public async Task ImportLocalImageAsync()
    {
        var dlg = new OpenFileDialog
        {
            Title = "Upload Own Photo / Local Wallpaper",
            Filter = "Image Files (*.jpg;*.jpeg;*.png;*.webp;*.bmp)|*.jpg;*.jpeg;*.png;*.webp;*.bmp"
        };
        if (dlg.ShowDialog() != true) return;

        try
        {
            var (imgW, imgH) = SkiaWallpaperCompositor.ReadImageDimensions(dlg.FileName);
            var activeMonitors = Monitors.Where(m => m.Model.IsConnected && m.IsIncluded).Select(m => m.Model).ToList();
            if (activeMonitors.Count == 0) activeMonitors = Monitors.Select(m => m.Model).ToList();

            bool lowResConfirmApply = false;
            if (activeMonitors.Count > 0 && imgW > 0 && imgH > 0)
            {
                int minX = activeMonitors.Min(m => m.Bounds.X);
                int minY = activeMonitors.Min(m => m.Bounds.Y);
                int maxX = activeMonitors.Max(m => m.Bounds.Right);
                int maxY = activeMonitors.Max(m => m.Bounds.Bottom);
                int requiredW = maxX - minX;
                int requiredH = maxY - minY;

                if (imgW < requiredW * 0.90 || imgH < requiredH * 0.90)
                {
                    var answer = MessageBox.Show(
                        $"This photo ({imgW} × {imgH} px) is smaller than your combined multi-monitor resolution ({requiredW} × {requiredH} px).\n\n" +
                        "Stretching or spanning a smaller photo across multiple monitors may reduce sharpness.\n\n" +
                        "Do you still want to upload and apply this low-resolution image?",
                        "Low-Resolution Image Warning · DuoWall by SRMD",
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Warning);

                    if (answer != MessageBoxResult.Yes)
                    {
                        SetStatus($"Canceled uploading low-resolution image ({imgW}×{imgH}).", isError: false);
                        return;
                    }
                    lowResConfirmApply = true;
                }
            }

            var imported = await _libraryService.ImportLocalImageAsync(dlg.FileName, DownloadDirectory);
            var card = new WallpaperCardViewModel(imported);
            await card.EnsureThumbnailLoadedAsync(_galleryProvider, Settings.ThumbnailCacheDirectory);
            RefreshLocalLibrary();
            StageWallpaper(card, loadProfile: false);
            CurrentPageIndex = 2; // Placement Studio

            if (lowResConfirmApply)
            {
                await ApplyWallpaperAsync();
            }
            else
            {
                SetStatus($"Uploaded '{imported.Title}' ({imported.Width}×{imported.Height}) and opened in Placement Studio.", isError: false);
            }
        }
        catch (Exception ex)
        {
            SetStatus($"Failed importing local image: {ex.Message}", isError: true);
        }
    }

    [RelayCommand]
    public void DeleteFromLibrary(WallpaperCardViewModel? card)
    {
        if (card == null) return;
        _db.DeleteWallpaper(card.Id, deleteLocalFile: true);
        RefreshLocalLibrary();
        RefreshCacheMetrics();
        SetStatus($"Removed '{card.Title}' from local library.", isError: false);
    }

    // ==================== SETTINGS COMMANDS ====================

    [RelayCommand]
    public void BrowseDownloadDirectory()
    {
        var dlg = new OpenFolderDialog
        {
            Title = "Choose Wallpaper Download Folder",
            InitialDirectory = Directory.Exists(DownloadDirectory) ? DownloadDirectory : Environment.GetFolderPath(Environment.SpecialFolder.MyPictures)
        };
        if (dlg.ShowDialog() == true && !string.IsNullOrWhiteSpace(dlg.FolderName))
        {
            DownloadDirectory = dlg.FolderName;
            Directory.CreateDirectory(DownloadDirectory);
            Settings.DownloadDirectory = DownloadDirectory;
            _db.SaveSettings(Settings);
            SetStatus($"Download directory updated to {DownloadDirectory}.", isError: false);
        }
    }

    [RelayCommand]
    public void OpenDownloadDirectory()
    {
        Directory.CreateDirectory(DownloadDirectory);
        Process.Start(new ProcessStartInfo("explorer.exe", $"\"{DownloadDirectory}\"") { UseShellExecute = true });
    }

    [RelayCommand]
    public void ClearCaches()
    {
        ImageCacheHelper.ClearMemoryCache();
        WallpaperLibraryService.ClearDirectory(Settings.ThumbnailCacheDirectory);
        RefreshCacheMetrics();
        SetStatus("Cleared thumbnail disk and memory cache.", isError: false);
    }

    [RelayCommand]
    public async Task SyncWeatherNowAsync()
    {
        Settings.WeatherCity = string.IsNullOrWhiteSpace(WeatherCity) ? "Lahore" : WeatherCity.Trim();
        Settings.WeatherUseFahrenheit = WeatherUseFahrenheit;
        Settings.CustomWeatherApiKey = string.IsNullOrWhiteSpace(CustomWeatherApiKey) ? null : CustomWeatherApiKey.Trim();
        _db.SaveSettings(Settings);

        await _overlayCoordinator.RefreshWeatherAsync();
        if (_overlayCoordinator.LatestWeather is { } wx)
        {
            SetStatus($"Weather synced for {wx.CityDisplay}: {wx.Temperature:0.#}{wx.UnitSymbol} ({wx.ConditionText}) via {wx.ProviderName}.", isError: false);
        }
        else
        {
            SetStatus(_overlayCoordinator.WeatherStatus, isError: true);
        }
    }

    [RelayCommand]
    public void ShowAboutMe()
    {
        var aboutWindow = new AboutMeDialogWindow
        {
            Owner = Application.Current?.MainWindow
        };
        aboutWindow.ShowDialog();
    }

    [RelayCommand]
    public async Task CheckForUpdatesAsync()
    {
        Settings.GitHubRepositorySlug = string.IsNullOrWhiteSpace(GitHubRepositorySlug)
            ? GitHubReleaseService.DefaultRepoSlug
            : GitHubRepositorySlug.Trim();
        _db.SaveSettings(Settings);

        SetStatus("Checking official GitHub Releases for newer DuoWall versions...", isError: false);
        var result = await _releaseService.CheckLatestReleaseAsync("2.0.0", Settings.GitHubRepositorySlug);
        if (!string.IsNullOrWhiteSpace(result.ReleasePageUrl))
        {
            LatestReleasePageUrl = result.ReleasePageUrl;
        }
        SetStatus(result.Message, isError: !result.Success);

        if (result.UpdateAvailable)
        {
            var msg = $"A newer version of DuoWall is available: v{result.LatestVersion}!\n\nCurrent version: v2.0.0\n\nWould you like to open the GitHub Releases page to view notes and download?";
            if (MessageBox.Show(msg, "DuoWall - Update Available", MessageBoxButton.YesNo, MessageBoxImage.Information) == MessageBoxResult.Yes)
            {
                OpenReleasePage();
            }
        }
        else if (result.Success)
        {
            MessageBox.Show($"You are using the latest version of DuoWall (v2.0.0).\n\nYour installation is up to date!", "DuoWall - Up to Date", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        else
        {
            MessageBox.Show($"Unable to check for updates right now: {result.Message}\n\nYou can check releases directly at:\nhttps://github.com/{Settings.GitHubRepositorySlug}/releases", "DuoWall - Update Check", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    [RelayCommand]
    public void OpenReleasePage()
    {
        if (!string.IsNullOrWhiteSpace(LatestReleasePageUrl))
        {
            OpenUrlInBrowser(LatestReleasePageUrl);
        }
    }

    partial void OnEnableDesktopWidgetsChanged(bool value)
    {
        Settings.EnableDesktopWidgets = value;
        _db.SaveSettings(Settings);
        WidgetStudio.EnableDesktopWidgets = value;
        _overlayCoordinator.RebuildOverlays();
    }

    partial void OnWeatherCityChanged(string value)
    {
        Settings.WeatherCity = string.IsNullOrWhiteSpace(value) ? "Lahore" : value.Trim();
        _db.SaveSettings(Settings);
    }

    partial void OnWeatherUseFahrenheitChanged(bool value)
    {
        Settings.WeatherUseFahrenheit = value;
        _db.SaveSettings(Settings);
        _ = _overlayCoordinator.RefreshWeatherAsync();
    }

    partial void OnCustomWeatherApiKeyChanged(string value)
    {
        Settings.CustomWeatherApiKey = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        _db.SaveSettings(Settings);
    }

    partial void OnGitHubRepositorySlugChanged(string value)
    {
        Settings.GitHubRepositorySlug = string.IsNullOrWhiteSpace(value) ? GitHubReleaseService.DefaultRepoSlug : value.Trim();
        LatestReleasePageUrl = $"https://github.com/{Settings.GitHubRepositorySlug}/releases";
        _db.SaveSettings(Settings);
    }

    public void RefreshCacheMetrics()
    {
        long thumbBytes = WallpaperLibraryService.GetDirectorySizeBytes(Settings.ThumbnailCacheDirectory);
        long genBytes = WallpaperLibraryService.GetDirectorySizeBytes(Settings.GeneratedDirectory);
        ThumbnailCacheSizeText = $"{thumbBytes / (1024.0 * 1024.0):0.0} MB";
        GeneratedCacheSizeText = $"{genBytes / (1024.0 * 1024.0):0.0} MB";
    }

    private async Task OnDisplayTopologyChangedAsync()
    {
        var live = _displayDetector.DetectMonitors();
        string newSig = Win32DisplayDetector.ComputeTopologySignature(live);
        if (string.Equals(newSig, _liveTopologySignature, StringComparison.Ordinal))
            return;

        if (!IsSimulatedTopology)
            RefreshHardwareMonitors();

        if (AutoReapplyOnDisplayChange && ActiveAppliedWallpaper != null && ActiveAppliedWallpaper.IsDownloaded)
        {
            StagedWallpaper = ActiveAppliedWallpaper;
            await ApplyWallpaperAsync();
        }
    }

    private void UpdateDetailPreviewPlan()
    {
        if (SelectedDetailWallpaper == null || Monitors.Count == 0)
        {
            DetailPreviewPlan = null;
            return;
        }

        try
        {
            var m = SelectedDetailWallpaper.Model;
            int w = m.Width > 0 ? m.Width : 7680;
            int h = m.Height > 0 ? m.Height : 2160;
            DetailPreviewPlan = _geometryEngine.ComputePlan(
                Monitors.Select(x => x.Model).ToList(),
                w, h,
                SelectedDetailWallpaper.BestPreviewSource,
                BuildPlacementSettings());
        }
        catch { }
    }

    private string GetCurrentTopologyKey()
    {
        if (!IsSimulatedTopology && !string.IsNullOrWhiteSpace(_liveTopologySignature))
            return _liveTopologySignature;
        return Win32DisplayDetector.ComputeTopologySignature(Monitors.Select(m => m.Model).ToList());
    }

    private void SetStatus(string message, bool isError)
    {
        StatusBannerMessage = message;
        IsStatusError = isError;
        HasStatusBanner = !string.IsNullOrWhiteSpace(message);
    }

    partial void OnHasStatusBannerChanged(bool value)
    {
        if (!value)
            StatusBannerMessage = string.Empty;
    }

    partial void OnDetailShowMultiMonitorPreviewChanged(bool value)
    {
        if (value)
            UpdateDetailPreviewPlan();
    }

    private static void OpenUrlInBrowser(string url)
    {
        try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
        catch { }
    }

    // ==================== PROPERTY CHANGE HOOKS ====================

    partial void OnPlacementModeChanged(WallpaperPlacementMode value) => RecomputePlan();
    partial void OnCoordinateSpaceChanged(GeometryCoordinateSpace value) => RecomputePlan();
    partial void OnPhysicalAlignmentChanged(PhysicalVerticalAlignment value) => RecomputePlan();
    partial void OnEnableBezelCompensationChanged(bool value) => RecomputePlan();
    partial void OnZoomChanged(double value) => RecomputePlan();
    partial void OnPanXChanged(double value) => RecomputePlan();
    partial void OnPanYChanged(double value) => RecomputePlan();
    partial void OnFocalXChanged(double value) => RecomputePlan();
    partial void OnFocalYChanged(double value) => RecomputePlan();
    partial void OnCropLeftChanged(double value) => RecomputePlan();
    partial void OnCropTopChanged(double value) => RecomputePlan();
    partial void OnCropRightChanged(double value) => RecomputePlan();
    partial void OnCropBottomChanged(double value) => RecomputePlan();
    partial void OnFlipHorizontalChanged(bool value) => RecomputePlan();
    partial void OnFlipVerticalChanged(bool value) => RecomputePlan();

    partial void OnBackgroundColorHexChanged(string value)
    {
        if (!_suppressRgbSync && TryParseHexRgb(value, out byte r, out byte g, out byte b))
        {
            _suppressRgbSync = true;
            BgRed = r;
            BgGreen = g;
            BgBlue = b;
            NormalizedBackgroundBrushHex = $"#{r:X2}{g:X2}{b:X2}";
            _suppressRgbSync = false;
        }
        RecomputePlan();
    }

    partial void OnBgRedChanged(byte value) => SyncHexFromRgb();
    partial void OnBgGreenChanged(byte value) => SyncHexFromRgb();
    partial void OnBgBlueChanged(byte value) => SyncHexFromRgb();

    private void SyncHexFromRgb()
    {
        if (_suppressRgbSync) return;
        _suppressRgbSync = true;
        string hex = $"#{BgRed:X2}{BgGreen:X2}{BgBlue:X2}";
        NormalizedBackgroundBrushHex = hex;
        BackgroundColorHex = hex;
        _suppressRgbSync = false;
        RecomputePlan();
    }

    private static bool TryParseHexRgb(string? hex, out byte r, out byte g, out byte b)
    {
        r = g = b = 0;
        if (string.IsNullOrWhiteSpace(hex)) return false;
        string clean = hex.Trim().TrimStart('#');
        if (clean.Length != 6) return false;
        return byte.TryParse(clean[0..2], System.Globalization.NumberStyles.HexNumber, null, out r) &&
               byte.TryParse(clean[2..4], System.Globalization.NumberStyles.HexNumber, null, out g) &&
               byte.TryParse(clean[4..6], System.Globalization.NumberStyles.HexNumber, null, out b);
    }

    partial void OnShowMonitorLabelsChanged(bool value)
    {
        Settings.ShowMonitorLabelsInPreview = value;
        _db.SaveSettings(Settings);
    }

    partial void OnSelectedSimulationPresetChanged(string value) => RefreshHardwareMonitors();

    partial void OnSelectedGalleryCategoryChanged(string value) => _ = LoadGalleryAsync(reset: true);
    partial void OnSelectedSortModeChanged(GallerySortMode value) => _ = LoadGalleryAsync(reset: true);

    partial void OnGallerySearchTextChanged(string value)
    {
        _searchDebounceCts?.Cancel();
        _searchDebounceCts = new CancellationTokenSource();
        var ct = _searchDebounceCts.Token;
        _ = Task.Run(async () =>
        {
            await Task.Delay(420, ct);
            await Application.Current.Dispatcher.InvokeAsync(() => LoadGalleryAsync(reset: true));
        }, ct);
    }

    partial void OnSelectedLibraryTabChanged(string value) => RefreshLocalLibrary();
    partial void OnLibrarySearchTextChanged(string value) => RefreshLocalLibrary();

    partial void OnIsDarkThemeChanged(bool value)
    {
        Settings.Theme = value ? AppThemeMode.Dark : AppThemeMode.Light;
        _db.SaveSettings(Settings);
        ApplicationThemeManager.Apply(value ? ApplicationTheme.Dark : ApplicationTheme.Light);
    }

    partial void OnAutoApplyOnStartupChanged(bool value)
    {
        Settings.AutoApplyOnWindowsStartup = value;
        _db.SaveSettings(Settings);
        StartupAndWatcherService.SetStartupEnabled(value);
    }

    partial void OnAutoReapplyOnDisplayChangeChanged(bool value)
    {
        Settings.AutoReapplyOnDisplayChange = value;
        _db.SaveSettings(Settings);
    }

    partial void OnOutputFormatChanged(OutputImageFormat value)
    {
        Settings.OutputFormat = value;
        _db.SaveSettings(Settings);
    }

    partial void OnMaxThumbnailCacheMbChanged(int value)
    {
        Settings.MaxThumbnailCacheMb = Math.Clamp(value, 50, 5000);
        _db.SaveSettings(Settings);
        WallpaperLibraryService.EnforceCacheLimit(Settings.ThumbnailCacheDirectory, Settings.MaxThumbnailCacheMb);
        RefreshCacheMetrics();
    }

    // ==================== SIMULATED MONITOR TOPOLOGIES ====================

    private static IReadOnlyList<MonitorInfo> BuildSimulated1080Plus1440() => new[]
    {
        new MonitorInfo
        {
            Id = "SIM_LEFT_1080", DeviceName = @"\\.\DISPLAY1", FriendlyName = "Dell P2419H (Sim)", Manufacturer = "Dell",
            Bounds = new PixelRect(-1920, 180, 1920, 1080), WorkingArea = new PixelRect(-1920, 180, 1920, 1040),
            NativeWidth = 1920, NativeHeight = 1080, PhysicalWidthMm = 527, PhysicalHeightMm = 296,
            HasHardwarePhysicalDimensions = true, ScalePercent = 100, RefreshRateHz = 60, IsConnected = true, IsIncluded = true
        },
        new MonitorInfo
        {
            Id = "SIM_RIGHT_1440", DeviceName = @"\\.\DISPLAY2", FriendlyName = "LG 27GL850 (Sim)", Manufacturer = "LG",
            Bounds = new PixelRect(0, 0, 2560, 1440), WorkingArea = new PixelRect(0, 0, 2560, 1400),
            NativeWidth = 2560, NativeHeight = 1440, PhysicalWidthMm = 597, PhysicalHeightMm = 336,
            HasHardwarePhysicalDimensions = true, IsPrimary = true, ScalePercent = 100, RefreshRateHz = 144, IsConnected = true, IsIncluded = true
        }
    };

    private static IReadOnlyList<MonitorInfo> BuildSimulatedThreeMonitors() => new[]
    {
        new MonitorInfo
        {
            Id = "SIM_PORTRAIT", DeviceName = @"\\.\DISPLAY1", FriendlyName = "Side Portrait 24\" (Sim)", Manufacturer = "Dell",
            Bounds = new PixelRect(-1080, -240, 1080, 1920), WorkingArea = new PixelRect(-1080, -240, 1080, 1880),
            NativeWidth = 1080, NativeHeight = 1920, Orientation = DisplayOrientation.Portrait,
            PhysicalWidthMm = 296, PhysicalHeightMm = 527, HasHardwarePhysicalDimensions = true,
            ScalePercent = 100, RefreshRateHz = 60, IsConnected = true, IsIncluded = true
        },
        new MonitorInfo
        {
            Id = "SIM_CENTER_1440", DeviceName = @"\\.\DISPLAY2", FriendlyName = "Main 27\" QHD (Sim)", Manufacturer = "LG",
            Bounds = new PixelRect(0, 0, 2560, 1440), WorkingArea = new PixelRect(0, 0, 2560, 1400),
            NativeWidth = 2560, NativeHeight = 1440, PhysicalWidthMm = 597, PhysicalHeightMm = 336,
            HasHardwarePhysicalDimensions = true, IsPrimary = true, ScalePercent = 125, RefreshRateHz = 144, IsConnected = true, IsIncluded = true
        },
        new MonitorInfo
        {
            Id = "SIM_RIGHT_1080", DeviceName = @"\\.\DISPLAY3", FriendlyName = "Right 24\" FHD (Sim)", Manufacturer = "ASUS",
            Bounds = new PixelRect(2560, 180, 1920, 1080), WorkingArea = new PixelRect(2560, 180, 1920, 1040),
            NativeWidth = 1920, NativeHeight = 1080, PhysicalWidthMm = 527, PhysicalHeightMm = 296,
            HasHardwarePhysicalDimensions = true, ScalePercent = 100, RefreshRateHz = 75, IsConnected = true, IsIncluded = true
        }
    };

    private static IReadOnlyList<MonitorInfo> BuildSimulatedStacked() => new[]
    {
        new MonitorInfo
        {
            Id = "SIM_TOP", DeviceName = @"\\.\DISPLAY1", FriendlyName = "Top 27\" 1080p (Sim)", Manufacturer = "Planar",
            Bounds = new PixelRect(320, -1080, 1920, 1080), WorkingArea = new PixelRect(320, -1080, 1920, 1040),
            NativeWidth = 1920, NativeHeight = 1080, PhysicalWidthMm = 597, PhysicalHeightMm = 336,
            HasHardwarePhysicalDimensions = true, ScalePercent = 100, RefreshRateHz = 60, IsConnected = true, IsIncluded = true
        },
        new MonitorInfo
        {
            Id = "SIM_BOTTOM", DeviceName = @"\\.\DISPLAY2", FriendlyName = "Bottom 34\" UWQHD (Sim)", Manufacturer = "Samsung",
            Bounds = new PixelRect(0, 0, 3440, 1440), WorkingArea = new PixelRect(0, 0, 3440, 1400),
            NativeWidth = 3440, NativeHeight = 1440, PhysicalWidthMm = 797, PhysicalHeightMm = 334,
            HasHardwarePhysicalDimensions = true, IsPrimary = true, ScalePercent = 100, RefreshRateHz = 144, IsConnected = true, IsIncluded = true
        }
    };

    public void Dispose()
    {
        _galleryCts?.Cancel();
        _galleryCts?.Dispose();
        _searchDebounceCts?.Cancel();
        _searchDebounceCts?.Dispose();
        _customSiteProvider.Dispose();
        _taskbarTweaksService.Dispose();
        _watcher.Dispose();
    }
}
