@echo off
setlocal
cd /d "%~dp0"
where dotnet >nul 2>nul
if errorlevel 1 (
  echo .NET SDK was not found. Install .NET 10 SDK first.
  pause
  exit /b 1
)
echo Publishing self-contained Windows x64 build...
dotnet publish TTGClient.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -o publish\win-x64
if errorlevel 1 (
  echo Publish failed. Check the build output above.
  pause
  exit /b 1
)
echo.
echo Publish complete: %CD%\publish\win-x64\TTGClient.exe
pause
endlocal
