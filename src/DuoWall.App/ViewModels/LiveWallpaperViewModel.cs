using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DuoWall.App.Services;
using DuoWall.App.Windows;
using DuoWall.Core.Models;
using DuoWall.Core.Services;
using DuoWall.Core.Storage;
using Microsoft.Win32;

namespace DuoWall.App.ViewModels;

public sealed partial class LiveWallpaperViewModel : ObservableObject
{
    private readonly YouTubeWallpaperService _youtubeService;
    private readonly DuoWallDatabase _db;
    private readonly DesktopOverlayCoordinator _coordinator;
    private readonly Action<string, string?> _stageVideoInStudio;
    private readonly Action<string, bool> _setStatus;
    private readonly Func<CompositionPlan?> _getActivePlan;
    private readonly Func<string> _getBackgroundHex;
    private readonly Action? _refreshMyWallpapers;

    private AppSettings _settings = new();
    private CancellationTokenSource? _searchCts;
    private CancellationTokenSource? _downloadCts;

    public LiveWallpaperViewModel(
        YouTubeWallpaperService youtubeService,
        DuoWallDatabase db,
        DesktopOverlayCoordinator coordinator,
        Action<string, string?> stageVideoInStudio,
        Action<string, bool> setStatus,
        Func<CompositionPlan?> getActivePlan,
        Func<string> getBackgroundHex,
        Action? refreshMyWallpapers = null)
    {
        _youtubeService = youtubeService;
        _db = db;
        _coordinator = coordinator;
        _stageVideoInStudio = stageVideoInStudio;
        _setStatus = setStatus;
        _getActivePlan = getActivePlan;
        _getBackgroundHex = getBackgroundHex;
        _refreshMyWallpapers = refreshMyWallpapers;
    }

    public ObservableCollection<YouTubeVideoSearchItem> SearchResults { get; } = new();
    public ObservableCollection<LocalLiveVideoItem> LocalVideos { get; } = new();

    public IReadOnlyList<string> PresetQueries { get; } = new[]
    {
        "4K Ultrawide Live Wallpaper Loop",
        "32:9 Dual Monitor Wallpaper Animation",
        "4K Cyberpunk City Night Loop",
        "4K Mountain Mist Aerial Loop",
        "4K Abstract Fluid Dark Wallpaper",
        "4K Ocean Sunset Relaxing Loop"
    };

    public IReadOnlyList<string> QualityOptions { get; } = new[]
    {
        "Auto Best MP4 (Up to 4K 2160p)",
        "4K Ultra HD (2160p)",
        "2K QHD (1440p)",
        "1080p Universal H.264 MP4"
    };

    [ObservableProperty] private string _searchQuery = "4K Ultrawide Live Wallpaper Loop";
    [ObservableProperty] private string _selectedQuality = "Auto Best MP4 (Up to 4K 2160p)";
    [ObservableProperty] private bool _isSearching;
    [ObservableProperty] private bool _isDownloading;
    [ObservableProperty] private double _downloadProgress;
    [ObservableProperty] private string _downloadStatusText = string.Empty;

    [ObservableProperty] private string _activeVideoTitle = "No live video wallpaper active";
    [ObservableProperty] private string? _activeVideoPath;
    [ObservableProperty] private string? _activeVideoStillPath;
    [ObservableProperty] private bool _hasActiveVideo;
    [ObservableProperty] private bool _loopLiveVideo = true;
    [ObservableProperty] private bool _pauseLiveOnBattery;
    [ObservableProperty] private bool _pauseLiveOnFullScreen = true;

    public void Initialize(AppSettings settings)
    {
        _settings = settings;
        LoopLiveVideo = settings.LoopLiveVideoWallpaper;
        PauseLiveOnBattery = settings.PauseLiveVideoOnBattery;
        PauseLiveOnFullScreen = settings.PauseLiveVideoOnFullScreen;

        RefreshActiveVideoBanner();
        RefreshLocalVideos();
    }

    public void RefreshActiveVideoBanner()
    {
        ActiveVideoPath = _settings.ActiveLiveVideoPath;
        ActiveVideoStillPath = _settings.ActiveLiveVideoStillPath;
        HasActiveVideo = !string.IsNullOrWhiteSpace(ActiveVideoPath) && File.Exists(ActiveVideoPath);
        ActiveVideoTitle = HasActiveVideo
            ? (Path.GetFileNameWithoutExtension(ActiveVideoPath) ?? "Live Video Wallpaper")
            : "No live video wallpaper active";
    }

