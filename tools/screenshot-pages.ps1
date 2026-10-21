# 自动截取各页面截图（开发自测）：启动应用 → UI 自动化点击导航 → 截图
param(
    [string]$DataDir = "",
    [string]$OutDir = ""
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$exe = Join-Path $root "src\SimWorkbench.App\bin\x64\Debug\net8.0-windows10.0.19041.0\win-x64\SimWorkbench.exe"
if ([string]::IsNullOrWhiteSpace($DataDir)) { $DataDir = Join-Path $root "_testdata" }
if ([string]::IsNullOrWhiteSpace($OutDir)) { $OutDir = Join-Path $root "docs\shots" }
New-Item -ItemType Directory -Force -Path $OutDir | Out-Null

Add-Type -AssemblyName System.Windows.Forms, System.Drawing, UIAutomationClient, UIAutomationTypes
Add-Type -TypeDefinition @"
using System;
using System.Runtime.InteropServices;
public class Native {
  [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
  [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h, int c);
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
  [DllImport("user32.dll")] public static extern void mouse_event(uint flags, uint dx, uint dy, int data, IntPtr extra);
  [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
  public struct RECT { public int Left, Top, Right, Bottom; }
}
"@

Get-Process SimWorkbench -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Milliseconds 800
Start-Process -FilePath $exe -ArgumentList @("--data-dir", $DataDir) | Out-Null
Start-Sleep -Seconds 9

$process = Get-Process SimWorkbench -ErrorAction SilentlyContinue | Select-Object -First 1
if (-not $process) { throw "应用未启动" }
$hwnd = $process.MainWindowHandle
[Native]::ShowWindow($hwnd, 3) | Out-Null
Start-Sleep -Milliseconds 500
[Native]::SetForegroundWindow($hwnd) | Out-Null
Start-Sleep -Milliseconds 800

$rootElement = [System.Windows.Automation.AutomationElement]::RootElement
$winCond = New-Object System.Windows.Automation.PropertyCondition(
    [System.Windows.Automation.AutomationElement]::NameProperty, "仿真工程师工作台")
$win = $null
for ($i = 0; $i -lt 20; $i++) {
    $win = $rootElement.FindFirst([System.Windows.Automation.TreeScope]::Children, $winCond)
    if ($win) { break }
    Start-Sleep -Milliseconds 500
}
if (-not $win) { throw "未找到应用窗口" }

function Save-Shot([string]$name) {
    Start-Sleep -Milliseconds 900
    $r = New-Object Native+RECT
    [Native]::GetWindowRect($hwnd, [ref]$r) | Out-Null
    $w = $r.Right - $r.Left; $h = $r.Bottom - $r.Top
    $bmp = New-Object System.Drawing.Bitmap($w, $h)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.CopyFromScreen($r.Left, $r.Top, 0, 0, (New-Object System.Drawing.Size($w, $h)))
    $path = Join-Path $OutDir "$name.png"
    $bmp.Save($path, [System.Drawing.Imaging.ImageFormat]::Png)
    $g.Dispose(); $bmp.Dispose()
    Write-Output "shot: $path"
}

function Invoke-Nav([string]$name) {
    $nameCond = New-Object System.Windows.Automation.PropertyCondition(
        [System.Windows.Automation.AutomationElement]::NameProperty, $name)
    $elements = $win.FindAll([System.Windows.Automation.TreeScope]::Descendants, $nameCond)
    foreach ($el in $elements) {
        try {
            $pattern = $el.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern)
            if ($pattern) {
                $pattern.Select()
                Start-Sleep -Milliseconds 1200
                return $true
            }
        } catch { }
    }
    foreach ($el in $elements) {
        try {
            $pattern = $el.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern)
            if ($pattern) {
                $pattern.Invoke()
                Start-Sleep -Milliseconds 1200
                return $true
            }
        } catch { }
    }
    Write-Output "nav not found: $name"
    return $false
}

function Scroll-Down([int]$notches) {
    $r = New-Object Native+RECT
    [Native]::GetWindowRect($hwnd, [ref]$r) | Out-Null
    $cx = [int](($r.Left + $r.Right) / 2)
    $cy = [int](($r.Top + $r.Bottom) / 2)
    [Native]::SetCursorPos($cx, $cy) | Out-Null
    for ($i = 0; $i -lt $notches; $i++) {
        [Native]::mouse_event(0x0800, 0, 0, -120, [IntPtr]::Zero)
        Start-Sleep -Milliseconds 120
    }
    Start-Sleep -Milliseconds 600
}

$pages = @(
    @("workbench", "工作台 / 总览"),
    @("tasks", "任务"),
    @("calendar", "日历"),
    @("daily-summary", "每日总结"),
    @("topic-log", "主题日志 / 断点"),
    @("checklist", "Checklist"),
    @("settings", "设置")
)

foreach ($page in $pages) {
    if (Invoke-Nav $page[1]) {
        Save-Shot $page[0]
        if ($page[0] -eq "settings") {
            Scroll-Down 6
            Save-Shot "settings-middle"
            Scroll-Down 8
            Save-Shot "settings-bottom"
        }
    }
}

Write-Output "done"
