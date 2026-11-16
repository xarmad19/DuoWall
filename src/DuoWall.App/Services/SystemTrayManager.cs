using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using DuoWall.App.ViewModels;
using Wpf.Ui.Controls;

namespace DuoWall.App.Services;

public sealed class SystemTrayManager : IDisposable
{
    public const int WM_APP = 0x8000;
    public const int WM_TRAY_CALLBACK = WM_APP + 101;

    private const int NIM_ADD = 0x00000000;
    private const int NIM_MODIFY = 0x00000001;
    private const int NIM_DELETE = 0x00000002;
    private const int NIM_SETVERSION = 0x00000004;

    private const int NIF_MESSAGE = 0x00000001;
    private const int NIF_ICON = 0x00000002;
    private const int NIF_TIP = 0x00000004;
    private const int NIF_STATE = 0x00000008;
    private const int NIF_INFO = 0x00000010;

    private const int WM_LBUTTONUP = 0x0202;
    private const int WM_LBUTTONDBLCLK = 0x0203;
    private const int WM_RBUTTONUP = 0x0205;
    private const int WM_CONTEXTMENU = 0x007B;

    private const uint IMAGE_ICON = 1;
    private const uint LR_LOADFROMFILE = 0x00000010;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NOTIFYICONDATA
    {
        public int cbSize;
        public IntPtr hWnd;
        public int uID;
        public int uFlags;
        public int uCallbackMessage;
        public IntPtr hIcon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string szTip;
        public int dwState;
        public int dwStateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
        public string szInfo;
        public int uTimeoutOrVersion;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
        public string szInfoTitle;
        public int dwInfoFlags;
        public Guid guidItem;
        public IntPtr hBalloonIcon;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern bool Shell_NotifyIconW(int dwMessage, ref NOTIFYICONDATA lpData);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern uint ExtractIconEx(string szFileName, int nIconIndex, out IntPtr phiconLarge, out IntPtr phiconSmall, uint nIcons);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr LoadImage(IntPtr hInst, string name, uint type, int cx, int cy, uint fuLoad);

    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr hIcon);

    private MainWindow? _mainWindow;
    private MainViewModel? _viewModel;
    private IntPtr _hwnd = IntPtr.Zero;
    private IntPtr _hIcon = IntPtr.Zero;
    private bool _isAdded;
    private System.Windows.Controls.ContextMenu? _activeContextMenu;

    public void Initialize(MainWindow mainWindow, MainViewModel viewModel)
    {
        _mainWindow = mainWindow;
        _viewModel = viewModel;

        var helper = new WindowInteropHelper(mainWindow);
        _hwnd = helper.EnsureHandle();

        var source = HwndSource.FromHwnd(_hwnd);
        source?.AddHook(WndProc);

        CreateOrUpdateIcon();
    }

    private void CreateOrUpdateIcon()
    {
        if (_hwnd == IntPtr.Zero) return;

        // Obtain icon: prefer ExtractIconEx on main process exe, fallback to Assets/duowall.ico
        if (_hIcon == IntPtr.Zero)
        {
            try
            {
                string? exePath = Process.GetCurrentProcess().MainModule?.FileName;
                if (!string.IsNullOrWhiteSpace(exePath) && File.Exists(exePath))
                {
                    ExtractIconEx(exePath, 0, out _, out _hIcon, 1);
                }
            }
            catch { }

            if (_hIcon == IntPtr.Zero)
            {
                string baseDir = AppDomain.CurrentDomain.BaseDirectory;
                string iconFile = Path.Combine(baseDir, "Assets", "duowall.ico");
                if (File.Exists(iconFile))
                {
                    _hIcon = LoadImage(IntPtr.Zero, iconFile, IMAGE_ICON, 16, 16, LR_LOADFROMFILE);
                }
            }
        }

        var nid = new NOTIFYICONDATA
        {
            cbSize = Marshal.SizeOf<NOTIFYICONDATA>(),
            hWnd = _hwnd,
            uID = 1001,
            uFlags = NIF_MESSAGE | NIF_ICON | NIF_TIP,
            uCallbackMessage = WM_TRAY_CALLBACK,
            hIcon = _hIcon,
            szTip = "DuoWall by SRMD"
        };

        if (!_isAdded)
        {
            _isAdded = Shell_NotifyIconW(NIM_ADD, ref nid);
        }
        else
        {
            Shell_NotifyIconW(NIM_MODIFY, ref nid);
        }
    }

