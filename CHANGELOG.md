# Changelog

All notable changes to **DuoWall by SRMD** are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

---

## [2.0.1] - 2026-11-26

### 🌟 Headline Improvements

Version 2.0.1 delivers full universal glassmorphic theme styling directly to the Windows taskbar, adds seamless in-place upgrade and fresh-install engine capabilities to the Inno Setup installer, and establishes an interactive release versioning workflow.

---

### 🪟 Taskbar Universal Glass Theme & Win+D Protection
- **Universal Glass Styling on Windows Taskbar**:
  - Full-bar optical glass backdrop rendering seamlessly beneath the native Windows taskbar across both Windows 10 and modern Windows 11 builds (22H2, 23H2, 24H2).
  - Fixed Windows 11 XAML Island (`DesktopWindowXamlSource`) occlusion by applying transparent gradient accents (`ACCENT_ENABLE_TRANSPARENTGRADIENT`) and full DWM client-area frame extensions (`DwmExtendFrameIntoClientArea`) across `Shell_TrayWnd` and child handles.
  - Dynamically extracts and renders high-fidelity SkiaSharp blurred crop slices from the active desktop wallpaper behind the exact taskbar bounding rectangle.
  - Renders sharp zero-radius edge-to-edge glass framing with customizable opacity (`TaskbarAcrylicOpacity`), optical border highlight, and glass tint.
  - Keeps native Start menu, taskbar icons, system tray notification icons, and clock fully interactive and layered above the glass surface.
- **Win + D Desktop Shell Attachment**:
  - Attached taskbar overlay windows to the desktop shell owner (`GWLP_HWNDPARENT` -> `SHELLDLL_DefView` / `Progman`), ensuring the glass backdrop is never minimized when invoking Win + D (Show Desktop).
- **Synchronized Auto-Hide Tracking**:
  - 60Hz WinEvent tracking smoothly slides the taskbar glass overlay in lock-step with Windows auto-hide animations on primary and secondary displays.

---

### 📦 Setup Installer Dual Engine (Fresh Install & Seamless Upgrade)
- **Restart Manager & Single-Instance Mutex Detection**:
  - Inno Setup installer now tracks `AppMutex=DuoWallBySRMD_SingleInstance_Mutex` with native Windows Restart Manager integration (`CloseApplications=yes`, `RestartApplications=yes`).
  - Running instances of DuoWall are cleanly closed before overwriting application binaries, preventing process termination crashes.
- **In-Place File Replacement (`restartreplace`)**:
  - Files marked with `restartreplace` ensure atomic replacement of binaries and libraries even if temporarily in use.
- **User Data & Database Preservation**:
  - Upgrading automatically detects existing installations via `AppId={{5A8C3D21-7B32-4E9A-A372-F9D1E4E8D201}}`, preserving install paths (`UsePreviousAppDir=yes`), start menu shortcuts (`UsePreviousGroup=yes`), and desktop preferences (`UsePreviousTasks=yes`).
  - User database (`duowall.db`), custom widget packages (`.dwwp`), glass presets, and downloaded wallpapers stored in `%LOCALAPPDATA%\DuoWallBySRMD` remain completely untouched during updates.
- **Dual Support**:
  - Seamlessly acts as a clean, stand-alone installer on brand-new PCs, or as an automatic in-place updater for previously installed versions.

---

### ℹ️ Dynamic Version Resolution in About Me & Update Checker
- **Centralized `AppVersionInfo` Engine**:
  - Dynamically extracts the active build version from assembly manifest attributes (`AssemblyInformationalVersionAttribute` / `AssemblyVersion`), removing static hardcoded `"v2.0.0"` strings.
  - **About Me Dialog**: Version badge dynamically displays the true runtime version (`v2.0.1`).
  - **Check for Updates**: Compares GitHub releases against dynamic `AppVersionInfo.Version` (`2.0.1`), accurately notifying users when up to date and reporting the correct active version.
  - **Settings View**: Title bar and footer dynamically bind to `AppVersionDisplay`.

