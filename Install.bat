@echo off
chcp 437 > nul
setlocal

set "ROOT=%~dp0"
set "PROJ=%ROOT%RevitMCP.Addin\RevitMCP.Addin.csproj"

echo.
echo  =====================================================
echo    RevitMCP - AI Revit Automation / Install
echo  =====================================================
echo.

:: dotnet check
where dotnet >nul 2>&1
if errorlevel 1 (
    echo [ERROR] .NET SDK not found.
    echo         Please install from: https://dotnet.microsoft.com/download
    goto :END
)
echo [OK] .NET SDK found.

:: Detect Revit versions
echo.
echo [INFO] Detecting Revit versions...
set "HAS2025=0" & set "HAS2026=0" & set "HAS2027=0" & set "FOUND=0"
if exist "C:\Program Files\Autodesk\Revit 2025\Revit.exe" ( set "HAS2025=1" & set /a FOUND+=1 & echo [OK] Revit 2025 found )
if exist "C:\Program Files\Autodesk\Revit 2026\Revit.exe" ( set "HAS2026=1" & set /a FOUND+=1 & echo [OK] Revit 2026 found )
if exist "C:\Program Files\Autodesk\Revit 2027\Revit.exe" ( set "HAS2027=1" & set /a FOUND+=1 & echo [OK] Revit 2027 found )

if "%FOUND%"=="0" (
    echo [ERROR] No Revit installation found.
    goto :END
)

:: Version menu
echo.
echo  Select version to install:
set "IDX=0"
if "%HAS2025%"=="1" ( set /a IDX+=1 & set "M!IDX!=2025" & echo    [!IDX!] Revit 2025 )
if "%HAS2026%"=="1" ( set /a IDX+=1 & set "M!IDX!=2026" & echo    [!IDX!] Revit 2026 )
if "%HAS2027%"=="1" ( set /a IDX+=1 & set "M!IDX!=2027" & echo    [!IDX!] Revit 2027 )
set /a IDX+=1
echo    [%IDX%] Install All
echo.

set /p "SEL= Enter number: "

if "%SEL%"=="%IDX%" (
    if "%HAS2025%"=="1" call :BUILD 2025
    if "%HAS2026%"=="1" call :BUILD 2026
    if "%HAS2027%"=="1" call :BUILD 2027
) else (
    setlocal EnableDelayedExpansion
    call :BUILD !M%SEL%!
    endlocal
)

:: Register Claude Desktop MCP via JScript
echo.
echo [INFO] Registering Claude Desktop MCP...
call :WRITE_JS
cscript //nologo "%TEMP%\revitmcp_cfg.js"
del "%TEMP%\revitmcp_cfg.js" >nul 2>&1

echo.
echo  =====================================================
echo   Install Complete!
echo.
echo   How to use:
echo    1. Restart Revit
echo    2. Click [Start MCP] in RevitMCP tab
echo    3. Use Claude Desktop to automate Revit!
echo  =====================================================
echo.
goto :END

:: ── BUILD function ──────────────────────────────────────────────
:BUILD
set "VER=%1"
echo.
echo  ----- Building for Revit %VER% -----
set "OUT=%ROOT%RevitMCP.Addin\bin\%VER%"
set "DST=%APPDATA%\Autodesk\Revit\Addins\%VER%\RevitMCP"
set "ADDIN_SRC=%ROOT%addin\RevitMCP.%VER%.addin"
set "ADDIN_DST=%APPDATA%\Autodesk\Revit\Addins\%VER%\RevitMCP.addin"

dotnet build "%PROJ%" /p:RevitVersion=%VER% /p:Configuration=Release /p:OutputPath="%OUT%" --nologo
if errorlevel 1 (
    echo [ERROR] Build failed for Revit %VER%
    goto :EOF
)
echo [OK] Build succeeded.

if not exist "%DST%" mkdir "%DST%"
xcopy /E /Y /Q "%OUT%\*" "%DST%\" >nul
echo [OK] DLL copied to %DST%

if not exist "%APPDATA%\Autodesk\Revit\Addins\%VER%" mkdir "%APPDATA%\Autodesk\Revit\Addins\%VER%"
copy /Y "%ADDIN_SRC%" "%ADDIN_DST%" >nul
echo [OK] Addin registered: %ADDIN_DST%
goto :EOF

:: ── Write JScript to temp file ──────────────────────────────────
:WRITE_JS
set "CFG=%APPDATA%\Claude\claude_desktop_config.json"
set "CFG_JS=%CFG:\=\\%"
set "CFGDIR=%APPDATA%\Claude"
if not exist "%CFGDIR%" mkdir "%CFGDIR%"

> "%TEMP%\revitmcp_cfg.js" (
    echo var fs = new ActiveXObject^("Scripting.FileSystemObject"^);
    echo var p = "%CFG_JS%";
    echo var cfg = {};
    echo if ^(fs.FileExists^(p^)^) {
    echo   try {
    echo     var f = fs.OpenTextFile^(p,1,false,-1^);
    echo     var r = f.ReadAll^(^); f.Close^(^);
    echo     if ^(r.replace^(/\s/g,""^) != ""^) cfg = eval^("^("+r+"^)"^);
    echo   } catch^(e^){}
    echo }
    echo if ^(!cfg.mcpServers^) cfg.mcpServers={};
    echo cfg.mcpServers["revit-mcp"]={transport:"http",url:"http://localhost:9876/"};
    echo var j=JSON.stringify^(cfg,null,2^);
    echo var fw=fs.CreateTextFile^(p,true,true^);
    echo fw.Write^(j^); fw.Close^(^);
    echo WScript.Echo^("[OK] Claude Desktop MCP registered: %CFG_JS%"^);
)
goto :EOF

:END
pause
