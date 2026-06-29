<#
.SYNOPSIS
    RevitMCP 올인원 설치 스크립트
.DESCRIPTION
    버전 선택 → 빌드 → 설치 → Claude MCP 설정까지 한 번에 처리합니다.
.EXAMPLE
    .\Setup.ps1
    .\Setup.ps1 -Version 2026 -Silent
#>

param(
    [ValidateSet("2025","2026","2027","")]
    [string]$Version = "",
    [switch]$Silent
)

$ErrorActionPreference = "Stop"
$Root   = $PSScriptRoot
$AppData = $env:APPDATA

# ── 색상 출력 헬퍼 ────────────────────────────────────────────────
function Write-Step  { param($msg) Write-Host "`n▶ $msg" -ForegroundColor Cyan }
function Write-Ok    { param($msg) Write-Host "  ✓ $msg" -ForegroundColor Green }
function Write-Warn  { param($msg) Write-Host "  ! $msg" -ForegroundColor Yellow }
function Write-Err   { param($msg) Write-Host "  ✗ $msg" -ForegroundColor Red }

# ── 배너 ─────────────────────────────────────────────────────────
Clear-Host
Write-Host @"
╔══════════════════════════════════════════════════╗
║          RevitMCP  —  AI 기반 Revit 자동화       ║
║              올인원 설치 프로그램                 ║
╚══════════════════════════════════════════════════╝
"@ -ForegroundColor Cyan

# ── 설치된 Revit 버전 자동 감지 ──────────────────────────────────
Write-Step "설치된 Revit 버전 확인 중..."
$detected = @()
foreach ($v in @("2025","2026","2027")) {
    if (Test-Path "C:\Program Files\Autodesk\Revit $v\Revit.exe") {
        $detected += $v
        Write-Ok "Revit $v 발견"
    }
}
if ($detected.Count -eq 0) {
    Write-Err "설치된 Revit을 찾을 수 없습니다. 스크립트를 종료합니다."
    exit 1
}

# ── 버전 선택 ─────────────────────────────────────────────────────
if ($Version -eq "") {
    Write-Host ""
    Write-Host "  설치할 Revit 버전을 선택하세요:" -ForegroundColor White
    $options = $detected + @("전체 설치")
    for ($i = 0; $i -lt $options.Count; $i++) {
        Write-Host "    [$($i+1)] $($options[$i])" -ForegroundColor White
    }
    Write-Host ""
    $choice = Read-Host "  번호 입력"
    $idx = [int]$choice - 1

    if ($idx -lt 0 -or $idx -ge $options.Count) {
        Write-Err "잘못된 선택입니다."; exit 1
    }

    if ($options[$idx] -eq "전체 설치") {
        $targets = $detected
    } else {
        $targets = @($options[$idx])
    }
} else {
    if ($detected -notcontains $Version) {
        Write-Err "Revit $Version 이 설치되어 있지 않습니다."; exit 1
    }
    $targets = @($Version)
}

# ── dotnet 확인 ───────────────────────────────────────────────────
Write-Step ".NET SDK 확인 중..."
try {
    $sdkVer = & dotnet --version 2>&1
    Write-Ok ".NET SDK $sdkVer"
} catch {
    Write-Err ".NET SDK가 필요합니다. https://dotnet.microsoft.com/download 에서 설치하세요."
    exit 1
}

# ── 빌드 + 설치 함수 ──────────────────────────────────────────────
function Install-ForVersion($ver) {
    Write-Host ""
    Write-Host "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━" -ForegroundColor DarkGray
    Write-Host "  Revit $ver 빌드 및 설치" -ForegroundColor White
    Write-Host "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━" -ForegroundColor DarkGray

    $proj   = "$Root\RevitMCP.Addin\RevitMCP.Addin.csproj"
    $outDir = "$Root\RevitMCP.Addin\bin\$ver"
    $dstDir = "$AppData\Autodesk\Revit\Addins\$ver\RevitMCP"
    $addinSrc = "$Root\addin\RevitMCP.$ver.addin"
    $addinDst = "$AppData\Autodesk\Revit\Addins\$ver\RevitMCP.addin"

    # ── 빌드 ──
    Write-Step "빌드 중 (Revit $ver)..."
    $buildOutput = & dotnet build $proj `
        /p:RevitVersion=$ver `
        /p:Configuration=Release `
        /p:OutputPath="$outDir" `
        --nologo 2>&1

    if ($LASTEXITCODE -ne 0) {
        Write-Err "빌드 실패:"
        $buildOutput | Where-Object { $_ -match "error" } | ForEach-Object { Write-Host "    $_" -ForegroundColor Red }
        return $false
    }
    Write-Ok "빌드 완료"

    # ── DLL 설치 ──
    Write-Step "파일 복사 중..."
    New-Item -ItemType Directory -Force $dstDir | Out-Null
    Copy-Item "$outDir\*" $dstDir -Recurse -Force
    Write-Ok "DLL 복사 → $dstDir"

    # ── .addin 설치 ──
    New-Item -ItemType Directory -Force (Split-Path $addinDst) | Out-Null
    Copy-Item $addinSrc $addinDst -Force
    Write-Ok ".addin 등록 → $addinDst"

    return $true
}

