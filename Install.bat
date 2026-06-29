@echo off
chcp 437 > nul
setlocal EnableDelayedExpansion

set "ROOT=%~dp0"
set "PROJ=%ROOT%RevitMCP.Addin\RevitMCP.Addin.csproj"

echo.
echo  =====================================================
echo    RevitMCP - AI Revit Automation Installer
echo  =====================================================
echo.

:: ── .NET SDK check ───────────────────────────────────────────────
where dotnet >nul 2>&1
if errorlevel 1 (
    echo [ERROR] .NET SDK not found.
    echo         https://dotnet.microsoft.com/download
    pause & exit /b
)
for /f "tokens=*" %%v in ('dotnet --version 2^>nul') do set "SDKVER=%%v"
echo [OK] .NET SDK %SDKVER%

:: ── Revit version detect ─────────────────────────────────────────
echo.
echo [INFO] Detecting Revit...
set "HAS2025=0" & set "HAS2026=0" & set "HAS2027=0"
if exist "C:\Program Files\Autodesk\Revit 2025\Revit.exe" ( set "HAS2025=1" & echo [OK] Revit 2025 )
if exist "C:\Program Files\Autodesk\Revit 2026\Revit.exe" ( set "HAS2026=1" & echo [OK] Revit 2026 )
if exist "C:\Program Files\Autodesk\Revit 2027\Revit.exe" ( set "HAS2027=1" & echo [OK] Revit 2027 )

if "%HAS2025%%HAS2026%%HAS2027%"=="000" (
    echo [ERROR] No Revit found.
    pause & exit /b
)

:: ── Version menu ─────────────────────────────────────────────────
echo.
echo  Select version:
set "OPT=0"
if "%HAS2025%"=="1" ( set /a OPT+=1 & set "V!OPT!=2025" & echo    [!OPT!] Revit 2025 )
if "%HAS2026%"=="1" ( set /a OPT+=1 & set "V!OPT!=2026" & echo    [!OPT!] Revit 2026 )
if "%HAS2027%"=="1" ( set /a OPT+=1 & set "V!OPT!=2027" & echo    [!OPT!] Revit 2027 )
set /a OPT+=1
echo    [!OPT!] All
echo.
set /p "SEL= Number: "

if "!SEL!"=="!OPT!" (
    if "%HAS2025%"=="1" call :BUILD 2025
    if "%HAS2026%"=="1" call :BUILD 2026
    if "%HAS2027%"=="1" call :BUILD 2027
) else (
    set "TARGET=!V%SEL%!"
    if "!TARGET!"=="" ( echo [ERROR] Invalid selection. & pause & exit /b )
    call :BUILD !TARGET!
)

:: ── Claude Desktop MCP register ──────────────────────────────────
echo.
echo [INFO] Registering Claude Desktop MCP...
set "CFGDIR=%APPDATA%\Claude"
set "CFG=%CFGDIR%\claude_desktop_config.json"
if not exist "%CFGDIR%" mkdir "%CFGDIR%"
if not exist "%CFG%" echo {} > "%CFG%"

set "JSF=%TEMP%\mcp_reg.js"
> "%JSF%" echo var fs=new ActiveXObject("Scripting.FileSystemObject");
>> "%JSF%" echo var p="%CFG:\=\\%";
>> "%JSF%" echo var cfg={};
>> "%JSF%" echo try{var f=fs.OpenTextFile(p,1,false,-1);var r=f.ReadAll();f.Close();if(r.trim()!="")cfg=JSON.parse(r);}catch(e){}
>> "%JSF%" echo if(!cfg.mcpServers)cfg.mcpServers={};
>> "%JSF%" echo cfg.mcpServers["revit-mcp"]={transport:"http",url:"http://localhost:9876/"};
>> "%JSF%" echo var fw=fs.CreateTextFile(p,true,true);fw.Write(JSON.stringify(cfg,null,2));fw.Close();
>> "%JSF%" echo WScript.Echo("[OK] Claude Desktop MCP registered.");

cscript //nologo "%JSF%"
del "%JSF%" >nul 2>&1

:: ── Done ─────────────────────────────────────────────────────────
echo.
echo  =====================================================
echo   Installation Complete!
echo.
echo   1. Restart Revit
echo   2. Click [Start MCP] in RevitMCP tab
echo   3. Start Revit automation with Claude Desktop!
echo  =====================================================
echo.
pause
exit /b

:: ── BUILD subroutine ─────────────────────────────────────────────
:BUILD
echo.
echo  ----- Revit %1 -----
set "OUT=%ROOT%RevitMCP.Addin\bin\%1"
set "DST=%APPDATA%\Autodesk\Revit\Addins\%1\RevitMCP"
set "ADDIN_SRC=%ROOT%addin\RevitMCP.%1.addin"
set "ADDIN_DST=%APPDATA%\Autodesk\Revit\Addins\%1\RevitMCP.addin"

dotnet build "%PROJ%" /p:RevitVersion=%1 /p:Configuration=Release /p:OutputPath="%OUT%" --nologo
if errorlevel 1 ( echo [ERROR] Build failed for Revit %1 & goto :EOF )
echo [OK] Build succeeded.

if not exist "%DST%" mkdir "%DST%"
xcopy /E /Y /Q "%OUT%\*" "%DST%\" >nul
echo [OK] DLL copied.

if not exist "%APPDATA%\Autodesk\Revit\Addins\%1" mkdir "%APPDATA%\Autodesk\Revit\Addins\%1"
copy /Y "%ADDIN_SRC%" "%ADDIN_DST%" >nul
echo [OK] Addin registered.
goto :EOF
