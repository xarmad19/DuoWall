using System.IO.Compression;
using System.Text.Json;
using DuoWall.Core.Theming;
using DuoWall.Core.Widgets;

namespace DuoWall.Core.Packages;

public interface IWidgetMarketplaceProvider
{
    string ProviderName { get; }
    Task<IReadOnlyList<MarketplaceCatalogEntry>> GetCatalogAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Local extensible marketplace catalog pre-seeded with built-in declarative widgets,
/// glass skins, and layout packs. Designed to accommodate a hosted community index later
/// without simulating fake online downloads today.
/// </summary>
public sealed class LocalWidgetMarketplaceProvider : IWidgetMarketplaceProvider
{
    public string ProviderName => "DuoWall Built-In & Local Catalog";

    public Task<IReadOnlyList<MarketplaceCatalogEntry>> GetCatalogAsync(CancellationToken cancellationToken = default)
    {
        var entries = new List<MarketplaceCatalogEntry>
        {
            MakeEntry("builtin.digital-clock", "Precision Digital Clock", "Time & Date", WidgetKind.DigitalClock, 4.9, 18400, true,
                "Clean multi-monitor digital clock with greeting, seconds toggle, and 12/24h formatting.",
                GlassmorphismStyle.GetPreset("Frosted Glass"),
                new() { ["Format24H"] = "false", ["ShowSeconds"] = "false", ["ShowGreeting"] = "true" }),

            MakeEntry("builtin.analog-clock", "Glass Analog Timepiece", "Time & Date", WidgetKind.AnalogClock, 4.8, 12100, true,
                "Minimal vector analog clock face rendered crisply over frosted or crystal glass.",
                GlassmorphismStyle.GetPreset("Crystal"),
                new() { ["ShowSecondHand"] = "true" }),

            MakeEntry("builtin.weather", "Live Weather & Forecast", "Weather", WidgetKind.Weather, 4.9, 16900, true,
                "Keyless Open-Meteo weather widget with optional custom provider API key support.",
                GlassmorphismStyle.GetPreset("Frosted Glass"),
                new() { ["City"] = "Lahore", ["Units"] = "metric" }),

            MakeEntry("builtin.calendar", "Full Monthly Calendar & Reminders", "Productivity", WidgetKind.CalendarAgenda, 4.9, 14400, true,
                "Monday-start month calendar with weekend & holiday highlights, date click Events/Reminders/Notes, and optional alarm alerts.",
                GlassmorphismStyle.GetPreset("Dark Glass"),
                new() { ["HighlightWeekends"] = "true" }),

            MakeEntry("builtin.todo", "Desktop To-Do Checklist", "Productivity", WidgetKind.TodoList, 4.8, 14200, true,
                "Interactive SQLite-backed task checklist right on your desktop.",
                GlassmorphismStyle.GetPreset("Dark Glass"),
                new()),

            MakeEntry("builtin.focus-timer", "Pomodoro Focus Timer", "Productivity", WidgetKind.FocusTimer, 4.9, 11800, false,
                "Configurable focus duration presets & custom minutes with countdown progress bar.",
                GlassmorphismStyle.GetPreset("Dark Glass"),
                new() { ["WorkMinutes"] = "25", ["BreakMinutes"] = "5" }),

            MakeEntry("builtin.shortcut-bar", "App & Web Shortcut Dock Bar", "Productivity", WidgetKind.ShortcutBar, 4.9, 17300, true,
                "Customizable multi-icon dock bar or launcher with auto app icons, glass glyphs, custom initials, and hover labels.",
                GlassmorphismStyle.GetPreset("Frosted Glass"),
                new() { ["ShortcutLayout"] = "Horizontal" }),

            MakeEntry("builtin.single-shortcut", "Quick Launch Glass Tile", "Productivity", WidgetKind.ShortcutBar, 4.8, 11200, false,
                "Single floating shortcut icon tile for launching any installed desktop app or website.",
                GlassmorphismStyle.GetPreset("Crystal"),
                new()
                {
                    ["ShortcutLayout"] = "Horizontal",
                    ["ShortcutItemsJson"] = "[{\"id\":\"tile1\",\"label\":\"YouTube\",\"targetPathOrUrl\":\"https://www.youtube.com\",\"iconMode\":\"Initials\",\"initials\":\"YT\",\"glyphKey\":\"Play\",\"customColorHex\":\"#FFEF4444\"}]"
                }),

            MakeEntry("builtin.system-stats", "System Telemetry HUD", "Monitoring", WidgetKind.SystemStats, 5.0, 21500, true,
                "Real-time CPU, RAM, GPU, Network Rx/Tx, Storage capacity, and hardware temperature sensors.",
                GlassmorphismStyle.GetPreset("Midnight Glass"),
                new() { ["Mode"] = "Detailed" }),

            // ==================== RAINMETER-INSPIRED NON-GLASS DESIGNS ====================
            MakeEntry("rainmeter.mond-clock", "Typographic Clock", "Rainmeter Inspired", WidgetKind.DigitalClock, 5.0, 32400, true,
                "Inspired by classic typographic desktop skins: borderless floating weekday, date, time & greeting with independent per-element fonts, scaling, accent/custom colors, and drop shadow.",
                GlassmorphismStyle.GetPreset("Mond Transparent Serif"),
                new() { ["Format24H"] = "false", ["ShowSeconds"] = "false", ["ShowGreeting"] = "true", ["ShowDay"] = "true", ["ShowDate"] = "true", ["CustomGreeting"] = "WEDNESDAY", ["FontFamily"] = "Bahnschrift", ["Alignment"] = "Center", ["FontScale"] = "1.15", ["ClockTextShadow"] = "0.55" }),

            MakeEntry("rainmeter.illustro-sys", "Illustro Classic System Monitor", "Rainmeter Inspired", WidgetKind.SystemStats, 4.8, 28900, true,
                "Inspired by Rainmeter Illustro: flat opaque slate diagnostic card with top hairline bar and focused CPU/RAM/Network/Disk meters (non-glass).",
                GlassmorphismStyle.GetPreset("Illustro Flat Matte"),
                new() { ["StatsLayout"] = "Detailed", ["ShowCpu"] = "true", ["ShowRam"] = "true", ["ShowNet"] = "true", ["ShowDisk"] = "true", ["ShowTemps"] = "false", ["ShowGpu"] = "false" }),

            MakeEntry("rainmeter.cyber-hud", "CyberHUD Retro Terminal Telemetry", "Rainmeter Inspired", WidgetKind.SystemStats, 4.9, 24100, true,
                "Inspired by Cyberpunk / CyberOS HUD Rainmeter skins: flat obsidian canvas, sharp 2px neon cyan frame, and monospace Cascadia Code telemetry readouts.",
                GlassmorphismStyle.GetPreset("CyberHUD Retro Frame"),
                new() { ["StatsLayout"] = "Detailed", ["FontFamily"] = "Cascadia Code", ["ShowCpu"] = "true", ["ShowRam"] = "true", ["ShowGpu"] = "true", ["ShowNet"] = "true", ["ShowTemps"] = "true", ["ShowUptime"] = "true" }),

            MakeEntry("rainmeter.sonder-weather", "Sonder Minimalist Weather Pill", "Rainmeter Inspired", WidgetKind.Weather, 4.9, 19800, false,
                "Inspired by Rainmeter Sonder: pure typographic weather display floating seamlessly on the desktop without background box or blur.",
                GlassmorphismStyle.GetPreset("Mond Transparent Serif"),
                new() { ["City"] = "Lahore", ["Units"] = "metric", ["WeatherShowProvider"] = "false", ["WeatherShowFeelsLike"] = "false" }),

            MakeEntry("rainmeter.paper-agenda", "Editorial Paper Agenda & Calendar", "Rainmeter Inspired", WidgetKind.CalendarAgenda, 4.8, 13400, false,
                "Warm matte paper editorial card with high-contrast charcoal typography and zero glass transparency.",
                GlassmorphismStyle.GetPreset("Editorial Warm Paper"),
                new() { ["HighlightWeekends"] = "true" })
        };

        return Task.FromResult<IReadOnlyList<MarketplaceCatalogEntry>>(entries);
    }

