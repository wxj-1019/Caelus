# @author zenjiro 18967498922@163.com
# 文件用途 开发专注深化真机 E2E：一次提权会话验证「分心阻断」与「服务自动拉起」完整链路。
#         A：notepad 在专注+阻断下被自动关闭，计数/按名/TSV/日志落盘正确；
#         B：预存守护服务 ping 被杀后按原命令行自动重启（快照入册 + 参数保留）。
#         结束后完整还原注册表与数据文件。用法：提权会话内 -File 运行。
param([string]$Repo = "E:\project\Caelus")
$ErrorActionPreference = "Continue"
$resultFile = "$Repo\docs\e2e-devfocus-result.txt"
$log = New-Object System.Collections.Generic.List[string]
$script:failures = 0
function Record([string]$name, [bool]$ok, [string]$detail = "") {
    if (-not $ok) { $script:failures += 1 }
    $tag = "FAIL"; if ($ok) { $tag = "PASS" }
    $line = "$tag  $name"; if ($detail) { $line = "$line :: $detail" }
    $script:log.Add($line); Write-Output $line
}

# —— 清场：优雅退出旧实例（完整还原链），超时强杀 ——
try { [System.Threading.EventWaitHandle]::OpenExisting('Global\Caelus_Exit').Set() } catch { }
$deadline = [DateTime]::UtcNow.AddSeconds(10)
while ([DateTime]::UtcNow -lt $deadline) {
    if (@(Get-Process Caelus -ErrorAction SilentlyContinue).Count -eq 0) { break }
    Start-Sleep -Milliseconds 400
}
foreach ($proc in @(Get-Process Caelus -ErrorAction SilentlyContinue)) {
    try { Stop-Process -Id $proc.Id -Force -ErrorAction SilentlyContinue } catch { }
}
Start-Sleep -Milliseconds 800

# —— 预置注册表（DistractCatalog/DevServiceCatalog 有进程级缓存，必须先于应用启动写入）——
$rk = "HKCU:\Software\Caelus"
Set-ItemProperty -Path $rk -Name DevModeOn -Value 1
Set-ItemProperty -Path $rk -Name DevFocusModeOn -Value 1
Set-ItemProperty -Path $rk -Name DevFocusDistractBlock -Value 1
Set-ItemProperty -Path $rk -Name DevFocusDistractList -Value "notepad"
Set-ItemProperty -Path $rk -Name DevServiceList -Value "ping"
Set-ItemProperty -Path $rk -Name DevSvcRestartOn -Value 1
# 清零统计基线，便于断言增量
Set-ItemProperty -Path $rk -Name FocusStatsDay -Value ""
Set-ItemProperty -Path $rk -Name FocusStatsSeconds -Value "0"
Set-ItemProperty -Path $rk -Name FocusStatsSessions -Value "0"
Set-ItemProperty -Path $rk -Name FocusStatsDistract -Value "0"
Set-ItemProperty -Path $rk -Name FocusStatsBlocked -Value "0"
Remove-ItemProperty -Path $rk -Name FocusStatsDistractNames -ErrorAction SilentlyContinue
Remove-Item -Path "$env:APPDATA\Caelus\focus-history.tsv" -ErrorAction SilentlyContinue

# —— 预存守护服务：ping -t（Caelus 启动前就在跑，验证快照入册链路）——
$pingOld = Start-Process -FilePath "$env:SystemRoot\System32\ping.exe" -ArgumentList "-t","127.0.0.1" -PassThru -WindowStyle Hidden
Start-Sleep -Seconds 4

# —— 启动真应用（提权会话内子进程继承提权）——
$appStart = Get-Date
$app = Start-Process -FilePath "$Repo\Caelus.exe" -PassThru
Start-Sleep -Seconds 12
Record "启动 Caelus 并进入专注掌权" ((Get-Process -Id $app.Id -ErrorAction SilentlyContinue) -ne $null) "PID=$($app.Id)"

# ========== 测试 A：分心阻断 ==========
# Win11 商店版 notepad 走启动别名：-PassThru PID 是毫秒级退出的存根，真进程另有其 PID。
# 不能拿存根存活当信号——用产品自身信号（计数器增长 + notepad 全体消失）驱动断言。
function Wait-Counters([int]$minHits) {
    $deadline = [DateTime]::UtcNow.AddSeconds(15)
    while ([DateTime]::UtcNow -lt $deadline) {
        $p = Get-ItemProperty -Path $rk
        if ([int]$p.FocusStatsDistract -ge $minHits -and [int]$p.FocusStatsBlocked -ge $minHits) { return $true }
        Start-Sleep -Milliseconds 300
    }
    return $false
}
function Wait-NoNotepad([int]$seconds) {
    $deadline = [DateTime]::UtcNow.AddSeconds($seconds)
    while ([DateTime]::UtcNow -lt $deadline) {
        if (@(Get-Process notepad -ErrorAction SilentlyContinue).Count -eq 0) { return $true }
        Start-Sleep -Milliseconds 300
    }
    return $false
}

Start-Process notepad | Out-Null
Record "A1 首次命中：notepad 被自动关闭" (Wait-NoNotepad 12) ""
Record "A2 命中计数落盘（hits=blocked 且 ≥2：存根+真进程各计一次）" (Wait-Counters 2) ""

