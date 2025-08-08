@echo off
setlocal
pushd %~dp0

REM Ensure git is available
where git >nul 2>nul
if errorlevel 1 (
  echo [ERROR] git is not installed or not in PATH.
  pause
  exit /b 1
)

REM Configure default user if missing (optional prompts if unset)
for /f "usebackq tokens=*" %%i in (`git config user.name`) do set GITUSER=%%i
if "%GITUSER%"=="" (
  echo [INFO] git user.name not set. Setting temporary fallback.
  git config user.name "Echoes Dev"
)
for /f "usebackq tokens=*" %%i in (`git config user.email`) do set GITEMAIL=%%i
if "%GITEMAIL%"=="" (
  echo [INFO] git user.email not set. Setting temporary fallback.
  git config user.email "echoes@example.com"
)

REM Init repo (idempotent)
git init || goto :err
git add . || goto :err
git commit -m "chore: init repo with docs, C# Raylib prototype, tasks" || goto :err
git branch -M main || goto :err

REM Try GitHub CLI path
where gh >nul 2>nul
if errorlevel 1 (
  echo [WARN] GitHub CLI (gh) not found. Creating remote requires manual step.
  echo.
  echo 1) Create empty repo named EchoesOfTheLastStar at: https://github.com/new
  echo 2) Then run:
  echo    git remote add origin https://github.com/<your-user>/EchoesOfTheLastStar.git
  echo    git push -u origin main
  pause
  popd
  exit /b 0
) else (
  echo [INFO] gh found. Creating GitHub repo and pushing...
  gh repo create EchoesOfTheLastStar --source . --public --push --confirm || goto :err
)

echo [OK] Repository initialized and pushed (or remote configured).
pause
popd
exit /b 0

:err
echo [ERROR] Operation failed.
pause
exit /b 1
