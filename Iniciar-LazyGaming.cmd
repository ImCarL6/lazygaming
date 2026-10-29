@echo off
cd /d "%~dp0"
if exist "dist\LazyGaming\LazyGaming.Host.exe" (
  "dist\LazyGaming\LazyGaming.Host.exe" --lan
) else (
  dotnet run --project src\LazyGaming.Host -- --lan
)
pause
