using System.IO;
using System.Runtime.InteropServices;
using DuoWall.Core.Models;

namespace DuoWall.App.Windows;

/// <summary>
/// Native Windows IDesktopWallpaper COM integration for applying individual
/// pre-composed wallpapers to specific monitors without using Windows Span distortion.
/// </summary>
public sealed class DesktopWallpaperApplier
{
    public WindowsWallpaperBackup CaptureCurrentWallpaperState()
    {
        return RunSta(() =>
        {
            var backup = new WindowsWallpaperBackup { CapturedUtc = DateTime.UtcNow };
            var com = CreateDesktopWallpaper();
            try
            {
                com.GetPosition(out var pos);
                backup.DesktopPosition = (int)pos;
                com.GetBackgroundColor(out uint color);
                backup.BackgroundColor = color;
                com.GetMonitorDevicePathCount(out uint count);

                for (uint i = 0; i < count; i++)
                {
                    try
                    {
                        com.GetMonitorDevicePathAt(i, out string id);
                        if (string.IsNullOrWhiteSpace(id)) continue;
                        com.GetWallpaper(id, out string wp);
                        backup.MonitorWallpapers[id] = wp ?? string.Empty;
                    }
                    catch { /* disconnected phantom monitor path */ }
                }
            }
            finally
            {
                Marshal.ReleaseComObject(com);
            }
            return backup;
        });
    }

    /// <summary>
    /// Resolves the live Windows wallpaper file path currently displayed on each detected MonitorInfo.
    /// </summary>
    public Dictionary<string, string> GetActiveWallpaperPaths(IReadOnlyList<MonitorInfo> monitors)
    {
        return RunSta(() =>
        {
            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var com = CreateDesktopWallpaper();
            try
            {
                com.GetMonitorDevicePathCount(out uint count);
                var comList = new List<(string ComId, PixelRect? Rect, string Path)>();
                for (uint i = 0; i < count; i++)
                {
                    try
                    {
                        com.GetMonitorDevicePathAt(i, out string id);
                        if (string.IsNullOrWhiteSpace(id)) continue;
                        com.GetWallpaper(id, out string wp);
                        PixelRect? rect = null;
                        try
                        {
                            com.GetMonitorRECT(id, out var rc);
                            rect = new PixelRect(rc.left, rc.top, rc.right - rc.left, rc.bottom - rc.top);
                        }
                        catch { }
                        comList.Add((id, rect, wp ?? string.Empty));
                    }
                    catch { }
                }

                var matchCandidates = comList.Select(x => (x.ComId, x.Rect)).ToList();
                foreach (var mon in monitors)
                {
                    string? matched = MatchComMonitorId(mon, matchCandidates);
                    if (!string.IsNullOrWhiteSpace(matched))
                    {
                        var found = comList.FirstOrDefault(x => string.Equals(x.ComId, matched, StringComparison.OrdinalIgnoreCase));
                        if (!string.IsNullOrWhiteSpace(found.Path) && File.Exists(found.Path))
                        {
                            result[mon.Id] = found.Path;
                        }
                    }
                }
            }
            catch { }
            finally
            {
                Marshal.ReleaseComObject(com);
            }

            return result;
        });
    }

    public void ApplyMonitorWallpapers(
        IReadOnlyList<MonitorInfo> monitors,
        IReadOnlyDictionary<string, string> generatedPathsByMonitorId,
        string? backgroundColorHex = null)
    {
        RunSta(() =>
        {
            var com = CreateDesktopWallpaper();
            try
            {
                com.Enable(true);
                com.SetPosition(DESKTOP_WALLPAPER_POSITION.DWPOS_FILL);

                if (!string.IsNullOrWhiteSpace(backgroundColorHex))
                {
                    com.SetBackgroundColor(HexToColorRef(backgroundColorHex));
                }

                com.GetMonitorDevicePathCount(out uint count);
                var comMonitors = new List<(string ComId, PixelRect? Rect)>();
                for (uint i = 0; i < count; i++)
                {
                    try
                    {
                        com.GetMonitorDevicePathAt(i, out string id);
                        if (string.IsNullOrWhiteSpace(id)) continue;
                        PixelRect? rect = null;
                        try
                        {
                            com.GetMonitorRECT(id, out var rc);
                            rect = new PixelRect(rc.left, rc.top, rc.right - rc.left, rc.bottom - rc.top);
                        }
                        catch { /* phantom / inactive display path */ }

                        comMonitors.Add((id, rect));
                    }
                    catch { }
                }

                foreach (var monitor in monitors)
                {
                    if (!generatedPathsByMonitorId.TryGetValue(monitor.Id, out string? filePath) ||
                        string.IsNullOrWhiteSpace(filePath) ||
                        !File.Exists(filePath))
                    {
                        continue;
                    }

                    string? matchedComId = MatchComMonitorId(monitor, comMonitors);
                    if (!string.IsNullOrWhiteSpace(matchedComId))
                    {
                        com.SetWallpaper(matchedComId, filePath!);
                    }
                }
            }
            finally
            {
                Marshal.ReleaseComObject(com);
            }
            return true;
        });
    }

    public void RestoreBackup(WindowsWallpaperBackup backup)
    {
        RunSta(() =>
        {
            var com = CreateDesktopWallpaper();
            try
            {
                com.Enable(true);
                com.SetBackgroundColor(backup.BackgroundColor);
                com.SetPosition((DESKTOP_WALLPAPER_POSITION)backup.DesktopPosition);

                foreach (var kv in backup.MonitorWallpapers)
                {
                    try
                    {
                        if (!string.IsNullOrWhiteSpace(kv.Value) && File.Exists(kv.Value))
                            com.SetWallpaper(kv.Key, kv.Value);
                    }
                    catch { /* monitor may no longer be connected */ }
                }
            }
            finally
            {
                Marshal.ReleaseComObject(com);
            }
            return true;
        });
    }

