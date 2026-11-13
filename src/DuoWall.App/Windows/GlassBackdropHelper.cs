using System;
using System.Runtime.InteropServices;
using System.Windows.Media;

namespace DuoWall.App.Windows;

/// <summary>
/// P/Invoke helper for Windows DWM composition, hardware Acrylic/BlurBehind backdrop effects,
/// Windows 11 rounded corners, Z-order desktop pinning, and click-through extended styles.
/// </summary>
public static class GlassBackdropHelper
{
    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_TRANSPARENT = 0x00000020;
    private const int WS_EX_TOOLWINDOW = 0x00000080;

    private const int DWMWA_EXCLUDED_FROM_PEEK = 12;
    private const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;

    private static readonly IntPtr HWND_BOTTOM = new(1);
    private const uint SWP_NOSIZE = 0x0001;
    private const uint SWP_NOMOVE = 0x0002;
    private const uint SWP_NOACTIVATE = 0x0010;
    private const uint SWP_NOOWNERZORDER = 0x0200;

    public enum AccentState
    {
        ACCENT_DISABLED = 0,
        ACCENT_ENABLE_GRADIENT = 1,
        ACCENT_ENABLE_TRANSPARENTGRADIENT = 2,
        ACCENT_ENABLE_BLURBEHIND = 3,
        ACCENT_ENABLE_ACRYLICBLURBEHIND = 4,
        ACCENT_INVALID_STATE = 5
    }

    public enum DwmCornerPreference
    {
        Default = 0,
        DoNotRound = 1,
        Round = 2,
        RoundSmall = 3
    }

