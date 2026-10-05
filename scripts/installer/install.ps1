<#
  InphicMouse 安装脚本（每用户安装，无需管理员）
  - 安装到 %LOCALAPPDATA%\Programs\InphicMouse
  - 创建「开始菜单」和「桌面」快捷方式
  - 注册到「设置 → 应用」的卸载列表
#>
param(
    # 自定义安装目录；留空则用默认 %LOCALAPPDATA%\Programs\InphicMouse
    [string]$InstallDir
)

$ErrorActionPreference = 'Stop'

$src    = $PSScriptRoot
$dest   = if ([string]::IsNullOrWhiteSpace($InstallDir)) { Join-Path $env:LOCALAPPDATA 'Programs\InphicMouse' } else { $InstallDir }
$appExe = 'InphicMouse.App.exe'
$zip    = Join-Path $src 'payload.zip'
$unps   = Join-Path $src 'uninstall.ps1'

Write-Host ''
Write-Host '==========================================' -ForegroundColor Cyan
Write-Host '  Inphic Mouse  安装程序' -ForegroundColor Cyan
Write-Host '==========================================' -ForegroundColor Cyan
Write-Host "安装位置: $dest"
Write-Host ''

if (-not (Test-Path -LiteralPath $zip)) { throw "缺少 payload.zip，安装包不完整。" }

# 关闭正在运行的实例
Get-Process 'InphicMouse.App' -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
Start-Sleep -Milliseconds 400

# 解压到临时目录
$stage = Join-Path $env:TEMP ('InphicMouse_stage_' + [guid]::NewGuid().ToString('N'))
if (Test-Path -LiteralPath $stage) { Remove-Item -LiteralPath $stage -Recurse -Force }
Write-Host '正在解压程序文件…'
Expand-Archive -LiteralPath $zip -DestinationPath $stage -Force

# 复制到安装目录
New-Item -ItemType Directory -Force -Path $dest | Out-Null
Copy-Item -Path (Join-Path $stage '*') -Destination $dest -Recurse -Force
Remove-Item -LiteralPath $stage -Recurse -Force -ErrorAction SilentlyContinue

$exePath = Join-Path $dest $appExe
if (-not (Test-Path -LiteralPath $exePath)) { throw "安装失败：未找到 $appExe" }

# 卸载器（随程序一起安装）
Copy-Item -LiteralPath $unps -Destination (Join-Path $dest 'uninstall.ps1') -Force
$uninsCmd = Join-Path $dest 'uninstall.cmd'
Set-Content -LiteralPath $uninsCmd -Encoding ASCII -Value @"
@echo off
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0uninstall.ps1"
"@

# 快捷方式
function New-Shortcut([string]$path) {
    New-Item -ItemType Directory -Force -Path (Split-Path $path -Parent) | Out-Null
    $ws = New-Object -ComObject WScript.Shell
    $sc = $ws.CreateShortcut($path)
    $sc.TargetPath = $exePath
    $sc.WorkingDirectory = $dest
    $sc.IconLocation = "$exePath,0"
    $sc.Description = 'Inphic Mouse 配置工具'
    $sc.Save()
}
$smLink   = Join-Path $env:APPDATA 'Microsoft\Windows\Start Menu\Programs\Inphic Mouse.lnk'
$deskLink = Join-Path ([Environment]::GetFolderPath('Desktop')) 'Inphic Mouse.lnk'
New-Shortcut $smLink
New-Shortcut $deskLink

# 「设置 → 应用」卸载项
$sizeKb = [int](((Get-ChildItem -Recurse -File -LiteralPath $dest | Measure-Object Length -Sum).Sum) / 1KB)
$key = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\InphicMouse'
New-Item -Path $key -Force | Out-Null
Set-ItemProperty -Path $key -Name 'DisplayName'     -Value 'Inphic Mouse'
Set-ItemProperty -Path $key -Name 'DisplayVersion'  -Value '1.0.0'
Set-ItemProperty -Path $key -Name 'Publisher'       -Value 'Inphic Mouse'
Set-ItemProperty -Path $key -Name 'InstallLocation' -Value $dest
Set-ItemProperty -Path $key -Name 'DisplayIcon'     -Value "$exePath,0"
Set-ItemProperty -Path $key -Name 'UninstallString' -Value "`"$uninsCmd`""
Set-ItemProperty -Path $key -Name 'QuietUninstallString' -Value "`"$uninsCmd`""
Set-ItemProperty -Path $key -Name 'NoModify' -Value 1 -Type DWord
Set-ItemProperty -Path $key -Name 'NoRepair' -Value 1 -Type DWord
Set-ItemProperty -Path $key -Name 'EstimatedSize' -Value $sizeKb -Type DWord

Write-Host '安装完成！' -ForegroundColor Green
Write-Host "  程序      : $exePath"
Write-Host "  开始菜单  : $smLink"
Write-Host "  桌面      : $deskLink"
Write-Host '  卸载      : 设置 → 应用 → Inphic Mouse'
Write-Host ''