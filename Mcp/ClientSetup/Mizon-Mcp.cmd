@echo off
setlocal DisableDelayedExpansion
set "MIZON_MCP_EXE="
for /f "usebackq delims=" %%I in (`""%SystemRoot%\System32\WindowsPowerShell\v1.0\powershell.exe" -NoLogo -NoProfile -NonInteractive -ExecutionPolicy Bypass -File "%~dp0Resolve-MizonMcp.ps1""`) do set "MIZON_MCP_EXE=%%I"
if not defined MIZON_MCP_EXE (
    >&2 echo Mizon MCP could not resolve its installed companion.
    exit /b 2
)
"%MIZON_MCP_EXE%"
exit /b %errorlevel%
