using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Management;
using System.Net.NetworkInformation;
using System.Security.Principal;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;

namespace DuoWall.App.Services;

public sealed class NetworkAdapterInfo
{
    public string Name { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public bool IsEnabled { get; set; }
    public bool IsConnected { get; set; }
    public int InterfaceIndex { get; set; }
    public bool IsActive => IsEnabled && IsConnected;
}

/// <summary>
/// Service to enumerate physical/dedicated network adapters in Windows, monitor connection changes,
/// and switch between active network connections by enabling the target adapter and disabling all others.
/// </summary>
public static class NetworkAdapterService
{
    private static readonly object Lock = new();
    private static List<NetworkAdapterInfo> _cachedAdapters = new();
    private static DateTime _lastQueryUtc = DateTime.MinValue;
    private static bool _isSwitching;
    private static string? _switchingToAdapterName;

    public static event Action? AdaptersChanged;

    static NetworkAdapterService()
    {
        try
        {
            NetworkChange.NetworkAddressChanged += (_, _) => _ = RefreshAdaptersAsync();
            NetworkChange.NetworkAvailabilityChanged += (_, _) => _ = RefreshAdaptersAsync();
        }
        catch { }
    }

    public static bool IsSwitching => _isSwitching;
    public static string? SwitchingToAdapterName => _switchingToAdapterName;

    public static IReadOnlyList<NetworkAdapterInfo> GetCachedAdapters()
    {
        lock (Lock)
        {
            if (_cachedAdapters.Count == 0 || (DateTime.UtcNow - _lastQueryUtc).TotalSeconds > 5)
            {
                _ = RefreshAdaptersAsync();
            }
            return _cachedAdapters.ToList();
        }
    }

    public static async Task<IReadOnlyList<NetworkAdapterInfo>> RefreshAdaptersAsync()
    {
        return await Task.Run(() =>
        {
            var list = QueryAdaptersInternal();
            lock (Lock)
            {
                _cachedAdapters = list;
                _lastQueryUtc = DateTime.UtcNow;
            }
            Application.Current?.Dispatcher.BeginInvoke(() => AdaptersChanged?.Invoke());
            return list;
        });
    }

    public static List<NetworkAdapterInfo> QueryAdaptersInternal()
    {
        var list = new List<NetworkAdapterInfo>();
        try
        {
            using var searcher = new ManagementObjectSearcher(
                "SELECT NetConnectionID, Name, Description, NetEnabled, NetConnectionStatus, InterfaceIndex " +
                "FROM Win32_NetworkAdapter " +
                "WHERE NetConnectionID IS NOT NULL");

            foreach (ManagementObject obj in searcher.Get())
            {
                string? connId = obj["NetConnectionID"]?.ToString()?.Trim();
                if (string.IsNullOrWhiteSpace(connId))
                    continue;

                string desc = obj["Description"]?.ToString() ?? string.Empty;
                string name = obj["Name"]?.ToString() ?? connId;

                // Filter out pseudo/virtual tunnels or debug interfaces
                if (name.Contains("WAN Miniport", StringComparison.OrdinalIgnoreCase) ||
                    name.Contains("Kernel Debugger", StringComparison.OrdinalIgnoreCase) ||
                    name.Contains("Loopback", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                bool isEnabled = obj["NetEnabled"] is true;
                int status = 0;
                if (obj["NetConnectionStatus"] != null)
                {
                    int.TryParse(obj["NetConnectionStatus"].ToString(), out status);
                }
                int ifIndex = 0;
                if (obj["InterfaceIndex"] != null)
                {
                    int.TryParse(obj["InterfaceIndex"].ToString(), out ifIndex);
                }

                list.Add(new NetworkAdapterInfo
                {
                    Name = connId,
                    Description = !string.IsNullOrWhiteSpace(desc) ? desc : name,
                    IsEnabled = isEnabled,
                    IsConnected = status == 2, // 2 = Connected in Win32_NetworkAdapter
                    InterfaceIndex = ifIndex
                });
            }
        }
        catch
        {
            list = QueryAdaptersNetshFallback();
        }

        if (list.Count == 0)
        {
            list = QueryAdaptersNetshFallback();
        }

        return list.OrderBy(a => a.Name).ToList();
    }

    private static List<NetworkAdapterInfo> QueryAdaptersNetshFallback()
    {
        var list = new List<NetworkAdapterInfo>();
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "netsh",
                Arguments = "interface show interface",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                CreateNoWindow = true
            };
            using var proc = Process.Start(psi);
            if (proc == null) return list;
            string output = proc.StandardOutput.ReadToEnd();
            proc.WaitForExit(1000);

            var lines = output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            bool pastHeader = false;
            foreach (var line in lines)
            {
                if (line.StartsWith("---"))
                {
                    pastHeader = true;
                    continue;
                }
                if (!pastHeader) continue;

                var parts = line.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length >= 4)
                {
                    string adminState = parts[0];
                    string state = parts[1];
                    int idx = line.IndexOf(parts[3], StringComparison.Ordinal);
                    string name = idx >= 0 ? line.Substring(idx).Trim() : parts[3];

                    bool isEnabled = adminState.Equals("Enabled", StringComparison.OrdinalIgnoreCase);
                    bool isConnected = state.Equals("Connected", StringComparison.OrdinalIgnoreCase);

                    list.Add(new NetworkAdapterInfo
                    {
                        Name = name,
                        Description = name,
                        IsEnabled = isEnabled,
                        IsConnected = isConnected
                    });
                }
            }
        }
        catch { }
        return list;
    }

