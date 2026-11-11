using DuoWall.Core.Models;
using DuoWall.Core.Storage;
using Xunit;

namespace DuoWall.Tests;

public sealed class DatabaseTests : IDisposable
{
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), $"duowall_db_{Guid.NewGuid():N}");

    public DatabaseTests()
    {
        Directory.CreateDirectory(_tempDir);
    }

    [Fact]
    public void Database_RoundTripsSettingsBackupsWallpapersProfilesAndCalibrations()
    {
        string dbPath = Path.Combine(_tempDir, "test.db");
        var db = new DuoWallDatabase(dbPath);

        var settings = db.LoadSettings();
        settings.Theme = AppThemeMode.Light;
        settings.AutoReapplyOnDisplayChange = false;
        db.SaveSettings(settings);

        var reloaded = db.LoadSettings();
        Assert.Equal(AppThemeMode.Light, reloaded.Theme);
        Assert.False(reloaded.AutoReapplyOnDisplayChange);

        var backup = new WindowsWallpaperBackup
        {
            DesktopPosition = 4,
            BackgroundColor = 0x112233,
            MonitorWallpapers = { ["MON1"] = @"C:\wall1.jpg" }
        };
        db.SaveWindowsBackup(backup);
        var loadedBackup = db.LoadWindowsBackup();
        Assert.NotNull(loadedBackup);
        Assert.Equal(4, loadedBackup!.DesktopPosition);
        Assert.Equal(@"C:\wall1.jpg", loadedBackup.MonitorWallpapers["MON1"]);

        var wp = new WallpaperItem
        {
            Id = "uwshot-6300.jpg",
            FileName = "uwshot-6300.jpg",
            Title = "Mechanical Scorpion",
            Width = 7680,
            Height = 2160,
            Tags = new List<string> { "Steampunk", "Animals" },
            IsFavorite = true
        };
        db.UpsertWallpaper(wp);
        Assert.Contains("uwshot-6300.jpg", db.GetFavoriteIds());

        var profile = new PlacementSettings
        {
            Mode = WallpaperPlacementMode.Fill,
            Zoom = 1.45,
            PanX = -0.15,
            FocalX = 0.72
        };
        db.SavePlacementProfile("uwshot-6300.jpg@@topo1", profile);
        var loadedProfile = db.LoadPlacementProfile("uwshot-6300.jpg@@topo1");
        Assert.NotNull(loadedProfile);
        Assert.Equal(1.45, loadedProfile!.Zoom, precision: 3);
        Assert.Equal(-0.15, loadedProfile.PanX, precision: 3);

        var mons = new[]
        {
            new MonitorInfo { Id = "M1", UserOffsetMmY = -12.5, BezelRightMm = 9.0, IsIncluded = true }
        };
        db.SaveMonitorCalibrations(mons);

        var freshMons = new[] { new MonitorInfo { Id = "M1" } };
        db.ApplyMonitorCalibrations(freshMons);
        Assert.Equal(-12.5, freshMons[0].UserOffsetMmY, precision: 3);
        Assert.Equal(9.0, freshMons[0].BezelRightMm, precision: 3);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch { }
    }
}

