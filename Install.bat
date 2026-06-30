@echo off
setlocal EnableDelayedExpansion
chcp 437 >nul

set "ROOT=%~dp0"
set "PROJ=%ROOT%RevitMCP.Addin\RevitMCP.Addin.csproj"
set "BRIDGE_PROJ=%ROOT%RevitMCP.Bridge\RevitMCP.Bridge.csproj"
set "DOTNET=%ProgramFiles%\dotnet\dotnet.exe"

echo.
echo  =====================================================
echo    RevitMCP Installer
echo  =====================================================
echo.
echo  Press any key to start...
pause >nul
echo.

:: Step 1 - .NET SDK check
echo  [1/4] Checking .NET SDK...
"%DOTNET%" --version
if errorlevel 1 (
    echo  [ERROR] .NET SDK not found at: %DOTNET%
    echo  Please install .NET SDK 8 from https://dotnet.microsoft.com
    pause
    exit /b 1
)
echo  [OK] .NET SDK found.
echo.

:: Step 2 - Detect Revit
echo  [2/4] Detecting Revit...
set "HAS2025=0"
set "HAS2026=0"
set "HAS2027=0"
if exist "C:\Program Files\Autodesk\Revit 2025\Revit.exe" set "HAS2025=1"
if exist "C:\Program Files\Autodesk\Revit 2026\Revit.exe" set "HAS2026=1"
if exist "C:\Program Files\Autodesk\Revit 2027\Revit.exe" set "HAS2027=1"

if "%HAS2025%"=="1" echo  Found: Revit 2025
if "%HAS2026%"=="1" echo  Found: Revit 2026
if "%HAS2027%"=="1" echo  Found: Revit 2027

if "%HAS2025%%HAS2026%%HAS2027%"=="000" (
    echo  [ERROR] No Revit found.
    pause
    exit /b 1
)

echo.
echo  Select version:
if "%HAS2025%"=="1" echo    [1] Revit 2025
if "%HAS2026%"=="1" echo    [2] Revit 2026
if "%HAS2027%"=="1" echo    [3] Revit 2027
echo    [4] All
echo.
set /p SEL= Enter number:
echo.

:: Step 3 - Build addin
echo  [3/4] Building addin...
if "%SEL%"=="1" goto BUILD2025
if "%SEL%"=="2" goto BUILD2026
if "%SEL%"=="3" goto BUILD2027
if "%SEL%"=="4" goto BUILDALL
echo  [ERROR] Invalid input: %SEL%
pause
exit /b 1

:BUILD2025
call :BUILD 2025
if errorlevel 1 ( echo [ERROR] Build failed & pause & exit /b 1 )
goto BRIDGE

:BUILD2026
call :BUILD 2026
if errorlevel 1 ( echo [ERROR] Build failed & pause & exit /b 1 )
goto BRIDGE

:BUILD2027
call :BUILD 2027
if errorlevel 1 ( echo [ERROR] Build failed & pause & exit /b 1 )
goto BRIDGE

:BUILDALL
if "%HAS2025%"=="1" call :BUILD 2025
if errorlevel 1 ( echo [ERROR] Build 2025 failed & pause & exit /b 1 )
if "%HAS2026%"=="1" call :BUILD 2026
if errorlevel 1 ( echo [ERROR] Build 2026 failed & pause & exit /b 1 )
if "%HAS2027%"=="1" call :BUILD 2027
if errorlevel 1 ( echo [ERROR] Build 2027 failed & pause & exit /b 1 )
goto BRIDGE

:: Step 4 - Bridge + config
:BRIDGE
echo.
echo  [4/4] Setting up Claude Desktop...
set "BRIDGE_OUT=%APPDATA%\RevitMCP\bridge"
if not exist "%BRIDGE_OUT%" mkdir "%BRIDGE_OUT%"

echo  Building bridge...
"%DOTNET%" publish "%BRIDGE_PROJ%" -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o "%BRIDGE_OUT%"
if errorlevel 1 (
    echo  [ERROR] Bridge build failed.
    pause
    exit /b 1
)
echo  [OK] Bridge ready.

echo  Updating Claude Desktop config...
set "CFG=%APPDATA%\Claude\claude_desktop_config.json"
set "BRIDGE_EXE=%BRIDGE_OUT%\RevitMCP.Bridge.exe"
if not exist "%APPDATA%\Claude" mkdir "%APPDATA%\Claude"

powershell -NoProfile -ExecutionPolicy Bypass -Command "$cfg=$env:APPDATA+'\Claude\claude_desktop_config.json'; $exe=$env:APPDATA+'\RevitMCP\bridge\RevitMCP.Bridge.exe'; if(Test-Path $cfg){$j=Get-Content $cfg -Raw|ConvertFrom-Json}else{$j=[PSCustomObject]@{mcpServers=[PSCustomObject]@{}}}; if($null -eq $j.mcpServers){$j|Add-Member -MemberType NoteProperty -Name mcpServers -Value([PSCustomObject]@{})-Force}; $e=[PSCustomObject]@{command=$exe;args=@()}; $j.mcpServers|Add-Member -MemberType NoteProperty -Name 'revit-mcp-addin' -Value $e -Force; $utf8nobom=New-Object System.Text.UTF8Encoding $false; [System.IO.File]::WriteAllText($cfg,($j|ConvertTo-Json -Depth 20),$utf8nobom); Write-Host '[OK] Config updated.'"
if errorlevel 1 (
    echo  [ERROR] Config update failed.
    pause
    exit /b 1
)

echo.
echo  =====================================================
echo   DONE!
echo   1. Start Revit - click [Start MCP] in RevitMCP tab
echo   2. Start Claude Desktop
echo   3. Use Revit tools in Claude chat!
echo  =====================================================
echo.
pause
exit /b 0

:: BUILD subroutine
:BUILD
echo  Building Revit %1...
set "OUT=%ROOT%RevitMCP.Addin\bin\%1"
set "DST=%APPDATA%\Autodesk\Revit\Addins\%1\RevitMCP"
set "ADDIN=%APPDATA%\Autodesk\Revit\Addins\%1\RevitMCP.addin"

"%DOTNET%" build "%PROJ%" /p:RevitVersion=%1 /p:Configuration=Release /p:OutputPath="%OUT%" /nologo /v:q
if errorlevel 1 ( exit /b 1 )

if not exist "%DST%" mkdir "%DST%"
xcopy /E /Y /Q "%OUT%\*" "%DST%\" >nul

if not exist "%APPDATA%\Autodesk\Revit\Addins\%1" mkdir "%APPDATA%\Autodesk\Revit\Addins\%1"
(
echo ^<?xml version="1.0" encoding="utf-8"?^>
echo ^<RevitAddIns^>
echo   ^<AddIn Type="Application"^>
echo     ^<Name^>RevitMCP^</Name^>
echo     ^<Assembly^>%DST%\RevitMCP.Addin.dll^</Assembly^>
echo     ^<AddInId^>A1B2C3D4-E5F6-7890-ABCD-EF1234567891^</AddInId^>
echo     ^<FullClassName^>RevitMCP.Addin.App^</FullClassName^>
echo     ^<VendorId^>REVITMCP^</VendorId^>
echo   ^</AddIn^>
echo ^</RevitAddIns^>
) > "%ADDIN%"
echo  [OK] Revit %1 done.
exit /b 0
