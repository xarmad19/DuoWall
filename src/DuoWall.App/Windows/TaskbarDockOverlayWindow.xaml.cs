using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using DuoWall.Core.Theming;

namespace DuoWall.App.Windows;

/// <summary>
/// Floating Glassmorphism dock pill rendered seamlessly behind transparent centered Windows taskbar icons.
/// Uses fast Win32 position sync during taskbar auto-hide slides so the dock glides lock-step with icons.
/// </summary>
public partial class TaskbarDockOverlayWindow : Window
{
    public const double MarginPad = 12.0;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowPos(
        IntPtr hWnd,
        IntPtr hWndInsertAfter,
        int X,
        int Y,
        int cx,
        int cy,
        uint uFlags);

    private const uint SWP_NOMOVE = 0x0002;
    private const uint SWP_NOSIZE = 0x0001;
    private const uint SWP_NOACTIVATE = 0x0010;

    private IntPtr _hwnd = IntPtr.Zero;
    private string _lastVisualKey = string.Empty;
    private bool _isFullBar = false;

    public TaskbarDockOverlayWindow()
    {
        InitializeComponent();
        StateChanged += (_, _) =>
        {
            if (WindowState == WindowState.Minimized)
                WindowState = WindowState.Normal;
        };
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        _hwnd = new WindowInteropHelper(this).Handle;
        GlassBackdropHelper.ConfigureDesktopOverlayWindow(_hwnd, excludeFromAeroPeek: false);
        GlassBackdropHelper.SetClickThrough(_hwnd, true);
        GlassBackdropHelper.AttachToDesktopShellOwner(_hwnd);
    }

    public void UpdateDockPhysicalRectAndStyle(
        int physLeft,
        int physTop,
        int physWidth,
        int physHeight,
        double fallbackDpi,
        double opacity,
        GlassmorphismStyle style,
        ImageSource? wallpaperSlice,
        string visualStyleKey,
        IntPtr placeBehindTrayHwnd,
        bool isFullBar = false)
    {
        _isFullBar = isFullBar;
        double currentMarginPad = isFullBar ? 0.0 : MarginPad;
        if (DockRootGrid.Margin.Left != currentMarginPad)
        {
            DockRootGrid.Margin = new Thickness(currentMarginPad);
        }

        double initDpi = Math.Max(1.0, fallbackDpi);
        if (!IsVisible)
        {
            Left = physLeft / initDpi - currentMarginPad;
            Top = physTop / initDpi - currentMarginPad;
            Width = Math.Max(36, physWidth / initDpi) + currentMarginPad * 2;
            Height = Math.Max(20, physHeight / initDpi) + currentMarginPad * 2;
            Opacity = Math.Clamp(opacity, 0.0, 1.0);
            Show();
        }

        double winDpiX = initDpi;
        double winDpiY = initDpi;
        if (_hwnd != IntPtr.Zero)
        {
            var wpfDpi = VisualTreeHelper.GetDpi(this);
            if (wpfDpi.DpiScaleX > 0.5) winDpiX = wpfDpi.DpiScaleX;
            if (wpfDpi.DpiScaleY > 0.5) winDpiY = wpfDpi.DpiScaleY;
        }

        double targetW = Math.Max(36, physWidth / winDpiX) + currentMarginPad * 2;
        double targetH = Math.Max(20, physHeight / winDpiY) + currentMarginPad * 2;
        double targetL = physLeft / winDpiX - currentMarginPad;
        double targetT = physTop / winDpiY - currentMarginPad;
        if (Math.Abs(Width - targetW) > 0.1) Width = targetW;
        if (Math.Abs(Height - targetH) > 0.1) Height = targetH;
        if (Math.Abs(Left - targetL) > 0.1) Left = targetL;
        if (Math.Abs(Top - targetT) > 0.1) Top = targetT;
        Opacity = Math.Clamp(opacity, 0.0, 1.0);

        if (!string.Equals(_lastVisualKey, visualStyleKey, StringComparison.Ordinal))
        {
            _lastVisualKey = visualStyleKey;
            DockGlassSurface.GlassStyle = style;
            DockGlassSurface.BrightnessMultiplier = 1.0;
            DockGlassSurface.IsHardwareBackdropActive = false;
            DockGlassSurface.FallbackBackdropSource = wallpaperSlice;
            DockGlassSurface.ApplyVisualStyle();
        }

        if (_hwnd != IntPtr.Zero)
        {
            int marginPxX = (int)Math.Round(currentMarginPad * winDpiX);
            int marginPxY = (int)Math.Round(currentMarginPad * winDpiY);
            int winW = Math.Max(40, physWidth + marginPxX * 2);
            int winH = Math.Max(20, physHeight + marginPxY * 2);
            IntPtr insertAfter = placeBehindTrayHwnd != IntPtr.Zero ? placeBehindTrayHwnd : IntPtr.Zero;
            uint flags = SWP_NOACTIVATE | (placeBehindTrayHwnd == IntPtr.Zero ? 0x0004u : 0u);
            SetWindowPos(_hwnd, insertAfter, physLeft - marginPxX, physTop - marginPxY, winW, winH, flags);
        }
    }

    /// <summary>
    /// Fast physical-pixel position &amp; opacity update used at 60Hz during taskbar auto-hide slide animations
    /// without re-rendering Skia blur slices or WPF styles.
    /// </summary>
    public void UpdatePositionPhysicalFast(int physLeft, int physTop, double fallbackDpi, double opacity, IntPtr placeBehindTrayHwnd)
    {
        Opacity = Math.Clamp(opacity, 0.0, 1.0);
        if (_hwnd != IntPtr.Zero)
        {
            var wpfDpi = VisualTreeHelper.GetDpi(this);
            double winDpiX = wpfDpi.DpiScaleX > 0.5 ? wpfDpi.DpiScaleX : fallbackDpi;
            double winDpiY = wpfDpi.DpiScaleY > 0.5 ? wpfDpi.DpiScaleY : fallbackDpi;
            double currentMarginPad = _isFullBar ? 0.0 : MarginPad;
            int marginPxX = (int)Math.Round(currentMarginPad * winDpiX);
            int marginPxY = (int)Math.Round(currentMarginPad * winDpiY);
            IntPtr insertAfter = placeBehindTrayHwnd != IntPtr.Zero ? placeBehindTrayHwnd : IntPtr.Zero;
            uint flags = SWP_NOSIZE | SWP_NOACTIVATE | (placeBehindTrayHwnd == IntPtr.Zero ? 0x0004u : 0u);
            SetWindowPos(_hwnd, insertAfter, physLeft - marginPxX, physTop - marginPxY, 0, 0, flags);
        }
    }
}
