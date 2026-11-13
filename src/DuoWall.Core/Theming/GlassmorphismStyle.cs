namespace DuoWall.Core.Theming;

public enum GlassAppearanceVariant
{
    Frosted,
    Dark,
    Light,
    Crystal,
    Midnight,
    Aurora,
    Neon,
    Minimal
}

/// <summary>
/// Declarative visual surface engine allowing widgets to use translucent Glassmorphism
/// OR classic Rainmeter-inspired non-glass designs (transparent typography, flat matte panels,
/// retro neon HUD frames, or editorial paper cards).
/// </summary>
public enum WidgetSurfaceTheme
{
    Glassmorphism,
    TransparentMinimal,
    MatteFlatCard,
    RetroTerminalHUD,
    WarmPaperEditorial
}

/// <summary>
/// Centralized, reusable Glassmorphism styling contract applied consistently across
/// all DuoWall desktop widgets, preview surfaces, and exportable skins.
/// </summary>
public sealed class GlassmorphismStyle
{
    public string Id { get; set; } = "frosted-glass";
    public string Name { get; set; } = "Frosted Glass";
    public bool IsBuiltIn { get; set; }

    /// <summary>
    /// Controls overall glass background transparency (0.0 = opaque, 1.0 = fully transparent).
    /// </summary>
    public double BackgroundTransparency { get; set; } = 0.72;

    /// <summary>
    /// Tint color blended into the glass backdrop (#RRGGBB).
    /// </summary>
    public string BackgroundTintHex { get; set; } = "#FFFFFF";

    /// <summary>
    /// Secondary gradient tint used by Aurora and highlight sheen effects (#RRGGBB).
    /// </summary>
    public string GradientSecondaryHex { get; set; } = "#60A5FA";

    /// <summary>
    /// Direct surface opacity multiplier (0.0 to 1.0).
    /// </summary>
    public double BackgroundOpacity { get; set; } = 0.24;

    /// <summary>
    /// Blur radius intensity in DIPs (0 to 40) used by backdrop / fallback blur compositor.
    /// </summary>
    public double BackgroundBlurIntensity { get; set; } = 22.0;

    /// <summary>
    /// Enables hardware Windows DWM backdrop blur (with graceful Skia fallback when unsupported).
    /// </summary>
    public bool EnableBackdropBlur { get; set; } = true;

    /// <summary>
    /// Border transparency (0.0 = solid border, 1.0 = invisible border).
    /// </summary>
    public double BorderTransparency { get; set; } = 0.55;

    public double BorderThickness { get; set; } = 1.2;
    public string BorderColorHex { get; set; } = "#FFFFFF";
    public double CornerRadius { get; set; } = 18.0;

    public double InnerShadowIntensity { get; set; } = 0.25;
    public double OuterShadowIntensity { get; set; } = 0.45;
    public double GlowIntensity { get; set; } = 0.15;
    public double HighlightIntensity { get; set; } = 0.35;

    public GlassAppearanceVariant AppearanceVariant { get; set; } = GlassAppearanceVariant.Frosted;
    public WidgetSurfaceTheme SurfaceTheme { get; set; } = WidgetSurfaceTheme.Glassmorphism;

    public string TextPrimaryHex { get; set; } = "#F8FAFC";
    public string TextSecondaryHex { get; set; } = "#CBD5E1";
    public string AccentHex { get; set; } = "#60A5FA";

    public GlassmorphismStyle Clone(string? newId = null, string? newName = null, bool? isBuiltIn = null) => new()
    {
        Id = newId ?? Id,
        Name = newName ?? Name,
        IsBuiltIn = isBuiltIn ?? IsBuiltIn,
        BackgroundTransparency = BackgroundTransparency,
        BackgroundTintHex = BackgroundTintHex,
        GradientSecondaryHex = GradientSecondaryHex,
        BackgroundOpacity = BackgroundOpacity,
        BackgroundBlurIntensity = BackgroundBlurIntensity,
        EnableBackdropBlur = EnableBackdropBlur,
        BorderTransparency = BorderTransparency,
        BorderThickness = BorderThickness,
        BorderColorHex = BorderColorHex,
        CornerRadius = CornerRadius,
        InnerShadowIntensity = InnerShadowIntensity,
        OuterShadowIntensity = OuterShadowIntensity,
        GlowIntensity = GlowIntensity,
        HighlightIntensity = HighlightIntensity,
        AppearanceVariant = AppearanceVariant,
        SurfaceTheme = SurfaceTheme,
        TextPrimaryHex = TextPrimaryHex,
        TextSecondaryHex = TextSecondaryHex,
        AccentHex = AccentHex
    };

