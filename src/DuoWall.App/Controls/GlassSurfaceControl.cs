using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using DuoWall.Core.Theming;

namespace DuoWall.App.Controls;

/// <summary>
/// Centralized, anti-aliased surface compositor supporting multi-layer frosted Glassmorphism
/// as well as Rainmeter-inspired non-glass surface engines (TransparentMinimal, MatteFlatCard,
/// RetroTerminalHUD, WarmPaperEditorial).
/// </summary>
public sealed class GlassSurfaceControl : ContentControl
{
    public static readonly DependencyProperty GlassStyleProperty = DependencyProperty.Register(
        nameof(GlassStyle),
        typeof(GlassmorphismStyle),
        typeof(GlassSurfaceControl),
        new PropertyMetadata(null, OnVisualPropertyChanged));

    public static readonly DependencyProperty BrightnessMultiplierProperty = DependencyProperty.Register(
        nameof(BrightnessMultiplier),
        typeof(double),
        typeof(GlassSurfaceControl),
        new PropertyMetadata(1.0, OnVisualPropertyChanged));

    public static readonly DependencyProperty IsHardwareBackdropActiveProperty = DependencyProperty.Register(
        nameof(IsHardwareBackdropActive),
        typeof(bool),
        typeof(GlassSurfaceControl),
        new PropertyMetadata(false, OnVisualPropertyChanged));

    public static readonly DependencyProperty FallbackBackdropSourceProperty = DependencyProperty.Register(
        nameof(FallbackBackdropSource),
        typeof(ImageSource),
        typeof(GlassSurfaceControl),
        new PropertyMetadata(null, OnVisualPropertyChanged));

    private readonly Grid _rootGrid = new();
    private readonly Border _outerGlowLayer = new() { IsHitTestVisible = false };
    private readonly Grid _clippedBodyGrid = new() { ClipToBounds = true };
    private readonly Border _backdropImageLayer = new() { IsHitTestVisible = false };
    private readonly Border _glassTintLayer = new() { IsHitTestVisible = false };
    private readonly Border _innerShadowLayer = new() { IsHitTestVisible = false };
    private readonly Border _highlightSheenLayer = new() { IsHitTestVisible = false };
    private readonly Border _hudTopBarLayer = new() { IsHitTestVisible = false, VerticalAlignment = VerticalAlignment.Top, Height = 2 };
    private readonly Border _borderStrokeLayer = new() { IsHitTestVisible = false };
    private readonly Border _outerGlassBevelRim = new() { IsHitTestVisible = false };
    private readonly ContentPresenter _presenter = new();

    public GlassmorphismStyle? GlassStyle
    {
        get => (GlassmorphismStyle?)GetValue(GlassStyleProperty);
        set => SetValue(GlassStyleProperty, value);
    }

    public double BrightnessMultiplier
    {
        get => (double)GetValue(BrightnessMultiplierProperty);
        set => SetValue(BrightnessMultiplierProperty, value);
    }

    public bool IsHardwareBackdropActive
    {
        get => (bool)GetValue(IsHardwareBackdropActiveProperty);
        set => SetValue(IsHardwareBackdropActiveProperty, value);
    }

    public ImageSource? FallbackBackdropSource
    {
        get => (ImageSource?)GetValue(FallbackBackdropSourceProperty);
        set => SetValue(FallbackBackdropSourceProperty, value);
    }

    public GlassSurfaceControl()
    {
        _clippedBodyGrid.Children.Add(_backdropImageLayer);
        _clippedBodyGrid.Children.Add(_glassTintLayer);
        _clippedBodyGrid.Children.Add(_innerShadowLayer);
        _clippedBodyGrid.Children.Add(_highlightSheenLayer);
        _clippedBodyGrid.Children.Add(_hudTopBarLayer);
        _clippedBodyGrid.Children.Add(_presenter);
        _clippedBodyGrid.Children.Add(_outerGlassBevelRim);
        _clippedBodyGrid.Children.Add(_borderStrokeLayer);

        _clippedBodyGrid.SizeChanged += (_, _) => UpdateRoundedGeometryClip();

        _rootGrid.Children.Add(_outerGlowLayer);
        _rootGrid.Children.Add(_clippedBodyGrid);

        ContentTemplate = null;
        Template = BuildTemplate();
        ApplyVisualStyle();
    }

