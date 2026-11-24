using System.IO.Compression;
using System.Text;
using DuoWall.Core.Packages;
using DuoWall.Core.Theming;
using DuoWall.Core.Widgets;
using Xunit;

namespace DuoWall.Tests;

public sealed class PackageServiceTests : IDisposable
{
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), $"duowall_pkg_test_{Guid.NewGuid():N}");

    public PackageServiceTests()
    {
        Directory.CreateDirectory(_tempDir);
    }

    [Fact]
    public async Task ExportAndImport_ValidDeclarativePackage_SucceedsAndCleansUpOnRemoval()
    {
        var svc = new WidgetPackageService();
        string pkgPath = Path.Combine(_tempDir, "test_skin.dwwp");

        var manifest = new DualWallWidgetPackageManifest
        {
            Id = "community.aurora.clock",
            Name = "Aurora Clock Skin",
            Version = "1.0.0",
            Author = "Tester",
            Category = "Time & Date",
            PackageType = PackageKind.WidgetSkin,
            WidgetKind = WidgetKind.DigitalClock,
            GlassStyle = GlassmorphismStyle.GetPreset("Aurora")
        };

        await svc.ExportPackageAsync(manifest, pkgPath);
        Assert.True(File.Exists(pkgPath));

        string installRoot = Path.Combine(_tempDir, "installed");
        var res = await svc.ImportPackageAsync(pkgPath, installRoot);

        Assert.True(res.Success);
        Assert.NotNull(res.InstalledRecord);
        Assert.True(Directory.Exists(res.InstalledRecord!.InstallDirectory));

        WidgetPackageService.RemovePackageAssets(res.InstalledRecord);
        Assert.False(Directory.Exists(res.InstalledRecord.InstallDirectory));
    }

    [Fact]
    public async Task ImportPackage_RejectsScriptPayloadsAndUnsupportedVersions()
    {
        var svc = new WidgetPackageService();
        string evilZip = Path.Combine(_tempDir, "evil.dwwp");

        using (var archive = ZipFile.Open(evilZip, ZipArchiveMode.Create))
        {
            var m = archive.CreateEntry("manifest.json");
            await using (var s = m.Open())
            {
                byte[] bytes = Encoding.UTF8.GetBytes("{\"packageFormat\":\"DualWallWidgetPackage\",\"schemaVersion\":\"2.0\",\"id\":\"bad\",\"name\":\"Bad\"}");
                await s.WriteAsync(bytes);
            }
            var ps1 = archive.CreateEntry("payload.ps1");
            await using (var s = ps1.Open())
            {
                byte[] bytes = Encoding.UTF8.GetBytes("Write-Host 'evil'");
                await s.WriteAsync(bytes);
            }
        }

        var res = await svc.ImportPackageAsync(evilZip, Path.Combine(_tempDir, "installed"));
        Assert.False(res.Success);
        Assert.Contains("Security policy blocked", res.ErrorMessage);
    }

    [Fact]
    public async Task LocalMarketplace_IncludesRainmeterInspiredNonGlassDesigns()
    {
        var catalog = await new LocalWidgetMarketplaceProvider().GetCatalogAsync();
        Assert.Contains(catalog, c => c.Manifest.Id == "rainmeter.mond-clock" &&
                                      c.Manifest.GlassStyle?.SurfaceTheme == WidgetSurfaceTheme.TransparentMinimal &&
                                      c.Manifest.DefaultSettings["FontFamily"] == "SF Pro");
        Assert.Contains(catalog, c => c.Manifest.Id == "rainmeter.illustro-sys" && c.Manifest.GlassStyle?.SurfaceTheme == WidgetSurfaceTheme.MatteFlatCard);
        Assert.Contains(catalog, c => c.Manifest.Id == "rainmeter.cyber-hud" && c.Manifest.GlassStyle?.SurfaceTheme == WidgetSurfaceTheme.RetroTerminalHUD);
    }

    [Fact]
    public async Task GenerateAndValidate_TypographicClock_Package_Succeeds()
    {
        var svc = new WidgetPackageService();
        string current = AppContext.BaseDirectory;
        while (!string.IsNullOrEmpty(current) && !File.Exists(Path.Combine(current, "DuoWall.sln")))
        {
            current = Path.GetDirectoryName(current)!;
        }
        string repoRoot = current;
        string fontsDir = Path.Combine(repoRoot, "src", "DuoWall.App", "Assets", "Fonts");
        Assert.True(Directory.Exists(fontsDir), $"Fonts directory should exist at: {fontsDir}");

        var fontFiles = Directory.GetFiles(fontsDir, "*.*")
            .Where(f => f.EndsWith(".ttf", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".otf", StringComparison.OrdinalIgnoreCase))
            .ToList();
        Assert.Equal(35, fontFiles.Count);

        var assets = new Dictionary<string, string>();
        foreach (var fontFile in fontFiles)
        {
            string rel = "Assets/Fonts/" + Path.GetFileName(fontFile);
            assets[rel] = fontFile;
        }

        var fontOptions = new List<string>
        {
            "SF Pro", "SF Pro Rounded", "SF Compact", "Lato", "Liberation Sans", "Nimbus Sans",
            "Noto Sans", "Noto Sans Display", "Noto Sans Mono", "Noto Serif", "Noto Serif Display", "URW Gothic"
        };

        var manifest = new DualWallWidgetPackageManifest
        {
            Id = "duowall.typographic-clock",
            Name = "Typographic Clock",
            Category = "Rainmeter Inspired",
            Version = "2.0.0",
            MinAppVersion = "2.0.0",
            Author = "Syed Sarmad Iftikhar",
            Description = "Curated typographic desktop timepiece with 12 bundled fonts (SF Pro, Lato, URW Gothic, Noto, Nimbus, Liberation), per-element typography, and borderless floating design.",
            Rating = 5.0,
            DownloadCount = 32400,
            PackageType = PackageKind.Widget,
            WidgetKind = WidgetKind.DigitalClock,
            GlassStyle = GlassmorphismStyle.GetPreset("Mond Transparent Serif"),
            Tags = new List<string> { "Rainmeter Inspired", "DigitalClock", "Typographic", "SF Pro", "Lato", "Glassmorphism" },
            ConfigSchema = new List<WidgetConfigFieldSchema>
            {
                new() { Key = "FontFamily", Label = "Master Font Family", Type = "select", DefaultValue = "SF Pro", Options = fontOptions },
                new() { Key = "TimeFontFamily", Label = "Time Font Family", Type = "select", DefaultValue = "SF Pro", Options = fontOptions },
                new() { Key = "DayFontFamily", Label = "Day Font Family", Type = "select", DefaultValue = "SF Pro Rounded", Options = fontOptions },
                new() { Key = "DateFontFamily", Label = "Date Font Family", Type = "select", DefaultValue = "SF Compact", Options = fontOptions },
                new() { Key = "GreetingFontFamily", Label = "Greeting Font Family", Type = "select", DefaultValue = "Lato", Options = fontOptions },
                new() { Key = "ClockLayout", Label = "Clock Layout", Type = "select", DefaultValue = "Typographic Clock (Spaced Day -> Time -> Date)" },
                new() { Key = "TimeFontWeight", Label = "Time Weight", Type = "select", DefaultValue = "Bold" }
            },
            DefaultSettings = new Dictionary<string, string>
            {
                ["ClockLayout"] = "Typographic Clock (Spaced Day -> Time -> Date)",
                ["FontFamily"] = "SF Pro",
                ["TimeFontFamily"] = "SF Pro",
                ["DayFontFamily"] = "SF Pro Rounded",
                ["DateFontFamily"] = "SF Compact",
                ["GreetingFontFamily"] = "Lato",
                ["TimeFontWeight"] = "Bold",
                ["Format24H"] = "false",
                ["ShowSeconds"] = "false",
                ["ShowDay"] = "true",
                ["ShowDate"] = "true",
                ["ShowGreeting"] = "true",
                ["CustomGreeting"] = "WEDNESDAY",
                ["DayPlacement"] = "Top",
                ["DayCaseStyle"] = "Spaced Uppercase",
                ["DatePlacement"] = "Bottom",
                ["DateFormat"] = "dddd, MMM d yyyy",
                ["Alignment"] = "Center",
                ["VerticalAlignment"] = "Center",
                ["ClockTimeBaseSize"] = "68",
                ["FontScale"] = "1.15",
                ["TimeScale"] = "1.25",
                ["DayScale"] = "1.0",
                ["DateScale"] = "0.95",
                ["GreetingScale"] = "0.85",
                ["ClockTextShadow"] = "0.55",
                ["DayUseAccent"] = "true",
                ["TimeUseAccent"] = "false",
                ["DateUseAccent"] = "false",
                ["GreetingUseAccent"] = "false"
            }
        };

        string outPkgDir = Path.Combine(repoRoot, "packages");
        Directory.CreateDirectory(outPkgDir);
        string pkgPath = Path.Combine(outPkgDir, "Typographic Clock.widget.dwwp");

        await svc.ExportPackageAsync(manifest, pkgPath, assets);
        Assert.True(File.Exists(pkgPath));

        string pubPkgDir = Path.Combine(repoRoot, "publish", "packages");
        Directory.CreateDirectory(pubPkgDir);
        File.Copy(pkgPath, Path.Combine(pubPkgDir, "Typographic Clock.widget.dwwp"), overwrite: true);

        // Verify package integrity with ImportPackageAsync
        string verifyDir = Path.Combine(_tempDir, "verify_install");
        var res = await svc.ImportPackageAsync(pkgPath, verifyDir);
        Assert.True(res.Success, $"Import failed: {res.ErrorMessage}");
        Assert.NotNull(res.InstalledRecord);
        Assert.True(Directory.Exists(res.InstalledRecord!.InstallDirectory));
        Assert.Equal(35, Directory.GetFiles(Path.Combine(res.InstalledRecord.InstallDirectory, "Assets", "Fonts")).Length);
    }

    [Fact]
    public void YouTubeWallpaperService_AndSnapGridSettings_BehaveCleanly()
    {
        var settings = new DuoWall.Core.Models.AppSettings();
        Assert.True(settings.EnableWidgetSnapToGrid);
        Assert.Equal(16, settings.WidgetGridSizePx);

        File.WriteAllBytes(Path.Combine(_tempDir, "sample_4k.mp4"), new byte[] { 0x00, 0x00, 0x00, 0x18 });
        var scanned = DuoWall.Core.Services.YouTubeWallpaperService.ScanLocalVideos(_tempDir);
        Assert.Single(scanned);
        Assert.Equal("sample_4k", scanned[0].Title);
    }

    [Fact]
    public async Task SystemAndNetworkWidgetPackage_ContainsNetworkAdapterConfiguration()
    {
        string dwwpPath = @"d:\Downloads\System & Network.widget.dwwp";
        if (!File.Exists(dwwpPath)) return;

        var svc = new WidgetPackageService();
        string installRoot = Path.Combine(_tempDir, "system_network_install");
        var res = await svc.ImportPackageAsync(dwwpPath, installRoot);

        Assert.True(res.Success, res.ErrorMessage);
        Assert.NotNull(res.Manifest);

        var manifest = res.Manifest!;
        Assert.Equal(WidgetKind.SystemStats, manifest.WidgetKind);
        Assert.True(manifest.DefaultSettings.TryGetValue("ShowNetAdapters", out string? showAdapters) && showAdapters == "true");
        Assert.True(manifest.DefaultSettings.TryGetValue("NetSwitchHotkey", out string? hotkey) && hotkey == "Ctrl+Alt+N");

        Assert.NotNull(manifest.WidgetTemplate);
        Assert.True(manifest.WidgetTemplate.Config.TryGetValue("ShowNetAdapters", out string? tplAdapters) && tplAdapters == "true");
        Assert.True(manifest.WidgetTemplate.Config.TryGetValue("NetSwitchHotkey", out string? tplHotkey) && tplHotkey == "Ctrl+Alt+N");
        Assert.Equal(220, manifest.WidgetTemplate.Height);
    }

    [Fact]
    public async Task Marketplace_Contains_SystemAndNetwork_WithTagsAndDefaults()
    {
        var catalog = await new LocalWidgetMarketplaceProvider().GetCatalogAsync();
        var sysNet = catalog.FirstOrDefault(c => c.Manifest.Id == "builtin.system-network");
        Assert.NotNull(sysNet);
        Assert.Equal("System & Network", sysNet.Manifest.Name);
        Assert.Equal("Monitoring", sysNet.Manifest.Category);
        Assert.Equal(WidgetKind.SystemStats, sysNet.Manifest.WidgetKind);
        Assert.True(sysNet.Manifest.DefaultSettings.TryGetValue("ShowNetAdapters", out string? show) && show == "true");
        Assert.True(sysNet.Manifest.DefaultSettings.TryGetValue("NetSwitchHotkey", out string? hotkey) && hotkey == "Ctrl+Alt+N");
        Assert.Contains(sysNet.Manifest.Tags, t => t.Equals("Network", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(sysNet.Manifest.Tags, t => t.Equals("Adapter", StringComparison.OrdinalIgnoreCase) || t.Equals("Adapters", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task InstallWidgetInUserDuoWall()
    {
        string dwwpPath = @"d:\Downloads\System & Network.widget.dwwp";
        Assert.True(File.Exists(dwwpPath));

        var svc = new WidgetPackageService();
        string packagesRoot = DuoWall.Core.Models.AppSettings.GetDefaultPackagesDirectory();
        var res = await svc.ImportPackageAsync(dwwpPath, packagesRoot);
        Assert.True(res.Success, res.ErrorMessage);
        Assert.NotNull(res.InstalledRecord);
        Assert.NotNull(res.Manifest);

        var db = new DuoWall.Core.Storage.DuoWallDatabase();
        db.SaveInstalledPackage(res.InstalledRecord!);

        var profiles = db.LoadDesktopProfilesEnsureDefaults();

        foreach (var profile in profiles)
        {
            var existing = profile.Widgets.FirstOrDefault(w => w.Kind == WidgetKind.SystemStats || w.PackageId == res.Manifest.Id);
            if (existing != null)
            {
                existing.PackageId = res.Manifest.Id;
                existing.Title = res.Manifest.Name;
                existing.Config["ShowNetAdapters"] = "true";
                existing.Config["NetSwitchHotkey"] = "Ctrl+Alt+N";
                if (existing.Height < 220) existing.Height = 220;
            }
            else if (profile.IsEnabled)
            {
                var w = (res.Manifest.WidgetTemplate ?? new WidgetInstance
                {
                    Kind = WidgetKind.SystemStats,
                    Title = res.Manifest.Name,
                    Height = 220,
                    Width = 340
                }).Clone(newInstanceId: true);
                w.PackageId = res.Manifest.Id;
                w.Config["ShowNetAdapters"] = "true";
                w.Config["NetSwitchHotkey"] = "Ctrl+Alt+N";
                profile.Widgets.Add(w);
            }
            db.SaveDesktopProfile(profile);
        }
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch { }
    }
}

