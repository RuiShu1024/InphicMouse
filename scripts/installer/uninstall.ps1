<#
  InphicMouse 卸载脚本
  在安装目录里直接运行：会先把自己复制到临时目录再启动，以便删除安装目录本身。
#>
param([string]$InstallDir)

$ErrorActionPreference = 'SilentlyContinue'

if (-not $InstallDir) {
    $InstallDir = if ($PSScriptRoot) { $PSScriptRoot } else { (Get-Location).Path }
}

# 第一阶段：转到临时目录执行
if ($env:INPHIC_UNINSTALL_RUN -ne '1') {
    $tmp = Join-Path $env:TEMP ('InphicMouse_uninstall_' + [guid]::NewGuid().ToString('N') + '.ps1')
    Copy-Item -LiteralPath $PSCommandPath -Destination $tmp -Force
    $env:INPHIC_UNINSTALL_RUN = '1'
    Start-Process -FilePath 'powershell.exe' -WindowStyle Hidden -ArgumentList @(
        '-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', "`"$tmp`"", '-InstallDir', "`"$InstallDir`""
    )
    exit 0
}

Write-Host ''
Write-Host '==========================================' -ForegroundColor Cyan
Write-Host '  Inphic Mouse  卸载程序' -ForegroundColor Cyan
Write-Host '==========================================' -ForegroundColor Cyan
Write-Host "安装位置: $InstallDir"
Write-Host ''

# 结束正在运行的实例
Get-Process 'InphicMouse.App' -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
Start-Sleep -Milliseconds 700

# 快捷方式
Remove-Item -LiteralPath (Join-Path $env:APPDATA 'Microsoft\Windows\Start Menu\Programs\Inphic Mouse.lnk') -Force
Remove-Item -LiteralPath (Join-Path ([Environment]::GetFolderPath('Desktop')) 'Inphic Mouse.lnk') -Force

# 「设置 → 应用」卸载项
Remove-Item -Path 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\InphicMouse' -Recurse -Force

# 安装目录（重试几次，等进程彻底退出）
for ($i = 0; $i -lt 5; $i++) {
    if (-not (Test-Path -LiteralPath $InstallDir)) { break }
    Remove-Item -LiteralPath $InstallDir -Recurse -Force
    Start-Sleep -Milliseconds 400
}

Write-Host '卸载完成。' -ForegroundColor Green
Start-Sleep -Milliseconds 600
Remove-Item -LiteralPath $PSCommandPath -Force