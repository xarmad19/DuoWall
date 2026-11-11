using DuoWall.Core.Engine;
using DuoWall.Core.Models;
using Xunit;

namespace DuoWall.Tests;

public sealed class GeometryEngineTests
{
    private readonly WallpaperGeometryEngine _engine = new();

    private static MonitorInfo MakeMonitor(
        string id,
        int x, int y, int w, int h,
        bool isPrimary = false,
        double physW = 0, double physH = 0,
        double bezelL = 7, double bezelR = 7, double bezelT = 7, double bezelB = 12) => new()
    {
        Id = id,
        DeviceName = id,
        FriendlyName = id,
        Bounds = new PixelRect(x, y, w, h),
        WorkingArea = new PixelRect(x, y, w, h),
        NativeWidth = w,
        NativeHeight = h,
        IsPrimary = isPrimary,
        PhysicalWidthMm = physW,
        PhysicalHeightMm = physH,
        HasHardwarePhysicalDimensions = physW > 0 && physH > 0,
        BezelLeftMm = bezelL,
        BezelRightMm = bezelR,
        BezelTopMm = bezelT,
        BezelBottomMm = bezelB,
        IsConnected = true,
        IsIncluded = true
    };

    [Fact]
    public void NegativeLeftCoordinates_Dual1080p_MapsExactSeamlessSplit()
    {
        // Matches user's actual Windows virtual coordinates: Left at X=-1920, Right Primary at X=0
        var monitors = new[]
        {
            MakeMonitor("LEFT", -1920, 0, 1920, 1080, isPrimary: false, physW: 527, physH: 296),
            MakeMonitor("RIGHT", 0, 0, 1920, 1080, isPrimary: true, physW: 597, physH: 336)
        };

        var settings = new PlacementSettings
        {
            Mode = WallpaperPlacementMode.PanoramicSpan,
            CoordinateSpace = GeometryCoordinateSpace.VirtualPixels
        };

        var plan = _engine.ComputePlan(monitors, 7680, 2160, "test.jpg", settings);

        Assert.Equal(3840, plan.VirtualDesktopWidth);
        Assert.Equal(1080, plan.VirtualDesktopHeight);
        Assert.Equal(2, plan.Slices.Count);

        var left = plan.Slices[0];
        var right = plan.Slices[1];

        Assert.Equal(0, left.SourcePixelRect.X, precision: 3);
        Assert.Equal(3840, left.SourcePixelRect.Width, precision: 3);
        Assert.Equal(3840, right.SourcePixelRect.X, precision: 3);
        Assert.Equal(3840, right.SourcePixelRect.Width, precision: 3);
        Assert.Equal(left.SourcePixelRect.Right, right.SourcePixelRect.X, precision: 3);
        Assert.Equal(0, left.DestinationPixelRect.X, precision: 3);
        Assert.Equal(1920, left.DestinationPixelRect.Width, precision: 3);
        Assert.Equal(1920, right.DestinationPixelRect.Width, precision: 3);
        Assert.False(plan.HasInsufficientResolution);
    }

    [Fact]
    public void MixedResolution_1080pLeft_1440pRight_WithVerticalOffset_AlignsSeamWithoutClipping()
    {
        var monitors = new[]
        {
            MakeMonitor("DELL_1080", 0, 180, 1920, 1080, isPrimary: false),
            MakeMonitor("MAIN_1440", 1920, 0, 2560, 1440, isPrimary: true)
        };

        var settings = new PlacementSettings
        {
            Mode = WallpaperPlacementMode.PanoramicSpan,
            CoordinateSpace = GeometryCoordinateSpace.VirtualPixels
        };

        // Combined canvas: 4480 x 1440 (aspect ~3.111). Source: 7680 x 2160 (aspect 3.556).
        // Base scale = max(4480/7680, 1440/2160) = 0.666667 -> image spans 5120x1440, centered at X=-320
        var plan = _engine.ComputePlan(monitors, 7680, 2160, "test.jpg", settings);

        Assert.Equal(4480, plan.VirtualDesktopWidth);
        Assert.Equal(1440, plan.VirtualDesktopHeight);

        var left = plan.Slices[0];
        var right = plan.Slices[1];

        // Seam continuity at X=1920
        Assert.Equal(left.SourcePixelRect.Right, right.SourcePixelRect.X, precision: 4);

        // Vertical offset mapping: Left starts at Y=180/1440 = 12.5% of source height (= 270px)
        Assert.Equal(270.0, left.SourcePixelRect.Y, precision: 3);
        Assert.Equal(1620.0, left.SourcePixelRect.Height, precision: 3);
        Assert.Equal(0.0, right.SourcePixelRect.Y, precision: 3);
        Assert.Equal(2160.0, right.SourcePixelRect.Height, precision: 3);

        // Full native pixel fill on both monitors (zero unwanted black bars or clipping)
        Assert.Equal(1920, left.DestinationPixelRect.Width, precision: 3);
        Assert.Equal(1080, left.DestinationPixelRect.Height, precision: 3);
        Assert.Equal(2560, right.DestinationPixelRect.Width, precision: 3);
        Assert.Equal(1440, right.DestinationPixelRect.Height, precision: 3);
    }

