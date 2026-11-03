@echo off
setlocal
cd /d "%~dp0"

if not exist "%~dp0Data\KH1ARDViewer.csproj" (
    echo Source code was not found in the Data folder.
    pause
    exit /b 1
)

where dotnet >nul 2>nul
if errorlevel 1 (
    echo dotnet was not found. Install the .NET 10 SDK to build from source.
    pause
    exit /b 1
)

set "BUILD_DIR=%~dp0_build"
if exist "%BUILD_DIR%" rmdir /s /q "%BUILD_DIR%"

dotnet publish "%~dp0Data\KH1ARDViewer.csproj" -c Release -r win-x64 --no-self-contained -p:SelfContained=false -p:PublishSingleFile=true -p:DebugType=None -p:DebugSymbols=false -o "%BUILD_DIR%"
if errorlevel 1 (
    echo Could not build the viewer. Make sure the .NET 10 SDK is installed.
    pause
    exit /b 1
)

if not exist "%BUILD_DIR%\KH1 ARD Viewer.exe" (
    echo KH1 ARD Viewer.exe was not created.
    pause
    exit /b 1
)

copy /y "%BUILD_DIR%\KH1 ARD Viewer.exe" "%~dp0KH1 ARD Viewer.exe" >nul
if errorlevel 1 (
    echo Could not copy the single-file executable into the distribution folder.
    pause
    exit /b 1
)

if exist "%~dp0KH1 ARD Viewer.dll" del /q "%~dp0KH1 ARD Viewer.dll"
if exist "%~dp0KH1 ARD Viewer.deps.json" del /q "%~dp0KH1 ARD Viewer.deps.json"
if exist "%~dp0KH1 ARD Viewer.runtimeconfig.json" del /q "%~dp0KH1 ARD Viewer.runtimeconfig.json"
if exist "%BUILD_DIR%" rmdir /s /q "%BUILD_DIR%"
if exist "%~dp0Data\bin" rmdir /s /q "%~dp0Data\bin"
if exist "%~dp0Data\obj" rmdir /s /q "%~dp0Data\obj"

echo Build complete: "%~dp0KH1 ARD Viewer.exe"

endlocal
