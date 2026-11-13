param(
    [string]$OutputDir = "$PSScriptRoot\..\docs\screenshots"
)

Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
Add-Type -AssemblyName System.Drawing
Add-Type -AssemblyName System.Windows.Forms

$pinvoke = @"
using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Threading;

public class Win32Capture {
    [DllImport("user32.dll")]
    public static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    public static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    public static void SaveWindowShot(IntPtr hWnd, string path) {
        SetForegroundWindow(hWnd);
        ShowWindow(hWnd, 9);
        Thread.Sleep(800);
        RECT r;
        GetWindowRect(hWnd, out r);
        int w = r.Right - r.Left;
        int h = r.Bottom - r.Top;
        if (w <= 50 || h <= 50) return;
        using (Bitmap bmp = new Bitmap(w, h)) {
            using (Graphics g = Graphics.FromImage(bmp)) {
                g.CopyFromScreen(r.Left, r.Top, 0, 0, new Size(w, h), CopyPixelOperation.SourceCopy);
            }
            bmp.Save(path, ImageFormat.Png);
        }
    }
}
"@
Add-Type -TypeDefinition $pinvoke -ReferencedAssemblies "System.Drawing.dll"

New-Item -ItemType Directory -Force -Path $OutputDir | Out-Null

# Stop any running DuoWall
Get-Process -Name DuoWall -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Milliseconds 600

$exePath = "$PSScriptRoot\..\publish\DuoWall-Portable\DuoWall.exe"
if (-not (Test-Path $exePath)) {
    $exePath = "$PSScriptRoot\..\src\DuoWall.App\bin\Release\net8.0-windows\DuoWall.exe"
}

Write-Host "Launching DuoWall from: $exePath" -ForegroundColor Cyan
$proc = Start-Process -FilePath $exePath -PassThru

# Wait up to 15s for main window
$hWnd = [IntPtr]::Zero
for ($i = 0; $i -lt 30; $i++) {
    Start-Sleep -Milliseconds 500
    $proc.Refresh()
    if ($proc.MainWindowHandle -ne [IntPtr]::Zero) {
        $hWnd = $proc.MainWindowHandle
        break
    }
}

if ($hWnd -eq [IntPtr]::Zero) {
    Write-Warning "Could not find main window handle."
    $proc.Kill()
    exit 1
}

Write-Host "MainWindow handle: $hWnd" -ForegroundColor Green
Start-Sleep -Seconds 3

# 1. Capture Gallery
[Win32Capture]::SaveWindowShot($hWnd, "$OutputDir\01_wallpaper_gallery.png")
Write-Host "Captured 01_wallpaper_gallery.png" -ForegroundColor Green

# Use UI Automation to click navigation items
$rootElement = [System.Windows.Automation.AutomationElement]::FromHandle($hWnd)

# Find radio buttons for workspace pages
$radioCond = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ControlTypeProperty, [System.Windows.Automation.ControlType]::RadioButton)
$radios = $rootElement.FindAll([System.Windows.Automation.TreeScope]::Descendants, $radioCond)
Write-Host "Found $($radios.Count) radio buttons" -ForegroundColor Cyan

if ($radios.Count -ge 4) {
    # 2. Placement Studio
    try {
        $sel = $radios[1].GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern)
        $sel.Select()
        Start-Sleep -Seconds 2
        [Win32Capture]::SaveWindowShot($hWnd, "$OutputDir\02_placement_studio.png")
        Write-Host "Captured 02_placement_studio.png" -ForegroundColor Green
    } catch { Write-Warning $_ }

    # 3. Widget Marketplace
    try {
        $sel = $radios[2].GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern)
        $sel.Select()
        Start-Sleep -Seconds 2
        [Win32Capture]::SaveWindowShot($hWnd, "$OutputDir\03_widget_marketplace.png")
        Write-Host "Captured 03_widget_marketplace.png" -ForegroundColor Green
    } catch { Write-Warning $_ }

    # 4. Taskbar Tweaks
    try {
        $sel = $radios[3].GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern)
        $sel.Select()
        Start-Sleep -Seconds 2
        [Win32Capture]::SaveWindowShot($hWnd, "$OutputDir\04_taskbar_tweaks.png")
        Write-Host "Captured 04_taskbar_tweaks.png" -ForegroundColor Green
    } catch { Write-Warning $_ }
}

# 5. About Me Dialog
$btnCond = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ControlTypeProperty, [System.Windows.Automation.ControlType]::Button)
$btns = $rootElement.FindAll([System.Windows.Automation.TreeScope]::Descendants, $btnCond)
foreach ($b in $btns) {
    if ($b.Current.Name -like "*About Me*") {
        try {
            $inv = $b.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern)
            $inv.Invoke()
            Start-Sleep -Seconds 2

            $scope = [System.Windows.Automation.TreeScope]::Children
            $nameCond = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::NameProperty, "About DuoWall by SRMD")
            $aboutElem = [System.Windows.Automation.AutomationElement]::RootElement.FindFirst($scope, $nameCond)
            if ($aboutElem) {
                $aboutHwnd = [IntPtr]$aboutElem.Current.NativeWindowHandle
                [Win32Capture]::SaveWindowShot($aboutHwnd, "$OutputDir\05_about_me_dialog.png")
                Write-Host "Captured 05_about_me_dialog.png" -ForegroundColor Green

                $descScope = [System.Windows.Automation.TreeScope]::Descendants
                $closeCond = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::NameProperty, "Close")
                $closeBtn = $aboutElem.FindFirst($descScope, $closeCond)
                if ($closeBtn) {
                    $closeInv = $closeBtn.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern)
                    $closeInv.Invoke()
                }
            }
        } catch { Write-Warning $_ }
        break
    }
}

# Close DuoWall
$proc.CloseMainWindow()
Start-Sleep -Seconds 1
if (-not $proc.HasExited) {
    $proc.Kill()
}
Write-Host "Screenshots captured successfully in: $OutputDir" -ForegroundColor Green