    [Fact]
    public void PhysicalDimensions_24InchLeft_27InchRight_CompensatesPhysicalHeightAndBezel()
    {
        // Exact user hardware: 24" Dell (527x296 mm) + 27" Planar (597x336 mm) both at 1920x1080
        var monitors = new[]
        {
            MakeMonitor("DELL_P2419H", -1920, 0, 1920, 1080, isPrimary: false, physW: 527, physH: 296, bezelL: 7, bezelR: 7),
            MakeMonitor("PLANAR_27", 0, 0, 1920, 1080, isPrimary: true, physW: 597, physH: 336, bezelL: 7, bezelR: 7)
        };

        var settings = new PlacementSettings
        {
            Mode = WallpaperPlacementMode.PanoramicSpan,
            CoordinateSpace = GeometryCoordinateSpace.PhysicalDimensions,
            PhysicalAlignment = PhysicalVerticalAlignment.AlignBottom,
            EnableBezelCompensation = true
        };

        var plan = _engine.ComputePlan(monitors, 7680, 2160, "test.jpg", settings);

        var left = plan.Slices[0];
        var right = plan.Slices[1];

        // Combined physical width = 527 + 14 (bezel) + 597 = 1138 mm; height = 336 mm
        Assert.Equal(1138.0, plan.CombinedCanvasWidth, precision: 3);
        Assert.Equal(336.0, plan.CombinedCanvasHeight, precision: 3);

        // Bottom-aligned: left monitor sits at Y = 336 - 296 = 40 mm
        Assert.Equal(40.0, left.CanvasRect.Y, precision: 3);
        Assert.Equal(0.0, right.CanvasRect.Y, precision: 3);

        // Bezel gap skips the hidden bezel strip in the source image so scenery continues behind the frame!
        Assert.True(right.SourcePixelRect.X > left.SourcePixelRect.Right);

        // Bottom of both screens should sample the exact same bottom row of the source image!
        Assert.Equal(left.SourcePixelRect.Bottom, right.SourcePixelRect.Bottom, precision: 3);
        // Top of smaller 24" monitor samples lower in source image than top of taller 27" monitor!
        Assert.True(left.SourcePixelRect.Y > right.SourcePixelRect.Y);
    }

    [Fact]
    public void ThreeMonitors_PortraitLeft_LandscapeCenter_StackedTop_HandlesNegativeCoordinates()
    {
        var monitors = new[]
        {
            MakeMonitor("PORTRAIT_LEFT", -1080, -300, 1080, 1920, physW: 296, physH: 527),
            MakeMonitor("CENTER_1440", 0, 0, 2560, 1440, isPrimary: true, physW: 597, physH: 336),
            MakeMonitor("TOP_1080", 320, -1080, 1920, 1080, physW: 527, physH: 296)
        };

        var settings = new PlacementSettings
        {
            Mode = WallpaperPlacementMode.PanoramicSpan,
            CoordinateSpace = GeometryCoordinateSpace.VirtualPixels
        };

        var plan = _engine.ComputePlan(monitors, 7680, 4320, "test.jpg", settings);

        // Bounds: X from -1080 to 2560 => 3640; Y from -1080 to (-300+1920=1620) => 2700
        Assert.Equal(3640, plan.VirtualDesktopWidth);
        Assert.Equal(2700, plan.VirtualDesktopHeight);
        Assert.Equal(3, plan.Slices.Count);

        // Right edge of portrait left (X=0) meets left edge of center (X=0) seamlessly where Y overlaps (0..1440)
        var left = plan.Slices[0];
        var center = plan.Slices[1];
        Assert.Equal(left.SourcePixelRect.Right, center.SourcePixelRect.X, precision: 3);
    }

