@echo off
echo Claude Desktop를 완전히 종료한 후 이 파일을 실행하세요.
echo.
pause

powershell -NoProfile -Command ^
  "$cfg = '$env:APPDATA\Claude\claude_desktop_config.json';" ^
  "$raw = Get-Content $cfg -Raw;" ^
  "$j = $raw | ConvertFrom-Json;" ^
  "$exe = '$env:APPDATA\RevitMCP\bridge\RevitMCP.Bridge.exe';" ^
  "$entry = [PSCustomObject]@{ command=$exe; args=@() };" ^
  "$j.mcpServers | Add-Member -MemberType NoteProperty -Name 'revit-mcp-addin' -Value $entry -Force;" ^
  "$j | ConvertTo-Json -Depth 10 | Out-File $cfg -Encoding utf8 -NoNewline;" ^
  "Write-Host 'Done!'"

echo.
echo [완료] Claude Desktop를 다시 시작하세요.
pause
