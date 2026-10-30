@echo off
setlocal
cd /d "%~dp0"
where dotnet >nul 2>nul
if errorlevel 1 (
  echo .NET SDK was not found.
  echo Install the .NET 10 SDK from https://dotnet.microsoft.com/download/dotnet/10.0
  pause
  exit /b 1
)
echo Starting TTG Client...
dotnet run --project TTGClient.csproj
if errorlevel 1 (
  echo.
  echo TTG Client did not start. Check the error above.
  pause
)
endlocal
