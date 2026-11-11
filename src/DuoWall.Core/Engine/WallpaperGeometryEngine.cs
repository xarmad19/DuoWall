using DuoWall.Core.Models;

namespace DuoWall.Core.Engine;

/// <summary>
/// Pure, deterministic multi-monitor geometry and projection engine.
/// Computes exact per-monitor source cropping and destination rectangles
/// from connected display geometry and user placement preferences.
/// </summary>
public sealed class WallpaperGeometryEngine
{
    public CompositionPlan ComputePlan(
        IReadOnlyList<MonitorInfo> monitors,
        int sourceWidth,
        int sourceHeight,
        string sourceImagePath,
        PlacementSettings settings,
        IReadOnlyDictionary<string, (int Width, int Height)>? perMonitorSourceDimensions = null)
    {
        if (monitors == null || monitors.Count == 0)
            throw new ArgumentException("At least one monitor is required.", nameof(monitors));
        if (sourceWidth <= 0 || sourceHeight <= 0)
            throw new ArgumentOutOfRangeException(nameof(sourceWidth), "Source image dimensions must be positive.");

        var active = monitors
            .Where(m => m.IsConnected && m.IsIncluded && !m.Bounds.IsEmpty)
            .ToList();

        if (active.Count == 0)
        {
            active = monitors
                .Where(m => m.IsConnected && !m.Bounds.IsEmpty)
                .ToList();
        }

        if (active.Count == 0)
            throw new InvalidOperationException("No valid active monitors found.");

        int virtMinX = active.Min(m => m.Bounds.X);
        int virtMinY = active.Min(m => m.Bounds.Y);
        int virtMaxX = active.Max(m => m.Bounds.Right);
        int virtMaxY = active.Max(m => m.Bounds.Bottom);
        int virtWidth = Math.Max(1, virtMaxX - virtMinX);
        int virtHeight = Math.Max(1, virtMaxY - virtMinY);

        var canvasRects = BuildCanvasRects(active, settings);
        double combinedWidth = Math.Max(1.0, canvasRects.Values.Max(r => r.Right));
        double combinedHeight = Math.Max(1.0, canvasRects.Values.Max(r => r.Bottom));

        if (settings.Mode == WallpaperPlacementMode.Individual)
        {
            var individualSlices = new List<MonitorSlicePlan>(active.Count);
            foreach (var mon in active)
            {
                var hasAssign = settings.PerMonitorAssignments.TryGetValue(mon.Id, out var assign);
                string imgPath = hasAssign && !string.IsNullOrWhiteSpace(assign!.ImagePath)
                    ? assign.ImagePath
                    : sourceImagePath;

                int monSrcW = sourceWidth;
                int monSrcH = sourceHeight;
                if (perMonitorSourceDimensions != null &&
                    perMonitorSourceDimensions.TryGetValue(imgPath, out var dims) &&
                    dims.Width > 0 && dims.Height > 0)
                {
                    monSrcW = dims.Width;
                    monSrcH = dims.Height;
                }

                var subMode = hasAssign ? assign!.SubMode : WallpaperPlacementMode.Fill;
                if (subMode is WallpaperPlacementMode.PanoramicSpan or WallpaperPlacementMode.Individual)
                    subMode = WallpaperPlacementMode.Fill;

                var singleCanvas = new RectD(0, 0, mon.PixelWidth, mon.PixelHeight);
                var imgRect = MapImageToCanvas(
                    singleCanvas.Width,
                    singleCanvas.Height,
                    monSrcW,
                    monSrcH,
                    subMode,
                    hasAssign ? assign!.Zoom : settings.Zoom,
                    hasAssign ? assign!.PanX : settings.PanX,
                    hasAssign ? assign!.PanY : settings.PanY,
                    hasAssign ? assign!.FocalX : settings.FocalX,
                    hasAssign ? assign!.FocalY : settings.FocalY,
                    0, 0, 0, 0,
                    pixelsPerCanvasUnit: 1.0);

                var (dstRect, srcRect) = ComputeMonitorIntersection(
                    singleCanvas,
                    imgRect,
                    mon.PixelWidth,
                    mon.PixelHeight,
                    0, 0, monSrcW, monSrcH);

                individualSlices.Add(new MonitorSlicePlan
                {
                    Monitor = mon,
                    CanvasRect = canvasRects[mon.Id],
                    DestinationPixelRect = dstRect,
                    SourcePixelRect = srcRect,
                    SourceImagePath = imgPath
                });
            }

            return new CompositionPlan
            {
                SourceWidth = sourceWidth,
                SourceHeight = sourceHeight,
                CombinedCanvasWidth = combinedWidth,
                CombinedCanvasHeight = combinedHeight,
                CoordinateSpace = settings.CoordinateSpace,
                Mode = settings.Mode,
                VirtualDesktopWidth = virtWidth,
                VirtualDesktopHeight = virtHeight,
                ImageCanvasRect = new RectD(0, 0, combinedWidth, combinedHeight),
                Slices = individualSlices,
                ResolutionAdvisory = BuildResolutionAdvisory(individualSlices, sourceWidth, sourceHeight, virtWidth, virtHeight)
            };
        }

        // Combined multi-monitor placement (PanoramicSpan, Fill, Fit, Stretch, Center)
        double cropL = Math.Clamp(settings.CropLeft, 0.0, 0.45);
        double cropT = Math.Clamp(settings.CropTop, 0.0, 0.45);
        double cropR = Math.Clamp(settings.CropRight, 0.0, 0.45);
        double cropB = Math.Clamp(settings.CropBottom, 0.0, 0.45);

        double effSrcX = sourceWidth * cropL;
        double effSrcY = sourceHeight * cropT;
        double effSrcW = Math.Max(1.0, sourceWidth * (1.0 - cropL - cropR));
        double effSrcH = Math.Max(1.0, sourceHeight * (1.0 - cropT - cropB));

        double avgPxPerUnit = settings.CoordinateSpace == GeometryCoordinateSpace.VirtualPixels
            ? 1.0
            : active.Average(m => (m.PixelsPerMmX + m.PixelsPerMmY) * 0.5);

        var imageCanvasRect = MapImageToCanvas(
            combinedWidth,
            combinedHeight,
            effSrcW,
            effSrcH,
            settings.Mode,
            settings.Zoom,
            settings.PanX,
            settings.PanY,
            settings.FocalX,
            settings.FocalY,
            cropL, cropT, cropR, cropB,
            avgPxPerUnit);

        var slices = new List<MonitorSlicePlan>(active.Count);
        foreach (var mon in active)
        {
            var monCanvasRect = canvasRects[mon.Id];
            var (dstRect, srcRect) = ComputeMonitorIntersection(
                monCanvasRect,
                imageCanvasRect,
                mon.PixelWidth,
                mon.PixelHeight,
                effSrcX,
                effSrcY,
                effSrcW,
                effSrcH);

            slices.Add(new MonitorSlicePlan
            {
                Monitor = mon,
                CanvasRect = monCanvasRect,
                DestinationPixelRect = dstRect,
                SourcePixelRect = srcRect,
                SourceImagePath = sourceImagePath
            });
        }

        return new CompositionPlan
        {
            SourceWidth = sourceWidth,
            SourceHeight = sourceHeight,
            CombinedCanvasWidth = combinedWidth,
            CombinedCanvasHeight = combinedHeight,
            CoordinateSpace = settings.CoordinateSpace,
            Mode = settings.Mode,
            VirtualDesktopWidth = virtWidth,
            VirtualDesktopHeight = virtHeight,
            ImageCanvasRect = imageCanvasRect,
            Slices = slices,
            ResolutionAdvisory = BuildResolutionAdvisory(slices, sourceWidth, sourceHeight, virtWidth, virtHeight)
        };
    }

