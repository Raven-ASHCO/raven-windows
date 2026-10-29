using System;
using System.Runtime.InteropServices;

namespace Raven.Windows.Helpers;

/// <summary>
/// Small Win32 helpers for the main window: the minimum size from tokens.json
/// (layout.minWindow 760 x 520, in DIPs) via WM_GETMINMAXINFO, DPI scale, and bringing the
/// window to the front.
/// </summary>
internal static class NativeWindow
{
    private const uint WM_GETMINMAXINFO = 0x0024;
    private const int SW_RESTORE = 9;

    private static readonly SubclassProc MinSizeProc = OnSubclassMessage; // kept alive for the process lifetime
    private static int _minWidthDip;
    private static int _minHeightDip;

    /// <summary>Makes Windows refuse to size <paramref name="hwnd"/> below the given DIPs.</summary>
    public static void SetMinimumSize(IntPtr hwnd, int widthDip, int heightDip)
    {
        _minWidthDip = widthDip;
        _minHeightDip = heightDip;
        _ = SetWindowSubclass(hwnd, MinSizeProc, (UIntPtr)1, UIntPtr.Zero);
    }

    /// <summary>DPI scale of the window (1.0 at 96 DPI).</summary>
    public static double Scale(IntPtr hwnd)
    {
        uint dpi = GetDpiForWindow(hwnd);
        return dpi == 0 ? 1.0 : dpi / 96.0;
    }

    /// <summary>Restores (if minimised) and brings the window to the front.</summary>
    public static void BringToFront(IntPtr hwnd)
    {
        if (IsIconic(hwnd)) _ = ShowWindow(hwnd, SW_RESTORE);
        _ = SetForegroundWindow(hwnd);
    }

    private static IntPtr OnSubclassMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam, UIntPtr id, UIntPtr data)
    {
        if (msg == WM_GETMINMAXINFO && lParam != IntPtr.Zero)
        {
            // DIPs to physical pixels: 560 x 480 is 700 x 600 on a screen scaled to 125 %.
            double scale = Scale(hWnd);
            MINMAXINFO info = Marshal.PtrToStructure<MINMAXINFO>(lParam);
            info.ptMinTrackSize.X = (int)Math.Ceiling(_minWidthDip * scale);
            info.ptMinTrackSize.Y = (int)Math.Ceiling(_minHeightDip * scale);
            Marshal.StructureToPtr(info, lParam, false);
        }
        return DefSubclassProc(hWnd, msg, wParam, lParam);
    }

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate IntPtr SubclassProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam, UIntPtr uIdSubclass, UIntPtr dwRefData);

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MINMAXINFO
    {
        public POINT ptReserved;
        public POINT ptMaxSize;
        public POINT ptMaxPosition;
        public POINT ptMinTrackSize;
        public POINT ptMaxTrackSize;
    }

    [DllImport("comctl32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowSubclass(IntPtr hWnd, SubclassProc pfnSubclass, UIntPtr uIdSubclass, UIntPtr dwRefData);

    [DllImport("comctl32.dll")]
    private static extern IntPtr DefSubclassProc(IntPtr hWnd, uint uMsg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr hwnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsIconic(IntPtr hWnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
}
