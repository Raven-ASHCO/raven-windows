using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Raven.Windows.Core.Presentation;

namespace Raven.Windows.Helpers;

/// <summary>
/// Minimal notification-area (tray) icon over Shell_NotifyIcon, with no extra package.
/// A hidden window (never shown; a normal top-level window rather than a message-only one so
/// it also gets the "TaskbarCreated" broadcast) receives the icon's callbacks on the UI
/// thread (it is created there, and the WinUI thread pumps Win32 messages). Left click or Enter opens
/// Raven; right click or the menu key shows the menu. The icon is added again when
/// Explorer restarts ("TaskbarCreated"). The icon can show one of several prepared frames
/// (<see cref="SetFrame"/>): the app uses them for the slow "breathing" while Raven listens
/// (<see cref="TrayBreath"/>).
/// </summary>
public sealed class TrayIcon : IDisposable
{
    private const uint WM_APP = 0x8000;
    private const uint CallbackMessage = WM_APP + 1;
    private const uint WM_NULL = 0x0000;
    private const uint WM_CONTEXTMENU = 0x007B;
    private const uint WM_LBUTTONDBLCLK = 0x0203;
    private const uint NIN_SELECT = 0x0400;
    private const uint NIN_KEYSELECT = 0x0401;

    private const uint NIM_ADD = 0x0;
    private const uint NIM_MODIFY = 0x1;
    private const uint NIM_DELETE = 0x2;
    private const uint NIM_SETVERSION = 0x4;
    private const uint NIF_MESSAGE = 0x1;
    private const uint NIF_ICON = 0x2;
    private const uint NIF_TIP = 0x4;
    private const uint NIF_SHOWTIP = 0x80;
    private const uint NOTIFYICON_VERSION_4 = 4;

    private const uint IMAGE_ICON = 1;
    private const uint LR_LOADFROMFILE = 0x10;
    private const int SM_CXSMICON = 49;
    private const int SM_CYSMICON = 50;

    private const uint MF_STRING = 0x0;
    private const uint MF_GRAYED = 0x1;
    private const uint MF_SEPARATOR = 0x800;
    private const uint TPM_RIGHTBUTTON = 0x2;
    private const uint TPM_RETURNCMD = 0x100;
    private const uint TPM_NONOTIFY = 0x80;

    private const uint WS_EX_TOOLWINDOW = 0x00000080;

    private readonly WndProc _wndProc; // kept alive while the window exists
    private readonly string _className;
    private readonly IntPtr _instance;
    private readonly uint _taskbarCreated;
    private readonly List<IntPtr> _icons = new();
    private IntPtr _hwnd;
    private int _frame;
    private string _tooltip;
    private bool _added;
    private bool _disposed;

    private TrayIcon(IReadOnlyList<string> iconPaths, string tooltip)
    {
        _tooltip = tooltip;
        _wndProc = OnMessage;
        _className = "RavenTrayWindow_" + Environment.ProcessId;
        _instance = GetModuleHandle(null);
        _taskbarCreated = RegisterWindowMessage("TaskbarCreated");

        var wc = new WNDCLASSEX
        {
            cbSize = (uint)Marshal.SizeOf<WNDCLASSEX>(),
            lpfnWndProc = Marshal.GetFunctionPointerForDelegate(_wndProc),
            hInstance = _instance,
            lpszClassName = _className,
        };
        if (RegisterClassEx(ref wc) == 0) throw new InvalidOperationException("RegisterClassEx failed: " + Marshal.GetLastWin32Error());

        _hwnd = CreateWindowEx(WS_EX_TOOLWINDOW, _className, string.Empty, 0, 0, 0, 0, 0, IntPtr.Zero, IntPtr.Zero, _instance, IntPtr.Zero);
        if (_hwnd == IntPtr.Zero) throw new InvalidOperationException("CreateWindowEx failed: " + Marshal.GetLastWin32Error());

        // Frame 0 is required; a missing extra frame only means no breathing.
        foreach (string path in iconPaths)
        {
            IntPtr icon = LoadImage(IntPtr.Zero, path, IMAGE_ICON, GetSystemMetrics(SM_CXSMICON), GetSystemMetrics(SM_CYSMICON), LR_LOADFROMFILE);
            if (icon != IntPtr.Zero) _icons.Add(icon);
            else if (_icons.Count == 0) throw new InvalidOperationException("LoadImage failed for " + path);
            else Debug.WriteLine("Raven: tray frame not loaded: " + path);
        }
        if (_icons.Count == 0) throw new InvalidOperationException("no tray icon given");

        Add();
    }

    /// <summary>Raised on the UI thread when a menu item or the icon itself is used.</summary>
    public event Action<TrayCommand>? CommandInvoked;