    /// <summary>
    /// Computes normalized canvas coordinates for each monitor in either VirtualPixels or PhysicalDimensions space.
    /// Guarantees all canvas rects have non-negative X/Y starting from (0, 0).
    /// </summary>
    public Dictionary<string, RectD> BuildCanvasRects(
        IReadOnlyList<MonitorInfo> active,
        PlacementSettings settings)
    {
        var result = new Dictionary<string, RectD>(active.Count);

        if (settings.CoordinateSpace == GeometryCoordinateSpace.VirtualPixels || active.Count == 1)
        {
            if (settings.CoordinateSpace == GeometryCoordinateSpace.PhysicalDimensions && active.Count == 1)
            {
                var single = active[0];
                result[single.Id] = new RectD(0, 0, single.EffectivePhysicalWidthMm, single.EffectivePhysicalHeightMm);
                return result;
            }

            int minX = active.Min(m => m.Bounds.X);
            int minY = active.Min(m => m.Bounds.Y);
            foreach (var m in active)
            {
                result[m.Id] = new RectD(
                    m.Bounds.X - minX,
                    m.Bounds.Y - minY,
                    m.Bounds.Width,
                    m.Bounds.Height);
            }
            return result;
        }

        // PhysicalDimensions layout for 2+ monitors:
        // Propagate physical panel positions from a primary/leftmost anchor along shared Win32 edges,
        // respecting bezel gaps and desk vertical alignment / user mm offsets.
        var placed = new Dictionary<string, RectD>();
        var ordered = active
            .OrderByDescending(m => m.IsPrimary)
            .ThenBy(m => m.Bounds.X)
            .ThenBy(m => m.Bounds.Y)
            .ToList();

        var anchor = ordered[0];
        placed[anchor.Id] = new RectD(0, 0, anchor.EffectivePhysicalWidthMm, anchor.EffectivePhysicalHeightMm);

        var queue = new Queue<MonitorInfo>();
        queue.Enqueue(anchor);

        const int EdgeTolerancePx = 12;

        while (queue.Count > 0)
        {
            var cur = queue.Dequeue();
            var curRect = placed[cur.Id];

            foreach (var cand in ordered)
            {
                if (placed.ContainsKey(cand.Id))
                    continue;

                double candW = cand.EffectivePhysicalWidthMm;
                double candH = cand.EffectivePhysicalHeightMm;

                bool sharesVertOverlap = Math.Min(cur.Bounds.Bottom, cand.Bounds.Bottom) -
                                         Math.Max(cur.Bounds.Y, cand.Bounds.Y) > 0;
                bool sharesHorizOverlap = Math.Min(cur.Bounds.Right, cand.Bounds.Right) -
                                          Math.Max(cur.Bounds.X, cand.Bounds.X) > 0;

                // Candidate is immediately to the RIGHT of current
                if (sharesVertOverlap && Math.Abs(cand.Bounds.X - cur.Bounds.Right) <= EdgeTolerancePx)
                {
                    double bezelX = settings.EnableBezelCompensation ? (cur.BezelRightMm + cand.BezelLeftMm) : 0.0;
                    double yOffsetRatio = cur.Bounds.Height > 0
                        ? (double)(cand.Bounds.Y - cur.Bounds.Y) / cur.Bounds.Height
                        : 0.0;
                    double yMm = curRect.Y + yOffsetRatio * curRect.Height;
                    placed[cand.Id] = new RectD(curRect.Right + bezelX, yMm, candW, candH);
                    queue.Enqueue(cand);
                    continue;
                }

                // Candidate is immediately to the LEFT of current
                if (sharesVertOverlap && Math.Abs(cand.Bounds.Right - cur.Bounds.X) <= EdgeTolerancePx)
                {
                    double bezelX = settings.EnableBezelCompensation ? (cand.BezelRightMm + cur.BezelLeftMm) : 0.0;
                    double yOffsetRatio = cur.Bounds.Height > 0
                        ? (double)(cand.Bounds.Y - cur.Bounds.Y) / cur.Bounds.Height
                        : 0.0;
                    double yMm = curRect.Y + yOffsetRatio * curRect.Height;
                    placed[cand.Id] = new RectD(curRect.X - bezelX - candW, yMm, candW, candH);
                    queue.Enqueue(cand);
                    continue;
                }

                // Candidate is immediately BELOW current
                if (sharesHorizOverlap && Math.Abs(cand.Bounds.Y - cur.Bounds.Bottom) <= EdgeTolerancePx)
                {
                    double bezelY = settings.EnableBezelCompensation ? (cur.BezelBottomMm + cand.BezelTopMm) : 0.0;
                    double xOffsetRatio = cur.Bounds.Width > 0
                        ? (double)(cand.Bounds.X - cur.Bounds.X) / cur.Bounds.Width
                        : 0.0;
                    double xMm = curRect.X + xOffsetRatio * curRect.Width;
                    placed[cand.Id] = new RectD(xMm, curRect.Bottom + bezelY, candW, candH);
                    queue.Enqueue(cand);
                    continue;
                }

                // Candidate is immediately ABOVE current
                if (sharesHorizOverlap && Math.Abs(cand.Bounds.Bottom - cur.Bounds.Y) <= EdgeTolerancePx)
                {
                    double bezelY = settings.EnableBezelCompensation ? (cand.BezelBottomMm + cur.BezelTopMm) : 0.0;
                    double xOffsetRatio = cur.Bounds.Width > 0
                        ? (double)(cand.Bounds.X - cur.Bounds.X) / cur.Bounds.Width
                        : 0.0;
                    double xMm = curRect.X + xOffsetRatio * curRect.Width;
                    placed[cand.Id] = new RectD(xMm, curRect.Y - bezelY - candH, candW, candH);
                    queue.Enqueue(cand);
                }
            }
        }

        // Fallback for any disconnected/non-touching monitor in virtual space
        double avgMmPerPx = active.Average(m => (1.0 / m.PixelsPerMmX + 1.0 / m.PixelsPerMmY) * 0.5);
        foreach (var m in ordered)
        {
            if (placed.ContainsKey(m.Id)) continue;
            double dxMm = (m.Bounds.X - anchor.Bounds.X) * avgMmPerPx;
            double dyMm = (m.Bounds.Y - anchor.Bounds.Y) * avgMmPerPx;
            placed[m.Id] = new RectD(dxMm, dyMm, m.EffectivePhysicalWidthMm, m.EffectivePhysicalHeightMm);
        }

        // Apply desk vertical alignment for single-row side-by-side setups (when monitors share a horizontal band)
        bool isHorizontalBand = active.All(m =>
            Math.Min(anchor.Bounds.Bottom, m.Bounds.Bottom) - Math.Max(anchor.Bounds.Y, m.Bounds.Y) > 0);

        if (isHorizontalBand && settings.PhysicalAlignment != PhysicalVerticalAlignment.Custom)
        {
            double maxHeightMm = placed.Values.Max(r => r.Height);
            foreach (var m in active)
            {
                var r = placed[m.Id];
                double alignedY = settings.PhysicalAlignment switch
                {
                    PhysicalVerticalAlignment.AlignTop => 0.0,
                    PhysicalVerticalAlignment.AlignCenter => (maxHeightMm - r.Height) * 0.5,
                    PhysicalVerticalAlignment.AlignBottom => maxHeightMm - r.Height,
                    _ => r.Y
                };
                placed[m.Id] = new RectD(r.X, alignedY, r.Width, r.Height);
            }
        }

        // Apply user fine-tuning offsets in millimeters
        foreach (var m in active)
        {
            var r = placed[m.Id];
            placed[m.Id] = new RectD(
                r.X + m.UserOffsetMmX,
                r.Y + m.UserOffsetMmY,
                r.Width,
                r.Height);
        }

        // Shift all rects so minimum X and Y sit cleanly at (0, 0)
        double minMmX = placed.Values.Min(r => r.X);
        double minMmY = placed.Values.Min(r => r.Y);
        foreach (var m in active)
        {
            var r = placed[m.Id];
            result[m.Id] = new RectD(r.X - minMmX, r.Y - minMmY, r.Width, r.Height);
        }

        return result;
    }

