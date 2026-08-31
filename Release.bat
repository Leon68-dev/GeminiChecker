@echo off
chcp 65001 > nul
echo ============================================
echo        Building Project (Release)
echo ============================================

:: Check if dotnet CLI is available
where dotnet >nul 2>nul
if %errorlevel% neq 0 (
    echo [ERROR] .NET SDK not found! Please make sure .NET SDK is installed.
    pause
    exit /b %errorlevel%
)

:: Run Release compilation
dotnet build -c Release

if %errorlevel% neq 0 (
    echo.
    echo [ERROR] Build failed!
    pause
    exit /b %errorlevel%
)

echo.
echo ============================================
echo [SUCCESS] Build completed successfully!
echo Output directory: bin\Release\net10.0\
echo ============================================
echo.

pause