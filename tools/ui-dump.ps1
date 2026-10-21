# 诊断：打开任务编辑对话框后导出 UI 自动化树，用于精确定位控件
param([string]$DataDir = "")
$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$exe = Join-Path $root "src\SimWorkbench.App\bin\x64\Debug\net8.0-windows10.0.19041.0\win-x64\SimWorkbench.exe"
if ([string]::IsNullOrWhiteSpace($DataDir)) { $DataDir = Join-Path $root "_testdata" }

Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes
Add-Type -TypeDefinition @"
using System;
using System.Runtime.InteropServices;
public class NatD {
  [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
  [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h, int c);
}
"@

Get-Process SimWorkbench -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Milliseconds 600
Start-Process -FilePath $exe -ArgumentList @("--data-dir", $DataDir) | Out-Null
Start-Sleep -Seconds 9
$p = Get-Process SimWorkbench | Select-Object -First 1
[NatD]::ShowWindow($p.MainWindowHandle, 3) | Out-Null
[NatD]::SetForegroundWindow($p.MainWindowHandle) | Out-Null
Start-Sleep -Milliseconds 800

$AE = [System.Windows.Automation.AutomationElement]
$win = $AE::RootElement.FindFirst([System.Windows.Automation.TreeScope]::Children,
    (New-Object System.Windows.Automation.PropertyCondition($AE::NameProperty, "仿真工程师工作台")))
if (-not $win) { throw "window not found" }

function Dump([string]$file) {
    $lines = New-Object System.Collections.Generic.List[string]
    $walker = [System.Windows.Automation.TreeWalker]::ControlViewWalker
    function Walk($el, [int]$depth) {
        if ($depth -gt 12) { return }
        $name = $el.Current.Name
        $type = $el.Current.ControlType.ProgrammaticName
        $auto = $el.Current.AutomationId
        $off = $el.Current.IsOffscreen
        $patterns = ""
        foreach ($pt in @([System.Windows.Automation.InvokePattern]::Pattern,
                          [System.Windows.Automation.ValuePattern]::Pattern,
                          [System.Windows.Automation.TogglePattern]::Pattern,
                          [System.Windows.Automation.SelectionItemPattern]::Pattern,
                          [System.Windows.Automation.ExpandCollapsePattern]::Pattern)) {
            $obj = $null
            try { if ($el.TryGetCurrentPattern($pt, [ref]$obj)) { $patterns += $pt.ProgrammaticName.Replace("PatternIdentifiers.Pattern","") + " " } } catch { }
        }
        $lines.Add(("  " * $depth) + "$type | name='$name' | auto='$auto' | offscreen=$off | $patterns")
        $child = $walker.GetFirstChild($el)
        while ($child) {
            Walk $child ($depth + 1)
            $child = $walker.GetNextSibling($child)
        }
    }
    Walk $win 0
    $lines | Set-Content -Path $file -Encoding UTF8
    Write-Host "dumped $($lines.Count) lines to $file"
}

# 点击“任务”导航
$navCond = New-Object System.Windows.Automation.PropertyCondition($AE::NameProperty, "任务")
$navs = $win.FindAll([System.Windows.Automation.TreeScope]::Descendants, $navCond)
foreach ($n in $navs) {
    try {
        $sel = $n.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern)
        if ($sel) { $sel.Select(); break }
    } catch { }
}
Start-Sleep -Milliseconds 1500

Dump (Join-Path $root "docs\ui-test\uia-tasks-page.txt")

# 点击“新建任务”
$btnCond = New-Object System.Windows.Automation.PropertyCondition($AE::NameProperty, "新建任务")
$buttons = $win.FindAll([System.Windows.Automation.TreeScope]::Descendants, $btnCond)
foreach ($b in $buttons) {
    if ($b.Current.IsOffscreen) { continue }
    try {
        $inv = $b.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern)
        if ($inv) { $inv.Invoke(); break }
    } catch { }
}
Start-Sleep -Milliseconds 2000
Dump (Join-Path $root "docs\ui-test\uia-task-dialog.txt")

Get-Process SimWorkbench -ErrorAction SilentlyContinue | Stop-Process -Force
