using System.Collections.Concurrent;
using System.IO;
using System.Windows.Media;

namespace DuoWall.App.Helpers;

/// <summary>
/// Manages and resolves the curated bundled font set extracted from App Fonts.zip.
/// Exposes the strictly permitted fonts for Typographic Clock customization and
/// dynamically resolves WPF FontFamily instances referencing local font assets.
/// </summary>
public static class BundledFontHelper
{
    /// <summary>
    /// The exact 12 font families bundled with DuoWall for the Typographic Clock widget.
    /// </summary>
    public static readonly IReadOnlyList<string> TypographicClockFonts = new[]
    {
        "SF Pro",
        "SF Pro Rounded",
        "SF Compact",
        "Lato",
        "Liberation Sans",
        "Nimbus Sans",
        "Noto Sans",
        "Noto Sans Display",
        "Noto Sans Mono",
        "Noto Serif",
        "Noto Serif Display",
        "URW Gothic"
    };

    private static readonly ConcurrentDictionary<string, FontFamily> FontCache = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Resolves a font name to a WPF FontFamily, referencing the bundled asset folder if it matches
    /// any curated font, or falling back to standard font resolution.
    /// </summary>
    public static FontFamily ResolveFontFamily(string? fontName)
    {
        if (string.IsNullOrWhiteSpace(fontName))
            return new FontFamily("Segoe UI Variable Display, Segoe UI");

        fontName = fontName.Trim();
        if (FontCache.TryGetValue(fontName, out var cached))
            return cached;

        var family = CreateFontFamily(fontName);
        FontCache[fontName] = family;
        return family;
    }

    private static FontFamily CreateFontFamily(string fontName)
    {
        if (fontName.Contains('#') || fontName.Contains('/') || fontName.Contains('\\'))
            return new FontFamily(fontName);

        var matched = TypographicClockFonts.FirstOrDefault(f => string.Equals(f, fontName, StringComparison.OrdinalIgnoreCase));
        if (matched != null)
        {
            // 1. Check local output directory
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            string localFontsDir = Path.Combine(baseDir, "Assets", "Fonts");
            if (!Directory.Exists(localFontsDir))
            {
                // Development fallback
                string devDir = Path.GetFullPath(Path.Combine(baseDir, "..", "..", "..", "Assets", "Fonts"));
                if (Directory.Exists(devDir))
                    localFontsDir = devDir;
            }

            if (Directory.Exists(localFontsDir))
            {
                try
                {
                    var dirUri = new Uri(localFontsDir.TrimEnd('\\', '/') + "/");
                    return new FontFamily(dirUri, "./#" + matched);
                }
                catch
                {
                    // Fall through to pack URI
                }
            }

            // 2. Try pack URI for embedded resources
            try
            {
                return new FontFamily(new Uri("pack://application:,,,/Assets/Fonts/"), "./#" + matched);
            }
            catch
            {
                // Fall through to system font
            }
        }

        return new FontFamily(fontName);
    }
}

