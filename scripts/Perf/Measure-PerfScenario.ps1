#!/usr/bin/env pwsh
# 可复现的 SecRandom 桌面端 CPU/RAM 场景测量脚本。
#
# 设计要点（保证 baseline 与每次改动后的 after-change 完全可比）：
#   * 使用固定的 Release 构建可执行文件；开始前把该构建目录下的 data 目录整体替换为"从模板复制的一份
#     全新数据"（settings.json / 名单 / 空历史 / 无安全凭据 / 无缓存），因为桌面端的 data 根就是
#     可执行文件所在目录下的 data。因此每次测量的初始状态完全一致；模板本身保存在
#     artifacts/perf/template，可随时重建；
#   * 场景通过单实例 IPC 命名管道驱动，不需要鼠标键盘：idle 什么都不做，draw 发送 roll_call/quick_draw，
#     settings 在设置窗口里轮换大页面；
#   * CPU 百分比由 Process.TotalProcessorTime 的增量除以采样间隔得到；cpu_pct_total 已除以逻辑处理器数，
#     与任务管理器"总 CPU 占用"同口径，cpu_pct_one_core 是单核口径（满一核 = 100）；
#   * 托管内存指标取自 dotnet-counters collect（--duration 自动结束），另存为 -counters.csv；
#   * 结果写入 artifacts/perf/results：每次运行一份 -samples.csv、一份 -summary.json，并追加一行到 _index.csv。
#
# 用法示例：
#   pwsh -File scripts/Perf/Measure-PerfScenario.ps1 -Label baseline -Scenario all
#   pwsh -File scripts/Perf/Measure-PerfScenario.ps1 -Label baseline -Scenario idle -IdleSeconds 60

[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$Label,
    [ValidateSet('idle', 'draw', 'settings', 'all')][string]$Scenario = 'idle',
    [int]$IdleSeconds = 60,
    [int]$DrawCount = 8,
    [int]$DrawIntervalMs = 3000,
    [int]$SettingsDwellMs = 3000,
    [string[]]$SettingsPages = @(
        'settings/basic', 'settings/list', 'settings/history',
        'settings/notification', 'settings/voice', 'settings/more'
    ),
    [string]$AppExe = '',
    [string]$TemplateDir = '',
    [string]$ResultDir = '',
    [int]$StartupTimeoutSeconds = 120,
    [int]$SettleSeconds = 10,
    [int]$SampleIntervalMs = 500,
    [switch]$SkipCounters
)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'SecRandomPerfIpc.ps1')

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
if ([string]::IsNullOrWhiteSpace($AppExe)) {
    $AppExe = Join-Path $repoRoot 'SecRandom.Desktop\bin\Release\net10.0-windows10.0.19041.0\SecRandom.Desktop.exe'
}
if ([string]::IsNullOrWhiteSpace($TemplateDir)) {
    $TemplateDir = Join-Path $repoRoot 'artifacts\perf\template'
}
if ([string]::IsNullOrWhiteSpace($ResultDir)) {
    $ResultDir = Join-Path $repoRoot 'artifacts\perf\results'
}
if (-not (Test-Path -LiteralPath $AppExe)) {
    throw "Release build not found: $AppExe. Build SecRandom.Desktop in Release first."
}
if (-not (Test-Path -LiteralPath (Join-Path $TemplateDir 'data'))) {
    throw "Perf template not found: $TemplateDir. Run scripts/Perf/Initialize-PerfTemplate.ps1 first."
}
New-Item -ItemType Directory -Path $ResultDir -Force | Out-Null

$logicalCpus = [Environment]::ProcessorCount
$processorName = (Get-CimInstance Win32_Processor | Select-Object -First 1 -ExpandProperty Name).Trim()
$osCaption = (Get-CimInstance Win32_OperatingSystem).Caption

$runId = '{0}-{1}-{2}' -f $Label, $Scenario, (Get-Date -Format 'yyyyMMdd-HHmmss')
$appDirectory = Split-Path -Parent $AppExe
$dataRoot = Join-Path $appDirectory 'data'