    [RelayCommand]
    public void RefreshLocalVideos()
    {
        Directory.CreateDirectory(_settings.LiveWallpaperDirectory);
        LocalVideos.Clear();
        foreach (var item in YouTubeWallpaperService.ScanLocalVideos(_settings.LiveWallpaperDirectory))
            LocalVideos.Add(item);
    }

    [RelayCommand]
    private async Task SearchYouTubeAsync()
    {
        _searchCts?.Cancel();
        _searchCts = new CancellationTokenSource();
        var ct = _searchCts.Token;

        IsSearching = true;
        SearchResults.Clear();
        try
        {
            var results = await _youtubeService.SearchVideosAsync(SearchQuery, 18, ct);
            foreach (var r in results)
                SearchResults.Add(r);

            _setStatus(results.Count > 0
                ? $"Found {results.Count} YouTube video(s) matching '{SearchQuery}'."
                : $"No YouTube videos found for '{SearchQuery}'.", results.Count == 0);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            _setStatus($"YouTube search failed: {ex.Message}", true);
        }
        finally
        {
            IsSearching = false;
        }
    }

    [RelayCommand]
    private async Task SearchPresetAsync(string? query)
    {
        if (string.IsNullOrWhiteSpace(query)) return;
        SearchQuery = query;
        await SearchYouTubeAsync();
    }

    [RelayCommand]
    private async Task DownloadOnlyAsync(YouTubeVideoSearchItem? item) =>
        await DownloadInternalAsync(item, applyImmediately: false, openPlacementStudio: false);

    [RelayCommand]
    private async Task DownloadAndApplyAsync(YouTubeVideoSearchItem? item) =>
        await DownloadInternalAsync(item, applyImmediately: true, openPlacementStudio: false);

    [RelayCommand]
    private async Task DownloadAndStageAsync(YouTubeVideoSearchItem? item) =>
        await DownloadInternalAsync(item, applyImmediately: false, openPlacementStudio: true);

    private async Task DownloadInternalAsync(YouTubeVideoSearchItem? item, bool applyImmediately, bool openPlacementStudio)
    {
        if (item == null || IsDownloading) return;

        _downloadCts?.Cancel();
        _downloadCts = new CancellationTokenSource();
        var ct = _downloadCts.Token;

        int maxH = SelectedQuality switch
        {
            var s when s.Contains("1080p", StringComparison.OrdinalIgnoreCase) => 1080,
            var s when s.Contains("1440p", StringComparison.OrdinalIgnoreCase) => 1440,
            _ => 2160
        };
        bool preferH264 = SelectedQuality.Contains("H.264", StringComparison.OrdinalIgnoreCase);

        IsDownloading = true;
        DownloadProgress = 0.02;
        DownloadStatusText = $"Starting download for '{item.Title}'...";

        try
        {
            var prog = new Progress<(double Progress, string Status)>(p =>
            {
                DownloadProgress = p.Progress;
                DownloadStatusText = p.Status;
            });

            var dl = await _youtubeService.DownloadWallpaperVideoAsync(
                item.WatchUrl,
                _settings.LiveWallpaperDirectory,
                maxH,
                preferH264,
                item.Title,
                item.ThumbnailUrl,
                prog,
                ct);

            if (!dl.Success || string.IsNullOrWhiteSpace(dl.LocalVideoPath))
            {
                _setStatus(dl.ErrorMessage ?? "Video download failed.", true);
                return;
            }

            DownloadStatusText = "Extracting high-resolution still frame for Placement Studio...";
            var frame = await VideoFrameExtractor.ExtractStillFrameAsync(dl.LocalVideoPath);
            string? stillPath = frame.StillImagePath ?? dl.ThumbnailPath;

            RefreshLocalVideos();
            RegisterVideoInMyWallpapers(dl.LocalVideoPath, stillPath, frame.Width, frame.Height, item.Title, item.Author, dl.ResolutionLabel, item.WatchUrl);

            if (openPlacementStudio)
            {
                _stageVideoInStudio(dl.LocalVideoPath, stillPath);
                _setStatus($"Downloaded {dl.ResolutionLabel} ({dl.CodecLabel}) — frame across monitors and click 'Apply Wallpaper' to play live video.", false);
            }
            else if (applyImmediately)
            {
                await ApplyVideoFileAsync(dl.LocalVideoPath, stillPath);
                _setStatus($"Downloaded {dl.ResolutionLabel} ({dl.CodecLabel}) and applied Live Wallpaper!", false);
            }
            else
            {
                _setStatus($"Saved '{Path.GetFileName(dl.LocalVideoPath)}' ({dl.ResolutionLabel}) to My Wallpapers.", false);
            }
        }
        catch (OperationCanceledException)
        {
            _setStatus("Video download canceled.", false);
        }
        catch (Exception ex)
        {
            _setStatus($"Download error: {ex.Message}", true);
        }
        finally
        {
            IsDownloading = false;
            DownloadProgress = 0;
            DownloadStatusText = string.Empty;
        }
    }

