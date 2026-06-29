@echo off
chcp 65001 > nul
setlocal EnableDelayedExpansion

echo.
echo ============================================================
echo    RevitMCP  -  AI 기반 Revit 자동화  /  올인원 설치
echo ============================================================
echo.

set "ROOT=%~dp0"
set "APPDATA_PATH=%APPDATA%"
set "PROJ=%ROOT%RevitMCP.Addin\RevitMCP.Addin.csproj"

:: ── dotnet 확인 ──────────────────────────────────────────────────
where dotnet >nul 2>&1
if errorlevel 1 (
    echo [오류] .NET SDK 가 설치되어 있지 않습니다.
    echo        https://dotnet.microsoft.com/download 에서 설치 후 다시 실행하세요.
    goto :END
)
for /f "tokens=*" %%v in ('dotnet --version 2^>nul') do set DOTNET_VER=%%v
echo [확인] .NET SDK %DOTNET_VER%

:: ── Revit 버전 감지 ──────────────────────────────────────────────
echo.
echo [탐색] 설치된 Revit 버전 확인 중...
set FOUND_COUNT=0
set "V1=" & set "V2=" & set "V3="

if exist "C:\Program Files\Autodesk\Revit 2025\Revit.exe" (
    set "V1=2025" & set /a FOUND_COUNT+=1
    echo        Revit 2025 발견
)
if exist "C:\Program Files\Autodesk\Revit 2026\Revit.exe" (
    set "V2=2026" & set /a FOUND_COUNT+=1
    echo        Revit 2026 발견
)
if exist "C:\Program Files\Autodesk\Revit 2027\Revit.exe" (
    set "V3=2027" & set /a FOUND_COUNT+=1
    echo        Revit 2027 발견
)

if %FOUND_COUNT%==0 (
    echo [오류] 설치된 Revit 을 찾을 수 없습니다.
    goto :END
)

:: ── 버전 선택 ────────────────────────────────────────────────────
echo.
echo  설치할 버전을 선택하세요:
set IDX=0
if defined V1 ( set /a IDX+=1 & set "OPT!IDX!=2025" & echo    [!IDX!] Revit 2025 )
if defined V2 ( set /a IDX+=1 & set "OPT!IDX!=2026" & echo    [!IDX!] Revit 2026 )
if defined V3 ( set /a IDX+=1 & set "OPT!IDX!=2027" & echo    [!IDX!] Revit 2027 )
set /a IDX+=1
set "OPT!IDX!=ALL"
echo    [!IDX!] 전체 설치
echo.
set /p CHOICE=" 번호 입력: "

if "%CHOICE%"=="!IDX!" (
    set "TARGETS=!V1! !V2! !V3!"
) else (
    set "TARGETS=!OPT%CHOICE%!"
)
if "!TARGETS!"=="" ( echo [오류] 잘못된 선택입니다. & goto :END )

:: ── 버전별 빌드 + 설치 ───────────────────────────────────────────
for %%T in (!TARGETS!) do (
    echo.
    echo ------------------------------------------------------------
    echo  Revit %%T  빌드 및 설치
    echo ------------------------------------------------------------

    set "OUT=%ROOT%RevitMCP.Addin\bin\%%T"
    set "DST=%APPDATA_PATH%\Autodesk\Revit\Addins\%%T\RevitMCP"
    set "ADDIN_SRC=%ROOT%addin\RevitMCP.%%T.addin"
    set "ADDIN_DST=%APPDATA_PATH%\Autodesk\Revit\Addins\%%T\RevitMCP.addin"

    echo [빌드] Revit %%T ...
    dotnet build "%PROJ%" /p:RevitVersion=%%T /p:Configuration=Release /p:OutputPath="!OUT!" --nologo
    if errorlevel 1 (
        echo [오류] %%T 빌드 실패
    ) else (
        echo [완료] 빌드 성공

        if not exist "!DST!" mkdir "!DST!"
        xcopy /E /Y /Q "!OUT!\*" "!DST!\" >nul
        echo [완료] DLL 복사 -^> !DST!

        if not exist "%APPDATA_PATH%\Autodesk\Revit\Addins\%%T" (
            mkdir "%APPDATA_PATH%\Autodesk\Revit\Addins\%%T"
        )
        copy /Y "!ADDIN_SRC!" "!ADDIN_DST!" >nul
        echo [완료] .addin 등록 -^> !ADDIN_DST!
    )
)

