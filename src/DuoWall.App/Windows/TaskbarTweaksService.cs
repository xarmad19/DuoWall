using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Media;
using System.Windows.Threading;
using DuoWall.App.Controls;
using DuoWall.Core.Models;
using DuoWall.Core.Services;
using DuoWall.Core.Theming;

namespace DuoWall.App.Windows;

/// <summary>
/// Manages Windows taskbar customization on Windows 10/11:
///  - Universal Glassmorphism acrylic/blur tint matching DuoWall widgets
///  - Invisible / crystal-transparent taskbar background
///  - macOS-style centered taskbar icons + resizable Glass Dock backdrop
///  - Smooth 60Hz WinEvent + fast-frame synchronized tracking during taskbar auto-hide slides
///  - Multi-monitor taskbar display visibility (Both / Primary Only / Secondary Only)
///  - Auto-Hide state & instant 1-click Restore Default Windows Taskbar
/// </summary>
public sealed class TaskbarTweaksService : IDisposable
{
    #region Win32 Interop
    private enum WindowCompositionAttribute
    {
        WCA_ACCENT_POLICY = 19
    }

    private enum AccentState
    {
        ACCENT_DISABLED = 0,
        ACCENT_ENABLE_GRADIENT = 1,
        ACCENT_ENABLE_TRANSPARENTGRADIENT = 2,
        ACCENT_ENABLE_BLURBEHIND = 3,
        ACCENT_ENABLE_ACRYLICBLURBEHIND = 4,
        ACCENT_INVALID_STATE = 5
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct AccentPolicy
    {
        public AccentState AccentState;
        public int AccentFlags;
        public uint GradientColor; // ABGR
        public int AnimationId;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WindowCompositionAttributeData
    {
        public WindowCompositionAttribute Attribute;
        public IntPtr Data;
        public int SizeOfData;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left, Top, Right, Bottom;
        public int Width => Right - Left;
        public int Height => Bottom - Top;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct APPBARDATA
    {
        public uint cbSize;
        public IntPtr hWnd;
        public uint uCallbackMessage;
        public uint uEdge;
        public RECT rc;
        public IntPtr lParam;
    }

    private sealed class DockLayoutCache
    {
        public int CachedLeftPx;
        public int CachedHeightPx;
        public int LastTrayTopPx;
        public string CachedStyleKey = string.Empty;
        public ImageSource? CachedSlice;
        public MonitorInfo? TargetMonitor;
        public double DpiScale = 1.0;
    }

    private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);
    private delegate void WinEventDelegate(IntPtr hWinEventHook, uint eventType, IntPtr hwnd, int idObject, int idChild, uint dwEventThread, uint dwmsEventTime);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int SetWindowCompositionAttribute(IntPtr hwnd, ref WindowCompositionAttributeData data);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr FindWindow(string lpClassName, string? lpWindowName);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr FindWindowEx(IntPtr parentHandle, IntPtr childAfter, string? lclassName, string? windowTitle);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool EnumChildWindows(IntPtr hWndParent, EnumWindowsProc lpEnumFunc, IntPtr lParam);

    [DllImport("user32.dll", ExactSpelling = true)]
    private static extern IntPtr GetParent(IntPtr hWnd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr hwnd);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [DllImport("user32.dll")]
    private static extern IntPtr SetWinEventHook(
        uint eventMin,
        uint eventMax,
        IntPtr hmodWinEventProc,
        WinEventDelegate lpfnWinEventProc,
        uint idProcess,
        uint idThread,
        uint dwFlags);

    [DllImport("user32.dll")]
    private static extern bool UnhookWinEvent(IntPtr hWinEventHook);

    [DllImport("shell32.dll", CallingConvention = CallingConvention.StdCall)]
    private static extern uint SHAppBarMessage(uint dwMessage, ref APPBARDATA pData);

    [DllImport("oleacc.dll")]
    private static extern int AccessibleObjectFromWindow(
        IntPtr hwnd,
        uint dwId,
        ref Guid riid,
        [MarshalAs(UnmanagedType.Interface)] out IAccessible ppvObject);

    [DllImport("oleacc.dll")]
    private static extern int AccessibleChildren(
        IAccessible paccContainer,
        int iChildStart,
        int cChildren,
        [Out, MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 2)] object[] rgvarChildren,
        out int pcObtained);