    [RelayCommand]
    private async Task ImportLocalVideoAsync()
    {
        var dlg = new OpenFileDialog
        {
            Title = "Import Local Live Wallpaper Video",
            Filter = "Video Files (*.mp4;*.webm;*.mov;*.wmv;*.mkv)|*.mp4;*.webm;*.mov;*.wmv;*.mkv"
        };
        if (dlg.ShowDialog() != true) return;

        Directory.CreateDirectory(_settings.LiveWallpaperDirectory);
        string dest = Path.Combine(_settings.LiveWallpaperDirectory, Path.GetFileName(dlg.FileName));
        if (!string.Equals(Path.GetFullPath(dlg.FileName), Path.GetFullPath(dest), StringComparison.OrdinalIgnoreCase))
        {
            File.Copy(dlg.FileName, dest, overwrite: true);
        }

        var frame = await VideoFrameExtractor.ExtractStillFrameAsync(dest);
        RefreshLocalVideos();
        await ApplyVideoFileAsync(dest, frame.StillImagePath);
        _setStatus($"Imported & applied '{Path.GetFileName(dest)}'.", false);
    }

    [RelayCommand]
    private async Task ApplyLocalVideoAsync(LocalLiveVideoItem? item)
    {
        if (item == null || !File.Exists(item.FilePath)) return;
        await ApplyVideoFileAsync(item.FilePath, item.StillImagePath);
        _setStatus($"Playing live wallpaper '{item.Title}'.", false);
    }

    [RelayCommand]
    private async Task SaveLocalVideoToMyWallpapersAsync(LocalLiveVideoItem? item)
    {
        if (item == null || !File.Exists(item.FilePath)) return;
        string? still = item.StillImagePath;
        int w = 3840, h = 2160;
        if (string.IsNullOrWhiteSpace(still) || !File.Exists(still))
        {
            var extracted = await VideoFrameExtractor.ExtractStillFrameAsync(item.FilePath);
            still = extracted.StillImagePath;
            if (extracted.Width > 0 && extracted.Height > 0) { w = extracted.Width; h = extracted.Height; }
            RefreshLocalVideos();
        }
        RegisterVideoInMyWallpapers(item.FilePath, still, w, h, item.Title, "Local Video", "HD", string.Empty);
        _setStatus($"Saved '{item.Title}' to My Wallpapers.", false);
    }

    [RelayCommand]
    private async Task StageVideoInPlacementStudioAsync(LocalLiveVideoItem? item)
    {
        string? videoPath = item?.FilePath ?? ActiveVideoPath;
        if (string.IsNullOrWhiteSpace(videoPath) || !File.Exists(videoPath))
        {
            _setStatus("Select or download a live video first to frame in Placement Studio.", true);
            return;
        }

        string? stillPath = item?.StillImagePath;
        if (string.IsNullOrWhiteSpace(stillPath) || !File.Exists(stillPath))
        {
            var extracted = await VideoFrameExtractor.ExtractStillFrameAsync(videoPath);
            stillPath = extracted.StillImagePath;
            RefreshLocalVideos();
        }

        _stageVideoInStudio(videoPath, stillPath);
        _setStatus("Staged live video frame in Placement Studio. Position across monitors, then click 'Apply Wallpaper' to play.", false);
    }

