using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using DuoWall.App.Helpers;
using DuoWall.App.Windows;
using DuoWall.Core.Services;
using DuoWall.Core.Theming;
using DuoWall.Core.Widgets;

namespace DuoWall.App.Controls;

/// <summary>
/// Declarative multi-theme widget renderer hosted inside <see cref="GlassSurfaceControl"/>.
/// Dynamically rescales typography and vector elements cleanly when resized,
/// and supports full Rainmeter-inspired minimal clock element placement + styling.
/// </summary>
public sealed class WidgetContentControl : UserControl
{
    private readonly GlassSurfaceControl _glassSurface = new();
    private readonly Border _outerPaddingHost = new() { Padding = new Thickness(15, 12, 15, 12) };

    private WidgetInstance _widget = new();
    private WeatherSnapshot? _weather;
    private string? _weatherStatus;
    private TelemetrySnapshot? _telemetry;
    private List<TodoTaskItem> _todos = new();
    private bool _videoPlaying;
    private string _videoStatus = "Idle";
    private string _builtSignature = string.Empty;
    private double _currentBoxWidth = 340;
    private double _currentBoxHeight = 170;

    // Clock sub-elements (built dynamically per ClockLayout & element placements)
    private TextBlock? _clockTimeText;
    private TextBlock? _clockDateText;
    private TextBlock? _clockDayText;
    private TextBlock? _clockGreetingText;
    private TextBlock? _clockTzText;
    private Canvas? _analogCanvas;
    private Ellipse? _analogDialEllipse;
    private TextBlock? _analogSubTimeText;

    // Focus Timer
    private TextBlock? _focusCountdownText;
    private TextBlock? _focusPhaseText;
    private ProgressBar? _focusProgress;
    private Button? _focusToggleBtn;
    private Button? _focusResetBtn;
    private bool _focusRunning;
    private bool _focusIsBreak;
    private int _focusRemainingSeconds = 25 * 60;
    private int _focusTotalSeconds = 25 * 60;

    // Todo / Full Monthly Calendar
    private List<CalendarAgendaEntry> _calendarEntries = new();
    private int _calMonthOffset;
    private TextBlock? _calHeader;
    private Button? _calTodayBtn;
    private Grid? _calMonthGridHost;
    private Border? _calUpcomingSection;
    private StackPanel? _todoListHost;
    private TextBox? _todoInputBox;
    private StackPanel? _agendaListHost;

    // Selectable System Telemetry elements
    private TextBlock? _statsHeader;
    private TextBlock? _statsCpuVal;
    private ProgressBar? _statsCpuBar;
    private TextBlock? _statsRamVal;
    private ProgressBar? _statsRamBar;
    private TextBlock? _statsNetVal;
    private TextBlock? _statsDiskVal;
    private TextBlock? _statsGpuVal;
    private TextBlock? _statsThermalVal;
    private TextBlock? _statsUptimeVal;
    private TextBlock? _statsPowerVal;

    // Weather
    private TextBlock? _wxTempText;
    private TextBlock? _wxCityText;
    private TextBlock? _wxConditionText;
    private TextBlock? _wxMetaText;
    private Border? _wxIconHost;
    private string _wxLastRenderedIconKey = string.Empty;

    // Shortcut Dock Bar
    private Panel? _shortcutItemsHost;

    // Video
    private TextBlock? _vidTitleText;
    private TextBlock? _vidStateText;

    public event Action<List<TodoTaskItem>>? TodoListChanged;
    public event Action? RefreshWeatherRequested;
    public event Action? ToggleVideoPlaybackRequested;
    public event Action<DateTime>? CalendarDateClicked;
    public event Action<ShortcutDockItem>? ShortcutLaunchRequested;

    public WidgetContentControl()
    {
        _glassSurface.Content = _outerPaddingHost;
        Content = _glassSurface;
    }

    public WidgetInstance CurrentWidget => _widget;

    public void Bind(
        WidgetInstance widget,
        GlassmorphismStyle effectiveGlass,
        bool isHardwareBackdropActive,
        ImageSource? fallbackBackdropSource = null,
        WeatherSnapshot? weather = null,
        string? weatherStatus = null,
        TelemetrySnapshot? telemetry = null,
        List<TodoTaskItem>? todos = null,
        bool videoPlaying = false,
        string? videoStatus = null,
        List<CalendarAgendaEntry>? calendarEntries = null)
    {
        _widget = widget;
        _currentBoxWidth = Math.Max(140, widget.Width);
        _currentBoxHeight = Math.Max(70, widget.Height);
        _weather = weather;
        _weatherStatus = weatherStatus;
        _telemetry = telemetry;
        if (todos != null) _todos = todos;
        if (calendarEntries != null) _calendarEntries = calendarEntries;
        _videoPlaying = videoPlaying;
        _videoStatus = videoStatus ?? (videoPlaying ? "Looping across displays" : "Paused / Ready");

        _glassSurface.GlassStyle = effectiveGlass;
        _glassSurface.BrightnessMultiplier = widget.BrightnessMultiplier;
        _glassSurface.IsHardwareBackdropActive = isHardwareBackdropActive;
        _glassSurface.FallbackBackdropSource = fallbackBackdropSource;
        _glassSurface.ApplyVisualStyle();

        string sig = BuildVisualSignature(widget, effectiveGlass);
        if (!string.Equals(sig, _builtSignature, StringComparison.Ordinal) || _outerPaddingHost.Child == null)
        {
            _builtSignature = sig;
            BuildBodyForKind();
        }

        ApplyResponsiveContentScaling();
        RefreshDynamicValues();
    }

    public void UpdateCalendarEntries(List<CalendarAgendaEntry> entries)
    {
        _calendarEntries = entries;
        if (_widget.Kind == WidgetKind.CalendarAgenda)
        {
            RebuildCalendarMonthGrid();
            RebuildAgendaRows();
        }
    }

    /// <summary>
    /// Dynamically adjusts fonts, icons, vector dials, and padding when the widget box is resized on desktop.
    /// </summary>
    public void UpdateDimensions(double newWidthDip, double newHeightDip)
    {
        _currentBoxWidth = Math.Max(140, newWidthDip);
        _currentBoxHeight = Math.Max(70, newHeightDip);
        ApplyResponsiveContentScaling();
        if (_widget.Kind == WidgetKind.CalendarAgenda && _calUpcomingSection != null)
        {
            _calUpcomingSection.Visibility = _currentBoxHeight > 365 ? Visibility.Visible : Visibility.Collapsed;
        }
        RefreshDynamicValues();
    }

    public void UpdateGlass(
        GlassmorphismStyle effectiveGlass,
        double brightnessMultiplier,
        bool isHardwareBackdropActive,
        ImageSource? fallbackBackdropSource)
    {
        _glassSurface.GlassStyle = effectiveGlass;
        _glassSurface.BrightnessMultiplier = brightnessMultiplier;
        _glassSurface.IsHardwareBackdropActive = isHardwareBackdropActive;
        _glassSurface.FallbackBackdropSource = fallbackBackdropSource;
        _glassSurface.ApplyVisualStyle();

        string sig = BuildVisualSignature(_widget, effectiveGlass);
        if (!string.Equals(sig, _builtSignature, StringComparison.Ordinal))
        {
            _builtSignature = sig;
            BuildBodyForKind();
        }

        ApplyResponsiveContentScaling();
        RefreshThemeColorsOnText();
    }

    public void TickSecond(TelemetrySnapshot? latestTelemetry)
    {
        if (latestTelemetry != null)
            _telemetry = latestTelemetry;

        if (_focusRunning && _widget.Kind == WidgetKind.FocusTimer)
        {
            if (_focusRemainingSeconds > 0)
            {
                _focusRemainingSeconds--;
            }
            else
            {
                _focusIsBreak = !_focusIsBreak;
                int workMin = ParseIntConfig("WorkMinutes", 25, 1, 180);
                int breakMin = ParseIntConfig("BreakMinutes", 5, 1, 60);
                _focusTotalSeconds = (_focusIsBreak ? breakMin : workMin) * 60;
                _focusRemainingSeconds = _focusTotalSeconds;
            }
        }

        RefreshDynamicValues();
    }

    public void UpdateWeather(WeatherSnapshot? snapshot, string? status)
    {
        _weather = snapshot;
        _weatherStatus = status;
        if (_widget.Kind == WidgetKind.Weather)
            RefreshDynamicValues();
    }

    public void UpdateTodos(List<TodoTaskItem> items)
    {
        _todos = items;
        if (_widget.Kind == WidgetKind.TodoList)
            RebuildTodoRows();
    }

    public void UpdateVideoStatus(bool isPlaying, string statusText)
    {
        _videoPlaying = isPlaying;
        _videoStatus = statusText;
        if (_widget.Kind == WidgetKind.PanoramicVideo)
            RefreshDynamicValues();
    }

    private double ComputeResizeScaleFactor(double baseW, double baseH, double min = 0.55, double max = 6.5)
    {
        double sw = _currentBoxWidth / Math.Max(60, baseW);
        double sh = _currentBoxHeight / Math.Max(50, baseH);
        return Math.Clamp(Math.Min(sw, sh), min, max);
    }

    private string BuildVisualSignature(WidgetInstance w, GlassmorphismStyle glass) =>
        $"{w.Kind}|{glass.SurfaceTheme}|{glass.AccentHex}|" +
        $"{GetConfig("ClockLayout", "Stacked")}|{GetConfig("DayPlacement", "Top")}|{GetConfig("DatePlacement", "Bottom")}|" +
        $"{GetConfig("VerticalAlignment", "Center")}|{GetConfig("Alignment", "Left")}|" +
        $"{GetConfig("FontFamily", "")}|{GetConfig("TimeFontFamily", "")}|{GetConfig("DayFontFamily", "")}|{GetConfig("DateFontFamily", "")}|{GetConfig("GreetingFontFamily", "")}|" +
        $"{GetConfig("TimeFontWeight", "Bold")}|{GetConfig("ClockTextShadow", "0.45")}|" +
        $"{GetConfig("ClockAccentHex", "")}|{GetConfig("TimeUseAccent", "false")}|{GetConfig("DayUseAccent", "true")}|{GetConfig("DateUseAccent", "false")}|{GetConfig("GreetingUseAccent", "false")}|" +
        $"{GetConfig("TimeColorHex", "")}|{GetConfig("DayColorHex", "")}|{GetConfig("DateColorHex", "")}|{GetConfig("GreetingColorHex", "")}|" +
        $"{GetConfig("ShowDay", "true")}|{GetConfig("ShowDate", "true")}|{GetConfig("ShowGreeting", "true")}|{GetConfig("Format24H", "false")}|{GetConfig("ShowSeconds", "false")}|" +
        $"{GetConfig("WeatherIconTheme", "Animated Minimal")}|" +
        $"{GetConfig("ShortcutLayout", "Horizontal")}|{GetConfig("ShortcutIconScale", "1.00")}|{GetConfig("ShortcutItemsJson", "")}|" +
        $"{GetConfig("WorkMinutes", "25")}|{GetConfig("BreakMinutes", "5")}|" +
        $"{GetConfig("AnalogDialStyle", "")}|{GetConfig("StatsLayout", GetConfig("Mode", ""))}|{_calMonthOffset}";