    [ComImport, Guid("618736E0-3C3D-11CF-810C-00AA00389B71"), InterfaceType(ComInterfaceType.InterfaceIsDual)]
    private interface IAccessible
    {
        [return: MarshalAs(UnmanagedType.IDispatch)] object? get_accParent();
        int get_accChildCount();
        [return: MarshalAs(UnmanagedType.IDispatch)] object? get_accChild(object varChild);
        string? get_accName(object varChild);
        string? get_accValue(object varChild);
        string? get_accDescription(object varChild);
        object? get_accRole(object varChild);
        object? get_accState(object varChild);
        string? get_accHelp(object varChild);
        int get_accHelpTopic(out string pszHelpFile, object varChild);
        string? get_accKeyboardShortcut(object varChild);
        object? get_accFocus();
        object? get_accSelection();
        string? get_accDefaultAction(object varChild);
        void accSelect(int flagsSelect, object varChild);
        void accLocation(out int pxLeft, out int pyTop, out int pcxWidth, out int pcyHeight, object varChild);
    }

    private const uint OBJID_CLIENT = 0xFFFFFFFC;
    private const int STATE_SYSTEM_INVISIBLE = 0x00008000;
    private const int STATE_SYSTEM_OFFSCREEN = 0x00010000;
    private const int ROLE_SYSTEM_MENUITEM = 0x0C;
    private const int ROLE_SYSTEM_PUSHBUTTON = 0x2B;
    private const int ROLE_SYSTEM_BUTTONDROPDOWN = 0x38;
    private const int ROLE_SYSTEM_SPLITBUTTON = 0x3E;

    private const int SW_HIDE = 0;
    private const int SW_SHOW = 5;
    private const uint SWP_NOZORDER = 0x0004;
    private const uint SWP_NOACTIVATE = 0x0010;
    private const uint ABM_GETSTATE = 0x00000004;
    private const uint ABM_SETSTATE = 0x0000000a;
    private const uint ABS_AUTOHIDE = 0x0000001;
    private const uint ABS_ALWAYSONTOP = 0x0000002;
    private const uint EVENT_OBJECT_LOCATIONCHANGE = 0x800B;
    private const uint WINEVENT_OUTOFCONTEXT = 0x0000;
    #endregion

    private readonly MonitorWallpaperSampler _wallpaperSampler;
    private readonly Win32DisplayDetector _displayDetector;
    private readonly DispatcherTimer _reapplyTimer;
    private readonly DispatcherTimer _smoothTrackTimer;
    private readonly Dictionary<IntPtr, TaskbarDockOverlayWindow> _dockWindows = new();
    private readonly Dictionary<IntPtr, DockLayoutCache> _dockCaches = new();

    private WinEventDelegate? _winEventProc;
    private IntPtr _trayMoveHook = IntPtr.Zero;
    private bool _syncPending;

    private AppSettings _settings = new();
    private GlassmorphismStyle _glassStyle = GlassmorphismStyle.BuiltInPresets[0].Clone();
    private bool _isAutoHideManaged;
    private bool _disposed;

    public TaskbarTweaksService(MonitorWallpaperSampler wallpaperSampler, Win32DisplayDetector displayDetector)
    {
        _wallpaperSampler = wallpaperSampler;
        _displayDetector = displayDetector;

        _reapplyTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(120)
        };
        _reapplyTimer.Tick += (_, _) => ApplyTick();

        // High-frequency sync tick runs ONLY during auto-hide / active slide so icons & dock stay locked frame-for-frame
        _smoothTrackTimer = new DispatcherTimer(DispatcherPriority.Render)
        {
            Interval = TimeSpan.FromMilliseconds(14)
        };
        _smoothTrackTimer.Tick += (_, _) => SyncDockPositionsFast();
    }

    public bool IsAnyTweakActive =>
        _settings.EnableTaskbarGlass ||
        _settings.MakeTaskbarInvisible ||
        _settings.CenterTaskbarIcons ||
        _settings.TaskbarDisplayTarget != TaskbarDisplayMode.Both;