    private static string? MatchComMonitorId(
        MonitorInfo monitor,
        List<(string ComId, PixelRect? Rect)> comMonitors)
    {
        // 1. Exact or prefix match by DeviceID interface string
        if (!string.IsNullOrWhiteSpace(monitor.DeviceId))
        {
            var byId = comMonitors.FirstOrDefault(c =>
                string.Equals(c.ComId, monitor.DeviceId, StringComparison.OrdinalIgnoreCase) ||
                c.ComId.Contains(ExtractHardwareSegment(monitor.DeviceId), StringComparison.OrdinalIgnoreCase));
            if (!string.IsNullOrWhiteSpace(byId.ComId))
                return byId.ComId;
        }

        // 2. Match by exact virtual desktop bounds
        var byRect = comMonitors.FirstOrDefault(c =>
            c.Rect.HasValue && c.Rect.Value == monitor.Bounds);
        if (!string.IsNullOrWhiteSpace(byRect.ComId))
            return byRect.ComId;

        return null;
    }

    private static string ExtractHardwareSegment(string deviceId)
    {
        string[] parts = deviceId.Split('#');
        return parts.Length >= 3 ? $"{parts[1]}#{parts[2]}" : deviceId;
    }

    private static uint HexToColorRef(string hex)
    {
        string clean = hex.Trim().TrimStart('#');
        if (clean.Length == 6 &&
            byte.TryParse(clean[0..2], System.Globalization.NumberStyles.HexNumber, null, out byte r) &&
            byte.TryParse(clean[2..4], System.Globalization.NumberStyles.HexNumber, null, out byte g) &&
            byte.TryParse(clean[4..6], System.Globalization.NumberStyles.HexNumber, null, out byte b))
        {
            // COLORREF is 0x00BBGGRR
            return (uint)(r | (g << 8) | (b << 16));
        }
        return 0;
    }

    private static T RunSta<T>(Func<T> action)
    {
        if (Thread.CurrentThread.GetApartmentState() == ApartmentState.STA)
            return action();

        T? result = default;
        Exception? error = null;
        var thread = new Thread(() =>
        {
            try { result = action(); }
            catch (Exception ex) { error = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (error != null) throw error;
        return result!;
    }

    private static IDesktopWallpaper CreateDesktopWallpaper()
    {
        var type = Type.GetTypeFromCLSID(new Guid("C2CF3110-460E-4FC1-B9D0-8A1C0C9CC4BD"))
                   ?? throw new PlatformNotSupportedException("Windows IDesktopWallpaper COM server is unavailable.");
        return (IDesktopWallpaper)Activator.CreateInstance(type)!;
    }

    public enum DESKTOP_WALLPAPER_POSITION
    {
        DWPOS_CENTER = 0,
        DWPOS_TILE = 1,
        DWPOS_STRETCH = 2,
        DWPOS_FIT = 3,
        DWPOS_FILL = 4,
        DWPOS_SPAN = 5
    }

    [Flags]
    public enum DESKTOP_SLIDESHOW_OPTIONS
    {
        DSO_SHUFFLEITEMS = 0x01
    }

    public enum DESKTOP_SLIDESHOW_DIRECTION
    {
        DSD_FORWARD = 0,
        DSD_BACKWARD = 1
    }

    [Flags]
    public enum DESKTOP_SLIDESHOW_STATE
    {
        DSS_ENABLED = 0x01,
        DSS_SLIDESHOW = 0x02,
        DSS_DISABLED_BY_REMOTE_SESSION = 0x04
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT
    {
        public int left, top, right, bottom;
    }

    [ComImport]
    [Guid("B92B56A9-8B55-4E14-9A89-0199BBB6F93B")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IDesktopWallpaper
    {
        void SetWallpaper([MarshalAs(UnmanagedType.LPWStr)] string? monitorID, [MarshalAs(UnmanagedType.LPWStr)] string wallpaper);
        void GetWallpaper([MarshalAs(UnmanagedType.LPWStr)] string monitorID, [MarshalAs(UnmanagedType.LPWStr)] out string wallpaper);
        void GetMonitorDevicePathAt(uint monitorIndex, [MarshalAs(UnmanagedType.LPWStr)] out string monitorID);
        void GetMonitorDevicePathCount(out uint count);
        void GetMonitorRECT([MarshalAs(UnmanagedType.LPWStr)] string monitorID, out RECT displayRect);
        void SetBackgroundColor(uint color);
        void GetBackgroundColor(out uint color);
        void SetPosition(DESKTOP_WALLPAPER_POSITION position);
        void GetPosition(out DESKTOP_WALLPAPER_POSITION position);
        void SetSlideshow(IntPtr items);
        void GetSlideshow(out IntPtr items);
        void SetSlideshowOptions(DESKTOP_SLIDESHOW_OPTIONS options, uint slideshowTick);
        void GetSlideshowOptions(out DESKTOP_SLIDESHOW_OPTIONS options, out uint slideshowTick);
        void AdvanceSlideshow([MarshalAs(UnmanagedType.LPWStr)] string? monitorID, DESKTOP_SLIDESHOW_DIRECTION direction);
        void GetStatus(out DESKTOP_SLIDESHOW_STATE state);
        void Enable([MarshalAs(UnmanagedType.Bool)] bool enable);
    }
}
