<#
  verify-ui.ps1 - Launch the built WinUI3 app, confirm the main window appears, save a screenshot.
  Usage: powershell -ExecutionPolicy Bypass -File scripts\verify-ui.ps1
  Exit code: 0 = window appeared and screenshot saved; non-zero = failure.
#>
param(
    [string]$ExePath = "$PSScriptRoot\..\src\InphicMouse.App\bin\x64\Debug\net9.0-windows10.0.19041.0\win-x64\InphicMouse.App.exe",
    [string]$ShotPath = "$PSScriptRoot\..\artifacts\ui-screenshot.png",
    [int]$TimeoutSec = 25,
    [switch]$KeepOpen
)

$ErrorActionPreference = 'Stop'
$ExePath = [System.IO.Path]::GetFullPath($ExePath)
if (-not (Test-Path $ExePath)) { Write-Error "exe not found: $ExePath"; exit 2 }

$dir = Split-Path $ShotPath
if (-not (Test-Path $dir)) { New-Item -ItemType Directory -Force -Path $dir | Out-Null }

Add-Type -AssemblyName System.Drawing
Add-Type @"
using System;
using System.Runtime.InteropServices;
public class NativeWin {
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
    [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr h, IntPtr hdc, uint flags);
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
}
"@

Write-Host "Launching: $ExePath"
$p = Start-Process -FilePath $ExePath -PassThru
$hwnd = [IntPtr]::Zero
$deadline = (Get-Date).AddSeconds($TimeoutSec)

while ((Get-Date) -lt $deadline) {
    Start-Sleep -Milliseconds 500
    if ($p.HasExited) { Write-Error "Process exited early, code $($p.ExitCode)"; exit 3 }
    $p.Refresh()
    if ($p.MainWindowHandle -ne [IntPtr]::Zero) { $hwnd = $p.MainWindowHandle; break }
}

if ($hwnd -eq [IntPtr]::Zero) {
    Write-Error "Timed out waiting for main window"
    if (-not $KeepOpen) { $p.Kill() }
    exit 4
}

Write-Host "Main window appeared (hwnd=$hwnd, title='$($p.MainWindowTitle)')"
Start-Sleep -Milliseconds 1500

try { [NativeWin]::SetForegroundWindow($hwnd) | Out-Null } catch {}
$r = New-Object NativeWin+RECT
[NativeWin]::GetWindowRect($hwnd, [ref]$r) | Out-Null
$w = $r.Right - $r.Left; $h = $r.Bottom - $r.Top
if ($w -le 0 -or $h -le 0) { $w = 1000; $h = 700 }

# PrintWindow captures the window's own content even if occluded/behind other windows.
$bmp = New-Object System.Drawing.Bitmap $w, $h
$g = [System.Drawing.Graphics]::FromImage($bmp)
$hdc = $g.GetHdc()
$ok = [NativeWin]::PrintWindow($hwnd, $hdc, 0x00000002)  # PW_RENDERFULLCONTENT
$g.ReleaseHdc($hdc)
if (-not $ok) {
    # fall back to screen grab
    $g.CopyFromScreen($r.Left, $r.Top, 0, 0, (New-Object System.Drawing.Size($w, $h)))
}
$bmp.Save($ShotPath, [System.Drawing.Imaging.ImageFormat]::Png)
$g.Dispose(); $bmp.Dispose()
$size = "${w}x${h}"
Write-Host "Screenshot saved: $([System.IO.Path]::GetFullPath($ShotPath)) ($size)"

if (-not $KeepOpen) { Start-Sleep -Milliseconds 500; $p.Kill(); Write-Host "App closed" }
exit 0