    public void UpdateConfiguration(AppSettings settings, GlassmorphismStyle glassStyle, bool applyAutoHideFlag = false)
    {
        _settings = settings;
        _glassStyle = glassStyle.Clone();

        if (applyAutoHideFlag)
            SetTaskbarAutoHide(settings.AutoHideTaskbar);

        if (IsAnyTweakActive)
        {
            ApplyTick();
            if (!_reapplyTimer.IsEnabled)
                _reapplyTimer.Start();

            bool trackMotion = _settings.CenterTaskbarIcons && _settings.EnableTaskbarGlassDock;
            if (trackMotion)
            {
                EnsureWinEventHookInstalled();
                if (_settings.AutoHideTaskbar && !_smoothTrackTimer.IsEnabled)
                    _smoothTrackTimer.Start();
                else if (!_settings.AutoHideTaskbar && _smoothTrackTimer.IsEnabled)
                    _smoothTrackTimer.Stop();
            }
            else
            {
                UninstallWinEventHook();
                _smoothTrackTimer.Stop();
            }
        }
        else
        {
            _reapplyTimer.Stop();
            _smoothTrackTimer.Stop();
            UninstallWinEventHook();
            RestoreVisualTaskbarDefaults(restoreAutoHide: false);
        }
    }

    private void EnsureWinEventHookInstalled()
    {
        if (_trayMoveHook != IntPtr.Zero) return;
        IntPtr primary = FindWindow("Shell_TrayWnd", null);
        if (primary == IntPtr.Zero) return;

        uint tid = GetWindowThreadProcessId(primary, out uint pid);
        if (tid == 0) return;

        _winEventProc ??= OnTrayWinEvent;
        _trayMoveHook = SetWinEventHook(
            EVENT_OBJECT_LOCATIONCHANGE,
            EVENT_OBJECT_LOCATIONCHANGE,
            IntPtr.Zero,
            _winEventProc,
            pid,
            tid,
            WINEVENT_OUTOFCONTEXT);
    }

    private void UninstallWinEventHook()
    {
        if (_trayMoveHook != IntPtr.Zero)
        {
            try { UnhookWinEvent(_trayMoveHook); } catch { }
            _trayMoveHook = IntPtr.Zero;
        }
    }

    private void OnTrayWinEvent(IntPtr hWinEventHook, uint eventType, IntPtr hwnd, int idObject, int idChild, uint dwEventThread, uint dwmsEventTime)
    {
        if (_disposed || !_settings.CenterTaskbarIcons || !_settings.EnableTaskbarGlassDock) return;
        if (!_dockWindows.ContainsKey(hwnd)) return;

        if (!_syncPending)
        {
            _syncPending = true;
            Application.Current?.Dispatcher.BeginInvoke(DispatcherPriority.Render, () =>
            {
                _syncPending = false;
                SyncDockPositionsFast();
            });
        }
    }

    private void SyncDockPositionsFast()
    {
        if (_disposed || !_settings.CenterTaskbarIcons || !_settings.EnableTaskbarGlassDock) return;

        foreach (var kv in _dockWindows)
        {
            IntPtr trayHwnd = kv.Key;
            var dockWin = kv.Value;
            if (!_dockCaches.TryGetValue(trayHwnd, out var cache)) continue;
            if (!GetWindowRect(trayHwnd, out var trayRect)) continue;

            // Skip work when stationary
            if (trayRect.Top == cache.LastTrayTopPx && !_settings.AutoHideTaskbar)
                continue;
            cache.LastTrayTopPx = trayRect.Top;

            double dpi = cache.DpiScale;
            double trayH = Math.Max(28, trayRect.Height);
            double monBottom = cache.TargetMonitor != null ? cache.TargetMonitor.Bounds.Bottom : trayRect.Bottom;
            double visibleFrac = Math.Clamp((monBottom - trayRect.Top) / trayH, 0.0, 1.0);
            double opacity = visibleFrac <= 0.08 ? 0.0 : Math.Clamp((visibleFrac - 0.08) / 0.72, 0.0, 1.0);

            int physDockTop = trayRect.Top + (trayRect.Height - cache.CachedHeightPx) / 2;
            dockWin.UpdatePositionPhysicalFast(cache.CachedLeftPx, physDockTop, dpi, opacity, trayHwnd);
        }
    }

