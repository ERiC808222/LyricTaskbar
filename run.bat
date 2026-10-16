@echo off
cd /d "%~dp0"
dotnet run --project "%~dp0LyricTaskbar.csproj" -c Release
exit /b %errorlevel%