    [Fact]
    public void PlacementModes_Fit_Stretch_Center_And_Zoom_BehaveAccurately()
    {
        var monitors = new[]
        {
            MakeMonitor("M1", 0, 0, 1920, 1080, isPrimary: true),
            MakeMonitor("M2", 1920, 0, 1920, 1080)
        };

        // Fit mode with 16:9 image (1920x1080) on 32:9 desktop (3840x1080)
        var fitPlan = _engine.ComputePlan(monitors, 1920, 1080, "16x9.jpg", new PlacementSettings
        {
            Mode = WallpaperPlacementMode.Fit,
            CoordinateSpace = GeometryCoordinateSpace.VirtualPixels
        });

        // Entire 1920x1080 image sits centered spanning X=960..2880 (half of M1 + half of M2)
        Assert.Equal(960, fitPlan.ImageCanvasRect.X, precision: 3);
        Assert.Equal(1920, fitPlan.ImageCanvasRect.Width, precision: 3);
        Assert.Equal(960, fitPlan.Slices[0].DestinationPixelRect.X, precision: 3);
        Assert.Equal(960, fitPlan.Slices[0].DestinationPixelRect.Width, precision: 3);
        Assert.Equal(0, fitPlan.Slices[1].DestinationPixelRect.X, precision: 3);
        Assert.Equal(960, fitPlan.Slices[1].DestinationPixelRect.Width, precision: 3);

        // Stretch mode fills full desktop with full source
        var stretchPlan = _engine.ComputePlan(monitors, 1920, 1080, "16x9.jpg", new PlacementSettings
        {
            Mode = WallpaperPlacementMode.Stretch,
            CoordinateSpace = GeometryCoordinateSpace.VirtualPixels
        });
        Assert.Equal(0, stretchPlan.Slices[0].SourcePixelRect.X, precision: 3);
        Assert.Equal(960, stretchPlan.Slices[0].SourcePixelRect.Width, precision: 3);
        Assert.Equal(1920, stretchPlan.Slices[0].DestinationPixelRect.Width, precision: 3);

        // Center mode keeps native 1:1 source pixels
        var centerPlan = _engine.ComputePlan(monitors, 1000, 500, "small.jpg", new PlacementSettings
        {
            Mode = WallpaperPlacementMode.Center,
            CoordinateSpace = GeometryCoordinateSpace.VirtualPixels
        });
        Assert.Equal(1000, centerPlan.ImageCanvasRect.Width, precision: 3);
        Assert.Equal(500, centerPlan.ImageCanvasRect.Height, precision: 3);

        // Low-res warning triggers when upscaling significantly
        var lowResPlan = _engine.ComputePlan(monitors, 1280, 360, "tiny.jpg", new PlacementSettings
        {
            Mode = WallpaperPlacementMode.PanoramicSpan,
            CoordinateSpace = GeometryCoordinateSpace.VirtualPixels
        });
        Assert.True(lowResPlan.HasInsufficientResolution);
        Assert.NotNull(lowResPlan.ResolutionAdvisory);
    }

