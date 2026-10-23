// LazyPin - keep any window always on top with a seamless native title-bar pin button.
//
// A lightweight Windows utility for Windows 10 & 11 that places an always-on-top toggle
// pin button seamlessly beside the minimize button on active windows.
//
// Developer:  Raisul Sohan (https://github.com/raisulsohan)
// Repository: https://github.com/raisulsohan/LazyPin
// Copyright (c) 2026 Raisul Sohan. All rights reserved. Licensed under the MIT License.
//
// Plain .NET Framework 4.x (C# 5). Build.ps1 compiles it with the csc.exe that ships with
// Windows, so nothing has to be installed to build or run it.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;
using System.Threading;
using System.Windows.Forms;

[assembly: AssemblyTitle("LazyPin")]
[assembly: AssemblyDescription("LazyPin - Always on top title bar pin button by Raisul Sohan")]
[assembly: AssemblyCompany("Raisul Sohan")]
[assembly: AssemblyProduct("LazyPin")]
[assembly: AssemblyCopyright("Copyright (c) 2026 Raisul Sohan. All rights reserved.")]
[assembly: AssemblyVersion(LazyPin.AppInfo.Version + ".0")]
[assembly: AssemblyFileVersion(LazyPin.AppInfo.Version + ".0")]
[assembly: AssemblyInformationalVersion(LazyPin.AppInfo.Version)]
[assembly: ComVisible(false)]

namespace LazyPin
{
    internal static class AppInfo
    {
        // The one place the version lives: Build.ps1 reads it from here for the installer.
        public const string Version = "1.1.0";

        // The button never shows a caption. This fixed title lets the installer and the
        // uninstaller find the running copy (FindWindow) and ask it to exit with WM_CLOSE.
        public const string ButtonWindowTitle = "LazyPin.PinButton";

        public const string RepositoryUrl = "https://github.com/raisulsohan/LazyPin";
    }

    internal static class Program
    {
        [STAThread]
        private static int Main(string[] args)
        {
            Log.Init();

            // "LazyPin.exe --unpin-all" asks the running copy to unpin every window it pinned
            // (for a shortcut or a script) and ends; it never starts a second copy.
            if (Array.IndexOf(args, "--unpin-all") >= 0)
            {
                IntPtr running = NativeMethods.FindWindow(null, AppInfo.ButtonWindowTitle);
                if (running != IntPtr.Zero) NativeMethods.PostMessage(running, NativeMethods.UnpinAllMessage, IntPtr.Zero, IntPtr.Zero);
                return 0;
            }

            // Started by the watchdog of a copy whose UI thread stopped responding: that copy
            // ends itself right after starting this one, so wait for it to let go. It hands
            // over the windows it had pinned, so they can still be unpinned all at once.
            bool takeover = Array.IndexOf(args, "--takeover") >= 0;
            IntPtr[] handedOver = ParsePinned(args);

            // The manifest declares per-monitor DPI awareness; this covers a build without it.
            NativeMethods.EnablePerMonitorDpiAwareness();

            Mutex mutex = AcquireSingleInstance(takeover ? 10000 : 0);
            if (mutex == null) return 0;
            try
            {
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                // A failure in an event handler is written to the log and LazyPin keeps
                // running; a tray tool must never stop on an error dialog.
                Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
                Application.ThreadException += delegate(object sender, ThreadExceptionEventArgs e)
                {
                    Log.Error("UI thread", e.Exception);
                };
                AppDomain.CurrentDomain.UnhandledException += delegate(object sender, UnhandledExceptionEventArgs e)
                {
                    Log.Error("fatal", e.ExceptionObject as Exception);
                };

                Log.Info(string.Format(
                    "LazyPin {0} started{1} (pid {2}, {3}-bit, Windows {4})",
                    AppInfo.Version,
                    takeover ? " to take over from a copy that stopped responding" : string.Empty,
                    Process.GetCurrentProcess().Id,
                    IntPtr.Size * 8,
                    Environment.OSVersion.Version));

                using (PinApp app = new PinApp(handedOver))
                {
                    Application.Run(app);
                }
                Log.Info("LazyPin stopped");
            }
            finally
            {
                try { mutex.ReleaseMutex(); } catch (ApplicationException) { }
                mutex.Dispose();
            }
            return 0;
        }

        // "--pinned=1A2B,3C4D": window handles in hex, from the copy this one takes over from.
        private static IntPtr[] ParsePinned(string[] args)
        {
            List<IntPtr> handles = new List<IntPtr>();
            foreach (string arg in args)
            {
                if (!arg.StartsWith(PinnedArgument, StringComparison.Ordinal)) continue;
                foreach (string part in arg.Substring(PinnedArgument.Length).Split(','))
                {
                    long value;
                    if (long.TryParse(part, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out value))
                    {
                        handles.Add(new IntPtr(value));
                    }
                }
            }
            return handles.ToArray();
        }

        public const string PinnedArgument = "--pinned=";

        private static Mutex AcquireSingleInstance(int waitMs)
        {
            // The same name as the 1.0.x builds, so an old and a new copy never run side by side.
            string sid;
            using (WindowsIdentity identity = WindowsIdentity.GetCurrent())
            {
                sid = identity.User.Value;
            }
            Mutex mutex = new Mutex(false, @"Local\LazyPin-" + sid);
            bool owned;
            try
            {
                owned = mutex.WaitOne(waitMs);
            }
            catch (AbandonedMutexException)
            {
                // The previous copy was ended without releasing it.
                owned = true;
            }
            if (owned) return mutex;
            mutex.Dispose();
            return null;
        }
    }

    // A small diagnostic log in %LOCALAPPDATA%\LazyPin. It records start, stop, recoveries
    // and errors, so a report like "it stopped working" can be traced afterwards.
    internal static class Log
    {
        private const long MaxBytes = 256 * 1024;
        private static readonly object gate = new object();
        private static string path;
        private static string lastMessage;
        private static DateTime lastTime;
        private static int repeats;

        public static void Init()
        {
            try
            {
                string directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LazyPin");
                Directory.CreateDirectory(directory);
                path = Path.Combine(directory, "LazyPin.log");
            }
            catch
            {
                path = null;
            }
        }

        public static void Info(string message)
        {
            Write(message);
        }

        public static readonly bool Tracing = Environment.GetEnvironmentVariable("LAZYPIN_TRACE") == "1";

        public static void Trace(string message)
        {
            if (!Tracing || path == null) return;
            lock (gate)
            {
                try { File.AppendAllText(path, DateTime.Now.ToString("HH:mm:ss.fff") + "  T " + message + Environment.NewLine, Encoding.UTF8); } catch { }
            }
        }

        public static void Error(string context, Exception exception)
        {
            string detail = exception == null ? "unknown error" : exception.ToString();
            if (detail.Length > 2000) detail = detail.Substring(0, 2000);
            Write("ERROR in " + context + ": " + detail);
        }

        private static void Write(string message)
        {
            if (path == null) return;
            lock (gate)
            {
                try
                {
                    DateTime now = DateTime.Now;
                    // The same failure repeating (for example on every refresh) is written at
                    // most once a minute, so the log can never fill the disk.
                    if (message == lastMessage && (now - lastTime).TotalSeconds < 60)
                    {
                        repeats++;
                        return;
                    }

                    FileInfo info = new FileInfo(path);
                    if (info.Exists && info.Length > MaxBytes)
                    {
                        string previous = Path.Combine(info.DirectoryName, "LazyPin.old.log");
                        if (File.Exists(previous)) File.Delete(previous);
                        File.Move(path, previous);
                    }

                    StringBuilder text = new StringBuilder();
                    if (repeats > 0)
                    {
                        text.Append(now.ToString("yyyy-MM-dd HH:mm:ss.fff")).Append("  (previous message repeated ")
                            .Append(repeats).Append(" more times)").Append(Environment.NewLine);
                    }
                    text.Append(now.ToString("yyyy-MM-dd HH:mm:ss.fff")).Append("  ").Append(message).Append(Environment.NewLine);
                    File.AppendAllText(path, text.ToString(), Encoding.UTF8);

                    lastMessage = message;
                    lastTime = now;
                    repeats = 0;
                }
                catch
                {
                    // Logging must never be the reason LazyPin fails.
                }
            }
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
        public int Width { get { return Right - Left; } }
        public int Height { get { return Bottom - Top; } }
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct POINT
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct MONITORINFO
    {
        public int Size;
        public RECT Monitor;
        public RECT Work;
        public uint Flags;
    }

    internal delegate void WinEventProc(IntPtr hook, uint eventType, IntPtr hwnd, int idObject, int idChild, uint eventThread, uint eventTime);

    internal static class NativeMethods
    {
        public static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);
        public static readonly IntPtr HWND_NOTOPMOST = new IntPtr(-2);

        public const int GWL_STYLE = -16;
        public const int GWL_EXSTYLE = -20;
        public const long WS_MAXIMIZE = 0x01000000L;
        public const long WS_CAPTION = 0x00C00000L;
        public const long WS_MINIMIZEBOX = 0x00020000L;
        public const long WS_MAXIMIZEBOX = 0x00010000L;
        public const long WS_EX_TOPMOST = 0x00000008L;
        public const long WS_EX_TOOLWINDOW = 0x00000080L;
        public const int WS_EX_NOACTIVATE = 0x08000000;

        public const uint SWP_NOSIZE = 0x0001;
        public const uint SWP_NOMOVE = 0x0002;
        public const uint SWP_NOZORDER = 0x0004;
        public const uint SWP_NOACTIVATE = 0x0010;
        public const uint SWP_ASYNCWINDOWPOS = 0x4000;
        public const uint GA_ROOT = 2;
        private const uint GW_HWNDPREV = 3;
        private const uint MONITOR_DEFAULTTONEAREST = 2;

        public const int DWMWA_CAPTION_BUTTON_BOUNDS = 5;
        public const int DWMWA_EXTENDED_FRAME_BOUNDS = 9;
        private const int DWMWA_CLOAKED = 14;

        private const uint WM_NULL = 0x0000;
        private const uint WM_NCHITTEST = 0x0084;
        // The whole title-bar probe may take this long. An app that answers each hit test
        // slowly (busy, but not hung) would otherwise hold LazyPin for over a second.
        private const int ProbeBudgetMs = 50;
        private const uint SMTO_ABORTIFHUNG = 0x0002;
        private const uint SMTO_ERRORONEXIT = 0x0020;
        private const int HTCAPTION = 2;
        private const int HTMINBUTTON = 8;
        private const int HTMAXBUTTON = 9;
        private const int HTCLOSE = 20;
        private const uint CLR_INVALID = 0xFFFFFFFF;

        public const uint WINEVENT_OUTOFCONTEXT = 0x0000;
        public const uint WINEVENT_SKIPOWNPROCESS = 0x0002;
        public const uint EVENT_SYSTEM_FOREGROUND = 0x0003;
        public const uint EVENT_SYSTEM_MOVESIZESTART = 0x000A;
        public const uint EVENT_SYSTEM_MOVESIZEEND = 0x000B;
        public const uint EVENT_SYSTEM_MINIMIZESTART = 0x0016;
        public const uint EVENT_SYSTEM_MINIMIZEEND = 0x0017;
        public const uint EVENT_OBJECT_DESTROY = 0x8001;
        public const uint EVENT_OBJECT_SHOW = 0x8002;
        public const uint EVENT_OBJECT_HIDE = 0x8003;
        public const uint EVENT_OBJECT_LOCATIONCHANGE = 0x800B;
        public const uint EVENT_OBJECT_CLOAKED = 0x8017;
        public const uint EVENT_OBJECT_UNCLOAKED = 0x8018;
        public const int OBJID_WINDOW = 0;
        public const int CHILDID_SELF = 0;

        public const int ERROR_ACCESS_DENIED = 5;

        private const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;
        private const uint TOKEN_QUERY = 0x0008;
        private const int TokenIntegrityLevel = 25;

        public static readonly int RectSize = Marshal.SizeOf(typeof(RECT));

        [DllImport("user32.dll")]
        public static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        public static extern IntPtr GetAncestor(IntPtr hwnd, uint flags);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool IsWindow(IntPtr hwnd);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool IsWindowVisible(IntPtr hwnd);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool IsIconic(IntPtr hwnd);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool IsHungAppWindow(IntPtr hwnd);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr GetWindow(IntPtr hwnd, uint command);

        [DllImport("user32.dll")]
        private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint flags);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetMonitorInfo(IntPtr monitor, ref MONITORINFO info);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool GetWindowRect(IntPtr hwnd, out RECT rect);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool ClientToScreen(IntPtr hwnd, ref POINT point);

