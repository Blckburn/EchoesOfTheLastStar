@echo off
setlocal
pushd %~dp0
set PROJ=%CD%\src\EchoesGame
where dotnet >nul 2>nul
if errorlevel 1 (
  echo [ERROR] dotnet SDK not found. Please install .NET 8/9 from https://dotnet.microsoft.com/download
  pause
  exit /b 1
)

dotnet restore "%PROJ%" || goto :err
 dotnet build "%PROJ%" -c Debug || goto :err
 dotnet run --project "%PROJ%" || goto :err
popd
exit /b 0
:err
echo [ERROR] Build or run failed.
pause
exit /b 1
