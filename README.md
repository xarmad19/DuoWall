# DuoWall by SRMD

**Smart Multi-Monitor Panoramic Wallpaper Manager for Windows 10 & Windows 11**

DuoWall by SRMD is a native .NET 8 WPF desktop application built to replace legacy multi-monitor wallpaper tools like MultiWall. It accurately detects connected displays, accounts for differences in resolution, physical panel dimensions, bezel spacing, DPI scaling, orientation, and negative virtual desktop coordinates, and applies pixel-accurate per-monitor wallpapers using the Windows `IDesktopWallpaper` COM interface.

---

## Key Features

### 1. Intelligent Multi-Monitor Wallpaper Engine
- **Hardware Display Detection**: Uses Win32 `EnumDisplayMonitors`, `EnumDisplaySettingsEx`, `QueryDisplayConfig`, `GetDpiForMonitor` (Per-Monitor V2 DPI aware), and hardware EDID registry descriptors (`HKLM\SYSTEM\CurrentControlSet\Enum\DISPLAY`) for millimeter-accurate physical dimensions.
- **Dual Coordinate Geometry Modes**:
  - **Physical Dimensions Mode (`PhysicalDimensions`)**: Maps wallpapers using real-world physical panel sizes (e.g., aligning a 24" 527×296 mm monitor next to a 27" 597×336 mm monitor) with bezel-gap compensation and desk alignment (`AlignBottom`, `AlignCenter`, `AlignTop`, or custom mm offset).
  - **Virtual Pixel Mode (`VirtualPixels`)**: Maps wallpapers directly onto Windows virtual pixel bounds (handling mixed resolutions like `1920×1080` + `2560×1440`, negative coordinates, portrait orientations, and vertical/horizontal offsets).
- **Placement Modes**: `Panoramic / Span`, `Fill`, `Fit`, `Stretch`, `Center`, and `Individual` (separate wallpaper per display).
- **Non-Destructive High-Quality Composition**: Renders dedicated per-monitor output files (PNG lossless or 96% JPEG) via `SkiaSharp` directly from full-resolution originals without ever overwriting downloaded source files.
- **Native Per-Monitor Application**: Applies each monitor's slice individually through `IDesktopWallpaper` (`CLSID C2CF3110-460E-4FC1-B9D0-8A1C0C9CC4BD`) and backs up the previous Windows desktop wallpaper state for one-click restoration.

### 2. Smart Interactive Preview (`MultiMonitorPreviewControl`)
- Renders every connected monitor proportionally to its true virtual or physical geometry.
- Drag directly inside the preview to pan horizontally/vertically, scroll mouse wheel to zoom (`0.25×`–`4.0×`), double-click to reset, and toggle monitor boundary & resolution badges.
- Displays resolution adequacy advisories when a source image is smaller than required for the combined desktop.

### 3. Integrated Wallpaper Gallery (`UltraWideWallpapers.net`)
- Browse 6,200+ wallpapers via UltraWideWallpapers.net's public gallery index, `gallery_load.php`, category/resolution pages, and detail pages.
- Filter by resolution/aspect (`32:9`, `21:9`, `16:9`, `4K`, `5K`, `8K`, `Dual Monitor`), themes (`Nature`, `Abstract`, `Space`, `Architecture`, `Technology`, `OLED`, `Dark`, `Cyberpunk`, `Fantasy`, `Steampunk`, `Animals`, `Vehicles`, `Retro`, `Surreal`, `Pixel Art`), `Favorites`, and sort by `Recent`, `Popular`, `Oldest`, or `Shuffle`.
- Respects website access restrictions cleanly: Club/Member-only (`vault-*`) or gated items clearly notify the user and open the official source webpage to sign in and complete authorized downloads.

### 4. Local Library, Automation & Settings
- SQLite-backed local wallpaper library (`%LOCALAPPDATA%\DuoWallBySRMD\duowall.db`) tracking favorites, recently used wallpapers, downloaded file metadata, monitor calibrations, and per-topology placement profiles.
- Supports importing local images (`JPG`, `PNG`, `WebP`, `BMP`).
- Optional Windows startup re-application (`HKCU\Software\Microsoft\Windows\CurrentVersion\Run`) and debounced `DisplaySettingsChanged` recovery when monitors connect, disconnect, or change resolution.
- Fluent Windows 11-inspired Dark (default) and Light themes.

---

## Project Structure

- `DuoWall.sln`
- `src/DuoWall.Core/` — Platform-agnostic multi-monitor geometry engine (`WallpaperGeometryEngine`), SkiaSharp compositor (`SkiaWallpaperCompositor`), UltraWideWallpapers.net provider (`UltraWideWallpapersProvider`), SQLite persistence (`DuoWallDatabase`), and library services.
- `src/DuoWall.App/` — .NET 8 WPF Fluent desktop application (`Win32DisplayDetector`, `DesktopWallpaperApplier`, `StartupAndWatcherService`, `MultiMonitorPreviewControl`, `MainViewModel`, and Views).
- `tests/DuoWall.Tests/` — xUnit test suite covering multi-monitor geometry, negative coordinates, mixed resolutions, physical dimensions + bezels, Skia cross-monitor seam alignment, and gallery HTML parsing.

---

## Installation & Usage

### Standalone Portable Executable (No .NET Runtime Required)
1. Locate the standalone Release output in `publish/DuoWall-Portable/DuoWall.exe` (or extract `publish/DuoWall-v1.0.0-win-x64-Portable.zip`).
2. Run `DuoWall.exe` directly on Windows 10 (19045+) or Windows 11 (x64), OR run `Install-DuoWall.ps1` to install into `%LOCALAPPDATA%\Programs\DuoWall` with Start Menu and Desktop shortcuts.
3. **Quick Start**:
   - Open **Monitor Setup** to verify your detected monitors, physical dimensions, and desk vertical alignment (`Align Desk Bottoms` / `Centers` / `Tops`).
   - Open **Wallpaper Gallery** to search and filter wallpapers from `UltraWideWallpapers.net` (or click **Import Local Image** in **My Wallpapers**).
   - Click any wallpaper to preview it across your actual monitor geometry, then click **Apply to Monitors** or **Adjust in Studio** to fine-tune pan, zoom, focal point, and crops.
   - Use **Settings** to configure your download directory, PNG/JPEG output quality, startup auto-apply, display change auto-recovery, or **Restore Previous Windows Wallpaper**.

### Building & Testing from Source
```powershell
dotnet test DuoWall.sln --configuration Release
dotnet build DuoWall.sln --configuration Release
dotnet publish src/DuoWall.App/DuoWall.App.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true
```
