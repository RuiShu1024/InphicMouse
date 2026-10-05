<#
  build-installer.ps1 — 生成 InphicMouse 安装包（支持自定义安装目录）
    - artifacts\InphicMouse-Setup.msi  ：标准 Windows 安装包（带向导，可选择安装目录）
    - artifacts\InphicMouse-Setup\     ：免管理员脚本版（install.cmd 会提示输入安装目录）
  依赖：dotnet tool install --global wix --version 5.*
        wix extension add -g WixToolset.UI.wixext/5.0.2
#>
param(
    [string]$MSBuild = "C:\BuildTools\MSBuild\Current\Bin\MSBuild.exe",
    [switch]$SkipPublish,
    [switch]$SkipFolder
)

$ErrorActionPreference = 'Stop'
$root = [System.IO.Path]::GetFullPath("$PSScriptRoot\..")
$pub  = Join-Path $root "src\InphicMouse.App\bin\x64\Release\net9.0-windows10.0.19041.0\win-x64\publish"
$art  = Join-Path $root "artifacts"
$ico  = Join-Path $root "src\InphicMouse.App\Assets\InphicMouse.ico"
$msi  = Join-Path $art "InphicMouse-Setup.msi"
$wxs  = Join-Path $art "InphicMouse.wxs"
$rtf  = Join-Path $art "license.rtf"

if (-not (Test-Path (Join-Path $pub 'InphicMouse.App.exe'))) {
    if ($SkipPublish) { throw "未找到发布产物：$pub" }
    Write-Host "[1/3] 发布 Release 便携版…"
    & powershell -ExecutionPolicy Bypass -File (Join-Path $PSScriptRoot 'publish-portable.ps1')
    if ($LASTEXITCODE -ne 0) { throw "发布失败" }
} else {
    Write-Host "[1/3] 使用已有发布产物"
}
if (-not (Test-Path $art)) { New-Item -ItemType Directory -Force -Path $art | Out-Null }

# ---- 许可页文本（RTF，中文用 \uN 转义，避免编码问题）----
$licenseText = '本软件为 Inphic IN9 系列鼠标的非官方配置工具，按“原样”提供，不附带任何明示或暗示的担保。' +
               '使用本软件即表示你理解：修改鼠标设置存在一定风险，请自行评估并承担后果。'
$sb = New-Object System.Text.StringBuilder
foreach ($ch in $licenseText.ToCharArray()) {
    $code = [int]$ch
    if ($code -lt 128) { [void]$sb.Append($ch) } else { [void]$sb.Append("\u$code?") }
}
$rtfContent = "{\rtf1\ansi\deff0{\fonttbl{\f0\fcharset134 Microsoft YaHei;}}\viewkind4\uc1\pard\f0\fs20 $($sb.ToString())\par}"
[System.IO.File]::WriteAllText($rtf, $rtfContent, [System.Text.Encoding]::ASCII)

# ---------- MSI（WiX）----------
Write-Host "[2/3] 生成 MSI…"
$wix = Join-Path $env:USERPROFILE ".dotnet\tools\wix.exe"
if (-not (Test-Path $wix)) {
    $cmd = Get-Command wix.exe -ErrorAction SilentlyContinue
    if ($cmd) { $wix = $cmd.Source } else { throw "未找到 wix 工具，请先执行: dotnet tool install --global wix --version 5.*" }
}

