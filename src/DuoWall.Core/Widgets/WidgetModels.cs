using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json;
using DuoWall.Core.Theming;

namespace DuoWall.Core.Widgets;

public enum WidgetKind
{
    DigitalClock,
    AnalogClock,
    Weather,
    CalendarAgenda,
    TodoList,
    FocusTimer,
    SystemStats,
    AmbientGeometry,
    PanoramicVideo,
    ShortcutBar,
    CustomDeclarative
}

public enum CalendarAgendaEntryKind
{
    Event,
    Reminder,
    Note
}

public sealed class CalendarAgendaEntry
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    /// <summary>ISO date string yyyy-MM-dd.</summary>
    public string DateIso { get; set; } = DateTime.Today.ToString("yyyy-MM-dd");
    public CalendarAgendaEntryKind Kind { get; set; } = CalendarAgendaEntryKind.Event;
    public string Title { get; set; } = string.Empty;
    public string Notes { get; set; } = string.Empty;
    /// <summary>24h local time HH:mm (optional for Notes).</summary>
    public string StartTimeText { get; set; } = string.Empty;
    public int DurationMinutes { get; set; } = 30;
    /// <summary>When false, shows silent reminder popup; when true, plays looping sound until dismissed.</summary>
    public bool PlayAlarmSound { get; set; }
    public bool IsDismissed { get; set; }
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
}

public sealed class ShortcutDockItem : INotifyPropertyChanged
{
    private string _id = Guid.NewGuid().ToString("N");
    private string _label = "Shortcut";
    private string _targetPathOrUrl = "https://google.com";
    private string _iconMode = "Auto";
    private string _initials = "SC";
    private string _glyphKey = "Globe";
    private string _customColorHex = "#FF38BDF8";
    private string _customIconPath = string.Empty;

    public string Id { get => _id; set => SetField(ref _id, value); }
    public string Label { get => _label; set => SetField(ref _label, value); }
    public string TargetPathOrUrl { get => _targetPathOrUrl; set => SetField(ref _targetPathOrUrl, value); }
    /// <summary>Auto | CustomIcon | Initials | GlassGlyph</summary>
    public string IconMode { get => _iconMode; set => SetField(ref _iconMode, value); }
    public string Initials { get => _initials; set => SetField(ref _initials, value); }
    public string GlyphKey { get => _glyphKey; set => SetField(ref _glyphKey, value); }
    public string CustomColorHex { get => _customColorHex; set => SetField(ref _customColorHex, value); }
    public string CustomIconPath { get => _customIconPath; set => SetField(ref _customIconPath, value); }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void SetField(ref string field, string value, [CallerMemberName] string? propertyName = null)
    {
        if (string.Equals(field, value, StringComparison.Ordinal)) return;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}

public static class ShortcutDockConfigHelper
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    public static List<ShortcutDockItem> ParseItems(IReadOnlyDictionary<string, string>? config)
    {
        // Respect explicit ShortcutItemsJson even when "[]" (user deleted all items -> show empty dock, NOT default icons)
        if (config != null && config.TryGetValue("ShortcutItemsJson", out string? json))
        {
            if (string.IsNullOrWhiteSpace(json))
                return new List<ShortcutDockItem>();

            try
            {
                return JsonSerializer.Deserialize<List<ShortcutDockItem>>(json, JsonOpts) ?? new List<ShortcutDockItem>();
            }
            catch
            {
                return new List<ShortcutDockItem>();
            }
        }

        return new List<ShortcutDockItem>
        {
            new() { Label = "Browser", TargetPathOrUrl = "https://www.google.com", IconMode = "GlassGlyph", Initials = "WW", GlyphKey = "Globe", CustomColorHex = "#FF38BDF8" },
            new() { Label = "YouTube", TargetPathOrUrl = "https://www.youtube.com", IconMode = "Initials", Initials = "YT", GlyphKey = "Play", CustomColorHex = "#FFEF4444" },
            new() { Label = "Notepad", TargetPathOrUrl = "notepad.exe", IconMode = "Auto", Initials = "NP", GlyphKey = "Document", CustomColorHex = "#FF22C55E" }
        };
    }