    private static MarketplaceCatalogEntry MakeEntry(
        string id, string name, string category, WidgetKind kind, double rating, int downloads, bool featured,
        string desc, GlassmorphismStyle glass, Dictionary<string, string> defaults)
    {
        return new MarketplaceCatalogEntry
        {
            IsFeatured = featured,
            SourceBadge = "Built-In Package",
            Manifest = new DualWallWidgetPackageManifest
            {
                Id = id,
                Name = name,
                Category = category,
                Version = "2.0.0",
                MinAppVersion = "2.0.0",
                Author = "SRMD Official",
                Description = desc,
                Rating = rating,
                DownloadCount = downloads,
                PackageType = PackageKind.Widget,
                WidgetKind = kind,
                GlassStyle = glass,
                DefaultSettings = defaults,
                Tags = new List<string> { category, kind.ToString(), "Glassmorphism" }
            }
        };
    }
}

/// <summary>
/// Exports, validates, imports, and cleanly uninstalls versioned DualWallWidgetPackage (.dwwp) files.
/// Strictly blocks scripts, native binaries, unsupported versions, and ZipSlip traversal attacks.
/// </summary>
public sealed class WidgetPackageService
{
    public const string CurrentAppVersion = "2.0.0";

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private static readonly HashSet<string> BlockedExecutableExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".exe", ".dll", ".bat", ".cmd", ".ps1", ".psm1", ".vbs", ".vbe", ".js", ".jse",
        ".wsf", ".wsh", ".py", ".sh", ".msi", ".msp", ".scr", ".com", ".hta", ".cpl", ".reg"
    };