    private void RegisterVideoInMyWallpapers(
        string videoPath,
        string? stillPath,
        int width,
        int height,
        string title,
        string author,
        string resolutionLabel,
        string sourceUrl)
    {
        try
        {
            long fileSize = File.Exists(videoPath) ? new FileInfo(videoPath).Length : 0;
            var wp = new WallpaperItem
            {
                Id = $"livevideo:{Path.GetFileName(videoPath)}",
                FileName = Path.GetFileName(videoPath),
                Title = string.IsNullOrWhiteSpace(title) ? Path.GetFileNameWithoutExtension(videoPath) : title,
                Description = $"Live Video Wallpaper ({resolutionLabel}) by {author}",
                SourceProvider = string.IsNullOrWhiteSpace(sourceUrl) ? "Local Live Video" : "YouTube Live Video",
                SourcePageUrl = sourceUrl,
                Width = width > 0 ? width : 3840,
                Height = height > 0 ? height : 2160,
                FileSizeBytes = fileSize,
                LocalOriginalPath = videoPath,
                LocalThumbnailPath = stillPath,
                PreviewUrl = stillPath ?? string.Empty,
                DownloadedUtc = DateTime.UtcNow,
                Tags = new List<string> { "Live Video", resolutionLabel, "Video" }
            };
            _db.UpsertWallpaper(wp);
            _refreshMyWallpapers?.Invoke();
        }
        catch { }
    }

    [RelayCommand]
    private void StopLiveVideo()
    {
        _settings.ActiveLiveVideoPath = null;
        _db.SaveSettings(_settings);
        _coordinator.ApplyLiveVideoWallpaper(null, null, null);
        RefreshActiveVideoBanner();
        _setStatus("Stopped live video wallpaper.", false);
    }

    [RelayCommand]
    private void DeleteLocalVideo(LocalLiveVideoItem? item)
    {
        if (item == null) return;
        if (string.Equals(ActiveVideoPath, item.FilePath, StringComparison.OrdinalIgnoreCase))
            StopLiveVideo();

        try { if (File.Exists(item.FilePath)) File.Delete(item.FilePath); } catch { }
        try
        {
            string still = Path.ChangeExtension(item.FilePath, ".still.png");
            string thumb = Path.ChangeExtension(item.FilePath, ".thumb.jpg");
            if (File.Exists(still)) File.Delete(still);
            if (File.Exists(thumb)) File.Delete(thumb);
        }
        catch { }

        RefreshLocalVideos();
        _setStatus($"Deleted '{item.Title}'.", false);
    }

    [RelayCommand]
    private void OpenLiveFolder()
    {
        Directory.CreateDirectory(_settings.LiveWallpaperDirectory);
        Process.Start(new ProcessStartInfo("explorer.exe", $"\"{_settings.LiveWallpaperDirectory}\"") { UseShellExecute = true });
    }

    private async Task ApplyVideoFileAsync(string videoPath, string? stillPath)
    {
        if (string.IsNullOrWhiteSpace(stillPath) || !File.Exists(stillPath))
        {
            var frame = await VideoFrameExtractor.ExtractStillFrameAsync(videoPath);
            stillPath = frame.StillImagePath;
        }

        _settings.ActiveLiveVideoPath = videoPath;
        _settings.ActiveLiveVideoStillPath = stillPath;
        _db.SaveSettings(_settings);
        RefreshActiveVideoBanner();

        _coordinator.ApplyLiveVideoWallpaper(videoPath, _getActivePlan(), _getBackgroundHex());
    }

    partial void OnLoopLiveVideoChanged(bool value)
    {
        _settings.LoopLiveVideoWallpaper = value;
        _db.SaveSettings(_settings);
        if (HasActiveVideo) _coordinator.ApplyLiveVideoWallpaper(ActiveVideoPath, _getActivePlan(), _getBackgroundHex());
    }

    partial void OnPauseLiveOnBatteryChanged(bool value)
    {
        _settings.PauseLiveVideoOnBattery = value;
        _db.SaveSettings(_settings);
        if (HasActiveVideo) _coordinator.ApplyLiveVideoWallpaper(ActiveVideoPath, _getActivePlan(), _getBackgroundHex());
    }

    partial void OnPauseLiveOnFullScreenChanged(bool value)
    {
        _settings.PauseLiveVideoOnFullScreen = value;
        _db.SaveSettings(_settings);
        if (HasActiveVideo) _coordinator.ApplyLiveVideoWallpaper(ActiveVideoPath, _getActivePlan(), _getBackgroundHex());
    }
}