    public static void SaveItems(IDictionary<string, string> config, IEnumerable<ShortcutDockItem> items)
    {
        config["ShortcutItemsJson"] = JsonSerializer.Serialize(items.ToList(), JsonOpts);
    }
}

public enum ProfileTriggerType
{
    Manual,
    TimeOfDay,
    DayOfWeek,
    Startup,
    MonitorTopology,
    FullScreenApp,
    PowerState
}

public enum TriggerPowerCondition
{
    Any,
    OnAcPower,
    OnBattery,
    BatteryLow
}

/// <summary>
/// Represents a single desktop widget instance positioned relative to a target monitor (or primary display).
/// </summary>
public sealed class WidgetInstance
{
    public string InstanceId { get; set; } = Guid.NewGuid().ToString("N");
    public string PackageId { get; set; } = string.Empty;
    public WidgetKind Kind { get; set; }
    public string Title { get; set; } = "Widget";

    /// <summary>
    /// Target MonitorInfo.Id, or "PRIMARY" / "LEFT" / "RIGHT" logical anchor resolved at runtime.
    /// </summary>
    public string TargetMonitorAnchor { get; set; } = "PRIMARY";

    /// <summary>
    /// Position & dimensions relative to the target monitor (in DIPs).
    /// </summary>
    public double X { get; set; } = 40;
    public double Y { get; set; } = 40;
    public double Width { get; set; } = 320;
    public double Height { get; set; } = 160;
    public int ZIndex { get; set; }

    public bool IsVisible { get; set; } = true;
    public bool IsClickThrough { get; set; }
    public bool MinimalAnimations { get; set; }
    public double BrightnessMultiplier { get; set; } = 1.0;

    /// <summary>
    /// When true, inherits the active global/wallpaper-adapted Glassmorphism style.
    /// When false, retains the widget's custom GlassStyle override.
    /// </summary>
    public bool UseGlobalGlassTheme { get; set; } = true;

    public GlassmorphismStyle GlassStyle { get; set; } = GlassmorphismStyle.BuiltInPresets[0].Clone();

    public Dictionary<string, string> Config { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public WidgetInstance Clone(bool newInstanceId = false) => new()
    {
        InstanceId = newInstanceId ? Guid.NewGuid().ToString("N") : InstanceId,
        PackageId = PackageId,
        Kind = Kind,
        Title = Title,
        TargetMonitorAnchor = TargetMonitorAnchor,
        X = X,
        Y = Y,
        Width = Width,
        Height = Height,
        ZIndex = ZIndex,
        IsVisible = IsVisible,
        IsClickThrough = IsClickThrough,
        MinimalAnimations = MinimalAnimations,
        BrightnessMultiplier = BrightnessMultiplier,
        UseGlobalGlassTheme = UseGlobalGlassTheme,
        GlassStyle = GlassStyle.Clone(),
        Config = new Dictionary<string, string>(Config, StringComparer.OrdinalIgnoreCase)
    };
}

public sealed class ProfileTrigger
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public bool IsEnabled { get; set; } = true;
    public ProfileTriggerType TriggerType { get; set; } = ProfileTriggerType.TimeOfDay;

    /// <summary>
    /// Start time (HH:mm) for TimeOfDay trigger. Supports overnight ranges (e.g. 22:00 -> 06:00).
    /// </summary>
    public TimeSpan StartTime { get; set; } = TimeSpan.FromHours(6);
    public TimeSpan EndTime { get; set; } = TimeSpan.FromHours(11);

    /// <summary>
    /// Optional day-of-week filter (empty = every day).
    /// </summary>
    public List<DayOfWeek> DaysOfWeek { get; set; } = new();

    /// <summary>
    /// Expected Win32 topology signature or substring for MonitorTopology trigger.
    /// </summary>
    public string? MonitorTopologyPattern { get; set; }

    /// <summary>
    /// When true on FullScreenApp trigger, activates whenever a full-screen app/game is running.
    /// </summary>
    public bool RequireFullScreenActive { get; set; } = true;

    public TriggerPowerCondition PowerCondition { get; set; } = TriggerPowerCondition.Any;

