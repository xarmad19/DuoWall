using System;
using System.Linq;
using System.Reflection;

namespace DuoWall.Core.Services;

/// <summary>
/// Centralized application version resolution that dynamically extracts the official
/// build version from the assembly manifest, ensuring About Me, Check for Updates,
/// and Settings always reflect the true runtime version.
/// </summary>
public static class AppVersionInfo
{
    private static string? _cachedVersion;

    /// <summary>
    /// Gets the Semantic Version (e.g., "2.0.1") extracted dynamically from the entry assembly
    /// or DuoWall assembly attributes.
    /// </summary>
    public static string Version
    {
        get
        {
            if (_cachedVersion == null)
            {
                Assembly? asm = Assembly.GetEntryAssembly();
                if (asm == null || !asm.GetName().Name?.StartsWith("DuoWall", StringComparison.OrdinalIgnoreCase) == true)
                {
                    asm = AppDomain.CurrentDomain.GetAssemblies()
                        .FirstOrDefault(a => string.Equals(a.GetName().Name, "DuoWall", StringComparison.OrdinalIgnoreCase) ||
                                             string.Equals(a.GetName().Name, "DuoWall.App", StringComparison.OrdinalIgnoreCase))
                          ?? typeof(AppVersionInfo).Assembly;
                }

                // 1. Try AssemblyInformationalVersionAttribute (e.g. "2.0.1" or "2.0.1+commit")
                var infoAttr = asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>();
                if (!string.IsNullOrWhiteSpace(infoAttr?.InformationalVersion))
                {
                    string infoVer = infoAttr.InformationalVersion.Trim();
                    int plusIdx = infoVer.IndexOf('+');
                    _cachedVersion = (plusIdx > 0 ? infoVer[..plusIdx] : infoVer).Trim();
                }

                // 2. Fallback to Assembly.GetName().Version
                if (string.IsNullOrWhiteSpace(_cachedVersion))
                {
                    var ver = asm.GetName().Version;
                    if (ver != null && ver.Major > 0)
                    {
                        _cachedVersion = $"{ver.Major}.{ver.Minor}.{ver.Build}";
                    }
                    else
                    {
                        _cachedVersion = "2.0.1";
                    }
                }
            }
            return _cachedVersion;
        }
    }

    /// <summary>
    /// Gets the formatted display version string (e.g., "v2.0.1").
    /// </summary>
    public static string DisplayVersion => $"v{Version}";

    /// <summary>
    /// Gets the full product title with version (e.g., "DuoWall by SRMD · Version 2.0.1").
    /// </summary>
    public static string FullProductTitle => $"DuoWall by SRMD · Version {Version}";
}
