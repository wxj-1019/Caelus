param(
    [int]$ProcessId = 0,
    [int]$NavigationRounds = 10,
    [int]$ModeRounds = 10,
    [int]$SettleSeconds = 5,
    [string]$OutputPath = ""
)

$ErrorActionPreference = "Stop"
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes

$process = if ($ProcessId -gt 0) {
    Get-Process -Id $ProcessId
} else {
    Get-Process -Name CaelusWpf | Select-Object -First 1
}

$root = [System.Windows.Automation.AutomationElement]::FromHandle($process.MainWindowHandle)
$names = @(
    "导航：概览", "导航：游戏库", "导航：优化策略", "导航：显卡",
    "导航：反作弊专项", "导航：系统环境", "导航：白名单", "导航：系统体检",
    "导航：日志", "导航：设置", "导航：关于"
)

function Select-ByName([string]$name) {
    $condition = New-Object System.Windows.Automation.PropertyCondition(
        [System.Windows.Automation.AutomationElement]::AutomationIdProperty, "")
    $all = $root.FindAll(
        [System.Windows.Automation.TreeScope]::Descendants,
        [System.Windows.Automation.Condition]::TrueCondition)
    foreach ($element in $all) {
        if ($element.Current.Name -ne $name) { continue }
        $pattern = $null
        if ($element.TryGetCurrentPattern(
            [System.Windows.Automation.SelectionItemPattern]::Pattern, [ref]$pattern)) {
            $pattern.Select()
            return $true
        }
    }
    return $false
}

$process.Refresh()
$memoryStart = $process.WorkingSet64
$privateStart = $process.PrivateMemorySize64
$failures = 0
for ($round = 0; $round -lt $NavigationRounds; $round++) {
    foreach ($name in $names) {
        if (-not (Select-ByName $name)) { $failures++ }
        Start-Sleep -Milliseconds 35
    }
}

for ($round = 0; $round -lt $ModeRounds; $round++) {
    foreach ($mode in @("常规", "竞技", "自定义")) {
        if (-not (Select-ByName $mode)) { $failures++ }
        Start-Sleep -Milliseconds 45
    }
}

Select-ByName "导航：概览" | Out-Null
Select-ByName "常规" | Out-Null
Start-Sleep -Seconds $SettleSeconds
[GC]::Collect()
[GC]::WaitForPendingFinalizers()
$process.Refresh()
$memoryEnd = $process.WorkingSet64
$privateEnd = $process.PrivateMemorySize64
$result = @(
    "NAVIGATION_SWITCHES=$($NavigationRounds * $names.Count)",
    "MODE_SWITCHES=$($ModeRounds * 3)",
    "FAILURES=$failures",
    "WORKING_SET_START_MB=$([Math]::Round($memoryStart / 1MB, 1))",
    "WORKING_SET_END_MB=$([Math]::Round($memoryEnd / 1MB, 1))",
    "WORKING_SET_DELTA_MB=$([Math]::Round(($memoryEnd - $memoryStart) / 1MB, 1))",
    "PRIVATE_START_MB=$([Math]::Round($privateStart / 1MB, 1))",
    "PRIVATE_END_MB=$([Math]::Round($privateEnd / 1MB, 1))",
    "PRIVATE_DELTA_MB=$([Math]::Round(($privateEnd - $privateStart) / 1MB, 1))"
)
$result | ForEach-Object { Write-Output $_ }
if ($OutputPath) { $result | Set-Content -LiteralPath $OutputPath -Encoding UTF8 }
if ($failures -gt 0) { exit 1 }
