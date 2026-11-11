using System.Runtime.InteropServices;
using System.Text;
using DuoWall.Core.Models;
using Microsoft.Win32;

namespace DuoWall.App.Windows;

/// <summary>
/// Queries Windows Win32 display APIs, Per-Monitor DPI, DisplayConfig target names,
/// and hardware EDID registry descriptors for millimeter-accurate monitor detection.
/// </summary>
public sealed class Win32DisplayDetector
{
    private const int ENUM_CURRENT_SETTINGS = -1;
    private const uint EDD_GET_DEVICE_INTERFACE_NAME = 0x00000001;
    private const uint MONITORINFOF_PRIMARY = 0x00000001;
    private const uint QDC_ONLY_ACTIVE_PATHS = 0x00000002;

    public IReadOnlyList<MonitorInfo> DetectMonitors()
    {
        var friendlyNamesByGdiSource = QueryFriendlyMonitorNames();
        var monitors = new List<MonitorInfo>();

        EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (IntPtr hMonitor, IntPtr _, ref RECT _, IntPtr _) =>
        {
            var mi = new MONITORINFOEX { cbSize = Marshal.SizeOf<MONITORINFOEX>() };
            if (!GetMonitorInfo(hMonitor, ref mi))
                return true;

            var bounds = new PixelRect(
                mi.rcMonitor.left,
                mi.rcMonitor.top,
                mi.rcMonitor.right - mi.rcMonitor.left,
                mi.rcMonitor.bottom - mi.rcMonitor.top);

            var work = new PixelRect(
                mi.rcWork.left,
                mi.rcWork.top,
                mi.rcWork.right - mi.rcWork.left,
                mi.rcWork.bottom - mi.rcWork.top);

            uint dpiX = 96, dpiY = 96;
            try
            {
                if (GetDpiForMonitor(hMonitor, 0 /* MDT_EFFECTIVE_DPI */, out uint dx, out uint dy) == 0 && dx > 0)
                {
                    dpiX = dx;
                    dpiY = dy;
                }
            }
            catch { /* Windows < 8.1 fallback */ }

            var dm = new DEVMODE { dmSize = (ushort)Marshal.SizeOf<DEVMODE>() };
            EnumDisplaySettingsEx(mi.szDevice, ENUM_CURRENT_SETTINGS, ref dm, 0);

            var dd = new DISPLAY_DEVICE { cb = Marshal.SizeOf<DISPLAY_DEVICE>() };
            EnumDisplayDevices(mi.szDevice, 0, ref dd, EDD_GET_DEVICE_INTERFACE_NAME);

            var orientation = (DisplayOrientation)(dm.dmDisplayOrientation <= 3 ? dm.dmDisplayOrientation : 0);
            int refreshHz = dm.dmDisplayFrequency > 0 ? (int)dm.dmDisplayFrequency : 60;
            int nativeW = dm.dmPelsWidth > 0 ? (int)dm.dmPelsWidth : bounds.Width;
            int nativeH = dm.dmPelsHeight > 0 ? (int)dm.dmPelsHeight : bounds.Height;

            var edidInfo = ReadEdidForDeviceInterface(dd.DeviceID);

            string friendly = friendlyNamesByGdiSource.TryGetValue(mi.szDevice, out var qdcName) && !string.IsNullOrWhiteSpace(qdcName)
                ? qdcName
                : (!string.IsNullOrWhiteSpace(edidInfo.ModelName) ? edidInfo.ModelName : dd.DeviceString);

            if (string.IsNullOrWhiteSpace(friendly) || friendly.Equals("Generic PnP Monitor", StringComparison.OrdinalIgnoreCase))
            {
                friendly = !string.IsNullOrWhiteSpace(edidInfo.ModelName)
                    ? edidInfo.ModelName
                    : mi.szDevice.Replace(@"\\.\", string.Empty);
            }

            double physW = edidInfo.WidthMm;
            double physH = edidInfo.HeightMm;
            bool hasHwPhys = physW > 10 && physH > 10;

            // EDID reports landscape panel size; swap if rotated to Portrait
            if (hasHwPhys && (orientation == DisplayOrientation.Portrait || orientation == DisplayOrientation.PortraitFlipped))
            {
                (physW, physH) = (physH, physW);
            }

            string stableId = !string.IsNullOrWhiteSpace(dd.DeviceID) ? dd.DeviceID : mi.szDevice;

            monitors.Add(new MonitorInfo
            {
                Id = stableId,
                DeviceName = mi.szDevice,
                DeviceId = dd.DeviceID ?? string.Empty,
                FriendlyName = friendly.Trim(),
                Manufacturer = edidInfo.Manufacturer,
                Bounds = bounds,
                WorkingArea = work,
                NativeWidth = nativeW,
                NativeHeight = nativeH,
                Orientation = orientation,
                IsPrimary = (mi.dwFlags & MONITORINFOF_PRIMARY) != 0,
                DpiX = dpiX,
                DpiY = dpiY,
                ScalePercent = Math.Round(dpiX * 100.0 / 96.0),
                RefreshRateHz = refreshHz,
                PhysicalWidthMm = physW,
                PhysicalHeightMm = physH,
                HasHardwarePhysicalDimensions = hasHwPhys,
                IsConnected = true,
                IsIncluded = true
            });

            return true;
        }, IntPtr.Zero);

        return monitors
            .OrderBy(m => m.Bounds.X)
            .ThenBy(m => m.Bounds.Y)
            .ToList();
    }

