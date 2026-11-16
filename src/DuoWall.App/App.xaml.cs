using System.Windows;
using DuoWall.App.Services;
using DuoWall.App.ViewModels;
using DuoWall.App.Windows;
using DuoWall.Core.Engine;
using DuoWall.Core.Gallery;
using DuoWall.Core.Packages;
using DuoWall.Core.Services;
using DuoWall.Core.Storage;
using DuoWall.Core.Theming;
using Microsoft.Extensions.DependencyInjection;
using Wpf.Ui.Appearance;

namespace DuoWall.App;

public partial class App : Application
{
    private const string SingleInstanceMutexName = "DuoWallBySRMD_SingleInstance_Mutex";
    private const string SingleInstanceEventName = "DuoWallBySRMD_ShowMainWindow_Event";

    private static Mutex? _singleInstanceMutex;
    private static EventWaitHandle? _singleInstanceEvent;
    private static Thread? _singleInstanceThread;

    private ServiceProvider? _services;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        DispatcherUnhandledException += (_, args) =>
        {
            LogCrash(args.Exception);
            MessageBox.Show(
                $"An unexpected error occurred:\n\n{args.Exception.Message}",
                "DuoWall by SRMD",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            args.Handled = true;
        };

        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            if (args.ExceptionObject is Exception ex)
                LogCrash(ex);
        };

