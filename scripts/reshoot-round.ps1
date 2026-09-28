# @author zenjiro 18967498922@163.com
# 文件用途 收口重摄驱动：矩阵全页 + 刷新 demo-dev-focus + 新增 demo-daily-trend。
$ErrorActionPreference = "Stop"
$exe = "E:\project\Caelus\Caelus.exe"
$out = "E:\project\Caelus\docs\shots"
$log = New-Object System.Collections.Generic.List[string]
function Log([string]$line) { $script:log.Add($line); Write-Output $line }

# 矩阵（--wpf-shot 全 76 张）
$p = Start-Process -FilePath $exe -ArgumentList "--wpf-shot", $out -Wait -PassThru
Log ("matrix exit=" + $p.ExitCode)

# demo 刷新：开发页（编译行/目标/合计）与日常页（新趋势卡 + 维护倒计时）
foreach ($shot in @(
    @("dev", "demo-dev-focus.png", "1032", "360"),
    @("daily", "demo-daily-trend.png", "1032", "380")
)) {
    $file = $shot[1]
    if (Test-Path "$out\$file") { Remove-Item "$out\$file" -Force }
    $p = Start-Process -FilePath $exe -ArgumentList "--screenshot", "$out\$file", $shot[0], $shot[2], $shot[3] -Wait -PassThru
    if (Test-Path "$out\$file") { Log ("OK " + $file) } else { Log ("FAIL " + $file + " exit=" + $p.ExitCode) }
}

[System.IO.File]::WriteAllLines("E:\project\Caelus\docs\reshoot-result.txt", $log, (New-Object System.Text.UTF8Encoding($false)))
Log "written"