    public static string ComputeTopologySignature(IReadOnlyList<MonitorInfo> monitors)
    {
        var parts = monitors
            .Where(m => m.IsConnected)
            .OrderBy(m => m.Id, StringComparer.OrdinalIgnoreCase)
            .Select(m => $"{m.Id}:{m.Bounds.X},{m.Bounds.Y},{m.Bounds.Width}x{m.Bounds.Height}:{(int)m.Orientation}:{m.ScalePercent:0}");
        return string.Join("|", parts);
    }

    private static (string Manufacturer, string ModelName, double WidthMm, double HeightMm) ReadEdidForDeviceInterface(string? deviceId)
    {
        if (string.IsNullOrWhiteSpace(deviceId))
            return (string.Empty, string.Empty, 0, 0);

        // Example: \\?\DISPLAY#DELD0DA#4&391f45bf&0&UID198203#{e6f07b5f-...}
        try
        {
            string trimmed = deviceId.Replace(@"\\?\", string.Empty);
            string[] parts = trimmed.Split('#');
            if (parts.Length < 3 || !parts[0].Equals("DISPLAY", StringComparison.OrdinalIgnoreCase))
                return (string.Empty, string.Empty, 0, 0);

            string pnpModel = parts[1];
            string instance = parts[2];
            string regPath = $@"SYSTEM\CurrentControlSet\Enum\DISPLAY\{pnpModel}\{instance}\Device Parameters";

            using var key = Registry.LocalMachine.OpenSubKey(regPath, writable: false);
            if (key?.GetValue("EDID") is not byte[] edid || edid.Length < 128)
                return (ExtractPnpVendor(pnpModel), string.Empty, 0, 0);

            string manufacturer = ParseEdidManufacturer(edid);
            string modelName = ParseEdidMonitorName(edid);

            // Exact millimeter dimensions from Detailed Timing Descriptor #1 (bytes 54..71)
            double widthMm = 0;
            double heightMm = 0;
            int pixelClock = edid[54] | (edid[55] << 8);
            if (pixelClock != 0)
            {
                int dtdW = edid[66] | ((edid[68] & 0xF0) << 4);
                int dtdH = edid[67] | ((edid[68] & 0x0F) << 8);
                if (dtdW >= 100 && dtdW <= 3000 && dtdH >= 80 && dtdH <= 2000)
                {
                    widthMm = dtdW;
                    heightMm = dtdH;
                }
            }

            // Fallback to EDID basic display parameters (bytes 21, 22 in cm)
            if (widthMm <= 0 || heightMm <= 0)
            {
                if (edid[21] > 0 && edid[22] > 0)
                {
                    widthMm = edid[21] * 10.0;
                    heightMm = edid[22] * 10.0;
                }
            }

            return (manufacturer, modelName, widthMm, heightMm);
        }
        catch
        {
            return (string.Empty, string.Empty, 0, 0);
        }
    }

    private static string ExtractPnpVendor(string pnpCode) =>
        pnpCode.Length >= 3 ? pnpCode[..3].ToUpperInvariant() : pnpCode;

    private static string ParseEdidManufacturer(byte[] edid)
    {
        ushort raw = (ushort)((edid[8] << 8) | edid[9]);
        char c1 = (char)(((raw >> 10) & 0x1F) + 'A' - 1);
        char c2 = (char)(((raw >> 5) & 0x1F) + 'A' - 1);
        char c3 = (char)((raw & 0x1F) + 'A' - 1);
        string code = $"{c1}{c2}{c3}";
        return code switch
        {
            "DEL" => "Dell",
            "SAM" => "Samsung",
            "GSM" => "LG",
            "ACI" or "AUS" => "ASUS",
            "BNQ" => "BenQ",
            "ACR" => "Acer",
            "HWP" => "HP",
            "LEN" => "Lenovo",
            "MSI" => "MSI",
            "AOC" => "AOC",
            "PHL" => "Philips",
            "PLN" => "Planar",
            "VSC" => "ViewSonic",
            "SNY" => "Sony",
            "GBT" => "Gigabyte",
            _ => code
        };
    }

    private static string ParseEdidMonitorName(byte[] edid)
    {
        for (int offset = 54; offset <= 108; offset += 18)
        {
            if (edid[offset] == 0x00 && edid[offset + 1] == 0x00 && edid[offset + 2] == 0x00 && edid[offset + 3] == 0xFC)
            {
                string raw = Encoding.ASCII.GetString(edid, offset + 5, 13);
                return raw.Trim('\n', '\r', ' ', '\0');
            }
        }
        return string.Empty;
    }

    private static Dictionary<string, string> QueryFriendlyMonitorNames()
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            if (GetDisplayConfigBufferSizes(QDC_ONLY_ACTIVE_PATHS, out uint pathCount, out uint modeCount) != 0)
                return map;

            var paths = new DISPLAYCONFIG_PATH_INFO[pathCount];
            var modes = new DISPLAYCONFIG_MODE_INFO[modeCount];
            if (QueryDisplayConfig(QDC_ONLY_ACTIVE_PATHS, ref pathCount, paths, ref modeCount, modes, IntPtr.Zero) != 0)
                return map;

            for (int i = 0; i < pathCount; i++)
            {
                var path = paths[i];
                var srcName = new DISPLAYCONFIG_SOURCE_DEVICE_NAME
                {
                    header = new DISPLAYCONFIG_DEVICE_INFO_HEADER
                    {
                        type = DISPLAYCONFIG_DEVICE_INFO_TYPE.GET_SOURCE_NAME,
                        size = (uint)Marshal.SizeOf<DISPLAYCONFIG_SOURCE_DEVICE_NAME>(),
                        adapterId = path.sourceInfo.adapterId,
                        id = path.sourceInfo.id
                    }
                };

                var tgtName = new DISPLAYCONFIG_TARGET_DEVICE_NAME
                {
                    header = new DISPLAYCONFIG_DEVICE_INFO_HEADER
                    {
                        type = DISPLAYCONFIG_DEVICE_INFO_TYPE.GET_TARGET_NAME,
                        size = (uint)Marshal.SizeOf<DISPLAYCONFIG_TARGET_DEVICE_NAME>(),
                        adapterId = path.targetInfo.adapterId,
                        id = path.targetInfo.id
                    }
                };

                if (DisplayConfigGetDeviceInfo(ref srcName) == 0 &&
                    DisplayConfigGetDeviceInfo(ref tgtName) == 0 &&
                    !string.IsNullOrWhiteSpace(srcName.viewGdiDeviceName) &&
                    !string.IsNullOrWhiteSpace(tgtName.monitorFriendlyDeviceName))
                {
                    map[srcName.viewGdiDeviceName] = tgtName.monitorFriendlyDeviceName.Trim();
                }
            }
        }
        catch { /* fallback cleanly */ }