    public static IReadOnlyList<GlassmorphismStyle> BuiltInPresets { get; } = new[]
    {
        new GlassmorphismStyle
        {
            Id = "frosted-glass",
            Name = "Frosted Glass",
            IsBuiltIn = true,
            AppearanceVariant = GlassAppearanceVariant.Frosted,
            BackgroundTransparency = 0.72,
            BackgroundOpacity = 0.22,
            BackgroundTintHex = "#F1F5F9",
            GradientSecondaryHex = "#CBD5E1",
            BackgroundBlurIntensity = 26,
            EnableBackdropBlur = true,
            BorderTransparency = 0.50,
            BorderThickness = 1.2,
            BorderColorHex = "#FFFFFF",
            CornerRadius = 18,
            InnerShadowIntensity = 0.25,
            OuterShadowIntensity = 0.45,
            GlowIntensity = 0.12,
            HighlightIntensity = 0.45,
            TextPrimaryHex = "#FFFFFF",
            TextSecondaryHex = "#E2E8F0",
            AccentHex = "#60A5FA"
        },
        new GlassmorphismStyle
        {
            Id = "dark-glass",
            Name = "Dark Glass",
            IsBuiltIn = true,
            AppearanceVariant = GlassAppearanceVariant.Dark,
            BackgroundTransparency = 0.38,
            BackgroundOpacity = 0.62,
            BackgroundTintHex = "#0B0F19",
            GradientSecondaryHex = "#1E293B",
            BackgroundBlurIntensity = 24,
            EnableBackdropBlur = true,
            BorderTransparency = 0.68,
            BorderThickness = 1.0,
            BorderColorHex = "#94A3B8",
            CornerRadius = 16,
            InnerShadowIntensity = 0.35,
            OuterShadowIntensity = 0.65,
            GlowIntensity = 0.08,
            HighlightIntensity = 0.20,
            TextPrimaryHex = "#F8FAFC",
            TextSecondaryHex = "#94A3B8",
            AccentHex = "#38BDF8"
        },
        new GlassmorphismStyle
        {
            Id = "light-glass",
            Name = "Light Glass",
            IsBuiltIn = true,
            AppearanceVariant = GlassAppearanceVariant.Light,
            BackgroundTransparency = 0.32,
            BackgroundOpacity = 0.68,
            BackgroundTintHex = "#F8FAFC",
            GradientSecondaryHex = "#E2E8F0",
            BackgroundBlurIntensity = 20,
            EnableBackdropBlur = true,
            BorderTransparency = 0.35,
            BorderThickness = 1.1,
            BorderColorHex = "#FFFFFF",
            CornerRadius = 16,
            InnerShadowIntensity = 0.15,
            OuterShadowIntensity = 0.28,
            GlowIntensity = 0.08,
            HighlightIntensity = 0.55,
            TextPrimaryHex = "#0F172A",
            TextSecondaryHex = "#334155",
            AccentHex = "#2563EB"
        },
        new GlassmorphismStyle
        {
            Id = "crystal",
            Name = "Crystal",
            IsBuiltIn = true,
            AppearanceVariant = GlassAppearanceVariant.Crystal,
            BackgroundTransparency = 0.88,
            BackgroundOpacity = 0.10,
            BackgroundTintHex = "#FFFFFF",
            GradientSecondaryHex = "#E0F2FE",
            BackgroundBlurIntensity = 18,
            EnableBackdropBlur = true,
            BorderTransparency = 0.38,
            BorderThickness = 1.0,
            BorderColorHex = "#E2E8F0",
            CornerRadius = 20,
            InnerShadowIntensity = 0.18,
            OuterShadowIntensity = 0.30,
            GlowIntensity = 0.14,
            HighlightIntensity = 0.60,
            TextPrimaryHex = "#FFFFFF",
            TextSecondaryHex = "#E2E8F0",
            AccentHex = "#7DD3FC"
        },
        new GlassmorphismStyle
        {
            Id = "midnight-glass",
            Name = "Midnight Glass",
            IsBuiltIn = true,
            AppearanceVariant = GlassAppearanceVariant.Midnight,
            BackgroundTransparency = 0.34,
            BackgroundOpacity = 0.66,
            BackgroundTintHex = "#091124",
            GradientSecondaryHex = "#1D4ED8",
            BackgroundBlurIntensity = 26,
            EnableBackdropBlur = true,
            BorderTransparency = 0.45,
            BorderThickness = 1.2,
            BorderColorHex = "#3B82F6",
            CornerRadius = 16,
            InnerShadowIntensity = 0.38,
            OuterShadowIntensity = 0.65,
            GlowIntensity = 0.32,
            HighlightIntensity = 0.30,
            TextPrimaryHex = "#EFF6FF",
            TextSecondaryHex = "#93C5FD",
            AccentHex = "#60A5FA"
        },
        new GlassmorphismStyle
        {
            Id = "aurora",
            Name = "Aurora",
            IsBuiltIn = true,
            AppearanceVariant = GlassAppearanceVariant.Aurora,
            BackgroundTransparency = 0.55,
            BackgroundOpacity = 0.42,
            BackgroundTintHex = "#4C1D95",
            GradientSecondaryHex = "#0EA5E9",
            BackgroundBlurIntensity = 28,
            EnableBackdropBlur = true,
            BorderTransparency = 0.42,
            BorderThickness = 1.3,
            BorderColorHex = "#A78BFA",
            CornerRadius = 20,
            InnerShadowIntensity = 0.25,
            OuterShadowIntensity = 0.50,
            GlowIntensity = 0.42,
            HighlightIntensity = 0.45,
            TextPrimaryHex = "#FFFFFF",
            TextSecondaryHex = "#DDD6FE",
            AccentHex = "#34D399"
        },
        new GlassmorphismStyle
        {
            Id = "neon-glass",
            Name = "Neon Glass",
            IsBuiltIn = true,
            AppearanceVariant = GlassAppearanceVariant.Neon,
            BackgroundTransparency = 0.28,
            BackgroundOpacity = 0.72,
            BackgroundTintHex = "#070B14",
            GradientSecondaryHex = "#06B6D4",
            BackgroundBlurIntensity = 20,
            EnableBackdropBlur = true,
            BorderTransparency = 0.12,
            BorderThickness = 1.6,
            BorderColorHex = "#22D3EE",
            CornerRadius = 14,
            InnerShadowIntensity = 0.42,
            OuterShadowIntensity = 0.72,
            GlowIntensity = 0.80,
            HighlightIntensity = 0.40,
            TextPrimaryHex = "#ECFEFF",
            TextSecondaryHex = "#67E8F9",
            AccentHex = "#22D3EE"
        },
        new GlassmorphismStyle
        {
            Id = "minimal-glass",
            Name = "Minimal Glass",
            IsBuiltIn = true,
            AppearanceVariant = GlassAppearanceVariant.Minimal,
            BackgroundTransparency = 0.92,
            BackgroundOpacity = 0.06,
            BackgroundTintHex = "#0F172A",
            GradientSecondaryHex = "#1E293B",
            BackgroundBlurIntensity = 10,
            EnableBackdropBlur = true,
            BorderTransparency = 0.78,
            BorderThickness = 0.8,
            BorderColorHex = "#FFFFFF",
            CornerRadius = 12,
            InnerShadowIntensity = 0.05,
            OuterShadowIntensity = 0.12,
            GlowIntensity = 0.02,
            HighlightIntensity = 0.12,
            TextPrimaryHex = "#F8FAFC",
            TextSecondaryHex = "#CBD5E1",
            AccentHex = "#94A3B8"
        }
    };