# ---------------------------------------------------------------- 1. 清掉本 exe 的残留实例
Get-Process -Name 'SecRandom.Desktop' -ErrorAction SilentlyContinue | Where-Object {
    try { $_.Path -eq $AppExe } catch { $false }
} | ForEach-Object {
    Write-Host "Stopping stale SecRandom.Desktop pid $($_.Id)"
    Stop-Process -Id $_.Id -Force -ErrorAction SilentlyContinue
}
Start-Sleep -Milliseconds 800

# ---------------------------------------------------------------- 2. 重置为模板数据（全新初始状态）
if (Test-Path -LiteralPath $dataRoot) { Remove-Item -LiteralPath $dataRoot -Recurse -Force }
Copy-Item -Path (Join-Path $TemplateDir 'data') -Destination $dataRoot -Recurse -Force
Write-Host "[$runId] data root reset from template: $dataRoot"

# ---------------------------------------------------------------- 3. 启动应用
$startedUtc = [DateTime]::UtcNow
$app = Start-Process -FilePath $AppExe -WorkingDirectory $appDirectory -PassThru
Write-Host "[$runId] started pid $($app.Id)"

# ---------------------------------------------------------------- 4. 等主窗口出现
$startupMs = $null
$windowSeenUtc = $null
while ((([DateTime]::UtcNow) - $startedUtc).TotalSeconds -lt $StartupTimeoutSeconds) {
    Start-Sleep -Milliseconds 250
    $exited = $true
    try { $exited = $app.HasExited } catch { $exited = $true }
    if ($exited) { break }
    try { $app.Refresh() } catch { break }
    if ($app.MainWindowHandle -ne [IntPtr]::Zero) {
        $windowSeenUtc = [DateTime]::UtcNow
        $startupMs = [math]::Round(($windowSeenUtc - $startedUtc).TotalMilliseconds, 0)
        break
    }
}

if ($null -eq $startupMs) {
    $logDir = Join-Path $dataRoot 'logs'
    $tail = @()
    if (Test-Path -LiteralPath $logDir) {
        $tail = Get-ChildItem -LiteralPath $logDir -ErrorAction SilentlyContinue |
            Sort-Object LastWriteTime | Select-Object -Last 1 |
            ForEach-Object { Get-Content -LiteralPath $_.FullName -Tail 60 -ErrorAction SilentlyContinue }
    }
    throw ("App did not show a main window within {0}s (exited={1}). Log tail:{2}{3}" -f
        $StartupTimeoutSeconds, $app.HasExited, [Environment]::NewLine, ($tail -join [Environment]::NewLine))
}
Write-Host "[$runId] main window ready, startup ${startupMs}ms"

# 数据目录校验：日志必须落在本次重置的 data root 里。
$appUsedRoot = $false
if (Test-Path -LiteralPath (Join-Path $dataRoot 'logs')) {
    $appUsedRoot = @(Get-ChildItem -LiteralPath (Join-Path $dataRoot 'logs') -ErrorAction SilentlyContinue).Count -gt 0
}
if (-not $appUsedRoot) {
    throw "App does not appear to write into $dataRoot (no log file)."
}

# ---------------------------------------------------------------- 5. 预热 + 起始快照
Start-Sleep -Seconds $SettleSeconds
$app.Refresh()
$startupSnapshot = [pscustomobject]@{
    ws_mb      = [math]::Round($app.WorkingSet64 / 1MB, 1)
    private_mb = [math]::Round($app.PrivateMemorySize64 / 1MB, 1)
    threads    = $app.Threads.Count
    handles    = $app.HandleCount
}

