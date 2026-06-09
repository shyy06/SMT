@echo off
echo ========================================
echo   SMT - 股票盯盘工具 构建脚本
echo ========================================
echo.

:: Check if dotnet SDK is available
where dotnet >nul 2>&1
if %ERRORLEVEL% NEQ 0 (
    echo [错误] 未找到 .NET SDK，请先安装：
    echo   https://dotnet.microsoft.com/download/dotnet/8.0
    echo.
    pause
    exit /b 1
)

echo [1/2] 正在还原依赖...
dotnet restore SMT.csproj
if %ERRORLEVEL% NEQ 0 (
    echo [错误] 依赖还原失败
    pause
    exit /b 1
)

echo [2/2] 正在编译...
dotnet build SMT.csproj -c Release --no-restore
if %ERRORLEVEL% NEQ 0 (
    echo [错误] 编译失败
    pause
    exit /b 1
)

echo.
echo ========================================
echo   构建成功！
echo   输出：.\bin\Release\net8.0-windows\SMT.exe
echo ========================================
echo.
echo 运行：dotnet run --project SMT.csproj
echo.

:: Optionally run
set /p RUN="是否立即运行？(y/n): "
if /i "%RUN%"=="y" (
    start "" ".\bin\Release\net8.0-windows\SMT.exe"
)
pause