    public void ApplyTick()
    {
        if (_disposed) return;

        try
        {
            var trays = DiscoverTaskbars();
            if (trays.Count == 0) return;

            bool hasSecondary = trays.Any(t => !t.IsPrimary);

            // 1. Display Target Visibility (4d)
            foreach (var tray in trays)
            {
                bool shouldBeVisible = _settings.TaskbarDisplayTarget switch
                {
                    TaskbarDisplayMode.PrimaryOnly => tray.IsPrimary,
                    TaskbarDisplayMode.SecondaryOnly => !tray.IsPrimary || !hasSecondary,
                    _ => true
                };

                bool currentlyVisible = IsWindowVisible(tray.Hwnd);
                if (shouldBeVisible && !currentlyVisible)
                    ShowWindow(tray.Hwnd, SW_SHOW);
                else if (!shouldBeVisible && currentlyVisible)
                    ShowWindow(tray.Hwnd, SW_HIDE);
            }

            // 2. Center Taskbar Icons + Resizable Glass Dock Backdrop + Glass / Invisible Taskbar
            var activeDockTrays = new HashSet<IntPtr>();
            var monitors = _displayDetector.DetectMonitors();

            foreach (var tray in trays)
            {
                if (!IsWindowVisible(tray.Hwnd)) continue;

                bool showDockHere = _settings.CenterTaskbarIcons && _settings.EnableTaskbarGlassDock;

                if (_settings.MakeTaskbarInvisible || showDockHere)
                {
                    SetTrayAccentPolicy(tray.Hwnd, AccentState.ACCENT_ENABLE_TRANSPARENTGRADIENT, 0x01000000);
                }
                else if (_settings.EnableTaskbarGlass)
                {
                    uint abgr = ComputeTaskbarGlassAbgr(_glassStyle, _settings.TaskbarAcrylicOpacity);
                    SetTrayAccentPolicy(tray.Hwnd, AccentState.ACCENT_ENABLE_ACRYLICBLURBEHIND, abgr);
                }
                else
                {
                    SetTrayAccentPolicy(tray.Hwnd, AccentState.ACCENT_DISABLED, 0);
                }

                if (_settings.CenterTaskbarIcons && GetWindowRect(tray.Hwnd, out var trayRect))
                {
                    var (taskSwHwnd, taskListHwnd) = FindTaskListWindows(tray.Hwnd, tray.IsPrimary);
                    if (taskSwHwnd != IntPtr.Zero && taskListHwnd != IntPtr.Zero &&
                        GetWindowRect(taskSwHwnd, out var swRect))
                    {
                        double dpi = Math.Max(96.0, GetDpiForWindow(tray.Hwnd)) / 96.0;
                        var (bounds, querySucceeded) = MeasureTaskButtonClusterBounds(taskListHwnd, swRect, trayRect);
                        int clusterWidthPx = bounds.WidthPx;

                        if (bounds.IsValid && swRect.Width > clusterWidthPx)
                        {
                            int monCenterX = trayRect.Left + trayRect.Width / 2;
                            int desiredClusterLeft = monCenterX - clusterWidthPx / 2;

                            bool hasListRect = GetWindowRect(taskListHwnd, out var listRect);
                            int prevListLeft = hasListRect ? listRect.Left : bounds.LeftPx;
                            int listLeftInsetPx = bounds.LeftPx - prevListLeft;

                            int desiredListScreenLeft = desiredClusterLeft - listLeftInsetPx;
                            int minListLeft = swRect.Left + (int)(4 * dpi);
                            int maxListLeft = Math.Max(minListLeft, swRect.Right - (clusterWidthPx + listLeftInsetPx) - (int)(8 * dpi));
                            desiredListScreenLeft = Math.Clamp(desiredListScreenLeft, minListLeft, maxListLeft);

                            int targetRelX = Math.Max(0, desiredListScreenLeft - swRect.Left);
                            int actualShiftPx = 0;
                            if (hasListRect)
                            {
                                int curRelX = listRect.Left - swRect.Left;
                                if (Math.Abs(curRelX - targetRelX) > 1)
                                {
                                    SetWindowPos(
                                        taskListHwnd,
                                        IntPtr.Zero,
                                        targetRelX,
                                        0,
                                        Math.Max(clusterWidthPx + Math.Max(0, listLeftInsetPx) + 48, swRect.Width - targetRelX),
                                        swRect.Height,
                                        SWP_NOZORDER | SWP_NOACTIVATE);

                                    if (GetWindowRect(taskListHwnd, out var afterListRect))
                                        actualShiftPx = afterListRect.Left - prevListLeft;
                                    else
                                        actualShiftPx = (swRect.Left + targetRelX) - prevListLeft;
                                }
                            }

                            int actualClusterLeftPx = bounds.LeftPx + actualShiftPx;

                            if (showDockHere)
                            {
                                activeDockTrays.Add(tray.Hwnd);

                                double extraHalfPadDip = Math.Clamp(_settings.TaskbarDockExtraWidthPx / 2.0, -8.0, 140.0);
                                int extraHalfPadPx = (int)Math.Round(extraHalfPadDip * dpi);
                                int physDockLeft = actualClusterLeftPx - extraHalfPadPx;
                                int physDockW = Math.Max(36, clusterWidthPx + extraHalfPadPx * 2);
                                double dockDipH = Math.Clamp(_settings.TaskbarDockHeightPx, 26.0, 76.0);
                                int physDockH = (int)Math.Round(dockDipH * dpi);
                                int physDockTop = trayRect.Top + (trayRect.Height - physDockH) / 2;

                                double dockDipLeft = physDockLeft / dpi;
                                double dockDipW = physDockW / dpi;
                                double dockDipTop = physDockTop / dpi;

                                var targetMon = monitors.FirstOrDefault(m =>
                                    trayRect.Left >= m.Bounds.X - 40 && trayRect.Left < m.Bounds.Right + 40);

                                double monBottom = targetMon != null ? targetMon.Bounds.Bottom : trayRect.Bottom;
                                double visibleFrac = Math.Clamp((monBottom - trayRect.Top) / Math.Max(28.0, trayRect.Height), 0.0, 1.0);
                                double dockOpacity = visibleFrac <= 0.08 ? 0.0 : Math.Clamp((visibleFrac - 0.08) / 0.72, 0.0, 1.0);

                                if (!_dockWindows.TryGetValue(tray.Hwnd, out var dockWin))
                                {
                                    dockWin = new TaskbarDockOverlayWindow();
                                    _dockWindows[tray.Hwnd] = dockWin;
                                }

                                if (!_dockCaches.TryGetValue(tray.Hwnd, out var cache))
                                {
                                    cache = new DockLayoutCache();
                                    _dockCaches[tray.Hwnd] = cache;
                                }

                                string styleKey = $"{_glassStyle.Id}|{_glassStyle.SurfaceTheme}|{_glassStyle.BackgroundTintHex}|{_glassStyle.BackgroundOpacity:0.00}|{_glassStyle.BackgroundBlurIntensity:0.0}|{_glassStyle.CornerRadius:0.0}|{_glassStyle.BorderThickness:0.0}|{_glassStyle.BorderColorHex}|{physDockW}|{physDockH}|{_settings.LastAppliedImagePath}";
                                if (!string.Equals(cache.CachedStyleKey, styleKey, StringComparison.Ordinal))
                                {
                                    double monDpi = targetMon != null ? Math.Max(96.0, targetMon.DpiX) / 96.0 : dpi;
                                    double relMonX = targetMon != null ? (physDockLeft - targetMon.Bounds.X) / monDpi : dockDipLeft;
                                    double relMonY = targetMon != null ? (physDockTop - targetMon.Bounds.Y) / monDpi : dockDipTop;

                                    cache.CachedSlice = _glassStyle.SurfaceTheme == WidgetSurfaceTheme.Glassmorphism && _glassStyle.EnableBackdropBlur
                                        ? _wallpaperSampler.CreateBlurredCropSlice(
                                            targetMon,
                                            _settings.LastAppliedImagePath,
                                            relMonX,
                                            relMonY,
                                            physDockW / monDpi,
                                            physDockH / monDpi,
                                            _glassStyle.BackgroundBlurIntensity,
                                            addFrostGrain: true)
                                        : null;
                                    cache.CachedStyleKey = styleKey;
                                }

                                cache.CachedLeftPx = physDockLeft;
                                cache.CachedHeightPx = physDockH;
                                cache.LastTrayTopPx = trayRect.Top;
                                cache.TargetMonitor = targetMon;
                                cache.DpiScale = dpi;

                                dockWin.UpdateDockPhysicalRectAndStyle(
                                    physDockLeft,
                                    physDockTop,
                                    physDockW,
                                    physDockH,
                                    dpi,
                                    dockOpacity,
                                    _glassStyle,
                                    cache.CachedSlice,
                                    styleKey,
                                    tray.Hwnd);
                            }
                        }
                    }
                }
                else
                {
                    ResetTaskListAlignment(tray.Hwnd, tray.IsPrimary);
                }
            }

            foreach (var staleHwnd in _dockWindows.Keys.Where(h => !activeDockTrays.Contains(h)).ToList())
            {
                _dockWindows[staleHwnd].Close();
                _dockWindows.Remove(staleHwnd);
                _dockCaches.Remove(staleHwnd);
            }
        }
        catch { }
    }

