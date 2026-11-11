using System.Diagnostics;
using System.Windows.Threading;
using Microsoft.Win32;

namespace DuoWall.App.Windows;

/// <summary>
/// Manages Windows startup registration (HKCU Run) and debounced display topology watching.
/// </summary>
public sealed class StartupAndWatcherService : IDisposable
{
    private const string RunRegistryPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string AppRunValueName = "DuoWallBySRMD";

    private readonly DispatcherTimer _debounceTimer;
    private bool _started;

    public event EventHandler? DisplayConfigurationStabilized;

    public StartupAndWatcherService()
    {
        _debounceTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(1500)
        };
        _debounceTimer.Tick += (_, _) =>
        {
            _debounceTimer.Stop();
            DisplayConfigurationStabilized?.Invoke(this, EventArgs.Empty);
        };
    }

    public void StartWatching()
    {
        if (_started) return;
        SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;
        _started = true;
    }

    private void OnDisplaySettingsChanged(object? sender, EventArgs e)
    {
        _debounceTimer.Stop();
        _debounceTimer.Start();
    }

    public static bool IsStartupEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunRegistryPath, writable: false);
            return key?.GetValue(AppRunValueName) is string s && !string.IsNullOrWhiteSpace(s);
        }
        catch
        {
            return false;
        }
    }

    public static void SetStartupEnabled(bool enabled)
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunRegistryPath, writable: true)
                        ?? Registry.CurrentUser.CreateSubKey(RunRegistryPath, writable: true);
        if (enabled)
        {
            string? exePath = Environment.ProcessPath ?? Process.GetCurrentProcess().MainModule?.FileName;
            if (!string.IsNullOrWhiteSpace(exePath))
                key.SetValue(AppRunValueName, $"\"{exePath}\" --startup-apply");
        }
        else
        {
            key.DeleteValue(AppRunValueName, throwOnMissingValue: false);
        }
    }

    public void Dispose()
    {
        if (_started)
        {
            SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;
            _started = false;
        }
        _debounceTimer.Stop();
    }
}