# ---------------------------------------------------------------- 6. 场景定义
# 抽奖动作会把每次 IPC 结果追加到 $script:draws，供汇总使用。
$draws = [System.Collections.Generic.List[object]]::new()
$script:draws = $draws
$phaseDefs = [System.Collections.Generic.List[object]]::new()
if ($Scenario -in 'idle', 'all') {
    $phaseDefs.Add([pscustomobject]@{ name = 'idle'; duration_ms = $IdleSeconds * 1000; invoke = $null; args = @() })
}
if ($Scenario -in 'draw', 'all') {
    for ($i = 1; $i -le $DrawCount; $i++) {
        $phaseDefs.Add([pscustomobject]@{
                name        = 'draw'
                duration_ms = $DrawIntervalMs
                invoke      = {
                    param($index)
                    $sw = [System.Diagnostics.Stopwatch]::StartNew()
                    $raw = $null
                    try { $raw = Send-SecRandomIpc -Url 'roll_call/quick_draw' }
                    catch { $raw = $null }
                    $sw.Stop()
                    $record = [pscustomobject]@{
                        index      = $index
                        latency_ms = [math]::Round($sw.Elapsed.TotalMilliseconds, 1)
                        success    = $false
                        status     = $null
                        code       = $null
                        message    = $null
                        error      = $null
                    }
                    $response = ConvertFrom-SecRandomIpcResponse $raw
                    if ($null -eq $response) {
                        $record.error = 'no_response'
                    }
                    else {
                        $record.success = [bool]$response.success
                        if ($response.result) {
                            $record.status = $response.result.status
                            $record.code = $response.result.code
                            $record.message = $response.result.message
                        }
                        if ($response.error) {
                            $record.error = "$($response.error.code): $($response.error.message)"
                        }
                    }
                    $script:draws.Add($record) | Out-Null
                }
                args        = @($i)
            })
    }
}
if ($Scenario -in 'settings', 'all') {
    foreach ($route in $SettingsPages) {
        $phaseDefs.Add([pscustomobject]@{
                name        = 'settings'
                duration_ms = $SettingsDwellMs
                invoke      = { param($url) $null = Send-SecRandomIpc -Url $url }
                args        = @($route)
            })
    }
}
if ($phaseDefs.Count -eq 0) { throw 'No phases defined for scenario.' }

$totalPhaseSeconds = [math]::Round((($phaseDefs | Measure-Object -Property duration_ms -Sum).Sum) / 1000.0, 0)
Write-Host "[$runId] phases: $(($phaseDefs | ForEach-Object { $_.name }) -join ',') total ${totalPhaseSeconds}s"

# ---------------------------------------------------------------- 7. dotnet-counters 旁路采样
$countersPath = Join-Path $ResultDir "$runId-counters.csv"
$countersProc = $null
$countersExe = $null
if (-not $SkipCounters) {
    $countersExe = (Get-Command dotnet-counters -ErrorAction SilentlyContinue).Source
}
if ($countersExe) {
    $counterArg = 'System.Runtime[cpu-usage,working-set,gc-heap-size,alloc-rate,gen-2-gc-count,loh-size,threadpool-thread-count,time-in-gc]'
    $duration = [timespan]::FromSeconds([double]$totalPhaseSeconds + 30).ToString('dd\:hh\:mm\:ss')
    $counterArgs = @(
        'collect', '-p', "$($app.Id)", '--refresh-interval', '1',
        '--counters', $counterArg, '--format', 'csv', '-o', $countersPath,
        '--duration', $duration
    )
    try {
        $countersProc = Start-Process -FilePath $countersExe -ArgumentList $counterArgs -PassThru -WindowStyle Hidden `
            -RedirectStandardOutput (Join-Path $ResultDir "$runId-counters.out") `
            -RedirectStandardError (Join-Path $ResultDir "$runId-counters.err")
        Write-Host "[$runId] dotnet-counters pid $($countersProc.Id) -> $countersPath"
    }
    catch {
        Write-Warning "dotnet-counters failed to start: $_"
        $countersProc = $null
    }
}
elseif (-not $SkipCounters) {
    Write-Warning 'dotnet-counters not found; managed memory counters skipped.'
}

# ---------------------------------------------------------------- 8. 采样循环
$samples = [System.Collections.Generic.List[object]]::new()
$script:app = $app
$script:samples = $samples
$script:prevCpuMs = $app.TotalProcessorTime.TotalMilliseconds
$script:prevTs = [DateTime]::UtcNow

