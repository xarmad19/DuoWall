using System.Net.Http;
using YoutubeExplode;
using YoutubeExplode.Common;
using YoutubeExplode.Videos;
using YoutubeExplode.Videos.Streams;

namespace DuoWall.Core.Services;

public sealed class YouTubeVideoSearchItem
{
    public string VideoId { get; init; } = string.Empty;
    public string Title { get; init; } = string.Empty;
    public string Author { get; init; } = string.Empty;
    public TimeSpan Duration { get; init; }
    public string DurationText => Duration.TotalHours >= 1 ? Duration.ToString(@"h\:mm\:ss") : Duration.ToString(@"m\:ss");
    public string WatchUrl { get; init; } = string.Empty;
    public string ThumbnailUrl { get; init; } = string.Empty;
}

public sealed class LocalLiveVideoItem
{
    public string FilePath { get; init; } = string.Empty;
    public string Title { get; init; } = string.Empty;
    public string? StillImagePath { get; set; }
    public long FileSizeBytes { get; init; }
    public string FileSizeLabel => FileSizeBytes >= 1024 * 1024
        ? $"{FileSizeBytes / (1024.0 * 1024.0):0.0} MB"
        : $"{FileSizeBytes / 1024.0:0} KB";
    public DateTime AddedUtc { get; init; }
}

public sealed class VideoDownloadResult
{
    public bool Success { get; init; }
    public string? LocalVideoPath { get; init; }
    public string? ThumbnailPath { get; init; }
    public string ResolutionLabel { get; init; } = string.Empty;
    public string CodecLabel { get; init; } = string.Empty;
    public string? ErrorMessage { get; init; }
}

/// <summary>
/// Searches YouTube for 4K/ultrawide live wallpaper loops and downloads muted high-resolution
/// video streams locally via YoutubeExplode without executing external scripts.
/// </summary>
public sealed class YouTubeWallpaperService
{
    private readonly YoutubeClient _youtube = new();
    private static readonly HttpClient ThumbHttp = new() { Timeout = TimeSpan.FromSeconds(15) };

    public async Task<IReadOnlyList<YouTubeVideoSearchItem>> SearchVideosAsync(
        string query,
        int maxResults = 18,
        CancellationToken cancellationToken = default)
    {
        string clean = string.IsNullOrWhiteSpace(query) ? "4k ultrawide live wallpaper loop" : query.Trim();
        var list = new List<YouTubeVideoSearchItem>();

        await foreach (var video in _youtube.Search.GetVideosAsync(clean, cancellationToken))
        {
            if (video.Duration.HasValue && video.Duration.Value.TotalSeconds < 4)
                continue; // skip ultra-short clips

            string thumb = video.Thumbnails
                .OrderByDescending(t => t.Resolution.Width)
                .FirstOrDefault()?.Url
                ?? $"https://i.ytimg.com/vi/{video.Id.Value}/hqdefault.jpg";

            list.Add(new YouTubeVideoSearchItem
            {
                VideoId = video.Id.Value,
                Title = video.Title,
                Author = video.Author.ChannelTitle,
                Duration = video.Duration ?? TimeSpan.Zero,
                WatchUrl = video.Url,
                ThumbnailUrl = thumb
            });

            if (list.Count >= maxResults)
                break;
        }

        return list;
    }

    public async Task<VideoDownloadResult> DownloadWallpaperVideoAsync(
        string videoUrlOrId,
        string destinationDirectory,
        int preferredMaxHeight = 2160,
        bool preferH264Compatibility = false,
        string? explicitTitle = null,
        string? thumbnailUrl = null,
        IProgress<(double Progress, string Status)>? progress = null,
        CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(destinationDirectory);

        try
        {
            progress?.Report((0.02, "Inspecting available YouTube video streams..."));
            var videoId = VideoId.Parse(videoUrlOrId);
            var videoMeta = await _youtube.Videos.GetAsync(videoId, cancellationToken);
            var manifest = await _youtube.Videos.Streams.GetManifestAsync(videoId, cancellationToken);

            IStreamInfo? chosen = SelectBestWallpaperStream(manifest, preferredMaxHeight, preferH264Compatibility);
            if (chosen == null)
            {
                return new VideoDownloadResult
                {
                    Success = false,
                    ErrorMessage = "No downloadable video stream was found for this YouTube video."
                };
            }

            string ext = chosen.Container.Name.Equals("webm", StringComparison.OrdinalIgnoreCase) ? "webm" : "mp4";
            string safeBase = SanitizeName(explicitTitle ?? videoMeta.Title);
            if (safeBase.Length > 60) safeBase = safeBase[..60].TrimEnd('_', ' ');

            string heightTag = chosen is IVideoStreamInfo vs ? $"{vs.VideoQuality.MaxHeight}p" : "hd";
            string fileName = $"{safeBase} [{videoId.Value}_{heightTag}].{ext}";
            string outPath = Path.Combine(destinationDirectory, fileName);

            var adapter = new Progress<double>(p =>
            {
                double mb = chosen.Size.MegaBytes;
                progress?.Report((0.05 + p * 0.90, $"Downloading {heightTag} ({p * 100:0}% of {mb:0.0} MB)..."));
            });

            await _youtube.Videos.Streams.DownloadAsync(chosen, outPath, adapter, cancellationToken);

            // Download thumbnail sidecar cleanly as initial poster if needed
            string thumbPath = Path.ChangeExtension(outPath, ".thumb.jpg");
            try
            {
                string? dlThumb = thumbnailUrl ?? videoMeta.Thumbnails.OrderByDescending(t => t.Resolution.Width).FirstOrDefault()?.Url;
                if (!string.IsNullOrWhiteSpace(dlThumb))
                {
                    byte[] bytes = await ThumbHttp.GetByteArrayAsync(dlThumb, cancellationToken);
                    await File.WriteAllBytesAsync(thumbPath, bytes, cancellationToken);
                }
            }
            catch { }

            string codecName = chosen is IVideoStreamInfo vInfo ? vInfo.VideoCodec : "h264";
            progress?.Report((1.0, $"Downloaded {heightTag} ({codecName})!"));

            return new VideoDownloadResult
            {
                Success = true,
                LocalVideoPath = outPath,
                ThumbnailPath = File.Exists(thumbPath) ? thumbPath : null,
                ResolutionLabel = heightTag,
                CodecLabel = codecName
            };
        }
        catch (Exception ex)
        {
            return new VideoDownloadResult
            {
                Success = false,
                ErrorMessage = $"YouTube download failed: {ex.Message}"
            };
        }
    }

