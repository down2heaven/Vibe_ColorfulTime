# UI 端到端自测：通过 UI 自动化执行关键流程，并用数据库校验结果
param(
    [string]$DataDir = "",
    [string]$OutDir = ""
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$exe = Join-Path $root "src\SimWorkbench.App\bin\x64\Debug\net8.0-windows10.0.19041.0\win-x64\SimWorkbench.exe"
$seed = Join-Path $root "tools\SeedData\bin\Debug\net8.0\SimWorkbench.SeedData.exe"
if ([string]::IsNullOrWhiteSpace($DataDir)) { $DataDir = Join-Path $root "_testdata" }
if ([string]::IsNullOrWhiteSpace($OutDir)) { $OutDir = Join-Path $root "docs\ui-test" }
New-Item -ItemType Directory -Force -Path $OutDir | Out-Null

Add-Type -AssemblyName System.Windows.Forms, System.Drawing, UIAutomationClient, UIAutomationTypes
Add-Type -TypeDefinition @"
using System;
using System.Runtime.InteropServices;
public class Nat2 {
  [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
  [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h, int c);
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
  public struct RECT { public int Left, Top, Right, Bottom; }
}
"@

$script:pass = 0
$script:fail = 0
function Check([bool]$ok, [string]$name, [string]$detail = "") {
    if ($ok) { $script:pass++; Write-Host "[PASS] $name" }
    else { $script:fail++; Write-Host "[FAIL] $name $detail" }
}

Get-Process SimWorkbench -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Milliseconds 800
& $seed $DataDir | Out-Null

function Get-Stats {
    $map = @{}
    foreach ($line in (& $seed --stats $DataDir)) {
        if ($line -match "^(.*?)=(.*)$") { $map[$Matches[1]] = $Matches[2] }
    }
    return $map
}

$before = Get-Stats
Write-Host ("baseline: tasks={0} topics={1} checklists={2}" -f $before["任务"], $before["主题日志"], $before["清单"])

Start-Process -FilePath $exe -ArgumentList @("--data-dir", $DataDir) | Out-Null
Start-Sleep -Seconds 9
$process = Get-Process SimWorkbench -ErrorAction SilentlyContinue | Select-Object -First 1
if (-not $process) { throw "app not started" }
$hwnd = $process.MainWindowHandle
[Nat2]::ShowWindow($hwnd, 3) | Out-Null
Start-Sleep -Milliseconds 400
[Nat2]::SetForegroundWindow($hwnd) | Out-Null
Start-Sleep -Milliseconds 600

$AE = [System.Windows.Automation.AutomationElement]
$TS = [System.Windows.Automation.TreeScope]
$rootEl = $AE::RootElement
$winCond = New-Object System.Windows.Automation.PropertyCondition($AE::NameProperty, "仿真工程师工作台")
$win = $null
for ($i = 0; $i -lt 20; $i++) {
    $win = $rootEl.FindFirst($TS::Children, $winCond)
    if ($win) { break }
    Start-Sleep -Milliseconds 500
}
if (-not $win) { throw "window not found" }

function Save-Shot([string]$name) {
    Start-Sleep -Milliseconds 800
    $r = New-Object Nat2+RECT
    [Nat2]::GetWindowRect($hwnd, [ref]$r) | Out-Null
    $w = $r.Right - $r.Left; $h = $r.Bottom - $r.Top
    $bmp = New-Object System.Drawing.Bitmap($w, $h)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.CopyFromScreen($r.Left, $r.Top, 0, 0, (New-Object System.Drawing.Size($w, $h)))
    $bmp.Save((Join-Path $OutDir "$name.png"), [System.Drawing.Imaging.ImageFormat]::Png)
    $g.Dispose(); $bmp.Dispose()
}

function Find-ById([string]$id) {
    $cond = New-Object System.Windows.Automation.PropertyCondition($AE::AutomationIdProperty, $id)
    $found = $win.FindFirst($TS::Descendants, $cond)
    if (-not $found) { $found = $rootEl.FindFirst($TS::Descendants, $cond) }
    return $found
}

function Find-ByName([string]$name) {
    $cond = New-Object System.Windows.Automation.PropertyCondition($AE::NameProperty, $name)
    return $win.FindAll($TS::Descendants, $cond)
}

function Invoke-ByCondition($element) {
    if (-not $element) { return $false }
    try {
        $inv = $element.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern)
        if ($inv) { $inv.Invoke(); Start-Sleep -Milliseconds 1100; return $true }
    } catch { }
    return $false
}

function Click-ById([string]$id) {
    $el = Find-ById $id
    if (-not $el) { Write-Host "  element not found: $id"; return $false }
    if (Invoke-ByCondition $el) { return $true }
    try {
        $tog = $el.GetCurrentPattern([System.Windows.Automation.TogglePattern]::Pattern)
        if ($tog) { $tog.Toggle(); Start-Sleep -Milliseconds 1200; return $true }
    } catch { }
    Write-Host "  element not invokable: $id"
    return $false
}

function Click-ButtonByName([string]$name) {
    foreach ($b in (Find-ByName $name)) {
        if ($b.Current.ControlType -eq [System.Windows.Automation.ControlType]::Button -and -not $b.Current.IsOffscreen) {
            if (Invoke-ByCondition $b) { return $true }
        }
    }
    Write-Host "  button not found: $name"
    return $false
}

function Set-TextById([string]$id, [string]$value) {
    $el = Find-ById $id
    if (-not $el) { Write-Host "  edit not found: $id"; return $false }
    try {
        $vp = $el.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern)
        $vp.SetValue($value)
        Start-Sleep -Milliseconds 300
        return $true
    } catch {
        Write-Host "  cannot set value: $id"
        return $false
    }
}