function Add-PerfSample {
    param([string]$Phase)
    $exited = $true
    try { $exited = $script:app.HasExited } catch { $exited = $true }
    if ($exited) { return $false }
    try { $script:app.Refresh() } catch { return $false }

    $now = [DateTime]::UtcNow
    $cpuMs = $script:app.TotalProcessorTime.TotalMilliseconds
    $elapsedMs = ($now - $script:prevTs).TotalMilliseconds
    $deltaMs = $cpuMs - $script:prevCpuMs
    $script:prevCpuMs = $cpuMs
    $script:prevTs = $now
    if ($elapsedMs -le 0) { return $true }

    $script:samples.Add([pscustomobject]@{
            phase           = $Phase
            timestamp_utc   = $now.ToString('o')
            cpu_pct_total   = [math]::Round(100.0 * $deltaMs / $elapsedMs / $logicalCpus, 3)
            cpu_pct_one_core = [math]::Round(100.0 * $deltaMs / $elapsedMs, 3)
            ws_mb           = [math]::Round($script:app.WorkingSet64 / 1MB, 1)
            private_mb      = [math]::Round($script:app.PrivateMemorySize64 / 1MB, 1)
            threads         = $script:app.Threads.Count
            handles         = $script:app.HandleCount
        }) | Out-Null
    return $true
}

foreach ($phase in $phaseDefs) {
    if ($null -ne $phase.invoke) {
        # 数组 splat：把 phase.args 的每一项作为一个位置参数传给动作，避免把数组整体塞给第一个参数。
        $phaseArgs = @($phase.args)
        try { & $phase.invoke @phaseArgs }
        catch { Write-Warning "phase '$($phase.name)' action failed: $_" }
    }
    $deadline = [DateTime]::UtcNow.AddMilliseconds([double]$phase.duration_ms)
    while ([DateTime]::UtcNow -lt $deadline) {
        Start-Sleep -Milliseconds $SampleIntervalMs
        $alive = Add-PerfSample -Phase $phase.name
        if (-not $alive) { break }
    }
    $exitedNow = $true
    try { $exitedNow = $app.HasExited } catch { $exitedNow = $true }
    if ($exitedNow) { break }
}

# ---------------------------------------------------------------- 9. 收尾：结束 counters、优雅退出
if ($null -ne $countersProc) {
    try { $countersProc | Wait-Process -Timeout 60 -ErrorAction SilentlyContinue } catch { }
    if (-not $countersProc.HasExited) {
        try { Stop-Process -Id $countersProc.Id -Force -ErrorAction SilentlyContinue } catch { }
    }
}

$finalSnapshot = $null
try {
    $app.Refresh()
    $finalSnapshot = [pscustomobject]@{
        ws_mb      = [math]::Round($app.WorkingSet64 / 1MB, 1)
        private_mb = [math]::Round($app.PrivateMemorySize64 / 1MB, 1)
        threads    = $app.Threads.Count
        handles    = $app.HandleCount
    }
}
catch { }

$exitRequested = $false
try {
    $null = Send-SecRandomIpc -Url 'tray/exit' -TimeoutMs 5000
    $exitRequested = $true
    $wait = 0
    while ($wait -lt 20) {
        Start-Sleep -Milliseconds 500
        $hasExited = $true
        try { $hasExited = $app.HasExited } catch { $hasExited = $true }
        if ($hasExited) { break }
        $wait++
    }
}
catch { }
$exitedCleanly = $false
try { $exitedCleanly = $app.HasExited } catch { $exitedCleanly = $true }
if (-not $exitedCleanly) {
    Stop-Process -Id $app.Id -Force -ErrorAction SilentlyContinue
}

# ---------------------------------------------------------------- 10. 汇总
function Get-MetricStats {
    param([object]$Values)
    $raw = @($Values | Where-Object { $null -ne $_ } | ForEach-Object { [double]$_ })
    if ($raw.Count -eq 0) { return $null }
    $sorted = @($raw | Sort-Object)
    $count = $sorted.Count
    $sum = 0.0
    foreach ($value in $sorted) { $sum += $value }
    $median = if ($count % 2 -eq 1) { $sorted[[int](($count - 1) / 2)] }
    else { ($sorted[$count / 2 - 1] + $sorted[$count / 2]) / 2.0 }
    $p95Index = [math]::Min($count - 1, [math]::Max(0, [int][math]::Ceiling($count * 0.95) - 1))
    return [pscustomobject]@{
        count  = $count
        avg    = [math]::Round($sum / $count, 3)
        median = [math]::Round([double]$median, 3)
        p95    = [math]::Round([double]$sorted[$p95Index], 3)
        max    = [math]::Round([double]$sorted[$count - 1], 3)
    }
}