    private enum WindowCompositionAttribute
    {
        WCA_ACCENT_POLICY = 19
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct AccentPolicy
    {
        public AccentState AccentState;
        public uint AccentFlags;
        public uint GradientColor;
        public uint AnimationId;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WindowCompositionAttributeData
    {
        public WindowCompositionAttribute Attribute;
        public IntPtr Data;
        public int SizeOfData;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MARGINS
    {
        public int cxLeftWidth;
        public int cxRightWidth;
        public int cyTopHeight;
        public int cyBottomHeight;
    }

    [DllImport("user32.dll")]
    private static extern int SetWindowCompositionAttribute(IntPtr hwnd, ref WindowCompositionAttributeData data);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW", SetLastError = true)]
    private static extern IntPtr GetWindowLongPtr64(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongW", SetLastError = true)]
    private static extern int GetWindowLong32(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
    private static extern IntPtr SetWindowLongPtr64(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongW", SetLastError = true)]
    private static extern int SetWindowLong32(IntPtr hWnd, int nIndex, int dwNewLong);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

    [DllImport("dwmapi.dll")]
    private static extern int DwmIsCompositionEnabled([MarshalAs(UnmanagedType.Bool)] out bool pfEnabled);

    [DllImport("dwmapi.dll")]
    private static extern int DwmExtendFrameIntoClientArea(IntPtr hWnd, ref MARGINS pMarInset);

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int dwAttribute, ref int pvAttribute, int cbAttribute);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateRoundRectRgn(int nLeftRect, int nTopRect, int nRightRect, int nBottomRect, int nWidthEllipse, int nHeightEllipse);

    [DllImport("user32.dll")]
    private static extern int SetWindowRgn(IntPtr hWnd, IntPtr hRgn, [MarshalAs(UnmanagedType.Bool)] bool bRedraw);

    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeleteObject(IntPtr hObject);

    /// <summary>
    /// Returns true if Windows DWM desktop composition is active and capable of backdrop blur.
    /// </summary>
    public static bool IsDwmCompositionEnabled()
    {
        try
        {
            return DwmIsCompositionEnabled(out bool enabled) == 0 && enabled;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Applies Windows hardware backdrop blur (Acrylic on Win10 1803+/Win11, BlurBehind during drag or legacy).
    /// Returns true when the composition attribute call succeeded; otherwise callers should use Skia fallback.
    /// </summary>
    public static bool TryEnableHardwareBackdrop(
        IntPtr hwnd,
        string tintHex,
        double opacity,
        bool preferAcrylic,
        double cornerRadius)
    {
        if (hwnd == IntPtr.Zero || !IsDwmCompositionEnabled())
        {
            return false;
        }

        try
        {
            var margins = new MARGINS
            {
                cxLeftWidth = -1,
                cxRightWidth = -1,
                cyTopHeight = -1,
                cyBottomHeight = -1
            };
            _ = DwmExtendFrameIntoClientArea(hwnd, ref margins);

            var cornerPref = cornerRadius <= 2
                ? (int)DwmCornerPreference.DoNotRound
                : cornerRadius <= 10
                    ? (int)DwmCornerPreference.RoundSmall
                    : (int)DwmCornerPreference.Round;
            _ = DwmSetWindowAttribute(hwnd, DWMWA_WINDOW_CORNER_PREFERENCE, ref cornerPref, sizeof(int));

            // Keep gradient alpha at >= 1 when Acrylic is active to avoid black/opaque rendering bugs on certain Win10 builds.
            var abgr = ToAbgr(tintHex, opacity, minimumAlpha: preferAcrylic ? (byte)1 : (byte)0);

            var accent = new AccentPolicy
            {
                AccentState = preferAcrylic
                    ? AccentState.ACCENT_ENABLE_ACRYLICBLURBEHIND
                    : AccentState.ACCENT_ENABLE_BLURBEHIND,
                AccentFlags = 2,
                GradientColor = abgr,
                AnimationId = 0
            };

            int size = Marshal.SizeOf(accent);
            IntPtr ptr = Marshal.AllocHGlobal(size);
            try
            {
                Marshal.StructureToPtr(accent, ptr, false);
                var data = new WindowCompositionAttributeData
                {
                    Attribute = WindowCompositionAttribute.WCA_ACCENT_POLICY,
                    SizeOfData = size,
                    Data = ptr
                };
                int result = SetWindowCompositionAttribute(hwnd, ref data);
                if (result == 0 && preferAcrylic)
                {
                    // Fallback from Acrylic to standard BlurBehind on older Windows 10 builds
                    accent.AccentState = AccentState.ACCENT_ENABLE_BLURBEHIND;
                    Marshal.StructureToPtr(accent, ptr, false);
                    result = SetWindowCompositionAttribute(hwnd, ref data);
                }

                return result != 0;
            }
            finally
            {
                Marshal.FreeHGlobal(ptr);
            }
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Disables DWM backdrop blur on the specified window handle.
    /// </summary>
    public static void DisableHardwareBackdrop(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero)
        {
            return;
        }

        try
        {
            var accent = new AccentPolicy
            {
                AccentState = AccentState.ACCENT_DISABLED,
                AccentFlags = 0,
                GradientColor = 0,
                AnimationId = 0
            };
            int size = Marshal.SizeOf(accent);
            IntPtr ptr = Marshal.AllocHGlobal(size);
            try
            {
                Marshal.StructureToPtr(accent, ptr, false);
                var data = new WindowCompositionAttributeData
                {
                    Attribute = WindowCompositionAttribute.WCA_ACCENT_POLICY,
                    SizeOfData = size,
                    Data = ptr
                };
                _ = SetWindowCompositionAttribute(hwnd, ref data);
            }
            finally
            {
                Marshal.FreeHGlobal(ptr);
            }
        }
        catch
        {
            // Non-fatal during window teardown
        }
    }

    /// <summary>
    /// Configures the overlay window as a tool window (excluded from Alt+Tab) and optionally hides from Aero Peek.
    /// </summary>
    public static void ConfigureDesktopOverlayWindow(IntPtr hwnd, bool excludeFromAeroPeek = false)
    {
        if (hwnd == IntPtr.Zero)
        {
            return;
        }

        try
        {
            long exStyle = GetWindowLong(hwnd, GWL_EXSTYLE);
            exStyle |= WS_EX_TOOLWINDOW;
            SetWindowLong(hwnd, GWL_EXSTYLE, exStyle);

            int peekVal = excludeFromAeroPeek ? 1 : 0;
            _ = DwmSetWindowAttribute(hwnd, DWMWA_EXCLUDED_FROM_PEEK, ref peekVal, sizeof(int));
        }
        catch
        {
            // Ignore on unsupported Windows SKUs
        }
    }

    /// <summary>
    /// Enables or disables WS_EX_TRANSPARENT so desktop widgets can become click-through.
    /// </summary>
    public static void SetClickThrough(IntPtr hwnd, bool enabled)
    {
        if (hwnd == IntPtr.Zero)
        {
            return;
        }

        try
        {
            long exStyle = GetWindowLong(hwnd, GWL_EXSTYLE);
            if (enabled)
            {
                exStyle |= WS_EX_TRANSPARENT | WS_EX_TOOLWINDOW;
            }
            else
            {
                exStyle &= ~WS_EX_TRANSPARENT;
                exStyle |= WS_EX_TOOLWINDOW;
            }

            SetWindowLong(hwnd, GWL_EXSTYLE, exStyle);
        }
        catch
        {
            // Ignore
        }
    }

    /// <summary>
    /// Clips the native HWND to rounded bounds in physical pixels so Windows 10/11 Acrylic blur
    /// never leaks square corners outside rounded Glassmorphism surfaces.
    /// </summary>
    public static void SetRoundedWindowRegion(IntPtr hwnd, int widthPx, int heightPx, int cornerRadiusPx)
    {
        if (hwnd == IntPtr.Zero || widthPx <= 0 || heightPx <= 0)
        {
            return;
        }

        try
        {
            if (cornerRadiusPx <= 1)
            {
                _ = SetWindowRgn(hwnd, IntPtr.Zero, true);
                return;
            }

            int diameter = Math.Max(2, cornerRadiusPx * 2);
            IntPtr rgn = CreateRoundRectRgn(0, 0, widthPx + 1, heightPx + 1, diameter, diameter);
            if (rgn == IntPtr.Zero)
            {
                return;
            }

            if (SetWindowRgn(hwnd, rgn, true) == 0)
            {
                DeleteObject(rgn);
            }
        }
        catch
        {
            // Ignore region failure gracefully
        }
    }

    /// <summary>
    /// Pins the desktop widget or video backdrop window at the bottom of the Z-order behind active applications.
    /// </summary>
    public static void SendToBottomZOrder(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero)
        {
            return;
        }

        try
        {
            _ = SetWindowPos(hwnd, HWND_BOTTOM, 0, 0, 0, 0,
                SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE | SWP_NOOWNERZORDER);
        }
        catch
        {
            // Ignore
        }
    }

    private static uint ToAbgr(string hex, double opacity, byte minimumAlpha)
    {
        byte r = 24, g = 32, b = 48;
        try
        {
            if (!string.IsNullOrWhiteSpace(hex) &&
                ColorConverter.ConvertFromString(hex) is Color c)
            {
                r = c.R;
                g = c.G;
                b = c.B;
            }
        }
        catch
        {
            // Use fallback slate tint
        }

        byte a = (byte)Math.Clamp(Math.Round(opacity * 255.0), minimumAlpha, 255);
        return ((uint)a << 24) | ((uint)b << 16) | ((uint)g << 8) | r;
    }

    private static long GetWindowLong(IntPtr hWnd, int nIndex)
    {
        return IntPtr.Size == 8
            ? GetWindowLongPtr64(hWnd, nIndex).ToInt64()
            : GetWindowLong32(hWnd, nIndex);
    }

    private static void SetWindowLong(IntPtr hWnd, int nIndex, long dwNewLong)
    {
        if (IntPtr.Size == 8)
        {
            _ = SetWindowLongPtr64(hWnd, nIndex, new IntPtr(dwNewLong));
        }
        else
        {
            _ = SetWindowLong32(hWnd, nIndex, (int)dwNewLong);
        }
    }
}