    public void RestoreVisualTaskbarDefaults(bool restoreAutoHide = true)
    {
        _reapplyTimer.Stop();
        _smoothTrackTimer.Stop();
        UninstallWinEventHook();

        foreach (var dockWin in _dockWindows.Values.ToList())
        {
            try { dockWin.Close(); } catch { }
        }
        _dockWindows.Clear();
        _dockCaches.Clear();

        foreach (var tray in DiscoverTaskbars())
        {
            try
            {
                ShowWindow(tray.Hwnd, SW_SHOW);
                SetTrayAccentPolicy(tray.Hwnd, AccentState.ACCENT_DISABLED, 0);
                ResetTaskListAlignment(tray.Hwnd, tray.IsPrimary);
            }
            catch { }
        }

        if (restoreAutoHide && _isAutoHideManaged)
        {
            SetTaskbarAutoHide(false);
            _isAutoHideManaged = false;
        }
    }

    public static bool GetTaskbarAutoHideState()
    {
        try
        {
            var abd = new APPBARDATA { cbSize = (uint)Marshal.SizeOf<APPBARDATA>() };
            uint state = SHAppBarMessage(ABM_GETSTATE, ref abd);
            return (state & ABS_AUTOHIDE) != 0;
        }
        catch
        {
            return false;
        }
    }