$phaseSummaries = @()
foreach ($phaseName in @($phaseDefs | ForEach-Object { $_.name } | Select-Object -Unique)) {
    $phaseSamples = @($samples | Where-Object { $_.phase -eq $phaseName })
    $phaseSummaries += [pscustomobject]@{
        phase          = $phaseName
        samples        = $phaseSamples.Count
        cpu_pct_total  = Get-MetricStats ($phaseSamples | ForEach-Object { $_.cpu_pct_total })
        cpu_pct_one_core = Get-MetricStats ($phaseSamples | ForEach-Object { $_.cpu_pct_one_core })
        ws_mb          = Get-MetricStats ($phaseSamples | ForEach-Object { $_.ws_mb })
        private_mb     = Get-MetricStats ($phaseSamples | ForEach-Object { $_.private_mb })
        threads        = Get-MetricStats ($phaseSamples | ForEach-Object { $_.threads })
        handles        = Get-MetricStats ($phaseSamples | ForEach-Object { $_.handles })
    }
}

# dotnet-counters CSV 解析（列名按 dotnet-counters 现行输出，找不到就跳过）
$counterSummary = @{}
if (Test-Path -LiteralPath $countersPath) {
    try {
        $rows = @(Import-Csv -LiteralPath $countersPath)
        if ($rows.Count -gt 0) {
            $columns = $rows[0].PSObject.Properties.Name
            $nameColumn = @($columns | Where-Object { $_ -match 'Counter Name' }) | Select-Object -First 1
            $valueColumn = @($columns | Where-Object { $_ -match 'Counter Value|Mean/Increment' }) | Select-Object -First 1
            if ($nameColumn -and $valueColumn) {
                foreach ($group in ($rows | Group-Object -Property $nameColumn)) {
                    $values = @()
                    foreach ($row in $group.Group) {
                        $text = "$($row.PSObject.Properties[$valueColumn].Value)"
                        if ($text -match '^[0-9.]+$') { $values += [double]$text }
                    }
                    if ($values.Count -gt 0) {
                        $counterSummary[$group.Name] = [pscustomobject]@{
                            samples = $values.Count
                            last    = [math]::Round($values[$values.Count - 1], 3)
                            avg     = [math]::Round((($values | Measure-Object -Average).Average), 3)
                            max     = [math]::Round((($values | Measure-Object -Maximum).Maximum), 3)
                        }
                    }
                }
            }
        }
    }
    catch { Write-Warning "counters parse failed: $_" }
}

$summary = [pscustomobject]@{
    label              = $Label
    scenario           = $Scenario
    run_id             = $runId
    app_exe            = $AppExe
    app_exe_created_utc = (Get-Item -LiteralPath $AppExe).LastWriteTimeUtc.ToString('o')
    started_utc        = $startedUtc.ToString('o')
    startup_ms         = $startupMs
    settle_seconds     = $SettleSeconds
    sample_interval_ms = $SampleIntervalMs
    machine            = [pscustomobject]@{
        processor      = $processorName
        logical_cpus   = $logicalCpus
        os             = $osCaption
        ps_version     = $PSVersionTable.PSVersion.ToString()
        dotnet_version = (& dotnet --version).Trim()
    }
    startup_snapshot   = $startupSnapshot
    final_snapshot     = $finalSnapshot
    phases             = $phaseSummaries
    draws              = @($draws)
    counters           = $counterSummary
    exit_requested     = $exitRequested
    exited_cleanly     = $exitedCleanly
    data_root          = $dataRoot
}

$samplesCsv = Join-Path $ResultDir "$runId-samples.csv"
$samples | Export-Csv -LiteralPath $samplesCsv -NoTypeInformation -Encoding utf8
$summaryJson = Join-Path $ResultDir "$runId-summary.json"
$summary | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath $summaryJson -Encoding utf8

