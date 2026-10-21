# 构建并启动仿真工程师工作台（开发自测用）
# 用法：powershell -File tools\dev-run.ps1 [-NoBuild] [-DataDir <path>]
param(
    [switch]$NoBuild,
    [string]$DataDir = ""
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$appProject = Join-Path $root "src\SimWorkbench.App\SimWorkbench.App.csproj"
$exe = Join-Path $root "src\SimWorkbench.App\bin\x64\Debug\net8.0-windows10.0.19041.0\win-x64\SimWorkbench.exe"

Get-Process SimWorkbench -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Milliseconds 800

if (-not $NoBuild) {
    $env:DOTNET_CLI_TELEMETRY_OPTOUT = "1"
    & "C:\Users\Li\.dotnet\dotnet.exe" build $appProject -p:Platform=x64 -v:m -nologo
    if ($LASTEXITCODE -ne 0) { throw "build failed" }
}

if ([string]::IsNullOrWhiteSpace($DataDir)) {
    $DataDir = Join-Path $root "_testdata"
}

New-Item -ItemType Directory -Force -Path $DataDir | Out-Null
Start-Process -FilePath $exe -ArgumentList @("--data-dir", $DataDir) -WindowStyle Normal
Start-Sleep -Seconds 8

$process = Get-Process SimWorkbench -ErrorAction SilentlyContinue
if ($process) {
    Write-Output "started pid=$($process.Id) title=$($process.MainWindowTitle) datadir=$DataDir"
} else {
    Write-Output "start failed; check log in $DataDir\logs"
}