    public void SetTaskbarAutoHide(bool autoHide)
    {
        try
        {
            var abd = new APPBARDATA
            {
                cbSize = (uint)Marshal.SizeOf<APPBARDATA>(),
                hWnd = FindWindow("Shell_TrayWnd", null),
                lParam = (IntPtr)(autoHide ? (ABS_AUTOHIDE | ABS_ALWAYSONTOP) : ABS_ALWAYSONTOP)
            };
            SHAppBarMessage(ABM_SETSTATE, ref abd);
            _isAutoHideManaged = autoHide;
        }
        catch { }
    }

    private static List<(IntPtr Hwnd, bool IsPrimary)> DiscoverTaskbars()
    {
        var list = new List<(IntPtr, bool)>();
        IntPtr primary = FindWindow("Shell_TrayWnd", null);
        if (primary != IntPtr.Zero)
            list.Add((primary, true));

        EnumWindows((hwnd, _) =>
        {
            var sb = new StringBuilder(128);
            if (GetClassName(hwnd, sb, sb.Capacity) > 0 &&
                string.Equals(sb.ToString(), "Shell_SecondaryTrayWnd", StringComparison.Ordinal))
            {
                list.Add((hwnd, false));
            }
            return true;
        }, IntPtr.Zero);

        return list;
    }

