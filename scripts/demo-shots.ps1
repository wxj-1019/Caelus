# @author zenjiro 18967498922@163.com
# 文件用途 实机演示截图驱动：提权会话内启动真应用，导航开发专注/设置并滚动到新增控件，
#         落屏截图输出 docs\shots\demo-*.png，写结果文件供非提权侧读取。
# 用法：需提权会话。powershell -ExecutionPolicy Bypass -File scripts\demo-shots.ps1
$ErrorActionPreference = "Stop"
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing

# DPI 感知：此后 SetWindowPos/GetWindowRect/CopyFromScreen 全部按物理像素一致工作
Add-Type -TypeDefinition 'using System.Runtime.InteropServices; public static class DpiAware { [DllImport("user32.dll")] public static extern bool SetProcessDPIAware(); }'
[DpiAware]::SetProcessDPIAware() | Out-Null

$resultFile = "E:\project\Caelus\docs\demo-shots-result.txt"
$shotsDir = "E:\project\Caelus\docs\shots"
$log = New-Object System.Collections.Generic.List[string]
function Log([string]$line) { $script:log.Add($line); Write-Output $line }

# —— 关闭旧实例：先优雅（全局退出事件走完整还原链），后强杀 ——
try { [System.Threading.EventWaitHandle]::OpenExisting('Global\Caelus_Exit').Set() } catch { }
$deadline = [DateTime]::UtcNow.AddSeconds(12)
while ([DateTime]::UtcNow -lt $deadline)
{
    $left = @(Get-Process Caelus, Caelus.dev, CaelusWpf -ErrorAction SilentlyContinue)
    if ($left.Count -eq 0) { break }
    Start-Sleep -Milliseconds 400
}
foreach ($proc in @(Get-Process Caelus, Caelus.dev, CaelusWpf -ErrorAction SilentlyContinue))
{
    try { Stop-Process -Id $proc.Id -Force -ErrorAction SilentlyContinue } catch { }
}
Start-Sleep -Milliseconds 800

# —— 启动真应用 ——
$exe = "E:\project\Caelus\Caelus.exe"
$proc = Start-Process -FilePath $exe -PassThru
Start-Sleep -Milliseconds 1500

# —— 解析真实主窗口：按 PID 枚举桌面子窗口取面积最大者。
# Process.MainWindowHandle 会竞争锁在已关闭的启动屏句柄上，导致 UIA 树永远为空 —— 不可用。
$rootEl = $null
function Resolve-MainWindow {
    $script:rootEl = $null
    $cond = New-Object System.Windows.Automation.PropertyCondition(
        [System.Windows.Automation.AutomationElement]::ProcessIdProperty, $script:proc.Id)
    $wins = [System.Windows.Automation.AutomationElement]::RootElement.FindAll(
        [System.Windows.Automation.TreeScope]::Children, $cond)
    $bestArea = 0.0
    foreach ($w in $wins) {
        try {
            if ($w.Current.IsOffscreen) { continue }
            $r = $w.Current.BoundingRectangle
            $area = $r.Width * $r.Height
            if ($area -gt $bestArea) { $bestArea = $area; $script:rootEl = $w }
        } catch { }
    }
    return ($null -ne $rootEl -and $bestArea -gt 10000)
}

$deadline = [DateTime]::UtcNow.AddSeconds(20)
while ([DateTime]::UtcNow -lt $deadline) {
    if (Resolve-MainWindow) { break }
    Start-Sleep -Milliseconds 500
}
if ($null -eq $rootEl) { Log "FAIL 主窗口未出现"; [System.IO.File]::WriteAllLines($resultFile, $log, (New-Object System.Text.UTF8Encoding($false))); exit 1 }
$hwnd = [IntPtr]$rootEl.Current.NativeWindowHandle
Log ("OK 主窗口出现 PID=" + $proc.Id)

Add-Type @"
using System;
using System.Runtime.InteropServices;
public static class Win32 {
    [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr hWnd, IntPtr after, int x, int y, int cx, int cy, uint flags);
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hWnd);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
}
"@

# —— 窗口定位：主屏内尽可能高；物理尺寸按 DPI 缩放换算，保持 1196 DIP 版式 ——
$wa = [System.Windows.Forms.Screen]::PrimaryScreen.WorkingArea
$g = [System.Drawing.Graphics]::FromHwnd($hwnd)
$scale = $g.DpiX / 96.0
$g.Dispose()
$winW = [Math]::Min([int](1196 * $scale) + 8, $wa.Width - 40)
$winH = [Math]::Min([int](1000 * $scale), $wa.Height - 60)
[Win32]::SetWindowPos($hwnd, [IntPtr](-1), 20, 20, $winW, $winH, 0x0040) | Out-Null   # HWND_TOPMOST
[Win32]::SetForegroundWindow($hwnd) | Out-Null
Start-Sleep -Milliseconds 1200
$null = Resolve-MainWindow   # 定位后重取一次，避免句柄失效

$null = Resolve-MainWindow; $root = $rootEl

