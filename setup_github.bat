@echo off
setlocal
pushd %~dp0

where git >nul 2>nul
if errorlevel 1 (
  echo [ERROR] git is not installed or not in PATH.
  pause
  exit /b 1
)

git init
git add .
REM Commit only if staged changes exist
git diff --cached --quiet && echo [INFO] Nothing to commit. || git commit -m "chore: init repo with docs, C# Raylib prototype, tasks"

git branch -M main

where gh >nul 2>nul
if errorlevel 1 (
  echo [WARN] gh not found. Will try remote+push.
  git remote add origin https://github.com/Blckburn/EchoesOfTheLastStar.git 2>nul
  git remote set-url origin https://github.com/Blckburn/EchoesOfTheLastStar.git
  echo Pushing to https://github.com/Blckburn/EchoesOfTheLastStar.git ...
  git push -u origin main
  echo If push failed with "repository not found", create the repo at https://github.com/new and run push again.
  pause
  popd
  exit /b 0
) else (
  echo [INFO] gh found. Creating GitHub repo and pushing...
  gh repo create EchoesOfTheLastStar --source . --public --remote origin --push --confirm
  echo [OK] Done.
  pause
  popd
  exit /b 0
)