        [DllImport("user32.dll")]
        private static extern uint GetDpiForWindow(IntPtr hwnd);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool SetWindowPos(IntPtr hwnd, IntPtr insertAfter, int x, int y, int cx, int cy, uint flags);

        [DllImport("user32.dll")]
        private static extern IntPtr GetDC(IntPtr hwnd);

        [DllImport("user32.dll")]
        private static extern int ReleaseDC(IntPtr hwnd, IntPtr dc);

        [DllImport("gdi32.dll")]
        private static extern uint GetPixel(IntPtr dc, int x, int y);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern IntPtr SetWinEventHook(uint eventMin, uint eventMax, IntPtr module, WinEventProc callback, uint processId, uint threadId, uint flags);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool UnhookWinEvent(IntPtr hook);

        [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
        private static extern IntPtr GetWindowLongPtr64(IntPtr hwnd, int index);

        [DllImport("user32.dll", EntryPoint = "GetWindowLongW")]
        private static extern int GetWindowLong32(IntPtr hwnd, int index);

        [DllImport("user32.dll")]
        public static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint processId);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetClassName(IntPtr hwnd, StringBuilder buffer, int maxCount);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr SendMessageTimeout(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam, uint flags, uint timeout, out IntPtr result);

        [DllImport("dwmapi.dll")]
        public static extern int DwmGetWindowAttribute(IntPtr hwnd, int attribute, out RECT value, int size);

        [DllImport("dwmapi.dll", EntryPoint = "DwmGetWindowAttribute")]
        private static extern int DwmGetWindowAttributeInt(IntPtr hwnd, int attribute, out int value, int size);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetProcessDpiAwarenessContext(IntPtr value);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr OpenProcess(uint access, [MarshalAs(UnmanagedType.Bool)] bool inheritHandle, uint processId);

