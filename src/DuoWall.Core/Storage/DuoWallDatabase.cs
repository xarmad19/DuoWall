using System.Text.Json;
using DuoWall.Core.Models;
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
}