function Select-ByName([string]$name) {
    $all = $root.FindAll([System.Windows.Automation.TreeScope]::Descendants,
        [System.Windows.Automation.Condition]::TrueCondition)
    foreach ($el in $all) {
        if ($el.Current.Name -ne $name) { continue }
        $pat = $null
        if ($el.TryGetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern, [ref]$pat)) {
            $pat.Select()
            return $true
        }
    }
    return $false
}

function Scroll-Detail([int]$percent) {
    # 找带 ScrollPattern 且包围盒最高的可滚动容器（主内容区，而非侧边导航），竖向滚到指定百分比
    $all = $root.FindAll([System.Windows.Automation.TreeScope]::Descendants,
        [System.Windows.Automation.Condition]::TrueCondition)
    $best = $null; $bestH = 0
    foreach ($el in $all) {
        $pat = $null
        if (-not $el.TryGetCurrentPattern([System.Windows.Automation.ScrollPattern]::Pattern, [ref]$pat)) { continue }
        try {
            if (-not $pat.Current.VerticallyScrollable) { continue }
            $h = $el.Current.BoundingRectangle.Height
            if ($h -gt $bestH) { $bestH = $h; $best = $pat }
        } catch { }
    }
    if ($null -ne $best) { try { $best.SetScrollPercent(-1, [double]$percent); return $true } catch { } }
    return $false
}

function Scroll-IntoView([string]$name) {
    # 按元素名精确滚动入镜（ScrollItemPattern），比盲滚百分比可靠
    $all = $root.FindAll([System.Windows.Automation.TreeScope]::Descendants,
        [System.Windows.Automation.Condition]::TrueCondition)
    foreach ($el in $all) {
        if ($el.Current.Name -ne $name) { continue }
        $pat = $null
        if ($el.TryGetCurrentPattern([System.Windows.Automation.ScrollItemPattern]::Pattern, [ref]$pat)) {
            try { $pat.ScrollIntoView(); return $true } catch { }
        }
    }
    return $false
}

function Shot([string]$file) {
    # 按窗口实际矩形取景（物理像素），别的窗口/桌面元素不入镜
    $r = New-Object Win32+RECT
    $null = [Win32]::GetWindowRect($hwnd, [ref]$r)
    $w = $r.Right - $r.Left
    $h = $r.Bottom - $r.Top
    $bmp = New-Object System.Drawing.Bitmap($w, $h)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.CopyFromScreen($r.Left, $r.Top, 0, 0, (New-Object System.Drawing.Size($w, $h)))
    $g.Dispose()
    $bmp.Save("$shotsDir\$file", [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
    Log ("OK 截图 $file " + $w + "x" + $h)
}

# —— 开发专注详情页：顶部（Hero + 专注模式卡 + 趋势卡上沿）——
# 导航项在启动屏动画完成后才就绪：重试等待，避免过早枚举拿不到
$navDev = $false
for ($i = 0; $i -lt 10 -and -not $navDev; $i++) {
    $null = Resolve-MainWindow; $root = $rootEl
    $navDev = Select-ByName "导航：开发专注"
    if (-not $navDev) { Start-Sleep -Milliseconds 800 }
}
if ($navDev) {
    Start-Sleep -Milliseconds 1500
    $null = Scroll-Detail 0
    Start-Sleep -Milliseconds 500
    Shot "demo-dev-top.png"
    # 滚到底：近 7 日专注趋势卡完整可见
    $null = Scroll-Detail 100
    Start-Sleep -Milliseconds 500
    Shot "demo-dev-trend.png"
} else { Log "FAIL 导航：开发专注 未选中" }

# —— 设置页：ScrollItem 精确滚到新增控件行，拍两张 ——
$navSet = $false
for ($i = 0; $i -lt 6 -and -not $navSet; $i++) {
    $navSet = Select-ByName "导航：设置"
    if (-not $navSet) { Start-Sleep -Milliseconds 800 }
}
if ($navSet) {
    Start-Sleep -Milliseconds 1500
    $null = Resolve-MainWindow; $root = $rootEl
    if (Scroll-IntoView "自定义 IDE 进程列表") { Start-Sleep -Milliseconds 600; Shot "demo-settings-ide.png" }
    else { Log "FAIL 未找到 IDE 自定义行" }
    if (Scroll-IntoView "服务自动拉起") { Start-Sleep -Milliseconds 600; Shot "demo-settings-toggles.png" }
    else { Log "FAIL 未找到 服务自动拉起 行" }
} else { Log "FAIL 导航：设置 未选中" }

# —— 优雅退出 ——
try { [System.Threading.EventWaitHandle]::OpenExisting('Global\Caelus_Exit').Set() } catch { }
$deadline = [DateTime]::UtcNow.AddSeconds(10)
while ([DateTime]::UtcNow -lt $deadline)
{
    if (@(Get-Process -Id $proc.Id -ErrorAction SilentlyContinue).Count -eq 0) { break }
    Start-Sleep -Milliseconds 400
}
$alive = @(Get-Process -Id $proc.Id -ErrorAction SilentlyContinue).Count -gt 0
if ($alive) { try { Stop-Process -Id $proc.Id -Force -ErrorAction SilentlyContinue } catch { } ; Log "WARN 强杀退出" } else { Log "OK 优雅退出" }

[System.IO.File]::WriteAllLines($resultFile, $log, (New-Object System.Text.UTF8Encoding($false)))
Log "written"
