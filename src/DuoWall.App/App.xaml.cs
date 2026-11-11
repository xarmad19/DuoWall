using System.Windows;
using DuoWall.App.ViewModels;
using DuoWall.App.Windows;
using DuoWall.Core.Engine;
using DuoWall.Core.Gallery;
using DuoWall.Core.Services;
using DuoWall.Core.Storage;
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
        sc.AddSingleton<WallpaperGeometryEngine>();
        sc.AddSingleton<SkiaWallpaperCompositor>();
        sc.AddSingleton<IWallpaperSourceProvider, UltraWideWallpapersProvider>();
        sc.AddSingleton<WallpaperLibraryService>();
        sc.AddSingleton<StartupAndWatcherService>();
        sc.AddSingleton<MainViewModel>();
        sc.AddSingleton<MainWindow>();

        _services = sc.BuildServiceProvider();

        var vm = _services.GetRequiredService<MainViewModel>();
        ApplicationThemeManager.Apply(
            vm.IsDarkTheme ? ApplicationTheme.Dark : ApplicationTheme.Light);

        var mainWindow = _services.GetRequiredService<MainWindow>();
        mainWindow.DataContext = vm;

        if (startupApply)
        {
            mainWindow.WindowState = WindowState.Minimized;
            mainWindow.ShowInTaskbar = true;
        }

        MainWindow = mainWindow;
        mainWindow.Show();

        await vm.InitializeAsync(startupApply);
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

        if (!string.IsNullOrWhiteSpace(outPath))
            await System.IO.File.WriteAllTextAsync(outPath, sb.ToString());
    }
}