    /// <summary>Raised on the UI thread when a friend's line is chosen (the friend's address).</summary>
    public event Action<string>? FriendInvoked;

    /// <summary>How many icon frames loaded (1 when only the normal icon is there).</summary>
    public int FrameCount => _icons.Count;

    /// <summary>Builds the menu each time it opens, so it shows the current status.</summary>
    public Func<IReadOnlyList<TrayMenuItem>>? MenuProvider { get; set; }

    /// <summary>
    /// Creates the icon from one or more .ico files (frame 0 first), or returns null when the
    /// notification area is not available.
    /// </summary>
    public static TrayIcon? TryCreate(IReadOnlyList<string> iconPaths, string tooltip)
    {
        try
        {
            return new TrayIcon(iconPaths, tooltip);
        }
        catch (Exception ex)
        {
            Debug.WriteLine("Raven: no tray icon: " + ex.Message);
            return null;
        }
    }

    /// <summary>Changes the hover text (at most 127 characters).</summary>
    public void SetTooltip(string tooltip)
    {
        if (_disposed || tooltip == _tooltip) return;
        _tooltip = tooltip;
        NOTIFYICONDATA data = NewData(NIF_TIP | NIF_SHOWTIP);
        _ = Shell_NotifyIcon(NIM_MODIFY, ref data);
    }

