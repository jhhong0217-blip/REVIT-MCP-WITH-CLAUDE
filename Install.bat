@echo off
chcp 65001 > nul
echo RevitMCP 설치를 시작합니다...
powershell.exe -ExecutionPolicy Bypass -File "%~dp0Setup.ps1"
pause
