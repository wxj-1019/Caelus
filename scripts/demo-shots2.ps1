# @author zenjiro 18967498922@163.com
# 文件用途 演示截图驱动（探针版）：一次提权内串行调用 --screenshot 单页探针（子进程继承提权，无额外 UAC）。
#         与 demo-shots.ps1（实机 UIA 版）互补——本版无 UIA，不依赖交互桌面，确定性出长页全图。
$ErrorActionPreference = "Stop"
$exe = "E:\project\Caelus\Caelus.exe"
$out = "E:\project\Caelus\docs\shots"
$log = New-Object System.Collections.Generic.List[string]
function Log([string]$line) { $script:log.Add($line); Write-Output $line }

function Shot([string]$page, [string]$file, [int]$h, [int]$scrollY) {
    if (Test-Path "$out\$file") { Remove-Item "$out\$file" -Force }
    $p = Start-Process -FilePath $exe -ArgumentList "--screenshot", "$out\$file", $page, "$h", "$scrollY" -Wait -PassThru
    if (Test-Path "$out\$file") { Log ("OK " + $file + " exit=" + $p.ExitCode) }
    else { Log ("FAIL " + $file + " exit=" + $p.ExitCode) }
}

# 视口高度 1032：系统会把窗口钳在工作区内（约 1080），1032 保证整窗渲染不出黑边
Shot "dev" "demo-dev-focus.png" 1032 360
Shot "settings" "demo-settings-ide.png" 1032 260
Shot "settings" "demo-settings-svc.png" 1032 700

[System.IO.File]::WriteAllLines("E:\project\Caelus\docs\demo-shots2-result.txt", $log, (New-Object System.Text.UTF8Encoding($false)))
Log "written"
