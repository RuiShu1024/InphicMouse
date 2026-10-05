@echo off
chcp 936 >nul
setlocal
echo ==========================================
echo   Inphic Mouse  安装程序
echo ==========================================
echo.
set "DEST="
set /p "DEST=请输入安装目录（直接回车用默认 %%LOCALAPPDATA%%\Programs\InphicMouse）: "
echo.
if "%DEST%"=="" (
  powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0install.ps1"
) else (
  powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0install.ps1" -InstallDir "%DEST%"
)
pause