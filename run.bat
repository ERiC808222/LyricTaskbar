@echo off
cd /d "%~dp0"
dotnet run --project "%~dp0LyricBar.csproj" -c Release
exit /b %errorlevel%