    private ControlTemplate BuildTemplate()
    {
        var template = new ControlTemplate(typeof(GlassSurfaceControl));
        var hostFactory = new FrameworkElementFactory(typeof(Border));
        hostFactory.Name = "PART_SurfaceRoot";
        template.VisualTree = hostFactory;
        return template;
    }

    public override void OnApplyTemplate()
    {
        base.OnApplyTemplate();
        if (GetTemplateChild("PART_SurfaceRoot") is Border surfaceRoot)
        {
            if (_rootGrid.Parent is Border prev)
                prev.Child = null;

            surfaceRoot.Child = _rootGrid;
            ApplyVisualStyle();
        }
    }

    protected override void OnContentChanged(object oldContent, object newContent)
    {
        base.OnContentChanged(oldContent, newContent);
        _presenter.Content = newContent;
    }

    private static void OnVisualPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is GlassSurfaceControl ctrl)
            ctrl.ApplyVisualStyle();
    }

    private void UpdateRoundedGeometryClip()
    {
        var style = GlassStyle ?? GlassmorphismStyle.BuiltInPresets[0];
        double corner = GetEffectiveCornerRadius(style);
        double w = _clippedBodyGrid.ActualWidth;
        double h = _clippedBodyGrid.ActualHeight;
        if (w <= 0 || h <= 0) return;

        if (style.SurfaceTheme == WidgetSurfaceTheme.TransparentMinimal)
        {
            _clippedBodyGrid.Clip = null;
            return;
        }

        _clippedBodyGrid.Clip = new RectangleGeometry(new Rect(0, 0, w, h), corner, corner);
    }

    private static double GetEffectiveCornerRadius(GlassmorphismStyle style) => style.SurfaceTheme switch
    {
        WidgetSurfaceTheme.TransparentMinimal => 0,
        WidgetSurfaceTheme.RetroTerminalHUD => Math.Clamp(style.CornerRadius, 0, 4),
        WidgetSurfaceTheme.MatteFlatCard => Math.Clamp(style.CornerRadius, 2, 10),
        _ => Math.Clamp(style.CornerRadius, 0, 40)
    };

    public void ApplyVisualStyle()
    {
        var style = GlassStyle ?? GlassmorphismStyle.BuiltInPresets[0];
        double brightness = Math.Clamp(BrightnessMultiplier, 0.2, 1.25);

        // 1. Rainmeter TransparentMinimal (Mond / Sonder): Pure floating typography, ZERO glass box/card
        if (style.SurfaceTheme == WidgetSurfaceTheme.TransparentMinimal)
        {
            _outerGlowLayer.Visibility = Visibility.Collapsed;
            _outerGlowLayer.Effect = null;
            _backdropImageLayer.Visibility = Visibility.Collapsed;
            _glassTintLayer.Visibility = Visibility.Collapsed;
            _innerShadowLayer.Visibility = Visibility.Collapsed;
            _highlightSheenLayer.Visibility = Visibility.Collapsed;
            _hudTopBarLayer.Visibility = Visibility.Collapsed;
            _borderStrokeLayer.Visibility = Visibility.Collapsed;
            _outerGlassBevelRim.Visibility = Visibility.Collapsed;
            _clippedBodyGrid.Clip = null;

            double shadowAlpha = Math.Clamp(style.OuterShadowIntensity, 0.15, 0.95);
            _presenter.Effect = new DropShadowEffect
            {
                Color = Colors.Black,
                Opacity = shadowAlpha,
                BlurRadius = 10,
                ShadowDepth = 2,
                Direction = 315,
                RenderingBias = RenderingBias.Performance
            };
            return;
        }

        _presenter.Effect = null;
        _outerGlowLayer.Visibility = Visibility.Visible;
        _glassTintLayer.Visibility = Visibility.Visible;
        _borderStrokeLayer.Visibility = Visibility.Visible;

        double corner = GetEffectiveCornerRadius(style);
        var cr = new CornerRadius(corner);
        _outerGlowLayer.CornerRadius = cr;
        _backdropImageLayer.CornerRadius = cr;
        _glassTintLayer.CornerRadius = cr;
        _innerShadowLayer.CornerRadius = cr;
        _borderStrokeLayer.CornerRadius = cr;
        _outerGlassBevelRim.CornerRadius = new CornerRadius(Math.Max(0, corner - 1));
        UpdateRoundedGeometryClip();

        Color primaryTint = ParseHex(style.BackgroundTintHex, Color.FromRgb(255, 255, 255), brightness);
        Color secondaryTint = ParseHex(style.GradientSecondaryHex, Color.FromRgb(96, 165, 250), brightness);
        Color borderColor = ParseHex(style.BorderColorHex, Color.FromRgb(255, 255, 255), brightness);
        Color accentColor = ParseHex(style.AccentHex, Color.FromRgb(96, 165, 250), brightness);

        // 2. Non-Glass Rainmeter Card Themes (Illustro Matte, CyberHUD Retro, Editorial Paper)
        if (style.SurfaceTheme != WidgetSurfaceTheme.Glassmorphism)
        {
            _backdropImageLayer.Visibility = Visibility.Collapsed;
            _innerShadowLayer.Visibility = Visibility.Collapsed;
            _highlightSheenLayer.Visibility = Visibility.Collapsed;
            _outerGlassBevelRim.Visibility = Visibility.Collapsed;

            byte cardAlpha = (byte)Math.Clamp(Math.Round((1.0 - style.BackgroundTransparency) * 255), 140, 252);
            _glassTintLayer.Background = new LinearGradientBrush(
                Color.FromArgb(cardAlpha, primaryTint.R, primaryTint.G, primaryTint.B),
                Color.FromArgb(cardAlpha,
                    (byte)Math.Clamp((primaryTint.R * 9 + secondaryTint.R) / 10, 0, 255),
                    (byte)Math.Clamp((primaryTint.G * 9 + secondaryTint.G) / 10, 0, 255),
                    (byte)Math.Clamp((primaryTint.B * 9 + secondaryTint.B) / 10, 0, 255)),
                new Point(0, 0), new Point(0, 1));

            _hudTopBarLayer.Visibility = style.SurfaceTheme is WidgetSurfaceTheme.MatteFlatCard or WidgetSurfaceTheme.RetroTerminalHUD
                ? Visibility.Visible
                : Visibility.Collapsed;
            _hudTopBarLayer.Background = new SolidColorBrush(accentColor);

            byte bA = (byte)Math.Clamp(Math.Round((1.0 - style.BorderTransparency) * 255), 40, 255);
            _borderStrokeLayer.BorderThickness = new Thickness(Math.Clamp(style.BorderThickness, 0, 4));
            _borderStrokeLayer.BorderBrush = new SolidColorBrush(Color.FromArgb(bA, borderColor.R, borderColor.G, borderColor.B));

            ApplyOuterShadowOrGlow(primaryTint, accentColor, cardAlpha, style);
            return;
        }

        // 3. Multi-Layer Optical Glassmorphism Theme
        _hudTopBarLayer.Visibility = Visibility.Collapsed;
        _innerShadowLayer.Visibility = Visibility.Visible;
        _highlightSheenLayer.Visibility = Visibility.Visible;
        _outerGlassBevelRim.Visibility = Visibility.Visible;

        bool hasBlurredSlice = FallbackBackdropSource != null;
        if (hasBlurredSlice && style.EnableBackdropBlur)
        {
            _backdropImageLayer.Visibility = Visibility.Visible;
            _backdropImageLayer.Background = new ImageBrush(FallbackBackdropSource)
            {
                Stretch = Stretch.Fill,
                Opacity = 1.0
            };
        }
        else
        {
            _backdropImageLayer.Visibility = Visibility.Collapsed;
            _backdropImageLayer.Background = Brushes.Transparent;
        }

        // Blend surface tint translucently over blurred slice so blur intensity remains visibly crisp and optical!
        double transp = Math.Clamp(style.BackgroundTransparency, 0.0, 0.98);
        double surfaceOpacity = Math.Clamp(style.BackgroundOpacity, 0.02, 0.95);
        double effectiveTintAlpha = Math.Clamp((1.0 - transp * 0.65) * surfaceOpacity * (hasBlurredSlice ? 0.78 : 0.95), 0.04, 0.90);
        byte fillAlpha = (byte)Math.Round(effectiveTintAlpha * 255);
        byte secAlpha = (byte)Math.Round(Math.Clamp(effectiveTintAlpha * 0.82, 0.04, 0.88) * 255);

        _glassTintLayer.Background = style.AppearanceVariant switch
        {
            GlassAppearanceVariant.Aurora => new LinearGradientBrush(
                new GradientStopCollection
                {
                    new(Color.FromArgb(fillAlpha, primaryTint.R, primaryTint.G, primaryTint.B), 0.0),
                    new(Color.FromArgb(secAlpha, secondaryTint.R, secondaryTint.G, secondaryTint.B), 0.52),
                    new(Color.FromArgb(fillAlpha, accentColor.R, accentColor.G, accentColor.B), 1.0)
                },
                new Point(0, 0), new Point(1, 1)),
            GlassAppearanceVariant.Midnight => new LinearGradientBrush(
                Color.FromArgb(fillAlpha, primaryTint.R, primaryTint.G, primaryTint.B),
                Color.FromArgb(fillAlpha,
                    (byte)Math.Clamp(primaryTint.R + 12, 0, 255),
                    (byte)Math.Clamp(primaryTint.G + 20, 0, 255),
                    (byte)Math.Clamp(primaryTint.B + 46, 0, 255)),
                new Point(0, 0), new Point(1, 1)),
            GlassAppearanceVariant.Neon => new LinearGradientBrush(
                Color.FromArgb(fillAlpha, primaryTint.R, primaryTint.G, primaryTint.B),
                Color.FromArgb((byte)Math.Clamp(fillAlpha + 14, 0, 255),
                    (byte)Math.Clamp((primaryTint.R + secondaryTint.R) / 4, 0, 255),
                    (byte)Math.Clamp((primaryTint.G + secondaryTint.G) / 4, 0, 255),
                    (byte)Math.Clamp((primaryTint.B + secondaryTint.B) / 4, 0, 255)),
                new Point(0, 0), new Point(0, 1)),
            _ => new LinearGradientBrush(
                Color.FromArgb((byte)Math.Clamp(fillAlpha + 14, 4, 255), primaryTint.R, primaryTint.G, primaryTint.B),
                Color.FromArgb((byte)Math.Clamp(fillAlpha - 12, 4, 255), secondaryTint.R, secondaryTint.G, secondaryTint.B),
                new Point(0, 0), new Point(1, 1))
        };

        // Diagonal optical glass border stroke + luminous inner rim (visionOS / acrylic specular catchlight)
        double borderAlphaFactor = 1.0 - Math.Clamp(style.BorderTransparency, 0.0, 1.0);
        byte topRimA = (byte)Math.Clamp(Math.Round(borderAlphaFactor * 255), 0, 255);
        byte botRimA = (byte)Math.Clamp(Math.Round(borderAlphaFactor * 115), 0, 255);

        _borderStrokeLayer.BorderThickness = new Thickness(Math.Clamp(style.BorderThickness, 0.0, 5.0));
        _borderStrokeLayer.BorderBrush = new LinearGradientBrush(
            new GradientStopCollection
            {
                new(Color.FromArgb(topRimA, borderColor.R, borderColor.G, borderColor.B), 0.0),
                new(Color.FromArgb((byte)((topRimA + botRimA) / 2), borderColor.R, borderColor.G, borderColor.B), 0.45),
                new(Color.FromArgb(botRimA, borderColor.R, borderColor.G, borderColor.B), 1.0)
            },
            new Point(0, 0), new Point(1, 1));

        // 1px soft white top bevel inset
        byte bevelAlpha = (byte)Math.Clamp(Math.Round(Math.Clamp(style.HighlightIntensity, 0, 1) * 110 * brightness), 0, 120);
        _outerGlassBevelRim.Margin = new Thickness(1);
        _outerGlassBevelRim.BorderThickness = new Thickness(0.8);
        _outerGlassBevelRim.BorderBrush = new LinearGradientBrush(
            Color.FromArgb(bevelAlpha, 255, 255, 255),
            Color.FromArgb(0, 255, 255, 255),
            new Point(0, 0), new Point(0.6, 0.85));

        // Top curved liquid-glass sheen
        double hlAlphaVal = Math.Clamp(style.HighlightIntensity, 0.0, 1.0) * 0.34 * brightness;
        byte hlAlpha = (byte)Math.Round(hlAlphaVal * 255);
        _highlightSheenLayer.CornerRadius = new CornerRadius(Math.Max(0, corner - 1), Math.Max(0, corner - 1), 2, 2);
        _highlightSheenLayer.Margin = new Thickness(1, 1, 1, 0);
        _highlightSheenLayer.Background = new LinearGradientBrush(
            new GradientStopCollection
            {
                new(Color.FromArgb(hlAlpha, 255, 255, 255), 0.0),
                new(Color.FromArgb((byte)(hlAlpha / 4), 255, 255, 255), 0.28),
                new(Color.FromArgb(0, 255, 255, 255), 0.56)
            },
            new Point(0, 0), new Point(0.25, 1));

        // Subtle inner edge depth shadow
        double innerAlpha = Math.Clamp(style.InnerShadowIntensity, 0.0, 1.0) * 0.32;
        byte inA = (byte)Math.Round(innerAlpha * 255);
        _innerShadowLayer.BorderThickness = new Thickness(style.InnerShadowIntensity > 0.03 ? 2.0 : 0);
        _innerShadowLayer.BorderBrush = new LinearGradientBrush(
            Color.FromArgb((byte)(inA / 3), 0, 0, 0),
            Color.FromArgb(inA, 0, 0, 0),
            new Point(0, 0), new Point(0, 1));

        ApplyOuterShadowOrGlow(primaryTint, accentColor, Math.Max((byte)18, fillAlpha), style);
    }

    private void ApplyOuterShadowOrGlow(Color primaryTint, Color accentColor, byte backAlpha, GlassmorphismStyle style)
    {
        double outer = Math.Clamp(style.OuterShadowIntensity, 0.0, 1.0);
        double glow = Math.Clamp(style.GlowIntensity, 0.0, 1.0);

        if (outer > 0.03 || glow > 0.03)
        {
            bool glowDominant = glow > outer * 0.72;
            Color shadowCol = glowDominant ? accentColor : Color.FromRgb(0, 0, 0);
            double strength = Math.Clamp(Math.Max(outer * 0.52, glow * 0.78), 0.05, 0.85);
            double radius = 8.0 + Math.Max(outer, glow) * 14.0;

            _outerGlowLayer.Background = new SolidColorBrush(Color.FromArgb(Math.Clamp((byte)(backAlpha / 2), (byte)10, (byte)110), primaryTint.R, primaryTint.G, primaryTint.B));
            _outerGlowLayer.Effect = new DropShadowEffect
            {
                Color = shadowCol,
                Opacity = strength,
                BlurRadius = radius,
                ShadowDepth = glowDominant ? 0 : 4,
                Direction = 270,
                RenderingBias = RenderingBias.Performance
            };
        }
        else
        {
            _outerGlowLayer.Background = Brushes.Transparent;
            _outerGlowLayer.Effect = null;
        }
    }

    public static Color ParseHex(string? hex, Color fallback, double brightness = 1.0)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(hex) && ColorConverter.ConvertFromString(hex) is Color parsed)
            {
                if (Math.Abs(brightness - 1.0) < 0.01)
                    return parsed;

                return Color.FromArgb(
                    parsed.A,
                    (byte)Math.Clamp(Math.Round(parsed.R * brightness), 0, 255),
                    (byte)Math.Clamp(Math.Round(parsed.G * brightness), 0, 255),
                    (byte)Math.Clamp(Math.Round(parsed.B * brightness), 0, 255));
            }
        }
        catch { }
        return fallback;
    }
}
