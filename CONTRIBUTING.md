# Contributing to DuoWall by SRMD

Thank you for helping improve **DuoWall by SRMD**!

## Development Prerequisites
- Windows 10 (build 19045+) or Windows 11 (x64)
- .NET 8 SDK (`8.0.4xx` or newer)

## Local Setup
```powershell
git clone https://github.com/xarmad19/DuoWall.git
cd DuoWall
dotnet restore DuoWall.sln
dotnet test DuoWall.sln --configuration Release
dotnet build DuoWall.sln --configuration Release
```

## Architecture Overview
- `src/DuoWall.Core` — Platform-agnostic geometry engine, SkiaSharp compositor, color analyzer, Glassmorphism models, declarative widget & profile engines, package validator (`.dwwp`), SQLite storage, and gallery/weather providers.
- `src/DuoWall.App` — Native .NET 8 WPF Fluent application, Win32 display/power/full-screen interop, `IDesktopWallpaper` COM integration, DWM Glassmorphism backdrop host, Widget Studio, Placement Studio, and desktop widget overlay engine.
- `tests/DuoWall.Tests` — xUnit unit & integration tests.

## Pull Request Guidelines
1. Keep core logic (geometry, color analysis, profile rules, package validation) decoupled from WPF UI in `DuoWall.Core` and covered by unit tests in `DuoWall.Tests`.
2. Never commit credentials, API keys, `.db` files, downloaded wallpaper collections, or build outputs (`bin/`, `obj/`, `publish/`).
3. Ensure `dotnet test DuoWall.sln --configuration Release` passes with zero warnings/errors before opening a PR.
4. Imported widget packages (`.dwwp`) must remain strictly declarative with zero executable/script payloads.