    public void ShowBalloonNotification(string title, string message)
    {
        if (!_isAdded || _hwnd == IntPtr.Zero) return;

        var nid = new NOTIFYICONDATA
        {
            cbSize = Marshal.SizeOf<NOTIFYICONDATA>(),
            hWnd = _hwnd,
            uID = 1001,
            uFlags = NIF_INFO,
            szInfoTitle = title ?? "DuoWall by SRMD",
            szInfo = message ?? string.Empty,
            dwInfoFlags = 0x00000001 // NIIF_INFO
        };

        Shell_NotifyIconW(NIM_MODIFY, ref nid);
    }

    public void RemoveIcon()
    {
        if (_isAdded && _hwnd != IntPtr.Zero)
        {
            var nid = new NOTIFYICONDATA
            {
                cbSize = Marshal.SizeOf<NOTIFYICONDATA>(),
                hWnd = _hwnd,
                uID = 1001
            };
            Shell_NotifyIconW(NIM_DELETE, ref nid);
            _isAdded = false;
        }

        if (_hIcon != IntPtr.Zero)
        {
            DestroyIcon(_hIcon);
            _hIcon = IntPtr.Zero;
        }
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_TRAY_CALLBACK)
        {
            int action = (int)lParam;
            if (action is WM_LBUTTONUP or WM_LBUTTONDBLCLK)
            {
                RestoreMainWindow();
                handled = true;
            }
            else if (action is WM_RBUTTONUP or WM_CONTEXTMENU)
            {
                ShowContextMenu();
                handled = true;
            }
        }

        return IntPtr.Zero;
    }

    public void RestoreMainWindow(int? navigateToPage = null)
    {
        if (_mainWindow == null) return;

        if (navigateToPage.HasValue && _viewModel != null)
        {
            _viewModel.CurrentPageIndex = navigateToPage.Value;
        }

        if (!_mainWindow.IsVisible)
        {
            _mainWindow.Show();
        }

        if (_mainWindow.WindowState == WindowState.Minimized)
        {
            _mainWindow.WindowState = WindowState.Normal;
        }

        _mainWindow.Activate();
        _mainWindow.Focus();

        if (_hwnd != IntPtr.Zero)
        {
            SetForegroundWindow(_hwnd);
        }
    }

    private void ShowContextMenu()
    {
        if (_mainWindow == null || _viewModel == null) return;

        if (_hwnd != IntPtr.Zero)
        {
            SetForegroundWindow(_hwnd);
        }

        var menu = new System.Windows.Controls.ContextMenu
        {
            Placement = System.Windows.Controls.Primitives.PlacementMode.MousePoint
        };

        // 1. Open DuoWall
        var itemOpen = new System.Windows.Controls.MenuItem
        {
            Header = "Open DuoWall",
            FontWeight = FontWeights.SemiBold
        };
        itemOpen.Click += (_, _) => RestoreMainWindow(0);
        menu.Items.Add(itemOpen);

        // 2. Placement Studio
        var itemStudio = new System.Windows.Controls.MenuItem
        {
            Header = "Placement Studio"
        };
        itemStudio.Click += (_, _) => RestoreMainWindow(2);
        menu.Items.Add(itemStudio);

        // 3. Widget Studio & Marketplace
        var itemWidgets = new System.Windows.Controls.MenuItem
        {
            Header = "Widget Marketplace"
        };
        itemWidgets.Click += (_, _) => RestoreMainWindow(6);
        menu.Items.Add(itemWidgets);

        // 4. Taskbar Tweaks
        var itemTaskbar = new System.Windows.Controls.MenuItem
        {
            Header = "Taskbar Tweaks"
        };
        itemTaskbar.Click += (_, _) => RestoreMainWindow(9);
        menu.Items.Add(itemTaskbar);

        menu.Items.Add(new Separator());

        // 5. Check for Updates
        var itemUpdates = new System.Windows.Controls.MenuItem
        {
            Header = "Check for Updates..."
        };
        itemUpdates.Click += async (_, _) =>
        {
            RestoreMainWindow();
            await _viewModel.CheckForUpdatesAsync();
        };
        menu.Items.Add(itemUpdates);

        // 6. About Me
        var itemAbout = new System.Windows.Controls.MenuItem
        {
            Header = "About Me..."
        };
        itemAbout.Click += (_, _) =>
        {
            RestoreMainWindow();
            _viewModel.ShowAboutMe();
        };
        menu.Items.Add(itemAbout);

        menu.Items.Add(new Separator());

        // 7. Exit DuoWall
        var itemExit = new System.Windows.Controls.MenuItem
        {
            Header = "Exit DuoWall"
        };
        itemExit.Click += (_, _) =>
        {
            RemoveIcon();
            _mainWindow.ExitApplication();
        };
        menu.Items.Add(itemExit);

        _activeContextMenu = menu;
        menu.IsOpen = true;
    }

    public void Dispose()
    {
        RemoveIcon();
    }
}