function Nav([string]$key) {
    $el = Find-ById ("Nav_" + $key)
    if (-not $el) { Write-Host "  nav not found: $key"; return $false }
    try {
        $sel = $el.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern)
        $sel.Select()
        Start-Sleep -Milliseconds 1300
        return $true
    } catch {
        Write-Host "  cannot select nav: $key"
        return $false
    }
}

function Select-ComboItem([string]$comboId, [string]$itemName) {
    $combo = Find-ById $comboId
    if (-not $combo) { Write-Host "  combo not found: $comboId"; return $false }
    try {
        $expand = $combo.GetCurrentPattern([System.Windows.Automation.ExpandCollapsePattern]::Pattern)
        $expand.Expand()
        Start-Sleep -Milliseconds 900
    } catch { }
    foreach ($item in (Find-ByName $itemName)) {
        try {
            $sel = $item.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern)
            if ($sel) { $sel.Select(); Start-Sleep -Milliseconds 1000; return $true }
        } catch { }
    }
    return $false
}

# ---------- 1. 新建任务 ----------
Check (Nav "tasks") "导航到任务页"
Check (Click-ButtonByName "新建任务") "打开任务编辑器"
Save-Shot "1-task-editor"
Check ((Find-ById "TitleBox") -ne $null) "任务编辑器对话框已打开"
Check (Set-TextById "TitleBox" "UI 自动化测试任务") "填写任务名称"
Check (Click-ById "PrimaryButton") "保存任务"
Save-Shot "2-tasks-after-create"

# ---------- 2. 周期任务 ----------
Check (Click-ButtonByName "周期任务") "打开周期任务列表"
Save-Shot "3-recurrence-list"
Check (Click-ById "PrimaryButton") "打开新建周期规则对话框"
Check (Set-TextById "TitleBox" "UI 自动化周期规则") "填写周期规则名称"
Check (Click-ById "PrimaryButton") "保存周期规则"
Start-Sleep -Milliseconds 900
Click-ById "CloseButton" | Out-Null
Save-Shot "4-after-recurrence"

# ---------- 3. 主题日志 ----------
Check (Nav "topic-log") "导航到主题日志"
Check (Click-ButtonByName "新建主题") "新建主题日志"
Save-Shot "5-topic-new"
Check ((Find-ById "TitleBox") -ne $null) "主题日志编辑器已打开"

# ---------- 4. Checklist ----------
Check (Nav "checklist") "导航到 Checklist"
Check (Click-ButtonByName "新建清单") "打开新建清单对话框"
Check (Set-TextById "InputBox" "UI 自动化检查清单") "填写清单名称"
Check (Click-ById "PrimaryButton") "保存新清单"
Save-Shot "6-checklist-after-create"

# ---------- 5. 隐藏 / 恢复日历导航入口 ----------
Check (Nav "settings") "导航到设置页"
Save-Shot "7-settings-top"
Check (Click-ById "NavToggle_calendar") "隐藏“日历”导航入口"
Start-Sleep -Milliseconds 900
$calendarNav = Find-ById "Nav_calendar"
Check (($calendarNav -eq $null) -or $calendarNav.Current.IsOffscreen) "隐藏后导航栏不再显示“日历”"
Save-Shot "8-settings-nav-hidden"
Check (Click-ById "NavToggle_calendar") "恢复“日历”导航入口"
Start-Sleep -Milliseconds 900
$calendarNav2 = Find-ById "Nav_calendar"
Check (($calendarNav2 -ne $null) -and (-not $calendarNav2.Current.IsOffscreen)) "恢复后导航栏重新显示“日历”"

# ---------- 6. 停用 / 启用 Checklist 模块 ----------
Check (Click-ById "ModuleEnabled_checklist") "停用 Checklist 模块"
Start-Sleep -Milliseconds 1100
$checklistNav = Find-ById "Nav_checklist"
Check (($checklistNav -eq $null) -or $checklistNav.Current.IsOffscreen) "停用模块后导航入口隐藏"
Save-Shot "9-module-disabled"
Check (Click-ById "ModuleEnabled_checklist") "重新启用 Checklist 模块"
Start-Sleep -Milliseconds 1100

# ---------- 7. 深色主题 ----------
Check (Select-ComboItem "ThemeBox" "深色") "切换为深色主题"
Save-Shot "10-theme-dark"
Select-ComboItem "ThemeBox" "跟随系统" | Out-Null
Start-Sleep -Milliseconds 800

# ---------- 8. 工作台 ----------
Check (Nav "workbench") "导航回工作台"
Save-Shot "11-workbench"

Write-Host "`n关闭应用并校验数据..."
Get-Process SimWorkbench -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Seconds 2
$after = Get-Stats

Check ([int]$after["任务"] -gt [int]$before["任务"]) "UI 创建的任务已保存到数据库" "before=$($before['任务']) after=$($after['任务'])"
Check ([int]$after["主题日志"] -gt [int]$before["主题日志"]) "UI 创建的主题日志已保存" "before=$($before['主题日志']) after=$($after['主题日志'])"
Check ([int]$after["清单"] -gt [int]$before["清单"]) "UI 创建的清单已保存" "before=$($before['清单']) after=$($after['清单'])"
Check ($after["隐藏导航"] -eq "0") "导航设置已持久化" "hidden=$($after['隐藏导航'])"
Check ($after["主题"] -eq "System") "主题设置已持久化" "theme=$($after['主题'])"

Write-Host "`n通过 $script:pass 项，失败 $script:fail 项"
if ($script:fail -gt 0) { exit 1 }
