#!/usr/bin/env pwsh
# Builds the reproducible perf data template consumed by Measure-PerfScenario.ps1.
#
# 用法（在仓库根目录执行一次即可）：
#   pwsh -File scripts/Perf/Initialize-PerfTemplate.ps1
#
# 模板 = 一份"已经完成首次引导"的 data 目录：
#   * 复制开发者本机 bin/Release 下已经跑过的 data/config/settings.json（其中 guide_completed/accepted_* 已完成），
#     因此每次测量都不会弹出首次引导；
#   * 删掉安全凭据、设备 UUID、用量统计、控制节点状态、日志/缓存/备份/证明等非确定性子目录，
#     于是每次测量都是"全新安装 + 已接受引导"的同一初始状态；
#   * 名单与奖品池按固定数量生成（学生 40、奖品 12），保证抽奖场景有候选池可抽。
#
# 模板一旦生成，后续所有 baseline / after-change 测量都复用它，保证数据侧完全一致。

[CmdletBinding()]
param(
    [string]$SourceDataRoot = '',
    [string]$TemplateDir = '',
    [int]$StudentCount = 40,
    [int]$PrizeCount = 12
)

$ErrorActionPreference = 'Stop'
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
if ([string]::IsNullOrWhiteSpace($SourceDataRoot)) {
    $SourceDataRoot = Join-Path $repoRoot 'SecRandom.Desktop\bin\Release\net10.0-windows10.0.19041.0\data'
}
if ([string]::IsNullOrWhiteSpace($TemplateDir)) {
    $TemplateDir = Join-Path $repoRoot 'artifacts\perf\template'
}

if (-not (Test-Path -LiteralPath (Join-Path $SourceDataRoot 'config\settings.json'))) {
    throw "Source data root has no config/settings.json: $SourceDataRoot. Run the Release build once first."
}

$dataOut = Join-Path $TemplateDir 'data'
if (Test-Path -LiteralPath $TemplateDir) {
    Remove-Item -LiteralPath $TemplateDir -Recurse -Force
}
New-Item -ItemType Directory -Path $dataOut -Force | Out-Null

# Copy the whole existing data root, then strip everything that must not carry over.
Copy-Item -Path (Join-Path $SourceDataRoot '*') -Destination $dataOut -Recurse -Force
$volatile = @(
    'config\security',
    'config\control',
    'config\control-plane',
    'config\plugins',
    'config\device-uuid.json',
    'config\usage-stats.json',
    'logs',
    'cache',
    'backup',
    'proofs',
    'TEMP',
    'plugins'
)
foreach ($relative in $volatile) {
    $target = Join-Path $dataOut $relative
    if (Test-Path -LiteralPath $target) {
        Remove-Item -LiteralPath $target -Recurse -Force
    }
}
foreach ($directory in @('logs', 'cache', 'backup', 'proofs', 'TEMP', 'audio', 'plugins')) {
    New-Item -ItemType Directory -Path (Join-Path $dataOut $directory) -Force | Out-Null
}

$utf8 = [System.Text.UTF8Encoding]::new($false)
function Write-JsonFile {
    param([string]$Path, [object]$Value)
    $parent = Split-Path -Parent $Path
    if (-not (Test-Path -LiteralPath $parent)) { New-Item -ItemType Directory -Path $parent -Force | Out-Null }
    $json = $Value | ConvertTo-Json -Depth 6
    [System.IO.File]::WriteAllText($Path, $json + "`n", $utf8)
}

$students = @()
for ($i = 1; $i -le $StudentCount; $i++) {
    $number = '{0:D2}' -f $i
    $students += [ordered]@{
        exists    = $true
        gender    = ''
        group     = ''
        id        = "S$number"
        name      = "学生$number"
        record_id = [guid]::NewGuid().ToString()
        tags      = ''
    }
}
Write-JsonFile -Path (Join-Path $dataOut 'list\roll_call_list\default.json') -Value ([ordered]@{
        students        = $students
        attached_objects = [ordered]@{}
    })

$prizes = @()
for ($i = 1; $i -le $PrizeCount; $i++) {
    $number = '{0:D2}' -f $i
    $prizes += [ordered]@{
        exists    = $true
        id        = "P$number"
        name      = "奖品$number"
        record_id = [guid]::NewGuid().ToString()
        tags      = ''
    }
}
Write-JsonFile -Path (Join-Path $dataOut 'list\lottery_list\default.json') -Value ([ordered]@{
        prizes          = $prizes
        attached_objects = [ordered]@{}
    })

Write-Host "Perf template ready: $TemplateDir"
Write-Host "  settings : $(Join-Path $dataOut 'config\settings.json')"
Write-Host "  students : $StudentCount  prizes: $PrizeCount"