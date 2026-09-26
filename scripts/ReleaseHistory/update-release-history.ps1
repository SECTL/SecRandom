# 一键刷新 SecRandom 版本历史数据集
#
#   powershell -File scripts/ReleaseHistory/update-release-history.ps1
#   pwsh      -File scripts/ReleaseHistory/update-release-history.ps1   # 装有 PowerShell 7 时
#
# 依次执行：抓取 GitHub 数据 -> 解析仓库 CHANGELOG -> 重建 XLSX。
# 需要：Python 3.11+（含 openpyxl）、本机 gh CLI 已登录（Star 时间线需要身份）。

[CmdletBinding()]
param(
    [string]$Repo = "SECTL/SecRandom"
)

$ErrorActionPreference = "Stop"
$root = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$scripts = Join-Path $root "scripts\ReleaseHistory"

function Invoke-Step {
    param([string]$Name, [string[]]$Command)
    Write-Host "==> $Name" -ForegroundColor Cyan
    & $Command[0] @($Command[1..($Command.Count - 1)])
    if ($LASTEXITCODE -ne 0) {
        throw "$Name 失败（exit $LASTEXITCODE）"
    }
}

Invoke-Step "抓取 GitHub 数据" @("python", (Join-Path $scripts "fetch_github_data.py"), "--repo", $Repo)
Invoke-Step "解析 CHANGELOG"   @("python", (Join-Path $scripts "parse_changelog.py"))
Invoke-Step "生成 XLSX"        @("python", (Join-Path $scripts "build_workbook.py"))

$out = Join-Path $root "docs\release-history\SecRandom-版本历史.xlsx"
Write-Host ""
Write-Host "完成：$out" -ForegroundColor Green
Write-Host "提示：人工结论请维护 docs/release-history/curated/annotations.json，然后重跑本脚本。"
