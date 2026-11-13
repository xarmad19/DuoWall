using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using DuoWall.App.Windows;
using DuoWall.Core.Models;
using DuoWall.Core.Profiles;
using DuoWall.Core.Services;
using DuoWall.Core.Storage;
using DuoWall.Core.Theming;
using DuoWall.Core.Widgets;

namespace DuoWall.App.Services;

/// <summary>
/// Coordinates live desktop widget overlay windows, panoramic video backdrop, screen-aligned
/// Skia variable-blur backdrop slices via <see cref="MonitorWallpaperSampler"/>, and shared timers.
/// </summary>
public sealed class DesktopOverlayCoordinator : IDisposable
{
    private readonly DuoWallDatabase _database;
    private readonly WeatherService _weatherService;
    private readonly Win32DisplayDetector _displayDetector;
    private readonly MonitorWallpaperSampler _wallpaperSampler;
    private readonly SystemTelemetryReader _telemetryReader = new();
    private readonly DesktopContextMonitor _contextMonitor = new();
    private readonly ProfileEvaluationEngine _profileEngine = new();

    private readonly Dictionary<string, DesktopWidgetHostWindow> _widgetWindows = new(StringComparer.OrdinalIgnoreCase);
    private readonly DispatcherTimer _secondTimer;
    private readonly DispatcherTimer _contextTimer;
    private readonly DispatcherTimer _weatherTimer;

    private PanoramicVideoBackdropWindow? _videoWindow;
    private AppSettings _settings = new();
    private DesktopProfile? _activeProfile;
    private GlassmorphismStyle _globalGlass = GlassmorphismStyle.BuiltInPresets[0].Clone();
    private IReadOnlyList<MonitorInfo> _monitors = Array.Empty<MonitorInfo>();
    private List<TodoTaskItem> _todos = new();
    private List<CalendarAgendaEntry> _calendarEntries = new();
    private readonly HashSet<string> _triggeredAlertKeys = new(StringComparer.OrdinalIgnoreCase);

    private WeatherSnapshot? _latestWeather;
    private string _weatherStatus = "Initializing weather...";
    private TelemetrySnapshot? _latestTelemetry;
    private CompositionPlan? _liveVideoPlan;
    private string _liveVideoBgHex = "#0D1017";

    private bool _isEditMode;
    private bool _disposed;
    private bool _isStartupEvaluation = true;

    public event Action<WidgetInstance>? WidgetBoundsUpdatedFromDesktop;
    public event Action<List<TodoTaskItem>>? TodosUpdated;
    public event Action<DesktopProfile, ProfileEvaluationResult>? AutoProfileActivated;
    public event Action<string>? StatusMessageRaised;

    public DesktopOverlayCoordinator(
        DuoWallDatabase database,
        WeatherService weatherService,
        Win32DisplayDetector displayDetector,
        MonitorWallpaperSampler wallpaperSampler)
    {
        _database = database;
        _weatherService = weatherService;
        _displayDetector = displayDetector;
        _wallpaperSampler = wallpaperSampler;
        _todos = _database.LoadTodoItems();
        _calendarEntries = _database.LoadCalendarEntries();

        _secondTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromSeconds(1)
        };
        _secondTimer.Tick += (_, _) => OnSecondTick();

