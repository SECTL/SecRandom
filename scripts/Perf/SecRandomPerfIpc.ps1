# Single-instance IPC helper shared by the perf scripts.
#
# SecRandom's single-instance host accepts newline-delimited JSON frames on a named pipe
# (SecRandom.Core/Services/SingleInstance/SingleInstanceService.cs, pipe name
# `SecRandom_IPC_SecRandom_3F2A1B0E`). A frame is
#     {"version":1,"type":"url","payload":{"url":"<route>[?query]"}}
# where <route> is the same relative route a secrandom:// activation accepts, e.g.
# roll_call/quick_draw. The host answers with one JSON line.

function Send-SecRandomIpc {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)][string]$Url,
        [int]$TimeoutMs = 30000,
        [string]$PipeName = 'SecRandom_IPC_SecRandom_3F2A1B0E'
    )

    $pipe = [System.IO.Pipes.NamedPipeClientStream]::new('.', $PipeName, [System.IO.Pipes.PipeDirection]::InOut)
    try {
        $pipe.Connect($TimeoutMs)
        $utf8 = [System.Text.UTF8Encoding]::new($false)
        $writer = [System.IO.StreamWriter]::new($pipe, $utf8)
        $reader = [System.IO.StreamReader]::new($pipe, $utf8)
        $frame = [ordered]@{
            version = 1
            type    = 'url'
            payload = [ordered]@{ url = $Url }
        } | ConvertTo-Json -Compress -Depth 4
        $writer.WriteLine($frame)
        $writer.Flush()
        return $reader.ReadLine()
    }
    finally {
        $pipe.Dispose()
    }
}

function ConvertFrom-SecRandomIpcResponse {
    param([string]$Response)
    if ([string]::IsNullOrWhiteSpace($Response)) { return $null }
    try {
        return $Response | ConvertFrom-Json
    }
    catch {
        return $null
    }
}