# ── 설치 실행 ─────────────────────────────────────────────────────
$results = @{}
foreach ($t in $targets) {
    $results[$t] = Install-ForVersion $t
}

# ── Claude MCP 설정 자동 등록 ────────────────────────────────────
Write-Host ""
Write-Host "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━" -ForegroundColor DarkGray
Write-Host "  Claude MCP 자동 등록" -ForegroundColor White
Write-Host "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━" -ForegroundColor DarkGray

$mcpJson = @"
{
  "mcpServers": {
    "revit-mcp": {
      "transport": "http",
      "url": "http://localhost:9876/"
    }
  }
}
"@

function Set-McpEntry($path) {
    New-Item -ItemType Directory -Force (Split-Path $path) | Out-Null

    if (-not (Test-Path $path)) {
        # 파일 없으면 새로 생성
        Set-Content $path $mcpJson -Encoding UTF8
        return
    }

    # 기존 파일에 revit-mcp 항목만 추가/덮어쓰기
    $raw = Get-Content $path -Raw -Encoding UTF8
    if ([string]::IsNullOrWhiteSpace($raw)) { $raw = "{}" }
    $cfg = $raw | ConvertFrom-Json

    if (-not $cfg.PSObject.Properties["mcpServers"]) {
        $cfg | Add-Member -MemberType NoteProperty -Name "mcpServers" -Value ([PSCustomObject]@{})
    }
    $cfg.mcpServers | Add-Member -MemberType NoteProperty -Name "revit-mcp" -Value ([PSCustomObject]@{
        transport = "http"
        url       = "http://localhost:9876/"
    }) -Force

    $cfg | ConvertTo-Json -Depth 10 | Set-Content $path -Encoding UTF8
}

# 1) Claude Code settings.json — revit-mcp 자동 승인
$claudeSettings = "$env:USERPROFILE\.claude\settings.json"
try {
    New-Item -ItemType Directory -Force (Split-Path $claudeSettings) | Out-Null
    if (-not (Test-Path $claudeSettings)) { Set-Content $claudeSettings "{}" -Encoding UTF8 }
    $raw = Get-Content $claudeSettings -Raw -Encoding UTF8
    if ([string]::IsNullOrWhiteSpace($raw)) { $raw = "{}" }
    $cfg = $raw | ConvertFrom-Json

    if (-not $cfg.PSObject.Properties["enabledMcpjsonServers"]) {
        $cfg | Add-Member -MemberType NoteProperty -Name "enabledMcpjsonServers" -Value @("revit-mcp")
    } elseif ($cfg.enabledMcpjsonServers -notcontains "revit-mcp") {
        $cfg.enabledMcpjsonServers += "revit-mcp"
    }
    $cfg | ConvertTo-Json -Depth 10 | Set-Content $claudeSettings -Encoding UTF8
    Write-Ok "Claude Code 자동 승인 설정 → $claudeSettings"
} catch {
    Write-Warn "Claude Code settings.json 설정 실패: $_"
}

# 2) claude mcp add 명령으로 Claude Code에 영구 등록
try {
    $claudeExe = (Get-Command claude -ErrorAction Stop).Source
    & $claudeExe mcp add revit-mcp --transport http http://localhost:9876/ 2>&1 | Out-Null
    Write-Ok "Claude Code MCP 영구 등록 완료"
} catch {
    Write-Warn "claude CLI를 찾을 수 없습니다. 나중에 아래 명령을 한 번만 실행하세요:"
    Write-Host "    claude mcp add revit-mcp --transport http http://localhost:9876/" -ForegroundColor Yellow
}

# 2) Claude Desktop 설정 (%APPDATA%\Claude\claude_desktop_config.json)
$claudeDesktopConfig = "$AppData\Claude\claude_desktop_config.json"
try {
    Set-McpEntry $claudeDesktopConfig
    Write-Ok "Claude Desktop MCP 등록 → $claudeDesktopConfig"
} catch {
    Write-Warn "Claude Desktop 설정 실패 (설치 안 된 경우 무시): $_"
}

# ── 최종 결과 ─────────────────────────────────────────────────────
Write-Host ""
Write-Host "╔══════════════════════════════════════════════╗" -ForegroundColor Cyan
Write-Host "║              설치 결과 요약                  ║" -ForegroundColor Cyan
Write-Host "╚══════════════════════════════════════════════╝" -ForegroundColor Cyan
$allOk = $true
foreach ($ver in $results.Keys) {
    if ($results[$ver]) {
        Write-Ok "Revit $ver  — 설치 성공"
    } else {
        Write-Err "Revit $ver  — 설치 실패"
        $allOk = $false
    }
}

Write-Host ""
if ($allOk) {
    Write-Host "  설치 완료!" -ForegroundColor Green
    Write-Host "  다음 단계:" -ForegroundColor White
    Write-Host "    1. Revit을 재시작합니다" -ForegroundColor White
    Write-Host "    2. 'RevitMCP' 탭 → [MCP 시작] 버튼 클릭" -ForegroundColor White
    Write-Host "    3. Claude Code에서 Revit 모델을 자동화하세요!" -ForegroundColor White
} else {
    Write-Err "일부 버전 설치에 실패했습니다. 위 오류 메시지를 확인하세요."
    exit 1
}

Write-Host ""
if (-not $Silent) {
    Read-Host "  엔터를 누르면 종료합니다"
}
