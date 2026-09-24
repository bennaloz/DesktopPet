@echo off
rem Avvia Zaira sul desktop. Ricompila solo se serve (qualche secondo).
set GODOT=C:\develop\personal\tools\Godot_v4.7.2-stable_mono_win64\Godot_v4.7.2-stable_mono_win64.exe
cd /d "%~dp0"
dotnet build ZairaDesktopPet.csproj -v q -nologo || (echo Build fallita & pause & exit /b 1)
if not exist ".godot\imported" "%GODOT%" --headless --path . --import
start "" "%GODOT%" --path "%~dp0" %*
