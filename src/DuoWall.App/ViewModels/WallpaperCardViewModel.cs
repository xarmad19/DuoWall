using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using DuoWall.App.Helpers;
using DuoWall.Core.Gallery;
using DuoWall.Core.Models;

namespace DuoWall.App.ViewModels;

public sealed partial class WallpaperCardViewModel : ObservableObject
{
    public WallpaperItem Model { get; }

    [ObservableProperty] private BitmapSource? _thumbnailBitmap;
    [ObservableProperty] private BitmapSource? _previewBitmap;
    [ObservableProperty] private bool _isFavorite;
    [ObservableProperty] private bool _isDownloaded;
    [ObservableProperty] private bool _isDownloading;
    [ObservableProperty] private double _downloadProgress;
    private string _loadedPreviewSource = string.Empty;

    public string Id => Model.Id;
    public string FileName => Model.FileName;
    public string Title => Model.Title;
    public string Description => Model.Description;
    public string SourceProvider => Model.SourceProvider;
    public string SourcePageUrl => Model.SourcePageUrl;
    public string ResolutionLabel => Model.ResolutionLabel;
    public string AspectRatioLabel => Model.AspectRatioLabel;
    public string FileSizeLabel => Model.FileSizeLabel;
    public string TagsSummary => Model.TagsSummary;
    public bool RequiresAuthentication => Model.RequiresAuthentication;
    public string? AccessRestrictionReason => Model.AccessRestrictionReason;
    public IReadOnlyList<string> Tags => Model.Tags;

    public static bool IsVideoFile(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return false;
        string ext = System.IO.Path.GetExtension(path);
        return ext.Equals(".mp4", StringComparison.OrdinalIgnoreCase)
            || ext.Equals(".webm", StringComparison.OrdinalIgnoreCase)
            || ext.Equals(".mov", StringComparison.OrdinalIgnoreCase)
            || ext.Equals(".wmv", StringComparison.OrdinalIgnoreCase)
            || ext.Equals(".mkv", StringComparison.OrdinalIgnoreCase);
    }

    public string BestPreviewSource => WallpaperItem.ResolveBestPreviewSource(Model);

    public WallpaperCardViewModel(WallpaperItem model)
    {
        Model = model;
        _isFavorite = model.IsFavorite;
        _isDownloaded = model.IsDownloaded;
    }

    public void RefreshFromModel()
    {
        IsFavorite = Model.IsFavorite;
        IsDownloaded = Model.IsDownloaded;
        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(Description));
        OnPropertyChanged(nameof(ResolutionLabel));
        OnPropertyChanged(nameof(AspectRatioLabel));
        OnPropertyChanged(nameof(FileSizeLabel));
        OnPropertyChanged(nameof(TagsSummary));
        OnPropertyChanged(nameof(BestPreviewSource));
        OnPropertyChanged(nameof(Tags));
    }

    public async Task EnsureThumbnailLoadedAsync(IWallpaperSourceProvider provider, string cacheDir, CancellationToken ct = default)
    {
        if (ThumbnailBitmap != null) return;

        if (!string.IsNullOrWhiteSpace(Model.LocalOriginalPath) && System.IO.File.Exists(Model.LocalOriginalPath) && !IsVideoFile(Model.LocalOriginalPath))
        {
            ThumbnailBitmap = await ImageCacheHelper.LoadBitmapAsync(Model.LocalOriginalPath, decodePixelWidth: 460, cancellationToken: ct);
            return;
        }

        if (!string.IsNullOrWhiteSpace(Model.LocalThumbnailPath) && System.IO.File.Exists(Model.LocalThumbnailPath))
        {
            ThumbnailBitmap = await ImageCacheHelper.LoadBitmapAsync(Model.LocalThumbnailPath, decodePixelWidth: 460, cancellationToken: ct);
            if (ThumbnailBitmap != null) return;
        }

        if (!string.IsNullOrWhiteSpace(Model.PreviewUrl) && System.IO.File.Exists(Model.PreviewUrl) && !IsVideoFile(Model.PreviewUrl))
        {
            ThumbnailBitmap = await ImageCacheHelper.LoadBitmapAsync(Model.PreviewUrl, decodePixelWidth: 460, cancellationToken: ct);
            if (ThumbnailBitmap != null) return;
        }

        string? cachedPath = await provider.GetOrCacheThumbnailAsync(Model, cacheDir, ct);
        if (!string.IsNullOrWhiteSpace(cachedPath))
        {
            ThumbnailBitmap = await ImageCacheHelper.LoadBitmapAsync(cachedPath, decodePixelWidth: 460, cancellationToken: ct);
        }
    }

    public async Task EnsurePreviewLoadedAsync(string cacheDir, CancellationToken ct = default)
    {
        string src = BestPreviewSource;
        if (string.IsNullOrWhiteSpace(src)) return;
        if (PreviewBitmap != null && string.Equals(_loadedPreviewSource, src, StringComparison.OrdinalIgnoreCase))
            return;

        var loaded = await ImageCacheHelper.LoadBitmapAsync(
            src,
            decodePixelWidth: 2600,
            referrer: Model.SourcePageUrl,
            diskCacheDirectory: cacheDir,
            cancellationToken: ct);
        if (loaded != null)
        {
            _loadedPreviewSource = src;
            PreviewBitmap = loaded;
        }
    }
}
