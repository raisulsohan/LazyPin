<#
.SYNOPSIS
    LazyPin - Keep any window always on top with a seamless native title-bar pin button.

.DESCRIPTION
    A lightweight Windows utility for Windows 10 & 11 that places an always-on-top toggle
    pin button seamlessly beside the minimize button on active windows.

.AUTHOR
    Raisul Sohan (https://github.com/raisulsohan)

.LINK
    https://github.com/raisulsohan/LazyPin

.COPYRIGHT
    Copyright (c) 2026 Raisul Sohan. All rights reserved.
#>

$ErrorActionPreference = 'Stop'
$script:scriptPath = $PSCommandPath

Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing

$script:executablePath = [System.Diagnostics.Process]::GetCurrentProcess().MainModule.FileName
$script:applicationDirectory = Split-Path -Parent $script:executablePath
if ([System.IO.Path]::GetFileNameWithoutExtension($script:executablePath) -match '^(powershell|pwsh)$' -and $script:scriptPath) {
    $script:applicationDirectory = Split-Path -Parent $script:scriptPath
}
$script:iconPath = Join-Path $script:applicationDirectory 'LazyPin.ico'
$script:applicationIcon = [System.Drawing.SystemIcons]::Application
if (Test-Path -LiteralPath $script:iconPath) {
    try { $script:applicationIcon = [System.Drawing.Icon]::new($script:iconPath) } catch { }
}

$nativeCode = @'
using System;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace LazyPin
{
    [StructLayout(LayoutKind.Sequential)]
    public struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
        public int Width { get { return Right - Left; } }
        public int Height { get { return Bottom - Top; } }
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct POINT
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct MONITORINFO
    {
        public int Size;
        public RECT Monitor;
        public RECT Work;
        public uint Flags;
    }

    public static class NativeMethods
    {
        private const uint EVENT_OBJECT_LOCATIONCHANGE = 0x800B;
        private const uint WINEVENT_OUTOFCONTEXT = 0x0000;
        private const int OBJID_WINDOW = 0;
        private const int WM_APP_LOCATION_CHANGED = 0x8001;
        private const uint GW_HWNDPREV = 3;
        private const uint MONITOR_DEFAULTTONEAREST = 2;
        private static int locationMessagePending;
        private static IntPtr locationHook = IntPtr.Zero;
        private static uint hookedProcessId;
        private static IntPtr trackedWindow = IntPtr.Zero;
        private static IntPtr trackedOverlay = IntPtr.Zero;
        private static int overlayRightOffset;
        private static int overlayTopOffset;
        private static int overlayWidth;
        private static int overlayHeight;
        private static int lastTrackedX = int.MinValue;
        private static int lastTrackedY = int.MinValue;
        private static readonly WinEventProc locationCallback = OnLocationChanged;

        private delegate void WinEventProc(IntPtr hook, uint eventType, IntPtr hwnd, int idObject, int idChild, uint eventThread, uint eventTime);

        public static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);
        public static readonly IntPtr HWND_NOTOPMOST = new IntPtr(-2);

        public const int GWL_EXSTYLE = -20;
        public const int GWL_STYLE = -16;
        public const long WS_EX_TOPMOST = 0x00000008L;
        public const long WS_CAPTION = 0x00C00000L;
        public const long WS_EX_TOOLWINDOW = 0x00000080L;
        public const int DWMWA_CLOAKED = 14;
        public const uint SWP_NOSIZE = 0x0001;
        public const uint SWP_NOMOVE = 0x0002;
        public const uint SWP_NOACTIVATE = 0x0010;
        public const uint SWP_SHOWWINDOW = 0x0040;
        public const uint SWP_ASYNCWINDOWPOS = 0x4000;
        public const uint GA_ROOT = 2;
        public const int DWMWA_CAPTION_BUTTON_BOUNDS = 5;
        public const int DWMWA_EXTENDED_FRAME_BOUNDS = 9;
        public const uint WM_NCHITTEST = 0x0084;
        public const uint SMTO_ABORTIFHUNG = 0x0002;

        [DllImport("user32.dll", SetLastError = true)]
        public static extern IntPtr SendMessageTimeout(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam, uint fuFlags, uint uTimeout, out IntPtr lpdwResult);

        public static int DetectControlsWidth(IntPtr hwnd, RECT visibleRect, int captionHeight, double scale, int baseControlsWidth)
        {
            // Never queue synchronous messages on a thread that is already not responding:
            // every timed-out WM_NCHITTEST still stays in that thread's queue.
            if (IsHungAppWindow(hwnd)) return baseControlsWidth;

            int y = visibleRect.Top + Math.Max(12, captionHeight / 2);

            // Step 1: Verify the window actually implements non-client caption hit testing.
            // The top-right area (where the Close button sits) must return HTCLOSE (20), HTMAXBUTTON (9), HTMINBUTTON (8), or HTCAPTION (2).
            // If it returns HTCLIENT (1), the window treats the entire title bar as client area (e.g. Win11 File Explorer, XAML Islands).
            // In that case, NCHITTEST cannot distinguish titlebar controls from client space, so keep baseControlsWidth.
            int closeTestX = visibleRect.Right - (int)Math.Round(20.0 * scale);
            IntPtr closeParam = new IntPtr((y << 16) | (closeTestX & 0xFFFF));
            IntPtr closeResult;
            IntPtr closeRes = SendMessageTimeout(hwnd, WM_NCHITTEST, IntPtr.Zero, closeParam, SMTO_ABORTIFHUNG, 20, out closeResult);
            if (closeRes == IntPtr.Zero) return baseControlsWidth;
            int closeHit = closeResult.ToInt32();
            if (closeHit != 20 && closeHit != 9 && closeHit != 8 && closeHit != 2)
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
                int x = visibleRect.Right - dist;
                IntPtr lParam = new IntPtr((y << 16) | (x & 0xFFFF));
                IntPtr result;
                IntPtr res = SendMessageTimeout(hwnd, WM_NCHITTEST, IntPtr.Zero, lParam, SMTO_ABORTIFHUNG, 20, out result);
                if (res == IntPtr.Zero) break;

                int hit = result.ToInt32();
                // Controls include custom client controls (1), object (19), menus (3, 5), and native buttons (8, 9, 20, 21)
                bool isControl = (hit == 1 || hit == 19 || hit == 3 || hit == 5 || hit == 8 || hit == 9 || hit == 20 || hit == 21);

                if (isControl)
                {
                    lastControlDist = dist;
                    consecutiveCaption = 0;
                }
                else if (hit == 2 || hit == 0)
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

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetMonitorInfo(IntPtr monitor, ref MONITORINFO info);

        // True when the target sits above the overlay in the z-order, i.e. the overlay
        // actually needs to be promoted again. Walking GW_HWNDPREV never sends messages.
        public static bool IsTargetAboveOverlay(IntPtr target, IntPtr overlay)
        {
            if (target == IntPtr.Zero || overlay == IntPtr.Zero) return false;
            IntPtr current = GetWindow(overlay, GW_HWNDPREV);
            int guard = 0;
            while (current != IntPtr.Zero && guard++ < 512)
            {
                if (current == target) return true;
                current = GetWindow(current, GW_HWNDPREV);
            }
            return false;
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

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool GetWindowRect(IntPtr hwnd, out RECT rect);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool GetClientRect(IntPtr hwnd, out RECT rect);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool ClientToScreen(IntPtr hwnd, ref POINT point);

        [DllImport("user32.dll")]
        public static extern uint GetDpiForWindow(IntPtr hwnd);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool SetWindowPos(IntPtr hwnd, IntPtr insertAfter, int x, int y, int cx, int cy, uint flags);

        [DllImport("user32.dll")]
        public static extern IntPtr GetDC(IntPtr hwnd);

        [DllImport("user32.dll")]
        public static extern int ReleaseDC(IntPtr hwnd, IntPtr dc);

        [DllImport("gdi32.dll")]
        public static extern uint GetPixel(IntPtr dc, int x, int y);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr SetWinEventHook(uint eventMin, uint eventMax, IntPtr module, WinEventProc callback, uint processId, uint threadId, uint flags);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool UnhookWinEvent(IntPtr hook);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool PostMessage(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam);

        public static bool TrackWindowLocation(IntPtr target, IntPtr overlay, int rightOffset, int topOffset, int width, int height)
        {
            bool targetOrOffsetsChanged = (trackedWindow != target || trackedOverlay != overlay ||
                                           overlayRightOffset != rightOffset || overlayTopOffset != topOffset ||
                                           overlayWidth != width || overlayHeight != height);
            trackedWindow = target;
            trackedOverlay = overlay;
            overlayRightOffset = rightOffset;
            overlayTopOffset = topOffset;
            overlayWidth = width;
            overlayHeight = height;
            if (targetOrOffsetsChanged)
            {
                lastTrackedX = int.MinValue;
                lastTrackedY = int.MinValue;
            }
            if (target == IntPtr.Zero)
            {
                if (locationHook != IntPtr.Zero)
                {
                    UnhookWinEvent(locationHook);
                    locationHook = IntPtr.Zero;
                    hookedProcessId = 0;
                }
                return true;
            }

            // Hook only the target's process. A system-wide LOCATIONCHANGE hook
            // delivers every cursor and window move on the machine to this thread.
            uint processId;
            GetWindowThreadProcessId(target, out processId);
            if (locationHook != IntPtr.Zero && hookedProcessId != processId)
            {
                UnhookWinEvent(locationHook);
                locationHook = IntPtr.Zero;
                hookedProcessId = 0;
            }
            if (locationHook == IntPtr.Zero)
            {
                locationHook = SetWinEventHook(
                    EVENT_OBJECT_LOCATIONCHANGE,
                    EVENT_OBJECT_LOCATIONCHANGE,
                    IntPtr.Zero,
                    locationCallback,
                    processId,
                    0,
                    WINEVENT_OUTOFCONTEXT
                );
                hookedProcessId = (locationHook != IntPtr.Zero) ? processId : 0;
            }
            return locationHook != IntPtr.Zero;
        }

        public static void MoveTrackedOverlay()
        {
            Interlocked.Exchange(ref locationMessagePending, 0);
            if (trackedWindow == IntPtr.Zero || trackedOverlay == IntPtr.Zero || !IsWindow(trackedWindow) || IsIconic(trackedWindow)) return;
            RECT rect;
            if (!GetWindowRect(trackedWindow, out rect)) return;
            int newX = rect.Right + overlayRightOffset;
            int newY = rect.Top + overlayTopOffset;
            if (newX == lastTrackedX && newY == lastTrackedY) return;
            lastTrackedX = newX;
            lastTrackedY = newY;

            SetWindowPos(
                trackedOverlay,
                HWND_TOPMOST,
                newX,
                newY,
                0,
                0,
                SWP_NOACTIVATE | SWP_SHOWWINDOW | SWP_NOSIZE
            );
        }

        public static void StopWindowLocationTracking()
        {
            trackedWindow = IntPtr.Zero;
            trackedOverlay = IntPtr.Zero;
            lastTrackedX = int.MinValue;
            lastTrackedY = int.MinValue;
            Interlocked.Exchange(ref locationMessagePending, 0);
            if (locationHook != IntPtr.Zero)
            {
                UnhookWinEvent(locationHook);
                locationHook = IntPtr.Zero;
                hookedProcessId = 0;
            }
        }

        private static void OnLocationChanged(IntPtr hook, uint eventType, IntPtr hwnd, int idObject, int idChild, uint eventThread, uint eventTime)
        {
            if (eventType == EVENT_OBJECT_LOCATIONCHANGE &&
                idObject == OBJID_WINDOW &&
                idChild == 0 &&
                hwnd == trackedWindow &&
                trackedOverlay != IntPtr.Zero)
            {
                MoveTrackedOverlay();
            }
        }

        [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW", SetLastError = true)]
        private static extern IntPtr GetWindowLongPtr64(IntPtr hwnd, int index);

        [DllImport("user32.dll", EntryPoint = "GetWindowLongW", SetLastError = true)]
        private static extern int GetWindowLong32(IntPtr hwnd, int index);

        public static IntPtr GetWindowLongPtr(IntPtr hwnd, int index)
        {
            return IntPtr.Size == 8
                ? GetWindowLongPtr64(hwnd, index)
                : new IntPtr(GetWindowLong32(hwnd, index));
        }

        [DllImport("user32.dll")]
        public static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint processId);

        [DllImport("dwmapi.dll")]
        public static extern int DwmGetWindowAttribute(IntPtr hwnd, int attribute, out RECT value, int valueSize);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool SetProcessDpiAwarenessContext(IntPtr value);

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

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern int GetClassName(IntPtr hwnd, StringBuilder buffer, int maxCount);

        [DllImport("dwmapi.dll", EntryPoint = "DwmGetWindowAttribute")]
        private static extern int DwmGetWindowAttributeInt(IntPtr hwnd, int attribute, out int value, int valueSize);

        public static string GetWindowClass(IntPtr hwnd)
        {
            if (hwnd == IntPtr.Zero) return string.Empty;
            StringBuilder buffer = new StringBuilder(256);
            int length = GetClassName(hwnd, buffer, buffer.Capacity);
            return length > 0 ? buffer.ToString(0, length) : string.Empty;
        }

        public static bool IsTopmost(IntPtr hwnd)
        {
            return (GetWindowLongPtr(hwnd, GWL_EXSTYLE).ToInt64() & WS_EX_TOPMOST) != 0;
        }

        // Cloaked windows exist and pass IsWindowVisible, but DWM does not draw them:
        // windows on another virtual desktop and suspended UWP apps. Nothing to pin to.
        public static bool IsCloaked(IntPtr hwnd)
        {
            int cloaked;
            int hr = DwmGetWindowAttributeInt(hwnd, DWMWA_CLOAKED, out cloaked, sizeof(int));
            return hr == 0 && cloaked != 0;
        }

        public static bool IsShellOrSystemWindow(IntPtr hwnd)
        {
            if (hwnd == IntPtr.Zero) return true;
            long exStyle = GetWindowLongPtr(hwnd, GWL_EXSTYLE).ToInt64();
            // Tool windows (floating palettes, flyouts, the taskbar itself) have no caption buttons.
            if ((exStyle & WS_EX_TOOLWINDOW) != 0) return true;
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
    }

    public class OverlayWindow : Form
    {
        public OverlayWindow()
        {
            this.DoubleBuffered = true;
            this.SetStyle(
                ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.AllPaintingInWmPaint |
                ControlStyles.UserPaint,
                true);
            this.UpdateStyles();
        }

        protected override bool ShowWithoutActivation { get { return true; } }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ExStyle |= 0x08000000 | 0x00000080; // WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW
                return cp;
            }
        }

        // Raised when Windows, the installer (Restart Manager) or "taskkill" without /F
        // asks this process to close. The script shuts the whole tool down in response.
        public event EventHandler ExitRequested;

        private const int WM_CLOSE = 0x0010;
        private const int WM_ENDSESSION = 0x0016;

        private void RaiseExitRequested()
        {
            EventHandler handler = ExitRequested;
            if (handler != null) handler(this, EventArgs.Empty);
        }

        protected override void WndProc(ref Message message)
        {
            if (message.Msg == 0x8001)
            {
                NativeMethods.MoveTrackedOverlay();
            }
            else if (message.Msg == WM_CLOSE)
            {
                // Closing only this form would leave a tray icon with no button behind it.
                RaiseExitRequested();
                return;
            }
            else if (message.Msg == WM_ENDSESSION && message.WParam != IntPtr.Zero)
            {
                RaiseExitRequested();
            }
            base.WndProc(ref message);
        }
    }
}
'@

$formsAssemblyPath = [System.Windows.Forms.Form].Assembly.Location
Add-Type -TypeDefinition $nativeCode -ReferencedAssemblies $formsAssemblyPath

try {
    # Per-monitor positioning keeps the overlay aligned on mixed-DPI displays.
    [void][LazyPin.NativeMethods]::SetProcessDpiAwarenessContext([IntPtr](-4))
} catch {
    # Older Windows versions may not expose this API.
}

$script:mutex = $null
$createdNew = $false
$sid = [System.Security.Principal.WindowsIdentity]::GetCurrent().User.Value
$mutexName = "Local\LazyPin-$sid"
$script:mutex = [System.Threading.Mutex]::new($true, $mutexName, [ref]$createdNew)
if (-not $createdNew) {
    $script:mutex.Dispose()
    exit
}

$script:targetHwnd = [IntPtr]::Zero
$script:pinnedHwnd = [IntPtr]::Zero
$script:stateHwnd = [IntPtr]::Zero
$script:lastBounds = [System.Drawing.Rectangle]::Empty
$script:buttonGap = 6
$script:isPinned = $false
$script:isHovered = $false
$script:isPressed = $false
$script:shuttingDown = $false
$script:chromeBackground = [System.Drawing.Color]::FromArgb(32, 32, 32)
$script:chromeIcon = [System.Drawing.Color]::FromArgb(245, 245, 245)
$script:pinnedIcon = [System.Drawing.Color]::FromArgb(96, 205, 255)
$script:hoverBackground = [System.Drawing.Color]::FromArgb(64, 64, 64)
$script:pressedBackground = [System.Drawing.Color]::FromArgb(79, 79, 79)
$script:buttonHeight = 30
$script:buttonWidth = 46

# Throttling state. The title-bar probe (WM_NCHITTEST) and the screen pixel read
# are the two expensive, foreign-process-touching operations; both are cached
# and refreshed only when the target/layout changes or on a slow schedule.
$script:clock = [System.Diagnostics.Stopwatch]::StartNew()
$script:controlsCacheHwnd = [IntPtr]::Zero
$script:controlsCacheKey = ''
$script:controlsCacheValue = 0
$script:controlsProbeTime = [long]-100000
$script:controlsProbeMinIntervalMs = 100
$script:controlsProbeMaxAgeMs = 1000
$script:sampleHwnd = [IntPtr]::Zero
$script:sampleForeground = [IntPtr]::Zero
$script:sampleTime = [long]-100000
$script:sampleBurstUntil = [long]0
$script:sampleBurstMs = 600
$script:sampleMaxAgeMs = 1000

$overlay = [LazyPin.OverlayWindow]::new()
$overlay.FormBorderStyle = [System.Windows.Forms.FormBorderStyle]::None
$overlay.ShowInTaskbar = $false
$overlay.Icon = $script:applicationIcon
$overlay.TopMost = $true
$overlay.StartPosition = [System.Windows.Forms.FormStartPosition]::Manual
$overlay.Size = [System.Drawing.Size]::new($script:buttonWidth, $script:buttonHeight)
$overlay.BackColor = $script:chromeBackground
$overlay.Cursor = [System.Windows.Forms.Cursors]::Hand
$overlay.Visible = $false
# A borderless window never shows its caption text; the fixed title lets the installer
# and uninstaller find this window (FindWindow) and ask the tool to exit cleanly
# with WM_CLOSE. Create the hidden window now so that works before the button has
# ever been shown.
$overlay.Text = 'LazyPin.PinButton'
[void]$overlay.Handle
$pinFont = [System.Drawing.Font]::new(
    'Segoe MDL2 Assets',
    [single]11,
    [System.Drawing.FontStyle]::Regular,
    [System.Drawing.GraphicsUnit]::Point
)
$pinGlyph = ([char]0xE718).ToString()
$buttonTooltip = [System.Windows.Forms.ToolTip]::new()
$buttonTooltip.SetToolTip($overlay, 'Keep this window on top')


$overlay.Add_Paint({
    param($sender, $eventArgs)
    $g = $eventArgs.Graphics
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $rect = [System.Drawing.Rectangle]::new(0, 0, $sender.ClientSize.Width - 1, $sender.ClientSize.Height - 1)
    if ($script:isPressed) {
        $fill = $script:pressedBackground
    } elseif ($script:isHovered) {
        $fill = $script:hoverBackground
    } else {
        $fill = $script:chromeBackground
    }

    $brush = [System.Drawing.SolidBrush]::new($fill)
    $g.FillRectangle($brush, $rect)
    $format = [System.Drawing.StringFormat]::new()
    $format.Alignment = [System.Drawing.StringAlignment]::Center
    $format.LineAlignment = [System.Drawing.StringAlignment]::Center
    # Segoe MDL2 glyphs sit optically high when their font line box is centered.
    # An optical baseline correction aligns the pin with the native caption icons.
    $baselineOffset = [single][Math]::Round(1.5 * ($sender.ClientSize.Height / 29.0), 1)
    $textBounds = [System.Drawing.RectangleF]::new(0, $baselineOffset, $sender.ClientSize.Width, $sender.ClientSize.Height)
    $glyph = if ($script:isPinned) { ([char]0xE842).ToString() } else { $pinGlyph }
    $iconBrush = [System.Drawing.SolidBrush]::new($(if ($script:isPinned) { $script:pinnedIcon } else { $script:chromeIcon }))
    $g.DrawString($glyph, $pinFont, $iconBrush, $textBounds, $format)
    $iconBrush.Dispose()
    $format.Dispose()
    $brush.Dispose()
})

$overlay.Add_MouseEnter({ $script:isHovered = $true; $overlay.Invalidate() })
$overlay.Add_MouseLeave({ $script:isHovered = $false; $script:isPressed = $false; $overlay.Invalidate() })
$overlay.Add_MouseDown({
    param($sender, $eventArgs)
    if ($eventArgs.Button -eq [System.Windows.Forms.MouseButtons]::Left) {
        $script:isPressed = $true
        $overlay.Invalidate()
    }
})
$overlay.Add_MouseUp({ $script:isPressed = $false; $overlay.Invalidate() })
$overlay.Add_MouseClick({
    param($sender, $eventArgs)
    if ($eventArgs.Button -ne [System.Windows.Forms.MouseButtons]::Left) { return }
    $hwnd = $script:targetHwnd
    # Never touch the z-order of a shell window: making the taskbar NOTOPMOST would hide it.
    if (-not [LazyPin.NativeMethods]::IsEligibleTarget($hwnd, [uint32]$PID)) { return }
    # SetWindowPos on a foreign window is synchronous; a hung target would block this tool too.
    if ([LazyPin.NativeMethods]::IsHungAppWindow($hwnd)) { return }
    if ($script:pinnedHwnd -ne $hwnd) {
        $currentForeground = [LazyPin.NativeMethods]::GetAncestor(
            [LazyPin.NativeMethods]::GetForegroundWindow(),
            [LazyPin.NativeMethods]::GA_ROOT
        )
        if ($currentForeground -ne $hwnd) { return }
    }

    $style = [LazyPin.NativeMethods]::GetWindowLongPtr($hwnd, [LazyPin.NativeMethods]::GWL_EXSTYLE).ToInt64()
    $alreadyTopmost = (($style -band [LazyPin.NativeMethods]::WS_EX_TOPMOST) -ne 0)
    $insertAfter = if ($alreadyTopmost) { [LazyPin.NativeMethods]::HWND_NOTOPMOST } else { [LazyPin.NativeMethods]::HWND_TOPMOST }
    $ok = [LazyPin.NativeMethods]::SetWindowPos(
        $hwnd,
        $insertAfter,
        0,
        0,
        0,
        0,
        [LazyPin.NativeMethods]::SWP_NOMOVE -bor [LazyPin.NativeMethods]::SWP_NOSIZE -bor [LazyPin.NativeMethods]::SWP_NOACTIVATE
    )
    if ($ok) {
        $script:isPinned = -not $alreadyTopmost
        $script:stateHwnd = $hwnd
        if ($script:isPinned) {
            $script:pinnedHwnd = $hwnd
            $buttonTooltip.SetToolTip($overlay, 'Unpin this window')
        } else {
            # Unpinning some other always-on-top window must not forget the one LazyPin pinned.
            if ($script:pinnedHwnd -eq $hwnd) { $script:pinnedHwnd = [IntPtr]::Zero }
            $buttonTooltip.SetToolTip($overlay, 'Keep this window on top')
        }
        $overlay.Invalidate()
    }
})

$startupFolder = [System.Environment]::GetFolderPath([System.Environment+SpecialFolder]::Startup)
$startupLink = Join-Path $startupFolder 'LazyPin.lnk'
$trayMenu = [System.Windows.Forms.ContextMenuStrip]::new()
$startupItem = [System.Windows.Forms.ToolStripMenuItem]::new('Run at Windows startup')
$startupItem.Checked = Test-Path -LiteralPath $startupLink
$null = $trayMenu.Items.Add($startupItem)

$null = $trayMenu.Items.Add([System.Windows.Forms.ToolStripSeparator]::new())

$aboutItem = [System.Windows.Forms.ToolStripMenuItem]::new('About LazyPin')
$aboutItem.Add_Click({
    [System.Windows.Forms.MessageBox]::Show(
        "LazyPin v1.0.4`n`nDeveloper: Raisul Sohan`nGitHub: https://github.com/raisulsohan/LazyPin`n`nA lightweight utility to keep any window always on top.",
        "About LazyPin",
        [System.Windows.Forms.MessageBoxButtons]::OK,
        [System.Windows.Forms.MessageBoxIcon]::Information
    )
})
$null = $trayMenu.Items.Add($aboutItem)

$exitItem = [System.Windows.Forms.ToolStripMenuItem]::new('Exit')
$null = $trayMenu.Items.Add($exitItem)

$startupItem.Add_Click({
    if (Test-Path -LiteralPath $startupLink) {
        Remove-Item -LiteralPath $startupLink -Force
        $startupItem.Checked = $false
    } else {
        $shell = New-Object -ComObject WScript.Shell
        $shortcut = $shell.CreateShortcut($startupLink)
        $appPath = [System.Diagnostics.Process]::GetCurrentProcess().MainModule.FileName
        $appName = [System.IO.Path]::GetFileNameWithoutExtension($appPath)
        $shortcut.TargetPath = $appPath
        if ($appName -match '^(powershell|pwsh)$') {
            $shortcut.Arguments = '-NoProfile -WindowStyle Hidden -ExecutionPolicy Bypass -File "{0}"' -f $script:scriptPath
            $shortcut.WorkingDirectory = Split-Path -Parent $script:scriptPath
        } else {
            # In a PS2EXE build the application is already the executable;
            # passing PowerShell's -File arguments would make startup fail.
            $shortcut.Arguments = ''
            $shortcut.WorkingDirectory = Split-Path -Parent $appPath
        }
        $shortcut.Description = 'Keep any window always on top with LazyPin'
        $shortcut.Save()
        $startupItem.Checked = $true
        [System.Runtime.InteropServices.Marshal]::ReleaseComObject($shortcut) | Out-Null
        [System.Runtime.InteropServices.Marshal]::ReleaseComObject($shell) | Out-Null
    }
})

$trayIcon = [System.Windows.Forms.NotifyIcon]::new()
$trayIcon.Icon = $script:applicationIcon
$trayIcon.Text = 'LazyPin v1.0.4 by Raisul Sohan'
$trayIcon.ContextMenuStrip = $trayMenu
$trayIcon.Visible = $true
$appContext = [System.Windows.Forms.ApplicationContext]::new()

# One exit path for the tray menu, Windows sign-out/shutdown and installer or
# "taskkill" close requests: give the pinned window its normal z-order back (there is
# no button left to unpin it with once LazyPin is gone), remove the tray icon (a killed
# process leaves a ghost icon until the mouse passes over it) and stop the loop.
function Invoke-LazyPinExit {
    if ($script:shuttingDown) { return }
    $script:shuttingDown = $true
    try {
        $pinned = $script:pinnedHwnd
        if ($pinned -ne [IntPtr]::Zero -and
            [LazyPin.NativeMethods]::IsWindow($pinned) -and
            [LazyPin.NativeMethods]::IsTopmost($pinned) -and
            -not [LazyPin.NativeMethods]::IsHungAppWindow($pinned)) {
            # Asynchronous so a slow target cannot hold up shutdown.
            [void][LazyPin.NativeMethods]::SetWindowPos(
                $pinned,
                [LazyPin.NativeMethods]::HWND_NOTOPMOST,
                0,
                0,
                0,
                0,
                [LazyPin.NativeMethods]::SWP_NOMOVE -bor [LazyPin.NativeMethods]::SWP_NOSIZE -bor [LazyPin.NativeMethods]::SWP_NOACTIVATE -bor [LazyPin.NativeMethods]::SWP_ASYNCWINDOWPOS
            )
        }
    } catch { }
    $script:pinnedHwnd = [IntPtr]::Zero
    try { if ($overlay.Visible) { $overlay.Hide() } } catch { }
    [LazyPin.NativeMethods]::StopWindowLocationTracking()
    try { $trayIcon.Visible = $false } catch { }
    $appContext.ExitThread()
}

$exitItem.Add_Click({ Invoke-LazyPinExit })
$overlay.Add_ExitRequested({ Invoke-LazyPinExit })

$timer = [System.Windows.Forms.Timer]::new()
$timer.Interval = 33
$timer.Add_Tick({
    if ($script:shuttingDown) { return }
    try {

    $ownProcessId = [uint32]$PID
    $foreground = [LazyPin.NativeMethods]::GetForegroundWindow()
    if ($foreground -ne [IntPtr]::Zero) {
        $root = [LazyPin.NativeMethods]::GetAncestor($foreground, [LazyPin.NativeMethods]::GA_ROOT)
        if ($root -ne [IntPtr]::Zero) { $foreground = $root }
    }

    # Forget the remembered pin as soon as that window is gone or something else un-topped it.
    if ($script:pinnedHwnd -ne [IntPtr]::Zero -and
        (-not [LazyPin.NativeMethods]::IsWindow($script:pinnedHwnd) -or
         -not [LazyPin.NativeMethods]::IsTopmost($script:pinnedHwnd))) {
        $script:pinnedHwnd = [IntPtr]::Zero
        $script:isPinned = $false
    }

    # Target priority:
    #   1. the focused window when it is itself on top, so any pinned window can be
    #      unpinned simply by focusing it;
    #   2. the window LazyPin pinned, while it is actually on screen (a minimised or
    #      cloaked pinned window must not take the button away from every other window);
    #   3. the focused window.
    # Shell surfaces (taskbar, Start, Task View, desktop...) are never targets.
    $hwnd = [IntPtr]::Zero
    if ($foreground -ne [IntPtr]::Zero -and
        [LazyPin.NativeMethods]::IsTopmost($foreground) -and
        [LazyPin.NativeMethods]::IsEligibleTarget($foreground, $ownProcessId)) {
        $hwnd = $foreground
    } elseif ($script:pinnedHwnd -ne [IntPtr]::Zero -and
        [LazyPin.NativeMethods]::IsEligibleTarget($script:pinnedHwnd, $ownProcessId)) {
        $hwnd = $script:pinnedHwnd
    } else {
        $hwnd = $foreground
    }

    if (-not [LazyPin.NativeMethods]::IsEligibleTarget($hwnd, $ownProcessId)) {
        if ($overlay.Visible) { $overlay.Hide() }
        $script:targetHwnd = [IntPtr]::Zero
        [void][LazyPin.NativeMethods]::TrackWindowLocation([IntPtr]::Zero, [IntPtr]::Zero, 0, 0, 0, 0)
        return
    }

    $windowRect = [LazyPin.RECT]::new()
    $captionRect = [LazyPin.RECT]::new()
    $visibleRect = [LazyPin.RECT]::new()
    $windowOk = [LazyPin.NativeMethods]::GetWindowRect($hwnd, [ref]$windowRect)
    $captionResult = [LazyPin.NativeMethods]::DwmGetWindowAttribute(
        $hwnd,
        [LazyPin.NativeMethods]::DWMWA_CAPTION_BUTTON_BOUNDS,
        [ref]$captionRect,
        [System.Runtime.InteropServices.Marshal]::SizeOf([type][LazyPin.RECT])
    )

    if (-not $windowOk) {
        if ($overlay.Visible) { $overlay.Hide() }
        $script:targetHwnd = [IntPtr]::Zero
        [void][LazyPin.NativeMethods]::TrackWindowLocation([IntPtr]::Zero, [IntPtr]::Zero, 0, 0, 0, 0)
        return
    }
    $visibleRect = $windowRect
    if ([LazyPin.NativeMethods]::DwmGetWindowAttribute(
        $hwnd,
        [LazyPin.NativeMethods]::DWMWA_EXTENDED_FRAME_BOUNDS,
        [ref]$visibleRect,
        [System.Runtime.InteropServices.Marshal]::SizeOf([type][LazyPin.RECT])
    ) -ne 0) {
        $visibleRect = $windowRect
    }

    $hasDwmBounds = $captionResult -eq 0 -and
        $captionRect.Width -gt 0 -and
        $captionRect.Height -gt 0 -and
        $captionRect.Left -lt ($windowRect.Right - $windowRect.Left)

    $dpi = 96
    try { $dpi = [LazyPin.NativeMethods]::GetDpiForWindow($hwnd) } catch { }
    if (-not $dpi -or $dpi -lt 48) { $dpi = 96 }
    $scale = [double]$dpi / 96.0
    $gap = [int][Math]::Round($script:buttonGap * $scale)

    $buttonWidth = [int][Math]::Round(46 * $scale)
    $buttonHeight = [int][Math]::Round(30 * $scale)
    $captionHeight = [int][Math]::Round(34 * $scale)
    $topBase = $visibleRect.Top

    $windowStyle = [LazyPin.NativeMethods]::GetWindowLongPtr($hwnd, [LazyPin.NativeMethods]::GWL_STYLE).ToInt64()
    $isMaximized = (($windowStyle -band 0x01000000L) -ne 0) # WS_MAXIMIZE
    $hasCaption = (($windowStyle -band [LazyPin.NativeMethods]::WS_CAPTION) -ne 0)
    $topBorderInset = if ($isMaximized) { 0 } else { [int][Math]::Max(1, [Math]::Round(1.0 * $scale)) }

    # A caption-less window that fills the monitor is a full-screen game, video or
    # F11 browser. Keeping a topmost overlay over it disables full-screen
    # optimisations and stutters that app, and it has no title bar to pin anyway.
    if (-not $hasCaption -and -not $isMaximized -and [LazyPin.NativeMethods]::CoversMonitor($hwnd, $windowRect)) {
        if ($overlay.Visible) { $overlay.Hide() }
        $script:targetHwnd = [IntPtr]::Zero
        [void][LazyPin.NativeMethods]::TrackWindowLocation([IntPtr]::Zero, [IntPtr]::Zero, 0, 0, 0, 0)
        return
    }

    if ($hasCaption) {
        $clientOrigin = [LazyPin.POINT]::new()
        if ([LazyPin.NativeMethods]::ClientToScreen($hwnd, [ref]$clientOrigin)) {
            $measuredCaption = $clientOrigin.Y - $windowRect.Top
            if ($measuredCaption -gt 0 -and $measuredCaption -lt [int](72 * $scale)) {
                $captionHeight = [Math]::Max($captionHeight, $measuredCaption)
            }
        }
    }

    $numButtons = 3
    $WS_MAXIMIZEBOX = 0x00010000L
    $WS_MINIMIZEBOX = 0x00020000L
    if (($windowStyle -band $WS_MAXIMIZEBOX) -eq 0 -and ($windowStyle -band $WS_MINIMIZEBOX) -eq 0) {
        $numButtons = 1
    } elseif (($windowStyle -band $WS_MAXIMIZEBOX) -eq 0 -or ($windowStyle -band $WS_MINIMIZEBOX) -eq 0) {
        $numButtons = 2
    }

    $baseControlsWidth = [int][Math]::Round(46.0 * $numButtons * $scale)

    if ($hasDwmBounds) {
        $nativeBtnWidth = [int][Math]::Round($captionRect.Width / [double]$numButtons)
        $buttonHeight = [Math]::Max(24, $captionRect.Height - $topBorderInset)
        $buttonWidth = [Math]::Max(32, $nativeBtnWidth)
        $captionHeight = $captionRect.Height
        $topBase = $visibleRect.Top + $topBorderInset

        # Calculate exact distance from visible window right edge to leftmost native caption button
        $nativeLeft = $windowRect.Left + $captionRect.Left
        $dwmDistFromRight = $visibleRect.Right - $nativeLeft
        if ($dwmDistFromRight -gt 0) {
            $baseControlsWidth = $dwmDistFromRight
        }
    }

    # The probe sends up to ~75 synchronous WM_NCHITTEST messages to the target's
    # UI thread. Doing that every 33 ms (2000+/s) saturates apps whose hit-test is
    # expensive (WPF WindowChrome, custom frames) and makes them appear hung.
    # Probe once per window, re-probe when the layout changes (rate-limited while
    # a resize drag is in progress), and refresh slowly otherwise.
    $now = $script:clock.ElapsedMilliseconds
    $probeKey = '{0}|{1}|{2}|{3}|{4}' -f $hwnd.ToInt64(), $visibleRect.Width, $captionHeight, $baseControlsWidth, $dpi
    $needProbe = $false
    if ($hwnd -ne $script:controlsCacheHwnd) {
        $needProbe = $true
    } elseif ($probeKey -ne $script:controlsCacheKey) {
        $needProbe = (($now - $script:controlsProbeTime) -ge $script:controlsProbeMinIntervalMs)
    } elseif (($now - $script:controlsProbeTime) -ge $script:controlsProbeMaxAgeMs) {
        $needProbe = $true
    }
    if ($needProbe) {
        $script:controlsCacheValue = [LazyPin.NativeMethods]::DetectControlsWidth($hwnd, $visibleRect, $captionHeight, $scale, $baseControlsWidth)
        $script:controlsCacheHwnd = $hwnd
        $script:controlsCacheKey = $probeKey
        $script:controlsProbeTime = $now
    }
    $detectedControls = $script:controlsCacheValue
    $controlsWidth = [Math]::Max($baseControlsWidth, $detectedControls)

    # Automatically adapt gap between buttons:
    # When directly adjacent to native caption buttons (e.g. File Explorer),
    # the buttons are seamless tiles with 0 gap, giving exact icon pitch alignment.
    # When extra custom icons exist to the left (e.g. Chrome profile/search icons),
    # keep a subtle gap to separate from the custom icon.
    if ($controlsWidth -le $baseControlsWidth + [int](2 * $scale)) {
        $gap = 0
        $controlsWidth = $baseControlsWidth
    } else {
        $gap = [int][Math]::Round(4 * $scale)
    }

    $x = $visibleRect.Right - $controlsWidth - $buttonWidth - $gap
    $y = $topBase + [int][Math]::Ceiling(($captionHeight - $topBorderInset - $buttonHeight) / 2.0)
    if ($y -lt ($visibleRect.Top + $topBorderInset)) { $y = $visibleRect.Top + $topBorderInset }
    if ($x -lt $visibleRect.Left) { $x = $visibleRect.Left }

    $sampleX = [Math]::Max($visibleRect.Left + 10, $x - [int][Math]::Round(4 * $scale))
    $sampleY = $y + [int][Math]::Floor($buttonHeight / 2.0)
    $overlay.Size = [System.Drawing.Size]::new($buttonWidth, $buttonHeight)

    # GetPixel on the screen DC makes DWM synchronise and read back the composed
    # frame; calling it 30 times a second stalls the whole desktop. The title-bar
    # colour only changes when the target or the focused window changes (active /
    # inactive caption), so sample in a short burst around those events (the
    # activation animation needs a few frames to settle) and otherwise slowly.
    $foregroundNow = [LazyPin.NativeMethods]::GetForegroundWindow()
    if ($hwnd -ne $script:sampleHwnd -or $foregroundNow -ne $script:sampleForeground) {
        $script:sampleHwnd = $hwnd
        $script:sampleForeground = $foregroundNow
        $script:sampleBurstUntil = $now + $script:sampleBurstMs
    }
    $shouldSample = ($now -le $script:sampleBurstUntil) -or (($now - $script:sampleTime) -ge $script:sampleMaxAgeMs)
    if ($shouldSample) {
        $script:sampleTime = $now
        $dc = [LazyPin.NativeMethods]::GetDC([IntPtr]::Zero)
        if ($dc -ne [IntPtr]::Zero) {
            $pixel = [LazyPin.NativeMethods]::GetPixel($dc, $sampleX, $sampleY)
            [void][LazyPin.NativeMethods]::ReleaseDC([IntPtr]::Zero, $dc)
            if ($pixel -ne [uint32]::MaxValue) {
                $pixelValue = [long]$pixel
                $sampled = [System.Drawing.Color]::FromArgb(
                    [int]($pixelValue -band 0xFF),
                    [int](($pixelValue -shr 8) -band 0xFF),
                    [int](($pixelValue -shr 16) -band 0xFF)
                )
                if ($sampled.ToArgb() -ne $script:chromeBackground.ToArgb()) {
                    $script:chromeBackground = $sampled
                    $luminance = 0.2126 * $script:chromeBackground.R + 0.7152 * $script:chromeBackground.G + 0.0722 * $script:chromeBackground.B
                    if ($luminance -lt 128) {
                        $script:chromeIcon = [System.Drawing.Color]::FromArgb(245, 245, 245)
                        $script:pinnedIcon = [System.Drawing.Color]::FromArgb(96, 205, 255)
                    } else {
                        $script:chromeIcon = [System.Drawing.Color]::FromArgb(32, 32, 32)
                        $script:pinnedIcon = [System.Drawing.Color]::FromArgb(0, 95, 184)
                    }
                    $script:hoverBackground = [System.Drawing.Color]::FromArgb(
                        [int][Math]::Round($script:chromeBackground.R + ($script:chromeIcon.R - $script:chromeBackground.R) * 0.16),
                        [int][Math]::Round($script:chromeBackground.G + ($script:chromeIcon.G - $script:chromeBackground.G) * 0.16),
                        [int][Math]::Round($script:chromeBackground.B + ($script:chromeIcon.B - $script:chromeBackground.B) * 0.16)
                    )
                    $script:pressedBackground = [System.Drawing.Color]::FromArgb(
                        [int][Math]::Round($script:chromeBackground.R + ($script:chromeIcon.R - $script:chromeBackground.R) * 0.28),
                        [int][Math]::Round($script:chromeBackground.G + ($script:chromeIcon.G - $script:chromeBackground.G) * 0.28),
                        [int][Math]::Round($script:chromeBackground.B + ($script:chromeIcon.B - $script:chromeBackground.B) * 0.28)
                    )
                    $overlay.BackColor = $script:chromeBackground
                    $overlay.Invalidate()
                }
            }
        }
    }
    $bounds = [System.Drawing.Rectangle]::new($x, $y, $buttonWidth, $buttonHeight)

    $script:targetHwnd = $hwnd
    $style = [LazyPin.NativeMethods]::GetWindowLongPtr($hwnd, [LazyPin.NativeMethods]::GWL_EXSTYLE).ToInt64()
    $pinnedNow = (($style -band [LazyPin.NativeMethods]::WS_EX_TOPMOST) -ne 0)
    if ($pinnedNow -ne $script:isPinned -or $script:stateHwnd -ne $hwnd) {
        $script:isPinned = $pinnedNow
        $script:stateHwnd = $hwnd
        $buttonTooltip.SetToolTip($overlay, $(if ($pinnedNow) { 'Unpin this window' } else { 'Keep this window on top' }))
        $overlay.Invalidate()
    }

    if (-not $overlay.Visible -or $bounds -ne $script:lastBounds) {
        if ($overlay.Size.Width -ne $bounds.Width -or $overlay.Size.Height -ne $bounds.Height) {
            $overlay.Size = $bounds.Size
        }
        if (-not $overlay.Visible) {
            # Move the hidden window first so its very first frame already sits beside
            # the caption buttons instead of flashing at its previous position.
            $overlay.Bounds = $bounds
            $overlay.Show()
        }
        [void][LazyPin.NativeMethods]::SetWindowPos(
            $overlay.Handle,
            [LazyPin.NativeMethods]::HWND_TOPMOST,
            $bounds.X,
            $bounds.Y,
            $bounds.Width,
            $bounds.Height,
            [LazyPin.NativeMethods]::SWP_NOACTIVATE -bor [LazyPin.NativeMethods]::SWP_SHOWWINDOW
        )
        $script:lastBounds = $bounds
    }

    # Making the target topmost (or activating a pinned window) moves it above this
    # separate overlay. Promote the button again only when that actually happened;
    # an unconditional SetWindowPos every tick forces a z-order pass and a DWM
    # recomposition 30 times a second.
    if ($overlay.Visible) {
        [void][LazyPin.NativeMethods]::TrackWindowLocation(
            $hwnd,
            $overlay.Handle,
            $bounds.X - $windowRect.Right,
            $bounds.Top - $windowRect.Top,
            $bounds.Width,
            $bounds.Height
        )
        if ([LazyPin.NativeMethods]::IsTargetAboveOverlay($hwnd, $overlay.Handle)) {
            [void][LazyPin.NativeMethods]::SetWindowPos(
                $overlay.Handle,
                [LazyPin.NativeMethods]::HWND_TOPMOST,
                0,
                0,
                0,
                0,
                [LazyPin.NativeMethods]::SWP_NOMOVE -bor [LazyPin.NativeMethods]::SWP_NOSIZE -bor [LazyPin.NativeMethods]::SWP_NOACTIVATE
            )
        }
    }

    } catch {
        # A window can vanish between any two Win32 calls above. Never let that
        # surface as an error dialog from a tray tool or stop the timer.
    }
})

$appContext.Add_ThreadExit({
    $script:shuttingDown = $true
    $timer.Stop()
    $trayIcon.Visible = $false
    $timer.Dispose()
    $trayIcon.Dispose()
    $trayMenu.Dispose()
    $overlay.Dispose()
    [LazyPin.NativeMethods]::StopWindowLocationTracking()
    $pinFont.Dispose()
    $buttonTooltip.Dispose()
    if ($script:mutex) {
        $script:mutex.ReleaseMutex()
        $script:mutex.Dispose()
    }
})

$timer.Start()
[System.Windows.Forms.Application]::Run($appContext)