    public ProfileTrigger Clone() => new()
    {
        Id = Id,
        IsEnabled = IsEnabled,
        TriggerType = TriggerType,
        StartTime = StartTime,
        EndTime = EndTime,
        DaysOfWeek = new List<DayOfWeek>(DaysOfWeek),
        MonitorTopologyPattern = MonitorTopologyPattern,
        RequireFullScreenActive = RequireFullScreenActive,
        PowerCondition = PowerCondition
    };
}

public sealed class DesktopProfile
{
    public string ProfileId { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "Custom Profile";
    public string Description { get; set; } = string.Empty;
    public bool IsBuiltIn { get; set; }
    public bool IsEnabled { get; set; } = true;

    /// <summary>
    /// Higher priority wins when multiple automatic triggers match simultaneously.
    /// </summary>
    public int Priority { get; set; } = 10;

    public string GlassPresetName { get; set; } = "Frosted Glass";
    public bool UseDarkTheme { get; set; } = true;
    public double WidgetBrightness { get; set; } = 1.0;
    public bool MinimalAnimations { get; set; }

    /// <summary>
    /// Optional panoramic video file path for Ambient / Video layouts.
    /// </summary>
    public string? PanoramicVideoPath { get; set; }
    public bool LoopPanoramicVideo { get; set; } = true;
    public bool PauseVideoOnBattery { get; set; }
    public bool PauseVideoOnFullScreen { get; set; } = true;

    public List<ProfileTrigger> Triggers { get; set; } = new();
    public List<WidgetInstance> Widgets { get; set; } = new();

    public DesktopProfile Clone() => new()
    {
        ProfileId = ProfileId,
        Name = Name,
        Description = Description,
        IsBuiltIn = IsBuiltIn,
        IsEnabled = IsEnabled,
        Priority = Priority,
        GlassPresetName = GlassPresetName,
        UseDarkTheme = UseDarkTheme,
        WidgetBrightness = WidgetBrightness,
        MinimalAnimations = MinimalAnimations,
        PanoramicVideoPath = PanoramicVideoPath,
        LoopPanoramicVideo = LoopPanoramicVideo,
        PauseVideoOnBattery = PauseVideoOnBattery,
        PauseVideoOnFullScreen = PauseVideoOnFullScreen,
        Triggers = Triggers.Select(t => t.Clone()).ToList(),
        Widgets = Widgets.Select(w => w.Clone()).ToList()
    };
}

public sealed class TodoTaskItem
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Text { get; set; } = string.Empty;
    public bool IsCompleted { get; set; }
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
}

