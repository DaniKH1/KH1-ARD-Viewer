@echo off
setlocal

set "ROOT=%~dp0"
set "PROJECT=%ROOT%Data\KH1ARDViewer.csproj"
set "OUTPUT=%ROOT%_build"
set "APP=%ROOT%KH1 ARD Viewer.exe"

if not exist "%PROJECT%" (
    echo ERROR: Project file not found: "%PROJECT%"
    exit /b 1
)

where dotnet >nul 2>nul
if errorlevel 1 (
    echo ERROR: .NET 10 SDK is required to build the viewer.
    exit /b 1
)

echo Publishing KH1 ARD Viewer for Windows x64...
dotnet publish "%PROJECT%" -c Release -r win-x64 --self-contained false ^
    -p:PublishSingleFile=true -p:DebugType=None -p:DebugSymbols=false ^
    -o "%OUTPUT%"
if errorlevel 1 (
    echo ERROR: Publish failed. See the output above for details.
    exit /b 1
)

if not exist "%OUTPUT%\KH1 ARD Viewer.exe" (
    echo ERROR: Published executable was not created.
    exit /b 1
)

copy /y "%OUTPUT%\KH1 ARD Viewer.exe" "%APP%" >nul
if errorlevel 1 (
    echo ERROR: Could not copy the executable to the repository root.
    exit /b 1
)

if exist "%OUTPUT%" rmdir /s /q "%OUTPUT%"
echo Build complete: "%APP%"
exit /b 0