    private static (IntPtr TaskSwHwnd, IntPtr TaskListHwnd) FindTaskListWindows(IntPtr trayHwnd, bool isPrimary)
    {
        IntPtr foundList = IntPtr.Zero;
        EnumChildWindows(trayHwnd, (child, _) =>
        {
            var sb = new StringBuilder(64);
            if (GetClassName(child, sb, sb.Capacity) > 0 &&
                string.Equals(sb.ToString(), "MSTaskListWClass", StringComparison.Ordinal))
            {
                foundList = child;
                return false;
            }
            return true;
        }, IntPtr.Zero);

        if (foundList != IntPtr.Zero)
        {
            IntPtr parent = GetParent(foundList);
            if (parent != IntPtr.Zero && parent != trayHwnd)
                return (parent, foundList);
        }

        if (isPrimary)
        {
            IntPtr rebar = FindWindowEx(trayHwnd, IntPtr.Zero, "ReBarWindow32", null);
            if (rebar == IntPtr.Zero) return (IntPtr.Zero, IntPtr.Zero);
            IntPtr taskSw = FindWindowEx(rebar, IntPtr.Zero, "MSTaskSwWClass", null);
            if (taskSw == IntPtr.Zero) return (IntPtr.Zero, IntPtr.Zero);
            IntPtr taskList = FindWindowEx(taskSw, IntPtr.Zero, "MSTaskListWClass", null);
            return (taskSw, taskList);
        }
        else
        {
            IntPtr worker = FindWindowEx(trayHwnd, IntPtr.Zero, "WorkerW", null);
            if (worker == IntPtr.Zero) return (IntPtr.Zero, IntPtr.Zero);
            IntPtr taskList = FindWindowEx(worker, IntPtr.Zero, "MSTaskListWClass", null);
            return (worker, taskList);
        }
    }

    private static (TaskbarClusterBounds Bounds, bool QuerySucceeded) MeasureTaskButtonClusterBounds(IntPtr taskListHwnd, RECT swRect, RECT trayRect)
    {
        try
        {
            var root = AutomationElement.FromHandle(taskListHwnd);
            if (root != null)
            {
                // On Windows 10/11, MSTaskListWClass often wraps buttons inside a ToolBar child ("Running applications"),
                // so querying Descendants finds all taskbar icon buttons whether direct children or nested inside the ToolBar.
                var descendants = root.FindAll(TreeScope.Descendants, System.Windows.Automation.Condition.TrueCondition);
                var candidates = new List<TaskbarButtonCandidate>(Math.Min(96, descendants.Count));
                int scanned = 0;
                foreach (AutomationElement el in descendants)
                {
                    if (++scanned > 128) break;
                    try
                    {
                        var cur = el.Current;
                        var ct = cur.ControlType;
                        bool isTaskButtonType = ct == ControlType.Button
                            || ct == ControlType.SplitButton
                            || ct == ControlType.MenuItem
                            || ct == ControlType.CheckBox;

                        var r = cur.BoundingRectangle;
                        if (r.IsEmpty) continue;

                        candidates.Add(new TaskbarButtonCandidate(
                            r.Left,
                            r.Right,
                            r.Top,
                            r.Bottom,
                            isTaskButtonType,
                            cur.IsOffscreen,
                            cur.Name ?? string.Empty));
                    }
                    catch { }
                }

                var bounds = TaskbarClusterLayoutHelper.ComputeContiguousButtonClusterBounds(
                    candidates,
                    swRect.Left,
                    swRect.Right,
                    trayRect.Height * 0.45);

                if (bounds.IsValid)
                    return (bounds, true);
            }
        }
        catch { }

        var msaaBounds = MeasureTaskButtonBoundsViaMsaa(taskListHwnd, swRect, trayRect);
        if (msaaBounds.IsValid)
            return (msaaBounds, true);

        return (default, false);
    }

    private static TaskbarClusterBounds MeasureTaskButtonBoundsViaMsaa(IntPtr taskListHwnd, RECT swRect, RECT trayRect)
    {
        try
        {
            Guid iidAcc = new("618736E0-3C3D-11CF-810C-00AA00389B71");
            if (AccessibleObjectFromWindow(taskListHwnd, OBJID_CLIENT, ref iidAcc, out var acc) != 0 || acc == null)
                return default;

            var candidates = new List<TaskbarButtonCandidate>();
            CollectMsaaButtonCandidates(acc, candidates, depth: 0);
            if (candidates.Count == 0)
                return default;

            return TaskbarClusterLayoutHelper.ComputeContiguousButtonClusterBounds(
                candidates,
                swRect.Left,
                swRect.Right,
                trayRect.Height * 0.45);
        }
        catch
        {
            return default;
        }
    }