:: ── Claude Desktop MCP 등록 (JScript 사용, PowerShell 불필요) ────
echo.
echo ------------------------------------------------------------
echo  Claude Desktop MCP 자동 등록
echo ------------------------------------------------------------

set "CFG_DIR=%APPDATA_PATH%\Claude"
set "CFG=%CFG_DIR%\claude_desktop_config.json"

if not exist "%CFG_DIR%" mkdir "%CFG_DIR%"

:: JScript 임시 파일로 JSON 편집 (Windows 내장 cscript 사용)
set "JS=%TEMP%\revitmcp_cfg.js"
(
echo var fs = new ActiveXObject^("Scripting.FileSystemObject"^);
echo var cfgPath = "%CFG:\=\\%";
echo var cfg = {};
echo if ^(fs.FileExists^(cfgPath^)^) {
echo     try {
echo         var f = fs.OpenTextFile^(cfgPath, 1, false, -1^);
echo         var raw = f.ReadAll^(^); f.Close^(^);
echo         if ^(raw.trim^(^) !== ""^) cfg = eval^("^(" + raw + "^)"^);
echo     } catch^(e^) {}
echo }
echo if ^(!cfg.mcpServers^) cfg.mcpServers = {};
echo cfg.mcpServers["revit-mcp"] = { transport: "http", url: "http://localhost:9876/" };
echo function toJson^(o, d^) {
echo     if ^(d === undefined^) d = 0;
echo     var s = "", i, k, v, pad = "";
echo     for ^(i = 0; i ^< ^(d+1^)*2; i++^) pad += " ";
echo     var pad0 = ""; for ^(i = 0; i ^< d*2; i++^) pad0 += " ";
echo     if ^(typeof o === "object" ^&^& o !== null^) {
echo         var keys = [], isArr = ^(o instanceof Array^);
echo         for ^(k in o^) keys.push^(k^);
echo         s = isArr ? "[" : "{";
echo         for ^(i = 0; i ^< keys.length; i++^) {
echo             k = keys[i]; v = o[k];
echo             s += "\n" + pad + ^(isArr ? "" : '"'+k+'": '^) + toJson^(v, d+1^);
echo             if ^(i ^< keys.length-1^) s += ",";
echo         }
echo         s += "\n" + pad0 + ^(isArr ? "]" : "}");
echo     } else if ^(typeof o === "string"^) {
echo         s = '"' + o.replace^(/\\/g,"\\\\").replace^(/"/g,'\\"'^) + '"';
echo     } else { s = String^(o^); }
echo     return s;
echo }
echo var out = toJson^(cfg^);
echo var fw = fs.CreateTextFile^(cfgPath, true, true^);
echo fw.Write^(out^); fw.Close^(^);
echo WScript.Echo^("[완료] Claude Desktop MCP 등록 -^> " + cfgPath^);
) > "%JS%"

cscript //nologo "%JS%"
del "%JS%" >nul 2>&1

:: ── 완료 메시지 ──────────────────────────────────────────────────
echo.
echo ============================================================
echo  설치 완료!
echo.
echo  사용 방법:
echo    1. Revit 재시작
echo    2. 'RevitMCP' 탭 -^> [MCP 시작] 버튼 클릭
echo    3. Claude Desktop 앱에서 Revit 자동화 시작!
echo ============================================================
echo.

:END
pause
