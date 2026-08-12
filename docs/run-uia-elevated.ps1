# 提权运行 WPF UIA 交互套件；输出写入指定文件，供非提权侧读取。
$ErrorActionPreference = "Stop"
$resultFile = "E:\project\Caelus\docs\uia-run-result.txt"
$log = New-Object System.Collections.Generic.List[string]
$log.Add("=== elevated UIA run @ " + (Get-Date -Format "yyyy-MM-dd HH:mm:ss") + " ===")

# 先关闭可能存在的旧 CaelusWpf 实例，避免多实例干扰
foreach ($proc in @(Get-Process CaelusWpf -ErrorAction SilentlyContinue))
{
    try { $proc.CloseMainWindow() | Out-Null; Start-Sleep -Milliseconds 400; $proc.Refresh() } catch { }
    if (-not $proc.HasExited) { try { Stop-Process -Id $proc.Id -Force -ErrorAction SilentlyContinue } catch { } }
}
Start-Sleep -Milliseconds 800

& powershell -NoProfile -ExecutionPolicy Bypass -File "E:\project\Caelus\scripts\wpf-interaction-test.ps1" 2>&1 | ForEach-Object { $log.Add($_) }

[System.IO.File]::WriteAllLines($resultFile, $log, (New-Object System.Text.UTF8Encoding($false)))
Write-Output "written"