        bool isCliTool = e.Args.Any(a =>
            string.Equals(a, "--capture-screenshots", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(a, "--diagnostics", StringComparison.OrdinalIgnoreCase));

        // Single instance check: if another instance is already running, signal it and exit immediately
        bool isFirstInstance = true;
        if (!isCliTool)
        {
            try
            {
                _singleInstanceMutex = new Mutex(true, SingleInstanceMutexName, out isFirstInstance);
            }
            catch
            {
                isFirstInstance = false;
            }

            if (!isFirstInstance)
            {
                try
                {
                    using var showEvent = EventWaitHandle.OpenExisting(SingleInstanceEventName);
                    showEvent.Set();
                }
                catch { }

                Shutdown(0);
                return;
            }
        }

        // Primary instance: listen for wake signals from subsequent launches
        if (!isCliTool)
        {
            try
            {
                _singleInstanceEvent = new EventWaitHandle(false, EventResetMode.AutoReset, SingleInstanceEventName);
                _singleInstanceThread = new Thread(() =>
                {
                    while (_singleInstanceEvent != null)
                    {
                        try
                        {
                            if (_singleInstanceEvent.WaitOne())
                            {
                                Current?.Dispatcher?.BeginInvoke(() =>
                                {
                                    var trayMgr = _services?.GetService<SystemTrayManager>();
                                    if (trayMgr != null)
                                    {
                                        trayMgr.RestoreMainWindow();
                                    }
                                    else if (MainWindow != null)
                                    {
                                        if (!MainWindow.IsVisible) MainWindow.Show();
                                        if (MainWindow.WindowState == WindowState.Minimized) MainWindow.WindowState = WindowState.Normal;
                                        MainWindow.Activate();
                                        MainWindow.Focus();
                                    }
                                });
                            }
                        }
                        catch
                        {
                            break;
                        }
                    }
                })
                {
                    IsBackground = true,
                    Name = "DuoWall_SingleInstance_Listener"
                };
                _singleInstanceThread.Start();
            }
            catch { }
        }

        bool startupApply = e.Args.Any(a => string.Equals(a, "--startup-apply", StringComparison.OrdinalIgnoreCase));
        int diagIdx = Array.FindIndex(e.Args, a => string.Equals(a, "--diagnostics", StringComparison.OrdinalIgnoreCase));
        if (diagIdx >= 0)
        {
            string? outPath = diagIdx + 1 < e.Args.Length ? e.Args[diagIdx + 1] : null;
            await RunDiagnosticsAsync(outPath);
            Shutdown(0);
            return;
        }

        var sc = new ServiceCollection();
        sc.AddSingleton<DuoWallDatabase>(_ => new DuoWallDatabase());
        sc.AddSingleton<Win32DisplayDetector>();
        sc.AddSingleton<DesktopWallpaperApplier>();
        sc.AddSingleton<MonitorWallpaperSampler>();
        sc.AddSingleton<WallpaperGeometryEngine>();
        sc.AddSingleton<SkiaWallpaperCompositor>();
        sc.AddSingleton<IWallpaperSourceProvider, UltraWideWallpapersProvider>();
        sc.AddSingleton<WallpaperLibraryService>();
        sc.AddSingleton<StartupAndWatcherService>();
        sc.AddSingleton<WeatherService>();
        sc.AddSingleton<WallpaperColorAnalyzer>();
        sc.AddSingleton<WidgetPackageService>();
        sc.AddSingleton<LocalWidgetMarketplaceProvider>();
        sc.AddSingleton<GitHubReleaseService>();
        sc.AddSingleton<YouTubeWallpaperService>();
        sc.AddSingleton<TaskbarTweaksService>();
        sc.AddSingleton<DesktopOverlayCoordinator>();
        sc.AddSingleton<SystemTrayManager>();
        sc.AddSingleton<MainViewModel>();
        sc.AddSingleton<MainWindow>();

        _services = sc.BuildServiceProvider();

        var vm = _services.GetRequiredService<MainViewModel>();
        ApplicationThemeManager.Apply(
            vm.IsDarkTheme ? ApplicationTheme.Dark : ApplicationTheme.Light);

        var mainWindow = _services.GetRequiredService<MainWindow>();
        mainWindow.DataContext = vm;
        mainWindow.AttachCoordinator(_services.GetRequiredService<DesktopOverlayCoordinator>());

        var trayManager = _services.GetRequiredService<SystemTrayManager>();
        trayManager.Initialize(mainWindow, vm);
        mainWindow.AttachTrayManager(trayManager);

        if (startupApply)
        {
            mainWindow.WindowState = WindowState.Minimized;
            mainWindow.ShowInTaskbar = true;
        }

        MainWindow = mainWindow;
        mainWindow.Show();

        int shotIdx = Array.FindIndex(e.Args, a => string.Equals(a, "--capture-screenshots", StringComparison.OrdinalIgnoreCase));
        await vm.InitializeAsync(startupApply || shotIdx >= 0);

        if (shotIdx >= 0)
        {
            string outDir = shotIdx + 1 < e.Args.Length ? e.Args[shotIdx + 1] : "docs/screenshots";
            await CaptureScreenshotsAsync(mainWindow, vm, outDir);
            Shutdown(0);
            return;
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        try
        {
            _services?.GetService<SystemTrayManager>()?.Dispose();
        }
        catch { }

        try
        {
            _singleInstanceEvent?.Dispose();
            _singleInstanceEvent = null;
            _singleInstanceMutex?.Dispose();
            _singleInstanceMutex = null;
        }
        catch { }

        _services?.Dispose();
        base.OnExit(e);
    }

    private static void LogCrash(Exception ex)
    {
        try
        {
            string logDir = System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "DuoWallBySRMD", "Logs");
            System.IO.Directory.CreateDirectory(logDir);
            string logFile = System.IO.Path.Combine(logDir, "error.log");
            System.IO.File.AppendAllText(logFile, $"[{DateTime.UtcNow:O}] {ex}\n\n");
        }
        catch { }
    }