        [DllImport("kernel32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool CloseHandle(IntPtr handle);

        [DllImport("advapi32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool OpenProcessToken(IntPtr process, uint access, out IntPtr token);

        [DllImport("advapi32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetTokenInformation(IntPtr token, int informationClass, IntPtr information, int length, out int returnLength);

        [DllImport("advapi32.dll")]
        private static extern IntPtr GetSidSubAuthorityCount(IntPtr sid);

        [DllImport("advapi32.dll")]
        private static extern IntPtr GetSidSubAuthority(IntPtr sid, uint index);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        public static extern IntPtr FindWindow(string className, string windowName);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool PostMessage(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int RegisterWindowMessage(string name);

        // Sent by "LazyPin.exe --unpin-all" to the running copy's button window.
        public static readonly int UnpinAllMessage = RegisterWindowMessage("LazyPin.UnpinAll");

        public static void EnablePerMonitorDpiAwareness()
        {
            try
            {
                // DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2: positions in physical pixels on every monitor.
                SetProcessDpiAwarenessContext(new IntPtr(-4));
            }
            catch (EntryPointNotFoundException)
            {
                // Windows 10 before 1703.
            }
        }

        private static bool dpiApiMissing;

        public static int GetWindowDpi(IntPtr hwnd)
        {
            uint dpi = 0;
            if (!dpiApiMissing)
            {
                try
                {
                    dpi = GetDpiForWindow(hwnd);
                }
                catch (EntryPointNotFoundException)
                {
                    // Windows 10 before 1607.
                    dpiApiMissing = true;
                }
            }
            return dpi < 48 ? 96 : (int)dpi;
        }

        public static long GetStyle(IntPtr hwnd)
        {
            return GetWindowLong(hwnd, GWL_STYLE);
        }

        public static bool IsTopmost(IntPtr hwnd)
        {
            return (GetWindowLong(hwnd, GWL_EXSTYLE) & WS_EX_TOPMOST) != 0;
        }

        private static long GetWindowLong(IntPtr hwnd, int index)
        {
            return IntPtr.Size == 8 ? GetWindowLongPtr64(hwnd, index).ToInt64() : GetWindowLong32(hwnd, index);
        }

        public static string GetWindowClass(IntPtr hwnd)
        {
            if (hwnd == IntPtr.Zero) return string.Empty;
            StringBuilder buffer = new StringBuilder(256);
            int length = GetClassName(hwnd, buffer, buffer.Capacity);
            return length > 0 ? buffer.ToString(0, length) : string.Empty;
        }

        // Cloaked windows exist and pass IsWindowVisible, but DWM does not draw them:
        // windows on another virtual desktop and suspended UWP apps. Nothing to pin to.
        public static bool IsCloaked(IntPtr hwnd)
        {
            int cloaked;
            int hr = DwmGetWindowAttributeInt(hwnd, DWMWA_CLOAKED, out cloaked, sizeof(int));
            return hr == 0 && cloaked != 0;
        }

        // Root window classes of the Windows shell and its flyouts. They become the
        // foreground window when the user clicks them (clock, tray, Start, Task View,
        // desktop) but have no title bar to put a pin on, and the taskbar is permanently
        // TOPMOST, so the button used to park itself beside the clock drawing the
        // "pinned" glyph.
        private static readonly string[] shellWindowClasses = new string[]
        {
            "Shell_TrayWnd",                          // taskbar
            "Shell_SecondaryTrayWnd",                 // taskbar on other monitors
            "TrayNotifyWnd",
            "NotifyIconOverflowWindow",               // Win10 tray overflow
            "TopLevelWindowForOverflowXamlIsland",    // Win11 tray overflow
            "Shell_InputSwitchTopLevelWindow",        // language / keyboard flyout
            "Progman",                                // desktop
            "WorkerW",                                // desktop (wallpaper host)
            "Windows.UI.Core.CoreWindow",             // Start, Search, Notification Centre, Quick Settings, Widgets, emoji panel
            "XamlExplorerHostIslandWindow",           // Win11 Task View, Alt+Tab, snap layouts
            "MultitaskingViewFrame",                  // Win10 Task View
            "TaskSwitcherWnd",                        // classic Alt+Tab
            "TaskSwitcherOverlayWnd",
            "TaskListThumbnailWnd",                   // taskbar thumbnails
            "ForegroundStaging",
            "ImmersiveLauncher",                      // Win8 Start
            "ApplicationManager_DesktopShellWindow",
            "NativeHWNDHost",                         // volume / brightness OSD host
            "Xaml_WindowedPopupClass",                // XAML popups
            "#32768",                                 // Win32 menus
            "tooltips_class32",
            "IME",
            "MSCTFIME UI"
        };

        public static bool IsShellOrSystemWindow(IntPtr hwnd)
        {
            if (hwnd == IntPtr.Zero) return true;
            // Tool windows (floating palettes, flyouts, the taskbar itself) have no caption buttons.
            if ((GetWindowLong(hwnd, GWL_EXSTYLE) & WS_EX_TOOLWINDOW) != 0) return true;
            string className = GetWindowClass(hwnd);
            for (int i = 0; i < shellWindowClasses.Length; i++)
            {
                if (string.Equals(className, shellWindowClasses[i], StringComparison.OrdinalIgnoreCase)) return true;
            }
            return false;
        }

        // A window the pin button may attach to: a real, visible, drawn, top-level
        // application window that is neither ours nor part of the shell.
        public static bool IsEligibleTarget(IntPtr hwnd, uint ownProcessId)
        {
            if (hwnd == IntPtr.Zero || !IsWindow(hwnd) || !IsWindowVisible(hwnd) || IsIconic(hwnd)) return false;
            uint processId;
            GetWindowThreadProcessId(hwnd, out processId);
            if (processId == ownProcessId) return false;
            if (IsShellOrSystemWindow(hwnd)) return false;
            if (IsCloaked(hwnd)) return false;
            return true;
        }

        // True when the window covers its whole monitor (borderless full-screen games, videos, F11 browsers).
        public static bool CoversMonitor(IntPtr hwnd, RECT rect)
        {
            IntPtr monitor = MonitorFromWindow(hwnd, MONITOR_DEFAULTTONEAREST);
            if (monitor == IntPtr.Zero) return false;
            MONITORINFO info = new MONITORINFO();
            info.Size = Marshal.SizeOf(typeof(MONITORINFO));
            if (!GetMonitorInfo(monitor, ref info)) return false;
            return rect.Left <= info.Monitor.Left &&
                   rect.Top <= info.Monitor.Top &&
                   rect.Right >= info.Monitor.Right &&
                   rect.Bottom >= info.Monitor.Bottom;
        }

        public static bool GetMonitorWorkArea(IntPtr hwnd, out RECT work)
        {
            work = new RECT();
            IntPtr monitor = MonitorFromWindow(hwnd, MONITOR_DEFAULTTONEAREST);
            if (monitor == IntPtr.Zero) return false;
            MONITORINFO info = new MONITORINFO();
            info.Size = Marshal.SizeOf(typeof(MONITORINFO));
            if (!GetMonitorInfo(monitor, ref info)) return false;
            work = info.Work;
            return true;
        }

        // True when the window answers a message within the time given (it is not busy).
        public static bool Responds(IntPtr hwnd, uint timeoutMs)
        {
            IntPtr result;
            return SendMessageTimeout(hwnd, WM_NULL, IntPtr.Zero, IntPtr.Zero, SMTO_ABORTIFHUNG, timeoutMs, out result) != IntPtr.Zero;
        }

        // True when the target sits above the button in the z-order, i.e. the button
        // actually needs to be promoted again. Walking GW_HWNDPREV never sends messages.
        public static bool IsWindowAbove(IntPtr target, IntPtr button)
        {
            if (target == IntPtr.Zero || button == IntPtr.Zero) return false;
            IntPtr current = GetWindow(button, GW_HWNDPREV);
            int guard = 0;
            while (current != IntPtr.Zero && guard++ < 512)
            {
                if (current == target) return true;
                current = GetWindow(current, GW_HWNDPREV);
            }
            return false;
        }

        // Distance from the window's visible right edge to the leftmost title-bar control
        // (native caption buttons plus custom ones such as a browser's profile or tab-search
        // icons), found with WM_NCHITTEST. Returns -1 when the window did not answer every
        // probe in time: a half-finished scan would describe only part of the title bar and
        // make the button jump, so the caller keeps what it already knows.
        public static int DetectControlsWidth(IntPtr hwnd, RECT visibleRect, int captionHeight, double scale, int baseControlsWidth)
        {
            // Never queue synchronous messages on a thread that is already not responding:
            // every timed-out WM_NCHITTEST still stays in that thread's queue.
            if (IsHungAppWindow(hwnd)) return -1;
            Stopwatch elapsed = Stopwatch.StartNew();

            int y = visibleRect.Top + Math.Max(12, captionHeight / 2);

            // Step 1: verify the window actually implements non-client caption hit testing.
            // The top-right area (where the Close button sits) must return HTCLOSE (20), HTMAXBUTTON (9), HTMINBUTTON (8), or HTCAPTION (2).
            // If it returns HTCLIENT (1), the window treats the entire title bar as client area (e.g. Win11 File Explorer, XAML Islands).
            // In that case, NCHITTEST cannot distinguish titlebar controls from client space, so keep baseControlsWidth.
            int closeHit;
            if (!HitTest(hwnd, visibleRect.Right - (int)Math.Round(20.0 * scale), y, out closeHit)) return -1;
            if (closeHit != HTCLOSE && closeHit != HTMAXBUTTON && closeHit != HTMINBUTTON && closeHit != HTCAPTION)
            {
                return baseControlsWidth;
            }

            int step = Math.Max(4, (int)Math.Round(6.0 * scale));
            int gapThreshold = (int)Math.Round(20.0 * scale);
            int maxDist = Math.Min(visibleRect.Width, (int)Math.Round(450.0 * scale));

            int lastControlDist = baseControlsWidth;
            int consecutiveCaption = 0;
            bool foundCaptionAfterControls = false;

            for (int dist = baseControlsWidth; dist <= maxDist; dist += step)
            {
                int hit;
                if (!HitTest(hwnd, visibleRect.Right - dist, y, out hit)) return -1;
                if (elapsed.ElapsedMilliseconds > ProbeBudgetMs) return -1;

                // Controls include custom client controls (1), object (19), menus (3, 5), and native buttons (8, 9, 20, 21)
                bool isControl = (hit == 1 || hit == 19 || hit == 3 || hit == 5 || hit == 8 || hit == 9 || hit == 20 || hit == 21);

                if (isControl)
                {
                    lastControlDist = dist;
                    consecutiveCaption = 0;
                }
                else if (hit == HTCAPTION || hit == 0)
                {
                    consecutiveCaption += step;
                    if (consecutiveCaption >= gapThreshold)
                    {
                        foundCaptionAfterControls = true;
                        break;
                    }
                }
            }

            if (lastControlDist > baseControlsWidth && !foundCaptionAfterControls)
            {
                return baseControlsWidth;
            }

            return lastControlDist;
        }

        private static bool HitTest(IntPtr hwnd, int x, int y, out int hit)
        {
            IntPtr lParam = new IntPtr((y << 16) | (x & 0xFFFF));
            IntPtr result;
            hit = 0;
            if (SendMessageTimeout(hwnd, WM_NCHITTEST, IntPtr.Zero, lParam, SMTO_ABORTIFHUNG | SMTO_ERRORONEXIT, 20, out result) == IntPtr.Zero)
            {
                return false;
            }
            hit = unchecked((int)result.ToInt64());
            return true;
        }

        public static bool TryGetScreenPixel(int x, int y, out Color color)
        {
            color = Color.Empty;
            IntPtr dc = GetDC(IntPtr.Zero);
            if (dc == IntPtr.Zero) return false;
            uint pixel;
            try
            {
                pixel = GetPixel(dc, x, y);
            }
            finally
            {
                ReleaseDC(IntPtr.Zero, dc);
            }
            if (pixel == CLR_INVALID) return false;
            color = Color.FromArgb((int)(pixel & 0xFF), (int)((pixel >> 8) & 0xFF), (int)((pixel >> 16) & 0xFF));
            return true;
        }

        // Mandatory integrity level of a process (0x2000 = medium, 0x3000 = high, i.e. run as
        // administrator), or -1 when Windows does not tell.
        public static int GetProcessIntegrityLevel(uint processId)
        {
            IntPtr process = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, processId);
            if (process == IntPtr.Zero) return -1;
            IntPtr token = IntPtr.Zero;
            IntPtr buffer = IntPtr.Zero;
            try
            {
                if (!OpenProcessToken(process, TOKEN_QUERY, out token)) return -1;
                int length;
                GetTokenInformation(token, TokenIntegrityLevel, IntPtr.Zero, 0, out length);
                if (length <= 0) return -1;
                buffer = Marshal.AllocHGlobal(length);
                if (!GetTokenInformation(token, TokenIntegrityLevel, buffer, length, out length)) return -1;
                IntPtr sid = Marshal.ReadIntPtr(buffer); // TOKEN_MANDATORY_LABEL.Label.Sid
                int count = Marshal.ReadByte(GetSidSubAuthorityCount(sid));
                if (count < 1) return -1;
                return Marshal.ReadInt32(GetSidSubAuthority(sid, (uint)(count - 1)));
            }
            finally
            {
                if (buffer != IntPtr.Zero) Marshal.FreeHGlobal(buffer);
                if (token != IntPtr.Zero) CloseHandle(token);
                CloseHandle(process);
            }
        }
    }

    // One out-of-context WinEvent hook, for all processes or for one process.
    internal sealed class WinEventHook
    {
        private readonly uint eventMin;
        private readonly uint eventMax;
        // Referenced here so the delegate lives as long as the native hook that calls it.
        private readonly WinEventProc callback;
        private IntPtr handle;
        private uint processId;

        public WinEventHook(uint eventMin, uint eventMax, WinEventProc callback)
        {
            this.eventMin = eventMin;
            this.eventMax = eventMax;
            this.callback = callback;
        }

        // processId 0 = every process. Returns false when Windows refused the hook.
        public bool Set(uint processId)
        {
            if (handle != IntPtr.Zero && this.processId == processId) return true;
            Unset();
            handle = NativeMethods.SetWinEventHook(
                eventMin,
                eventMax,
                IntPtr.Zero,
                callback,
                processId,
                0,
                NativeMethods.WINEVENT_OUTOFCONTEXT | NativeMethods.WINEVENT_SKIPOWNPROCESS);
            this.processId = processId;
            return handle != IntPtr.Zero;
        }

        public void Unset()
        {
            if (handle != IntPtr.Zero)
            {
                NativeMethods.UnhookWinEvent(handle);
                handle = IntPtr.Zero;
            }
            processId = 0;
        }
    }

    // Reads title-bar pixels on a background thread. A screen read waits until DWM has
    // composed and read back a frame: about 10 ms, at times well over 100 ms. The UI thread
    // must stay free to follow the window, so it only hands over where to look.
    internal sealed class ColourSampler
    {
        private readonly object gate = new object();
        private readonly AutoResetEvent wake = new AutoResetEvent(false);
        // Called on the sampler thread with the request's token and the colour found.
        private readonly Action<int, Color> deliver;
        private bool hasRequest;
        private int requestX;
        private int requestY;
        private int requestToken;
        private volatile bool stopped;

        public ColourSampler(Action<int, Color> deliver)
        {
            this.deliver = deliver;
            Thread thread = new Thread(Run);
            thread.IsBackground = true;
            thread.Name = "LazyPin colour sampler";
            thread.Priority = ThreadPriority.BelowNormal;
            thread.Start();
        }

        // Only the newest request counts; one that has not started yet is replaced.
        public void Request(int x, int y, int token)
        {
            lock (gate)
            {
                requestX = x;
                requestY = y;
                requestToken = token;
                hasRequest = true;
            }
            wake.Set();
        }

        public void Stop()
        {
            stopped = true;
            wake.Set();
        }

        private void Run()
        {
            while (true)
            {
                wake.WaitOne();
                if (stopped) return;
                int x;
                int y;
                int token;
                lock (gate)
                {
                    if (!hasRequest) continue;
                    x = requestX;
                    y = requestY;
                    token = requestToken;
                    hasRequest = false;
                }
                try
                {
                    Color colour;
                    if (NativeMethods.TryGetScreenPixel(x, y, out colour)) deliver(token, colour);
                }
                catch (Exception ex)
                {
                    Log.Error("colour sampler", ex);
                }
            }
        }
    }

    // The pin button: a small borderless window that never takes the focus.
    internal sealed class PinButton : Form
    {
        private const int WM_CLOSE = 0x0010;
        private const int WM_QUERYENDSESSION = 0x0011;
        private const int WM_ENDSESSION = 0x0016;
        private const int WM_SETTINGCHANGE = 0x001A;
        private const int WM_MOUSEACTIVATE = 0x0021;
        private const int WM_DISPLAYCHANGE = 0x007E;
        private const int WM_POWERBROADCAST = 0x0218;
        private const int WM_DPICHANGED = 0x02E0;
        private const int WM_THEMECHANGED = 0x031A;
        private const int WM_DWMCOMPOSITIONCHANGED = 0x031E;
        private const int WM_DWMCOLORIZATIONCOLORCHANGED = 0x0320;
        private const int MA_NOACTIVATE = 3;
        private const int PBT_APMRESUMESUSPEND = 0x0007;
        private const int PBT_APMRESUMEAUTOMATIC = 0x0012;
        private const string GlyphUnpinned = "";
        private const string GlyphPinned = "";

        private bool pinned;
        private bool blocked;
        private bool hovered;
        private bool pressed;
        private Color background = Color.FromArgb(32, 32, 32);
        private Color icon = Color.FromArgb(245, 245, 245);
        private Color pinnedIcon = Color.FromArgb(96, 205, 255);
        private Color hoverBackground = Color.FromArgb(64, 64, 64);
        private Color pressedBackground = Color.FromArgb(79, 79, 79);
        private int dpi = 96;
        private Font glyphFont;
        private int glyphFontDpi;

        public event EventHandler Clicked;
        public event EventHandler ExitRequested;
        public event EventHandler SystemChanged;
        public event EventHandler Resumed;
        public event EventHandler UnpinAllRequested;

        public PinButton()
        {
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            AutoScaleMode = AutoScaleMode.None;
            TopMost = true;
            Cursor = Cursors.Hand;
            Text = AppInfo.ButtonWindowTitle;
            BackColor = background;
            Size = new Size(46, 30);
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint, true);
        }

        protected override bool ShowWithoutActivation { get { return true; } }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ExStyle |= NativeMethods.WS_EX_NOACTIVATE | (int)NativeMethods.WS_EX_TOOLWINDOW;
                return cp;
            }
        }

        public void SetState(bool pinned, bool blocked)
        {
            if (this.pinned == pinned && this.blocked == blocked) return;
            this.pinned = pinned;
            this.blocked = blocked;
            Invalidate();
        }

        public void SetPalette(Color background, Color icon, Color pinnedIcon, Color hoverBackground, Color pressedBackground)
        {
            this.background = background;
            this.icon = icon;
            this.pinnedIcon = pinnedIcon;
            this.hoverBackground = hoverBackground;
            this.pressedBackground = pressedBackground;
            BackColor = background;
            Invalidate();
        }

        // The DPI of the monitor the target window is on; the glyph is drawn for it.
        public void SetDpi(int dpi)
        {
            if (this.dpi == dpi) return;
            this.dpi = dpi;
            Invalidate();
        }

        protected override void OnMouseEnter(EventArgs e)
        {
            hovered = true;
            Invalidate();
            base.OnMouseEnter(e);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            hovered = false;
            pressed = false;
            Invalidate();
            base.OnMouseLeave(e);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                pressed = true;
                Invalidate();
            }
            base.OnMouseDown(e);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            pressed = false;
            Invalidate();
            base.OnMouseUp(e);
        }

        protected override void OnMouseClick(MouseEventArgs e)
        {
            base.OnMouseClick(e);
            if (e.Button == MouseButtons.Left) Raise(Clicked);
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            // OnPaint fills every pixel.
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            // Clear, not an anti-aliased FillRectangle: that covers the top and left edge
            // pixels only half and leaves a dark line there.
            g.Clear(pressed ? pressedBackground : hovered ? hoverBackground : background);

            // Segoe MDL2 glyphs sit optically high when their font line box is centered.
            // An optical baseline correction aligns the pin with the native caption icons.
            float baselineOffset = (float)Math.Round(1.5 * (ClientSize.Height / 29.0), 1);
            RectangleF textBounds = new RectangleF(0, baselineOffset, ClientSize.Width, ClientSize.Height);
            Color glyphColor = pinned ? pinnedIcon : icon;
            if (blocked) glyphColor = Color.FromArgb(110, glyphColor);
            using (StringFormat format = new StringFormat())
            using (SolidBrush brush = new SolidBrush(glyphColor))
            {
                format.Alignment = StringAlignment.Center;
                format.LineAlignment = StringAlignment.Center;
                g.DrawString(pinned ? GlyphPinned : GlyphUnpinned, GetGlyphFont(), brush, textBounds, format);
            }
        }

        private Font GetGlyphFont()
        {
            if (glyphFont != null && glyphFontDpi == dpi) return glyphFont;
            if (glyphFont != null) glyphFont.Dispose();
            // 11 pt at the DPI of the target's monitor, in pixels, so the glyph also has the
            // right size on a second monitor with another scale.
            float pixels = 11f * dpi / 72f;
            glyphFont = new Font("Segoe MDL2 Assets", pixels, FontStyle.Regular, GraphicsUnit.Pixel);
            if (!string.Equals(glyphFont.Name, "Segoe MDL2 Assets", StringComparison.OrdinalIgnoreCase))
            {
                glyphFont.Dispose();
                glyphFont = new Font("Segoe Fluent Icons", pixels, FontStyle.Regular, GraphicsUnit.Pixel);
            }
            glyphFontDpi = dpi;
            return glyphFont;
        }

        private void Raise(EventHandler handler)
        {
            if (handler != null) handler(this, EventArgs.Empty);
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == NativeMethods.UnpinAllMessage && NativeMethods.UnpinAllMessage != 0)
            {
                Raise(UnpinAllRequested);
                return;
            }
            switch (m.Msg)
            {
                case WM_MOUSEACTIVATE:
                    // Clicking the button must never take the focus from the window it pins.
                    m.Result = new IntPtr(MA_NOACTIVATE);
                    return;
                case WM_CLOSE:
                    // Windows, the installer (Restart Manager) or "taskkill" without /F asks
                    // LazyPin to close. Closing only this form would leave a tray icon with no
                    // button behind it, so the whole tool shuts down.
                    Raise(ExitRequested);
                    return;
                case WM_QUERYENDSESSION:
                    m.Result = new IntPtr(1);
                    return;
                case WM_ENDSESSION:
                    if (m.WParam != IntPtr.Zero) Raise(ExitRequested);
                    m.Result = IntPtr.Zero;
                    return;
                case WM_DPICHANGED:
                    // The button is sized for the window it sits on, never by WinForms scaling.
                    m.Result = IntPtr.Zero;
                    return;
                case WM_DISPLAYCHANGE:
                case WM_SETTINGCHANGE:
                case WM_THEMECHANGED:
                case WM_DWMCOMPOSITIONCHANGED:
                case WM_DWMCOLORIZATIONCOLORCHANGED:
                    base.WndProc(ref m);
                    Raise(SystemChanged);
                    return;
                case WM_POWERBROADCAST:
                    base.WndProc(ref m);
                    int reason = unchecked((int)m.WParam.ToInt64());
                    if (reason == PBT_APMRESUMEAUTOMATIC || reason == PBT_APMRESUMESUSPEND) Raise(Resumed);
                    return;
            }
            base.WndProc(ref m);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && glyphFont != null)
            {
                glyphFont.Dispose();
                glyphFont = null;
            }
            base.Dispose(disposing);
        }
    }

