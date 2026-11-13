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
        sc.AddSingleton<MainViewModel>();
        sc.AddSingleton<MainWindow>();

        _services = sc.BuildServiceProvider();

        var vm = _services.GetRequiredService<MainViewModel>();
        ApplicationThemeManager.Apply(
            vm.IsDarkTheme ? ApplicationTheme.Dark : ApplicationTheme.Light);

        var mainWindow = _services.GetRequiredService<MainWindow>();
        mainWindow.DataContext = vm;
        mainWindow.AttachCoordinator(_services.GetRequiredService<DesktopOverlayCoordinator>());

        if (startupApply)
        {
            mainWindow.WindowState = WindowState.Minimized;
            mainWindow.ShowInTaskbar = true;
        }

        MainWindow = mainWindow;
        mainWindow.Show();

        await vm.InitializeAsync(startupApply);

        int shotIdx = Array.FindIndex(e.Args, a => string.Equals(a, "--capture-screenshots", StringComparison.OrdinalIgnoreCase));
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

        // 1. Gallery View
        vm.CurrentPageIndex = 0;
        window.UpdateLayout();
        await Task.Delay(800);
        SaveElementToPng(window, System.IO.Path.Combine(outDir, "01_wallpaper_gallery.png"), 1360, 860);

        // 2. Placement Studio
        vm.CurrentPageIndex = 1;
        window.UpdateLayout();
        await Task.Delay(800);
        SaveElementToPng(window, System.IO.Path.Combine(outDir, "02_placement_studio.png"), 1360, 860);

        // 3. Widget Marketplace
        vm.CurrentPageIndex = 2;
        window.UpdateLayout();
        await Task.Delay(800);
        SaveElementToPng(window, System.IO.Path.Combine(outDir, "03_widget_marketplace.png"), 1360, 860);

        // 4. Taskbar Tweaks
        vm.CurrentPageIndex = 3;
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