    private static readonly HashSet<string> AllowedAssetExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".json", ".png", ".jpg", ".jpeg", ".webp", ".bmp", ".svg", ".mp4", ".webm", ".mov", ".ttf", ".otf"
    };

    public async Task ExportPackageAsync(
        DualWallWidgetPackageManifest manifest,
        string destinationFilePath,
        IReadOnlyDictionary<string, string>? localAssetFilesToInclude = null,
        CancellationToken cancellationToken = default)
    {
        manifest.PackageFormat = DualWallWidgetPackageManifest.CurrentFormatName;
        manifest.SchemaVersion = DualWallWidgetPackageManifest.CurrentSchemaVersion;

        if (localAssetFilesToInclude != null)
        {
            foreach (var relPath in localAssetFilesToInclude.Keys)
            {
                string norm = relPath.Replace('\\', '/').TrimStart('/');
                if (!manifest.Assets.Contains(norm, StringComparer.OrdinalIgnoreCase))
                    manifest.Assets.Add(norm);
            }
        }

        string? dir = Path.GetDirectoryName(destinationFilePath);
        if (!string.IsNullOrWhiteSpace(dir))
            Directory.CreateDirectory(dir);

        if (File.Exists(destinationFilePath))
            File.Delete(destinationFilePath);

        using var archive = ZipFile.Open(destinationFilePath, ZipArchiveMode.Create);
        var manifestEntry = archive.CreateEntry("manifest.json", CompressionLevel.Optimal);
        await using (var s = manifestEntry.Open())
        {
            await JsonSerializer.SerializeAsync(s, manifest, JsonOpts, cancellationToken);
        }

        if (localAssetFilesToInclude != null)
        {
            foreach (var kv in localAssetFilesToInclude)
            {
                if (!File.Exists(kv.Value)) continue;
                string ext = Path.GetExtension(kv.Value);
                if (BlockedExecutableExtensions.Contains(ext) || !AllowedAssetExtensions.Contains(ext))
                    throw new InvalidOperationException($"Refusing to bundle unsupported or unsafe asset type '{ext}'.");

                string entryName = kv.Key.Replace('\\', '/').TrimStart('/');
                archive.CreateEntryFromFile(kv.Value, entryName, CompressionLevel.Optimal);
            }
        }
    }

    public async Task<PackageImportResult> ImportPackageAsync(
        string packageFilePath,
        string packagesInstallRoot,
        IReadOnlySet<string>? availablePackageIds = null,
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(packageFilePath))
        {
            return new PackageImportResult { Success = false, ErrorMessage = "Package file not found." };
        }

        var warnings = new List<string>();
        string tempExtractDir = Path.Combine(Path.GetTempPath(), $"duowall_pkg_{Guid.NewGuid():N}");

        try
        {
            Directory.CreateDirectory(tempExtractDir);

            using var archive = ZipFile.OpenRead(packageFilePath);
            var manifestEntry = archive.GetEntry("manifest.json");
            if (manifestEntry == null)
            {
                return new PackageImportResult
                {
                    Success = false,
                    ErrorMessage = "Invalid package: missing manifest.json."
                };
            }

            // Security check across ALL entries before extracting anything
            foreach (var entry in archive.Entries)
            {
                if (string.IsNullOrEmpty(entry.Name))
                    continue; // directory entry

                string fullTarget = Path.GetFullPath(Path.Combine(tempExtractDir, entry.FullName));
                if (!fullTarget.StartsWith(Path.GetFullPath(tempExtractDir) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                {
                    return new PackageImportResult
                    {
                        Success = false,
                        ErrorMessage = $"Security violation: unsafe archive path '{entry.FullName}'."
                    };
                }

                string ext = Path.GetExtension(entry.Name);
                if (BlockedExecutableExtensions.Contains(ext))
                {
                    return new PackageImportResult
                    {
                        Success = false,
                        ErrorMessage = $"Security policy blocked executable/script entry '{entry.FullName}'. Only declarative JSON and media/font assets are allowed."
                    };
                }

                if (!string.IsNullOrEmpty(ext) && !AllowedAssetExtensions.Contains(ext))
                {
                    warnings.Add($"Skipped unrecognized asset extension '{entry.FullName}'.");
                }
            }

            DualWallWidgetPackageManifest? manifest;
            await using (var ms = manifestEntry.Open())
            {
                manifest = await JsonSerializer.DeserializeAsync<DualWallWidgetPackageManifest>(ms, JsonOpts, cancellationToken);
            }

            if (manifest == null ||
                !string.Equals(manifest.PackageFormat, DualWallWidgetPackageManifest.CurrentFormatName, StringComparison.OrdinalIgnoreCase) ||
                string.IsNullOrWhiteSpace(manifest.Id) ||
                string.IsNullOrWhiteSpace(manifest.Name))
            {
                return new PackageImportResult
                {
                    Success = false,
                    ErrorMessage = "Invalid or corrupted DualWallWidgetPackage manifest."
                };
            }

            if (manifest.SchemaVersion != "1.0" && manifest.SchemaVersion != "2.0")
            {
                return new PackageImportResult
                {
                    Success = false,
                    ErrorMessage = $"Unsupported package schema version '{manifest.SchemaVersion}' (supported: 1.0, 2.0)."
                };
            }

            if (Version.TryParse(manifest.MinAppVersion, out var minVer) &&
                Version.TryParse(CurrentAppVersion, out var appVer) &&
                minVer > appVer)
            {
                warnings.Add($"Package targets DuoWall v{manifest.MinAppVersion} (current: v{CurrentAppVersion}). Some features may degrade.");
            }

            if (manifest.Dependencies.Count > 0 && availablePackageIds != null)
            {
                foreach (var dep in manifest.Dependencies)
                {
                    if (!availablePackageIds.Contains(dep))
                        warnings.Add($"Missing optional dependency '{dep}'.");
                }
            }

            // Extract safe allowed entries
            foreach (var entry in archive.Entries)
            {
                if (string.IsNullOrEmpty(entry.Name)) continue;
                string ext = Path.GetExtension(entry.Name);
                if (!AllowedAssetExtensions.Contains(ext)) continue;

                string dest = Path.GetFullPath(Path.Combine(tempExtractDir, entry.FullName));
                string? parent = Path.GetDirectoryName(dest);
                if (!string.IsNullOrWhiteSpace(parent))
                    Directory.CreateDirectory(parent);
                entry.ExtractToFile(dest, overwrite: true);
            }

            string safePkgFolder = SanitizeId(manifest.Id);
            string installDir = Path.Combine(packagesInstallRoot, safePkgFolder);
            if (Directory.Exists(installDir))
                Directory.Delete(installDir, recursive: true);

            Directory.CreateDirectory(packagesInstallRoot);
            CopyDirectory(tempExtractDir, installDir);

            var record = new InstalledPackageRecord
            {
                PackageId = manifest.Id,
                Name = manifest.Name,
                Version = manifest.Version,
                Author = manifest.Author,
                Category = manifest.Category,
                PackageType = manifest.PackageType,
                IsBuiltIn = false,
                InstallDirectory = installDir,
                ManifestJson = JsonSerializer.Serialize(manifest, JsonOpts),
                InstalledUtc = DateTime.UtcNow
            };

            return new PackageImportResult
            {
                Success = true,
                Manifest = manifest,
                InstalledRecord = record,
                Warnings = warnings
            };
        }
        catch (Exception ex)
        {
            return new PackageImportResult
            {
                Success = false,
                ErrorMessage = $"Failed to import package: {ex.Message}"
            };
        }
        finally
        {
            if (Directory.Exists(tempExtractDir))
            {
                try { Directory.Delete(tempExtractDir, recursive: true); } catch { }
            }
        }
    }

    public static void RemovePackageAssets(InstalledPackageRecord record)
    {
        if (!string.IsNullOrWhiteSpace(record.InstallDirectory) && Directory.Exists(record.InstallDirectory))
        {
            Directory.Delete(record.InstallDirectory, recursive: true);
        }
    }

    private static string SanitizeId(string id) =>
        string.Concat(id.Select(c => char.IsLetterOrDigit(c) || c is '.' or '-' or '_' ? c : '_'));

    private static void CopyDirectory(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (var file in Directory.GetFiles(source))
        {
            File.Copy(file, Path.Combine(destination, Path.GetFileName(file)), overwrite: true);
        }
        foreach (var dir in Directory.GetDirectories(source))
        {
            CopyDirectory(dir, Path.Combine(destination, Path.GetFileName(dir)));
        }
    }
}