    // The tool: tray icon, window tracking and the pin button.
    internal sealed class PinApp : ApplicationContext
    {
        // Window events (focus, move, resize, minimise, close, virtual desktop switch) move
        // the button at once. The timer is only a safety net for changes no event reports
        // (another app toggling always-on-top, a title bar that adds an icon); it runs fast
        // only for a moment, while the title-bar colour settles after an activation.
        private const int FastIntervalMs = 33;
        private const int VisibleIntervalMs = 250;
        private const int IdleIntervalMs = 500;

        // The title-bar probe sends synchronous WM_NCHITTEST messages to the target's UI
        // thread: once per window, again when the layout changes (rate-limited while a
        // resize is in progress) and slowly otherwise. Doing it on every tick saturates apps
        // whose hit-test is expensive (WPF WindowChrome, custom frames).
        private const int ProbeMinIntervalMs = 100;
        private const int ProbeMaxAgeMs = 1000;
        private const int ProbeRetryMs = 150;
        private const int ProbeGuessRetryMs = 50;
        private const int ProbeMaxRetries = 3;

        // Reading a screen pixel makes DWM synchronise and read back the composed frame.
        // The title-bar colour only changes when the target or the focused window changes
        // (active / inactive caption), so sample in a short burst around those events (the
        // activation animation needs a few frames to settle) and otherwise slowly.
        private const int SampleBurstMs = 600;
        private const int SampleMinIntervalMs = 50;
        private const int SampleMaxAgeMs = 2000;

        // A pin request for an app that is busy is queued (asynchronous); the window turns
        // always-on-top when the app gets to it. Until then it must not be forgotten.
        private const int PendingPinMs = 10000;

        // The watchdog restarts LazyPin when its UI thread has not run its timer for this long.
        private const int WatchdogCheckMs = 5000;
        private const int WatchdogTimeoutMs = 30000;

        private const string TipPin = "Keep this window on top";
        private const string TipUnpin = "Unpin this window";
        private const string TipBlocked = "This window runs as administrator, so Windows does not let LazyPin pin it";

        private readonly uint ownProcessId;
        private readonly int ownIntegrity;
        private readonly Stopwatch clock = Stopwatch.StartNew();
        private readonly Icon appIcon;
        private readonly Icon trayImage;
        private readonly PinButton button;
        private readonly ToolTip tooltip;
        private readonly NotifyIcon trayIcon;
        private readonly ContextMenuStrip trayMenu;
        private readonly ToolStripMenuItem startupItem;
        private readonly string startupLink;
        private readonly System.Windows.Forms.Timer timer;
        private readonly WinEventProc eventCallback;
        // Rare events from every process.
        private readonly WinEventHook foregroundHook;
        private readonly WinEventHook moveSizeHook;
        private readonly WinEventHook minimizeHook;
        // Frequent events, from the target's process only. A system-wide LOCATIONCHANGE hook
        // would deliver every cursor and window move on the machine to this thread.
        private readonly WinEventHook locationHook;
        private readonly WinEventHook showHideHook;
        private readonly WinEventHook cloakHook;
        private readonly ManualResetEvent watchdogStop = new ManualResetEvent(false);
        private long heartbeat = Stopwatch.GetTimestamp();
        private bool hookFailureLogged;

        // Windows LazyPin made always-on-top, oldest first. Their order among each other is left
        // to Windows: the window pinned or clicked last is in front, as with any windows.
        private readonly List<IntPtr> pinnedWindows = new List<IntPtr>();
        // Pins queued for a busy app (see PendingPinMs), with the time they may take.
        private readonly Dictionary<IntPtr, long> pendingPins = new Dictionary<IntPtr, long>();
        // A copy of pinnedWindows for the watchdog thread, which hands it to the fresh copy.
        private volatile IntPtr[] pinnedSnapshot = new IntPtr[0];
        private readonly ToolStripMenuItem unpinAllItem;

        private IntPtr targetHwnd = IntPtr.Zero;
        private IntPtr stateHwnd = IntPtr.Zero;
        private bool isPinned;
        private bool isBlocked;
        private Rectangle lastBounds = Rectangle.Empty;
        private IntPtr moveSizeHwnd = IntPtr.Zero;
        private bool shuttingDown;
        private bool refreshing;
        private bool refreshAgain;
        private long fastUntil;