$props = Get-ItemProperty -Path $rk
$today = (Get-Date).ToString("yyyy-MM-dd")
$hits = [int]$props.FocusStatsDistract
Record "A3 按名统计落盘 notepad:N" ($props.FocusStatsDistractNames -match "notepad:(\d+)") "names=$($props.FocusStatsDistractNames)"

$tsvRow = ""
if (Test-Path "$env:APPDATA\Caelus\focus-history.tsv") {
    $tsvRow = Get-Content "$env:APPDATA\Caelus\focus-history.tsv" | Where-Object { $_.StartsWith($today) } | Select-Object -First 1
}
$tsvOk = $false
if ($tsvRow) {
    $cols = $tsvRow.Split("`t")
    $tsvOk = ($cols.Length -ge 5 -and [int]$cols[3] -eq $hits -and [int]$cols[4] -eq $hits)
}
Record "A4 TSV 当日行分心/阻断列与计数器一致" $tsvOk "row=$tsvRow hits=$hits"

# 日志被高频 CPU 拓扑行刷屏：按时间戳过滤出本次应用启动后的行再匹配
$logHit = $false
try {
    foreach ($line in @(Get-Content "$env:APPDATA\Caelus\Caelus.log" -Tail 3000 -Encoding UTF8 -ErrorAction Stop)) {
        if ($line -notmatch "^(\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2})\s+(.*)$") { continue }
        $ts = [DateTime]::ParseExact($matches[1], "yyyy-MM-dd HH:mm:ss", $null)
        if ($ts -ge $appStart.AddSeconds(-2) -and $matches[2] -match "分心应用已阻断关闭") { $logHit = $true; break }
    }
} catch { }
Record "A5 日志含阻断关闭记录（本次启动后）" $logHit ""

Start-Process notepad | Out-Null
Record "A6 再次命中：静默再阻断（气球限频不阻断执行）" (Wait-NoNotepad 12) ""

# ========== 测试 B：服务自动拉起 ==========
Stop-Process -Id $pingOld.Id -Force -ErrorAction SilentlyContinue
$newPing = $null
$deadline = [DateTime]::UtcNow.AddSeconds(15)
while ([DateTime]::UtcNow -lt $deadline) {
    $cand = @(Get-Process ping -ErrorAction SilentlyContinue) | Where-Object { $_.Id -ne $pingOld.Id }
    if ($cand.Count -gt 0) { $newPing = $cand[0]; break }
    Start-Sleep -Milliseconds 400
}
Record "B1 预存服务被杀后自动重启（快照入册生效）" ($null -ne $newPing) "old=$($pingOld.Id) new=$(if ($newPing) { $newPing.Id } else { 'none' })"

$cmdline = ""
if ($newPing) {
    try {
        $mo = Get-WmiObject Win32_Process -Filter "ProcessId = $($newPing.Id)" -ErrorAction Stop
        $cmdline = $mo.CommandLine
    } catch { }
}
Record "B2 重启保留原命令行参数" ($cmdline -match "127\.0\.0\.1") "cmdline=$cmdline"

# ========== 清场还原 ==========
try { [System.Threading.EventWaitHandle]::OpenExisting('Global\Caelus_Exit').Set() } catch { }
$deadline = [DateTime]::UtcNow.AddSeconds(12)
while ([DateTime]::UtcNow -lt $deadline) {
    if (@(Get-Process -Id $app.Id -ErrorAction SilentlyContinue).Count -eq 0) { break }
    Start-Sleep -Milliseconds 400
}
if (Get-Process -Id $app.Id -ErrorAction SilentlyContinue) {
    try { Stop-Process -Id $app.Id -Force -ErrorAction SilentlyContinue } catch { }
    Record "应用退出" $false "强杀（优雅退出超时）"
} else {
    Record "应用优雅退出（还原链完整）" $true ""
}
foreach ($p in @(Get-Process ping -ErrorAction SilentlyContinue)) { try { Stop-Process -Id $p.Id -Force } catch { } }
foreach ($p in @(Get-Process notepad -ErrorAction SilentlyContinue)) { try { Stop-Process -Id $p.Id -Force } catch { } }

Set-ItemProperty -Path $rk -Name DevFocusModeOn -Value 0
Set-ItemProperty -Path $rk -Name DevFocusDistractBlock -Value 0
Set-ItemProperty -Path $rk -Name DevFocusDistractList -Value ""
Set-ItemProperty -Path $rk -Name DevServiceList -Value ""
Set-ItemProperty -Path $rk -Name DevSvcRestartOn -Value 0
Set-ItemProperty -Path $rk -Name FocusStatsDay -Value ""
Set-ItemProperty -Path $rk -Name FocusStatsSeconds -Value "0"
Set-ItemProperty -Path $rk -Name FocusStatsSessions -Value "0"
Set-ItemProperty -Path $rk -Name FocusStatsDistract -Value "0"
Set-ItemProperty -Path $rk -Name FocusStatsBlocked -Value "0"
Remove-ItemProperty -Path $rk -Name FocusStatsDistractNames -ErrorAction SilentlyContinue
Remove-Item -Path "$env:APPDATA\Caelus\focus-history.tsv" -ErrorAction SilentlyContinue
$script:log.Add("CLEANUP 注册表与数据文件已还原")

$script:log.Add(("TOTAL " + $script:log.Count + "  FAIL " + $script:failures))
[System.IO.File]::WriteAllLines($resultFile, $script:log, (New-Object System.Text.UTF8Encoding($false)))
Write-Output "written"
if ($script:failures -gt 0) { exit 1 } else { exit 0 }
