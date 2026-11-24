using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Input;
using System.Windows.Interop;

namespace DuoWall.App.Services;

/// <summary>
/// Manages registration and handling of global Windows hotkeys via RegisterHotKey Win32 API.
/// </summary>
public sealed class GlobalHotkeyManager : IDisposable
{
    private const int WM_HOTKEY = 0x0312;
    private const int HOTKEY_ID = 0xD011;

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    public const uint MOD_ALT = 0x0001;
    public const uint MOD_CONTROL = 0x0002;
    public const uint MOD_SHIFT = 0x0004;
    public const uint MOD_WIN = 0x0008;
    public const uint MOD_NOREPEAT = 0x4000;

    private IntPtr _hwnd = IntPtr.Zero;
    private HwndSource? _source;
    private bool _isRegistered;
    private Action? _currentAction;
    private string _registeredHotkey = string.Empty;

    public string CurrentHotkey => _registeredHotkey;
    public bool IsRegistered => _isRegistered;

    public void Initialize(IntPtr hwnd)
    {
        if (_hwnd != IntPtr.Zero && _source != null)
        {
            _source.RemoveHook(HwndHook);
        }

        _hwnd = hwnd;
        _source = HwndSource.FromHwnd(hwnd);
        _source?.AddHook(HwndHook);

        if (!string.IsNullOrWhiteSpace(_registeredHotkey) && _currentAction != null)
        {
            Register(_registeredHotkey, _currentAction);
        }
    }

    public bool Register(string hotkeyString, Action callback)
    {
        _currentAction = callback;
        _registeredHotkey = hotkeyString?.Trim() ?? string.Empty;

        if (_hwnd == IntPtr.Zero)
            return false;

        if (_isRegistered)
        {
            UnregisterHotKey(_hwnd, HOTKEY_ID);
            _isRegistered = false;
        }

        if (string.IsNullOrWhiteSpace(_registeredHotkey))
            return true;

        if (!TryParseHotkey(_registeredHotkey, out uint modifiers, out uint vk))
            return false;

        _isRegistered = RegisterHotKey(_hwnd, HOTKEY_ID, modifiers | MOD_NOREPEAT, vk);
        return _isRegistered;
    }

    public void Unregister()
    {
        if (_hwnd != IntPtr.Zero && _isRegistered)
        {
            UnregisterHotKey(_hwnd, HOTKEY_ID);
            _isRegistered = false;
        }
        _registeredHotkey = string.Empty;
    }

    public static bool TryParseHotkey(string hotkey, out uint modifiers, out uint vk)
    {
        modifiers = 0;
        vk = 0;

        if (string.IsNullOrWhiteSpace(hotkey))
            return false;

        string[] parts = hotkey.Split(new[] { '+', '-', ' ' }, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0) return false;

        for (int i = 0; i < parts.Length - 1; i++)
        {
            string mod = parts[i].Trim().ToLowerInvariant();
            if (mod is "ctrl" or "control") modifiers |= MOD_CONTROL;
            else if (mod is "alt") modifiers |= MOD_ALT;
            else if (mod is "shift") modifiers |= MOD_SHIFT;
            else if (mod is "win" or "windows") modifiers |= MOD_WIN;
        }

        string keyPart = parts[^1].Trim();
        if (parts.Length == 1 && (keyPart.Equals("ctrl", StringComparison.OrdinalIgnoreCase) ||
                                  keyPart.Equals("alt", StringComparison.OrdinalIgnoreCase) ||
                                  keyPart.Equals("shift", StringComparison.OrdinalIgnoreCase)))
        {
            return false;
        }

        if (Enum.TryParse<Key>(keyPart, true, out var key))
        {
            vk = (uint)KeyInterop.VirtualKeyFromKey(key);
            return vk != 0;
        }

        if (keyPart.Length == 1)
        {
            char c = char.ToUpperInvariant(keyPart[0]);
            if ((c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9'))
            {
                vk = c;
                return true;
            }
        }

        return false;
    }

    private IntPtr HwndHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_HOTKEY && wParam.ToInt32() == HOTKEY_ID)
        {
            _currentAction?.Invoke();
            handled = true;
        }
        return IntPtr.Zero;
    }

    public void Dispose()
    {
        Unregister();
        if (_source != null && _hwnd != IntPtr.Zero)
        {
            _source.RemoveHook(HwndHook);
            _source = null;
        }
    }
}