        // Where the button sits relative to the target's window rectangle, so a plain move
        // can be followed without measuring the title bar again.
        private int trackRightOffset;
        private int trackTopOffset;
        private int trackWidth;
        private int trackHeight;

        // Frame geometry from DWM, kept per window. DWM learns a window's new position a
        // frame or so after the window moves, so values read while it moves can belong to
        // where it was a moment ago; mixing them with GetWindowRect made the button wobble
        // by a few pixels while dragging. They are therefore read only while the window
        // stands still (the same rectangle as at the previous refresh) or when the window,
        // its DPI or its maximised state is new, and are kept relative to the window
        // rectangle: the invisible resize borders and the caption buttons' distance from the
        // right edge stay the same when the window moves or is resized.
        private IntPtr frameHwnd = IntPtr.Zero;
        private int frameDpi;
        private bool frameMaximized;
        private int insetLeft;
        private int insetTop;
        private int insetRight;
        private int insetBottom;
        private bool frameHasCaptionButtons;
        private int captionRightDist;
        private int captionButtonsWidth;
        private int captionButtonsHeight;
        private IntPtr restHwnd = IntPtr.Zero;
        private RECT restRect;

        private IntPtr probeHwnd = IntPtr.Zero;
        private ProbeKey probeKey;
        private int probeValue;
        private bool probeIsGuess;
        private int probeCandidate = -1;
        private bool probeRetry;
        private int probeRetries;
        private long probeTime = long.MinValue / 2;
        private const int ProbeMemorySize = 16;
        private readonly Dictionary<IntPtr, ProbeMemory> probeMemory = new Dictionary<IntPtr, ProbeMemory>();
        private long probeMemoryOrder;

        private readonly ColourSampler sampler;
        private IntPtr sampleHwnd = IntPtr.Zero;
        private IntPtr sampleForeground = IntPtr.Zero;
        private int sampleToken;
        private long sampleTime = long.MinValue / 2;
        private long sampleBurstUntil;
        private Color background = Color.FromArgb(32, 32, 32);

        private IntPtr integrityHwnd = IntPtr.Zero;
        private bool integrityBlocked;

        public PinApp(IntPtr[] handedOver)
        {
            ownProcessId = (uint)Process.GetCurrentProcess().Id;
            // Windows pinned by the copy this one took over from, while they are still on top.
            foreach (IntPtr hwnd in handedOver)
            {
                if (NativeMethods.IsWindow(hwnd) && NativeMethods.IsTopmost(hwnd) && !pinnedWindows.Contains(hwnd)) pinnedWindows.Add(hwnd);
            }
            if (pinnedWindows.Count > 0) Log.Info("took over " + pinnedWindows.Count + " pinned window(s)");
            pinnedSnapshot = pinnedWindows.ToArray();
            ownIntegrity = NativeMethods.GetProcessIntegrityLevel(ownProcessId);
            appIcon = LoadAppIcon();
            trayImage = new Icon(appIcon, SystemInformation.SmallIconSize);

            button = new PinButton();
            button.Icon = appIcon;
            button.Clicked += delegate { Guarded("click", OnButtonClicked); };
            button.ExitRequested += delegate { Exit(); };
            button.SystemChanged += delegate { Guarded("system change", OnSystemChanged); };
            button.Resumed += delegate { Guarded("resume", OnResumed); };
            button.UnpinAllRequested += delegate { Guarded("unpin all", OnUnpinAll); };
            // Create the hidden window now so the installer can find it before the button
            // has ever been shown.
            IntPtr buttonHandle = button.Handle;

            tooltip = new ToolTip();
            // The button never becomes the active window, so tips must not wait for that.
            tooltip.ShowAlways = true;
            tooltip.SetToolTip(button, TipPin);

            startupLink = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Startup), "LazyPin.lnk");
            trayMenu = new ContextMenuStrip();
            unpinAllItem = new ToolStripMenuItem("Unpin all windows");
            unpinAllItem.Click += delegate { Guarded("unpin all", OnUnpinAll); };
            trayMenu.Items.Add(unpinAllItem);
            trayMenu.Items.Add(new ToolStripSeparator());
            startupItem = new ToolStripMenuItem("Run at Windows startup");
            startupItem.Checked = File.Exists(startupLink);
            startupItem.Click += delegate { Guarded("startup shortcut", ToggleStartup); };
            trayMenu.Items.Add(startupItem);
            trayMenu.Items.Add(new ToolStripSeparator());
            ToolStripMenuItem aboutItem = new ToolStripMenuItem("About LazyPin");
            aboutItem.Click += delegate { ShowAbout(); };
            trayMenu.Items.Add(aboutItem);
            ToolStripMenuItem exitItem = new ToolStripMenuItem("Exit");
            exitItem.Click += delegate { Exit(); };
            trayMenu.Items.Add(exitItem);
            trayMenu.Opening += delegate
            {
                // The installer can add or remove the startup shortcut while LazyPin runs.
                startupItem.Checked = File.Exists(startupLink);
                PrunePinned(clock.ElapsedMilliseconds);
                int count = pinnedWindows.Count;
                unpinAllItem.Text = count > 0 ? "Unpin all windows (" + count + ")" : "Unpin all windows";
                unpinAllItem.Enabled = count > 0;
            };

            trayIcon = new NotifyIcon();
            trayIcon.Icon = trayImage;
            trayIcon.Text = "LazyPin v" + AppInfo.Version + " by Raisul Sohan";
            trayIcon.ContextMenuStrip = trayMenu;
            trayIcon.Visible = true;

            eventCallback = OnWinEvent;
            foregroundHook = new WinEventHook(NativeMethods.EVENT_SYSTEM_FOREGROUND, NativeMethods.EVENT_SYSTEM_FOREGROUND, eventCallback);
            moveSizeHook = new WinEventHook(NativeMethods.EVENT_SYSTEM_MOVESIZESTART, NativeMethods.EVENT_SYSTEM_MOVESIZEEND, eventCallback);
            minimizeHook = new WinEventHook(NativeMethods.EVENT_SYSTEM_MINIMIZESTART, NativeMethods.EVENT_SYSTEM_MINIMIZEEND, eventCallback);
            locationHook = new WinEventHook(NativeMethods.EVENT_OBJECT_LOCATIONCHANGE, NativeMethods.EVENT_OBJECT_LOCATIONCHANGE, eventCallback);
            showHideHook = new WinEventHook(NativeMethods.EVENT_OBJECT_DESTROY, NativeMethods.EVENT_OBJECT_HIDE, eventCallback);
            cloakHook = new WinEventHook(NativeMethods.EVENT_OBJECT_CLOAKED, NativeMethods.EVENT_OBJECT_UNCLOAKED, eventCallback);

            sampler = new ColourSampler(delegate(int token, Color colour)
            {
                try
                {
                    button.BeginInvoke(new Action(delegate { OnColourSampled(token, colour); }));
                }
                catch (InvalidOperationException)
                {
                    // The button window is already gone: LazyPin is exiting.
                }
            });
            InstallGlobalHooks();

            timer = new System.Windows.Forms.Timer();
            timer.Interval = IdleIntervalMs;
            timer.Tick += delegate { Guarded("timer", OnTimer); };
            timer.Start();

