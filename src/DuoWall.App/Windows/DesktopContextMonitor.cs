using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using DuoWall.Core.Models;
using DuoWall.Core.Profiles;

namespace DuoWall.App.Windows;

/// <summary>
/// Captures current live Windows desktop state (AC vs battery, battery percentage, full-screen app state,
/// active display topology signature, and local time) for Intelligent Desktop Profile evaluation.
/// </summary>
public sealed class DesktopContextMonitor
{
    private enum QUERY_USER_NOTIFICATION_STATE
    {
        QUNS_NOT_PRESENT = 1,
        QUNS_BUSY = 2,
        QUNS_RUNNING_D3D_FULL_SCREEN = 3,
        QUNS_PRESENTATION_MODE = 4,
        QUNS_ACCEPTS_NOTIFICATIONS = 5,
        QUNS_QUIET_TIME = 6,
        QUNS_APP = 7
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SYSTEM_POWER_STATUS
    {
        public byte ACLineStatus;
        public byte BatteryFlag;
        public byte BatteryLifePercent;
        public byte SystemStatusFlag;
        public int BatteryLifeTime;
        public int BatteryFullLifeTime;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetSystemPowerStatus(out SYSTEM_POWER_STATUS lpSystemPowerStatus);

    [DllImport("shell32.dll")]
    private static extern int SHQueryUserNotificationState(out QUERY_USER_NOTIFICATION_STATE pquns);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);

    private static readonly HashSet<string> ShellWindowClasses = new(StringComparer.OrdinalIgnoreCase)
    {
        "Progman",
        "WorkerW",
        "Shell_TrayWnd",
        "Shell_SecondaryTrayWnd"
    };

    public DesktopContextSnapshot Capture(
        IReadOnlyList<MonitorInfo> monitors,
        string topologySignature,
        bool isStartup,
        string? manualOverrideProfileId)
    {
        var (isOnBattery, batteryPct) = ReadPowerState();
        bool isFullScreen = DetectFullScreenForegroundApp(monitors);

        return new DesktopContextSnapshot
        {
            LocalNow = DateTime.Now,
            IsStartup = isStartup,
            TopologySignature = topologySignature,
            IsFullScreenAppRunning = isFullScreen,
            IsOnBattery = isOnBattery,
            BatteryPercent = batteryPct,
            ManualOverrideProfileId = manualOverrideProfileId
        };
    }

    public static (bool IsOnBattery, int BatteryPercent) ReadPowerState()
    {
        try
        {
            if (!GetSystemPowerStatus(out var status))
            {
                return (false, 100);
            }

            // Desktop PCs without battery report BatteryFlag == 128 and ACLineStatus == 1
            bool noBattery = (status.BatteryFlag & 128) != 0;
            bool onBattery = !noBattery && status.ACLineStatus == 0;
            int pct = status.BatteryLifePercent <= 100 ? status.BatteryLifePercent : 100;
            return (onBattery, pct);
        }
        catch
        {
            return (false, 100);
        }
    }

    public static bool DetectFullScreenForegroundApp(IReadOnlyList<MonitorInfo> monitors)
    {
        try
        {
            if (SHQueryUserNotificationState(out var quns) == 0)
            {
                if (quns is QUERY_USER_NOTIFICATION_STATE.QUNS_RUNNING_D3D_FULL_SCREEN
                    or QUERY_USER_NOTIFICATION_STATE.QUNS_PRESENTATION_MODE)
                {
                    return true;
                }
            }

            IntPtr fg = GetForegroundWindow();
            if (fg == IntPtr.Zero) return false;

            _ = GetWindowThreadProcessId(fg, out uint pid);
            if (pid == (uint)Environment.ProcessId)
            {
                return false;
            }

            var sb = new StringBuilder(128);
            if (GetClassName(fg, sb, sb.Capacity) > 0 && ShellWindowClasses.Contains(sb.ToString()))
            {
                return false;
            }

            if (!GetWindowRect(fg, out var rect))
            {
                return false;
            }

            int width = rect.Right - rect.Left;
            int height = rect.Bottom - rect.Top;
            if (width <= 0 || height <= 0) return false;

            foreach (var mon in monitors)
            {
                // Matches or covers the entire physical monitor bounds (borderless fullscreen game / media player)
                if (rect.Left <= mon.Bounds.X + 2 &&
                    rect.Top <= mon.Bounds.Y + 2 &&
                    rect.Right >= mon.Bounds.Right - 2 &&
                    rect.Bottom >= mon.Bounds.Bottom - 2)
                {
                    return true;
                }
            }
        }
        catch
        {
            // Ignore access errors against elevated fullscreen windows
        }

        return false;
    }
}