    [Fact]
    public void IndividualMode_AppliesDifferentWallpapersPerMonitor()
    {
        var monitors = new[]
        {
            MakeMonitor("M1", 0, 0, 1920, 1080, isPrimary: true),
            MakeMonitor("M2", 1920, 0, 2560, 1440)
        };

        var settings = new PlacementSettings
        {
            Mode = WallpaperPlacementMode.Individual,
            PerMonitorAssignments =
            {
                ["M1"] = new PerMonitorAssignment { MonitorId = "M1", ImagePath = "left.jpg", SubMode = WallpaperPlacementMode.Fill },
                ["M2"] = new PerMonitorAssignment { MonitorId = "M2", ImagePath = "right.jpg", SubMode = WallpaperPlacementMode.Fit }
            }
        };

        var dims = new Dictionary<string, (int, int)>
        {
            ["left.jpg"] = (3840, 2160),
            ["right.jpg"] = (2560, 1440)
        };

        var plan = _engine.ComputePlan(monitors, 3840, 2160, "left.jpg", settings, dims);

        Assert.Equal("left.jpg", plan.Slices[0].SourceImagePath);
        Assert.Equal("right.jpg", plan.Slices[1].SourceImagePath);
        Assert.Equal(1920, plan.Slices[0].OutputWidth);
        Assert.Equal(2560, plan.Slices[1].OutputWidth);
    }

    [Fact]
    public void PhysicalDimensions_CenterAndTopAlignments_PositionSmallerMonitorAccurately()
    {
        var monitors = new[]
        {
            MakeMonitor("SMALL_24", -1920, 0, 1920, 1080, physW: 527, physH: 296, bezelL: 0, bezelR: 0),
            MakeMonitor("BIG_27", 0, 0, 1920, 1080, isPrimary: true, physW: 597, physH: 336, bezelL: 0, bezelR: 0)
        };

        var centerPlan = _engine.ComputePlan(monitors, 7680, 2160, "t.jpg", new PlacementSettings
        {
            CoordinateSpace = GeometryCoordinateSpace.PhysicalDimensions,
            PhysicalAlignment = PhysicalVerticalAlignment.AlignCenter,
            EnableBezelCompensation = false
        });
        // Difference = 336 - 296 = 40mm -> centered at Y = 20mm
        Assert.Equal(20.0, centerPlan.Slices[0].CanvasRect.Y, precision: 3);

        var topPlan = _engine.ComputePlan(monitors, 7680, 2160, "t.jpg", new PlacementSettings
        {
            CoordinateSpace = GeometryCoordinateSpace.PhysicalDimensions,
            PhysicalAlignment = PhysicalVerticalAlignment.AlignTop,
            EnableBezelCompensation = false
        });
        Assert.Equal(0.0, topPlan.Slices[0].CanvasRect.Y, precision: 3);
    }

    [Fact]
    public void FocalPointAndCropFineTune_ShiftAndTrimSourceRegionAccurately()
    {
        var monitors = new[]
        {
            MakeMonitor("M1", 0, 0, 1920, 1080, isPrimary: true),
            MakeMonitor("M2", 1920, 0, 1920, 1080)
        };

        // Tall 4000x3000 image on 32:9 (3840x1080): vertical crop depends on FocalY
        var topFocus = _engine.ComputePlan(monitors, 4000, 3000, "tall.jpg", new PlacementSettings
        {
            Mode = WallpaperPlacementMode.PanoramicSpan,
            CoordinateSpace = GeometryCoordinateSpace.VirtualPixels,
            FocalY = 0.0
        });
        Assert.Equal(0.0, topFocus.Slices[0].SourcePixelRect.Y, precision: 3);

        var bottomFocus = _engine.ComputePlan(monitors, 4000, 3000, "tall.jpg", new PlacementSettings
        {
            Mode = WallpaperPlacementMode.PanoramicSpan,
            CoordinateSpace = GeometryCoordinateSpace.VirtualPixels,
            FocalY = 1.0
        });
        Assert.Equal(3000.0, bottomFocus.Slices[0].SourcePixelRect.Bottom, precision: 3);

        // Trim 10% from left & right edges of source before scaling
        var cropped = _engine.ComputePlan(monitors, 7680, 2160, "wide.jpg", new PlacementSettings
        {
            Mode = WallpaperPlacementMode.Stretch,
            CoordinateSpace = GeometryCoordinateSpace.VirtualPixels,
            CropLeft = 0.10,
            CropRight = 0.10
        });
        Assert.Equal(768.0, cropped.Slices[0].SourcePixelRect.X, precision: 3);
        Assert.Equal(6912.0, cropped.Slices[1].SourcePixelRect.Right, precision: 3);
    }
}