---

## [2.0.0] - 2026-11-13

### 🌟 Headline Features

Version 2.0.0 represents a massive leap forward for **DuoWall by SRMD**, transforming it from a dual-monitor wallpaper tool into an all-in-one panoramic workspace suite featuring glassmorphic desktop widgets, taskbar tweaks, direct canvas transformation, and automated release packaging.

---

### 🎨 Placement Studio & Direct Canvas Transforms
- **Direct Interactive Wallpaper Transformation**:
  - Click on wallpaper preview to reveal an interactive 8-handle dashed bounding box (`NW`, `N`, `NE`, `E`, `SE`, `S`, `SW`, `W`).
  - Drag handles to resize the wallpaper while anchoring the opposite edge via `WallpaperGeometryEngine.ComputeResizedTransform`.
  - Click and drag anywhere inside the bounding box to pan live.
  - Mouse wheel zoom with proportional scaling around cursor or focal center.
  - Releasing mouse auto-applies live wallpaper framing to the Windows desktop (`AutoApplyStudioChanges`).
- **Multi-Widget Selection & Group Dragging**:
  - Select widgets via single-click, `Ctrl+Click`, or `Shift+Click`.
  - Rubber-band marquee drag selection inside Placement Studio to box-select multiple widgets at once.
  - Dragging any selected widget moves the entire group simultaneously, preserving exact relative distances and grid snapping.
- **Resizable Monitor Preview Viewport**:
  - Smoothly resize monitor preview window to fit any screen resolution or workspace layout.
- **Profile Backup & Restoration**:
  - Export and import complete workspace configuration backups (`.json` / `.dwwp`), preserving wallpaper geometry, active widgets, screen coordinates, and glass styling presets.

---

### 🧩 Widget Marketplace & Glass Desktop Widgets
- **Win + D Desktop Pinning (Never Minimize)**:
  - Widget hosts now integrate into the desktop shell hierarchy by attaching ownership to `SHELLDLL_DefView` / `Progman` via Win32 `GWLP_HWNDPARENT`.
  - Pressing `Win + D` (Show Desktop) minimizes all open windows while desktop widgets remain strictly pinned to the desktop layer behind applications.
  - Win32 `WndProc` intercepts minimize commands (`SC_MINIMIZE`), suppresses shell hide flags (`SWP_HIDEWINDOW`), and asserts bottom desktop Z-order.
- **Glass Theme Persistence Across PC Restarts**:
  - Full persistence of active glass presets and custom slider tweaks via serialized JSON in `AppSettings` and `DesktopProfile`.
  - DuoWall automatically remembers and restores whichever glass theme (Dark Glass, Midnight Glass, Obsidian, Cyber HUD, or custom color tints/blur values) was active before restart, preventing any fallback to the default white Frosted Glass.
- **Renamed Tab**: Updated navigation to **Widget Marketplace** with integrated widget catalog.
- **Direct "Edit" Button**:
  - If a widget is already added to the desktop, an **Edit** button appears next to "Add to Desktop" on its marketplace card, opening its configuration immediately.
- **Wallpaper-Aware Universal Accent Colors**:
  - Select custom accent colors or enable **"Auto Accent from Wallpaper"**, utilizing SkiaSharp dominant palette and luminance analysis for perfect contrast.
- **Enhanced Full-Month Calendar Widget**:
  - Displays full monthly grid with Monday as the starting day of the week.
  - Today's date is prominently marked with a smooth, translucent circular backdrop.
  - Weekends (Saturday & Sunday) and public holidays highlighted with distinct warm badge colors.
  - Month navigation arrows (`<` / `>`) to browse previous or upcoming months.
  - Interactive Date Click: Opens the **Schedule Event / Reminder / Note** composer.
  - Alarms & Notifications: Supports silent notifications or continuous audio alarms until dismissed.
  - Collapsible upcoming events list displayed below calendar when vertically expanded.
