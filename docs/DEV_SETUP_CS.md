# DEV SETUP (C# + Raylib-cs)

## Требования
- .NET SDK 9 (или 8)
- IDE: Visual Studio / VS Code

## Структура
- `src/EchoesGame` — проект
- `src/EchoesGame/assets/textures` — текстуры
- `run_game.bat` — сборка и запуск

## Запуск
- Двойной клик `run_game.bat` ИЛИ команды:
  - `dotnet restore src/EchoesGame`
  - `dotnet build src/EchoesGame -c Debug`
  - `dotnet run --project src/EchoesGame`

## Управление (по умолчанию)
- WASD — движение
- Мышь — направление стрельбы
- Shift — рывок (есть iFrames и КД)