        return map;
    }

    private delegate bool MonitorEnumProc(IntPtr hMonitor, IntPtr hdcMonitor, ref RECT lprcMonitor, IntPtr dwData);

    [DllImport("user32.dll")]
    private static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr lprcClip, MonitorEnumProc lpfnEnum, IntPtr dwData);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFOEX lpmi);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool EnumDisplayDevices(string? lpDevice, uint iDevNum, ref DISPLAY_DEVICE lpDisplayDevice, uint dwFlags);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool EnumDisplaySettingsEx(string lpszDeviceName, int iModeNum, ref DEVMODE lpDevMode, uint dwFlags);

    [DllImport("shcore.dll")]
    private static extern int GetDpiForMonitor(IntPtr hmonitor, int dpiType, out uint dpiX, out uint dpiY);

    [DllImport("user32.dll")]
    private static extern int GetDisplayConfigBufferSizes(uint flags, out uint numPathArrayElements, out uint numModeInfoArrayElements);

    [DllImport("user32.dll")]
    private static extern int QueryDisplayConfig(
        uint flags,
        ref uint numPathArrayElements,
        [Out] DISPLAYCONFIG_PATH_INFO[] pathArray,
        ref uint numModeInfoArrayElements,
        [Out] DISPLAYCONFIG_MODE_INFO[] modeInfoArray,
        IntPtr currentTopologyId);

    [DllImport("user32.dll")]
    private static extern int DisplayConfigGetDeviceInfo(ref DISPLAYCONFIG_SOURCE_DEVICE_NAME requestPacket);

    [DllImport("user32.dll")]
    private static extern int DisplayConfigGetDeviceInfo(ref DISPLAYCONFIG_TARGET_DEVICE_NAME requestPacket);

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT { public int left, top, right, bottom; }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct MONITORINFOEX
    {
        public int cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public uint dwFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string szDevice;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DISPLAY_DEVICE
    {
        public int cb;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string DeviceName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceString;
        public uint StateFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceID;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceKey;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DEVMODE
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmDeviceName;
        public ushort dmSpecVersion, dmDriverVersion, dmSize, dmDriverExtra;
        public uint dmFields;
        public int dmPositionX, dmPositionY;
        public uint dmDisplayOrientation, dmDisplayFixedOutput;
        public short dmColor, dmDuplex, dmYResolution, dmTTOption, dmCollate;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmFormName;
        public ushort dmLogPixels;
        public uint dmBitsPerPel, dmPelsWidth, dmPelsHeight, dmDisplayFlags, dmDisplayFrequency;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct LUID { public uint LowPart; public int HighPart; }

    [StructLayout(LayoutKind.Sequential)]
    private struct DISPLAYCONFIG_RATIONAL { public uint Numerator; public uint Denominator; }

    [StructLayout(LayoutKind.Sequential)]
    private struct DISPLAYCONFIG_PATH_SOURCE_INFO
    {
        public LUID adapterId;
        public uint id;
        public uint modeInfoIdx;
        public uint statusFlags;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DISPLAYCONFIG_PATH_TARGET_INFO
    {
        public LUID adapterId;
        public uint id;
        public uint modeInfoIdx;
        public uint outputTechnology;
        public uint rotation;
        public uint scaling;
        public DISPLAYCONFIG_RATIONAL refreshRate;
        public uint scanLineOrdering;
        [MarshalAs(UnmanagedType.Bool)] public bool targetAvailable;
        public uint statusFlags;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DISPLAYCONFIG_PATH_INFO
    {
        public DISPLAYCONFIG_PATH_SOURCE_INFO sourceInfo;
        public DISPLAYCONFIG_PATH_TARGET_INFO targetInfo;
        public uint flags;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DISPLAYCONFIG_MODE_INFO
    {
        public uint infoType;
        public uint id;
        public LUID adapterId;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 48)]
        public byte[] unionBytes;
    }

    private enum DISPLAYCONFIG_DEVICE_INFO_TYPE : uint
    {
        GET_SOURCE_NAME = 1,
        GET_TARGET_NAME = 2
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DISPLAYCONFIG_DEVICE_INFO_HEADER
    {
        public DISPLAYCONFIG_DEVICE_INFO_TYPE type;
        public uint size;
        public LUID adapterId;
        public uint id;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DISPLAYCONFIG_SOURCE_DEVICE_NAME
    {
        public DISPLAYCONFIG_DEVICE_INFO_HEADER header;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string viewGdiDeviceName;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DISPLAYCONFIG_TARGET_DEVICE_NAME
    {
        public DISPLAYCONFIG_DEVICE_INFO_HEADER header;
        public uint flags;
        public uint outputTechnology;
        public ushort edidManufactureId;
        public ushort edidProductCodeId;
        public uint connectorInstance;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
        public string monitorFriendlyDeviceName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string monitorDevicePath;
    }
}
