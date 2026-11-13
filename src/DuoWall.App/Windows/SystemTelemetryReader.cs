using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Management;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;

namespace DuoWall.App.Windows;

public sealed class DriveTelemetry
{
    public string Name { get; init; } = "C:\\";
    public double UsedGb { get; init; }
    public double TotalGb { get; init; }
    public double UsagePercent => TotalGb > 0 ? (UsedGb / TotalGb) * 100.0 : 0.0;
}

public sealed class TelemetrySnapshot
{
    public double CpuUsagePercent { get; init; }
    public double RamUsedGb { get; init; }
    public double RamTotalGb { get; init; }
    public double RamUsagePercent { get; init; }
    public double NetworkDownKbps { get; init; }
    public double NetworkUpKbps { get; init; }
    public string GpuName { get; init; } = "Graphics Adapter";
    public double? CpuTemperatureC { get; init; }
    public double? GpuTemperatureC { get; init; }
    public string ThermalStatusNote { get; init; } = "Checking sensors...";
    public List<DriveTelemetry> Drives { get; init; } = new();
    public TimeSpan SystemUptime { get; init; }
    public string PowerSourceText { get; init; } = "AC Power";
    public DateTime CapturedAt { get; init; } = DateTime.Now;
}

/// <summary>
/// Non-admin live Windows system telemetry collector using native Win32 APIs (GetSystemTimes,
/// GlobalMemoryStatusEx, NetworkInterface, DriveInfo) plus throttled WMI/NVSMI thermal probes.
/// </summary>
public sealed class SystemTelemetryReader
{
    [StructLayout(LayoutKind.Sequential)]
    private struct FILETIME
    {
        public uint dwLowDateTime;
        public uint dwHighDateTime;
        public ulong ToUInt64() => ((ulong)dwHighDateTime << 32) | dwLowDateTime;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct MEMORYSTATUSEX
    {
        public uint dwLength;
        public uint dwMemoryLoad;
        public ulong ullTotalPhys;
        public ulong ullAvailPhys;
        public ulong ullTotalPageFile;
        public ulong ullAvailPageFile;
        public ulong ullTotalVirtual;
        public ulong ullAvailVirtual;
        public ulong ullAvailExtendedVirtual;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetSystemTimes(out FILETIME lpIdleTime, out FILETIME lpKernelTime, out FILETIME lpUserTime);

    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX lpBuffer);

    private ulong _prevIdle;
    private ulong _prevKernel;
    private ulong _prevUser;
    private bool _hasCpuBaseline;

    private long _prevBytesRx;
    private long _prevBytesTx;
    private DateTime _prevNetUtc;
    private bool _hasNetBaseline;

    private string? _cachedGpuName;
    private string? _nvidiaSmiPath;
    private bool _checkedNvidiaSmi;

    private DateTime _lastThermalPollUtc = DateTime.MinValue;
    private double? _lastCpuTemp;
    private double? _lastGpuTemp;
    private string _lastThermalNote = "No user-mode thermal sensor";

    public TelemetrySnapshot Sample(bool includeDetailedSensors = true)
    {
        double cpuPct = ReadCpuPercent();
        var (ramUsedGb, ramTotalGb, ramPct) = ReadMemory();
        var (rxKbps, txKbps) = ReadNetworkKbps();
        var drives = ReadFixedDrives();

        _cachedGpuName ??= QueryGpuDisplayName();

        if (includeDetailedSensors && (DateTime.UtcNow - _lastThermalPollUtc).TotalSeconds >= 6)
        {
            _lastThermalPollUtc = DateTime.UtcNow;
            PollTemperatures();
        }

        var (isOnBattery, battPct) = DesktopContextMonitor.ReadPowerState();
        string powerText = isOnBattery ? $"Battery ({battPct}%)" : "AC Power";

        return new TelemetrySnapshot
        {
            CpuUsagePercent = cpuPct,
            RamUsedGb = ramUsedGb,
            RamTotalGb = ramTotalGb,
            RamUsagePercent = ramPct,
            NetworkDownKbps = rxKbps,
            NetworkUpKbps = txKbps,
            GpuName = _cachedGpuName,
            CpuTemperatureC = _lastCpuTemp,
            GpuTemperatureC = _lastGpuTemp,
            ThermalStatusNote = _lastThermalNote,
            Drives = drives,
            SystemUptime = TimeSpan.FromMilliseconds(Environment.TickCount64),
            PowerSourceText = powerText,
            CapturedAt = DateTime.Now
        };
    }

