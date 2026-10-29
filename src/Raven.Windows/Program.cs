using System;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.Windows.AppLifecycle;
using Raven.Windows.Core;

namespace Raven.Windows;

/// <summary>
/// Entry point (replaces the XAML-generated Main, see DISABLE_XAML_GENERATED_MAIN).
/// Raven runs once per user: a second launch hands its activation to the running Raven,
/// which shows its window, and then exits. This follows the Windows App SDK
/// single-instance sample (AppInstance.FindOrRegisterForKey + RedirectActivationToAsync).
/// The app id (<see cref="AppIdentity.Id"/>) is the single-instance key and, for the
/// unpackaged build, the AppUserModelID (an MSIX package gets its own from the manifest).
/// Before any window exists it also asks for English control texts (<see cref="UseEnglish"/>).
/// </summary>
public static class Program
{
    private const string InstanceKey = AppIdentity.Id;
    private const int AppModelErrorNoPackage = 15700;

    /// <summary>Raven is English only (DESIGN.md); Windows' own control texts should be too.</summary>
    private const string English = "en-US";

    [STAThread]
    private static int Main(string[] args)
    {
        SetAppUserModelIdWhenUnpackaged();
        XamlCheckProcessRequirements();
        WinRT.ComWrappersSupport.InitializeComWrappers();

        if (RedirectToRunningInstance())
        {
            return 0;
        }

        UseEnglish();

        Application.Start(callbackParams =>
        {
            var context = new DispatcherQueueSynchronizationContext(DispatcherQueue.GetForCurrentThread());
            SynchronizationContext.SetSynchronizationContext(context);
            _ = new App();
        });
        return 0;
    }

    /// <summary>True when another Raven is already running and got this activation.</summary>
    private static bool RedirectToRunningInstance()
    {
        AppActivationArguments activation = AppInstance.GetCurrent().GetActivatedEventArgs();
        AppInstance main = AppInstance.FindOrRegisterForKey(InstanceKey);
        if (main.IsCurrent)
        {
            main.Activated += OnActivated;
            return false;
        }

        // Let the running instance take the foreground when it shows its window.
        _ = AllowSetForegroundWindow(main.ProcessId);

        // Redirect on another thread and wait without blocking COM (sample pattern).
        IntPtr redirected = CreateEvent(IntPtr.Zero, true, false, null);
        Task.Run(() =>
        {
            main.RedirectActivationToAsync(activation).AsTask().Wait();
            SetEvent(redirected);
        });
        const uint CwmoDefault = 0;
        const uint Infinite = 0xFFFFFFFF;
        _ = CoWaitForMultipleObjects(CwmoDefault, Infinite, 1, new[] { redirected }, out _);
        CloseHandle(redirected);

        try
        {
            using Process process = Process.GetProcessById((int)main.ProcessId);
            if (process.MainWindowHandle != IntPtr.Zero) SetForegroundWindow(process.MainWindowHandle);
        }
        catch (ArgumentException)
        {
            // The other instance exited meanwhile; nothing to bring forward.
        }
        return true;
    }

    /// <summary>
    /// Asks for English texts in the controls Raven does not write itself (for example the text
    /// box context menu "Cut / Copy / Paste", tooltips of WinUI controls), whatever the Windows
    /// display language is. Raven's own texts come from strings.en.json anyway.
    /// <list type="bullet">
    /// <item>.NET: the UI culture of every thread is en-US (unpackaged and packaged).</item>
    /// <item>WinUI's resources (MRT Core): Microsoft.Windows.Globalization.ApplicationLanguages,
    /// which the Windows App SDK supports in unpackaged and packaged apps.</item>
    /// <item>Packaged (MSIX) only: Windows.Globalization.ApplicationLanguages as well (it throws
    /// in an unpackaged process). The manifest declares en-US as the only language.</item>
    /// </list>
    /// What still follows the Windows display language is drawn by Windows itself: the title
    /// bar's button tooltips and window menu, the notification area around Raven's icon,
    /// firewall and other system prompts, and the MSIX installer. Each call is best effort:
    /// a failure is logged and never stops Raven.
    /// </summary>
    private static void UseEnglish()
    {
        CultureInfo english = CultureInfo.GetCultureInfo(English);
        CultureInfo.DefaultThreadCurrentUICulture = english;
        CultureInfo.CurrentUICulture = english;
        try
        {
            Microsoft.Windows.Globalization.ApplicationLanguages.PrimaryLanguageOverride = English;
        }
        catch (Exception ex)
        {
            Debug.WriteLine("Raven: English override (Windows App SDK) failed: " + ex.Message);
        }
        if (!IsPackaged()) return;
        try
        {
            global::Windows.Globalization.ApplicationLanguages.PrimaryLanguageOverride = English;
        }
        catch (Exception ex)
        {
            Debug.WriteLine("Raven: English override (packaged) failed: " + ex.Message);
        }
    }

    private static bool IsPackaged()
    {
        uint length = 0;
        return GetCurrentPackageFullName(ref length, IntPtr.Zero) != AppModelErrorNoPackage;
    }

    /// <summary>
    /// The taskbar and the notification area group Raven's windows by AppUserModelID. A packaged
    /// (MSIX) Raven already has one; the unpackaged build sets Raven's app id before any window.
    /// </summary>
    private static void SetAppUserModelIdWhenUnpackaged()
    {
        if (IsPackaged()) return;
        int hr = SetCurrentProcessExplicitAppUserModelID(AppIdentity.Id);
        if (hr < 0) Debug.WriteLine($"Raven: SetCurrentProcessExplicitAppUserModelID failed (0x{hr:X8})");
    }

    /// <summary>Raised in the running instance (on a background thread) when Raven is launched again.</summary>
    private static void OnActivated(object? sender, AppActivationArguments args)
    {
        App.Current?.OnRedirectedActivation();
    }

    [DllImport("Microsoft.ui.xaml.dll")]
    private static extern void XamlCheckProcessRequirements();

    [DllImport("kernel32.dll")]
    private static extern int GetCurrentPackageFullName(ref uint packageFullNameLength, IntPtr packageFullName);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SetCurrentProcessExplicitAppUserModelID([MarshalAs(UnmanagedType.LPWStr)] string appId);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateEvent(IntPtr lpEventAttributes, bool bManualReset, bool bInitialState, string? lpName);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetEvent(IntPtr hEvent);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(IntPtr hObject);

    [DllImport("ole32.dll")]
    private static extern uint CoWaitForMultipleObjects(uint dwFlags, uint dwMilliseconds, uint nHandles, IntPtr[] pHandles, out uint dwIndex);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AllowSetForegroundWindow(uint dwProcessId);
}
