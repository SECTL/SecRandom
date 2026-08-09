$repo = $env:repoName
$tag = $env:tagName
if ([string]::IsNullOrWhiteSpace($repo)) {
    throw "Environment variable 'repoName' is required."
}
if ([string]::IsNullOrWhiteSpace($tag)) {
    throw "Environment variable 'tagName' is required."
}

$changelogPath = "./CHANGELOG/v3/${tag}/CHANGELOG.md"
$releaseNotePath = "./release-note.md"
$outDir = "./artifacts/release/output"

if (-not (Test-Path $outDir)) {
    throw "Output directory not found: $outDir"
}

$files = Get-ChildItem -Path $outDir -File | Sort-Object Name
if (-not $files) {
    throw "No files found in $outDir"
}

$downloadSummary = @"
**下载链接**

| 文件名 | GitHub | SECTL高速 |
| --- | --- | --- |
"@

foreach ($file in $files) {
    $gh = "https://github.com/${repo}/releases/download/${tag}/$($file.Name)"
    $stk = "https://stk.sectl.top/SecRandom/${tag}/$($file.Name)"
    $downloadSummary += "`n| $($file.Name) | [下载](${gh}) | [下载](${stk}) |"
}

$md5Summary = @"
> [!important]
> 下载时请核对文件 MD5。

| 文件名 | MD5 |
| --- | --- |
"@

$hashes = [ordered]@{}
foreach ($file in $files) {
    $hash = (Get-FileHash $file.FullName -Algorithm MD5).Hash
    $hashes[$file.Name] = $hash

    $md5Summary += "`n| $($file.Name) | ``$hash`` |"
}

$json = ConvertTo-Json $hashes -Compress
$md5Summary += "`n`n<!-- SECRANDOM_PKG_MD5 $json -->"

$changelog = if (Test-Path $changelogPath) {
    Get-Content $changelogPath -Raw
} else {
    "- 发布说明待补充。`n---`n"
}

$fullContent = "$changelog`n`n$downloadSummary`n`n$md5Summary"
Set-Content -Path $releaseNotePath -Value $fullContent -Encoding utf8

Write-Host "Release Note generated"