    private double ReadCpuPercent()
    {
        if (!GetSystemTimes(out var idleFt, out var kernelFt, out var userFt))
        {
            return 0;
        }

        ulong idle = idleFt.ToUInt64();
        ulong kernel = kernelFt.ToUInt64();
        ulong user = userFt.ToUInt64();

        if (!_hasCpuBaseline)
        {
            _prevIdle = idle;
            _prevKernel = kernel;
            _prevUser = user;
            _hasCpuBaseline = true;
            return 0;
        }

        ulong idleDiff = idle >= _prevIdle ? idle - _prevIdle : 0;
        ulong kernelDiff = kernel >= _prevKernel ? kernel - _prevKernel : 0;
        ulong userDiff = user >= _prevUser ? user - _prevUser : 0;

        _prevIdle = idle;
        _prevKernel = kernel;
        _prevUser = user;

        // Note: Win32 kernel time already includes idle time
        ulong total = kernelDiff + userDiff;
        if (total == 0)
        {
            return 0;
        }

        double busy = (double)(total > idleDiff ? total - idleDiff : 0) / total;
        return Math.Clamp(busy * 100.0, 0.0, 100.0);
    }

    private static (double UsedGb, double TotalGb, double Pct) ReadMemory()
    {
        var mem = new MEMORYSTATUSEX { dwLength = (uint)Marshal.SizeOf<MEMORYSTATUSEX>() };
        if (!GlobalMemoryStatusEx(ref mem) || mem.ullTotalPhys == 0)
        {
            return (0, 0, 0);
        }

        const double bytesPerGb = 1024.0 * 1024.0 * 1024.0;
        double totalGb = mem.ullTotalPhys / bytesPerGb;
        double usedGb = (mem.ullTotalPhys - mem.ullAvailPhys) / bytesPerGb;
        double pct = mem.dwMemoryLoad;
        return (Math.Round(usedGb, 1), Math.Round(totalGb, 1), pct);
    }

    private (double DownKbps, double UpKbps) ReadNetworkKbps()
    {
        long totalRx = 0;
        long totalTx = 0;

        try
        {
            foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (nic.OperationalStatus != OperationalStatus.Up)
                    continue;
                if (nic.NetworkInterfaceType is NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel)
                    continue;

                var stats = nic.GetIPStatistics();
                totalRx += stats.BytesReceived;
                totalTx += stats.BytesSent;
            }
        }
        catch
        {
            return (0, 0);
        }

        DateTime now = DateTime.UtcNow;
        if (!_hasNetBaseline)
        {
            _prevBytesRx = totalRx;
            _prevBytesTx = totalTx;
            _prevNetUtc = now;
            _hasNetBaseline = true;
            return (0, 0);
        }

        double seconds = Math.Max(0.25, (now - _prevNetUtc).TotalSeconds);
        double rxKbps = Math.Max(0, (totalRx - _prevBytesRx) / 1024.0 / seconds);
        double txKbps = Math.Max(0, (totalTx - _prevBytesTx) / 1024.0 / seconds);

        _prevBytesRx = totalRx;
        _prevBytesTx = totalTx;
        _prevNetUtc = now;

        return (Math.Round(rxKbps, 1), Math.Round(txKbps, 1));
    }

