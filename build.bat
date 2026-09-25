@echo off
setlocal
cd /d "%~dp0"

echo [1/3] Build...
dotnet build 7daysModLauncher.csproj -c Release --nologo -v minimal
if errorlevel 1 goto :fail

echo [2/3] Tests...
dotnet test 7daysModLauncher.Tests\7daysModLauncher.Tests.csproj -c Release --nologo -v minimal
if errorlevel 1 goto :fail

echo [3/3] Self-contained publish (true single file)...
dotnet publish 7daysModLauncher.csproj -c Release -r win-x64 -p:PublishSingleFile=true -p:SelfContained=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -o release_single --nologo -v minimal
if errorlevel 1 goto :fail

echo.
echo OK - release_single\7daysModLauncher.exe
goto :end

:fail
echo.
echo FAILED with error %errorlevel%
exit /b %errorlevel%

:end