    public static IReadOnlyList<GlassmorphismStyle> RainmeterStylePresets { get; } = new[]
    {
        new GlassmorphismStyle
        {
            Id = "rainmeter-mond",
            Name = "Mond Transparent Serif",
            IsBuiltIn = true,
            SurfaceTheme = WidgetSurfaceTheme.TransparentMinimal,
            AppearanceVariant = GlassAppearanceVariant.Minimal,
            BackgroundTransparency = 1.0,
            BackgroundOpacity = 0.0,
            BackgroundTintHex = "#000000",
            BackgroundBlurIntensity = 0,
            EnableBackdropBlur = false,
            BorderTransparency = 1.0,
            BorderThickness = 0,
            CornerRadius = 0,
            OuterShadowIntensity = 0.75,
            HighlightIntensity = 0,
            InnerShadowIntensity = 0,
            GlowIntensity = 0,
            TextPrimaryHex = "#FFFFFF",
            TextSecondaryHex = "#E2E8F0",
            AccentHex = "#F8FAFC"
        },
        new GlassmorphismStyle
        {
            Id = "rainmeter-illustro",
            Name = "Illustro Flat Matte",
            IsBuiltIn = true,
            SurfaceTheme = WidgetSurfaceTheme.MatteFlatCard,
            AppearanceVariant = GlassAppearanceVariant.Dark,
            BackgroundTransparency = 0.14,
            BackgroundOpacity = 0.86,
            BackgroundTintHex = "#141820",
            GradientSecondaryHex = "#1F2937",
            BackgroundBlurIntensity = 0,
            EnableBackdropBlur = false,
            BorderTransparency = 0.45,
            BorderThickness = 1.0,
            BorderColorHex = "#475569",
            CornerRadius = 4,
            OuterShadowIntensity = 0.35,
            HighlightIntensity = 0.08,
            InnerShadowIntensity = 0,
            GlowIntensity = 0,
            TextPrimaryHex = "#F1F5F9",
            TextSecondaryHex = "#94A3B8",
            AccentHex = "#38BDF8"
        },
        new GlassmorphismStyle
        {
            Id = "rainmeter-cyberhud",
            Name = "CyberHUD Retro Frame",
            IsBuiltIn = true,
            SurfaceTheme = WidgetSurfaceTheme.RetroTerminalHUD,
            AppearanceVariant = GlassAppearanceVariant.Neon,
            BackgroundTransparency = 0.12,
            BackgroundOpacity = 0.88,
            BackgroundTintHex = "#050914",
            GradientSecondaryHex = "#0B132B",
            BackgroundBlurIntensity = 0,
            EnableBackdropBlur = false,
            BorderTransparency = 0.10,
            BorderThickness = 1.6,
            BorderColorHex = "#00F0FF",
            CornerRadius = 2,
            OuterShadowIntensity = 0.25,
            GlowIntensity = 0.65,
            HighlightIntensity = 0.0,
            InnerShadowIntensity = 0.2,
            TextPrimaryHex = "#E0FCFF",
            TextSecondaryHex = "#7DD3FC",
            AccentHex = "#00F0FF"
        },
        new GlassmorphismStyle
        {
            Id = "rainmeter-editorial-paper",
            Name = "Editorial Warm Paper",
            IsBuiltIn = true,
            SurfaceTheme = WidgetSurfaceTheme.WarmPaperEditorial,
            AppearanceVariant = GlassAppearanceVariant.Light,
            BackgroundTransparency = 0.05,
            BackgroundOpacity = 0.95,
            BackgroundTintHex = "#F7F4EE",
            GradientSecondaryHex = "#EDE7DD",
            BackgroundBlurIntensity = 0,
            EnableBackdropBlur = false,
            BorderTransparency = 0.25,
            BorderThickness = 1.0,
            BorderColorHex = "#D6CEC0",
            CornerRadius = 8,
            OuterShadowIntensity = 0.35,
            HighlightIntensity = 0.05,
            InnerShadowIntensity = 0,
            GlowIntensity = 0,
            TextPrimaryHex = "#1C1917",
            TextSecondaryHex = "#57534E",
            AccentHex = "#B45309"
        }
    };

    public static GlassmorphismStyle GetPreset(string nameOrId) =>
        BuiltInPresets.Concat(RainmeterStylePresets).FirstOrDefault(p =>
            string.Equals(p.Id, nameOrId, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(p.Name, nameOrId, StringComparison.OrdinalIgnoreCase))?.Clone()
        ?? BuiltInPresets[0].Clone();
}