$idleStats = ($phaseSummaries | Where-Object { $_.phase -eq 'idle' } | Select-Object -First 1)
$drawStats = ($phaseSummaries | Where-Object { $_.phase -eq 'draw' } | Select-Object -First 1)
$settingsStats = ($phaseSummaries | Where-Object { $_.phase -eq 'settings' } | Select-Object -First 1)
$drawLatency = if ($draws.Count -gt 0) { Get-MetricStats ($draws | ForEach-Object { $_.latency_ms }) } else { $null }
$drawOkCount = @($draws | Where-Object { $_.success }).Count
# dotnet-counters CSV 用的是显示名（如 "GC Heap Size (MB)"），按其显示名取最后一个值
function Get-CounterLast {
    param([string]$DisplayName)
    if ($counterSummary.ContainsKey($DisplayName)) { return $counterSummary[$DisplayName].last }
    return $null
}
$gcHeap = Get-CounterLast 'GC Heap Size (MB)'
$cpuOneCore = Get-CounterLast 'CPU Usage (%)'
$wsCounter = Get-CounterLast 'Working Set (MB)'
$allocRate = Get-CounterLast 'Allocation Rate (B / 1 sec)'
$gen2 = Get-CounterLast 'Gen 2 GC Count (Count / 1 sec)'
$timeInGc = Get-CounterLast '% Time in GC since last GC (%)'
$threadPool = Get-CounterLast 'ThreadPool Thread Count'

$indexPath = Join-Path $ResultDir '_index.csv'
$indexRow = [pscustomobject]@{
    label                  = $Label
    scenario               = $Scenario
    run_id                 = $runId
    startup_ms             = $startupMs
    idle_cpu_avg           = if ($idleStats) { $idleStats.cpu_pct_total.avg } else { $null }
    idle_cpu_p95           = if ($idleStats) { $idleStats.cpu_pct_total.p95 } else { $null }
    draw_cpu_avg           = if ($drawStats) { $drawStats.cpu_pct_total.avg } else { $null }
    draw_cpu_p95           = if ($drawStats) { $drawStats.cpu_pct_total.p95 } else { $null }
    draw_latency_avg_ms    = if ($drawLatency) { $drawLatency.avg } else { $null }
    draw_latency_p95_ms    = if ($drawLatency) { $drawLatency.p95 } else { $null }
    draw_ok                = "$drawOkCount/$($draws.Count)"
    settings_cpu_avg       = if ($settingsStats) { $settingsStats.cpu_pct_total.avg } else { $null }
    ws_after_settle_mb     = $startupSnapshot.ws_mb
    ws_end_mb              = if ($finalSnapshot) { $finalSnapshot.ws_mb } else { $null }
    private_after_settle_mb = $startupSnapshot.private_mb
    private_end_mb         = if ($finalSnapshot) { $finalSnapshot.private_mb } else { $null }
    threads_end            = if ($finalSnapshot) { $finalSnapshot.threads } else { $null }
    handles_end            = if ($finalSnapshot) { $finalSnapshot.handles } else { $null }
    gc_heap_mb             = $gcHeap
    cpu_counter_total_pct  = $cpuOneCore
    ws_counter_mb          = $wsCounter
    alloc_rate_bytes_s     = $allocRate
    gen2_gc_rate_per_s     = $gen2
    threadpool_threads     = $threadPool
    time_in_gc_pct         = $timeInGc
}
if (Test-Path -LiteralPath $indexPath) {
    $indexRow | Export-Csv -LiteralPath $indexPath -NoTypeInformation -Append -Encoding utf8
}
else {
    $indexRow | Export-Csv -LiteralPath $indexPath -NoTypeInformation -Encoding utf8
}

Write-Host ''
Write-Host "=== [$runId] result ==="
$indexRow | Format-List
Write-Host "samples : $samplesCsv"
Write-Host "summary : $summaryJson"
Write-Host "counters: $countersPath"
Write-Host "index   : $indexPath"