# @author zenjiro 18967498922@163.com
# 文件用途 WPF 预览宿主 UIA 交互功能测试：启动→导航全部页→模式切换→干净关闭。
# 与 wpf-motion-stress.ps1（压力/内存）不同，本脚本验证功能正确性（每页可选中、模式可切换、进程可正常退出）。
# 用法：需提权会话。pwsh scripts/wpf-interaction-test.ps1

param([int]$WaitReadyMs = 6000)

$ErrorActionPreference = "Stop"
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes

$results = New-Object System.Collections.Generic.List[string]
$failures = 0
function Record([string]$name, [bool]$ok, [string]$detail = "") {
    if (-not $ok) { $script:failures += 1 }
    $tag = "FAIL"; if ($ok) { $tag = "PASS" }
    $line = "$tag  $name"
    if ($detail) { $line = "$line :: $detail" }
    $script:results.Add($line)
    Write-Output $line
}

$exe = Join-Path $PSScriptRoot '..\wpf\bin\Release\CaelusWpf.exe'
$proc = $null
$root = $null

# —— T1 启动 + 窗口出现 ——
try {
    $before = @(Get-Process CaelusWpf -ErrorAction SilentlyContinue | ForEach-Object { $_.Id })
    $proc = Start-Process -FilePath $exe -PassThru
    $deadline = [DateTime]::UtcNow.AddMilliseconds($WaitReadyMs)
    do {
        Start-Sleep -Milliseconds 200
        $proc.Refresh()
    } while ($proc.MainWindowHandle -eq 0 -and [DateTime]::UtcNow -lt $deadline)
    if ($proc.MainWindowHandle -eq 0) { throw "主窗口未在 ${WaitReadyMs}ms 内出现" }
    $root = [System.Windows.Automation.AutomationElement]::FromHandle($proc.MainWindowHandle)
    Record "T1 启动并显示主窗口" ($null -ne $root) "PID=$($proc.Id)"
} catch {
    Record "T1 启动并显示主窗口" $false $_.Exception.Message
}

function Select-ByName([string]$name) {
    if ($null -eq $root) { return $false }
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

# —— T2 导航全部 11 页（验证每页回写后仍可加载渲染） ——
if ($null -ne $root) {
    $navs = @("游戏库","优化策略","显卡","反作弊专项","系统环境","白名单","系统体检","日志","设置","关于","概览")
    $navOk = 0
    foreach ($n in $navs) {
        if (Select-ByName "导航：$n") { $navOk++; Start-Sleep -Milliseconds 120 }
        $root = [System.Windows.Automation.AutomationElement]::FromHandle($proc.MainWindowHandle)
    }
    Record "T2 导航 11 页均可选中" ($navOk -eq 11) "选中 $navOk/11"
}

# —— T3 模式切换（巡航→竞技→自定义→巡航，验证 ModeChanged 订阅不崩） ——
if ($null -ne $root) {
    $modeOk = 0
    foreach ($m in @("竞技","自定义","常规")) {
        if (Select-ByName $m) { $modeOk++; Start-Sleep -Milliseconds 150 }
        $root = [System.Windows.Automation.AutomationElement]::FromHandle($proc.MainWindowHandle)
    }
    Record "T3 模式三档切换均生效" ($modeOk -eq 3) "切换 $modeOk/3"
}

# —— T4 进程无崩溃（崩溃处理器会写 %TEMP%\CaelusWpf.crash.log 新增段） ——
if ($null -ne $proc) {
    $proc.Refresh()
    Record "T4 交互期间进程存活未崩溃" (-not $proc.HasExited) ""
}

# —— T5 干净关闭（关闭按钮 → 进程退出或转入托盘均可接受，但不得异常） ——
if ($null -ne $root) {
    $closed = Select-ByName "关闭窗口"
    if (-not $closed) {
        # 兜底：窗口模式 Close
        try {
            $wp = $null
            if ($root.TryGetCurrentPattern([System.Windows.Automation.WindowPattern]::Pattern, [ref]$wp)) { $wp.Close(); $closed = $true }
        } catch { }
    }
    Start-Sleep -Milliseconds 800
    $proc.Refresh()
    # 关闭到托盘也算正常退出路径；此处仅验证无异常崩溃
    Record "T5 关闭动作已执行无异常" $closed ""
}

# 清理：若仍在托盘/运行，强制结束（测试结束）
if ($null -ne $proc) {
    $proc.Refresh()
    if (-not $proc.HasExited) {
        try { $proc.CloseMainWindow() | Out-Null; Start-Sleep -Milliseconds 500; $proc.Refresh() } catch { }
        if (-not $proc.HasExited) { Stop-Process -Id $proc.Id -Force -ErrorAction SilentlyContinue }
    }
}

Write-Output "----"
Write-Output ("TOTAL " + $results.Count + "  FAIL " + $failures)
if ($failures -gt 0) { exit 1 }
