@echo off
REM OmenCore GitHub Upload Script (Windows)

echo =========================================
echo   OmenCore GitHub Upload Script
echo =========================================
echo.

REM Check if git is installed
where git >nul 2>nul
if %ERRORLEVEL% NEQ 0 (
    echo Error: Git is not installed or not in PATH
    echo Please install Git from https://git-scm.com/
    pause
    exit /b 1
)

REM Check if git is initialized
if not exist ".git" (
    echo Initializing Git repository...
    git init
    echo [OK] Git initialized
) else (
    echo [OK] Git repository already initialized
)

REM Check for .gitignore
if not exist ".gitignore" (
    echo Error: .gitignore not found!
    pause
    exit /b 1
)

REM Stage all files
echo.
echo Staging files...
git add .
echo [OK] Files staged

REM Check if there are changes to commit
git diff --staged --quiet
if %ERRORLEVEL% EQU 0 (
    echo [WARNING] No changes to commit
) else (
    REM Commit
    echo.
    echo Committing changes...
    git commit -m "Initial OmenCore commit - v1.0.0" -m "" -m "Features:" -m "- Fan & thermal control with custom curves" -m "- CPU undervolting support" -m "- RGB lighting profiles" -m "- Hardware monitoring" -m "- Corsair/Logitech device integration" -m "- Auto-update via GitHub releases" -m "- HP Omen system detection" -m "- System optimization tools"
    echo [OK] Changes committed
)

REM Check if remote exists
git remote get-url origin >nul 2>nul
if %ERRORLEVEL% EQU 0 (
    echo [OK] Remote 'origin' already configured
) else (
    echo.
    echo Adding remote repository...
    git remote add origin https://github.com/theantipopau/omencore.git
    echo [OK] Remote added
)

REM Set main branch
echo.
echo Setting main branch...
git branch -M main
echo [OK] Branch set to main

REM Push to GitHub
echo.
echo Pushing to GitHub...
echo You may be prompted for your GitHub credentials...
echo.
git push -u origin main

if %ERRORLEVEL% EQU 0 (
    echo.
    echo =========================================
    echo   [OK] Successfully uploaded to GitHub!
    echo =========================================
    echo.
    echo Repository: https://github.com/theantipopau/omencore
    echo.
    echo Next steps:
    echo 1. Create first release: git tag v1.0.0 ^&^& git push origin v1.0.0
    echo 2. GitHub Actions will automatically build and publish
    echo 3. Users can then auto-update from within the app
    echo.
) else (
    echo.
    echo =========================================
    echo   [X] Push failed
    echo =========================================
    echo.
    echo Possible issues:
    echo - GitHub credentials not configured
    echo - Repository doesn't exist (create it at github.com/theantipopau/omencore)
    echo - Network connectivity problems
    echo.
    echo Try manually:
    echo   git push -u origin main
    echo.
)

pause