    private static RectD MapImageToCanvas(
        double canvasW,
        double canvasH,
        double effSrcW,
        double effSrcH,
        WallpaperPlacementMode mode,
        double zoom,
        double panX,
        double panY,
        double focalX,
        double focalY,
        double cropL,
        double cropT,
        double cropR,
        double cropB,
        double pixelsPerCanvasUnit)
    {
        double clampedZoom = Math.Clamp(zoom, 0.20, 8.0);
        double fx = Math.Clamp(focalX, 0.0, 1.0);
        double fy = Math.Clamp(focalY, 0.0, 1.0);

        double imgW;
        double imgH;

        switch (mode)
        {
            case WallpaperPlacementMode.Fit:
            {
                double baseScale = Math.Min(canvasW / effSrcW, canvasH / effSrcH);
                double scale = baseScale * clampedZoom;
                imgW = effSrcW * scale;
                imgH = effSrcH * scale;
                break;
            }
            case WallpaperPlacementMode.Stretch:
            {
                imgW = canvasW * clampedZoom;
                imgH = canvasH * clampedZoom;
                break;
            }
            case WallpaperPlacementMode.Center:
            {
                double unitScale = pixelsPerCanvasUnit > 0 ? 1.0 / pixelsPerCanvasUnit : 1.0;
                double scale = unitScale * clampedZoom;
                imgW = effSrcW * scale;
                imgH = effSrcH * scale;
                break;
            }
            case WallpaperPlacementMode.PanoramicSpan:
            case WallpaperPlacementMode.Fill:
            default:
            {
                double baseScale = Math.Max(canvasW / effSrcW, canvasH / effSrcH);
                double scale = baseScale * clampedZoom;
                imgW = effSrcW * scale;
                imgH = effSrcH * scale;
                break;
            }
        }

        double x = fx * (canvasW - imgW) + panX * canvasW;
        double y = fy * (canvasH - imgH) + panY * canvasH;

        return new RectD(x, y, imgW, imgH);
    }

