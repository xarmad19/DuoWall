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
        Assert.Contains(catalog, c => c.Manifest.Id == "rainmeter.mond-clock" && c.Manifest.GlassStyle?.SurfaceTheme == WidgetSurfaceTheme.TransparentMinimal);
        Assert.Contains(catalog, c => c.Manifest.Id == "rainmeter.illustro-sys" && c.Manifest.GlassStyle?.SurfaceTheme == WidgetSurfaceTheme.MatteFlatCard);
        Assert.Contains(catalog, c => c.Manifest.Id == "rainmeter.cyber-hud" && c.Manifest.GlassStyle?.SurfaceTheme == WidgetSurfaceTheme.RetroTerminalHUD);
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

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch { }
    }
}