    private static async Task RunDiagnosticsAsync(string? outPath)
    {
        var sb = new System.Text.StringBuilder();
        var detector = new Win32DisplayDetector();
        var monitors = detector.DetectMonitors();
        sb.AppendLine($"DetectedMonitors={monitors.Count}");
        foreach (var m in monitors)
        {
            sb.AppendLine($"  {m.DisplayLabel} | Mfr={m.Manufacturer} | Dev={m.DeviceName} | Bounds={m.Bounds} | Phys={m.PhysicalWidthMm}x{m.PhysicalHeightMm}mm (HW={m.HasHardwarePhysicalDimensions}) | DPI={m.DpiX} ({m.ScalePercent}%) | Hz={m.RefreshRateHz} | Primary={m.IsPrimary}");
        }

        var applier = new DesktopWallpaperApplier();
        var backup = applier.CaptureCurrentWallpaperState();
        sb.AppendLine($"IDesktopWallpaperBackupCount={backup.MonitorWallpapers.Count}, Position={backup.DesktopPosition}");
        foreach (var kv in backup.MonitorWallpapers)
            sb.AppendLine($"  COM={kv.Key} -> {kv.Value}");

        using var provider = new UltraWideWallpapersProvider();
        var page = await provider.QueryGalleryAsync(new DuoWall.Core.Models.GalleryQuery { Limit = 5 });
        sb.AppendLine($"LiveGalleryCount={page.Items.Count}, TotalKnown={page.TotalKnownCount}");
        foreach (var item in page.Items)
            sb.AppendLine($"  Item={item.FileName} | Title={item.Title} | Tags={string.Join(",", item.Tags)}");

        bool vlcCoreOk = VideoFrameExtractor.InitializeLibVlcCore();
        bool vlcNewOk = false;
        if (vlcCoreOk)
        {
            try
            {
                using var testVlc = new LibVLCSharp.Shared.LibVLC("--no-audio", "--quiet");
                vlcNewOk = true;
            }
            catch { }
        }
        sb.AppendLine($"LibVlcCoreOk={vlcCoreOk}, LibVlcInstanceOk={vlcNewOk}");

        if (!string.IsNullOrWhiteSpace(outPath))
            await System.IO.File.WriteAllTextAsync(outPath, sb.ToString());
    }

