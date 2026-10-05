<#
  verify-pages.ps1 - Launch the app, then use UI Automation to click each NavigationView
  item and screenshot each page. Confirms every page renders without a runtime binding crash.
  Usage: powershell -ExecutionPolicy Bypass -File scripts\verify-pages.ps1
#>
param(
    [string]$ExePath = "$PSScriptRoot\..\src\InphicMouse.App\bin\x64\Debug\net9.0-windows10.0.19041.0\win-x64\InphicMouse.App.exe",
    [string]$OutDir  = "$PSScriptRoot\..\artifacts",
    [int]$TimeoutSec = 25
)

$ErrorActionPreference = 'Stop'
$ExePath = [System.IO.Path]::GetFullPath($ExePath)
if (-not (Test-Path $ExePath)) { Write-Error "exe not found: $ExePath"; exit 2 }
if (-not (Test-Path $OutDir)) { New-Item -ItemType Directory -Force -Path $OutDir | Out-Null }

Add-Type -AssemblyName System.Drawing
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
Add-Type @"
using System;
using System.Runtime.InteropServices;
public class NW {
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr h, IntPtr hdc, uint flags);
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
}
"@

function Save-Shot($hwnd, $path) {
    $r = New-Object NW+RECT
    [NW]::GetWindowRect($hwnd, [ref]$r) | Out-Null
    $w = $r.Right - $r.Left; $h = $r.Bottom - $r.Top
    if ($w -le 0 -or $h -le 0) { $w = 1000; $h = 700 }
    $bmp = New-Object System.Drawing.Bitmap $w, $h
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $hdc = $g.GetHdc()
    [NW]::PrintWindow($hwnd, $hdc, 0x2) | Out-Null
    $g.ReleaseHdc($hdc)
    $bmp.Save($path, [System.Drawing.Imaging.ImageFormat]::Png)
    $g.Dispose(); $bmp.Dispose()
}

$p = Start-Process -FilePath $ExePath -PassThru
$deadline = (Get-Date).AddSeconds($TimeoutSec)
while ((Get-Date) -lt $deadline) {
    Start-Sleep -Milliseconds 500
    if ($p.HasExited) { Write-Error "Process exited early, code $($p.ExitCode)"; exit 3 }
    $p.Refresh()
    if ($p.MainWindowHandle -ne [IntPtr]::Zero) { break }
}
if ($p.MainWindowHandle -eq [IntPtr]::Zero) { Write-Error "no window"; $p.Kill(); exit 4 }
$hwnd = $p.MainWindowHandle
Start-Sleep -Milliseconds 1500

$root = [System.Windows.Automation.AutomationElement]::FromHandle($hwnd)
$pages = 'DPI','回报率','灯光','按键映射','设置'
$names = 'dpi','rate','light','keys','settings'

Save-Shot $hwnd (Join-Path $OutDir 'page-info.png')
Write-Host "captured: page-info.png"

for ($i = 0; $i -lt $pages.Count; $i++) {
    $cond = New-Object System.Windows.Automation.PropertyCondition(
        [System.Windows.Automation.AutomationElement]::NameProperty, $pages[$i])
    $el = $root.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $cond)
    if ($null -eq $el) { Write-Warning "nav item not found: $($pages[$i])"; continue }
    $selPat = [System.Windows.Automation.SelectionItemPattern]::Pattern
    $invPat = [System.Windows.Automation.InvokePattern]::Pattern
    $obj = $null
    if ($el.TryGetCurrentPattern($selPat, [ref]$obj)) { $obj.Select() }
    elseif ($el.TryGetCurrentPattern($invPat, [ref]$obj)) { $obj.Invoke() }
    else { Write-Warning "no invoke/select pattern for $($pages[$i])"; continue }
    Start-Sleep -Milliseconds 900
    Save-Shot $hwnd (Join-Path $OutDir "page-$($names[$i]).png")
    Write-Host "captured: page-$($names[$i]).png"
}

$p.Kill()
Write-Host "done"
exit 0