            StartWatchdog();
            Guarded("start", RefreshNow);
        }

        private static Icon LoadAppIcon()
        {
            try
            {
                using (Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("LazyPin.ico"))
                {
                    if (stream != null) return new Icon(stream);
                }
            }
            catch (Exception ex)
            {
                Log.Error("icon", ex);
            }
            return (Icon)SystemIcons.Application.Clone();
        }

        private static void Guarded(string context, Action action)
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                Log.Error(context, ex);
            }
        }

        private static int Round(double value)
        {
            return (int)Math.Round(value);
        }

        private static IntPtr RootForegroundWindow()
        {
            IntPtr foreground = NativeMethods.GetForegroundWindow();
            if (foreground == IntPtr.Zero) return IntPtr.Zero;
            IntPtr root = NativeMethods.GetAncestor(foreground, NativeMethods.GA_ROOT);
            return root != IntPtr.Zero ? root : foreground;
        }

        private void InstallGlobalHooks()
        {
            bool ok = foregroundHook.Set(0);
            ok &= moveSizeHook.Set(0);
            ok &= minimizeHook.Set(0);
            if (!ok && !hookFailureLogged)
            {
                hookFailureLogged = true;
                Log.Info("Windows refused a window event hook; the timer keeps the button working and the hook is retried");
            }
        }

        private void SetProcessHooks(uint processId)
        {
            locationHook.Set(processId);
            showHideHook.Set(processId);
            cloakHook.Set(processId);
        }

        private void UnsetProcessHooks()
        {
            locationHook.Unset();
            showHideHook.Unset();
            cloakHook.Unset();
        }

        private void OnTimer()
        {
            Interlocked.Exchange(ref heartbeat, Stopwatch.GetTimestamp());
            if (shuttingDown) return;
            InstallGlobalHooks();
            RefreshNow();
        }

        private void OnWinEvent(IntPtr hook, uint eventType, IntPtr hwnd, int idObject, int idChild, uint eventThread, uint eventTime)
        {
            if (shuttingDown) return;
            try
            {
                switch (eventType)
                {
                    case NativeMethods.EVENT_OBJECT_LOCATIONCHANGE:
                        if (hwnd != IntPtr.Zero && hwnd == targetHwnd &&
                            idObject == NativeMethods.OBJID_WINDOW && idChild == NativeMethods.CHILDID_SELF)
                        {
                            FollowTarget();
                        }
                        break;
                    case NativeMethods.EVENT_SYSTEM_FOREGROUND:
                    case NativeMethods.EVENT_SYSTEM_MINIMIZESTART:
                    case NativeMethods.EVENT_SYSTEM_MINIMIZEEND:
                        RefreshNow();
                        break;
                    case NativeMethods.EVENT_SYSTEM_MOVESIZESTART:
                        moveSizeHwnd = hwnd;
                        break;
                    case NativeMethods.EVENT_SYSTEM_MOVESIZEEND:
                        moveSizeHwnd = IntPtr.Zero;
                        if (hwnd == targetHwnd)
                        {
                            // A resize drag is over: measure the final title-bar layout now.
                            probeTime = long.MinValue / 2;
                            RefreshNow();
                        }
                        break;
                    case NativeMethods.EVENT_OBJECT_DESTROY:
                    case NativeMethods.EVENT_OBJECT_SHOW:
                    case NativeMethods.EVENT_OBJECT_HIDE:
                    case NativeMethods.EVENT_OBJECT_CLOAKED:
                    case NativeMethods.EVENT_OBJECT_UNCLOAKED:
                        if (hwnd != IntPtr.Zero && hwnd == targetHwnd &&
                            idObject == NativeMethods.OBJID_WINDOW && idChild == NativeMethods.CHILDID_SELF)
                        {
                            RefreshNow();
                        }
                        break;
                }
            }
            catch (Exception ex)
            {
                // An exception must never travel back into Windows from this callback.
                Log.Error("window event", ex);
            }
        }

        // Follows a plain move of the target with one SetWindowPos, straight from the
        // LOCATIONCHANGE event, so the button stays locked to the title bar while dragging.
        private void FollowTarget()
        {
            if (refreshing)
            {
                refreshAgain = true;
                return;
            }
            if (!button.Visible) return;
            RECT rect;
            if (!NativeMethods.GetWindowRect(targetHwnd, out rect))
            {
                RefreshNow();
                return;
            }
            // A new size means maximise, restore, snap, resize or a move to a monitor with
            // another DPI: the layout itself changes, so measure everything again.
            if (rect.Width != trackWidth || rect.Height != trackHeight)
            {
                RefreshNow();
                return;
            }
            Rectangle bounds = new Rectangle(rect.Right + trackRightOffset, rect.Top + trackTopOffset, lastBounds.Width, lastBounds.Height);
            if (bounds != lastBounds) MoveButton(bounds);
        }

        private void RefreshNow()
        {
            if (shuttingDown) return;
            // Probing the title bar waits on the target, and Windows can deliver further
            // events during that wait. Run them as one more pass instead of nesting.
            if (refreshing)
            {
                refreshAgain = true;
                return;
            }
            refreshing = true;
            try
            {
                int passes = 0;
                do
                {
                    refreshAgain = false;
                    Refresh();
                }
                while (refreshAgain && ++passes < 3);
            }
            finally
            {
                refreshing = false;
            }
            UpdateTimerInterval();
        }

        private void Refresh()
        {
            long now = clock.ElapsedMilliseconds;
            IntPtr foreground = RootForegroundWindow();

            PrunePinned(now);

            // The button always sits on the focused window: a click pins it, or unpins it when
            // it is already on top. Any number of windows can be pinned this way. Shell surfaces
            // (taskbar, Start, Task View, desktop...) are never targets.
            IntPtr hwnd = foreground;
            if (!NativeMethods.IsEligibleTarget(hwnd, ownProcessId))
            {
                HideButton();
                return;
            }

            RECT windowRect;
            if (!NativeMethods.GetWindowRect(hwnd, out windowRect))
            {
                HideButton();
                return;
            }

            int dpi = NativeMethods.GetWindowDpi(hwnd);
            double scale = dpi / 96.0;
            long windowStyle = NativeMethods.GetStyle(hwnd);
            bool isMaximized = (windowStyle & NativeMethods.WS_MAXIMIZE) != 0;
            bool hasCaption = (windowStyle & NativeMethods.WS_CAPTION) != 0;

            bool sameFrame = hwnd == frameHwnd && dpi == frameDpi && isMaximized == frameMaximized;
            bool atRest = hwnd == restHwnd && SameRect(windowRect, restRect) && moveSizeHwnd != hwnd;
            restHwnd = hwnd;
            restRect = windowRect;
            if (!sameFrame || atRest) ReadFrame(hwnd, windowRect, dpi, isMaximized, scale, sameFrame);
            // A new window, DPI or maximised state may have been read while DWM was still
            // behind; look again shortly, once the window stands still.
            if (!sameFrame) RequestFastTimer(now + 300);

            RECT visibleRect = windowRect;
            visibleRect.Left += insetLeft;
            visibleRect.Top += insetTop;
            visibleRect.Right -= insetRight;
            visibleRect.Bottom -= insetBottom;
            bool hasDwmBounds = frameHasCaptionButtons;

            int buttonWidth = Round(46 * scale);
            int buttonHeight = Round(30 * scale);
            int captionHeight = Round(34 * scale);
            int topBase = visibleRect.Top;
            int topBorderInset = isMaximized ? 0 : Math.Max(1, Round(1.0 * scale));

            // A caption-less window that fills the monitor is a full-screen game, video or
            // F11 browser. Keeping a topmost button over it disables full-screen
            // optimisations and stutters that app, and it has no title bar to pin anyway.
            if (!hasCaption && !isMaximized && NativeMethods.CoversMonitor(hwnd, windowRect))
            {
                HideButton();
                return;
            }

            if (hasCaption)
            {
                POINT clientOrigin = new POINT();
                if (NativeMethods.ClientToScreen(hwnd, ref clientOrigin))
                {
                    int measuredCaption = clientOrigin.Y - windowRect.Top;
                    if (measuredCaption > 0 && measuredCaption < Round(72 * scale))
                    {
                        captionHeight = Math.Max(captionHeight, measuredCaption);
                    }
                }
            }

            int numButtons = 3;
            bool hasMaximizeBox = (windowStyle & NativeMethods.WS_MAXIMIZEBOX) != 0;
            bool hasMinimizeBox = (windowStyle & NativeMethods.WS_MINIMIZEBOX) != 0;
            if (!hasMaximizeBox && !hasMinimizeBox)
            {
                numButtons = 1;
            }
            else if (!hasMaximizeBox || !hasMinimizeBox)
            {
                numButtons = 2;
            }

            int baseControlsWidth = Round(46.0 * numButtons * scale);

            if (hasDwmBounds)
            {
                buttonHeight = Math.Max(24, captionButtonsHeight - topBorderInset);
                buttonWidth = Math.Max(32, Round(captionButtonsWidth / (double)numButtons));
                captionHeight = captionButtonsHeight;
                topBase = visibleRect.Top + topBorderInset;

                // Exact distance from the visible right edge to the leftmost native caption button.
                int dwmDistFromRight = visibleRect.Right - (windowRect.Right - captionRightDist);
                if (dwmDistFromRight > 0)
                {
                    baseControlsWidth = dwmDistFromRight;
                }
            }

            int controlsWidth = Math.Max(baseControlsWidth, GetControlsWidth(hwnd, visibleRect, captionHeight, scale, baseControlsWidth, dpi, now));

            // Automatically adapt the gap between buttons: directly beside native caption
            // buttons (e.g. File Explorer) the buttons are seamless tiles with 0 gap, giving
            // exact icon pitch alignment; beside extra custom icons (e.g. Chrome profile /
            // search icons) a subtle gap separates the pin from them.
            int gap;
            if (controlsWidth <= baseControlsWidth + Round(2 * scale))
            {
                gap = 0;
                controlsWidth = baseControlsWidth;
            }
            else
            {
                gap = Round(4 * scale);
            }

            int x = visibleRect.Right - controlsWidth - buttonWidth - gap;
            int y = topBase + (int)Math.Ceiling((captionHeight - topBorderInset - buttonHeight) / 2.0);
            if (y < visibleRect.Top + topBorderInset) y = visibleRect.Top + topBorderInset;
            if (x < visibleRect.Left) x = visibleRect.Left;
            Rectangle bounds = new Rectangle(x, y, buttonWidth, buttonHeight);

            long t1 = clock.ElapsedMilliseconds;
            SampleTitleBarColour(hwnd, visibleRect, x, y, buttonHeight, scale, now);
            long t2 = clock.ElapsedMilliseconds;

            targetHwnd = hwnd;
            button.SetDpi(dpi);
            UpdateButtonState(hwnd);
            long t3 = clock.ElapsedMilliseconds;
            ShowButton(bounds);
            long t4 = clock.ElapsedMilliseconds;
            if (Log.Tracing && t4 - now > 5) Log.Trace(string.Format("slow refresh target {0:X} fg {1:X}: probe {2} sample {3} state {4} show {5} ms", hwnd.ToInt64(), foreground.ToInt64(), t1 - now, t2 - t1, t3 - t2, t4 - t3));

            uint processId;
            NativeMethods.GetWindowThreadProcessId(hwnd, out processId);
            SetProcessHooks(processId);
            trackRightOffset = bounds.X - windowRect.Right;
            trackTopOffset = bounds.Y - windowRect.Top;
            trackWidth = windowRect.Width;
            trackHeight = windowRect.Height;
        }

        private static bool SameRect(RECT a, RECT b)
        {
            return a.Left == b.Left && a.Top == b.Top && a.Right == b.Right && a.Bottom == b.Bottom;
        }

        // Reads the visible frame and the caption buttons from DWM and keeps them relative to
        // the window rectangle (see the fields). Values that cannot belong to this rectangle
        // were read while DWM was still behind and are not used.
        private void ReadFrame(IntPtr hwnd, RECT windowRect, int dpi, bool isMaximized, double scale, bool sameFrame)
        {
            int limit = Round(32 * scale);
            RECT visible;
            bool insetsOk = false;
            if (NativeMethods.DwmGetWindowAttribute(hwnd, NativeMethods.DWMWA_EXTENDED_FRAME_BOUNDS, out visible, NativeMethods.RectSize) == 0)
            {
                int left = visible.Left - windowRect.Left;
                int top = visible.Top - windowRect.Top;
                int right = windowRect.Right - visible.Right;
                int bottom = windowRect.Bottom - visible.Bottom;
                if (InRange(left, limit) && InRange(top, limit) && InRange(right, limit) && InRange(bottom, limit))
                {
                    insetLeft = left;
                    insetTop = top;
                    insetRight = right;
                    insetBottom = bottom;
                    insetsOk = true;
                }
            }
            if (!insetsOk && !sameFrame)
            {
                // Nothing usable yet for this window: no invisible border, except that a
                // maximised window only shows the part on its monitor's work area.
                insetLeft = insetTop = insetRight = insetBottom = 0;
                RECT work;
                if (isMaximized && NativeMethods.GetMonitorWorkArea(hwnd, out work))
                {
                    insetLeft = Math.Max(0, work.Left - windowRect.Left);
                    insetTop = Math.Max(0, work.Top - windowRect.Top);
                    insetRight = Math.Max(0, windowRect.Right - work.Right);
                    insetBottom = Math.Max(0, windowRect.Bottom - work.Bottom);
                }
            }

            RECT caption;
            bool captionOk = NativeMethods.DwmGetWindowAttribute(hwnd, NativeMethods.DWMWA_CAPTION_BUTTON_BOUNDS, out caption, NativeMethods.RectSize) == 0 &&
                caption.Width > 0 && caption.Height > 0 && caption.Left >= 0 && caption.Left < windowRect.Width;
            int rightDist = windowRect.Width - caption.Left;
            if (captionOk && InRange(rightDist - caption.Width, limit))
            {
                frameHasCaptionButtons = true;
                captionRightDist = rightDist;
                captionButtonsWidth = caption.Width;
                captionButtonsHeight = caption.Height;
            }
            else if (!captionOk || !sameFrame)
            {
                frameHasCaptionButtons = false;
            }

            frameHwnd = hwnd;
            frameDpi = dpi;
            frameMaximized = isMaximized;
        }

        private static bool InRange(int value, int limit)
        {
            return value >= 0 && value <= limit;
        }

        private struct ProbeKey
        {
            private readonly IntPtr hwnd;
            private readonly int width;
            private readonly int captionHeight;
            private readonly int baseControlsWidth;
            private readonly int dpi;

            public ProbeKey(IntPtr hwnd, int width, int captionHeight, int baseControlsWidth, int dpi)
            {
                this.hwnd = hwnd;
                this.width = width;
                this.captionHeight = captionHeight;
                this.baseControlsWidth = baseControlsWidth;
                this.dpi = dpi;
            }

            public bool SameAs(ProbeKey other)
            {
                return hwnd == other.hwnd && width == other.width && captionHeight == other.captionHeight &&
                       baseControlsWidth == other.baseControlsWidth && dpi == other.dpi;
            }
        }

        private struct ProbeMemory
        {
            public ProbeKey Key;
            public int Value;
            public long Order;
        }

        private void RememberProbe(IntPtr hwnd, ProbeKey key, int value)
        {
            ProbeMemory entry;
            entry.Key = key;
            entry.Value = value;
            entry.Order = ++probeMemoryOrder;
            probeMemory[hwnd] = entry;
            if (probeMemory.Count <= ProbeMemorySize) return;
            // Forget the window that was measured longest ago.
            IntPtr oldest = IntPtr.Zero;
            long oldestOrder = long.MaxValue;
            foreach (KeyValuePair<IntPtr, ProbeMemory> pair in probeMemory)
            {
                if (pair.Value.Order < oldestOrder)
                {
                    oldestOrder = pair.Value.Order;
                    oldest = pair.Key;
                }
            }
            probeMemory.Remove(oldest);
        }

        private int GetControlsWidth(IntPtr hwnd, RECT visibleRect, int captionHeight, double scale, int baseControlsWidth, int dpi, long now)
        {
            ProbeKey key = new ProbeKey(hwnd, visibleRect.Width, captionHeight, baseControlsWidth, dpi);
            bool newWindow = hwnd != probeHwnd;
            bool layoutChanged = !newWindow && !key.SameAs(probeKey);
            long age = now - probeTime;
            bool due;
            if (newWindow) due = true;
            else if (layoutChanged) due = age >= ProbeMinIntervalMs;
            else if (probeRetry) due = age >= (probeIsGuess ? ProbeGuessRetryMs : ProbeRetryMs);
            // A plain move cannot change the title-bar layout.
            else due = age >= ProbeMaxAgeMs && moveSizeHwnd != hwnd;
            if (!due) return probeValue;

            int value = NativeMethods.DetectControlsWidth(hwnd, visibleRect, captionHeight, scale, baseControlsWidth);
            if (Log.Tracing) Log.Trace(string.Format("probe {0:X} value {1} base {2} new {3} layout {4} guess {5} current {6} candidate {7} retries {8}", hwnd.ToInt64(), value, baseControlsWidth, newWindow, layoutChanged, probeIsGuess, probeValue, probeCandidate, probeRetries));
            probeTime = now;
            if (newWindow)
            {
                // Coming back to a window seen before: start from what it answered last time.
                // Browsers are often too busy to answer right when they are activated, and the
                // button would otherwise flash over their title-bar icons.
                ProbeMemory known;
                probeHwnd = hwnd;
                if (probeMemory.TryGetValue(hwnd, out known) && known.Key.SameAs(key))
                {
                    probeValue = known.Value;
                    probeIsGuess = false;
                }
                else
                {
                    probeValue = baseControlsWidth;
                    probeIsGuess = true;
                }
                probeCandidate = -1;
                probeRetries = 0;
            }
            probeKey = key;

            if (value < 0)
            {
                // No answer in time (the app is busy) or no answer allowed (an administrator
                // window): keep the last known layout and ask again shortly, a few times.
                probeRetry = probeRetries < ProbeMaxRetries;
                if (probeRetry)
                {
                    probeRetries++;
                    RequestFastTimer(now + ProbeRetryMs + FastIntervalMs);
                }
                return probeValue;
            }

            if (layoutChanged || probeIsGuess || value == probeValue || value == probeCandidate || probeRetries >= ProbeMaxRetries)
            {
                probeValue = value;
                probeIsGuess = false;
                probeCandidate = -1;
                probeRetry = false;
                probeRetries = 0;
                RememberProbe(hwnd, key, value);
            }
            else
            {
                // Same window, same layout, a different answer: an app that is redrawing its
                // title bar can answer oddly once. Move the button only when a second probe agrees.
                probeCandidate = value;
                probeRetry = true;
                probeRetries++;
                RequestFastTimer(now + ProbeRetryMs + FastIntervalMs);
            }
            return probeValue;
        }

        private void SampleTitleBarColour(IntPtr hwnd, RECT visibleRect, int x, int y, int buttonHeight, double scale, long now)
        {
            IntPtr foregroundNow = NativeMethods.GetForegroundWindow();
            if (hwnd != sampleHwnd || foregroundNow != sampleForeground)
            {
                sampleHwnd = hwnd;
                sampleForeground = foregroundNow;
                sampleToken++;
                sampleBurstUntil = now + SampleBurstMs;
                RequestFastTimer(sampleBurstUntil);
            }
            if (now > sampleBurstUntil && now - sampleTime < SampleMaxAgeMs) return;
            if (now - sampleTime < SampleMinIntervalMs) return;
            // While the window is dragged the button trails it by a frame, and the pixel
            // beside the button's new place could then be the button itself.
            if (moveSizeHwnd == hwnd) return;
            sampleTime = now;

            int sampleX = Math.Max(visibleRect.Left + 10, x - Round(4 * scale));
            int sampleY = y + buttonHeight / 2;
            sampler.Request(sampleX, sampleY, sampleToken);
        }

        private void OnColourSampled(int token, Color colour)
        {
            // A late answer about the previous window must not colour the button on this one.
            if (shuttingDown || token != sampleToken) return;
            ApplyBackground(colour);
        }

        private void ApplyBackground(Color sampled)
        {
            if (sampled.ToArgb() == background.ToArgb()) return;
            background = sampled;
            double luminance = 0.2126 * sampled.R + 0.7152 * sampled.G + 0.0722 * sampled.B;
            Color icon;
            Color pinnedIcon;
            if (luminance < 128)
            {
                icon = Color.FromArgb(245, 245, 245);
                pinnedIcon = Color.FromArgb(96, 205, 255);
            }
            else
            {
                icon = Color.FromArgb(32, 32, 32);
                pinnedIcon = Color.FromArgb(0, 95, 184);
            }
            button.SetPalette(sampled, icon, pinnedIcon, Mix(sampled, icon, 0.16), Mix(sampled, icon, 0.28));
        }

        private static Color Mix(Color from, Color to, double amount)
        {
            return Color.FromArgb(
                Round(from.R + (to.R - from.R) * amount),
                Round(from.G + (to.G - from.G) * amount),
                Round(from.B + (to.B - from.B) * amount));
        }

        // Windows (UIPI) does not let a normal program change the z-order of an app that
        // runs as administrator, so the button is drawn dimmed there and explains why.
        private bool IsBlocked(IntPtr hwnd)
        {
            if (hwnd != integrityHwnd)
            {
                integrityHwnd = hwnd;
                uint processId;
                NativeMethods.GetWindowThreadProcessId(hwnd, out processId);
                int level = NativeMethods.GetProcessIntegrityLevel(processId);
                integrityBlocked = ownIntegrity >= 0 && level > ownIntegrity;
            }
            return integrityBlocked;
        }

        private void UpdateButtonState(IntPtr hwnd)
        {
            bool pinnedNow = NativeMethods.IsTopmost(hwnd);
            bool blockedNow = IsBlocked(hwnd);
            if (pinnedNow == isPinned && blockedNow == isBlocked && hwnd == stateHwnd) return;
            isPinned = pinnedNow;
            isBlocked = blockedNow;
            stateHwnd = hwnd;
            button.SetState(pinnedNow, blockedNow);
            tooltip.SetToolTip(button, blockedNow ? TipBlocked : pinnedNow ? TipUnpin : TipPin);
        }

        private void ShowButton(Rectangle bounds)
        {
            if (!button.Visible)
            {
                // Move the hidden window first so its very first frame already sits beside
                // the caption buttons instead of flashing at its previous position.
                button.Bounds = bounds;
                button.Show();
                lastBounds = Rectangle.Empty;
            }
            if (bounds != lastBounds)
            {
                MoveButton(bounds);
            }
            else if (!NativeMethods.IsTopmost(button.Handle) || NativeMethods.IsWindowAbove(targetHwnd, button.Handle))
            {
                NativeMethods.SetWindowPos(
                    button.Handle,
                    NativeMethods.HWND_TOPMOST,
                    0,
                    0,
                    0,
                    0,
                    NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOACTIVATE);
            }
        }

        private void MoveButton(Rectangle bounds)
        {
            // Making the target topmost (or activating a pinned window) moves it above the
            // button. Change the z-order only when that actually happened: every z-order
            // change makes DWM recompose the desktop.
            uint flags = NativeMethods.SWP_NOACTIVATE;
            if (NativeMethods.IsTopmost(button.Handle) && !NativeMethods.IsWindowAbove(targetHwnd, button.Handle))
            {
                flags |= NativeMethods.SWP_NOZORDER;
            }
            NativeMethods.SetWindowPos(button.Handle, NativeMethods.HWND_TOPMOST, bounds.X, bounds.Y, bounds.Width, bounds.Height, flags);
            if (Log.Tracing) Log.Trace("move " + bounds + (refreshing ? " (refresh)" : " (follow)") + " target " + targetHwnd.ToInt64().ToString("X"));
            lastBounds = bounds;
        }

        private void HideButton()
        {
            if (button.Visible) button.Hide();
            targetHwnd = IntPtr.Zero;
            UnsetProcessHooks();
        }

        private void RequestFastTimer(long until)
        {
            if (until > fastUntil) fastUntil = until;
        }

        private void UpdateTimerInterval()
        {
            if (shuttingDown) return;
            int interval;
            if (clock.ElapsedMilliseconds < fastUntil) interval = FastIntervalMs;
            else if (button.Visible) interval = VisibleIntervalMs;
            else interval = IdleIntervalMs;
            if (timer.Interval != interval) timer.Interval = interval;
        }

        private void OnButtonClicked()
        {
            IntPtr hwnd = targetHwnd;
            if (Log.Tracing) Log.Trace(string.Format("click target {0:X} foreground {1:X} pinned windows {2} eligible {3}", hwnd.ToInt64(), RootForegroundWindow().ToInt64(), pinnedWindows.Count, NativeMethods.IsEligibleTarget(hwnd, ownProcessId)));
            // Never touch the z-order of a shell window: making the taskbar NOTOPMOST would hide it.
            if (!NativeMethods.IsEligibleTarget(hwnd, ownProcessId)) return;
            if (IsBlocked(hwnd))
            {
                ShowBlockedHint();
                return;
            }
            if (NativeMethods.IsHungAppWindow(hwnd)) return;
            // The focus may have moved on between the click and now.
            if (RootForegroundWindow() != hwnd) return;

            // SetWindowPos on a foreign window waits until that app has handled it. For an app
            // that is busy right now, queue the request instead (asynchronous), so LazyPin does
            // not freeze with it.
            bool busy = !NativeMethods.Responds(hwnd, 100);
            uint flags = NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOACTIVATE;
            if (busy) flags |= NativeMethods.SWP_ASYNCWINDOWPOS;

            bool alreadyTopmost = NativeMethods.IsTopmost(hwnd);
            bool ok = NativeMethods.SetWindowPos(
                hwnd,
                alreadyTopmost ? NativeMethods.HWND_NOTOPMOST : NativeMethods.HWND_TOPMOST,
                0,
                0,
                0,
                0,
                flags);
            if (!ok)
            {
                int error = Marshal.GetLastWin32Error();
                if (error == NativeMethods.ERROR_ACCESS_DENIED)
                {
                    integrityHwnd = hwnd;
                    integrityBlocked = true;
                    UpdateButtonState(hwnd);
                    ShowBlockedHint();
                }
                else
                {
                    Log.Info("could not change the z-order of a window (Windows error " + error + ")");
                }
                return;
            }

            if (!alreadyTopmost)
            {
                if (!pinnedWindows.Contains(hwnd)) pinnedWindows.Add(hwnd);
                if (busy) pendingPins[hwnd] = clock.ElapsedMilliseconds + PendingPinMs;
            }
            else
            {
                // Also an always-on-top window LazyPin did not pin: the button unpins it too.
                pinnedWindows.Remove(hwnd);
                pendingPins.Remove(hwnd);
            }
            pinnedSnapshot = pinnedWindows.ToArray();
            // Show the new state at once and put the button back above the (now topmost) target.
            RefreshNow();
        }

        // Drops windows that are gone or that something else un-topped. A busy app gets time
        // to carry out a queued pin request first.
        private void PrunePinned(long now)
        {
            bool changed = false;
            for (int i = pinnedWindows.Count - 1; i >= 0; i--)
            {
                IntPtr hwnd = pinnedWindows[i];
                if (NativeMethods.IsWindow(hwnd))
                {
                    if (NativeMethods.IsTopmost(hwnd))
                    {
                        pendingPins.Remove(hwnd);
                        continue;
                    }
                    long pendingUntil;
                    if (pendingPins.TryGetValue(hwnd, out pendingUntil) && now < pendingUntil) continue;
                }
                pinnedWindows.RemoveAt(i);
                pendingPins.Remove(hwnd);
                changed = true;
            }
            if (changed) pinnedSnapshot = pinnedWindows.ToArray();
        }

        // Gives every window LazyPin pinned its normal z-order back. Asynchronous: an app that
        // is busy carries it out when it can, and LazyPin never waits for it (a queued pin
        // that has not happened yet is overtaken in the same order).
        private int UnpinAll()
        {
            int count = 0;
            foreach (IntPtr hwnd in pinnedWindows)
            {
                if (!NativeMethods.IsWindow(hwnd)) continue;
                if (NativeMethods.SetWindowPos(
                    hwnd,
                    NativeMethods.HWND_NOTOPMOST,
                    0,
                    0,
                    0,
                    0,
                    NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOACTIVATE | NativeMethods.SWP_ASYNCWINDOWPOS))
                {
                    count++;
                }
            }
            pinnedWindows.Clear();
            pendingPins.Clear();
            pinnedSnapshot = new IntPtr[0];
            return count;
        }

        private void OnUnpinAll()
        {
            int count = UnpinAll();
            Log.Trace("unpin all: " + count + " window(s)");
            // The requests are queued; look again quickly so the button shows the new state.
            RequestFastTimer(clock.ElapsedMilliseconds + 300);
            RefreshNow();
        }

        private void ShowBlockedHint()
        {
            tooltip.Show(TipBlocked, button, 0, button.Height + 4, 4000);
        }

        private void OnSystemChanged()
        {
            // Display layout, DPI, theme or accent colour changed: measure everything again.
            probeHwnd = IntPtr.Zero;
            frameHwnd = IntPtr.Zero;
            sampleHwnd = IntPtr.Zero;
            lastBounds = Rectangle.Empty;
            RefreshNow();
        }

        private void OnResumed()
        {
            Log.Info("resumed from sleep");
            // Nothing should be lost over sleep, but installing the hooks afresh costs nothing.
            foregroundHook.Unset();
            moveSizeHook.Unset();
            minimizeHook.Unset();
            UnsetProcessHooks();
            InstallGlobalHooks();
            OnSystemChanged();
        }

        private void ToggleStartup()
        {
            try
            {
                if (File.Exists(startupLink))
                {
                    File.Delete(startupLink);
                }
                else
                {
                    string exe = Application.ExecutablePath;
                    ShellLink.Create(startupLink, exe, Path.GetDirectoryName(exe), "Keep any window always on top with LazyPin");
                }
            }
            catch (Exception ex)
            {
                Log.Error("startup shortcut", ex);
                MessageBox.Show(
                    "LazyPin could not change the startup shortcut.\n\n" + ex.Message,
                    "LazyPin",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
            }
            startupItem.Checked = File.Exists(startupLink);
        }

        private static void ShowAbout()
        {
            MessageBox.Show(
                "LazyPin v" + AppInfo.Version + "\n\nDeveloper: Raisul Sohan\nGitHub: " + AppInfo.RepositoryUrl +
                "\n\nA lightweight utility to keep any window always on top.",
                "About LazyPin",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        }

        // LazyPin's UI thread talks to other apps' windows. If it ever stops running (stuck on
        // an app that hung while LazyPin waited for it, or a bug), the button and the tray icon
        // would freeze until the next sign-in. The watchdog notices that the timer has stopped
        // ticking, starts a fresh copy and ends this one; the fresh copy takes over at once.
        private void StartWatchdog()
        {
            Thread thread = new Thread(WatchdogLoop);
            thread.IsBackground = true;
            thread.Name = "LazyPin watchdog";
            thread.Priority = ThreadPriority.BelowNormal;
            thread.Start();
        }

        private void WatchdogLoop()
        {
            try
            {
                while (!watchdogStop.WaitOne(WatchdogCheckMs))
                {
                    if (SilentMs() < WatchdogTimeoutMs) continue;
                    // Look again shortly: right after the PC wakes from sleep the clock jumps
                    // ahead before the first tick has had a chance to run.
                    if (watchdogStop.WaitOne(3000)) return;
                    long silent = SilentMs();
                    if (silent < WatchdogTimeoutMs) continue;

                    Log.Info("the UI thread has not run for " + (silent / 1000) + " s; starting a fresh copy to take over");
                    string arguments = "--takeover";
                    IntPtr[] pinned = pinnedSnapshot;
                    if (pinned.Length > 0)
                    {
                        arguments += " " + Program.PinnedArgument +
                            string.Join(",", Array.ConvertAll(pinned, delegate(IntPtr h) { return h.ToInt64().ToString("X", CultureInfo.InvariantCulture); }));
                    }
                    ProcessStartInfo start = new ProcessStartInfo(Application.ExecutablePath, arguments);
                    start.UseShellExecute = false;
                    start.WorkingDirectory = Path.GetDirectoryName(Application.ExecutablePath);
                    Process.Start(start);
                    Log.Trace("fresh copy started; ending this one");
                    Process.GetCurrentProcess().Kill();
                    return;
                }
            }
            catch (Exception ex)
            {
                Log.Error("watchdog", ex);
            }
        }

        private long SilentMs()
        {
            long ticks = Stopwatch.GetTimestamp() - Interlocked.Read(ref heartbeat);
            return ticks * 1000 / Stopwatch.Frequency;
        }

        // One exit path for the tray menu, Windows sign-out/shutdown and installer or
        // "taskkill" close requests.
        private void Exit()
        {
            if (shuttingDown) return;
            shuttingDown = true;
            watchdogStop.Set();
            try
            {
                // Give the pinned windows their normal z-order back: once LazyPin is gone there
                // is no button left to unpin them with.
                UnpinAll();
            }
            catch (Exception ex)
            {
                Log.Error("exit", ex);
            }
            timer.Stop();
            sampler.Stop();
            foregroundHook.Unset();
            moveSizeHook.Unset();
            minimizeHook.Unset();
            UnsetProcessHooks();
            try
            {
                if (button.Visible) button.Hide();
            }
            catch (Exception ex)
            {
                Log.Error("exit", ex);
            }
            // A killed process leaves a ghost tray icon until the mouse passes over it.
            trayIcon.Visible = false;
            ExitThread();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                shuttingDown = true;
                watchdogStop.Set();
                sampler.Stop();
                timer.Dispose();
                foregroundHook.Unset();
                moveSizeHook.Unset();
                minimizeHook.Unset();
                UnsetProcessHooks();
                trayIcon.Visible = false;
                trayIcon.Dispose();
                trayMenu.Dispose();
                tooltip.Dispose();
                button.Dispose();
                trayImage.Dispose();
                appIcon.Dispose();
            }
            base.Dispose(disposing);
        }
    }

    // Creates a .lnk shortcut through the Windows Script Host COM object.
    internal static class ShellLink
    {
        public static void Create(string linkPath, string targetPath, string workingDirectory, string description)
        {
            Type shellType = Type.GetTypeFromProgID("WScript.Shell", true);
            object shell = Activator.CreateInstance(shellType);
            try
            {
                object link = shellType.InvokeMember("CreateShortcut", BindingFlags.InvokeMethod, null, shell, new object[] { linkPath });
                try
                {
                    Type linkType = link.GetType();
                    linkType.InvokeMember("TargetPath", BindingFlags.SetProperty, null, link, new object[] { targetPath });
                    linkType.InvokeMember("Arguments", BindingFlags.SetProperty, null, link, new object[] { string.Empty });
                    linkType.InvokeMember("WorkingDirectory", BindingFlags.SetProperty, null, link, new object[] { workingDirectory });
                    linkType.InvokeMember("Description", BindingFlags.SetProperty, null, link, new object[] { description });
                    linkType.InvokeMember("Save", BindingFlags.InvokeMethod, null, link, null);
                }
                finally
                {
                    Marshal.ReleaseComObject(link);
                }
            }
            finally
            {
                Marshal.ReleaseComObject(shell);
            }
        }
    }
}