    private static void CollectMsaaButtonCandidates(IAccessible container, List<TaskbarButtonCandidate> candidates, int depth)
    {
        if (depth > 2) return;
        int count;
        try { count = container.get_accChildCount(); }
        catch { return; }
        if (count <= 0 || count > 128) return;

        var children = new object[count];
        if (AccessibleChildren(container, 0, count, children, out int obtained) != 0)
            return;

        for (int i = 0; i < obtained; i++)
        {
            object? child = children[i];
            if (child == null) continue;

            if (child is IAccessible childAcc)
            {
                CollectMsaaButtonCandidates(childAcc, candidates, depth + 1);
                continue;
            }

            try
            {
                int role = Convert.ToInt32(container.get_accRole(child) ?? 0);
                int state = Convert.ToInt32(container.get_accState(child) ?? 0);
                string name = container.get_accName(child) ?? string.Empty;
                container.accLocation(out int left, out int top, out int width, out int height, child);

                bool isButtonRole = role == ROLE_SYSTEM_PUSHBUTTON
                    || role == ROLE_SYSTEM_BUTTONDROPDOWN
                    || role == ROLE_SYSTEM_SPLITBUTTON
                    || role == ROLE_SYSTEM_MENUITEM;
                bool offscreen = (state & (STATE_SYSTEM_INVISIBLE | STATE_SYSTEM_OFFSCREEN)) != 0;

                if (width > 0 && height > 0)
                {
                    candidates.Add(new TaskbarButtonCandidate(
                        left,
                        left + width,
                        top,
                        top + height,
                        isButtonRole,
                        offscreen,
                        name));
                }
            }
            catch { }
        }
    }

    private static void ResetTaskListAlignment(IntPtr trayHwnd, bool isPrimary)
    {
        var (sw, list) = FindTaskListWindows(trayHwnd, isPrimary);
        if (sw != IntPtr.Zero && list != IntPtr.Zero && GetWindowRect(sw, out var swRect) && GetWindowRect(list, out var listRect))
        {
            if (Math.Abs(listRect.Left - swRect.Left) > 2)
                SetWindowPos(list, IntPtr.Zero, 0, 0, swRect.Width, swRect.Height, SWP_NOZORDER | SWP_NOACTIVATE);
        }
    }

    private static void SetTrayAccentPolicy(IntPtr hwnd, AccentState state, uint gradientColorAbgr)
    {
        var policy = new AccentPolicy
        {
            AccentState = state,
            AccentFlags = state == AccentState.ACCENT_DISABLED ? 0 : 2,
            GradientColor = gradientColorAbgr,
            AnimationId = 0
        };
        int size = Marshal.SizeOf(policy);
        IntPtr ptr = Marshal.AllocHGlobal(size);
        try
        {
            Marshal.StructureToPtr(policy, ptr, false);
            var data = new WindowCompositionAttributeData
            {
                Attribute = WindowCompositionAttribute.WCA_ACCENT_POLICY,
                SizeOfData = size,
                Data = ptr
            };
            SetWindowCompositionAttribute(hwnd, ref data);
        }
        finally
        {
            Marshal.FreeHGlobal(ptr);
        }
    }

    private static uint ComputeTaskbarGlassAbgr(GlassmorphismStyle style, double opacity)
    {
        Color tint = GlassSurfaceControl.ParseHex(style.BackgroundTintHex, Color.FromRgb(18, 24, 38), 1.0);
        byte alpha = (byte)Math.Clamp((int)Math.Round(opacity * 255.0), 16, 240);
        return ((uint)alpha << 24) | ((uint)tint.B << 16) | ((uint)tint.G << 8) | tint.R;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        RestoreVisualTaskbarDefaults(restoreAutoHide: true);
    }
}