        _contextTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromSeconds(12)
        };
        _contextTimer.Tick += (_, _) => EvaluateAutoContext();

        _weatherTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMinutes(20)
        };
        _weatherTimer.Tick += async (_, _) => await RefreshWeatherAsync();
    }

    public WeatherSnapshot? LatestWeather => _latestWeather;
    public string WeatherStatus => _weatherStatus;
    public TelemetrySnapshot? LatestTelemetry => _latestTelemetry;
    public IReadOnlyList<TodoTaskItem> Todos => _todos;
    public MonitorWallpaperSampler WallpaperSampler => _wallpaperSampler;
    public bool IsEditMode => _isEditMode;

    public void Initialize(
        AppSettings settings,
        IReadOnlyList<MonitorInfo> monitors,
        DesktopProfile? activeProfile,
        GlassmorphismStyle globalGlass)
    {
        _settings = settings;
        _monitors = monitors;
        _activeProfile = activeProfile;
        _globalGlass = globalGlass;
        _todos = _database.LoadTodoItems();
        _calendarEntries = _database.LoadCalendarEntries();

        _wallpaperSampler.RefreshActivePaths(monitors);
        _latestTelemetry = _telemetryReader.Sample(includeDetailedSensors: true);

        RebuildOverlays();

        _secondTimer.Start();
        _contextTimer.Start();
        _weatherTimer.Start();
        _ = RefreshWeatherAsync();
    }

    public void RefreshWallpaperCache(IReadOnlyList<MonitorInfo>? monitors = null)
    {
        if (monitors != null) _monitors = monitors;
        _wallpaperSampler.RefreshActivePaths(_monitors);
        UpdateGlobalGlassLive(_globalGlass, _activeProfile?.WidgetBrightness ?? 1.0);
    }

    public void UpdateConfiguration(
        AppSettings settings,
        IReadOnlyList<MonitorInfo> monitors,
        DesktopProfile? activeProfile,
        GlassmorphismStyle globalGlass)
    {
        _settings = settings;
        _monitors = monitors;
        _activeProfile = activeProfile;
        _globalGlass = globalGlass;
        _wallpaperSampler.RefreshActivePaths(monitors);
        RebuildOverlays();
    }

    public void UpdateGlobalGlassLive(GlassmorphismStyle globalGlass, double brightnessMultiplier)
    {
        _globalGlass = globalGlass;
        if (_activeProfile != null)
        {
            _activeProfile.WidgetBrightness = brightnessMultiplier;
        }

        foreach (var host in _widgetWindows.Values)
        {
            var widget = host.ContentControl.CurrentWidget;
            GlassmorphismStyle effective = widget.UseGlobalGlassTheme ? globalGlass : widget.GlassStyle;
            var mon = ResolveTargetMonitor(widget.TargetMonitorAnchor);
            ImageSource? slice = BuildSliceForWidget(mon, widget, effective);
            host.UpdateLiveGlass(
                effective,
                widget.UseGlobalGlassTheme ? brightnessMultiplier : widget.BrightnessMultiplier,
                slice);
        }
    }

    public void SetEditMode(bool enabled)
    {
        _isEditMode = enabled;
        foreach (var host in _widgetWindows.Values)
        {
            host.SetEditMode(enabled);
        }
    }

    public void UpdateGridOptions(bool snapToGrid, int gridSizePx)
    {
        _settings.EnableWidgetSnapToGrid = snapToGrid;
        _settings.WidgetGridSizePx = Math.Clamp(gridSizePx, 2, 128);
        foreach (var host in _widgetWindows.Values)
        {
            host.SetGridOptions(snapToGrid, _settings.WidgetGridSizePx);
        }
    }

    /// <summary>
    /// Repositions already-visible desktop widget host windows in real time while dragging inside Placement Studio,
    /// avoiding window reconstruction until the move completes.
    /// </summary>
    public void UpdateWidgetPositionsLive(IEnumerable<WidgetInstance> widgets)
    {
        if (!_settings.EnableDesktopWidgets) return;
        foreach (var w in widgets)
        {
            if (!_widgetWindows.TryGetValue(w.InstanceId, out var host) || !host.IsVisible)
                continue;
            var mon = ResolveTargetMonitor(w.TargetMonitorAnchor);
            var (dipX, dipY) = ComputeMonitorOriginDip(mon);
            host.Left = dipX + w.X;
            host.Top = dipY + w.Y;
        }
    }

    public void ApplyLiveVideoWallpaper(string? videoPath, CompositionPlan? plan = null, string? backgroundColorHex = null)
    {
        _settings.ActiveLiveVideoPath = string.IsNullOrWhiteSpace(videoPath) ? null : videoPath;
        _liveVideoPlan = plan;
        if (!string.IsNullOrWhiteSpace(backgroundColorHex))
            _liveVideoBgHex = backgroundColorHex;

        UpdatePanoramicVideoLayer();
        UpdateGlobalGlassLive(_globalGlass, _activeProfile?.WidgetBrightness ?? 1.0);
    }

    public void SetTodos(List<TodoTaskItem> items)
    {
        _todos = items;
        _database.SaveTodoItems(items);
        foreach (var host in _widgetWindows.Values)
        {
            host.ContentControl.UpdateTodos(items);
        }
    }

    public async Task RefreshWeatherAsync()
    {
        var wxWidget = _activeProfile?.Widgets.FirstOrDefault(w => w.IsVisible && w.Kind == WidgetKind.Weather)
                       ?? _activeProfile?.Widgets.FirstOrDefault(w => w.Kind == WidgetKind.Weather);

        string city = wxWidget != null && wxWidget.Config.TryGetValue("City", out var wCity) && !string.IsNullOrWhiteSpace(wCity)
            ? wCity
            : _settings.WeatherCity;
        bool useF = wxWidget != null && wxWidget.Config.TryGetValue("Units", out var units) && !string.IsNullOrWhiteSpace(units)
            ? string.Equals(units, "imperial", StringComparison.OrdinalIgnoreCase)
            : _settings.WeatherUseFahrenheit;
        string provider = wxWidget != null && wxWidget.Config.TryGetValue("Provider", out var prov) && !string.IsNullOrWhiteSpace(prov)
            ? prov
            : (string.IsNullOrWhiteSpace(_settings.CustomWeatherApiKey) ? "OpenMeteo" : "OpenWeatherMap");
        string? apiKey = wxWidget != null && wxWidget.Config.TryGetValue("ApiKey", out var k) && !string.IsNullOrWhiteSpace(k)
            ? k
            : _settings.CustomWeatherApiKey;

        try
        {
            _weatherStatus = "Refreshing weather...";
            var wx = await _weatherService.GetCurrentWeatherAsync(city, useF, provider, apiKey);
            _latestWeather = wx;
            _weatherStatus = $"Synced ({wx.ProviderName})";
        }
        catch (Exception ex)
        {
            _weatherStatus = $"Weather error: {ex.Message}";
        }

        Application.Current?.Dispatcher.Invoke(() =>
        {
            foreach (var host in _widgetWindows.Values)
            {
                host.ContentControl.UpdateWeather(_latestWeather, _weatherStatus);
            }
        });
    }

    public void RebuildOverlays()
    {
        UpdatePanoramicVideoLayer();

        if (!_settings.EnableDesktopWidgets || _activeProfile == null)
        {
            foreach (var host in _widgetWindows.Values)
                host.Close();
            _widgetWindows.Clear();
            return;
        }

        var activeIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        string? effectiveVideo = ResolveActiveVideoPath();
        bool keepLoopingVideo = !string.IsNullOrWhiteSpace(effectiveVideo) && File.Exists(effectiveVideo);
        bool videoPlaying = _videoWindow?.IsEffectivelyPlaying ?? false;
        string videoStatus = _videoWindow?.StatusSummary ?? (keepLoopingVideo ? "Ready" : "No video assigned");

        foreach (var widget in _activeProfile.Widgets.Where(w => w.IsVisible))
        {
            activeIds.Add(widget.InstanceId);

            if (!_widgetWindows.TryGetValue(widget.InstanceId, out var host))
            {
                host = new DesktopWidgetHostWindow();
                host.BoundsCommitted += OnHostBoundsCommitted;
                host.ContentControl.TodoListChanged += list =>
                {
                    SetTodos(list);
                    TodosUpdated?.Invoke(list);
                };
                host.ContentControl.RefreshWeatherRequested += () => _ = RefreshWeatherAsync();
                host.ContentControl.ToggleVideoPlaybackRequested += () => _videoWindow?.ToggleUserPause();
                host.ContentControl.CalendarDateClicked += OpenCalendarDateModal;
                host.ContentControl.ShortcutLaunchRequested += LaunchShortcutItem;
                _widgetWindows[widget.InstanceId] = host;
            }

            var targetMonitor = ResolveTargetMonitor(widget.TargetMonitorAnchor);
            var (dipX, dipY) = ComputeMonitorOriginDip(targetMonitor);
            GlassmorphismStyle effectiveStyle = widget.UseGlobalGlassTheme ? _globalGlass : widget.GlassStyle;

            widget.BrightnessMultiplier = widget.UseGlobalGlassTheme
                ? _activeProfile.WidgetBrightness
                : widget.BrightnessMultiplier;
            widget.MinimalAnimations = _activeProfile.MinimalAnimations;

            if (!host.IsVisible)
            {
                host.Show();
            }

            ImageSource? slice = BuildSliceForWidget(targetMonitor, widget, effectiveStyle);

            host.ApplyWidgetState(
                widget,
                effectiveStyle,
                dipX + widget.X,
                dipY + widget.Y,
                _isEditMode,
                _settings.EnableWidgetSnapToGrid,
                _settings.WidgetGridSizePx,
                slice,
                _latestWeather,
                _weatherStatus,
                _latestTelemetry,
                _todos,
                videoPlaying,
                videoStatus,
                _calendarEntries);
        }

        foreach (string staleId in _widgetWindows.Keys.Where(id => !activeIds.Contains(id)).ToList())
        {
            _widgetWindows[staleId].Close();
            _widgetWindows.Remove(staleId);
        }
    }

    private ImageSource? BuildSliceForWidget(MonitorInfo? targetMonitor, WidgetInstance widget, GlassmorphismStyle style)
    {
        if (style.SurfaceTheme != WidgetSurfaceTheme.Glassmorphism || !style.EnableBackdropBlur)
        {
            return null;
        }

        string? fallbackBackdropPath = !string.IsNullOrWhiteSpace(_settings.ActiveLiveVideoPath) &&
                                       File.Exists(_settings.ActiveLiveVideoStillPath)
            ? _settings.ActiveLiveVideoStillPath
            : _settings.LastAppliedImagePath;

        return _wallpaperSampler.CreateBlurredCropSlice(
            targetMonitor,
            fallbackBackdropPath,
            widget.X,
            widget.Y,
            widget.Width,
            widget.Height,
            style.BackgroundBlurIntensity,
            addFrostGrain: style.BackgroundBlurIntensity > 1.5);
    }

    private string? ResolveActiveVideoPath()
    {
        if (!string.IsNullOrWhiteSpace(_settings.ActiveLiveVideoPath) && File.Exists(_settings.ActiveLiveVideoPath))
            return _settings.ActiveLiveVideoPath;
        if (_activeProfile != null && !string.IsNullOrWhiteSpace(_activeProfile.PanoramicVideoPath) && File.Exists(_activeProfile.PanoramicVideoPath))
            return _activeProfile.PanoramicVideoPath;
        return null;
    }

    private void UpdatePanoramicVideoLayer()
    {
        string? videoPath = ResolveActiveVideoPath();
        if (string.IsNullOrWhiteSpace(videoPath) || !File.Exists(videoPath))
        {
            if (_videoWindow != null)
            {
                _videoWindow.Close();
                _videoWindow = null;
            }
            return;
        }

        _videoWindow ??= new PanoramicVideoBackdropWindow();
        if (!_videoWindow.IsVisible)
            _videoWindow.Show();

        var (minX, minY, maxR, maxB) = ComputeCombinedDesktopDipBounds();
        bool loop = !string.IsNullOrWhiteSpace(_settings.ActiveLiveVideoPath)
            ? _settings.LoopLiveVideoWallpaper
            : (_activeProfile?.LoopPanoramicVideo ?? true);

        _videoWindow.Configure(
            minX,
            minY,
            maxR - minX,
            maxB - minY,
            videoPath,
            loop,
            _liveVideoPlan,
            _liveVideoBgHex);

        EvaluateVideoPowerAndFullscreenState();
    }

    private void OnSecondTick()
    {
        bool needsTelemetry = _activeProfile?.Widgets.Any(w => w.IsVisible && w.Kind == WidgetKind.SystemStats) == true;
        if (needsTelemetry)
            _latestTelemetry = _telemetryReader.Sample(includeDetailedSensors: true);

        foreach (var host in _widgetWindows.Values)
        {
            host.ContentControl.TickSecond(needsTelemetry ? _latestTelemetry : null);
            if (_videoWindow != null)
                host.ContentControl.UpdateVideoStatus(_videoWindow.IsEffectivelyPlaying, _videoWindow.StatusSummary);
        }

        CheckScheduledCalendarReminders();
    }

    public IReadOnlyList<CalendarAgendaEntry> CalendarEntries => _calendarEntries;

    public void OpenCalendarDateModal(DateTime date)
    {
        string iso = date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var entriesForDate = _calendarEntries.Where(e => string.Equals(e.DateIso, iso, StringComparison.OrdinalIgnoreCase)).ToList();

        var dlg = new CalendarEntryDialogWindow(
            date,
            entriesForDate,
            saved =>
            {
                _database.UpsertCalendarEntry(saved);
                _calendarEntries = _database.LoadCalendarEntries();
                PushCalendarEntriesToWidgets();
                StatusMessageRaised?.Invoke($"Saved {saved.Kind} '{saved.Title}' for {saved.DateIso}.");
            },
            deletedId =>
            {
                _database.DeleteCalendarEntry(deletedId);
                _calendarEntries = _database.LoadCalendarEntries();
                PushCalendarEntriesToWidgets();
            });
        dlg.ShowDialog();
    }

    private void PushCalendarEntriesToWidgets()
    {
        foreach (var host in _widgetWindows.Values)
            host.ContentControl.UpdateCalendarEntries(_calendarEntries);
    }

    private void CheckScheduledCalendarReminders()
    {
        var now = DateTime.Now;
        string todayIso = now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        string hhmm = $"{now.Hour:00}:{now.Minute:00}";

        foreach (var entry in _calendarEntries.ToList())
        {
            if (entry.IsDismissed || entry.Kind == CalendarAgendaEntryKind.Note) continue;
            if (!string.Equals(entry.DateIso, todayIso, StringComparison.Ordinal)) continue;
            if (!string.Equals(entry.StartTimeText, hhmm, StringComparison.Ordinal)) continue;

            string triggerKey = $"{entry.Id}@{todayIso}@{hhmm}";
            if (!_triggeredAlertKeys.Add(triggerKey)) continue;

            var alertWin = new ReminderAlertWindow(entry, (actedEntry, snoozed) =>
            {
                if (snoozed)
                {
                    var snoozeAt = DateTime.Now.AddMinutes(5);
                    actedEntry.DateIso = snoozeAt.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
                    actedEntry.StartTimeText = $"{snoozeAt.Hour:00}:{snoozeAt.Minute:00}";
                }
                else
                {
                    actedEntry.IsDismissed = true;
                }
                _database.UpsertCalendarEntry(actedEntry);
                _calendarEntries = _database.LoadCalendarEntries();
                PushCalendarEntriesToWidgets();
            });
            alertWin.Show();
        }
    }

    private void LaunchShortcutItem(ShortcutDockItem item)
    {
        if (string.IsNullOrWhiteSpace(item.TargetPathOrUrl)) return;
        try
        {
            string target = item.TargetPathOrUrl.Trim();
            if (target.StartsWith("shell:", StringComparison.OrdinalIgnoreCase))
            {
                Process.Start(new ProcessStartInfo("explorer.exe", target) { UseShellExecute = true });
            }
            else
            {
                Process.Start(new ProcessStartInfo(target) { UseShellExecute = true });
            }
        }
        catch (Exception ex)
        {
            StatusMessageRaised?.Invoke($"Could not launch '{item.Label}': {ex.Message}");
        }
    }

    private void EvaluateAutoContext()
    {
        if (_disposed || !_settings.EnableDesktopWidgets)
            return;

        EvaluateVideoPowerAndFullscreenState();

        if (!_settings.EnableAutomaticProfileSwitching)
        {
            _isStartupEvaluation = false;
            return;
        }

        var profiles = _database.LoadDesktopProfilesEnsureDefaults();
        string topoSig = _monitors.Count > 0 ? Win32DisplayDetector.ComputeTopologySignature(_monitors) : string.Empty;
        string? manualLock = _settings.ManualProfileOverrideId;

        var ctx = _contextMonitor.Capture(_monitors, topoSig, _isStartupEvaluation, manualLock);
        _isStartupEvaluation = false;

        var result = _profileEngine.Evaluate(profiles, ctx);
        if (result.SelectedProfile != null &&
            !string.Equals(result.SelectedProfile.ProfileId, _activeProfile?.ProfileId, StringComparison.OrdinalIgnoreCase))
        {
            _activeProfile = result.SelectedProfile;
            _settings.ActiveDesktopProfileId = result.SelectedProfile.ProfileId;
            _globalGlass = GlassmorphismStyle.GetPreset(result.SelectedProfile.GlassPresetName);
            RebuildOverlays();
            AutoProfileActivated?.Invoke(result.SelectedProfile, result);
            StatusMessageRaised?.Invoke($"Auto-switched to '{result.SelectedProfile.Name}' ({result.MatchReason}).");
        }
    }

    private void EvaluateVideoPowerAndFullscreenState()
    {
        if (_videoWindow == null) return;

        bool pauseOnBattery = !string.IsNullOrWhiteSpace(_settings.ActiveLiveVideoPath)
            ? _settings.PauseLiveVideoOnBattery
            : (_activeProfile?.PauseVideoOnBattery ?? false);
        bool pauseOnFullscreen = !string.IsNullOrWhiteSpace(_settings.ActiveLiveVideoPath)
            ? _settings.PauseLiveVideoOnFullScreen
            : (_activeProfile?.PauseVideoOnFullScreen ?? true);
        bool loop = !string.IsNullOrWhiteSpace(_settings.ActiveLiveVideoPath)
            ? _settings.LoopLiveVideoWallpaper
            : (_activeProfile?.LoopPanoramicVideo ?? true);

        var (isOnBattery, _) = DesktopContextMonitor.ReadPowerState();
        bool isFullscreen = DesktopContextMonitor.DetectFullScreenForegroundApp(_monitors);

        if (pauseOnBattery && isOnBattery)
            _videoWindow.SetSystemPause(true, "Paused (Battery saver)");
        else if (pauseOnFullscreen && isFullscreen)
            _videoWindow.SetSystemPause(true, "Paused (Full-screen app)");
        else
            _videoWindow.SetSystemPause(false, loop ? "Looping continuously" : "Playing");
    }

    private void OnHostBoundsCommitted(WidgetInstance widget, double dipLeft, double dipTop, double dipWidth, double dipHeight)
    {
        var mon = ResolveTargetMonitorForPoint(dipLeft + dipWidth * 0.5, dipTop + dipHeight * 0.5)
                  ?? ResolveTargetMonitor(widget.TargetMonitorAnchor);

        if (mon != null)
        {
            widget.TargetMonitorAnchor = DetermineMonitorAnchorLabel(mon);
        }

        var (originX, originY) = ComputeMonitorOriginDip(mon);

        widget.X = Math.Round(dipLeft - originX, 1);
        widget.Y = Math.Round(dipTop - originY, 1);
        widget.Width = Math.Round(dipWidth, 1);
        widget.Height = Math.Round(dipHeight, 1);

        // Re-slice backdrop glass immediately for the new location on the desktop!
        if (_widgetWindows.TryGetValue(widget.InstanceId, out var host))
        {
            GlassmorphismStyle effective = widget.UseGlobalGlassTheme ? _globalGlass : widget.GlassStyle;
            host.UpdateLiveGlass(effective, widget.BrightnessMultiplier, BuildSliceForWidget(mon, widget, effective));
        }

        WidgetBoundsUpdatedFromDesktop?.Invoke(widget);
    }

    private string DetermineMonitorAnchorLabel(MonitorInfo mon)
    {
        if (mon.IsPrimary) return "PRIMARY";
        if (_monitors.Count >= 2)
        {
            var leftmost = _monitors.OrderBy(m => m.Bounds.X).First();
            if (string.Equals(leftmost.Id, mon.Id, StringComparison.OrdinalIgnoreCase))
                return "LEFT";
            var rightmost = _monitors.OrderByDescending(m => m.Bounds.X).First();
            if (string.Equals(rightmost.Id, mon.Id, StringComparison.OrdinalIgnoreCase))
                return "RIGHT";
        }
        return mon.Id;
    }

    private MonitorInfo? ResolveTargetMonitorForPoint(double dipX, double dipY)
    {
        foreach (var mon in _monitors)
        {
            double sx = Math.Max(96, mon.DpiX) / 96.0;
            double sy = Math.Max(96, mon.DpiY) / 96.0;
            double x = mon.Bounds.X / sx;
            double y = mon.Bounds.Y / sy;
            double w = mon.Bounds.Width / sx;
            double h = mon.Bounds.Height / sy;
            if (dipX >= x && dipX <= x + w && dipY >= y && dipY <= y + h)
                return mon;
        }
        return null;
    }

    public MonitorInfo? ResolveTargetMonitor(string anchor)
    {
        if (_monitors.Count == 0)
            _monitors = _displayDetector.DetectMonitors();

        if (_monitors.Count == 0)
            return null;

        if (string.Equals(anchor, "LEFT", StringComparison.OrdinalIgnoreCase))
            return _monitors.OrderBy(m => m.Bounds.X).First();

        if (string.Equals(anchor, "RIGHT", StringComparison.OrdinalIgnoreCase))
            return _monitors.OrderByDescending(m => m.Bounds.X).First();

        var byId = _monitors.FirstOrDefault(m => string.Equals(m.Id, anchor, StringComparison.OrdinalIgnoreCase));
        if (byId != null)
            return byId;

        return _monitors.FirstOrDefault(m => m.IsPrimary) ?? _monitors[0];
    }

    private static (double DipX, double DipY) ComputeMonitorOriginDip(MonitorInfo? mon)
    {
        if (mon == null) return (0, 0);
        double scaleX = Math.Max(96, mon.DpiX) / 96.0;
        double scaleY = Math.Max(96, mon.DpiY) / 96.0;
        return (mon.Bounds.X / scaleX, mon.Bounds.Y / scaleY);
    }

    private (double MinX, double MinY, double MaxR, double MaxB) ComputeCombinedDesktopDipBounds()
    {
        if (_monitors.Count == 0)
            return (0, 0, SystemParameters.VirtualScreenWidth, SystemParameters.VirtualScreenHeight);

        double minX = double.MaxValue, minY = double.MaxValue, maxR = double.MinValue, maxB = double.MinValue;
        foreach (var mon in _monitors)
        {
            double sx = Math.Max(96, mon.DpiX) / 96.0;
            double sy = Math.Max(96, mon.DpiY) / 96.0;
            double x = mon.Bounds.X / sx;
            double y = mon.Bounds.Y / sy;
            double w = mon.Bounds.Width / sx;
            double h = mon.Bounds.Height / sy;
            minX = Math.Min(minX, x);
            minY = Math.Min(minY, y);
            maxR = Math.Max(maxR, x + w);
            maxB = Math.Max(maxB, y + h);
        }

        return (minX, minY, maxR, maxB);
    }

    public void HideAllOverlays()
    {
        foreach (var host in _widgetWindows.Values)
            host.Close();
        _widgetWindows.Clear();

        if (_videoWindow != null)
        {
            _videoWindow.Close();
            _videoWindow = null;
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _secondTimer.Stop();
        _contextTimer.Stop();
        _weatherTimer.Stop();
        HideAllOverlays();
    }
}
