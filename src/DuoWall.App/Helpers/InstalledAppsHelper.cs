using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media;
using Microsoft.Win32;

namespace DuoWall.App.Helpers;

public sealed class InstalledAppEntry : INotifyPropertyChanged
{
    private ImageSource? _icon;
    public string DisplayName { get; init; } = string.Empty;
    public string LaunchTarget { get; init; } = string.Empty;
    public string SourceKind { get; init; } = "Desktop App";

    public ImageSource? Icon
    {
        get => _icon;
        set
        {
            if (ReferenceEquals(_icon, value)) return;
            _icon = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Icon)));
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}

/// <summary>
/// Discovers installed Windows desktop applications, Start Menu shortcuts, and Store/UWP apps
/// cleanly without requiring the user to hunt through Explorer folders.
/// </summary>
public static class InstalledAppsHelper
{
    private static readonly string[] IgnoredPrefixes =
    {
        "uninstall ", "remove ", "repair ", "readme", "release notes", "documentation", "manual", "help "
    };

    public static Task<List<InstalledAppEntry>> DiscoverInstalledAppsAsync(CancellationToken ct = default)
    {
        var tcs = new TaskCompletionSource<List<InstalledAppEntry>>(TaskCreationOptions.RunContinuationsAsynchronously);

        // Shell.Application COM object requires an STA apartment
        var thread = new Thread(() =>
        {
            try
            {
                var result = DiscoverCore(ct);
                tcs.TrySetResult(result);
            }
            catch (Exception ex)
            {
                tcs.TrySetException(ex);
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.IsBackground = true;
        thread.Start();

        return tcs.Task;
    }

    private static List<InstalledAppEntry> DiscoverCore(CancellationToken ct)
    {
        var byName = new Dictionary<string, InstalledAppEntry>(StringComparer.OrdinalIgnoreCase);

        // 1. Windows shell:AppsFolder enumerates Store/UWP apps AND modern Start desktop apps cleanly
        try
        {
            Type? shellType = Type.GetTypeFromProgID("Shell.Application");
            if (shellType != null)
            {
                dynamic shell = Activator.CreateInstance(shellType)!;
                dynamic appsFolder = shell.NameSpace("shell:AppsFolder");
                if (appsFolder != null)
                {
                    foreach (dynamic item in appsFolder.Items())
                    {
                        if (ct.IsCancellationRequested) break;
                        try
                        {
                            string name = (string?)item.Name ?? string.Empty;
                            string path = (string?)item.Path ?? string.Empty;
                            if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(path))
                                continue;
                            if (IsIgnoredShortcutName(name))
                                continue;

                            bool isDirectFile = Path.IsPathRooted(path) && File.Exists(path);
                            string launchTarget = isDirectFile ? path : $"shell:AppsFolder\\{path}";
                            string kind = isDirectFile ? "Desktop App" : "Windows App";

                            byName.TryAdd(name.Trim(), new InstalledAppEntry
                            {
                                DisplayName = name.Trim(),
                                LaunchTarget = launchTarget,
                                SourceKind = kind
                            });
                        }
                        catch { }
                    }
                }
            }
        }
        catch { }

        // 2. Supplement with Start Menu .lnk shortcuts
        foreach (var root in new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu),
            Environment.GetFolderPath(Environment.SpecialFolder.StartMenu)
        })
        {
            if (ct.IsCancellationRequested) break;
            string programs = string.IsNullOrWhiteSpace(root) ? string.Empty : Path.Combine(root, "Programs");
            if (!Directory.Exists(programs)) continue;

            try
            {
                foreach (string lnk in Directory.EnumerateFiles(programs, "*.lnk", SearchOption.AllDirectories))
                {
                    if (ct.IsCancellationRequested) break;
                    string name = Path.GetFileNameWithoutExtension(lnk).Trim();
                    if (string.IsNullOrWhiteSpace(name) || IsIgnoredShortcutName(name))
                        continue;

                    byName.TryAdd(name, new InstalledAppEntry
                    {
                        DisplayName = name,
                        LaunchTarget = lnk,
                        SourceKind = "Start Menu App"
                    });
                }
            }
            catch { }
        }

        // 3. Supplement with Registry App Paths
        foreach (var hive in new[] { Registry.LocalMachine, Registry.CurrentUser })
        {
            if (ct.IsCancellationRequested) break;
            try
            {
                using var key = hive.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths");
                if (key == null) continue;
                foreach (string sub in key.GetSubKeyNames())
                {
                    using var appKey = key.OpenSubKey(sub);
                    string? rawPath = appKey?.GetValue(null) as string;
                    if (string.IsNullOrWhiteSpace(rawPath)) continue;
                    string exePath = Environment.ExpandEnvironmentVariables(rawPath.Trim().Trim('"'));
                    if (!File.Exists(exePath)) continue;

                    string display = Path.GetFileNameWithoutExtension(sub);
                    if (string.IsNullOrWhiteSpace(display) || IsIgnoredShortcutName(display)) continue;
                    string pretty = char.ToUpperInvariant(display[0]) + display[1..];

                    byName.TryAdd(pretty, new InstalledAppEntry
                    {
                        DisplayName = pretty,
                        LaunchTarget = exePath,
                        SourceKind = "Registered App"
                    });
                }
            }
            catch { }
        }

        return byName.Values
            .OrderBy(a => a.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static bool IsIgnoredShortcutName(string name)
    {
        string lower = name.Trim().ToLowerInvariant();
        foreach (string prefix in IgnoredPrefixes)
        {
            if (lower.StartsWith(prefix, StringComparison.Ordinal))
                return true;
        }
        return lower.Contains("uninstall", StringComparison.Ordinal);
    }
}
