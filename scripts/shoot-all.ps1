# @author zenjiro 18967498922@163.com
# 文件用途 一键全页截图验收：构建后 --wpf-shot 全矩阵出图到 docs\shots\
#         （13 页 × 明暗 × 三模式 = 概览 4 组合 + 其余 12 页 × 6 组合，共 76 张 PNG）
# ASCII-safe usage: powershell -ExecutionPolicy Bypass -File scripts\shoot-all.ps1
param([string]$OutDir = "$PSScriptRoot\..\docs\shots")
$ErrorActionPreference = "Stop"
$repo = (Resolve-Path "$PSScriptRoot\..").Path
Push-Location $repo
try {
    # 构建输出直接可见：失败时能当场看到 MSBuild 错误
    cmd /c build.cmd
    if ($LASTEXITCODE -ne 0) { throw "构建失败" }
    $exe = Join-Path $repo "Caelus.exe"
    & $exe --wpf-shot $OutDir
    if ($LASTEXITCODE -ne 0) { throw "截图探针失败" }
    Write-Host "全矩阵截图已输出到 $OutDir"
}
finally { Pop-Location }
