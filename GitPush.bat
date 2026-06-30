@echo off
chcp 437 >nul
cd /d "%~dp0"
echo.
echo  Committing and pushing to GitHub...
echo.
git add -A
git commit -m "Fix ElementId int->long across all tool files for Revit 2024+ compatibility"
git push origin main
echo.
echo  Done.
pause