    /// <summary>Shows icon frame <paramref name="index"/> (0 = the normal icon); unknown frames show frame 0.</summary>
    public void SetFrame(int index)
    {
        if (index < 0 || index >= _icons.Count) index = 0;
        if (_disposed || index == _frame) return;
        _frame = index;
        if (!_added) return;
        NOTIFYICONDATA data = NewData(NIF_ICON);
        _ = Shell_NotifyIcon(NIM_MODIFY, ref data);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_added)
        {
            NOTIFYICONDATA data = NewData(0);
            _ = Shell_NotifyIcon(NIM_DELETE, ref data);
            _added = false;
        }
        if (_hwnd != IntPtr.Zero)
        {
            _ = DestroyWindow(_hwnd);
            _hwnd = IntPtr.Zero;
        }
        _ = UnregisterClass(_className, _instance);
        foreach (IntPtr icon in _icons) _ = DestroyIcon(icon);
        _icons.Clear();
    }

    private void Add()
    {
        NOTIFYICONDATA data = NewData(NIF_MESSAGE | NIF_ICON | NIF_TIP | NIF_SHOWTIP);
        if (!Shell_NotifyIcon(NIM_ADD, ref data)) throw new InvalidOperationException("Shell_NotifyIcon(NIM_ADD) failed");
        data.uVersion = NOTIFYICON_VERSION_4;
        _ = Shell_NotifyIcon(NIM_SETVERSION, ref data);
        _added = true;
    }

    private NOTIFYICONDATA NewData(uint flags)
    {
        string tip = _tooltip.Length > 127 ? _tooltip[..127] : _tooltip;
        return new NOTIFYICONDATA
        {
            cbSize = (uint)Marshal.SizeOf<NOTIFYICONDATA>(),
            hWnd = _hwnd,
            uID = 1,
            uFlags = flags,
            uCallbackMessage = CallbackMessage,
            hIcon = _icons[_frame],
            szTip = tip,
            szInfo = string.Empty,
            szInfoTitle = string.Empty,
        };
    }

    private IntPtr OnMessage(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        if (msg == CallbackMessage)
        {
            // NOTIFYICON_VERSION_4: LOWORD(lParam) is the event, wParam holds the anchor point.
            uint evt = (uint)(lParam.ToInt64() & 0xFFFF);
            switch (evt)
            {
                case NIN_SELECT:
                case NIN_KEYSELECT:
                case WM_LBUTTONDBLCLK:
                    CommandInvoked?.Invoke(TrayCommand.Open);
                    break;
                case WM_CONTEXTMENU:
                    int x = unchecked((short)(wParam.ToInt64() & 0xFFFF));
                    int y = unchecked((short)((wParam.ToInt64() >> 16) & 0xFFFF));
                    ShowMenu(x, y);
                    break;
            }
            return IntPtr.Zero;
        }

        if (msg == _taskbarCreated && _taskbarCreated != 0)
        {
            try
            {
                Add();
            }
            catch (InvalidOperationException ex)
            {
                Debug.WriteLine("Raven: could not add the tray icon again: " + ex.Message);
            }
            return IntPtr.Zero;
        }

        return DefWindowProc(hwnd, msg, wParam, lParam);
    }

    private void ShowMenu(int x, int y)
    {
        IReadOnlyList<TrayMenuItem>? items = MenuProvider?.Invoke();
        if (items is null || items.Count == 0) return;

        IntPtr menu = CreatePopupMenu();
        if (menu == IntPtr.Zero) return;
        try
        {
            // Menu ids are positions (1, 2, …), so friends' lines get ids too.
            for (int i = 0; i < items.Count; i++)
            {
                TrayMenuItem item = items[i];
                if (item.SeparatorBefore) _ = AppendMenu(menu, MF_SEPARATOR, UIntPtr.Zero, null);
                uint flags = MF_STRING | (item.Enabled && item.Command is not null ? 0 : MF_GRAYED);
                // '&' would become a keyboard accelerator; show it as text.
                string text = item.Text.Replace("&", "&&", StringComparison.Ordinal);
                _ = AppendMenu(menu, flags, (UIntPtr)(uint)(i + 1), text);
            }

            // Required so the menu closes when the user clicks elsewhere.
            _ = SetForegroundWindow(_hwnd);
            int chosen = TrackPopupMenuEx(menu, TPM_RIGHTBUTTON | TPM_RETURNCMD | TPM_NONOTIFY, x, y, _hwnd, IntPtr.Zero);
            _ = PostMessage(_hwnd, WM_NULL, IntPtr.Zero, IntPtr.Zero);
            if (chosen > 0 && chosen <= items.Count && items[chosen - 1] is { Enabled: true, Command: TrayCommand command } chosenItem)
            {
                if (command == TrayCommand.OpenFriend)
                {
                    if (!string.IsNullOrEmpty(chosenItem.Address)) FriendInvoked?.Invoke(chosenItem.Address);
                }
                else
                {
                    CommandInvoked?.Invoke(command);
                }
            }
        }
        finally
        {
            _ = DestroyMenu(menu);
        }
    }

    // ------------------------------------------------------------------ Win32

    private delegate IntPtr WndProc(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WNDCLASSEX
    {
        public uint cbSize;
        public uint style;
        public IntPtr lpfnWndProc;
        public int cbClsExtra;
        public int cbWndExtra;
        public IntPtr hInstance;
        public IntPtr hIcon;
        public IntPtr hCursor;
        public IntPtr hbrBackground;
        public string? lpszMenuName;
        public string lpszClassName;
        public IntPtr hIconSm;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NOTIFYICONDATA
    {
        public uint cbSize;
        public IntPtr hWnd;
        public uint uID;
        public uint uFlags;
        public uint uCallbackMessage;
        public IntPtr hIcon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string szTip;
        public uint dwState;
        public uint dwStateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string szInfo;
        public uint uVersion; // union with uTimeout
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string szInfoTitle;
        public uint dwInfoFlags;
        public Guid guidItem;
        public IntPtr hBalloonIcon;
    }

    [DllImport("shell32.dll", EntryPoint = "Shell_NotifyIconW", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool Shell_NotifyIcon(uint dwMessage, ref NOTIFYICONDATA lpData);

    [DllImport("user32.dll", EntryPoint = "RegisterClassExW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern ushort RegisterClassEx(ref WNDCLASSEX lpwcx);

    [DllImport("user32.dll", EntryPoint = "UnregisterClassW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnregisterClass(string lpClassName, IntPtr hInstance);

    [DllImport("user32.dll", EntryPoint = "CreateWindowExW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateWindowEx(uint dwExStyle, string lpClassName, string lpWindowName, uint dwStyle,
        int x, int y, int nWidth, int nHeight, IntPtr hWndParent, IntPtr hMenu, IntPtr hInstance, IntPtr lpParam);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyWindow(IntPtr hWnd);

    [DllImport("user32.dll", EntryPoint = "DefWindowProcW", CharSet = CharSet.Unicode)]
    private static extern IntPtr DefWindowProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", EntryPoint = "RegisterWindowMessageW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern uint RegisterWindowMessage(string lpString);

    [DllImport("kernel32.dll", EntryPoint = "GetModuleHandleW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr GetModuleHandle(string? lpModuleName);

    [DllImport("user32.dll", EntryPoint = "LoadImageW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr LoadImage(IntPtr hInst, string name, uint type, int cx, int cy, uint fuLoad);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyIcon(IntPtr hIcon);

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int nIndex);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr CreatePopupMenu();

    [DllImport("user32.dll", EntryPoint = "AppendMenuW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AppendMenu(IntPtr hMenu, uint uFlags, UIntPtr uIDNewItem, string? lpNewItem);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int TrackPopupMenuEx(IntPtr hMenu, uint uFlags, int x, int y, IntPtr hwnd, IntPtr lptpm);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyMenu(IntPtr hMenu);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll", EntryPoint = "PostMessageW", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PostMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);
}
