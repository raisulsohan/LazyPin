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
- **Accurate Window Tracking**: Stays locked firmly in place beside caption buttons with zero jitter during live window movement and resizing.
- **Intelligent App Adaptation**:
  - Automatically aligns with native Windows software (File Explorer, Notepad, Settings).
  - Dynamically detects custom caption controls in modern apps and web browsers (Chrome, Edge, Claude Desktop, etc.) so it never overlaps existing buttons.
- **Fullscreen Intelligence**: Automatically hides when entering full-screen apps, games, media players, or F11 browser views to avoid disrupting full-screen experiences.
- **System Tray Integration**: Operates quietly in the notification area without cluttering your taskbar. Right-click the tray icon to toggle Windows startup, view developer info, or exit.
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
1. Download **`LazyPin-v1.0.3-portable.zip`** from [Releases](https://github.com/raisulsohan/LazyPin/releases/latest).
2. Extract the archive and double-click `LazyPin.exe` (or `Start LazyPin.cmd`).

---

## 🖥️ How It Works

1. When **LazyPin** is running in your System Tray, open or focus any application window.
2. An elegant pin icon appears directly to the left of the window's minimize button.
3. Click the pin button:
   - 📌 **Pinned**: The button highlights with an active accent color, and the window stays pinned on top of all other windows even if you focus another app.
   - 📌 **Unpinned**: Click once more to return the window to standard behavior.

---

## 🛠️ Building from Source

To build `LazyPin.exe` and `LazyPinSetup.exe` from source:

### Prerequisites
- Windows 10 or 11 (64-bit)
- Windows PowerShell 5.1+
- [PS2EXE](https://github.com/MScholtes/PS2EXE) module:
  ```powershell
  Install-Module ps2exe -Scope CurrentUser
  ```
- [Inno Setup 6](https://jrsoftware.org/isdl.php) (for compiling the installer)

### Build Command
Simply run:
```cmd
Build-Installer.cmd
```
or run via PowerShell:
```powershell
.\Build.ps1
```
This automatically compiles `LazyPin.exe` and packages `LazyPinSetup.exe` in the root folder.

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
