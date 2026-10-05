<#
  publish-portable.ps1 - Build a self-contained, install-free "portable" (便捷版) of the app,
  bundle model metadata, and zip it for copying to the test machine.

  Fixes the known WinUI3 unpackaged-publish quirk where the app's own compiled
  resources (InphicMouse.App.pri) are NOT copied into the publish folder, which
  otherwise crashes the app at startup (STATUS_STOWED_EXCEPTION 0xC000027B).

  Usage: powershell -ExecutionPolicy Bypass -File scripts\publish-portable.ps1
#>
param(
    [string]$MSBuild = "C:\BuildTools\MSBuild\Current\Bin\MSBuild.exe",
    [string]$Config  = "Release"
)

$ErrorActionPreference = 'Stop'
$root    = [System.IO.Path]::GetFullPath("$PSScriptRoot\..")
$csproj  = Join-Path $root "src\InphicMouse.App\InphicMouse.App.csproj"
$outRoot = Join-Path $root "src\InphicMouse.App\bin\x64\$Config\net9.0-windows10.0.19041.0\win-x64"
$pub     = Join-Path $outRoot "publish"
$artDir  = Join-Path $root "artifacts"
$zip     = Join-Path $artDir "InphicMouse-portable.zip"

Write-Host "[1/5] MSBuild publish ($Config, self-contained x64)..."
& $MSBuild $csproj /t:Restore,Publish /p:Configuration=$Config /p:Platform=x64 `
    /p:RuntimeIdentifier=win-x64 /p:SelfContained=true /p:WindowsAppSDKSelfContained=true `
    /v:m /nologo
if ($LASTEXITCODE -ne 0) { Write-Error "MSBuild failed ($LASTEXITCODE)"; exit 1 }

Write-Host "[2/5] Copying app resources (InphicMouse.App.pri) into publish..."
$pri = Join-Path $outRoot "InphicMouse.App.pri"
if (-not (Test-Path $pri)) { Write-Error "PRI not found: $pri"; exit 2 }
Copy-Item $pri $pub -Force

Write-Host "[3/5] Bundling model metadata..."
$src = "C:\Program Files (x86)\Inphic Mouse"
if (Test-Path (Join-Path $src 'config.xml')) {
    $meta = Join-Path $pub 'Metadata'
    New-Item -ItemType Directory -Force -Path (Join-Path $meta 'device') | Out-Null
    Copy-Item (Join-Path $src 'config.xml') $meta -Force
    Copy-Item (Join-Path $src 'device\*.xml') (Join-Path $meta 'device') -Force
    Write-Host "      bundled config.xml + device xmls"
} else {
    Write-Warning "      original program not found; app will use built-in default model"
}

Write-Host "[3.5/5] Pruning unused language packs (WinUI control MUI resources)..."
# Self-contained publish ships 80+ locale folders (WinUI built-in control .mui only).
# Keep Chinese + English (must keep the active UI language, else WinUI crashes at startup);
# delete the rest to declutter. Runtime dlls must stay next to the exe and are NOT moved.
$keepLang = @('zh-CN','zh-TW','zh-Hans','zh-Hant','en-US','en-us','en-GB')
$removed = 0
foreach ($d in Get-ChildItem $pub -Directory) {
    $isLangPack = Get-ChildItem $d.FullName -Filter *.mui -File -Recurse -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($isLangPack -and ($keepLang -notcontains $d.Name)) { Remove-Item $d.FullName -Recurse -Force; $removed++ }
}
Write-Host "      removed $removed language folders (kept: $($keepLang -join ', '))"

Write-Host "[4/5] Smoke test (launch + window check)..."
Get-Process InphicMouse.App -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
Start-Sleep -Milliseconds 400
& (Join-Path $PSScriptRoot 'verify-ui.ps1') -ExePath (Join-Path $pub 'InphicMouse.App.exe') `
    -ShotPath (Join-Path $artDir 'portable-check.png')
if ($LASTEXITCODE -ne 0) { Write-Error "Smoke test failed ($LASTEXITCODE)"; exit 3 }

Write-Host "[5/5] Zipping portable package..."
if (-not (Test-Path $artDir)) { New-Item -ItemType Directory -Force -Path $artDir | Out-Null }
if (Test-Path $zip) { Remove-Item $zip -Force }
Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem
# Pack everything under a top-level InphicMouse/ folder so extraction yields one clean dir.
$archive = [System.IO.Compression.ZipFile]::Open($zip, 'Create')
$baseLen = ((Resolve-Path $pub).Path.TrimEnd('\')).Length + 1
$level = [System.IO.Compression.CompressionLevel]::Optimal
foreach ($f in Get-ChildItem $pub -Recurse -File) {
    $rel = $f.FullName.Substring($baseLen) -replace '\\','/'
    $entry = $archive.CreateEntry("InphicMouse/$rel", $level)
    $dst = $entry.Open()
    $srcStream = [System.IO.File]::OpenRead($f.FullName)
    $srcStream.CopyTo($dst)
    $srcStream.Dispose(); $dst.Dispose()
}
$archive.Dispose()
$zmb = [math]::Round((Get-Item $zip).Length/1MB,1)

Write-Host ""
Write-Host "DONE. Portable package:"
Write-Host "  folder: $pub"
Write-Host "  zip   : $zip ($zmb MB)"
exit 0
