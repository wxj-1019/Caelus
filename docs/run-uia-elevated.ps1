# 提权运行 WPF UIA 交互套件；输出写入指定文件，供非提权侧读取。
$ErrorActionPreference = "Stop"
$resultFile = "E:\project\Caelus\docs\uia-run-result.txt"
$log = New-Object System.Collections.Generic.List[string]
$log.Add("=== elevated UIA run @ " + (Get-Date -Format "yyyy-MM-dd HH:mm:ss") + " ===")

# 先优雅关闭可能存在的旧实例（全局退出事件走完整还原链），避免多实例干扰
try { [System.Threading.EventWaitHandle]::OpenExisting('Global\Caelus_Exit').Set() } catch { }
$deadline = [DateTime]::UtcNow.AddSeconds(12)
while ([DateTime]::UtcNow -lt $deadline)
{
    $left = @(Get-Process Caelus.dev, Caelus -ErrorAction SilentlyContinue)
    if ($left.Count -eq 0) { break }
    Start-Sleep -Milliseconds 400
}
foreach ($proc in @(Get-Process Caelus.dev, Caelus -ErrorAction SilentlyContinue))
{
    try { Stop-Process -Id $proc.Id -Force -ErrorAction SilentlyContinue } catch { }
}
Start-Sleep -Milliseconds 800

& powershell -NoProfile -ExecutionPolicy Bypass -File "E:\project\Caelus\scripts\wpf-interaction-test.ps1" 2>&1 | ForEach-Object { $log.Add($_) }

[System.IO.File]::WriteAllLines($resultFile, $log, (New-Object System.Text.UTF8Encoding($false)))
Write-Output "written"
