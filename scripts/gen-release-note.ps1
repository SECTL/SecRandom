# Renders the release body from the tag changelog plus a download table and a SHA256 table.
#
# Default mode (build_publish.yml) hashes the freshly built files in ./artifacts/release/output.
# -InventoryPath describes release assets by name + SHA256 instead, which is what build_ios.yml needs
# when it refreshes the notes of a release that is already published. -BodyPath keeps the body that is
# already live and replaces only the generated tables in it, so hand written prose survives.
[CmdletBinding()]
param(
    [string]$InventoryPath = '',
    [string]$BodyPath = '',
    [string]$OutFile = './release-note.md'
)

$repo = $env:repoName
$tag = $env:tagName
if ([string]::IsNullOrWhiteSpace($repo)) {
    throw "Environment variable 'repoName' is required."
}
if ([string]::IsNullOrWhiteSpace($tag)) {
    throw "Environment variable 'tagName' is required."
}

$changelogPath = "./CHANGELOG/v3/${tag}/CHANGELOG.md"
$releaseNotePath = $OutFile
$outDir = "./artifacts/release/output"

if (-not [string]::IsNullOrWhiteSpace($InventoryPath)) {
    if (-not (Test-Path -LiteralPath $InventoryPath -PathType Leaf)) {
        throw "Inventory file not found: $InventoryPath"
    }

    $inventory = Get-Content -LiteralPath $InventoryPath -Raw | ConvertFrom-Json
    $inventoryEntries = foreach ($entry in @($inventory)) {
        if ([string]::IsNullOrWhiteSpace($entry.name)) {
            throw "Every inventory entry needs a file name."
        }
        if ([string]::IsNullOrWhiteSpace($entry.sha256)) {
            throw "Inventory entry '$($entry.name)' has no SHA256 digest."
        }
        [pscustomobject]@{ Name = $entry.name; Sha256 = $entry.sha256.ToUpperInvariant() }
    }
    $files = @($inventoryEntries | Sort-Object Name)

    if (-not $files) {
        throw "No assets found in $InventoryPath"
    }
} else {
    if (-not (Test-Path $outDir)) {
        throw "Output directory not found: $outDir"
    }

    $localFiles = Get-ChildItem -Path $outDir -File | Sort-Object Name
    if (-not $localFiles) {
        throw "No files found in $outDir"
    }

    $files = foreach ($file in $localFiles) {
        [pscustomobject]@{ Name = $file.Name; Sha256 = (Get-FileHash $file.FullName -Algorithm SHA256).Hash }
    }
}

$downloadSummary = @"
**下载链接**

| 文件名 | GitHub | SECTL 高速 |
| --- | --- | --- |
"@

foreach ($file in $files) {
    $gh = "https://github.com/${repo}/releases/download/${tag}/$($file.Name)"
    $stk = "https://stk.sectl.cn/SecRandom/%E6%80%9D%E6%8B%93%E5%88%9B%E8%81%94%20Gihub%20%E9%95%9C%E5%83%8F%E6%BA%90/${tag}/$($file.Name)"
    $downloadSummary += "`n| $($file.Name) | [下载](${gh}) | [下载](${stk}) |"
}

$md5Summary = @"
> [!important]
> 下载时请核对文件 SHA256。

<details>
<summary>展开 SHA256 </summary>

| 文件名 | SHA256 |
| --- | --- |
"@

foreach ($file in $files) {
    $md5Summary += "`n| $($file.Name) | ``$($file.Sha256)`` |"
}
$md5Summary += "`n`n</details>"

$changelog = if (-not [string]::IsNullOrWhiteSpace($BodyPath)) {
    if (-not (Test-Path -LiteralPath $BodyPath -PathType Leaf)) {
        throw "Release body file not found: $BodyPath"
    }

    $body = Get-Content -LiteralPath $BodyPath -Raw
    $tableMarker = $body.LastIndexOf('**下载链接**')
    if ($tableMarker -ge 0) {
        $body.Substring(0, $tableMarker).TrimEnd()
    } else {
        $body.TrimEnd()
    }
} elseif (Test-Path $changelogPath) {
    Get-Content $changelogPath -Raw
} else {
    "- 发布说明待补充。`n---`n"
}

$fullContent = "$changelog`n`n$downloadSummary`n`n$md5Summary"
Set-Content -Path $releaseNotePath -Value $fullContent -Encoding utf8

Write-Host "Release Note generated with $($files.Count) asset(s)"
