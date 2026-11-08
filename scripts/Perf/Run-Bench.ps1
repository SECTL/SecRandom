# ---------------------------------------------------------------------------------------------
# Run-Bench.ps1 - 批量跑基准/对照测量（顺序执行，绝不并行，避免互相干扰）
#
# 用法：
#   powershell -NoProfile -ExecutionPolicy Bypass -File scripts\Perf\Run-Bench.ps1 -Label base
#   powershell -NoProfile -ExecutionPolicy Bypass -File scripts\Perf\Run-Bench.ps1 -Label A1 `
#       -Only idle,draw
#
# 每个场景默认跑 3 次（idle/draw），settings 跑 2 次；结果只记在 artifacts/perf/results/_index.csv。
# ---------------------------------------------------------------------------------------------
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$Label,
    [string[]]$Only = @('idle', 'draw', 'settings'),

    [int]$IdleSeconds = 60,
    [int]$SettleSeconds = 10,
    [int]$DrawCount = 10,
    [int]$SettingsSeconds = 45
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$measure = Join-Path $PSScriptRoot 'Measure-PerfScenario.ps1'

$scenarios = @{
    idle     = @{ rep = 3; args = @{ Scenario = 'idle'; IdleSeconds = $IdleSeconds; SettleSeconds = $SettleSeconds } }
    draw     = @{ rep = 3; args = @{ Scenario = 'draw'; IdleSeconds = 15; SettleSeconds = $SettleSeconds; DrawCount = $DrawCount } }
    settings = @{ rep = 2; args = @{ Scenario = 'settings'; IdleSeconds = $SettingsSeconds; SettleSeconds = $SettleSeconds } }
}

if (Get-Process -Name 'SecRandom.Desktop' -ErrorAction SilentlyContinue) {
    throw 'A SecRandom.Desktop instance is running; stop it before benchmarking (scenarios must not overlap).'
}

foreach ($scenario in $Only) {
    if (-not $scenarios.ContainsKey($scenario)) { throw "Unknown scenario '$scenario'" }
    $definition = $scenarios[$scenario]
    for ($rep = 1; $rep -le $definition.rep; $rep++) {
        $runLabel = '{0}-{1}-{2}' -f $Label, $scenario, $rep
        Write-Host ''
        Write-Host "=== bench run $runLabel ==="
        $params = $definition.args
        & $measure -Label $runLabel @params
    }
}

Write-Host ''
Write-Host 'Bench complete. Aggregated rows:'
$indexPath = Join-Path $root 'artifacts\perf\results\_index.csv'
if (Test-Path -LiteralPath $indexPath) {
    Import-Csv -LiteralPath $indexPath | ForEach-Object {
        "  {0,-22} startup={1,6}ms idle_cpu={2,7}% draw_cpu={3,7}% draw_lat(avg)={4,8}ms ws={5,6}MB priv={6,6}MB gc_heap={7,6}" -f `
            $_.run_id, $_.startup_ms, $_.idle_cpu_avg, $_.draw_cpu_avg, $_.draw_latency_avg_ms, $_.ws_after_settle_mb, $_.private_after_settle_mb, $_.gc_heap_mb
    }
}