    private static List<DriveTelemetry> ReadFixedDrives()
    {
        var list = new List<DriveTelemetry>();
        const double bytesPerGb = 1024.0 * 1024.0 * 1024.0;

        try
        {
            foreach (var drive in DriveInfo.GetDrives())
            {
                if (drive.DriveType != DriveType.Fixed || !drive.IsReady || drive.TotalSize <= 0)
                    continue;

                double totalGb = drive.TotalSize / bytesPerGb;
                double usedGb = (drive.TotalSize - drive.AvailableFreeSpace) / bytesPerGb;
                list.Add(new DriveTelemetry
                {
                    Name = drive.Name.TrimEnd('\\'),
                    TotalGb = Math.Round(totalGb, 0),
                    UsedGb = Math.Round(usedGb, 0)
                });
            }
        }
        catch
        {
            // Ignore inaccessible drives
        }

        return list;
    }

    private static string QueryGpuDisplayName()
    {
        try
        {
            using var searcher = new ManagementObjectSearcher(@"root\CIMV2", "SELECT Name FROM Win32_VideoController");
            foreach (ManagementObject obj in searcher.Get())
            {
                string? name = obj["Name"]?.ToString();
                if (!string.IsNullOrWhiteSpace(name))
                    return name.Trim();
            }
        }
        catch
        {
            // Fallback below
        }

        return "Windows Display Adapter";
    }

    private void PollTemperatures()
    {
        double? cpuTemp = TryReadAcpiThermalZoneCelsius();
        double? gpuTemp = TryReadNvidiaGpuTempCelsius();

        _lastCpuTemp = cpuTemp;
        _lastGpuTemp = gpuTemp;

        if (cpuTemp.HasValue || gpuTemp.HasValue)
        {
            _lastThermalNote = cpuTemp.HasValue && gpuTemp.HasValue
                ? "ACPI + GPU sensors"
                : gpuTemp.HasValue
                    ? "NVIDIA GPU sensor (CPU ACPI requires Admin/BIOS)"
                    : "ACPI thermal zone";
        }
        else
        {
            _lastThermalNote = "CPU/GPU thermal sensors unavailable without Admin driver";
        }
    }

    private static double? TryReadAcpiThermalZoneCelsius()
    {
        try
        {
            using var searcher = new ManagementObjectSearcher(@"root\WMI", "SELECT CurrentTemperature FROM MSAcpi_ThermalZoneTemperature");
            double? hottest = null;
            foreach (ManagementObject obj in searcher.Get())
            {
                if (obj["CurrentTemperature"] == null) continue;
                double tenthsKelvin = Convert.ToDouble(obj["CurrentTemperature"]);
                double celsius = (tenthsKelvin / 10.0) - 273.15;
                if (celsius is >= 10 and <= 115)
                {
                    hottest = hottest.HasValue ? Math.Max(hottest.Value, celsius) : celsius;
                }
            }

            return hottest.HasValue ? Math.Round(hottest.Value, 1) : null;
        }
        catch
        {
            // Expected on desktop boards or when not elevated as Admin
            return null;
        }
    }

    private double? TryReadNvidiaGpuTempCelsius()
    {
        if (!_checkedNvidiaSmi)
        {
            _checkedNvidiaSmi = true;
            string sysDir = Environment.GetFolderPath(Environment.SpecialFolder.System);
            string progFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            string[] candidates =
            {
                Path.Combine(sysDir, "nvidia-smi.exe"),
                Path.Combine(progFiles, "NVIDIA Corporation", "NVSMI", "nvidia-smi.exe")
            };
            _nvidiaSmiPath = candidates.FirstOrDefault(File.Exists);
        }

        if (string.IsNullOrEmpty(_nvidiaSmiPath))
        {
            return null;
        }

        try
        {
            using var proc = Process.Start(new ProcessStartInfo
            {
                FileName = _nvidiaSmiPath,
                Arguments = "--query-gpu=temperature.gpu --format=csv,noheader,nounits",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            });

            if (proc == null) return null;
            string output = proc.StandardOutput.ReadToEnd();
            proc.WaitForExit(800);

            string? firstLine = output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
            if (double.TryParse(firstLine?.Trim(), out double c) && c is >= 10 and <= 115)
            {
                return c;
            }
        }
        catch
        {
            // Ignore CLI probe errors
        }

        return null;
    }
}

