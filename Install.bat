@echo off
setlocal
chcp 437 > nul

set "ROOT=%~dp0"
set "PROJ=%ROOT%RevitMCP.Addin\RevitMCP.Addin.csproj"

echo.
echo  =====================================================
echo    RevitMCP Installer
echo  =====================================================
echo.

:: .NET SDK check
where dotnet >nul 2>&1
if errorlevel 1 (
    echo [ERROR] .NET SDK not found.
    echo         https://dotnet.microsoft.com/download
    goto :END
)
for /f "tokens=*" %%v in ('dotnet --version 2^>nul') do set "SDKVER=%%v"
echo [OK] .NET SDK %SDKVER%

:: Detect Revit
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

:: Menu
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

:MCP
echo.
echo [INFO] Registering Claude Desktop MCP...

set "CFGDIR=%APPDATA%\Claude"
set "CFG=%CFGDIR%\claude_desktop_config.json"
if not exist "%CFGDIR%" mkdir "%CFGDIR%"
if not exist "%CFG%" echo {} > "%CFG%"

set "JSF=%TEMP%\mcp_reg.js"

echo var fs=new ActiveXObject("Scripting.FileSystemObject"); > "%JSF%"
echo var p="%CFG:\=\\%"; >> "%JSF%"
echo var cfg={}; >> "%JSF%"
echo try{var f=fs.OpenTextFile(p,1,false,-1);var r=f.ReadAll();f.Close();if(r.trim()!="")cfg=JSON.parse(r);}catch(e){} >> "%JSF%"
echo if(!cfg.mcpServers)cfg.mcpServers={}; >> "%JSF%"
echo cfg.mcpServers["revit-mcp"]={transport:"http",url:"http://localhost:9876/"}; >> "%JSF%"
echo var fw=fs.CreateTextFile(p,true,true);fw.Write(JSON.stringify(cfg,null,2));fw.Close(); >> "%JSF%"
echo WScript.Echo("[OK] Claude Desktop MCP registered."); >> "%JSF%"

cscript //nologo "%JSF%"
del "%JSF%" >nul 2>&1

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

dotnet build "%PROJ%" /p:RevitVersion=%1 /p:Configuration=Release /p:OutputPath="%OUT%"
if errorlevel 1 (
    echo.
    echo [ERROR] Build failed for Revit %1
    goto :EOF
)
echo [OK] Build succeeded.

if not exist "%DST%" mkdir "%DST%"
xcopy /E /Y /Q "%OUT%\*" "%DST%\" >nul
echo [OK] DLL copied to %DST%

if not exist "%APPDATA%\Autodesk\Revit\Addins\%1" mkdir "%APPDATA%\Autodesk\Revit\Addins\%1"
copy /Y "%ADDIN_SRC%" "%ADDIN_DST%" >nul
echo [OK] Addin registered: %ADDIN_DST%
goto :EOF

:END
echo.
pause