    public static IStreamInfo? SelectBestWallpaperStream(
        StreamManifest manifest,
        int preferredMaxHeight,
        bool preferH264Compatibility)
    {
        var videoOnly = manifest.GetVideoOnlyStreams()
            .Where(s => s.VideoQuality.MaxHeight <= preferredMaxHeight)
            .ToList();

        if (videoOnly.Count == 0)
            videoOnly = manifest.GetVideoOnlyStreams().ToList();

        if (preferH264Compatibility)
        {
            var h264Mp4 = videoOnly
                .Where(s => s.Container == Container.Mp4 &&
                            (s.VideoCodec.StartsWith("avc", StringComparison.OrdinalIgnoreCase) ||
                             s.VideoCodec.Contains("h264", StringComparison.OrdinalIgnoreCase)))
                .OrderByDescending(s => s.VideoQuality.MaxHeight)
                .ThenByDescending(s => s.VideoQuality.Framerate)
                .ThenByDescending(s => s.Bitrate)
                .FirstOrDefault();
            if (h264Mp4 != null) return h264Mp4;
        }

        // Prefer MP4 container (H.264 -> HEVC -> AV1) up to preferred resolution, falling back cleanly to highest WebM/Muxed
        var bestMp4 = videoOnly
            .Where(s => s.Container == Container.Mp4)
            .OrderByDescending(s => s.VideoQuality.MaxHeight)
            .ThenByDescending(s => s.VideoCodec.StartsWith("avc", StringComparison.OrdinalIgnoreCase) ? 3
                : s.VideoCodec.StartsWith("hev", StringComparison.OrdinalIgnoreCase) || s.VideoCodec.StartsWith("hvc", StringComparison.OrdinalIgnoreCase) ? 2 : 1)
            .ThenByDescending(s => s.VideoQuality.Framerate)
            .ThenByDescending(s => s.Bitrate)
            .FirstOrDefault();
        if (bestMp4 != null) return bestMp4;

        var anyVideoOnly = videoOnly
            .OrderByDescending(s => s.VideoQuality.MaxHeight)
            .ThenByDescending(s => s.Bitrate)
            .FirstOrDefault();
        if (anyVideoOnly != null) return anyVideoOnly;

        return manifest.GetMuxedStreams()
            .OrderByDescending(s => s.VideoQuality.MaxHeight)
            .ThenByDescending(s => s.Bitrate)
            .FirstOrDefault();
    }

    public static List<LocalLiveVideoItem> ScanLocalVideos(string directory)
    {
        if (!Directory.Exists(directory))
            return new List<LocalLiveVideoItem>();

        string[] exts = { ".mp4", ".webm", ".mov", ".wmv", ".mkv" };
        var items = new List<LocalLiveVideoItem>();

        foreach (string file in Directory.EnumerateFiles(directory))
        {
            if (!exts.Contains(Path.GetExtension(file), StringComparer.OrdinalIgnoreCase))
                continue;

            var fi = new FileInfo(file);
            string stillCandidate = Path.ChangeExtension(file, ".still.png");
            string thumbCandidate = Path.ChangeExtension(file, ".thumb.jpg");
            items.Add(new LocalLiveVideoItem
            {
                FilePath = file,
                Title = Path.GetFileNameWithoutExtension(file),
                StillImagePath = File.Exists(stillCandidate)
                    ? stillCandidate
                    : (File.Exists(thumbCandidate) ? thumbCandidate : null),
                FileSizeBytes = fi.Length,
                AddedUtc = fi.LastWriteTimeUtc
            });
        }

        return items.OrderByDescending(i => i.AddedUtc).ToList();
    }

    public static string SanitizeName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return "live_wallpaper";

        var invalid = Path.GetInvalidFileNameChars();
        var sb = new System.Text.StringBuilder(name.Length);
        bool prevSpace = false;

        foreach (char c in name)
        {
            // Keep safe ASCII letters/digits/basic separators; strip #, %, ?, &, quotes, and non-BMP emojis
            // so WPF/LibVLC file paths and Uri builders never truncate fragments or break file handles.
            if (Array.IndexOf(invalid, c) >= 0 || c is '#' or '%' or '?' or '&' or '+' or '`' or '{' or '}')
            {
                if (!prevSpace && sb.Length > 0)
                {
                    sb.Append(' ');
                    prevSpace = true;
                }
                continue;
            }

            if (char.IsControl(c) || c > 0x007E)
                continue;

            if (char.IsWhiteSpace(c))
            {
                if (!prevSpace && sb.Length > 0)
                {
                    sb.Append(' ');
                    prevSpace = true;
                }
            }
            else
            {
                sb.Append(c);
                prevSpace = false;
            }
        }

        string clean = sb.ToString().Trim(' ', '.', '_', '-');
        return string.IsNullOrWhiteSpace(clean) ? "live_wallpaper" : clean;
    }
}