    private static async Task CaptureScreenshotsAsync(MainWindow window, MainViewModel vm, string outDir)
    {
        System.IO.Directory.CreateDirectory(outDir);
        window.WindowState = WindowState.Normal;
        window.Width = 1360;
        window.Height = 860;
        window.UpdateLayout();
        await Task.Delay(1200);

        // 1. Home / Gallery View
        vm.CurrentPageIndex = 0;
        window.UpdateLayout();
        await Task.Delay(800);
        SaveElementToPng(window, System.IO.Path.Combine(outDir, "01_wallpaper_gallery.png"), 1360, 860);

        // 2. Placement Studio
        vm.CurrentPageIndex = 2;
        window.UpdateLayout();
        await Task.Delay(800);
        SaveElementToPng(window, System.IO.Path.Combine(outDir, "02_placement_studio.png"), 1360, 860);

        // 3. Widget Marketplace
        vm.CurrentPageIndex = 6;
        vm.WidgetStudio.StudioSectionIndex = 2;
        window.UpdateLayout();
        await Task.Delay(800);
        SaveElementToPng(window, System.IO.Path.Combine(outDir, "03_widget_marketplace.png"), 1360, 860);

        // 4. Taskbar Tweaks
        vm.CurrentPageIndex = 9;
        window.UpdateLayout();
        await Task.Delay(800);
        SaveElementToPng(window, System.IO.Path.Combine(outDir, "04_taskbar_tweaks.png"), 1360, 860);

        // 5. About Me Dialog
        var about = new AboutMeDialogWindow
        {
            Owner = window
        };
        about.Show();
        about.UpdateLayout();
        await Task.Delay(600);
        int aw = (int)about.ActualWidth;
        int ah = (int)about.ActualHeight;
        if (aw <= 0) aw = 520;
        if (ah <= 0) ah = 440;
        SaveElementToPng(about, System.IO.Path.Combine(outDir, "05_about_me_dialog.png"), aw, ah);
        about.Close();

        // 6. Quick Settings Widget (Apple Style)
        try
        {
            var qsWidget = new DuoWall.Core.Widgets.WidgetInstance
            {
                Kind = DuoWall.Core.Widgets.WidgetKind.QuickSettings,
                Title = "Quick Settings Control Center",
                Width = 340,
                Height = 280,
                GlassStyle = DuoWall.Core.Theming.GlassmorphismStyle.GetPreset("Frosted Glass")
            };
            var qsControl = new Controls.WidgetContentControl
            {
                Width = 340,
                Height = 280
            };
            qsControl.Bind(qsWidget, qsWidget.GlassStyle, false);
            qsControl.Measure(new Size(340, 280));
            qsControl.Arrange(new Rect(0, 0, 340, 280));
            qsControl.UpdateLayout();
            SaveElementToPng(qsControl, System.IO.Path.Combine(outDir, "06_quick_settings_widget.png"), 340, 280);
        }
        catch { }

        // 7. Typographic Clock Widget (Curated App Fonts)
        try
        {
            var typoWidget = new DuoWall.Core.Widgets.WidgetInstance
            {
                Kind = DuoWall.Core.Widgets.WidgetKind.DigitalClock,
                Title = "Typographic Clock",
                Width = 520,
                Height = 220,
                GlassStyle = DuoWall.Core.Theming.GlassmorphismStyle.GetPreset("Mond Transparent Serif")
            };
            typoWidget.Config["ClockLayout"] = "Typographic Clock (Spaced Day -> Time -> Date)";
            typoWidget.Config["FontFamily"] = "SF Pro";
            typoWidget.Config["TimeFontFamily"] = "SF Pro";
            typoWidget.Config["DayFontFamily"] = "SF Pro Rounded";
            typoWidget.Config["DateFontFamily"] = "SF Compact";
            typoWidget.Config["GreetingFontFamily"] = "Lato";
            typoWidget.Config["CustomGreeting"] = "WEDNESDAY";
            typoWidget.Config["Alignment"] = "Center";
            typoWidget.Config["ClockTextShadow"] = "0.55";

            var typoControl = new Controls.WidgetContentControl
            {
                Width = 520,
                Height = 220
            };
            typoControl.Bind(typoWidget, typoWidget.GlassStyle, false);
            typoControl.Measure(new Size(520, 220));
            typoControl.Arrange(new Rect(0, 0, 520, 220));
            typoControl.UpdateLayout();
            SaveElementToPng(typoControl, System.IO.Path.Combine(outDir, "07_typographic_clock_widget.png"), 520, 220);
        }
        catch { }

        // 8. Typographic Clock Configurator Studio
        try
        {
            vm.CurrentPageIndex = 6;
            vm.WidgetStudio.StudioSectionIndex = 0;
            var typoInstance = vm.WidgetStudio.Widgets.FirstOrDefault(w => w.Title.Contains("Typographic", StringComparison.OrdinalIgnoreCase) || w.Kind == DuoWall.Core.Widgets.WidgetKind.DigitalClock);
            if (typoInstance != null)
            {
                vm.WidgetStudio.SelectedWidget = typoInstance;
                vm.WidgetStudio.IsWidgetConfigOpen = true;
            }
            window.UpdateLayout();
            await Task.Delay(800);
            SaveElementToPng(window, System.IO.Path.Combine(outDir, "08_typographic_clock_configurator.png"), 1360, 860);
        }
        catch { }
    }

    private static void SaveElementToPng(FrameworkElement element, string filePath, int width, int height)
    {
        try
        {
            var rtb = new System.Windows.Media.Imaging.RenderTargetBitmap(
                width, height, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
            rtb.Render(element);

            var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
            encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(rtb));
            using var fs = System.IO.File.Create(filePath);
            encoder.Save(fs);
        }
        catch (Exception ex)
        {
            LogCrash(ex);
        }
    }
}