    private static (RectD DestinationPixelRect, RectD SourcePixelRect) ComputeMonitorIntersection(
        RectD monitorCanvasRect,
        RectD imageCanvasRect,
        int monitorPixelWidth,
        int monitorPixelHeight,
        double effSrcX,
        double effSrcY,
        double effSrcW,
        double effSrcH)
    {
        double interLeft = Math.Max(monitorCanvasRect.X, imageCanvasRect.X);
        double interTop = Math.Max(monitorCanvasRect.Y, imageCanvasRect.Y);
        double interRight = Math.Min(monitorCanvasRect.Right, imageCanvasRect.Right);
        double interBottom = Math.Min(monitorCanvasRect.Bottom, imageCanvasRect.Bottom);

        if (interRight <= interLeft || interBottom <= interTop ||
            monitorCanvasRect.Width <= 0 || monitorCanvasRect.Height <= 0 ||
            imageCanvasRect.Width <= 0 || imageCanvasRect.Height <= 0)
        {
            return (new RectD(0, 0, 0, 0), new RectD(0, 0, 0, 0));
        }

        double dstLeft = ((interLeft - monitorCanvasRect.X) / monitorCanvasRect.Width) * monitorPixelWidth;
        double dstTop = ((interTop - monitorCanvasRect.Y) / monitorCanvasRect.Height) * monitorPixelHeight;
        double dstRight = ((interRight - monitorCanvasRect.X) / monitorCanvasRect.Width) * monitorPixelWidth;
        double dstBottom = ((interBottom - monitorCanvasRect.Y) / monitorCanvasRect.Height) * monitorPixelHeight;

        double srcLeft = effSrcX + ((interLeft - imageCanvasRect.X) / imageCanvasRect.Width) * effSrcW;
        double srcTop = effSrcY + ((interTop - imageCanvasRect.Y) / imageCanvasRect.Height) * effSrcH;
        double srcRight = effSrcX + ((interRight - imageCanvasRect.X) / imageCanvasRect.Width) * effSrcW;
        double srcBottom = effSrcY + ((interBottom - imageCanvasRect.Y) / imageCanvasRect.Height) * effSrcH;

        return (
            new RectD(dstLeft, dstTop, dstRight - dstLeft, dstBottom - dstTop),
            new RectD(srcLeft, srcTop, srcRight - srcLeft, srcBottom - srcTop)
        );
    }

    private static string? BuildResolutionAdvisory(
        IReadOnlyList<MonitorSlicePlan> slices,
        int srcW,
        int srcH,
        int virtW,
        int virtH)
    {
        var worst = slices.OrderByDescending(s => s.MaxUpscaleRatio).FirstOrDefault();
        if (worst == null || !worst.HasResolutionWarning)
            return null;

        double pct = (worst.MaxUpscaleRatio - 1.0) * 100.0;
        return $"Resolution notice: Source image ({srcW}×{srcH}) is upscaled by {pct:0}% on {worst.Monitor.DisplayLabel} ({worst.Monitor.ResolutionLabel}) across your {virtW}×{virtH} desktop. Try a higher-resolution wallpaper (e.g., 5K/8K 32:9) or reduce zoom for maximum sharpness.";
    }
}