    private FontFamily ResolveFontFamily(string? explicitOverride = null)
    {
        if (!string.IsNullOrWhiteSpace(explicitOverride))
            return new FontFamily(explicitOverride);

        string configured = GetConfig("FontFamily", string.Empty);
        if (!string.IsNullOrWhiteSpace(configured))
            return new FontFamily(configured);

        var surface = (_glassSurface.GlassStyle ?? GlassmorphismStyle.BuiltInPresets[0]).SurfaceTheme;
        return surface switch
        {
            WidgetSurfaceTheme.RetroTerminalHUD => new FontFamily("Cascadia Code, Consolas"),
            WidgetSurfaceTheme.WarmPaperEditorial => new FontFamily("Georgia, Segoe UI"),
            _ => new FontFamily("Segoe UI Variable Display, Segoe UI")
        };
    }

    private FontFamily ResolveElementFont(string elementPrefix) =>
        ResolveFontFamily(GetConfig($"{elementPrefix}FontFamily", string.Empty));

    private double ResolveElementScale(string elementPrefix) =>
        double.TryParse(GetConfig($"{elementPrefix}Scale", "1.0"), NumberStyles.Float, CultureInfo.InvariantCulture, out double s)
            ? Math.Clamp(s, 0.45, 3.0)
            : 1.0;

    private Color ResolveElementColor(string elementPrefix, Color defaultNonAccentColor, bool defaultUseAccent)
    {
        if (GetBoolConfig($"{elementPrefix}UseAccent", defaultUseAccent))
            return AccentColor();

        string hex = GetConfig($"{elementPrefix}ColorHex", string.Empty);
        if (!string.IsNullOrWhiteSpace(hex))
            return GlassSurfaceControl.ParseHex(hex, defaultNonAccentColor, _widget.BrightnessMultiplier);

        return defaultNonAccentColor;
    }

    private FontWeight ResolveTimeFontWeight() => GetConfig("TimeFontWeight", "Bold").ToLowerInvariant() switch
    {
        "thin" => FontWeights.Thin,
        "light" => FontWeights.Light,
        "normal" => FontWeights.Normal,
        "semibold" => FontWeights.SemiBold,
        "black" => FontWeights.Black,
        _ => FontWeights.Bold
    };

    private double ResolveUserFontScale() =>
        double.TryParse(GetConfig("FontScale", "1.0"), NumberStyles.Float, CultureInfo.InvariantCulture, out double fs)
            ? Math.Clamp(fs, 0.45, 5.0)
            : 1.0;

    private HorizontalAlignment ResolveHorizontalAlignment() => GetConfig("Alignment", "Left").ToLowerInvariant() switch
    {
        "center" => HorizontalAlignment.Center,
        "right" => HorizontalAlignment.Right,
        _ => HorizontalAlignment.Left
    };

    private VerticalAlignment ResolveVerticalAlignment() => GetConfig("VerticalAlignment", "Center").ToLowerInvariant() switch
    {
        "top" => VerticalAlignment.Top,
        "bottom" => VerticalAlignment.Bottom,
        _ => VerticalAlignment.Center
    };

    private TextAlignment ResolveTextAlignEnum() => GetConfig("Alignment", "Left").ToLowerInvariant() switch
    {
        "center" => TextAlignment.Center,
        "right" => TextAlignment.Right,
        _ => TextAlignment.Left
    };

    private Color PrimaryTextColor()
    {
        var custom = GetConfig("ClockTextPrimaryHex", string.Empty);
        if (!string.IsNullOrWhiteSpace(custom))
            return GlassSurfaceControl.ParseHex(custom, Colors.White, _widget.BrightnessMultiplier);

        var style = _glassSurface.GlassStyle ?? GlassmorphismStyle.BuiltInPresets[0];
        return GlassSurfaceControl.ParseHex(style.TextPrimaryHex, Color.FromRgb(248, 250, 252), _widget.BrightnessMultiplier);
    }

    private Color SecondaryTextColor()
    {
        var custom = GetConfig("ClockTextSecondaryHex", string.Empty);
        if (!string.IsNullOrWhiteSpace(custom))
            return GlassSurfaceControl.ParseHex(custom, Color.FromRgb(203, 213, 225), _widget.BrightnessMultiplier);

        var style = _glassSurface.GlassStyle ?? GlassmorphismStyle.BuiltInPresets[0];
        return GlassSurfaceControl.ParseHex(style.TextSecondaryHex, Color.FromRgb(203, 213, 225), _widget.BrightnessMultiplier);
    }

    private Color AccentColor()
    {
        var custom = GetConfig("ClockAccentHex", string.Empty);
        if (!string.IsNullOrWhiteSpace(custom))
            return GlassSurfaceControl.ParseHex(custom, Color.FromRgb(96, 165, 250), _widget.BrightnessMultiplier);

        var style = _glassSurface.GlassStyle ?? GlassmorphismStyle.BuiltInPresets[0];
        return GlassSurfaceControl.ParseHex(style.AccentHex, Color.FromRgb(96, 165, 250), _widget.BrightnessMultiplier);
    }

    private void BuildBodyForKind()
    {
        _clockTimeText = null;
        _clockDateText = null;
        _clockDayText = null;
        _clockGreetingText = null;
        _clockTzText = null;
        _analogCanvas = null;
        _analogDialEllipse = null;
        _analogSubTimeText = null;
        _focusCountdownText = null;
        _todoListHost = null;
        _agendaListHost = null;
        _calMonthGridHost = null;
        _calUpcomingSection = null;
        _statsCpuVal = null;
        _wxTempText = null;
        _wxIconHost = null;
        _wxLastRenderedIconKey = string.Empty;
        _shortcutItemsHost = null;
        _vidTitleText = null;

        _outerPaddingHost.Child = _widget.Kind switch
        {
            WidgetKind.DigitalClock => BuildDigitalClock(),
            WidgetKind.AnalogClock => BuildAnalogClock(),
            WidgetKind.Weather => BuildWeatherWidget(),
            WidgetKind.CalendarAgenda => BuildCalendarAgenda(),
            WidgetKind.TodoList => BuildTodoWidget(),
            WidgetKind.FocusTimer => BuildFocusTimer(),
            WidgetKind.SystemStats => BuildSystemStats(),
            WidgetKind.ShortcutBar => BuildShortcutBar(),
            WidgetKind.PanoramicVideo => BuildPanoramicVideoCard(),
            _ => BuildCustomDeclarative()
        };
    }