$wxsContent = @"
<?xml version="1.0" encoding="utf-8"?>
<Wix xmlns="http://wixtoolset.org/schemas/v4/wxs"
     xmlns:ui="http://wixtoolset.org/schemas/v4/wxs/ui">
  <Package Name="Inphic Mouse"
           Manufacturer="Inphic Mouse"
           Version="1.0.0"
           UpgradeCode="B7A3F1E2-9C4D-4E7A-8F1B-2D6C3A5E9B41"
           Scope="perUser"
           Codepage="936"
           Compressed="yes">

    <SummaryInformation Description="Inphic IN9 系列鼠标配置工具（重写版）" Manufacturer="Inphic Mouse" />

    <MajorUpgrade AllowSameVersionUpgrades="yes" DowngradeErrorMessage="已安装更新版本的 Inphic Mouse。" />
    <MediaTemplate EmbedCab="yes" />

    <Icon Id="AppIcon" SourceFile="$ico" />
    <Property Id="ARPPRODUCTICON" Value="AppIcon" />
    <Property Id="ARPNOREPAIR" Value="1" />
    <Property Id="ARPCOMMENTS" Value="Inphic IN9 系列鼠标配置工具（重写版）" />

    <!-- 安装向导：允许用户选择安装目录 -->
    <WixVariable Id="WixUILicenseRtf" Value="$rtf" />
    <ui:WixUI Id="WixUI_InstallDir" />
    <Property Id="WIXUI_INSTALLDIR" Value="INSTALLFOLDER" />

    <StandardDirectory Id="LocalAppDataFolder">
      <Directory Id="ProgramsDir" Name="Programs">
        <Directory Id="INSTALLFOLDER" Name="InphicMouse" />
      </Directory>
    </StandardDirectory>
    <StandardDirectory Id="ProgramMenuFolder">
      <Directory Id="AppMenuDir" Name="Inphic Mouse" />
    </StandardDirectory>
    <StandardDirectory Id="DesktopFolder" />

    <Feature Id="Main" Title="Inphic Mouse" Level="1">
      <ComponentGroupRef Id="AppFiles" />
      <ComponentRef Id="StartMenuShortcut" />
      <ComponentRef Id="DesktopShortcut" />
      <ComponentRef Id="UserDataCleanup" />
    </Feature>
  </Package>

  <Fragment>
    <ComponentGroup Id="AppFiles" Directory="INSTALLFOLDER">
      <Files Include="$pub\**" />
    </ComponentGroup>
  </Fragment>

  <Fragment>
    <Component Id="StartMenuShortcut" Directory="AppMenuDir" Guid="*">
      <Shortcut Id="StartMenuLnk" Name="Inphic Mouse" Target="[INSTALLFOLDER]InphicMouse.App.exe"
                WorkingDirectory="INSTALLFOLDER" Icon="AppIcon" Description="Inphic Mouse 配置工具" />
      <RemoveFolder Id="RemoveAppMenuDir" On="uninstall" />
      <RegistryValue Root="HKCU" Key="Software\InphicMouse" Name="StartMenu" Type="integer" Value="1" KeyPath="yes" />
    </Component>
    <!-- 卸载时清掉程序自己写在安装目录里的数据文件，否则目录残留 -->
    <Component Id="UserDataCleanup" Directory="INSTALLFOLDER" Guid="*">
      <RemoveFile Id="RemoveMacrosJson" Name="macros.json" On="uninstall" />
      <RemoveFile Id="RemoveSettingsJson" Name="settings.json" On="uninstall" />
      <RegistryValue Root="HKCU" Key="Software\InphicMouse" Name="UserData" Type="integer" Value="1" KeyPath="yes" />
    </Component>
    <Component Id="DesktopShortcut" Directory="DesktopFolder" Guid="*">
      <Shortcut Id="DesktopLnk" Name="Inphic Mouse" Target="[INSTALLFOLDER]InphicMouse.App.exe"
                WorkingDirectory="INSTALLFOLDER" Icon="AppIcon" Description="Inphic Mouse 配置工具" />
      <RegistryValue Root="HKCU" Key="Software\InphicMouse" Name="Desktop" Type="integer" Value="1" KeyPath="yes" />
    </Component>
  </Fragment>
</Wix>
"@
[System.IO.File]::WriteAllText($wxs, $wxsContent, (New-Object System.Text.UTF8Encoding($true)))
if (Test-Path $msi) { Remove-Item -LiteralPath $msi -Force }

& $wix build $wxs -arch x64 -ext WixToolset.UI.wixext -o $msi
if ($LASTEXITCODE -ne 0 -or -not (Test-Path $msi)) { throw "WiX 生成 MSI 失败" }
Write-Host "      MSI = $([math]::Round((Get-Item $msi).Length/1MB,1)) MB"

# ---------- 脚本版 ----------
if (-not $SkipFolder) {
    Write-Host "[3/3] 生成脚本版安装包…"
    $setupDir = Join-Path $art "InphicMouse-Setup"
    if (Test-Path $setupDir) { Remove-Item -LiteralPath $setupDir -Recurse -Force }
    New-Item -ItemType Directory -Force -Path $setupDir | Out-Null
    $zip = Join-Path $setupDir 'payload.zip'
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    [System.IO.Compression.ZipFile]::CreateFromDirectory($pub, $zip, [System.IO.Compression.CompressionLevel]::Optimal, $false)
    $instDir = Join-Path $PSScriptRoot 'installer'
    Copy-Item -LiteralPath (Join-Path $instDir 'install.ps1')   -Destination $setupDir -Force
    Copy-Item -LiteralPath (Join-Path $instDir 'uninstall.ps1') -Destination $setupDir -Force
    Copy-Item -LiteralPath (Join-Path $instDir 'install.cmd')   -Destination $setupDir -Force
}

Write-Host ""
Write-Host "完成："
Write-Host "  安装包(MSI)  : $msi    <- 双击安装，向导里可改安装目录"
Write-Host "  脚本版       : $setupDir\（双击 install.cmd，会提示输入安装目录）"
Write-Host ""
Write-Host "  静默安装(默认目录): msiexec /i `"$msi`" /qn"
Write-Host "  静默安装(自定义)  : msiexec /i `"$msi`" /qn INSTALLFOLDER=`"D:\Apps\InphicMouse`""
Write-Host "  静默卸载          : msiexec /x `"$msi`" /qn"