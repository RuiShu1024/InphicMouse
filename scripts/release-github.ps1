<#
  release-github.ps1 — 把本项目推送到 GitHub 并创建 Release（附带安装包与免安装版）

  前置：
    1) 已安装并登录 GitHub CLI：  gh auth login
    2) 已生成发布产物（没有就加 -Build 自动生成）：
         artifacts\InphicMouse-Setup.msi
         artifacts\InphicMouse-portable.zip

  用法：
    powershell -ExecutionPolicy Bypass -File scripts\release-github.ps1 -Repo InphicMouse -Tag v1.0.0 -Build
#>
param(
    [string]$Repo = "InphicMouse",
    [string]$Tag  = "v1.0.0",
    [string]$Title = "Inphic Mouse v1.0.0",
    [string]$Description = "Inphic IN9 系列鼠标的现代化 WinUI 3 配置工具（协议层复刻原厂软件，无需驱动）",
    [switch]$Private,
    [switch]$Build,
    [switch]$SkipPush
)

$ErrorActionPreference = 'Stop'
$root = [System.IO.Path]::GetFullPath("$PSScriptRoot\..")
$art  = Join-Path $root 'artifacts'
$msi  = Join-Path $art 'InphicMouse-Setup.msi'
$zip  = Join-Path $art 'InphicMouse-portable.zip'
$notes = Join-Path $art 'release-notes.md'

# ---- gh ----
$gh = (Get-Command gh.exe -ErrorAction SilentlyContinue).Source
if (-not $gh) { $gh = "C:\Program Files\GitHub CLI\gh.exe" }
if (-not (Test-Path $gh)) { throw "未找到 GitHub CLI(gh)。请先安装: winget install --id GitHub.cli -e" }

& $gh auth status 2>&1 | Out-Null
if ($LASTEXITCODE -ne 0) { throw "尚未登录 GitHub。请先运行:  `"$gh`" auth login" }

# ---- 产物 ----
if ($Build -or -not (Test-Path $msi) -or -not (Test-Path $zip)) {
    Write-Host "[1/4] 生成发布产物（便携版 + 安装包）…"
    & powershell -ExecutionPolicy Bypass -File (Join-Path $PSScriptRoot 'publish-portable.ps1')
    if ($LASTEXITCODE -ne 0) { throw "便携版打包失败" }
    & powershell -ExecutionPolicy Bypass -File (Join-Path $PSScriptRoot 'build-installer.ps1') -SkipPublish
    if ($LASTEXITCODE -ne 0) { throw "安装包生成失败" }
} else {
    Write-Host "[1/4] 使用已有发布产物"
}
if (-not (Test-Path $msi)) { throw "缺少 $msi" }
if (-not (Test-Path $zip)) { throw "缺少 $zip" }

# ---- Release 说明 ----
$notesText = @"
## 下载

| 文件 | 说明 |
|------|------|
| ``InphicMouse-Setup.msi`` | 安装包（推荐）：带向导、**可自定义安装目录**，可正常在「设置 → 应用」卸载 |
| ``InphicMouse-portable.zip`` | 免安装版：解压后双击 ``InphicMouse.App.exe`` 即用 |

两者都是**自包含**的，目标机无需安装 .NET / Windows App SDK。

> ⚠️ **仅实测 Inphic IN9 升级版**（ic 0x11）一只鼠标。其它型号/固件未验证，协议参数可能不同，
> 请先备份原厂设置、自行评估风险。

## 主要功能
- 设备信息 + **实机诊断**（逐条读 0x10~0x20，原始字节 + 解码 + 一键复制）
- DPI（档位数 1–6、各档数值与颜色、当前档）
- 回报率 125/250/500/1000 Hz
- 灯光（模式 / 亮度 / 速度 / 常亮颜色）
- 鼠标按键映射（鼠标功能 / DPI / 多媒体 / 宏 / 关闭 / 键盘快捷键）
- 快捷指令（宏）编辑器：录制、拖动排序、双击编辑、导出/导入 JSON
- 主题：跟随系统 / 亮色 / 暗色

## 说明
- 未签名：首次运行 SmartScreen / Smart App Control 可能提示，需手动允许。
- 程序数据（``macros.json`` / ``settings.json``）优先放程序目录，不可写时自动用 ``%APPDATA%\InphicMouse``。
"@
[System.IO.File]::WriteAllText($notes, $notesText, (New-Object System.Text.UTF8Encoding($false)))

# ---- 仓库 ----
Write-Host "[2/4] 检查远程仓库…"
Push-Location $root
try {
    $exists = $true
    & $gh repo view $Repo 2>&1 | Out-Null
    if ($LASTEXITCODE -ne 0) { $exists = $false }

    if (-not $exists) {
        Write-Host "      创建仓库 $Repo …"
        $vis = if ($Private) { '--private' } else { '--public' }
        & $gh repo create $Repo $vis --description $Description --source . --remote origin --push
        if ($LASTEXITCODE -ne 0) { throw "创建仓库失败" }
    } else {
        Write-Host "      仓库已存在，推送到 origin …"
        $hasRemote = (& git remote) -contains 'origin'
        if (-not $hasRemote) {
            $login = (& $gh api user --jq .login).Trim()
            & git remote add origin "https://github.com/$login/$Repo.git"
        }
        if (-not $SkipPush) {
            & git push -u origin HEAD
            if ($LASTEXITCODE -ne 0) { throw "推送失败" }
        }
    }

    # ---- Release ----
    Write-Host "[3/4] 创建 Release $Tag …"
    $asset1 = $msi; $asset2 = $zip
    & $gh release view $Tag 2>&1 | Out-Null
    if ($LASTEXITCODE -eq 0) {
        Write-Host "      Release 已存在，追加/覆盖附件…"
        & $gh release upload $Tag $asset1 $asset2 --clobber
    } else {
        & $gh release create $Tag $asset1 $asset2 --title $Title --notes-file $notes
    }
    if ($LASTEXITCODE -ne 0) { throw "创建 Release 失败" }

    $login = (& $gh api user --jq .login).Trim()
    Write-Host "[4/4] 完成"
    Write-Host ""
    Write-Host "  仓库   : https://github.com/$login/$Repo"
    Write-Host "  Release: https://github.com/$login/$Repo/releases/tag/$Tag"
} finally {
    Pop-Location
}