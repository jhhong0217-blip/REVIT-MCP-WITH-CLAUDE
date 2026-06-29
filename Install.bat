@echo off
setlocal
chcp 437 > nul

set "ROOT=%~dp0"
set "PROJ=%ROOT%RevitMCP.Addin\RevitMCP.Addin.csproj"
set "DOTNET=%ProgramFiles%\dotnet\dotnet.exe"

echo.
echo  =====================================================
echo    RevitMCP Installer
echo  =====================================================
echo.

:: ── .NET SDK check / auto install ────────────────────────────────
set "SDK_OK=0"
"%DOTNET%" --version >nul 2>&1
if not errorlevel 1 set "SDK_OK=1"

if "%SDK_OK%"=="0" (
    echo [INFO] .NET SDK not found. Downloading...
    echo        (~200MB - please wait)
    echo.
    curl -L -o "%TEMP%\dotnet-sdk-installer.exe" "https://aka.ms/dotnet/8.0/dotnet-sdk-win-x64.exe"
    if errorlevel 1 (
        echo [ERROR] Download failed. Check internet connection.
        goto :END
    )
    echo [INFO] Installing .NET SDK (run as Administrator if this fails)...
    "%TEMP%\dotnet-sdk-installer.exe" /install /quiet /norestart
    if errorlevel 1 (
        echo [ERROR] Install failed. Right-click Install.bat and choose "Run as administrator".
        del "%TEMP%\dotnet-sdk-installer.exe" >nul 2>&1
        goto :END
    )
    del "%TEMP%\dotnet-sdk-installer.exe" >nul 2>&1
    echo [OK] .NET SDK installed.
    echo.
)

for /f "tokens=*" %%v in ('"%DOTNET%" --version 2^>nul') do set "SDKVER=%%v"
echo [OK] .NET SDK %SDKVER%

:: ── Detect Revit ─────────────────────────────────────────────────
echo.
set "HAS2025=0"
set "HAS2026=0"
set "HAS2027=0"
if exist "C:\Program Files\Autodesk\Revit 2025\Revit.exe" set "HAS2025=1"
if exist "C:\Program Files\Autodesk\Revit 2026\Revit.exe" set "HAS2026=1"
if exist "C:\Program Files\Autodesk\Revit 2027\Revit.exe" set "HAS2027=1"

if "%HAS2025%"=="1" echo [OK] Revit 2025 found
if "%HAS2026%"=="1" echo [OK] Revit 2026 found
if "%HAS2027%"=="1" echo [OK] Revit 2027 found

if "%HAS2025%%HAS2026%%HAS2027%"=="000" (
    echo [ERROR] No Revit installation found.
    goto :END
)

:: ── Version menu ─────────────────────────────────────────────────
echo.
echo  Select version to install:
if "%HAS2025%"=="1" echo    [1] Revit 2025
if "%HAS2026%"=="1" echo    [2] Revit 2026
if "%HAS2027%"=="1" echo    [3] Revit 2027
echo    [4] Install All
echo.
set /p SEL= Number:

if "%SEL%"=="1" goto :DO1
if "%SEL%"=="2" goto :DO2
if "%SEL%"=="3" goto :DO3
if "%SEL%"=="4" goto :DO4
echo [ERROR] Invalid selection.
goto :END

:DO1
call :BUILD 2025
goto :MCP

:DO2
call :BUILD 2026
goto :MCP

:DO3
call :BUILD 2027
goto :MCP

:DO4
if "%HAS2025%"=="1" call :BUILD 2025
if "%HAS2026%"=="1" call :BUILD 2026
if "%HAS2027%"=="1" call :BUILD 2027
goto :MCP

:: ── Claude Desktop MCP register ──────────────────────────────────
:MCP
echo.
echo [INFO] Registering Claude Desktop MCP...
set "CFGDIR=%APPDATA%\Claude"
set "CFG=%CFGDIR%\claude_desktop_config.json"
if not exist "%CFGDIR%" mkdir "%CFGDIR%"

(
echo {
echo   "mcpServers": {
echo     "revit-mcp": {
echo       "transport": "http",
echo       "url": "http://localhost:9876/"
echo     }
echo   }
echo }
) > "%CFG%"

echo [OK] Claude Desktop MCP registered.

echo.
echo  =====================================================
echo   Done! Next steps:
echo    1. Restart Revit
echo    2. RevitMCP tab - click [Start MCP]
echo    3. Use Claude Desktop to automate Revit!
echo  =====================================================
goto :END

:: ── BUILD subroutine ─────────────────────────────────────────────
:BUILD
echo.
echo  ----- Building Revit %1 -----
set "OUT=%ROOT%RevitMCP.Addin\bin\%1"
set "DST=%APPDATA%\Autodesk\Revit\Addins\%1\RevitMCP"
set "ADDIN_SRC=%ROOT%addin\RevitMCP.%1.addin"
set "ADDIN_DST=%APPDATA%\Autodesk\Revit\Addins\%1\RevitMCP.addin"

"%DOTNET%" build "%PROJ%" /p:RevitVersion=%1 /p:Configuration=Release /p:OutputPath="%OUT%"
if errorlevel 1 (
    echo [ERROR] Build failed for Revit %1
    goto :EOF
)
echo [OK] Build succeeded.

if not exist "%DST%" mkdir "%DST%"
xcopy /E /Y /Q "%OUT%\*" "%DST%\" >nul
echo [OK] DLL copied.

if not exist "%APPDATA%\Autodesk\Revit\Addins\%1" mkdir "%APPDATA%\Autodesk\Revit\Addins\%1"
copy /Y "%ADDIN_SRC%" "%ADDIN_DST%" >nul
echo [OK] Addin registered.
goto :EOF

:END
echo.
pause
