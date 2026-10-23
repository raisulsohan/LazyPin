# LazyPin 📌

> A lightweight, seamless Windows utility that adds a native-style **Always-On-Top** pin button directly beside window caption controls (minimize / maximize / close) on any active window.

[![Release](https://img.shields.io/github/v/release/raisulsohan/LazyPin?style=flat-square&color=2ea44f)](https://github.com/raisulsohan/LazyPin/releases)
[![Platform](https://img.shields.io/badge/platform-Windows%2010%20%7C%2011-0078D6?style=flat-square)](https://github.com/raisulsohan/LazyPin)
[![Architecture](https://img.shields.io/badge/arch-x64-555555?style=flat-square)](https://github.com/raisulsohan/LazyPin)
[![Developer](https://img.shields.io/badge/developer-Raisul%20Sohan-FF6F00?style=flat-square)](https://github.com/raisulsohan)
[![License: MIT](https://img.shields.io/badge/license-MIT-blueviolet?style=flat-square)](LICENSE)

<p align="center">
  <img src="demo/lazypin-demo.gif" alt="LazyPin Demo Animation" width="100%" />
</p>

<p align="center">
  <em>⚡ Seamless Always-on-Top title bar integration in action.</em><br>
  <a href="demo/lazypin-demo.html">🎮 <b>Open Interactive 10s Demo Player with Audio</b></a>
</p>

---

## ✨ Features

- **Seamless Native Design**: Matches the exact size, border radius, title bar background color, and theme of standard Windows 10 & 11 caption controls.
- **Single-Click Pin Toggle**: Click the pin icon to keep the active window **Always on top**. Click again to unpin and restore normal z-order.
- **Pin Several Windows**: Pin as many windows as you like. The one you pinned or clicked last is in front, exactly as Windows orders any windows. **Unpin all windows** in the tray menu (or `LazyPin.exe --unpin-all`, for a shortcut or a script) releases them all at once.
- **Accurate Window Tracking**: Stays locked firmly in place beside caption buttons with zero jitter during live window movement and resizing.
- **Intelligent App Adaptation**:
  - Automatically aligns with native Windows software (File Explorer, Notepad, Settings).
  - Dynamically detects custom caption controls in modern apps and web browsers (Chrome, Edge, Claude Desktop, etc.) so it never overlaps existing buttons.
- **Fullscreen Intelligence**: Automatically hides when entering full-screen apps, games, media players, or F11 browser views to avoid disrupting full-screen experiences.
- **System Tray Integration**: Operates quietly in the notification area without cluttering your taskbar. Right-click the tray icon to unpin all windows, toggle Windows startup, view developer info, or exit.
- **Light and Self-Healing**: A small native program that reacts to window events instead of polling, uses about 25 MB of memory and around 0.1 % of one CPU core, and restarts itself if it ever gets stuck on an app that stops responding.
- **Zero Admin Rights Required**: Installs directly into your local user directory without triggering UAC prompt restrictions.

---

## 🚀 Download & Installation

### Option 1: Installer (Recommended)
Download **`LazyPinSetup.exe`** from the [Latest Release](https://github.com/raisulsohan/LazyPin/releases/latest):
1. Run `LazyPinSetup.exe`.
2. Follow the setup wizard to choose your options (Start Menu shortcut, Desktop icon, Run on Windows sign-in).
3. The app starts immediately in your System Tray.
4. **Uninstall anytime**: Can be cleanly uninstalled from Windows **Settings > Apps > Installed apps**.

### Option 2: Portable
1. Download **`LazyPin-v1.1.0-portable.zip`** from [Releases](https://github.com/raisulsohan/LazyPin/releases/latest).
2. Extract the archive and double-click `LazyPin.exe`.

---

## 🖥️ How It Works

1. When **LazyPin** is running in your System Tray, open or focus any application window.
2. An elegant pin icon appears directly to the left of the window's minimize button.
3. Click the pin button:
   - 📌 **Pinned**: The button highlights with an active accent color, and the window stays pinned on top of all other windows even if you focus another app.
   - 📌 **Unpinned**: Click once more to return the window to standard behavior.
4. The button always sits on the window you are using, so you can pin more windows one after another. To unpin one of them, click it and then click its pin button, or use **Unpin all windows** in the tray menu.

---

## 🛠️ Building from Source

To build `LazyPin.exe` and `LazyPinSetup.exe` from source:

LazyPin is a single C# file (`LazyPin.cs`). It is compiled with the C# compiler of the .NET Framework 4.x that is part of every Windows 10 and 11 installation, so no SDK or extra module is needed.

### Prerequisites
- Windows 10 or 11 (64-bit)
- Windows PowerShell 5.1+ (runs `Build.ps1`)
- [Inno Setup 6](https://jrsoftware.org/isdl.php) (for compiling the installer only)

### Build Command
Simply run:
```cmd
Build-Installer.cmd
```
or run via PowerShell:
```powershell
.\Build.ps1
```
This automatically compiles `LazyPin.exe` and packages `LazyPinSetup.exe` in the root folder. Use `.\Build.ps1 -SkipInstaller` to build only `LazyPin.exe`. The version number lives in one place, `AppInfo.Version` in `LazyPin.cs`; the installer takes it from there.

---

## 🩺 Troubleshooting

LazyPin keeps a small log (start, stop, recoveries and errors) in `%LOCALAPPDATA%\LazyPin\LazyPin.log`. If something goes wrong, that file shows what happened. For a detailed trace of every measurement and button move, start LazyPin with the environment variable `LAZYPIN_TRACE=1`.

---

## 📝 Changelog

### v1.1.0
- **New**: pin several windows at once. The pin button now always sits on the focused window (before, it stayed on the one pinned window, so a second window could not be pinned). Windows keeps the pinned windows in their natural order: the one pinned or clicked last is in front.
- **New**: **Unpin all windows** in the tray menu, and `LazyPin.exe --unpin-all` for shortcuts and scripts. Exiting LazyPin also unpins every window it pinned.
- **Rebuilt as a native program**: LazyPin no longer runs a PowerShell script inside a PS2EXE wrapper. It uses about a third of the memory (25 MB instead of 70 MB) and about a fifth of the CPU time, starts in a fraction of a second, and no longer uses the PS2EXE wrapper that antivirus tools often flag.
- **No more polling**: window events (focus, move, resize, minimise, close, virtual desktop switch) move the button at once; a slow timer only covers the few changes Windows does not report.
- **Fixed**: the button no longer trails or wobbles while a window is dragged. It follows within about 2 ms (before: up to 140 ms), and it no longer jumps by a few pixels when Windows reports the frame position a frame late.
- **Fixed**: the button no longer jumps onto a browser's title-bar icons while the browser is busy, and it remembers each window's layout, so it lands in the right place at once when you switch back.
- **Fixed**: an app that answers slowly can no longer freeze LazyPin. Title-bar measurement is capped at 50 ms, screen colour reads run on a background thread, and pinning a busy app is queued instead of waited for.
- **Self-healing**: if LazyPin ever gets stuck (for example on an app that hangs while being pinned), a watchdog starts a fresh copy within about 35 seconds and the stuck one is closed. Before, the button stayed frozen until the next sign-in.
- **New**: on a window that runs as administrator, which Windows does not let a normal program pin, the button is dimmed and explains why.
- **New**: errors are written to a log file instead of being lost.

### v1.0.4
- **Fixed**: the pin button no longer attaches itself to the taskbar (beside the clock), Start menu, Task View, desktop or other Windows shell surfaces when they take focus. The taskbar is a permanently always-on-top window, so the button used to appear there in its "pinned" colour, and clicking it could un-top the taskbar.
- **Fixed**: a pinned window that is minimised, or that lives on another virtual desktop, no longer makes the button disappear from every other window.
- **Fixed**: focusing an always-on-top window now moves the button to it, so a pinned window can always be unpinned simply by focusing it.
- **Fixed**: exiting LazyPin restores the normal z-order of the window it pinned, and a close request from Windows, the installer or `taskkill` shuts the tool down cleanly (no ghost tray icon).
- **Fixed**: the installer removes the leftovers of the old "Pin to top" build, which could start alongside LazyPin at sign-in and show a second pin button.

---

## 👨‍💻 Developer & Credits

- **Developer**: **Raisul Sohan**
- **GitHub**: [@raisulsohan](https://github.com/raisulsohan)
- **Repository**: [https://github.com/raisulsohan/LazyPin](https://github.com/raisulsohan/LazyPin)
- **Email**: lettertosohan@gmail.com
- **Copyright**: © 2026 Raisul Sohan. All rights reserved.

---

## 📄 License

This project is licensed under the **MIT License** - see the [LICENSE](LICENSE) file for details.