public static class BuiltInProfilePresets
{
    public static List<DesktopProfile> CreateDefaultProfiles()
    {
        var frosted = GlassmorphismStyle.GetPreset("Frosted Glass");
        var dark = GlassmorphismStyle.GetPreset("Dark Glass");
        var midnight = GlassmorphismStyle.GetPreset("Midnight Glass");
        var crystal = GlassmorphismStyle.GetPreset("Crystal");
        var minimal = GlassmorphismStyle.GetPreset("Minimal Glass");

        return new List<DesktopProfile>
        {
            new DesktopProfile
            {
                ProfileId = "preset-morning",
                Name = "Morning Profile",
                Description = "Large digital clock, weather, today's calendar, and soft frosted glass.",
                IsBuiltIn = true,
                IsEnabled = false,
                Priority = 20,
                GlassPresetName = "Frosted Glass",
                UseDarkTheme = false,
                WidgetBrightness = 1.0,
                MinimalAnimations = true,
                Triggers = new List<ProfileTrigger>
                {
                    new()
                    {
                        IsEnabled = true,
                        TriggerType = ProfileTriggerType.TimeOfDay,
                        StartTime = TimeSpan.FromHours(6),
                        EndTime = TimeSpan.FromHours(10)
                    }
                },
                Widgets = new List<WidgetInstance>
                {
                    new()
                    {
                        InstanceId = "morning-clock", PackageId = "builtin.digital-clock", Kind = WidgetKind.DigitalClock,
                        Title = "Morning Clock", TargetMonitorAnchor = "PRIMARY", X = 48, Y = 48, Width = 380, Height = 155,
                        MinimalAnimations = true, GlassStyle = frosted.Clone(),
                        Config = new() { ["Format24H"] = "false", ["ShowSeconds"] = "false", ["ShowGreeting"] = "true" }
                    },
                    new()
                    {
                        InstanceId = "morning-weather", PackageId = "builtin.weather", Kind = WidgetKind.Weather,
                        Title = "Weather Forecast", TargetMonitorAnchor = "PRIMARY", X = 48, Y = 220, Width = 380, Height = 180,
                        MinimalAnimations = true, GlassStyle = frosted.Clone(),
                        Config = new() { ["City"] = "Lahore", ["Units"] = "metric" }
                    },
                    new()
                    {
                        InstanceId = "morning-cal", PackageId = "builtin.calendar", Kind = WidgetKind.CalendarAgenda,
                        Title = "Monthly Calendar", TargetMonitorAnchor = "LEFT", X = 48, Y = 48, Width = 340, Height = 320,
                        MinimalAnimations = true, GlassStyle = frosted.Clone()
                    },
                    new()
                    {
                        InstanceId = "morning-dock", PackageId = "builtin.shortcut-bar", Kind = WidgetKind.ShortcutBar,
                        Title = "Quick Dock", TargetMonitorAnchor = "LEFT", X = 48, Y = 386, Width = 340, Height = 96,
                        MinimalAnimations = true, GlassStyle = frosted.Clone()
                    }
                }
            },
            new DesktopProfile
            {
                ProfileId = "preset-work",
                Name = "Work Profile",
                Description = "Calendar, to-do list, CPU/RAM & network telemetry, and focus timer in Dark Glass.",
                IsBuiltIn = true,
                IsEnabled = false,
                Priority = 30,
                GlassPresetName = "Dark Glass",
                UseDarkTheme = true,
                WidgetBrightness = 1.0,
                Triggers = new List<ProfileTrigger>
                {
                    new()
                    {
                        IsEnabled = true,
                        TriggerType = ProfileTriggerType.TimeOfDay,
                        StartTime = TimeSpan.FromHours(10),
                        EndTime = TimeSpan.FromHours(18),
                        DaysOfWeek = new() { DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday }
                    }
                },
                Widgets = new List<WidgetInstance>
                {
                    new()
                    {
                        InstanceId = "work-focus", PackageId = "builtin.focus-timer", Kind = WidgetKind.FocusTimer,
                        Title = "Focus Timer", TargetMonitorAnchor = "LEFT", X = 42, Y = 42, Width = 320, Height = 190,
                        GlassStyle = dark.Clone(),
                        Config = new() { ["WorkMinutes"] = "25", ["BreakMinutes"] = "5" }
                    },
                    new()
                    {
                        InstanceId = "work-todo", PackageId = "builtin.todo", Kind = WidgetKind.TodoList,
                        Title = "Tasks", TargetMonitorAnchor = "LEFT", X = 42, Y = 250, Width = 320, Height = 280,
                        GlassStyle = dark.Clone()
                    },
                    new()
                    {
                        InstanceId = "work-cal", PackageId = "builtin.calendar", Kind = WidgetKind.CalendarAgenda,
                        Title = "Calendar", TargetMonitorAnchor = "PRIMARY", X = 48, Y = 48, Width = 340, Height = 320,
                        GlassStyle = dark.Clone()
                    },
                    new()
                    {
                        InstanceId = "work-sys", PackageId = "builtin.system-stats", Kind = WidgetKind.SystemStats,
                        Title = "System & Network", TargetMonitorAnchor = "PRIMARY", X = 48, Y = 384, Width = 340, Height = 195,
                        GlassStyle = dark.Clone(),
                        Config = new() { ["Mode"] = "Compact" }
                    }
                }
            },
            new DesktopProfile
            {
                ProfileId = "preset-monitoring",
                Name = "Monitoring Profile",
                Description = "Full CPU/GPU, RAM, network, storage, and hardware temperatures in futuristic Midnight HUD.",
                IsBuiltIn = true,
                IsEnabled = false,
                Priority = 40,
                GlassPresetName = "Midnight Glass",
                UseDarkTheme = true,
                WidgetBrightness = 1.0,
                Widgets = new List<WidgetInstance>
                {
                    new()
                    {
                        InstanceId = "mon-hud", PackageId = "builtin.system-stats", Kind = WidgetKind.SystemStats,
                        Title = "Hardware Telemetry HUD", TargetMonitorAnchor = "LEFT", X = 40, Y = 40, Width = 440, Height = 360,
                        GlassStyle = midnight.Clone(),
                        Config = new() { ["Mode"] = "Detailed" }
                    },
                    new()
                    {
                        InstanceId = "mon-clock", PackageId = "builtin.digital-clock", Kind = WidgetKind.DigitalClock,
                        Title = "System Clock", TargetMonitorAnchor = "PRIMARY", X = 48, Y = 48, Width = 340, Height = 140,
                        GlassStyle = midnight.Clone(),
                        Config = new() { ["Format24H"] = "true", ["ShowSeconds"] = "true", ["ShowGreeting"] = "false" }
                    }
                }
            },
            new DesktopProfile
            {
                ProfileId = "preset-ambient",
                Name = "Ambient Profile",
                Description = "Analog clock, quick dock bar, and weather in subtle Crystal glass.",
                IsBuiltIn = true,
                IsEnabled = false,
                Priority = 25,
                GlassPresetName = "Crystal",
                UseDarkTheme = true,
                Widgets = new List<WidgetInstance>
                {
                    new()
                    {
                        InstanceId = "amb-analog", PackageId = "builtin.analog-clock", Kind = WidgetKind.AnalogClock,
                        Title = "Analog Timepiece", TargetMonitorAnchor = "LEFT", X = 56, Y = 56, Width = 220, Height = 220,
                        GlassStyle = crystal.Clone()
                    },
                    new()
                    {
                        InstanceId = "amb-dock", PackageId = "builtin.shortcut-bar", Kind = WidgetKind.ShortcutBar,
                        Title = "Quick Dock", TargetMonitorAnchor = "LEFT", X = 56, Y = 296, Width = 320, Height = 96,
                        GlassStyle = crystal.Clone()
                    },
                    new()
                    {
                        InstanceId = "amb-weather", PackageId = "builtin.weather", Kind = WidgetKind.Weather,
                        Title = "Minimal Weather", TargetMonitorAnchor = "PRIMARY", X = 42, Y = 206, Width = 320, Height = 165,
                        GlassStyle = crystal.Clone()
                    }
                }
            },
            new DesktopProfile
            {
                ProfileId = "preset-night",
                Name = "Night Profile",
                Description = "Dimmed dark glass, minimal animations, and nighttime clock & weather.",
                IsBuiltIn = true,
                IsEnabled = false,
                Priority = 35,
                GlassPresetName = "Minimal Glass",
                UseDarkTheme = true,
                WidgetBrightness = 0.65,
                MinimalAnimations = true,
                Triggers = new List<ProfileTrigger>
                {
                    new()
                    {
                        IsEnabled = true,
                        TriggerType = ProfileTriggerType.TimeOfDay,
                        StartTime = TimeSpan.FromHours(21),
                        EndTime = TimeSpan.FromHours(6)
                    }
                },
                Widgets = new List<WidgetInstance>
                {
                    new()
                    {
                        InstanceId = "night-clock", PackageId = "builtin.digital-clock", Kind = WidgetKind.DigitalClock,
                        Title = "Night Clock", TargetMonitorAnchor = "PRIMARY", X = 48, Y = 48, Width = 360, Height = 150,
                        BrightnessMultiplier = 0.65, MinimalAnimations = true, GlassStyle = minimal.Clone(),
                        Config = new() { ["Format24H"] = "false", ["ShowSeconds"] = "false", ["ShowGreeting"] = "false" }
                    },
                    new()
                    {
                        InstanceId = "night-weather", PackageId = "builtin.weather", Kind = WidgetKind.Weather,
                        Title = "Night Weather", TargetMonitorAnchor = "PRIMARY", X = 48, Y = 216, Width = 360, Height = 170,
                        BrightnessMultiplier = 0.65, MinimalAnimations = true, GlassStyle = minimal.Clone()
                    }
                }
            }
        };
    }
}

