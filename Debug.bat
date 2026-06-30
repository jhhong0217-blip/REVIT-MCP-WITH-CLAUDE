@echo off
echo Step 1: started
pause

echo Step 2: checking dotnet
"%ProgramFiles%\dotnet\dotnet.exe" --version
echo dotnet exit: %errorlevel%
pause

echo Step 3: checking tasklist
tasklist /FI "IMAGENAME eq claude.exe"
pause

echo Step 4: done
pause