- **App, Folder & Web Shortcut Dock**:
  - Supports shortcuts to installed applications, folders, and websites.
  - **Windows Installed Apps Picker**: Enumerates Windows Store/UWP apps (`shell:AppsFolder`), Start Menu shortcuts, and Registry `App Paths` with live search and native icon extraction, replacing the raw Explorer browse dialog.
  - **Custom Icon Support**: Upload custom image icons with borderless display (eliminating forced outer container boxes).
  - Smart Initials Mode: Automatically derives clean glass lettermarks.
  - Icon Scale Slider: Adjust shortcut icon size dynamically from `0.45×` to `2.50×`.
  - Contextual Tooltips: App titles appear on hover in theme-adaptive black or white text.
- **Typographic Clock**:
  - Renamed from "Mond Typographic Clock" to **Typographic Clock**.
  - Independent typography selection with live font preview for time, date, and day elements.
  - Per-element color picker and universal accent color toggle.
  - Master and individual scale sliders.
  - Improved drop-shadow blur and depth sliders.
  - Removed obsolete analog second hand.
- **Customizable Focus Timer**:
  - Configure custom focus session intervals (e.g., 15m, 25m, 45m, 60m).
- **Weather Widget**:
  - Select minimal condition icon themes with support for animated weather states.
  - Keyless Open-Meteo backend with fallback to custom OpenWeatherMap API keys.

---

### 🖥️ Taskbar Tweaks
- **Dedicated Taskbar Tweaks Tab**: Comprehensive control over Windows taskbar visual style and behavior.
- **Taskbar Center Glass Dock**:
  - Inspects active running application buttons inside Windows taskbar toolbars via UIAutomation and MSAA accessibility fallback.
  - Centers a sleek, blurred glass pill directly behind active application buttons.
  - Dock Width and Height sliders for custom padding and margin adjustments.
  - Smooth auto-hide tracking: syncs seamlessly with Windows auto-hide animations.
- **Multi-Display Taskbar Controls**:
  - Configure taskbar styling across all monitors or restrict to a selected display.
  - Universal Glass theme integration matching desktop widgets.

---

### 🖼️ Wallpaper Gallery
- **Local Photo Upload**:
  - Upload custom personal wallpapers with automated resolution and aspect-ratio checking for dual/multi-monitor setups.
  - Warning dialog before applying low-resolution photos to prevent blurry spanning.
- **Custom Ultra-Wide Website Linking**:
  - Add custom wallpaper sources or websites; parses live HTML and extracts panoramic wallpaper links.

---

### ℹ️ Top Navigation & Developer Contact
- **Top Menu Buttons**:
  - **About Me**: Opens developer info dialog displaying:
    > *"This software is developed by Syed Sarmad Iftikhar. You can give your feedback and request new features at engr.sarmad.iftikhar@gmail.com"*
    Includes quick-launch `mailto:` feedback button and GitHub link.
  - **Check for Updates**: Direct GitHub Release query notifying users of newer versions without background silent downloads.

---

### 📦 Packaging & Installation
- **Inno Setup 6 Installer (`DuoWall-v2.0.0-Setup.exe`)**:
  - Self-contained, ultra-compressed setup with desktop shortcut, start menu shortcut, and optional autostart.
- **Portable Distribution (`DuoWall-v2.0.0-win-x64-Portable.zip`)**:
  - Zero-installation zip package containing self-contained single-file binary.
- **Cryptographic Verification**:
  - Standard SHA-256 checksums in `publish/SHA256SUMS.txt`.

---

## [1.0.0] - 2026-11-10
### Added
- Multi-monitor geometry calibration (`VirtualPixels` and millimeter EDID `PhysicalDimensions` with bezel compensation).
- Per-monitor SkiaSharp compositor and native `IDesktopWallpaper` COM wallpaper application.
- Integrated `UltraWideWallpapers.net` gallery browser, local library, Placement Studio, Monitor Setup, and Settings.