    private UIElement BuildDigitalClock()
    {
        var hAlign = ResolveHorizontalAlignment();
        var vAlign = ResolveVerticalAlignment();
        var tAlign = ResolveTextAlignEnum();
        string layout = GetConfig("ClockLayout", "Stacked (Header -> Time -> Date)");
        string dayPlace = GetConfig("DayPlacement", "Top");
        string datePlace = GetConfig("DatePlacement", "Bottom");

        _clockGreetingText = new TextBlock { FontFamily = ResolveElementFont("Greeting"), FontWeight = FontWeights.SemiBold, TextAlignment = tAlign, TextWrapping = TextWrapping.NoWrap };
        _clockDayText = new TextBlock { FontFamily = ResolveElementFont("Day"), FontWeight = FontWeights.SemiBold, TextAlignment = tAlign, TextWrapping = TextWrapping.NoWrap };
        _clockTimeText = new TextBlock { FontFamily = ResolveElementFont("Time"), FontWeight = ResolveTimeFontWeight(), TextAlignment = tAlign, TextWrapping = TextWrapping.NoWrap };
        _clockDateText = new TextBlock { FontFamily = ResolveElementFont("Date"), FontWeight = FontWeights.Medium, TextAlignment = tAlign, TextWrapping = TextWrapping.NoWrap };
        _clockTzText = new TextBlock { FontFamily = ResolveFontFamily(), TextAlignment = tAlign, Margin = new Thickness(0, 2, 0, 0) };

        double shadowOp = double.TryParse(GetConfig("ClockTextShadow", "0.45"), NumberStyles.Float, CultureInfo.InvariantCulture, out double sh)
            ? Math.Clamp(sh, 0.0, 1.0) : 0.45;
        if (shadowOp > 0.02)
        {
            DropShadowEffect MakeShadow() => new()
            {
                Color = Colors.Black,
                Opacity = shadowOp,
                BlurRadius = 4 + shadowOp * 16,
                ShadowDepth = 1 + shadowOp * 3,
                RenderingBias = RenderingBias.Performance
            };
            _clockTimeText.Effect = MakeShadow();
            _clockDayText.Effect = MakeShadow();
            _clockDateText.Effect = MakeShadow();
            _clockGreetingText.Effect = MakeShadow();
            _clockTzText.Effect = MakeShadow();
        }

        var container = new StackPanel
        {
            VerticalAlignment = vAlign,
            HorizontalAlignment = hAlign
        };

        if (layout.Contains("Split Horizontal", StringComparison.OrdinalIgnoreCase))
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = hAlign };
            var rightMeta = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(16, 0, 0, 0) };
            rightMeta.Children.Add(_clockDayText);
            rightMeta.Children.Add(_clockDateText);
            rightMeta.Children.Add(_clockGreetingText);
            rightMeta.Children.Add(_clockTzText);

            var divider = new Border
            {
                Width = 2,
                Margin = new Thickness(14, 6, 0, 6),
                Background = new SolidColorBrush(AccentColor()),
                CornerRadius = new CornerRadius(1)
            };

            row.Children.Add(_clockTimeText);
            row.Children.Add(divider);
            row.Children.Add(rightMeta);
            container.Children.Add(row);
            return container;
        }

        if (layout.Contains("Wide Split Header", StringComparison.OrdinalIgnoreCase))
        {
            var grid = new Grid { VerticalAlignment = vAlign };
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var headerDock = new DockPanel();
            DockPanel.SetDock(_clockDayText, Dock.Left);
            DockPanel.SetDock(_clockDateText, Dock.Right);
            headerDock.Children.Add(_clockDayText);
            headerDock.Children.Add(_clockDateText);
            Grid.SetRow(headerDock, 0);

            Grid.SetRow(_clockTimeText, 1);
            var foot = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = hAlign };
            foot.Children.Add(_clockGreetingText);
            foot.Children.Add(_clockTzText);
            Grid.SetRow(foot, 2);

            grid.Children.Add(headerDock);
            grid.Children.Add(_clockTimeText);
            grid.Children.Add(foot);
            return grid;
        }

        // Stacked / Flexible Element Ordering based on DayPlacement & DatePlacement & Layout preset
        var topItems = new StackPanel { HorizontalAlignment = hAlign };
        var midRow = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = hAlign, VerticalAlignment = VerticalAlignment.Center };
        var bottomItems = new StackPanel { HorizontalAlignment = hAlign };

        if (layout.Contains("Date Top", StringComparison.OrdinalIgnoreCase))
        {
            topItems.Children.Add(_clockDateText);
            midRow.Children.Add(_clockTimeText);
            bottomItems.Children.Add(_clockDayText);
            bottomItems.Children.Add(_clockGreetingText);
        }
        else if (layout.Contains("Mond", StringComparison.OrdinalIgnoreCase) || layout.Contains("Typographic", StringComparison.OrdinalIgnoreCase))
        {
            topItems.Children.Add(_clockDayText);
            midRow.Children.Add(_clockTimeText);
            bottomItems.Children.Add(_clockDateText);
            bottomItems.Children.Add(_clockGreetingText);
        }
        else
        {
            // Fine-grained DayPlacement & DatePlacement
            topItems.Children.Add(_clockGreetingText);

            if (dayPlace == "Top") topItems.Children.Add(_clockDayText);
            if (datePlace == "Top") topItems.Children.Add(_clockDateText);

            if (dayPlace == "Left of Time")
            {
                _clockDayText.VerticalAlignment = VerticalAlignment.Center;
                _clockDayText.Margin = new Thickness(0, 0, 12, 0);
                midRow.Children.Add(_clockDayText);
            }
            midRow.Children.Add(_clockTimeText);
            if (dayPlace == "Right of Time")
            {
                _clockDayText.VerticalAlignment = VerticalAlignment.Center;
                _clockDayText.Margin = new Thickness(12, 0, 0, 0);
                midRow.Children.Add(_clockDayText);
            }

            if (dayPlace == "Above Date") bottomItems.Children.Add(_clockDayText);
            if (datePlace == "Bottom" || datePlace == "Inline with Day") bottomItems.Children.Add(_clockDateText);
            if (dayPlace == "Bottom") bottomItems.Children.Add(_clockDayText);
        }

        bottomItems.Children.Add(_clockTzText);

        container.Children.Add(topItems);
        container.Children.Add(midRow);
        container.Children.Add(bottomItems);
        return container;
    }

    private UIElement BuildAnalogClock()
    {
        var stack = new StackPanel { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
        _analogCanvas = new Canvas
        {
            Width = 148,
            Height = 148,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        RebuildAnalogDialGraphics(148);

        _analogSubTimeText = new TextBlock
        {
            FontFamily = ResolveFontFamily(),
            FontSize = 11.5,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 6, 0, 0)
        };

        stack.Children.Add(_analogCanvas);
        stack.Children.Add(_analogSubTimeText);
        return stack;
    }

    private void RebuildAnalogDialGraphics(double size)
    {
        if (_analogCanvas == null) return;
        _analogCanvas.Children.Clear();
        _analogCanvas.Width = size;
        _analogCanvas.Height = size;

        double c = size * 0.5;
        double outerR = c - 4;
        string dialStyle = GetConfig("AnalogDialStyle", "Minimal Ticks");
        bool showNumbers = dialStyle.Contains("Number", StringComparison.OrdinalIgnoreCase);
        bool cleanDots = dialStyle.Contains("Dot", StringComparison.OrdinalIgnoreCase);

        _analogDialEllipse = new Ellipse
        {
            Width = outerR * 2,
            Height = outerR * 2,
            StrokeThickness = Math.Max(1.4, size / 75.0),
            Stroke = new SolidColorBrush(Color.FromArgb(145, SecondaryTextColor().R, SecondaryTextColor().G, SecondaryTextColor().B))
        };
        Canvas.SetLeft(_analogDialEllipse, 4);
        Canvas.SetTop(_analogDialEllipse, 4);
        _analogCanvas.Children.Add(_analogDialEllipse);

        for (int i = 0; i < 12; i++)
        {
            double angle = i * 30.0 * Math.PI / 180.0;
            if (showNumbers && i % 3 == 0)
            {
                string lbl = i == 0 ? "12" : i.ToString(CultureInfo.InvariantCulture);
                double fs = Math.Max(9, size * 0.085);
                var numTb = new TextBlock
                {
                    Text = lbl,
                    FontSize = fs,
                    FontWeight = FontWeights.Bold,
                    Foreground = new SolidColorBrush(PrimaryTextColor())
                };
                Canvas.SetLeft(numTb, c + Math.Sin(angle) * (outerR * 0.78) - fs * 0.55);
                Canvas.SetTop(numTb, c - Math.Cos(angle) * (outerR * 0.78) - fs * 0.65);
                _analogCanvas.Children.Add(numTb);
            }
            else if (cleanDots)
            {
                double dotSize = Math.Max(2, (i % 3 == 0 ? 0.036 : 0.020) * size);
                var dot = new Ellipse
                {
                    Width = dotSize,
                    Height = dotSize,
                    Fill = new SolidColorBrush(i % 3 == 0 ? PrimaryTextColor() : SecondaryTextColor())
                };
                Canvas.SetLeft(dot, c + Math.Sin(angle) * (outerR * 0.85) - dotSize / 2);
                Canvas.SetTop(dot, c - Math.Cos(angle) * (outerR * 0.85) - dotSize / 2);
                _analogCanvas.Children.Add(dot);
            }
            else
            {
                _analogCanvas.Children.Add(new Line
                {
                    X1 = c + Math.Sin(angle) * (outerR * 0.82),
                    Y1 = c - Math.Cos(angle) * (outerR * 0.82),
                    X2 = c + Math.Sin(angle) * (outerR * 0.92),
                    Y2 = c - Math.Cos(angle) * (outerR * 0.92),
                    StrokeThickness = i % 3 == 0 ? Math.Max(1.8, size / 65.0) : Math.Max(1.0, size / 110.0),
                    Stroke = new SolidColorBrush(SecondaryTextColor())
                });
            }
        }

        DateTime now = GetClockLocalTime();
        double secAngle = (now.Second * 6.0) * Math.PI / 180.0;
        double minAngle = ((now.Minute + now.Second / 60.0) * 6.0) * Math.PI / 180.0;
        double hrAngle = (((now.Hour % 12) + now.Minute / 60.0) * 30.0) * Math.PI / 180.0;

        var hrHand = new Line
        {
            X1 = c, Y1 = c,
            X2 = c + Math.Sin(hrAngle) * (outerR * 0.50),
            Y2 = c - Math.Cos(hrAngle) * (outerR * 0.50),
            Stroke = new SolidColorBrush(PrimaryTextColor()),
            StrokeThickness = Math.Max(2.5, size / 36.0),
            StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round
        };
        var minHand = new Line
        {
            X1 = c, Y1 = c,
            X2 = c + Math.Sin(minAngle) * (outerR * 0.72),
            Y2 = c - Math.Cos(minAngle) * (outerR * 0.72),
            Stroke = new SolidColorBrush(PrimaryTextColor()),
            StrokeThickness = Math.Max(1.8, size / 55.0),
            StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round
        };
        _analogCanvas.Children.Add(hrHand);
        _analogCanvas.Children.Add(minHand);

        bool showSecHand = !_widget.MinimalAnimations && GetBoolConfig("ShowSecondHand", true);
        if (showSecHand)
        {
            var secHand = new Line
            {
                X1 = c - Math.Sin(secAngle) * (outerR * 0.14),
                Y1 = c + Math.Cos(secAngle) * (outerR * 0.14),
                X2 = c + Math.Sin(secAngle) * (outerR * 0.80),
                Y2 = c - Math.Cos(secAngle) * (outerR * 0.80),
                Stroke = new SolidColorBrush(AccentColor()),
                StrokeThickness = Math.Max(1.1, size / 100.0),
                StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round
            };
            _analogCanvas.Children.Add(secHand);
        }

        double pivSz = Math.Max(6, size / 18.0);
        var pivot = new Ellipse { Width = pivSz, Height = pivSz, Fill = new SolidColorBrush(AccentColor()) };
        Canvas.SetLeft(pivot, c - pivSz / 2);
        Canvas.SetTop(pivot, c - pivSz / 2);
        _analogCanvas.Children.Add(pivot);
    }

    private UIElement BuildWeatherWidget()
    {
        var ff = ResolveFontFamily();
        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var topBar = new DockPanel();
        _wxCityText = new TextBlock { FontFamily = ff, FontSize = 13.5, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center };
        var refreshBtn = CreateGlassPillButton("Refresh", (_, _) => RefreshWeatherRequested?.Invoke());
        DockPanel.SetDock(refreshBtn, Dock.Right);
        if ((_glassSurface.GlassStyle?.SurfaceTheme ?? WidgetSurfaceTheme.Glassmorphism) == WidgetSurfaceTheme.TransparentMinimal)
            refreshBtn.Visibility = Visibility.Collapsed;
        topBar.Children.Add(refreshBtn);
        topBar.Children.Add(_wxCityText);
        Grid.SetRow(topBar, 0);

        var mid = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 4, 0, 4) };
        _wxIconHost = new Border { Width = 44, Height = 44, Margin = new Thickness(0, 0, 12, 0), VerticalAlignment = VerticalAlignment.Center };
        var tempStack = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        _wxTempText = new TextBlock { FontFamily = ff, FontSize = 32, FontWeight = FontWeights.Bold };
        _wxConditionText = new TextBlock { FontFamily = ff, FontSize = 12.5 };
        tempStack.Children.Add(_wxTempText);
        tempStack.Children.Add(_wxConditionText);
        mid.Children.Add(_wxIconHost);
        mid.Children.Add(tempStack);
        Grid.SetRow(mid, 1);

        _wxMetaText = new TextBlock { FontFamily = ff, FontSize = 11, TextWrapping = TextWrapping.Wrap };
        Grid.SetRow(_wxMetaText, 2);

        root.Children.Add(topBar);
        root.Children.Add(mid);
        root.Children.Add(_wxMetaText);
        return root;
    }

    private UIElement BuildCalendarAgenda()
    {
        var ff = ResolveFontFamily();
        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        // Month Header with Prev < / Month Title / Today / Next > arrows
        var headerDock = new DockPanel { Margin = new Thickness(0, 0, 0, 6) };
        var prevBtn = CreateGlassPillButton("<", (_, _) => { _calMonthOffset--; RebuildCalendarMonthGrid(); });
        prevBtn.Padding = new Thickness(8, 2, 8, 2);
        var nextBtn = CreateGlassPillButton(">", (_, _) => { _calMonthOffset++; RebuildCalendarMonthGrid(); });
        nextBtn.Padding = new Thickness(8, 2, 8, 2);
        _calTodayBtn = CreateGlassPillButton("Today", (_, _) => { _calMonthOffset = 0; RebuildCalendarMonthGrid(); });
        _calTodayBtn.Padding = new Thickness(7, 2, 7, 2);
        _calTodayBtn.Margin = new Thickness(0, 0, 6, 0);

        DockPanel.SetDock(prevBtn, Dock.Left);
        DockPanel.SetDock(nextBtn, Dock.Right);
        DockPanel.SetDock(_calTodayBtn, Dock.Right);

        _calHeader = new TextBlock
        {
            FontFamily = ff,
            FontSize = 13.5,
            FontWeight = FontWeights.Bold,
            Foreground = new SolidColorBrush(PrimaryTextColor()),
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center,
            Cursor = Cursors.Hand,
            ToolTip = "Click to jump back to current month"
        };
        _calHeader.MouseLeftButtonUp += (_, _) => { _calMonthOffset = 0; RebuildCalendarMonthGrid(); };

        headerDock.Children.Add(prevBtn);
        headerDock.Children.Add(nextBtn);
        headerDock.Children.Add(_calTodayBtn);
        headerDock.Children.Add(_calHeader);
        Grid.SetRow(headerDock, 0);

        // Unified 7-column Grid (Row 0 = Mo..Su weekday header, Rows 1..6 = 42 day cells)
        // Sharing ColumnDefinitions guarantees exact column-for-column center alignment.
        _calMonthGridHost = new Grid();
        for (int c = 0; c < 7; c++)
            _calMonthGridHost.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        _calMonthGridHost.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        for (int r = 0; r < 6; r++)
            _calMonthGridHost.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        Grid.SetRow(_calMonthGridHost, 1);

        // Expanded Upcoming Agenda Section (automatically appears when user resizes widget taller > 365px)
        _agendaListHost = new StackPanel();
        var agendaScroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, MaxHeight = 140, Content = _agendaListHost };
        var upcomingStack = new StackPanel();
        upcomingStack.Children.Add(new TextBlock
        {
            Text = "UPCOMING EVENTS & REMINDERS",
            FontFamily = ff,
            FontSize = 9.5,
            FontWeight = FontWeights.Bold,
            Foreground = new SolidColorBrush(AccentColor()),
            Margin = new Thickness(0, 0, 0, 4)
        });
        upcomingStack.Children.Add(agendaScroll);
        _calUpcomingSection = new Border
        {
            Margin = new Thickness(0, 8, 0, 0),
            Padding = new Thickness(0, 8, 0, 0),
            BorderBrush = new SolidColorBrush(Color.FromArgb(60, 255, 255, 255)),
            BorderThickness = new Thickness(0, 1, 0, 0),
            Child = upcomingStack,
            Visibility = _currentBoxHeight > 365 ? Visibility.Visible : Visibility.Collapsed
        };
        Grid.SetRow(_calUpcomingSection, 2);

        root.Children.Add(headerDock);
        root.Children.Add(_calMonthGridHost);
        root.Children.Add(_calUpcomingSection);

        RebuildCalendarMonthGrid();
        RebuildAgendaRows();
        return root;
    }

    private UIElement BuildTodoWidget()
    {
        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var header = new TextBlock
        {
            Text = _widget.Title,
            FontFamily = ResolveFontFamily(),
            FontSize = 13.5,
            FontWeight = FontWeights.Bold,
            Foreground = new SolidColorBrush(PrimaryTextColor()),
            Margin = new Thickness(0, 0, 0, 8)
        };
        Grid.SetRow(header, 0);

        _todoListHost = new StackPanel();
        var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = _todoListHost };
        Grid.SetRow(scroll, 1);

        var addRow = new Grid { Margin = new Thickness(0, 8, 0, 0) };
        addRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        addRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        _todoInputBox = CreateGlassTextBox("New task...");
        _todoInputBox.KeyDown += (_, e) => { if (e.Key == Key.Enter) AddTodoFromInput(); };
        var addBtn = CreateGlassPillButton("Add", (_, _) => AddTodoFromInput());
        Grid.SetColumn(_todoInputBox, 0);
        Grid.SetColumn(addBtn, 1);
        addRow.Children.Add(_todoInputBox);
        addRow.Children.Add(addBtn);
        Grid.SetRow(addRow, 2);

        root.Children.Add(header);
        root.Children.Add(scroll);
        root.Children.Add(addRow);

        RebuildTodoRows();
        return root;
    }

    private UIElement BuildFocusTimer()
    {
        var ff = ResolveFontFamily();
        int workMin = ParseIntConfig("WorkMinutes", 25, 1, 180);
        if (!_focusRunning && !_focusIsBreak)
        {
            _focusTotalSeconds = workMin * 60;
            _focusRemainingSeconds = _focusTotalSeconds;
        }

        var stack = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        _focusPhaseText = new TextBlock { FontFamily = ff, FontSize = 11.5, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 2) };
        _focusCountdownText = new TextBlock { FontFamily = ff, FontSize = 34, FontWeight = FontWeights.Bold };
        _focusProgress = new ProgressBar
        {
            Height = 5, Minimum = 0, Maximum = 100, Margin = new Thickness(0, 8, 0, 10),
            Background = new SolidColorBrush(Color.FromArgb(65, 255, 255, 255))
        };

        var btnRow = new StackPanel { Orientation = Orientation.Horizontal };
        _focusToggleBtn = CreateGlassPillButton("Start", (_, _) => { _focusRunning = !_focusRunning; RefreshDynamicValues(); });
        _focusResetBtn = CreateGlassPillButton("Reset", (_, _) =>
        {
            _focusRunning = false;
            _focusIsBreak = false;
            int wMin = ParseIntConfig("WorkMinutes", 25, 1, 120);
            _focusTotalSeconds = wMin * 60;
            _focusRemainingSeconds = _focusTotalSeconds;
            RefreshDynamicValues();
        });
        _focusResetBtn.Margin = new Thickness(8, 0, 0, 0);
        btnRow.Children.Add(_focusToggleBtn);
        btnRow.Children.Add(_focusResetBtn);

        stack.Children.Add(_focusPhaseText);
        stack.Children.Add(_focusCountdownText);
        stack.Children.Add(_focusProgress);
        stack.Children.Add(btnRow);
        return stack;
    }

    private UIElement BuildSystemStats()
    {
        var ff = ResolveFontFamily();
        string layout = GetConfig("StatsLayout", GetConfig("Mode", "Detailed"));
        bool showBars = !string.Equals(layout, "MinimalHUD", StringComparison.OrdinalIgnoreCase);

        var stack = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        _statsHeader = new TextBlock { Text = _widget.Title, FontFamily = ff, FontSize = 13, FontWeight = FontWeights.Bold, Margin = new Thickness(0, 0, 0, 6) };
        stack.Children.Add(_statsHeader);

        _statsCpuVal = new TextBlock { FontFamily = ff, FontSize = 11.5, FontWeight = FontWeights.SemiBold };
        _statsCpuBar = new ProgressBar { Height = 5, Minimum = 0, Maximum = 100, Margin = new Thickness(0, 2, 0, 6), Background = new SolidColorBrush(Color.FromArgb(55, 255, 255, 255)), Visibility = showBars ? Visibility.Visible : Visibility.Collapsed };
        _statsRamVal = new TextBlock { FontFamily = ff, FontSize = 11.5, FontWeight = FontWeights.SemiBold };
        _statsRamBar = new ProgressBar { Height = 5, Minimum = 0, Maximum = 100, Margin = new Thickness(0, 2, 0, 6), Background = new SolidColorBrush(Color.FromArgb(55, 255, 255, 255)), Visibility = showBars ? Visibility.Visible : Visibility.Collapsed };
        _statsNetVal = new TextBlock { FontFamily = ff, FontSize = 11, Margin = new Thickness(0, 1, 0, 2) };
        _statsDiskVal = new TextBlock { FontFamily = ff, FontSize = 11, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 1, 0, 2) };
        _statsGpuVal = new TextBlock { FontFamily = ff, FontSize = 11, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 1, 0, 2) };
        _statsThermalVal = new TextBlock { FontFamily = ff, FontSize = 11, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 1, 0, 2) };
        _statsUptimeVal = new TextBlock { FontFamily = ff, FontSize = 11, Margin = new Thickness(0, 1, 0, 2) };
        _statsPowerVal = new TextBlock { FontFamily = ff, FontSize = 11, Margin = new Thickness(0, 1, 0, 0) };

        stack.Children.Add(_statsCpuVal);
        stack.Children.Add(_statsCpuBar);
        stack.Children.Add(_statsRamVal);
        stack.Children.Add(_statsRamBar);
        stack.Children.Add(_statsNetVal);
        stack.Children.Add(_statsDiskVal);
        stack.Children.Add(_statsGpuVal);
        stack.Children.Add(_statsThermalVal);
        stack.Children.Add(_statsUptimeVal);
        stack.Children.Add(_statsPowerVal);

        return stack;
    }

    private UIElement BuildShortcutBar()
    {
        string layout = GetConfig("ShortcutLayout", "Horizontal");
        bool isVertical = string.Equals(layout, "Vertical", StringComparison.OrdinalIgnoreCase);

        var items = ShortcutDockConfigHelper.ParseItems(_widget.Config);
        Color accent = AccentColor();
        Color primary = PrimaryTextColor();
        Color secondary = SecondaryTextColor();

        if (items.Count == 0)
        {
            return new Border
            {
                Child = new TextBlock
                {
                    Text = "Empty Dock — click Edit to add apps, folders, or websites",
                    FontFamily = ResolveFontFamily(),
                    FontSize = 11.5,
                    Foreground = new SolidColorBrush(secondary),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    TextWrapping = TextWrapping.Wrap,
                    TextAlignment = TextAlignment.Center
                }
            };
        }

        double scale = ComputeResizeScaleFactor(items.Count == 1 ? 120 : 320, 90, 0.72, 2.2);
        double iconScale = double.TryParse(GetConfig("ShortcutIconScale", "1.00"), NumberStyles.Float, CultureInfo.InvariantCulture, out double scVal)
            ? Math.Clamp(scVal, 0.45, 2.50)
            : 1.00;
        double tileSize = Math.Clamp(46 * scale * iconScale, 20, 150);

        var host = new WrapPanel
        {
            Orientation = isVertical ? Orientation.Vertical : Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        _shortcutItemsHost = host;

        bool darkSurface = IsEffectiveDockSurfaceDark();
        Color tooltipTextColor = darkSurface ? Colors.White : Colors.Black;
        Color tooltipFeatherColor = darkSurface ? Color.FromArgb(200, 0, 0, 0) : Color.FromArgb(180, 255, 255, 255);

        foreach (var item in items)
        {
            Color tileColor = accent;
            if (!string.IsNullOrWhiteSpace(item.CustomColorHex))
                tileColor = GlassSurfaceControl.ParseHex(item.CustomColorHex, accent, _widget.BrightnessMultiplier);

            var iconVisualGrid = new Grid { Width = tileSize, Height = tileSize };

            // Determine visual icon mode: CustomIcon | Auto App/Folder Icon | Initials | GlassGlyph
            ImageSource? customBmp = string.Equals(item.IconMode, "CustomIcon", StringComparison.OrdinalIgnoreCase)
                ? IconExtractorHelper.LoadCustomIconImage(item.CustomIconPath)
                : null;
            ImageSource? extracted = customBmp == null && string.Equals(item.IconMode, "Auto", StringComparison.OrdinalIgnoreCase)
                ? IconExtractorHelper.TryExtractAppIcon(item.TargetPathOrUrl)
                : null;
            ImageSource? imgSrc = customBmp ?? extracted;

            bool isPureCustomIcon = customBmp != null;

            if (imgSrc != null)
            {
                var img = new Image
                {
                    Source = imgSrc,
                    Width = isPureCustomIcon ? tileSize * 0.88 : tileSize * 0.70,
                    Height = isPureCustomIcon ? tileSize * 0.88 : tileSize * 0.70,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    Stretch = Stretch.Uniform
                };
                RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.HighQuality);
                iconVisualGrid.Children.Add(img);
            }
            else if (string.Equals(item.IconMode, "Initials", StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(item.IconMode, "CustomIcon", StringComparison.OrdinalIgnoreCase))
            {
                string mono = string.IsNullOrWhiteSpace(item.Initials)
                    ? (item.Label.Length >= 2 ? item.Label[..2].ToUpperInvariant() : "SC")
                    : item.Initials.Trim().ToUpperInvariant();
                if (mono.Length > 3) mono = mono[..3];

                iconVisualGrid.Children.Add(new TextBlock
                {
                    Text = mono,
                    FontFamily = ResolveFontFamily(),
                    FontSize = Math.Clamp(tileSize * (mono.Length > 2 ? 0.30 : 0.36), 10, 48),
                    FontWeight = FontWeights.Bold,
                    Foreground = new SolidColorBrush(Colors.White),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                });
            }
            else
            {
                string pathData = IconExtractorHelper.GetVectorGlyphData(item.GlyphKey);
                iconVisualGrid.Children.Add(new Viewbox
                {
                    Width = tileSize * 0.50,
                    Height = tileSize * 0.50,
                    Child = new System.Windows.Shapes.Path
                    {
                        Data = Geometry.Parse(pathData),
                        Stroke = new SolidColorBrush(primary),
                        StrokeThickness = 1.9,
                        Fill = string.Equals(item.GlyphKey, "Play", StringComparison.OrdinalIgnoreCase)
                            ? new SolidColorBrush(primary)
                            : Brushes.Transparent,
                        Width = 24,
                        Height = 24,
                        Stretch = Stretch.Uniform
                    }
                });
            }

            var tileBg = isPureCustomIcon
                ? Colors.Transparent
                : string.Equals(item.IconMode, "Initials", StringComparison.OrdinalIgnoreCase)
                    ? Color.FromArgb(165, tileColor.R, tileColor.G, tileColor.B)
                    : Color.FromArgb(45, 255, 255, 255);

            var tileBtn = new Button
            {
                Width = tileSize,
                Height = tileSize,
                Margin = new Thickness(5),
                Padding = new Thickness(0),
                Background = isPureCustomIcon ? Brushes.Transparent : new SolidColorBrush(tileBg),
                BorderBrush = isPureCustomIcon ? Brushes.Transparent : new SolidColorBrush(Color.FromArgb(115, tileColor.R, tileColor.G, tileColor.B)),
                BorderThickness = new Thickness(isPureCustomIcon ? 0.0 : 1.2),
                Cursor = Cursors.Hand,
                Content = iconVisualGrid
            };

            // Rounded pill template cleanly matching squircle glass aesthetic
            var tpl = new ControlTemplate(typeof(Button));
            var borderFactory = new FrameworkElementFactory(typeof(Border));
            borderFactory.SetValue(Border.CornerRadiusProperty, new CornerRadius(Math.Round(tileSize * 0.28)));
            borderFactory.SetBinding(Border.BackgroundProperty, new System.Windows.Data.Binding(nameof(Button.Background)) { RelativeSource = System.Windows.Data.RelativeSource.TemplatedParent });
            borderFactory.SetBinding(Border.BorderBrushProperty, new System.Windows.Data.Binding(nameof(Button.BorderBrush)) { RelativeSource = System.Windows.Data.RelativeSource.TemplatedParent });
            borderFactory.SetBinding(Border.BorderThicknessProperty, new System.Windows.Data.Binding(nameof(Button.BorderThickness)) { RelativeSource = System.Windows.Data.RelativeSource.TemplatedParent });
            var cp = new FrameworkElementFactory(typeof(ContentPresenter));
            borderFactory.AppendChild(cp);
            tpl.VisualTree = borderFactory;
            tileBtn.Template = tpl;

            // Boxless floating hover label directly ABOVE icon (White on dark backgrounds, Black on light backgrounds)
            var tipTpl = new ControlTemplate(typeof(ToolTip));
            tipTpl.VisualTree = new FrameworkElementFactory(typeof(ContentPresenter));
            var tip = new ToolTip
            {
                Placement = System.Windows.Controls.Primitives.PlacementMode.Top,
                VerticalOffset = -4,
                Background = Brushes.Transparent,
                BorderBrush = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                HasDropShadow = false,
                Padding = new Thickness(0),
                Template = tipTpl,
                Content = new TextBlock
                {
                    Text = item.Label,
                    FontFamily = ResolveFontFamily(),
                    FontSize = 12,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = new SolidColorBrush(tooltipTextColor),
                    Effect = new DropShadowEffect
                    {
                        Color = tooltipFeatherColor,
                        BlurRadius = 6,
                        ShadowDepth = 1,
                        Opacity = 0.85
                    }
                }
            };
            ToolTipService.SetToolTip(tileBtn, tip);
            ToolTipService.SetInitialShowDelay(tileBtn, 70);
            ToolTipService.SetShowDuration(tileBtn, 8000);

            var captured = item;
            tileBtn.MouseEnter += (_, _) => tileBtn.Opacity = 0.86;
            tileBtn.MouseLeave += (_, _) => tileBtn.Opacity = 1.0;
            tileBtn.Click += (_, _) => ShortcutLaunchRequested?.Invoke(captured);

            host.Children.Add(tileBtn);
        }

        return new Border { Child = host };
    }

    private bool IsEffectiveDockSurfaceDark()
    {
        var style = _glassSurface.GlassStyle ?? GlassmorphismStyle.BuiltInPresets[0];
        if (style.SurfaceTheme == WidgetSurfaceTheme.Glassmorphism && _glassSurface.FallbackBackdropSource is System.Windows.Media.Imaging.BitmapSource bmp && style.BackgroundOpacity < 0.35)
        {
            try
            {
                int w = Math.Max(1, Math.Min(32, bmp.PixelWidth));
                int h = Math.Max(1, Math.Min(32, bmp.PixelHeight));
                byte[] pixels = new byte[bmp.PixelWidth * bmp.PixelHeight * 4];
                int stride = bmp.PixelWidth * 4;
                if (bmp.Format == PixelFormats.Bgra32 || bmp.Format == PixelFormats.Pbgra32 || bmp.Format == PixelFormats.Bgr32)
                {
                    bmp.CopyPixels(pixels, stride, 0);
                    double totalLum = 0;
                    int count = 0;
                    int stepY = Math.Max(1, bmp.PixelHeight / h);
                    int stepX = Math.Max(1, bmp.PixelWidth / w);
                    for (int y = 0; y < bmp.PixelHeight; y += stepY)
                    {
                        for (int x = 0; x < bmp.PixelWidth; x += stepX)
                        {
                            int idx = y * stride + x * 4;
                            totalLum += 0.114 * pixels[idx] + 0.587 * pixels[idx + 1] + 0.299 * pixels[idx + 2];
                            count++;
                        }
                    }
                    if (count > 0)
                        return (totalLum / count) < 138;
                }
            }
            catch { }
        }

        Color p = PrimaryTextColor();
        return (0.299 * p.R + 0.587 * p.G + 0.114 * p.B) >= 140;
    }

    private UIElement BuildAmbientGeometry()
    {
        return BuildShortcutBar();
    }

    private UIElement BuildPanoramicVideoCard()
    {
        var stack = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        _vidTitleText = new TextBlock { FontFamily = ResolveFontFamily(), FontSize = 13.5, FontWeight = FontWeights.Bold };
        _vidStateText = new TextBlock { FontFamily = ResolveFontFamily(), FontSize = 12, Margin = new Thickness(0, 4, 0, 8), TextWrapping = TextWrapping.Wrap };
        var btn = CreateGlassPillButton("Toggle Play / Pause", (_, _) => ToggleVideoPlaybackRequested?.Invoke());

        stack.Children.Add(_vidTitleText);
        stack.Children.Add(_vidStateText);
        stack.Children.Add(btn);
        return stack;
    }

    private UIElement BuildCustomDeclarative()
    {
        var ff = ResolveFontFamily();
        var stack = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        stack.Children.Add(new TextBlock
        {
            Text = _widget.Title, FontFamily = ff, FontSize = 15, FontWeight = FontWeights.Bold,
            Foreground = new SolidColorBrush(PrimaryTextColor()), Margin = new Thickness(0, 0, 0, 4)
        });

        string subtitle = GetConfig("Subtitle", GetConfig("CustomText", "Declarative Package Widget (Sandboxed)"));
        stack.Children.Add(new TextBlock
        {
            Text = subtitle, FontFamily = ff, FontSize = 12, TextWrapping = TextWrapping.Wrap,
            Foreground = new SolidColorBrush(SecondaryTextColor()), Margin = new Thickness(0, 0, 0, 6)
        });

        foreach (var kv in _widget.Config.Where(k => !k.Key.Equals("Subtitle", StringComparison.OrdinalIgnoreCase)).Take(4))
        {
            stack.Children.Add(new TextBlock { Text = $"{kv.Key}: {kv.Value}", FontFamily = ff, FontSize = 11, Foreground = new SolidColorBrush(AccentColor()) });
        }

        return stack;
    }

    private void ApplyResponsiveContentScaling()
    {
        double padScale = ComputeResizeScaleFactor(340, 170, 0.70, 2.2);
        _outerPaddingHost.Padding = new Thickness(Math.Round(15 * padScale), Math.Round(12 * padScale), Math.Round(15 * padScale), Math.Round(12 * padScale));

        switch (_widget.Kind)
        {
            case WidgetKind.DigitalClock when _clockTimeText != null:
            {
                double autoBoxScale = ComputeResizeScaleFactor(340, 155, 0.52, 7.0);
                double userScale = ResolveUserFontScale();
                double baseTimePx = double.TryParse(GetConfig("ClockTimeBaseSize", "40"), NumberStyles.Float, CultureInfo.InvariantCulture, out double bt)
                    ? Math.Clamp(bt, 16, 220) : 40.0;

                double effectiveScale = autoBoxScale * userScale;
                _clockTimeText.FontSize = Math.Clamp(baseTimePx * effectiveScale * ResolveElementScale("Time"), 12, 280);
                _clockTimeText.FontWeight = ResolveTimeFontWeight();
                _clockDayText!.FontSize = Math.Clamp(13.5 * effectiveScale * ResolveElementScale("Day"), 8, 120);
                _clockDateText!.FontSize = Math.Clamp(13.0 * effectiveScale * ResolveElementScale("Date"), 8, 120);
                _clockGreetingText!.FontSize = Math.Clamp(11.5 * effectiveScale * ResolveElementScale("Greeting"), 8, 100);
                _clockTzText!.FontSize = Math.Clamp(10.5 * effectiveScale, 8, 48);
                break;
            }

            case WidgetKind.AnalogClock when _analogCanvas != null:
            {
                bool showDate = GetBoolConfig("ShowDate", true);
                double availDial = Math.Clamp(Math.Min(_currentBoxWidth - 34, _currentBoxHeight - (showDate ? 48 : 26)), 74, 720);
                RebuildAnalogDialGraphics(availDial);
                if (_analogSubTimeText != null)
                {
                    _analogSubTimeText.FontSize = Math.Clamp(availDial * 0.085, 9.5, 36);
                }
                break;
            }

            case WidgetKind.Weather when _wxTempText != null:
            {
                double s = ComputeResizeScaleFactor(340, 170, 0.65, 3.6);
                _wxCityText!.FontSize = Math.Clamp(13.5 * s, 10, 42);
                double iconSz = Math.Clamp(44.0 * s, 26, 130);
                _wxIconHost!.Width = iconSz;
                _wxIconHost!.Height = iconSz;
                _wxTempText.FontSize = Math.Clamp(32.0 * s, 16, 110);
                _wxConditionText!.FontSize = Math.Clamp(12.5 * s, 9.5, 38);
                _wxMetaText!.FontSize = Math.Clamp(11.0 * s, 9, 28);
                break;
            }

            case WidgetKind.FocusTimer when _focusCountdownText != null:
            {
                double s = ComputeResizeScaleFactor(320, 175, 0.65, 3.8);
                _focusPhaseText!.FontSize = Math.Clamp(11.5 * s, 9, 32);
                _focusCountdownText.FontSize = Math.Clamp(34.0 * s, 18, 118);
                _focusProgress!.Height = Math.Clamp(5.0 * s, 3, 14);
                break;
            }

            case WidgetKind.SystemStats when _statsCpuVal != null:
            {
                double s = ComputeResizeScaleFactor(340, 225, 0.72, 2.4);
                _statsHeader!.FontSize = Math.Clamp(13.0 * s, 10, 30);
                _statsCpuVal.FontSize = Math.Clamp(11.5 * s, 9.5, 26);
                _statsRamVal!.FontSize = Math.Clamp(11.5 * s, 9.5, 26);
                _statsCpuBar!.Height = Math.Clamp(5.0 * s, 3, 12);
                _statsRamBar!.Height = Math.Clamp(5.0 * s, 3, 12);
                _statsNetVal!.FontSize = Math.Clamp(11.0 * s, 9, 24);
                _statsDiskVal!.FontSize = Math.Clamp(11.0 * s, 9, 24);
                _statsGpuVal!.FontSize = Math.Clamp(11.0 * s, 9, 24);
                _statsThermalVal!.FontSize = Math.Clamp(11.0 * s, 9, 24);
                _statsUptimeVal!.FontSize = Math.Clamp(11.0 * s, 9, 24);
                _statsPowerVal!.FontSize = Math.Clamp(11.0 * s, 9, 24);
                break;
            }

            case WidgetKind.CalendarAgenda when _calHeader != null:
            {
                double s = ComputeResizeScaleFactor(340, 320, 0.78, 2.0);
                _calHeader.FontSize = Math.Clamp(13.5 * s, 10.5, 28);
                RebuildCalendarMonthGrid();
                break;
            }

            case WidgetKind.ShortcutBar when _shortcutItemsHost != null:
            {
                _outerPaddingHost.Child = BuildShortcutBar();
                break;
            }

            case WidgetKind.PanoramicVideo when _vidTitleText != null:
            {
                double s = ComputeResizeScaleFactor(320, 150, 0.72, 2.6);
                _vidTitleText.FontSize = Math.Clamp(13.5 * s, 10, 32);
                _vidStateText!.FontSize = Math.Clamp(12.0 * s, 9.5, 28);
                break;
            }
        }
    }

    private DateTime GetClockLocalTime()
    {
        string tzId = GetConfig("TimeZoneId", string.Empty);
        if (string.IsNullOrWhiteSpace(tzId) || string.Equals(tzId, "Local", StringComparison.OrdinalIgnoreCase))
            return DateTime.Now;

        try
        {
            var tz = TimeZoneInfo.FindSystemTimeZoneById(tzId);
            return TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, tz);
        }
        catch
        {
            return DateTime.Now;
        }
    }

    private void RefreshDynamicValues()
    {
        Color primary = PrimaryTextColor();
        Color secondary = SecondaryTextColor();
        Color accent = AccentColor();

        switch (_widget.Kind)
        {
            case WidgetKind.DigitalClock when _clockTimeText != null:
            {
                bool f24 = GetBoolConfig("Format24H", false);
                bool showAmPm = GetBoolConfig("ShowAmPm", true);
                bool showSec = GetBoolConfig("ShowSeconds", false);
                bool showDate = GetBoolConfig("ShowDate", true);
                bool showDay = GetBoolConfig("ShowDay", true);
                bool showGreet = GetBoolConfig("ShowGreeting", true);
                string dateFmt = GetConfig("DateFormat", "dddd, MMM d yyyy");
                string dayStyle = GetConfig("DayCaseStyle", "Spaced Uppercase");
                string tzId = GetConfig("TimeZoneId", string.Empty);

                DateTime now = GetClockLocalTime();
                string fmt = f24
                    ? (showSec ? "HH:mm:ss" : "HH:mm")
                    : (showSec ? (showAmPm ? "hh:mm:ss tt" : "hh:mm:ss") : (showAmPm ? "hh:mm tt" : "hh:mm"));

                _clockTimeText.Text = now.ToString(fmt, CultureInfo.InvariantCulture);
                _clockTimeText.Foreground = new SolidColorBrush(ResolveElementColor("Time", primary, defaultUseAccent: false));

                string dayName = now.ToString("dddd", CultureInfo.InvariantCulture);
                _clockDayText!.Visibility = showDay && GetConfig("DayPlacement", "Top") != "Hidden"
                    ? Visibility.Visible : Visibility.Collapsed;
                _clockDayText.Text = dayStyle switch
                {
                    "Spaced Uppercase" => string.Join(" ", dayName.ToUpperInvariant().ToCharArray()),
                    "Uppercase" => dayName.ToUpperInvariant(),
                    "Short Upper" => now.ToString("ddd", CultureInfo.InvariantCulture).ToUpperInvariant(),
                    _ => dayName
                };
                _clockDayText.Foreground = new SolidColorBrush(ResolveElementColor("Day", accent, defaultUseAccent: true));

                _clockDateText!.Visibility = showDate && GetConfig("DatePlacement", "Bottom") != "Hidden"
                    ? Visibility.Visible : Visibility.Collapsed;
                try { _clockDateText.Text = now.ToString(dateFmt, CultureInfo.InvariantCulture); }
                catch { _clockDateText.Text = now.ToString("dddd, MMM d yyyy", CultureInfo.InvariantCulture); }
                _clockDateText.Foreground = new SolidColorBrush(ResolveElementColor("Date", secondary, defaultUseAccent: false));

                if (showGreet)
                {
                    _clockGreetingText!.Visibility = Visibility.Visible;
                    string customGreet = GetConfig("CustomGreeting", string.Empty);
                    string autoGreet = now.Hour switch
                    {
                        < 12 => "GOOD MORNING",
                        < 17 => "GOOD AFTERNOON",
                        < 21 => "GOOD EVENING",
                        _ => "NIGHT MODE"
                    };
                    _clockGreetingText.Text = string.IsNullOrWhiteSpace(customGreet) ? autoGreet : customGreet;
                    _clockGreetingText.Foreground = new SolidColorBrush(ResolveElementColor("Greeting", secondary, defaultUseAccent: false));
                }
                else
                {
                    _clockGreetingText!.Visibility = Visibility.Collapsed;
                }

                if (!string.IsNullOrWhiteSpace(tzId) && !string.Equals(tzId, "Local", StringComparison.OrdinalIgnoreCase))
                {
                    _clockTzText!.Visibility = Visibility.Visible;
                    _clockTzText.Text = tzId;
                    _clockTzText.Foreground = new SolidColorBrush(secondary);
                }
                else
                {
                    _clockTzText!.Visibility = Visibility.Collapsed;
                }
                break;
            }

            case WidgetKind.AnalogClock when _analogCanvas != null:
            {
                DateTime now = GetClockLocalTime();
                RebuildAnalogDialGraphics(_analogCanvas.Width);

                bool showSub = GetBoolConfig("ShowDate", true);
                _analogSubTimeText!.Visibility = showSub ? Visibility.Visible : Visibility.Collapsed;
                string tz = GetConfig("TimeZoneId", string.Empty);
                _analogSubTimeText.Text = string.IsNullOrWhiteSpace(tz) || tz == "Local"
                    ? now.ToString("ddd, MMM d", CultureInfo.InvariantCulture)
                    : $"{now:HH:mm} • {tz}";
                _analogSubTimeText.Foreground = new SolidColorBrush(secondary);
                break;
            }

            case WidgetKind.Weather when _wxTempText != null:
            {
                _wxCityText!.Foreground = new SolidColorBrush(primary);
                _wxTempText.Foreground = new SolidColorBrush(primary);
                _wxConditionText!.Foreground = new SolidColorBrush(secondary);
                _wxMetaText!.Foreground = new SolidColorBrush(secondary);

                bool showFeels = GetBoolConfig("WeatherShowFeelsLike", true);
                bool showHum = GetBoolConfig("WeatherShowHumidity", true);
                bool showWind = GetBoolConfig("WeatherShowWind", true);
                bool showProv = GetBoolConfig("WeatherShowProvider", true);
                string iconTheme = GetConfig("WeatherIconTheme", "Animated Minimal");

                if (_weather != null)
                {
                    _wxCityText.Text = _weather.CityDisplay;
                    RenderWeatherIconVisual(iconTheme, InferWeatherWmoCode(_weather.ConditionText), _weather.ConditionIcon, primary, accent);
                    _wxTempText.Text = $"{_weather.Temperature:0.#}{_weather.UnitSymbol}";
                    _wxConditionText.Text = _weather.ConditionText;

                    var metaParts = new List<string>();
                    if (showFeels) metaParts.Add($"Feels {_weather.ApparentTemperature:0.#}{_weather.UnitSymbol}");
                    if (showHum) metaParts.Add($"Humidity {_weather.HumidityPercent}%");
                    if (showWind) metaParts.Add($"Wind {_weather.WindSpeedKmh:0.#} km/h");
                    if (showProv) metaParts.Add(_weather.ProviderName);
                    _wxMetaText.Text = string.Join("  •  ", metaParts);
                }
                else
                {
                    _wxCityText.Text = GetConfig("City", "Lahore");
                    RenderWeatherIconVisual(iconTheme, 2, "☁", primary, accent);
                    _wxTempText.Text = "--°";
                    _wxConditionText.Text = _weatherStatus ?? "Waiting for weather sync...";
                    _wxMetaText.Text = $"{GetConfig("Provider", "OpenMeteo")} • Open Edit to change City / Icon Theme";
                }
                break;
            }

            case WidgetKind.FocusTimer when _focusCountdownText != null:
            {
                int mins = _focusRemainingSeconds / 60;
                int secs = _focusRemainingSeconds % 60;
                _focusPhaseText!.Text = _focusIsBreak ? "BREAK SESSION" : "DEEP FOCUS";
                _focusPhaseText.Foreground = new SolidColorBrush(accent);
                _focusCountdownText.Text = $"{mins:00}:{secs:00}";
                _focusCountdownText.Foreground = new SolidColorBrush(primary);
                double pct = _focusTotalSeconds > 0
                    ? ((_focusTotalSeconds - _focusRemainingSeconds) / (double)_focusTotalSeconds) * 100.0 : 0;
                _focusProgress!.Value = pct;
                _focusProgress.Foreground = new SolidColorBrush(accent);
                if (_focusToggleBtn != null) _focusToggleBtn.Content = _focusRunning ? "Pause" : "Start";
                break;
            }

            case WidgetKind.SystemStats when _statsCpuVal != null:
            {
                bool showCpu = GetBoolConfig("ShowCpu", true);
                bool showRam = GetBoolConfig("ShowRam", true);
                bool showNet = GetBoolConfig("ShowNet", true);
                bool showDisk = GetBoolConfig("ShowDisk", true);
                bool showAllDrives = GetBoolConfig("ShowAllDrives", false);
                bool showGpu = GetBoolConfig("ShowGpu", true);
                bool showTemps = GetBoolConfig("ShowTemps", true);
                bool showUptime = GetBoolConfig("ShowUptime", false);
                bool showPower = GetBoolConfig("ShowPower", false);
                bool showBars = !string.Equals(GetConfig("StatsLayout", GetConfig("Mode", "Detailed")), "MinimalHUD", StringComparison.OrdinalIgnoreCase);

                _statsHeader!.Foreground = new SolidColorBrush(primary);
                _statsCpuVal.Foreground = new SolidColorBrush(primary);
                _statsRamVal!.Foreground = new SolidColorBrush(primary);
                _statsCpuBar!.Foreground = new SolidColorBrush(accent);
                _statsRamBar!.Foreground = new SolidColorBrush(accent);
                _statsNetVal!.Foreground = new SolidColorBrush(secondary);
                _statsDiskVal!.Foreground = new SolidColorBrush(secondary);
                _statsGpuVal!.Foreground = new SolidColorBrush(secondary);
                _statsThermalVal!.Foreground = new SolidColorBrush(secondary);
                _statsUptimeVal!.Foreground = new SolidColorBrush(secondary);
                _statsPowerVal!.Foreground = new SolidColorBrush(secondary);

                _statsCpuVal.Visibility = showCpu ? Visibility.Visible : Visibility.Collapsed;
                _statsCpuBar.Visibility = showCpu && showBars ? Visibility.Visible : Visibility.Collapsed;
                _statsRamVal.Visibility = showRam ? Visibility.Visible : Visibility.Collapsed;
                _statsRamBar.Visibility = showRam && showBars ? Visibility.Visible : Visibility.Collapsed;
                _statsNetVal.Visibility = showNet ? Visibility.Visible : Visibility.Collapsed;
                _statsDiskVal.Visibility = showDisk ? Visibility.Visible : Visibility.Collapsed;
                _statsGpuVal.Visibility = showGpu ? Visibility.Visible : Visibility.Collapsed;
                _statsThermalVal.Visibility = showTemps ? Visibility.Visible : Visibility.Collapsed;
                _statsUptimeVal.Visibility = showUptime ? Visibility.Visible : Visibility.Collapsed;
                _statsPowerVal.Visibility = showPower ? Visibility.Visible : Visibility.Collapsed;

                if (_telemetry != null)
                {
                    _statsCpuVal.Text = $"CPU  {_telemetry.CpuUsagePercent:0}% ({Environment.ProcessorCount} threads)";
                    _statsCpuBar.Value = _telemetry.CpuUsagePercent;
                    _statsRamVal.Text = $"RAM  {_telemetry.RamUsedGb:0.0} / {_telemetry.RamTotalGb:0.0} GB ({_telemetry.RamUsagePercent:0}%)";
                    _statsRamBar.Value = _telemetry.RamUsagePercent;
                    _statsNetVal.Text = $"Net  ↓ {_telemetry.NetworkDownKbps:0.0} KB/s   ↑ {_telemetry.NetworkUpKbps:0.0} KB/s";

                    int driveTake = showAllDrives ? 6 : 2;
                    _statsDiskVal.Text = _telemetry.Drives.Count > 0
                        ? string.Join("   ", _telemetry.Drives.Take(driveTake).Select(d => $"{d.Name} {d.UsedGb:0}/{d.TotalGb:0} GB ({d.UsagePercent:0}%)"))
                        : "Storage: Unavailable";

                    _statsGpuVal.Text = $"GPU: {_telemetry.GpuName}";

                    string cpuT = _telemetry.CpuTemperatureC.HasValue ? $"{_telemetry.CpuTemperatureC:0}°C" : "N/A";
                    string gpuT = _telemetry.GpuTemperatureC.HasValue ? $"{_telemetry.GpuTemperatureC:0}°C" : "N/A";
                    _statsThermalVal.Text = $"Temps: CPU {cpuT} • GPU {gpuT} ({_telemetry.ThermalStatusNote})";

                    var up = _telemetry.SystemUptime;
                    _statsUptimeVal.Text = $"Uptime: {(int)up.TotalHours}h {up.Minutes:00}m {up.Seconds:00}s";
                    _statsPowerVal.Text = $"Power: {_telemetry.PowerSourceText}";
                }
                else
                {
                    _statsCpuVal.Text = "CPU  Sampling...";
                    _statsRamVal.Text = "RAM  Sampling...";
                }
                break;
            }

            case WidgetKind.PanoramicVideo when _vidTitleText != null:
            {
                string path = GetConfig("VideoPath", string.Empty);
                _vidTitleText.Text = string.IsNullOrWhiteSpace(path)
                    ? "Panoramic Video Backdrop"
                    : System.IO.Path.GetFileName(path);
                _vidTitleText.Foreground = new SolidColorBrush(primary);
                _vidStateText!.Text = $"{(_videoPlaying ? "Playing" : "Paused")}  •  {_videoStatus}";
                _vidStateText.Foreground = new SolidColorBrush(secondary);
                break;
            }
        }
    }

    private static int InferWeatherWmoCode(string condition)
    {
        if (string.IsNullOrWhiteSpace(condition)) return 2;
        string c = condition.ToLowerInvariant();
        if (c.Contains("thunder") || c.Contains("storm")) return 95;
        if (c.Contains("snow") || c.Contains("sleet") || c.Contains("blizzard")) return 71;
        if (c.Contains("rain") || c.Contains("drizzle") || c.Contains("shower")) return 61;
        if (c.Contains("clear") || c.Contains("sun")) return 0;
        return 2;
    }

    private void RefreshWeatherVisualIcon(string theme, int wmoCode, string fallbackEmoji, Color primary, Color accent)
    {
        RenderWeatherIconVisual(theme, wmoCode, fallbackEmoji, primary, accent);
    }

    private void RenderWeatherIconVisual(string theme, int wmoCode, string fallbackEmoji, Color primary, Color accent)
    {
        if (_wxIconHost == null) return;
        string key = $"{theme}|{wmoCode}|{primary}|{accent}|{_widget.MinimalAnimations}";
        if (string.Equals(_wxLastRenderedIconKey, key, StringComparison.Ordinal) && _wxIconHost.Child != null)
            return;
        _wxLastRenderedIconKey = key;

        if (string.Equals(theme, "Classic Emoji", StringComparison.OrdinalIgnoreCase))
        {
            _wxIconHost.Child = new TextBlock
            {
                Text = fallbackEmoji,
                FontSize = 32,
                Foreground = new SolidColorBrush(accent),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            return;
        }

        bool animated = !string.Equals(theme, "Minimal Mono", StringComparison.OrdinalIgnoreCase) && !_widget.MinimalAnimations;
        var canvas = new Canvas { Width = 44, Height = 44, ClipToBounds = false };
        var vb = new Viewbox { Child = canvas, Stretch = Stretch.Uniform };

        // Classify WMO code: 0 clear, 1-3 cloudy, 45-48 fog, 51-67/80-82 rain, 71-77/85-86 snow, 95+ storm
        if (wmoCode == 0)
        {
            var sunColor = theme.Contains("Mono", StringComparison.OrdinalIgnoreCase) ? primary : Color.FromRgb(251, 191, 36);
            var raysGroup = new Canvas { Width = 44, Height = 44 };
            var centerCore = new Ellipse
            {
                Width = 16, Height = 16,
                Stroke = new SolidColorBrush(sunColor), StrokeThickness = 2.2,
                Fill = new SolidColorBrush(Color.FromArgb(65, sunColor.R, sunColor.G, sunColor.B))
            };
            Canvas.SetLeft(centerCore, 14);
            Canvas.SetTop(centerCore, 14);
            raysGroup.Children.Add(centerCore);

            for (int i = 0; i < 8; i++)
            {
                double ang = i * Math.PI / 4.0;
                raysGroup.Children.Add(new Line
                {
                    X1 = 22 + Math.Cos(ang) * 12,
                    Y1 = 22 + Math.Sin(ang) * 12,
                    X2 = 22 + Math.Cos(ang) * 17,
                    Y2 = 22 + Math.Sin(ang) * 17,
                    Stroke = new SolidColorBrush(sunColor),
                    StrokeThickness = 2.1,
                    StrokeStartLineCap = PenLineCap.Round,
                    StrokeEndLineCap = PenLineCap.Round
                });
            }

            if (animated)
            {
                var rt = new RotateTransform(0, 22, 22);
                raysGroup.RenderTransform = rt;
                rt.BeginAnimation(RotateTransform.AngleProperty, new DoubleAnimation(0, 360, TimeSpan.FromSeconds(14))
                {
                    RepeatBehavior = RepeatBehavior.Forever
                });
            }
            canvas.Children.Add(raysGroup);
        }
        else if (wmoCode >= 95)
        {
            var cloud = BuildCloudPath(primary, yOffset: 0);
            canvas.Children.Add(cloud);

            var bolt = new System.Windows.Shapes.Path
            {
                Data = Geometry.Parse("M23,24 L18,33 L23,33 L20,41 L29,30 L24,30 Z"),
                Fill = new SolidColorBrush(Color.FromRgb(250, 204, 21))
            };
            if (animated)
            {
                var flash = new DoubleAnimation(1.0, 0.25, TimeSpan.FromSeconds(0.9))
                {
                    AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever
                };
                bolt.BeginAnimation(UIElement.OpacityProperty, flash);
            }
            canvas.Children.Add(bolt);
        }
        else if (wmoCode is (>= 51 and <= 67) or (>= 80 and <= 82))
        {
            var cloud = BuildCloudPath(primary, yOffset: -2);
            canvas.Children.Add(cloud);
            Color dropColor = theme.Contains("Mono", StringComparison.OrdinalIgnoreCase) ? accent : Color.FromRgb(56, 189, 248);

            for (int i = 0; i < 3; i++)
            {
                var drop = new Line
                {
                    X1 = 14 + i * 7, Y1 = 27,
                    X2 = 12 + i * 7, Y2 = 34,
                    Stroke = new SolidColorBrush(dropColor),
                    StrokeThickness = 2.0,
                    StrokeStartLineCap = PenLineCap.Round,
                    StrokeEndLineCap = PenLineCap.Round
                };
                if (animated)
                {
                    var tt = new TranslateTransform();
                    drop.RenderTransform = tt;
                    tt.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(-2, 7, TimeSpan.FromSeconds(0.85))
                    {
                        BeginTime = TimeSpan.FromSeconds(i * 0.28),
                        RepeatBehavior = RepeatBehavior.Forever
                    });
                    drop.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0.95, 0.15, TimeSpan.FromSeconds(0.85))
                    {
                        BeginTime = TimeSpan.FromSeconds(i * 0.28),
                        RepeatBehavior = RepeatBehavior.Forever
                    });
                }
                canvas.Children.Add(drop);
            }
        }
        else if (wmoCode is (>= 71 and <= 77) or 85 or 86)
        {
            var cloud = BuildCloudPath(primary, yOffset: -2);
            canvas.Children.Add(cloud);
            for (int i = 0; i < 3; i++)
            {
                var flake = new Ellipse { Width = 4, Height = 4, Fill = new SolidColorBrush(accent) };
                Canvas.SetLeft(flake, 13 + i * 8);
                Canvas.SetTop(flake, 28);
                if (animated)
                {
                    var tt = new TranslateTransform();
                    flake.RenderTransform = tt;
                    tt.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(0, 9, TimeSpan.FromSeconds(1.4))
                    {
                        BeginTime = TimeSpan.FromSeconds(i * 0.4),
                        RepeatBehavior = RepeatBehavior.Forever
                    });
                }
                canvas.Children.Add(flake);
            }
        }
        else
        {
            var mainCloud = BuildCloudPath(primary, yOffset: 3);
            var subCloud = BuildCloudPath(accent, yOffset: -3, opacity: 0.55);
            if (animated)
            {
                var tt = new TranslateTransform();
                subCloud.RenderTransform = tt;
                tt.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(-2.5, 3.0, TimeSpan.FromSeconds(3.2))
                {
                    AutoReverse = true,
                    RepeatBehavior = RepeatBehavior.Forever,
                    EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut }
                });
            }
            canvas.Children.Add(subCloud);
            canvas.Children.Add(mainCloud);
        }

        _wxIconHost.Child = vb;
    }

    private static System.Windows.Shapes.Path BuildCloudPath(Color strokeColor, double yOffset = 0, double opacity = 0.95)
    {
        var p = new System.Windows.Shapes.Path
        {
            Data = Geometry.Parse("M12,26 H31 A6,6 0 0 0 32,14.5 A7.5,7.5 0 0 0 18,12 A6,6 0 0 0 12,26 Z"),
            Stroke = new SolidColorBrush(strokeColor),
            StrokeThickness = 2.0,
            Fill = new SolidColorBrush(Color.FromArgb(38, strokeColor.R, strokeColor.G, strokeColor.B)),
            Opacity = opacity
        };
        Canvas.SetTop(p, yOffset);
        return p;
    }

    private void RefreshThemeColorsOnText()
    {
        RefreshDynamicValues();
        if (_widget.Kind == WidgetKind.TodoList) RebuildTodoRows();
        else if (_widget.Kind == WidgetKind.CalendarAgenda)
        {
            RebuildCalendarMonthGrid();
            RebuildAgendaRows();
        }
    }

    private void RebuildTodoRows()
    {
        if (_todoListHost == null) return;
        _todoListHost.Children.Clear();
        Color primary = PrimaryTextColor();
        Color secondary = SecondaryTextColor();

        if (_todos.Count == 0)
        {
            _todoListHost.Children.Add(new TextBlock { Text = "No tasks yet. Add one below.", FontSize = 12, Foreground = new SolidColorBrush(secondary) });
            return;
        }

        foreach (var item in _todos)
        {
            var row = new Grid { Margin = new Thickness(0, 2, 0, 2) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var cb = new CheckBox
            {
                IsChecked = item.IsCompleted,
                Content = new TextBlock
                {
                    Text = item.Text, TextWrapping = TextWrapping.Wrap, FontSize = 12,
                    TextDecorations = item.IsCompleted ? TextDecorations.Strikethrough : null,
                    Foreground = new SolidColorBrush(item.IsCompleted ? secondary : primary)
                }
            };
            cb.Checked += (_, _) => { item.IsCompleted = true; TodoListChanged?.Invoke(_todos.ToList()); RebuildTodoRows(); };
            cb.Unchecked += (_, _) => { item.IsCompleted = false; TodoListChanged?.Invoke(_todos.ToList()); RebuildTodoRows(); };

            var del = new Button { Content = "×", Background = Brushes.Transparent, BorderThickness = new Thickness(0), Foreground = new SolidColorBrush(secondary), Padding = new Thickness(6, 0, 4, 0), Cursor = Cursors.Hand };
            del.Click += (_, _) => { _todos.RemoveAll(t => t.Id == item.Id); TodoListChanged?.Invoke(_todos.ToList()); RebuildTodoRows(); };

            Grid.SetColumn(cb, 0);
            Grid.SetColumn(del, 1);
            row.Children.Add(cb);
            row.Children.Add(del);
            _todoListHost.Children.Add(row);
        }
    }

    private void AddTodoFromInput()
    {
        if (_todoInputBox == null) return;
        string text = _todoInputBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(text)) return;

        _todos.Add(new TodoTaskItem { Text = text });
        _todoInputBox.Text = string.Empty;
        TodoListChanged?.Invoke(_todos.ToList());
        RebuildTodoRows();
    }

    private void RebuildCalendarMonthGrid()
    {
        if (_calMonthGridHost == null || _calHeader == null) return;
        _calMonthGridHost.Children.Clear();

        DateTime today = DateTime.Today;
        DateTime displayMonth = new DateTime(today.Year, today.Month, 1).AddMonths(_calMonthOffset);
        _calHeader.Text = displayMonth.ToString("MMMM yyyy", CultureInfo.InvariantCulture);
        if (_calTodayBtn != null)
            _calTodayBtn.Visibility = _calMonthOffset == 0 ? Visibility.Collapsed : Visibility.Visible;

        var entryDates = new HashSet<string>(_calendarEntries.Select(e => e.DateIso), StringComparer.OrdinalIgnoreCase);
        var cells = CalendarHolidayHelper.BuildMondayFirstMonthGrid(displayMonth.Year, displayMonth.Month, today);
        var ff = ResolveFontFamily();
        Color primary = PrimaryTextColor();
        Color secondary = SecondaryTextColor();
        Color accent = AccentColor();

        double calScale = ComputeResizeScaleFactor(340, 320, 0.78, 2.0);
        double badgeSize = Math.Clamp(Math.Round(30 * calScale), 24, 52);
        bool darkSurface = IsEffectiveDockSurfaceDark();

        string[] dayAbbr = { "Mo", "Tu", "We", "Th", "Fr", "Sa", "Su" };
        for (int col = 0; col < 7; col++)
        {
            bool isWknd = col >= 5;
            var hdr = new TextBlock
            {
                Text = dayAbbr[col],
                FontFamily = ff,
                FontSize = Math.Clamp(10.5 * calScale, 9.5, 19),
                FontWeight = FontWeights.SemiBold,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 0, 5),
                Foreground = new SolidColorBrush(isWknd ? Color.FromRgb(251, 146, 60) : secondary)
            };
            Grid.SetRow(hdr, 0);
            Grid.SetColumn(hdr, col);
            _calMonthGridHost.Children.Add(hdr);
        }

        for (int idx = 0; idx < cells.Count; idx++)
        {
            var cell = cells[idx];
            string iso = cell.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            bool hasEntries = entryDates.Contains(iso);
            bool isHoliday = !string.IsNullOrWhiteSpace(cell.HolidayName);

            Color bg = cell.IsToday
                ? Color.FromArgb(darkSurface ? (byte)102 : (byte)130, accent.R, accent.G, accent.B)
                : isHoliday && cell.IsCurrentMonth
                    ? Color.FromArgb(46, 245, 158, 11)
                    : cell.IsWeekend && cell.IsCurrentMonth
                        ? Color.FromArgb(26, 251, 146, 60)
                        : Colors.Transparent;

            Color strokeColor = cell.IsToday
                ? Color.FromArgb(
                    175,
                    (byte)Math.Min(255, accent.R + 65),
                    (byte)Math.Min(255, accent.G + 65),
                    (byte)Math.Min(255, accent.B + 65))
                : isHoliday && cell.IsCurrentMonth
                    ? Color.FromArgb(145, 251, 191, 36)
                    : Colors.Transparent;

            Color fg = cell.IsToday
                ? Colors.White
                : !cell.IsCurrentMonth
                    ? Color.FromArgb(95, secondary.R, secondary.G, secondary.B)
                    : isHoliday
                        ? Color.FromRgb(253, 224, 71)
                        : cell.IsWeekend
                            ? Color.FromRgb(253, 186, 116)
                            : primary;

            // Smooth anti-aliased WPF vector Ellipse (avoids jagged pixel-snapped Border corners)
            var circleGrid = new Grid
            {
                Width = badgeSize,
                Height = badgeSize,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                UseLayoutRounding = false,
                SnapsToDevicePixels = false
            };

            if (bg.A > 0 || strokeColor.A > 0)
            {
                circleGrid.Children.Add(new Ellipse
                {
                    Fill = bg.A > 0 ? new SolidColorBrush(bg) : Brushes.Transparent,
                    Stroke = strokeColor.A > 0 ? new SolidColorBrush(strokeColor) : Brushes.Transparent,
                    StrokeThickness = cell.IsToday ? 1.15 : (isHoliday ? 1.0 : 0.0),
                    UseLayoutRounding = false,
                    SnapsToDevicePixels = false
                });
            }

            circleGrid.Children.Add(new TextBlock
            {
                Text = cell.Date.Day.ToString(CultureInfo.InvariantCulture),
                FontFamily = ff,
                FontSize = Math.Clamp((cell.IsToday ? 12.2 : 11.4) * calScale, 10, 22),
                FontWeight = cell.IsToday ? FontWeights.Bold : (isHoliday ? FontWeights.Bold : FontWeights.Medium),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = hasEntries || isHoliday ? new Thickness(0, 0, 0, 3) : new Thickness(0),
                Foreground = new SolidColorBrush(fg)
            });

            if (hasEntries || isHoliday)
            {
                circleGrid.Children.Add(new Ellipse
                {
                    Width = 3.5,
                    Height = 3.5,
                    Margin = new Thickness(0, 0, 0, 2.5),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Bottom,
                    UseLayoutRounding = false,
                    SnapsToDevicePixels = false,
                    Fill = new SolidColorBrush(cell.IsToday ? Color.FromArgb(220, 255, 255, 255) : (isHoliday ? Color.FromRgb(251, 191, 36) : accent))
                });
            }

            var dayBtn = new Button
            {
                Background = Brushes.Transparent,
                BorderBrush = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Padding = new Thickness(0),
                Margin = new Thickness(0),
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Center,
                VerticalContentAlignment = VerticalAlignment.Center,
                Cursor = Cursors.Hand,
                Content = circleGrid,
                ToolTip = isHoliday
                    ? $"{cell.HolidayName} • Click to add Event / Reminder / Note"
                    : "Click date to add Event, Reminder, or Note"
            };

            var tpl = new ControlTemplate(typeof(Button));
            var hitBorder = new FrameworkElementFactory(typeof(Border));
            hitBorder.SetValue(Border.BackgroundProperty, Brushes.Transparent);
            var cp = new FrameworkElementFactory(typeof(ContentPresenter));
            cp.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center);
            cp.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
            hitBorder.AppendChild(cp);
            tpl.VisualTree = hitBorder;
            dayBtn.Template = tpl;

            var clickedDate = cell.Date;
            dayBtn.Click += (_, _) => CalendarDateClicked?.Invoke(clickedDate);
            Grid.SetRow(dayBtn, 1 + (idx / 7));
            Grid.SetColumn(dayBtn, idx % 7);
            _calMonthGridHost.Children.Add(dayBtn);
        }
    }

    private void RebuildAgendaRows()
    {
        if (_agendaListHost == null) return;
        _agendaListHost.Children.Clear();

        Color primary = PrimaryTextColor();
        Color secondary = SecondaryTextColor();
        Color accent = AccentColor();

        string todayIso = DateTime.Today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var upcoming = _calendarEntries
            .Where(e => string.CompareOrdinal(e.DateIso, todayIso) >= 0 && !e.IsDismissed)
            .OrderBy(e => e.DateIso)
            .ThenBy(e => e.StartTimeText)
            .Take(6)
            .ToList();

        if (upcoming.Count == 0)
        {
            _agendaListHost.Children.Add(new TextBlock
            {
                Text = "Click any date above to add an Event, Reminder, or Note.",
                FontSize = 11,
                TextWrapping = TextWrapping.Wrap,
                Foreground = new SolidColorBrush(secondary)
            });
            return;
        }

        foreach (var entry in upcoming)
        {
            var tb = new TextBlock
            {
                Text = string.IsNullOrWhiteSpace(entry.StartTimeText)
                    ? $"{entry.DateIso} · {entry.Title} ({entry.Kind})"
                    : $"{entry.DateIso} {entry.StartTimeText} · {entry.Title} ({entry.Kind})",
                FontSize = 11.5,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 1.5, 0, 1.5),
                Foreground = new SolidColorBrush(entry.Kind == CalendarAgendaEntryKind.Reminder ? accent : primary)
            };
            _agendaListHost.Children.Add(tb);
        }
    }

    private Button CreateGlassPillButton(string label, RoutedEventHandler onClick)
    {
        var btn = new Button
        {
            Content = label, Padding = new Thickness(10, 4, 10, 4), FontSize = 11.5, FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(PrimaryTextColor()),
            Background = new SolidColorBrush(Color.FromArgb(46, 255, 255, 255)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(90, 255, 255, 255)),
            BorderThickness = new Thickness(1), Cursor = Cursors.Hand
        };
        btn.Click += onClick;
        return btn;
    }

    private TextBox CreateGlassTextBox(string placeholder) => new()
    {
        ToolTip = placeholder, FontSize = 12, Padding = new Thickness(8, 4, 8, 4), Margin = new Thickness(0, 0, 6, 0),
        Foreground = new SolidColorBrush(PrimaryTextColor()),
        Background = new SolidColorBrush(Color.FromArgb(40, 255, 255, 255)),
        BorderBrush = new SolidColorBrush(Color.FromArgb(85, 255, 255, 255)),
        BorderThickness = new Thickness(1)
    };

    private string GetConfig(string key, string fallback) =>
        _widget.Config.TryGetValue(key, out var val) && !string.IsNullOrWhiteSpace(val) ? val : fallback;

    private bool GetBoolConfig(string key, bool fallback) =>
        _widget.Config.TryGetValue(key, out var val) && bool.TryParse(val, out bool b) ? b : fallback;

    private int ParseIntConfig(string key, int fallback, int min, int max) =>
        int.TryParse(GetConfig(key, fallback.ToString(CultureInfo.InvariantCulture)), out int v)
            ? Math.Clamp(v, min, max) : fallback;
}
