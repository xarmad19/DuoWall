using System.Text.Json;
using DuoWall.Core.Engine;
using DuoWall.Core.Gallery;
using DuoWall.Core.Models;
using DuoWall.Core.Packages;
using DuoWall.Core.Services;
using DuoWall.Core.Storage;
using DuoWall.Core.Widgets;
using Xunit;

namespace DuoWall.Tests;

public sealed class V22FeatureTests : IDisposable
{
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), $"duowall_v22_test_{Guid.NewGuid():N}");

    public V22FeatureTests()
    {
        Directory.CreateDirectory(_tempDir);
    }

    [Fact]
    public void CalendarHolidayHelper_BuildsMondayFirstGrid_WithWeekendsAndPublicHolidaysHighlighted()
    {
        var cells = CalendarHolidayHelper.BuildMondayFirstMonthGrid(2026, 12, new DateTime(2026, 12, 11));
        Assert.Equal(42, cells.Count);
        Assert.Equal(DayOfWeek.Monday, cells[0].Date.DayOfWeek);

        var dec25 = cells.Single(c => c.Date == new DateTime(2026, 12, 25));
        Assert.True(dec25.IsCurrentMonth);
        Assert.False(string.IsNullOrWhiteSpace(dec25.HolidayName));

        var sat = cells.First(c => c.Date.DayOfWeek == DayOfWeek.Saturday);
        var sun = cells.First(c => c.Date.DayOfWeek == DayOfWeek.Sunday);
        Assert.True(sat.IsWeekend);
        Assert.True(sun.IsWeekend);
    }

    [Fact]
    public void Database_RoundTripsCalendarEventsRemindersAndNotes()
    {
        var db = new DuoWallDatabase(Path.Combine(_tempDir, "cal.db"));
        var ev = new CalendarAgendaEntry
        {
            DateIso = "2026-11-15",
            Kind = CalendarAgendaEntryKind.Reminder,
            Title = "Team Sync Alarm",
            Notes = "Bring Q4 metrics",
            StartTimeText = "14:30",
            DurationMinutes = 45,
            PlayAlarmSound = true
        };
        db.UpsertCalendarEntry(ev);

        var loaded = db.LoadCalendarEntries();
        Assert.Single(loaded);
        Assert.Equal(CalendarAgendaEntryKind.Reminder, loaded[0].Kind);
        Assert.Equal("14:30", loaded[0].StartTimeText);
        Assert.Equal(45, loaded[0].DurationMinutes);
        Assert.True(loaded[0].PlayAlarmSound);

        db.DeleteCalendarEntry(ev.Id);
        Assert.Empty(db.LoadCalendarEntries());
    }

    [Fact]
    public void ShortcutDockConfigHelper_RoundTripsShortcutsAndRespectsExplicitEmptyDeletion()
    {
        var unconfiguredCfg = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        Assert.NotEmpty(ShortcutDockConfigHelper.ParseItems(unconfiguredCfg));

        var cfg = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var list = new List<ShortcutDockItem>
        {
            new() { Label = "Projects Folder", TargetPathOrUrl = @"C:\Projects", IconMode = "Auto", Initials = "PF" },
            new() { Label = "GitHub", TargetPathOrUrl = "https://github.com", IconMode = "CustomIcon", Initials = "GH", CustomIconPath = @"C:\icons\gh.png", CustomColorHex = "#FF6366F1" }
        };

        ShortcutDockConfigHelper.SaveItems(cfg, list);
        var parsed = ShortcutDockConfigHelper.ParseItems(cfg);

        Assert.Equal(2, parsed.Count);
        Assert.Equal("Projects Folder", parsed[0].Label);
        Assert.Equal(@"C:\icons\gh.png", parsed[1].CustomIconPath);

        // Saving empty list after user deletes all icons MUST stay empty (not restore starter defaults)
        ShortcutDockConfigHelper.SaveItems(cfg, Array.Empty<ShortcutDockItem>());
        var afterClear = ShortcutDockConfigHelper.ParseItems(cfg);
        Assert.Empty(afterClear);
    }

    [Fact]
    public void YouTubeSanitizeName_StripsHashTagsAndEmojisForSafePaths()
    {
        string raw = "Samurai Warrior 4K Wallpaper⚔️ For PC #samurai #livewallpaper";
        string clean = YouTubeWallpaperService.SanitizeName(raw);

        Assert.DoesNotContain("#", clean);
        Assert.Contains("Samurai Warrior 4K Wallpaper", clean);
    }

    [Fact]
    public void CustomWebsiteGalleryProvider_ExtractsValidImagesAndSkipsFavicons()
    {
        var html = """
            <html><body>
                <img src="/favicon.ico" />
                <img src="/logo.svg" />
                <a href="/wallpapers/cyber-32-9.jpg">Cyber Horizon</a>
                <img data-src="https://cdn.example.com/dual/space.webp" alt="Deep Space 32:9" />
            </body></html>
            """;

        var items = CustomWebsiteGalleryProvider.ExtractWallpapersFromHtml(html, new Uri("https://example.com/gallery/"));
        Assert.Equal(2, items.Count);
        Assert.Contains(items, i => i.HighResUrl == "https://example.com/wallpapers/cyber-32-9.jpg" && i.Title == "Cyber Horizon");
        Assert.Contains(items, i => i.HighResUrl == "https://cdn.example.com/dual/space.webp" && i.Title == "Deep Space 32:9");
    }

    [Fact]
    public async Task MarketplaceAndBuiltInPresets_ExcludeRemovedWidgets_AndIncludeUpdatedClockAndShortcuts()
    {
        var catalog = await new LocalWidgetMarketplaceProvider().GetCatalogAsync();
        Assert.DoesNotContain(catalog, c => c.Manifest.WidgetKind == WidgetKind.AmbientGeometry);
        Assert.DoesNotContain(catalog, c => c.Manifest.WidgetKind == WidgetKind.PanoramicVideo);
        Assert.Contains(catalog, c => c.Manifest.Id == "rainmeter.mond-clock" && c.Manifest.Name == "Typographic Clock");
        Assert.Contains(catalog, c => c.Manifest.WidgetKind == WidgetKind.ShortcutBar);

        var profiles = BuiltInProfilePresets.CreateDefaultProfiles();
        Assert.All(profiles, p => Assert.DoesNotContain(p.Widgets, w => w.Kind == WidgetKind.AmbientGeometry));
        Assert.All(profiles, p => Assert.DoesNotContain(p.Widgets, w => w.Kind == WidgetKind.PanoramicVideo));

        var settings = new AppSettings();
        Assert.InRange(settings.TaskbarDockExtraWidthPx, 4.0, 240.0);
        Assert.InRange(settings.TaskbarDockHeightPx, 26.0, 76.0);
    }

    [Fact]
    public void LayoutPresetBackup_RoundTripsPlacementAndWidgets()
    {
        var backup = new DuoWallLayoutPresetBackup
        {
            PresetName = "Studio Backup",
            WallpaperFilePath = @"C:\wall.jpg",
            WidgetAccentColorHex = "#FFEC4899",
            Placement = new PlacementSettings { Mode = WallpaperPlacementMode.Fill, FlipHorizontal = true, BackgroundColorHex = "#1E293B" },
            Widgets = new List<WidgetInstance>
            {
                new() { Kind = WidgetKind.CalendarAgenda, Title = "Cal", X = 120, Y = 80, Width = 340, Height = 330 }
            }
        };

        string json = JsonSerializer.Serialize(backup);
        var restored = JsonSerializer.Deserialize<DuoWallLayoutPresetBackup>(json);
        Assert.NotNull(restored);
        Assert.True(restored!.Placement.FlipHorizontal);
        Assert.Equal("#FFEC4899", restored.WidgetAccentColorHex);
        Assert.Single(restored.Widgets);
        Assert.Equal(WidgetKind.CalendarAgenda, restored.Widgets[0].Kind);
    }

    [Fact]
    public void ResolveBestPreviewSource_PrefersHighResPreviewOverCachedGridThumbnail()
    {
        string thumbFile = Path.Combine(_tempDir, "thumb_400.jpg");
        string origFile = Path.Combine(_tempDir, "orig_5120.jpg");
        File.WriteAllBytes(thumbFile, new byte[] { 1, 2, 3 });

        var onlineItem = new WallpaperItem
        {
            PreviewUrl = "https://ultrawidewallpapers.net/resizecache.php?image=shot.jpg&width=1700",
            ThumbnailUrl = "https://ultrawidewallpapers.net/resizecachethumbs.php?image=shot.jpg&width=400",
            LocalThumbnailPath = thumbFile
        };
        Assert.Equal(onlineItem.PreviewUrl, WallpaperItem.ResolveBestPreviewSource(onlineItem));

        File.WriteAllBytes(origFile, new byte[] { 4, 5, 6 });
        onlineItem.LocalOriginalPath = origFile;
        Assert.Equal(origFile, WallpaperItem.ResolveBestPreviewSource(onlineItem));

        // Video local file must still resolve its still preview instead of the .mp4 path
        string videoFile = Path.Combine(_tempDir, "live.mp4");
        File.WriteAllBytes(videoFile, new byte[] { 7, 8 });
        var videoItem = new WallpaperItem
        {
            LocalOriginalPath = videoFile,
            LocalThumbnailPath = thumbFile
        };
        Assert.Equal(thumbFile, WallpaperItem.ResolveBestPreviewSource(videoItem));
    }

    [Fact]
    public void TaskbarClusterLayoutHelper_ExcludesOffscreenPagersAndTrailingRightGaps()
    {
        var candidates = new List<TaskbarButtonCandidate>
        {
            new(500, 548, 1032, 1080, IsButton: true, IsOffscreen: false, Name: "File Explorer"),
            new(550, 598, 1032, 1080, IsButton: true, IsOffscreen: false, Name: "Google Chrome"),
            new(600, 648, 1032, 1080, IsButton: true, IsOffscreen: false, Name: "Calculator"),
            // Trailing offscreen / non-button / orphan spacer elements that previously inflated dock right edge
            new(650, 698, 1032, 1080, IsButton: true, IsOffscreen: true, Name: "Closed App Ghost"),
            new(650, 740, 1032, 1080, IsButton: false, IsOffscreen: false, Name: "Running applications spacer"),
            new(690, 738, 1032, 1080, IsButton: true, IsOffscreen: false, Name: "Scroll Down"),
            new(745, 793, 1032, 1080, IsButton: true, IsOffscreen: false, Name: "Detached Orphan")
        };

        var bounds = TaskbarClusterLayoutHelper.ComputeContiguousButtonClusterBounds(
            candidates, containerLeft: 120, containerRight: 1680, minButtonHeight: 20);

        Assert.True(bounds.IsValid);
        Assert.Equal(3, bounds.ButtonCount);
        Assert.Equal(500, bounds.LeftPx);
        Assert.Equal(648, bounds.RightPx);
        Assert.Equal(148, bounds.WidthPx);
    }

    [Fact]
    public void ShortcutDockItem_RaisesPropertyChangedForLiveCustomIconUpdates()
    {
        var item = new ShortcutDockItem { Label = "GitHub", IconMode = "Auto" };
        var changedProps = new List<string?>();
        item.PropertyChanged += (_, e) => changedProps.Add(e.PropertyName);

        item.IconMode = "CustomIcon";
        item.CustomIconPath = @"C:\icons\github.png";

        Assert.Contains(nameof(ShortcutDockItem.IconMode), changedProps);
        Assert.Contains(nameof(ShortcutDockItem.CustomIconPath), changedProps);
    }

    [Fact]
    public void WallpaperGeometryEngine_ComputeResizedTransform_KeepsOppositeCornerAnchored()
    {
        var monitors = new List<MonitorInfo>
        {
            new() { Id = "M1", Bounds = new PixelRect(0, 0, 1920, 1080), NativeWidth = 1920, NativeHeight = 1080, IsConnected = true, IsIncluded = true, IsPrimary = true },
            new() { Id = "M2", Bounds = new PixelRect(1920, 0, 1920, 1080), NativeWidth = 1920, NativeHeight = 1080, IsConnected = true, IsIncluded = true }
        };

        var engine = new WallpaperGeometryEngine();
        var startSettings = new PlacementSettings
        {
            Mode = WallpaperPlacementMode.Fill,
            CoordinateSpace = GeometryCoordinateSpace.VirtualPixels,
            Zoom = 1.0,
            PanX = 0.0,
            PanY = 0.0,
            FocalX = 0.5,
            FocalY = 0.5
        };

        var startPlan = engine.ComputePlan(monitors, 3840, 1080, "wall.jpg", startSettings);
        var (newZoom, newPanX, newPanY) = WallpaperGeometryEngine.ComputeResizedTransform(
            startPlan.ImageCanvasRect,
            startPlan.CombinedCanvasWidth,
            startPlan.CombinedCanvasHeight,
            startSettings.Zoom,
            startSettings.FocalX,
            startSettings.FocalY,
            deltaCanvasX: 384,
            deltaCanvasY: 108,
            WallpaperResizeAnchor.BottomRight);

        Assert.True(newZoom > 1.05);

        startSettings.Zoom = newZoom;
        startSettings.PanX = newPanX;
        startSettings.PanY = newPanY;
        var resizedPlan = engine.ComputePlan(monitors, 3840, 1080, "wall.jpg", startSettings);

        // Top-Left corner must stay anchored when dragging BottomRight handle
        Assert.InRange(Math.Abs(resizedPlan.ImageCanvasRect.X - startPlan.ImageCanvasRect.X), 0, 1.5);
        Assert.InRange(Math.Abs(resizedPlan.ImageCanvasRect.Y - startPlan.ImageCanvasRect.Y), 0, 1.5);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch { }
    }
}

