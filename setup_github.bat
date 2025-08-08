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
REM Commit only if there is something staged
git diff --cached --quiet && echo [INFO] Nothing to commit. Skipping commit. || git commit -m "chore: init repo with docs, C# Raylib prototype, tasks" || goto :err
git branch -M main || goto :err

REM Try GitHub CLI path
where gh >nul 2>nul
if errorlevel 1 (
  echo [WARN] GitHub CLI (gh) not found. Creating remote requires manual step if repo does not exist.
  echo.
  echo Trying to set remote to https://github.com/Blckburn/EchoesOfTheLastStar.git and push...
  git remote get-url origin >nul 2>nul && git remote set-url origin https://github.com/Blckburn/EchoesOfTheLastStar.git || git remote add origin https://github.com/Blckburn/EchoesOfTheLastStar.git
  git push -u origin main || (
    echo.
    echo [ACTION REQUIRED]
    echo 1) Create empty repo named EchoesOfTheLastStar here: https://github.com/new
    echo 2) Then run:
    echo    git push -u origin main
    goto :endok
  )
  goto :endok
) else (
  echo [INFO] gh found. Creating GitHub repo and pushing...
  gh repo create EchoesOfTheLastStar --source . --public --remote origin --push --confirm || goto :ghfallback
)

goto :endok

:ghfallback
echo [WARN] gh action failed. Trying to set remote and push manually...
if not exist .git goto :err
git remote get-url origin >nul 2>nul && git remote set-url origin https://github.com/Blckburn/EchoesOfTheLastStar.git || git remote add origin https://github.com/Blckburn/EchoesOfTheLastStar.git || goto :err
git push -u origin main || goto :err

goto :endok

:endok
echo [OK] Repository initialized. Remote is https://github.com/Blckburn/EchoesOfTheLastStar.git (if created). If push failed, create the repo and push again.
pause
popd
exit /b 0

:err
echo [ERROR] Operation failed.
pause
exit /b 1