    private const string NetSwitchTaskName = "DuoWall-NetSwitch";

    public static bool IsProcessAdmin =>
        new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator);

    public static bool HasNetSwitchTask()
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "schtasks.exe",
                Arguments = $"/query /tn \"{NetSwitchTaskName}\"",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };
            using var p = Process.Start(psi);
            if (p == null) return false;
            p.WaitForExit(1000);
            return p.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    public static bool IsSilentSwitchingConfigured() => IsProcessAdmin || HasNetSwitchTask();

    public static async Task<bool> RegisterSilentNetSwitchTaskAsync()
    {
        return await Task.Run(() =>
        {
            try
            {
                string appDataRoot = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "DuoWallBySRMD");
                Directory.CreateDirectory(appDataRoot);
                string scriptPath = Path.Combine(appDataRoot, "switch-net.bat");
                if (!File.Exists(scriptPath))
                {
                    File.WriteAllText(scriptPath, "@echo off\r\nexit /b 0\r\n");
                }

                var psi = new ProcessStartInfo
                {
                    FileName = "schtasks.exe",
                    Arguments = $"/create /tn \"{NetSwitchTaskName}\" /tr \"cmd.exe /c \\\"{scriptPath}\\\"\" /sc ONCE /st 00:00 /rl HIGHEST /f",
                    UseShellExecute = true,
                    Verb = "runas",
                    WindowStyle = ProcessWindowStyle.Hidden
                };
                using var p = Process.Start(psi);
                if (p == null) return false;
                p.WaitForExit(10000);
                return p.ExitCode == 0;
            }
            catch
            {
                return false;
            }
        });
    }

    /// <summary>
    /// Enables the specified network adapter and disables all other adapters.
    /// </summary>
    public static async Task SwitchToAdapterAsync(string targetAdapterName)
    {
        if (string.IsNullOrWhiteSpace(targetAdapterName)) return;

        var adapters = QueryAdaptersInternal();
        var target = adapters.FirstOrDefault(a => string.Equals(a.Name, targetAdapterName, StringComparison.OrdinalIgnoreCase));
        if (target == null && adapters.Count > 0)
            target = adapters[0];

        if (target == null) return;

        _isSwitching = true;
        _switchingToAdapterName = target.Name;
        Application.Current?.Dispatcher.BeginInvoke(() => AdaptersChanged?.Invoke());

        await Task.Run(() =>
        {
            try
            {
                bool isAdmin = IsProcessAdmin;
                bool hasTask = HasNetSwitchTask();

                var commands = new List<string>();

                // Enable the selected adapter first
                commands.Add($"netsh interface set interface name=\"{target.Name}\" admin=ENABLED");

                // Disable all other network adapters
                foreach (var other in adapters.Where(a => !string.Equals(a.Name, target.Name, StringComparison.OrdinalIgnoreCase)))
                {
                    commands.Add($"netsh interface set interface name=\"{other.Name}\" admin=DISABLED");
                }

                string combined = string.Join(" & ", commands);

                if (isAdmin)
                {
                    var psi = new ProcessStartInfo
                    {
                        FileName = "cmd.exe",
                        Arguments = $"/c \"{combined}\"",
                        UseShellExecute = false,
                        CreateNoWindow = true
                    };
                    using var proc = Process.Start(psi);
                    proc?.WaitForExit(5000);
                }
                else if (hasTask)
                {
                    string scriptPath = Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                        "DuoWallBySRMD", "switch-net.bat");
                    File.WriteAllText(scriptPath, $"@echo off\r\n{string.Join("\r\n", commands)}\r\n");

                    var psi = new ProcessStartInfo
                    {
                        FileName = "schtasks.exe",
                        Arguments = $"/run /tn \"{NetSwitchTaskName}\"",
                        UseShellExecute = false,
                        CreateNoWindow = true
                    };
                    using var proc = Process.Start(psi);
                    proc?.WaitForExit(3000);
                }
                else
                {
                    var psi = new ProcessStartInfo
                    {
                        FileName = "cmd.exe",
                        Arguments = $"/c \"{combined}\"",
                        UseShellExecute = true,
                        Verb = "runas",
                        WindowStyle = ProcessWindowStyle.Hidden,
                        CreateNoWindow = true
                    };
                    using var proc = Process.Start(psi);
                    proc?.WaitForExit(10000);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to switch network adapter: {ex.Message}");
            }
            finally
            {
                Thread.Sleep(1200);
            }
        });

        _isSwitching = false;
        _switchingToAdapterName = null;
        await RefreshAdaptersAsync();
    }

    /// <summary>
    /// Cycles to the next available network adapter.
    /// </summary>
    public static async Task CycleNextAdapterAsync()
    {
        var adapters = QueryAdaptersInternal();
        if (adapters.Count <= 1)
        {
            if (adapters.Count == 1 && !adapters[0].IsEnabled)
            {
                await SwitchToAdapterAsync(adapters[0].Name);
            }
            return;
        }

        int currentIdx = adapters.FindIndex(a => a.IsActive);
        if (currentIdx < 0)
        {
            currentIdx = adapters.FindIndex(a => a.IsEnabled);
        }

        int nextIdx = (currentIdx + 1) % adapters.Count;
        if (nextIdx < 0) nextIdx = 0;

        await SwitchToAdapterAsync(adapters[nextIdx].Name);
    }
}

