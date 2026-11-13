using System.Text.Json;
using DuoWall.Core.Models;
using DuoWall.Core.Packages;
using DuoWall.Core.Theming;
using DuoWall.Core.Widgets;
using Microsoft.Data.Sqlite;

namespace DuoWall.Core.Storage;

/// <summary>
/// SQLite persistence layer for DuoWall settings, local wallpaper library,
/// favorites, usage history, per-topology placement profiles, and Windows backup state.
/// </summary>
public sealed class DuoWallDatabase
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = false,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly string _connectionString;

    public DuoWallDatabase(string? dbPath = null)
    {
        string path = dbPath ?? AppSettings.GetDatabasePath();
        string? dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(dir))
            Directory.CreateDirectory(dir);

        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Mode = SqliteOpenMode.ReadWriteCreate
        }.ToString();

        InitializeSchema();
    }

    private SqliteConnection OpenConnection()
    {
        var conn = new SqliteConnection(_connectionString);
        conn.Open();
        return conn;
    }

    private void InitializeSchema()
    {
        using var conn = OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
CREATE TABLE IF NOT EXISTS AppKeyValue (
    Key TEXT PRIMARY KEY,
    Value TEXT NOT NULL
);

CREATE TABLE IF NOT EXISTS Wallpapers (
    Id TEXT PRIMARY KEY,
    FileName TEXT NOT NULL,
    Title TEXT NOT NULL,
    Description TEXT NOT NULL,
    SourceProvider TEXT NOT NULL,
    SourcePageUrl TEXT NOT NULL,
    ThumbnailUrl TEXT NOT NULL,
    PreviewUrl TEXT NOT NULL,
    HighResUrl TEXT NOT NULL,
    Width INTEGER NOT NULL,
    Height INTEGER NOT NULL,
    FileSizeBytes INTEGER NOT NULL,
    TagsJson TEXT NOT NULL,
    LocalOriginalPath TEXT,
    IsFavorite INTEGER NOT NULL DEFAULT 0,
    DownloadedUtc TEXT,
    LastAppliedUtc TEXT
);

CREATE TABLE IF NOT EXISTS PlacementProfiles (
    ProfileKey TEXT PRIMARY KEY,
    SettingsJson TEXT NOT NULL,
    UpdatedUtc TEXT NOT NULL
);

CREATE TABLE IF NOT EXISTS MonitorCalibrations (
    MonitorId TEXT PRIMARY KEY,
    OffsetMmX REAL NOT NULL DEFAULT 0,
    OffsetMmY REAL NOT NULL DEFAULT 0,
    BezelLeftMm REAL NOT NULL DEFAULT 7,
    BezelRightMm REAL NOT NULL DEFAULT 7,
    BezelTopMm REAL NOT NULL DEFAULT 7,
    BezelBottomMm REAL NOT NULL DEFAULT 12,
    IsIncluded INTEGER NOT NULL DEFAULT 1
);

CREATE TABLE IF NOT EXISTS GlassPresets (
    Id TEXT PRIMARY KEY,
    Name TEXT NOT NULL,
    StyleJson TEXT NOT NULL,
    UpdatedUtc TEXT NOT NULL
);

CREATE TABLE IF NOT EXISTS ColorPalettes (
    Id TEXT PRIMARY KEY,
    Name TEXT NOT NULL,
    PaletteJson TEXT NOT NULL,
    CreatedUtc TEXT NOT NULL
);

CREATE TABLE IF NOT EXISTS DesktopProfiles (
    ProfileId TEXT PRIMARY KEY,
    Name TEXT NOT NULL,
    ProfileJson TEXT NOT NULL,
    UpdatedUtc TEXT NOT NULL
);

CREATE TABLE IF NOT EXISTS InstalledPackages (
    PackageId TEXT PRIMARY KEY,
    Name TEXT NOT NULL,
    Version TEXT NOT NULL,
    Author TEXT NOT NULL,
    Category TEXT NOT NULL,
    PackageType INTEGER NOT NULL,
    InstallDirectory TEXT NOT NULL,
    ManifestJson TEXT NOT NULL,
    InstalledUtc TEXT NOT NULL
);

CREATE TABLE IF NOT EXISTS TodoItems (
    Id TEXT PRIMARY KEY,
    Text TEXT NOT NULL,
    IsCompleted INTEGER NOT NULL DEFAULT 0,
    CreatedUtc TEXT NOT NULL
);

CREATE TABLE IF NOT EXISTS CalendarEntries (
    Id TEXT PRIMARY KEY,
    DateIso TEXT NOT NULL,
    Kind INTEGER NOT NULL DEFAULT 0,
    Title TEXT NOT NULL,
    Notes TEXT NOT NULL DEFAULT '',
    StartTimeText TEXT NOT NULL DEFAULT '',
    DurationMinutes INTEGER NOT NULL DEFAULT 30,
    PlayAlarmSound INTEGER NOT NULL DEFAULT 0,
    IsDismissed INTEGER NOT NULL DEFAULT 0,
    CreatedUtc TEXT NOT NULL
);
";
        cmd.ExecuteNonQuery();
    }

    public AppSettings LoadSettings()
    {
        string? json = GetValue("AppSettings");
        if (string.IsNullOrWhiteSpace(json))
            return new AppSettings();

        try
        {
            return JsonSerializer.Deserialize<AppSettings>(json, JsonOpts) ?? new AppSettings();
        }
        catch
        {
            return new AppSettings();
        }
    }

    public void SaveSettings(AppSettings settings)
    {
        SetValue("AppSettings", JsonSerializer.Serialize(settings, JsonOpts));
    }

    public WindowsWallpaperBackup? LoadWindowsBackup()
    {
        string? json = GetValue("WindowsWallpaperBackup");
        if (string.IsNullOrWhiteSpace(json))
            return null;

        try
        {
            return JsonSerializer.Deserialize<WindowsWallpaperBackup>(json, JsonOpts);
        }
        catch
        {
            return null;
        }
    }

    public void SaveWindowsBackup(WindowsWallpaperBackup backup)
    {
        SetValue("WindowsWallpaperBackup", JsonSerializer.Serialize(backup, JsonOpts));
    }

    public void UpsertWallpaper(WallpaperItem item)
    {
        using var conn = OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
INSERT INTO Wallpapers (
    Id, FileName, Title, Description, SourceProvider, SourcePageUrl,
    ThumbnailUrl, PreviewUrl, HighResUrl, Width, Height, FileSizeBytes,
    TagsJson, LocalOriginalPath, IsFavorite, DownloadedUtc, LastAppliedUtc
) VALUES (
    $id, $fn, $title, $desc, $prov, $page,
    $thumb, $prev, $high, $w, $h, $size,
    $tags, $local, $fav, $dl, $applied
)
ON CONFLICT(Id) DO UPDATE SET
    FileName = excluded.FileName,
    Title = CASE WHEN excluded.Title <> '' THEN excluded.Title ELSE Wallpapers.Title END,
    Description = CASE WHEN excluded.Description <> '' THEN excluded.Description ELSE Wallpapers.Description END,
    SourcePageUrl = CASE WHEN excluded.SourcePageUrl <> '' THEN excluded.SourcePageUrl ELSE Wallpapers.SourcePageUrl END,
    ThumbnailUrl = CASE WHEN excluded.ThumbnailUrl <> '' THEN excluded.ThumbnailUrl ELSE Wallpapers.ThumbnailUrl END,
    PreviewUrl = CASE WHEN excluded.PreviewUrl <> '' THEN excluded.PreviewUrl ELSE Wallpapers.PreviewUrl END,
    HighResUrl = CASE WHEN excluded.HighResUrl <> '' THEN excluded.HighResUrl ELSE Wallpapers.HighResUrl END,
    Width = CASE WHEN excluded.Width > 0 THEN excluded.Width ELSE Wallpapers.Width END,
    Height = CASE WHEN excluded.Height > 0 THEN excluded.Height ELSE Wallpapers.Height END,
    FileSizeBytes = CASE WHEN excluded.FileSizeBytes > 0 THEN excluded.FileSizeBytes ELSE Wallpapers.FileSizeBytes END,
    TagsJson = CASE WHEN excluded.TagsJson <> '[]' THEN excluded.TagsJson ELSE Wallpapers.TagsJson END,
    LocalOriginalPath = COALESCE(excluded.LocalOriginalPath, Wallpapers.LocalOriginalPath),
    IsFavorite = excluded.IsFavorite,
    DownloadedUtc = COALESCE(excluded.DownloadedUtc, Wallpapers.DownloadedUtc),
    LastAppliedUtc = COALESCE(excluded.LastAppliedUtc, Wallpapers.LastAppliedUtc);
";
        cmd.Parameters.AddWithValue("$id", item.Id);
        cmd.Parameters.AddWithValue("$fn", item.FileName);
        cmd.Parameters.AddWithValue("$title", item.Title ?? string.Empty);
        cmd.Parameters.AddWithValue("$desc", item.Description ?? string.Empty);
        cmd.Parameters.AddWithValue("$prov", item.SourceProvider ?? string.Empty);
        cmd.Parameters.AddWithValue("$page", item.SourcePageUrl ?? string.Empty);
        cmd.Parameters.AddWithValue("$thumb", item.ThumbnailUrl ?? string.Empty);
        cmd.Parameters.AddWithValue("$prev", item.PreviewUrl ?? string.Empty);
        cmd.Parameters.AddWithValue("$high", item.HighResUrl ?? string.Empty);
        cmd.Parameters.AddWithValue("$w", item.Width);
        cmd.Parameters.AddWithValue("$h", item.Height);
        cmd.Parameters.AddWithValue("$size", item.FileSizeBytes);
        cmd.Parameters.AddWithValue("$tags", JsonSerializer.Serialize(item.Tags ?? new List<string>(), JsonOpts));
        cmd.Parameters.AddWithValue("$local", (object?)item.LocalOriginalPath ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$fav", item.IsFavorite ? 1 : 0);
        cmd.Parameters.AddWithValue("$dl", (object?)item.DownloadedUtc?.ToString("O") ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$applied", (object?)item.LastAppliedUtc?.ToString("O") ?? DBNull.Value);
        cmd.ExecuteNonQuery();
    }

    public List<WallpaperItem> GetAllLibraryWallpapers()
    {
        var list = new List<WallpaperItem>();
        using var conn = OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
SELECT Id, FileName, Title, Description, SourceProvider, SourcePageUrl,
       ThumbnailUrl, PreviewUrl, HighResUrl, Width, Height, FileSizeBytes,
       TagsJson, LocalOriginalPath, IsFavorite, DownloadedUtc, LastAppliedUtc
FROM Wallpapers
ORDER BY COALESCE(LastAppliedUtc, DownloadedUtc) DESC, Title ASC;
";
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            var tags = new List<string>();
            string tagsJson = r.GetString(12);
            try { tags = JsonSerializer.Deserialize<List<string>>(tagsJson, JsonOpts) ?? new(); } catch { }

            list.Add(new WallpaperItem
            {
                Id = r.GetString(0),
                FileName = r.GetString(1),
                Title = r.GetString(2),
                Description = r.GetString(3),
                SourceProvider = r.GetString(4),
                SourcePageUrl = r.GetString(5),
                ThumbnailUrl = r.GetString(6),
                PreviewUrl = r.GetString(7),
                HighResUrl = r.GetString(8),
                Width = r.GetInt32(9),
                Height = r.GetInt32(10),
                FileSizeBytes = r.GetInt64(11),
                Tags = tags,
                LocalOriginalPath = r.IsDBNull(13) ? null : r.GetString(13),
                IsFavorite = r.GetInt32(14) == 1,
                DownloadedUtc = r.IsDBNull(15) ? null : DateTime.Parse(r.GetString(15)).ToUniversalTime(),
                LastAppliedUtc = r.IsDBNull(16) ? null : DateTime.Parse(r.GetString(16)).ToUniversalTime()
            });
        }
        return list;
    }

    public HashSet<string> GetFavoriteIds()
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        using var conn = OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT Id FROM Wallpapers WHERE IsFavorite = 1;";
        using var r = cmd.ExecuteReader();
        while (r.Read())
            set.Add(r.GetString(0));
        return set;
    }

    public WallpaperItem? GetWallpaperById(string id)
    {
        return GetAllLibraryWallpapers()
            .FirstOrDefault(w => string.Equals(w.Id, id, StringComparison.OrdinalIgnoreCase));
    }

    public void SetFavorite(WallpaperItem item, bool isFavorite)
    {
        item.IsFavorite = isFavorite;
        UpsertWallpaper(item);
    }

    public void DeleteWallpaper(string id, bool deleteLocalFile)
    {
        var existing = GetWallpaperById(id);
        if (deleteLocalFile && existing?.LocalOriginalPath != null && File.Exists(existing.LocalOriginalPath))
        {
            try { File.Delete(existing.LocalOriginalPath); } catch { }
        }

        using var conn = OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM Wallpapers WHERE Id = $id;";
        cmd.Parameters.AddWithValue("$id", id);
        cmd.ExecuteNonQuery();
    }

    public void SavePlacementProfile(string profileKey, PlacementSettings settings)
    {
        using var conn = OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
INSERT INTO PlacementProfiles (ProfileKey, SettingsJson, UpdatedUtc)
VALUES ($key, $json, $utc)
ON CONFLICT(ProfileKey) DO UPDATE SET
    SettingsJson = excluded.SettingsJson,
    UpdatedUtc = excluded.UpdatedUtc;
";
        cmd.Parameters.AddWithValue("$key", profileKey);
        cmd.Parameters.AddWithValue("$json", JsonSerializer.Serialize(settings, JsonOpts));
        cmd.Parameters.AddWithValue("$utc", DateTime.UtcNow.ToString("O"));
        cmd.ExecuteNonQuery();
    }

    public PlacementSettings? LoadPlacementProfile(string profileKey)
    {
        using var conn = OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT SettingsJson FROM PlacementProfiles WHERE ProfileKey = $key;";
        cmd.Parameters.AddWithValue("$key", profileKey);
        var val = cmd.ExecuteScalar() as string;
        if (string.IsNullOrWhiteSpace(val))
            return null;

        try { return JsonSerializer.Deserialize<PlacementSettings>(val, JsonOpts); }
        catch { return null; }
    }

    public void SaveMonitorCalibrations(IEnumerable<MonitorInfo> monitors)
    {
        using var conn = OpenConnection();
        foreach (var m in monitors)
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
INSERT INTO MonitorCalibrations (
    MonitorId, OffsetMmX, OffsetMmY, BezelLeftMm, BezelRightMm, BezelTopMm, BezelBottomMm, IsIncluded
) VALUES ($id, $ox, $oy, $bl, $br, $bt, $bb, $inc)
ON CONFLICT(MonitorId) DO UPDATE SET
    OffsetMmX = excluded.OffsetMmX,
    OffsetMmY = excluded.OffsetMmY,
    BezelLeftMm = excluded.BezelLeftMm,
    BezelRightMm = excluded.BezelRightMm,
    BezelTopMm = excluded.BezelTopMm,
    BezelBottomMm = excluded.BezelBottomMm,
    IsIncluded = excluded.IsIncluded;
";
            cmd.Parameters.AddWithValue("$id", m.Id);
            cmd.Parameters.AddWithValue("$ox", m.UserOffsetMmX);
            cmd.Parameters.AddWithValue("$oy", m.UserOffsetMmY);
            cmd.Parameters.AddWithValue("$bl", m.BezelLeftMm);
            cmd.Parameters.AddWithValue("$br", m.BezelRightMm);
            cmd.Parameters.AddWithValue("$bt", m.BezelTopMm);
            cmd.Parameters.AddWithValue("$bb", m.BezelBottomMm);
            cmd.Parameters.AddWithValue("$inc", m.IsIncluded ? 1 : 0);
            cmd.ExecuteNonQuery();
        }
    }

    public void ApplyMonitorCalibrations(IReadOnlyList<MonitorInfo> monitors)
    {
        using var conn = OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT MonitorId, OffsetMmX, OffsetMmY, BezelLeftMm, BezelRightMm, BezelTopMm, BezelBottomMm, IsIncluded FROM MonitorCalibrations;";
        using var r = cmd.ExecuteReader();
        var map = new Dictionary<string, (double ox, double oy, double bl, double br, double bt, double bb, bool inc)>(StringComparer.OrdinalIgnoreCase);
        while (r.Read())
        {
            map[r.GetString(0)] = (
                r.GetDouble(1), r.GetDouble(2),
                r.GetDouble(3), r.GetDouble(4), r.GetDouble(5), r.GetDouble(6),
                r.GetInt32(7) == 1);
        }

        foreach (var m in monitors)
        {
            if (map.TryGetValue(m.Id, out var cal))
            {
                m.UserOffsetMmX = cal.ox;
                m.UserOffsetMmY = cal.oy;
                m.BezelLeftMm = cal.bl;
                m.BezelRightMm = cal.br;
                m.BezelTopMm = cal.bt;
                m.BezelBottomMm = cal.bb;
                m.IsIncluded = cal.inc;
            }
        }
    }

    private string? GetValue(string key)
    {
        using var conn = OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT Value FROM AppKeyValue WHERE Key = $k;";
        cmd.Parameters.AddWithValue("$k", key);
        return cmd.ExecuteScalar() as string;
    }

    private void SetValue(string key, string value)
    {
        using var conn = OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
INSERT INTO AppKeyValue (Key, Value) VALUES ($k, $v)
ON CONFLICT(Key) DO UPDATE SET Value = excluded.Value;
";
        cmd.Parameters.AddWithValue("$k", key);
        cmd.Parameters.AddWithValue("$v", value);
        cmd.ExecuteNonQuery();
    }

    // ==================== V2.0 PERSISTENCE: GLASS PRESETS, PALETTES, PROFILES, PACKAGES, TODOS ====================

    public void SaveCustomGlassPreset(GlassmorphismStyle style)
    {
        using var conn = OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
INSERT INTO GlassPresets (Id, Name, StyleJson, UpdatedUtc) VALUES ($id, $n, $j, $u)
ON CONFLICT(Id) DO UPDATE SET Name = excluded.Name, StyleJson = excluded.StyleJson, UpdatedUtc = excluded.UpdatedUtc;";
        cmd.Parameters.AddWithValue("$id", style.Id);
        cmd.Parameters.AddWithValue("$n", style.Name);
        cmd.Parameters.AddWithValue("$j", JsonSerializer.Serialize(style, JsonOpts));
        cmd.Parameters.AddWithValue("$u", DateTime.UtcNow.ToString("O"));
        cmd.ExecuteNonQuery();
    }

    public List<GlassmorphismStyle> LoadCustomGlassPresets()
    {
        var list = new List<GlassmorphismStyle>();
        using var conn = OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT StyleJson FROM GlassPresets ORDER BY Name ASC;";
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            try
            {
                var st = JsonSerializer.Deserialize<GlassmorphismStyle>(r.GetString(0), JsonOpts);
                if (st != null) list.Add(st);
            }
            catch { }
        }
        return list;
    }

    public void DeleteCustomGlassPreset(string id)
    {
        using var conn = OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM GlassPresets WHERE Id = $id;";
        cmd.Parameters.AddWithValue("$id", id);
        cmd.ExecuteNonQuery();
    }

    public void SaveColorPalette(WallpaperColorPalette palette)
    {
        using var conn = OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
INSERT INTO ColorPalettes (Id, Name, PaletteJson, CreatedUtc) VALUES ($id, $n, $j, $c)
ON CONFLICT(Id) DO UPDATE SET Name = excluded.Name, PaletteJson = excluded.PaletteJson;";
        cmd.Parameters.AddWithValue("$id", palette.Id);
        cmd.Parameters.AddWithValue("$n", palette.Name);
        cmd.Parameters.AddWithValue("$j", JsonSerializer.Serialize(palette, JsonOpts));
        cmd.Parameters.AddWithValue("$c", palette.AnalyzedUtc.ToString("O"));
        cmd.ExecuteNonQuery();
    }

    public List<WallpaperColorPalette> LoadColorPalettes()
    {
        var list = new List<WallpaperColorPalette>();
        using var conn = OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT PaletteJson FROM ColorPalettes ORDER BY CreatedUtc DESC;";
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            try
            {
                var p = JsonSerializer.Deserialize<WallpaperColorPalette>(r.GetString(0), JsonOpts);
                if (p != null) list.Add(p);
            }
            catch { }
        }
        return list;
    }

    public void SaveDesktopProfile(DesktopProfile profile)
    {
        using var conn = OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
INSERT INTO DesktopProfiles (ProfileId, Name, ProfileJson, UpdatedUtc) VALUES ($id, $n, $j, $u)
ON CONFLICT(ProfileId) DO UPDATE SET Name = excluded.Name, ProfileJson = excluded.ProfileJson, UpdatedUtc = excluded.UpdatedUtc;";
        cmd.Parameters.AddWithValue("$id", profile.ProfileId);
        cmd.Parameters.AddWithValue("$n", profile.Name);
        cmd.Parameters.AddWithValue("$j", JsonSerializer.Serialize(profile, JsonOpts));
        cmd.Parameters.AddWithValue("$u", DateTime.UtcNow.ToString("O"));
        cmd.ExecuteNonQuery();
    }

    public List<DesktopProfile> LoadDesktopProfilesEnsureDefaults()
    {
        var list = new List<DesktopProfile>();
        using var conn = OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT ProfileJson FROM DesktopProfiles;";
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            try
            {
                var p = JsonSerializer.Deserialize<DesktopProfile>(r.GetString(0), JsonOpts);
                if (p != null) list.Add(p);
            }
            catch { }
        }

        if (list.Count == 0)
        {
            list = BuiltInProfilePresets.CreateDefaultProfiles();
            foreach (var p in list)
                SaveDesktopProfile(p);
        }
        return list.OrderByDescending(p => p.Priority).ThenBy(p => p.Name).ToList();
    }

    public void DeleteDesktopProfile(string profileId)
    {
        using var conn = OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM DesktopProfiles WHERE ProfileId = $id;";
        cmd.Parameters.AddWithValue("$id", profileId);
        cmd.ExecuteNonQuery();
    }

    public void SaveInstalledPackage(InstalledPackageRecord record)
    {
        using var conn = OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
INSERT INTO InstalledPackages (PackageId, Name, Version, Author, Category, PackageType, InstallDirectory, ManifestJson, InstalledUtc)
VALUES ($id, $n, $v, $a, $c, $t, $dir, $m, $u)
ON CONFLICT(PackageId) DO UPDATE SET
    Name = excluded.Name, Version = excluded.Version, Author = excluded.Author,
    Category = excluded.Category, PackageType = excluded.PackageType,
    InstallDirectory = excluded.InstallDirectory, ManifestJson = excluded.ManifestJson,
    InstalledUtc = excluded.InstalledUtc;";
        cmd.Parameters.AddWithValue("$id", record.PackageId);
        cmd.Parameters.AddWithValue("$n", record.Name);
        cmd.Parameters.AddWithValue("$v", record.Version);
        cmd.Parameters.AddWithValue("$a", record.Author);
        cmd.Parameters.AddWithValue("$c", record.Category);
        cmd.Parameters.AddWithValue("$t", (int)record.PackageType);
        cmd.Parameters.AddWithValue("$dir", record.InstallDirectory);
        cmd.Parameters.AddWithValue("$m", record.ManifestJson);
        cmd.Parameters.AddWithValue("$u", record.InstalledUtc.ToString("O"));
        cmd.ExecuteNonQuery();
    }

    public List<InstalledPackageRecord> LoadInstalledPackages()
    {
        var list = new List<InstalledPackageRecord>();
        using var conn = OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT PackageId, Name, Version, Author, Category, PackageType, InstallDirectory, ManifestJson, InstalledUtc FROM InstalledPackages ORDER BY InstalledUtc DESC;";
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            list.Add(new InstalledPackageRecord
            {
                PackageId = r.GetString(0),
                Name = r.GetString(1),
                Version = r.GetString(2),
                Author = r.GetString(3),
                Category = r.GetString(4),
                PackageType = (PackageKind)r.GetInt32(5),
                InstallDirectory = r.GetString(6),
                ManifestJson = r.GetString(7),
                InstalledUtc = DateTime.Parse(r.GetString(8)).ToUniversalTime()
            });
        }
        return list;
    }

    public void RemoveInstalledPackage(string packageId)
    {
        using var conn = OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM InstalledPackages WHERE PackageId = $id;";
        cmd.Parameters.AddWithValue("$id", packageId);
        cmd.ExecuteNonQuery();
    }

    public void SaveTodoItems(IEnumerable<TodoTaskItem> items)
    {
        using var conn = OpenConnection();
        using var clear = conn.CreateCommand();
        clear.CommandText = "DELETE FROM TodoItems;";
        clear.ExecuteNonQuery();

        foreach (var item in items)
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "INSERT INTO TodoItems (Id, Text, IsCompleted, CreatedUtc) VALUES ($id, $t, $c, $u);";
            cmd.Parameters.AddWithValue("$id", item.Id);
            cmd.Parameters.AddWithValue("$t", item.Text);
            cmd.Parameters.AddWithValue("$c", item.IsCompleted ? 1 : 0);
            cmd.Parameters.AddWithValue("$u", item.CreatedUtc.ToString("O"));
            cmd.ExecuteNonQuery();
        }
    }

    public List<TodoTaskItem> LoadTodoItems()
    {
        var list = new List<TodoTaskItem>();
        using var conn = OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT Id, Text, IsCompleted, CreatedUtc FROM TodoItems ORDER BY CreatedUtc ASC;";
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            list.Add(new TodoTaskItem
            {
                Id = r.GetString(0),
                Text = r.GetString(1),
                IsCompleted = r.GetInt32(2) == 1,
                CreatedUtc = DateTime.Parse(r.GetString(3)).ToUniversalTime()
            });
        }
        return list;
    }

    public List<CalendarAgendaEntry> LoadCalendarEntries()
    {
        var list = new List<CalendarAgendaEntry>();
        using var conn = OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT Id, DateIso, Kind, Title, Notes, StartTimeText, DurationMinutes, PlayAlarmSound, IsDismissed, CreatedUtc FROM CalendarEntries ORDER BY DateIso ASC, StartTimeText ASC, CreatedUtc ASC;";
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            list.Add(new CalendarAgendaEntry
            {
                Id = r.GetString(0),
                DateIso = r.GetString(1),
                Kind = (CalendarAgendaEntryKind)r.GetInt32(2),
                Title = r.GetString(3),
                Notes = r.GetString(4),
                StartTimeText = r.GetString(5),
                DurationMinutes = r.GetInt32(6),
                PlayAlarmSound = r.GetInt32(7) == 1,
                IsDismissed = r.GetInt32(8) == 1,
                CreatedUtc = DateTime.TryParse(r.GetString(9), out var dt) ? dt.ToUniversalTime() : DateTime.UtcNow
            });
        }
        return list;
    }

    public void UpsertCalendarEntry(CalendarAgendaEntry entry)
    {
        using var conn = OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
INSERT INTO CalendarEntries (Id, DateIso, Kind, Title, Notes, StartTimeText, DurationMinutes, PlayAlarmSound, IsDismissed, CreatedUtc)
VALUES ($id, $d, $k, $t, $n, $st, $dur, $snd, $dis, $c)
ON CONFLICT(Id) DO UPDATE SET
    DateIso = excluded.DateIso,
    Kind = excluded.Kind,
    Title = excluded.Title,
    Notes = excluded.Notes,
    StartTimeText = excluded.StartTimeText,
    DurationMinutes = excluded.DurationMinutes,
    PlayAlarmSound = excluded.PlayAlarmSound,
    IsDismissed = excluded.IsDismissed;";
        cmd.Parameters.AddWithValue("$id", entry.Id);
        cmd.Parameters.AddWithValue("$d", entry.DateIso);
        cmd.Parameters.AddWithValue("$k", (int)entry.Kind);
        cmd.Parameters.AddWithValue("$t", entry.Title);
        cmd.Parameters.AddWithValue("$n", entry.Notes);
        cmd.Parameters.AddWithValue("$st", entry.StartTimeText);
        cmd.Parameters.AddWithValue("$dur", entry.DurationMinutes);
        cmd.Parameters.AddWithValue("$snd", entry.PlayAlarmSound ? 1 : 0);
        cmd.Parameters.AddWithValue("$dis", entry.IsDismissed ? 1 : 0);
        cmd.Parameters.AddWithValue("$c", entry.CreatedUtc.ToString("O"));
        cmd.ExecuteNonQuery();
    }

    public void DeleteCalendarEntry(string id)
    {
        using var conn = OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM CalendarEntries WHERE Id = $id;";
        cmd.Parameters.AddWithValue("$id", id);
        cmd.ExecuteNonQuery();
    }
}
