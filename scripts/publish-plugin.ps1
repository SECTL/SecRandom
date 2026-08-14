<#
.SYNOPSIS
    Packages a SecRandom plugin as an .srpx file, computes its SHA-256, and generates the
    plugins/<id>.yaml metadata for a SecRandom-PluginIndex contribution PR.

.DESCRIPTION
    Build the plugin first (with CreateSrpx=true), then run this script against the produced .srpx.

    Workflow for a plugin author:
      1. dotnet build -c Release            (produces srpx/<Plugin>.srpx)
      2. .\publish-plugin.ps1 -SrpxPath .\srpx\My.Plugin.srpx -RepoOwner MyGitHub -RepoName my-plugin-repo
      3. Create a GitHub release in my-plugin-repo with the .srpx asset and a release note that
         embeds the SHA-256 block (printed by this script).
      4. Fork SECTL/SecRandom-PluginIndex, add plugins/<id>.yaml, open a PR.

    The release note SHA-256 block must be:
        <!-- SECRANDOM_SHA256: <hex> -->
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$SrpxPath,

    [Parameter(Mandatory = $true)]
    [string]$RepoOwner,

    [Parameter(Mandatory = $true)]
    [string]$RepoName,

    [string]$ProjectUrl = '',

    [string]$ReadmeUrl = '',

    [string]$IconUrl = ''
)

$ErrorActionPreference = 'Stop'

$srpxFullPath = [IO.Path]::GetFullPath($SrpxPath)
if (-not [IO.File]::Exists($srpxFullPath)) {
    throw "The .srpx package was not found: $srpxFullPath"
}
if ([IO.Path]::GetExtension($srpxFullPath) -ne '.srpx') {
    throw 'The selected file is not a .srpx package.'
}

# SHA-256 of the package (used for the release-note block that the index workflow parses).
$sha256 = (Get-FileHash -LiteralPath $srpxFullPath -Algorithm SHA256).Hash.ToLowerInvariant()

# Read manifest.yml from inside the package (ZIP).
Add-Type -AssemblyName System.IO.Compression.FileSystem
$manifestText = ''
$zip = [IO.Compression.ZipFile]::OpenRead($srpxFullPath)
try {
    $entry = $zip.Entries | Where-Object { $_.FullName -eq 'manifest.yml' } | Select-Object -First 1
    if ($null -eq $entry) { throw 'The .srpx package does not contain manifest.yml at its root.' }
    $reader = New-Object IO.StreamReader($entry.Open())
    try { $manifestText = $reader.ReadToEnd() } finally { $reader.Dispose() }
}
finally {
    $zip.Dispose()
}

# Parse the minimal manifest fields we need (id/name/description/author/version/apiVersion).
$manifest = @{}
foreach ($line in $manifestText -split "`n") {
    $trimmed = $line.Trim()
    if ($trimmed -eq '' -or $trimmed.StartsWith('#') -or -not $trimmed.Contains(':')) { continue }
    $colon = $trimmed.IndexOf(':')
    $key = $trimmed.Substring(0, $colon).Trim()
    $value = $trimmed.Substring($colon + 1).Trim().Trim('"', "'")
    $manifest[$key] = $value
}

$pluginId = if ($manifest.ContainsKey('id')) { $manifest['id'] } else { [IO.Path]::GetFileNameWithoutExtension($srpxFullPath) }
$pluginName = if ($manifest.ContainsKey('name')) { $manifest['name'] } else { $pluginId }
$pluginDescription = if ($manifest.ContainsKey('description')) { $manifest['description'] } else { '' }
$pluginAuthor = if ($manifest.ContainsKey('author')) { $manifest['author'] } else { $RepoOwner }
$pluginVersion = if ($manifest.ContainsKey('version')) { $manifest['version'] } else { '0.0.0' }
$pluginApiVersion = if ($manifest.ContainsKey('apiVersion')) { $manifest['apiVersion'] } else { '3.0.0' }

# Dependency list (id + required) from manifest.yml.
$dependencies = @()
$inDeps = $false
foreach ($line in $manifestText -split "`n") {
    $trimmed = $line.Trim()
    if ($trimmed -match '^dependencies:\s*$') {
        $inDeps = $true
        continue
    }
    if ($inDeps -and $trimmed.StartsWith('- id:')) {
        $depId = $trimmed.Substring(5).Trim().Trim('"', "'")
        if ($depId) {
            $dependencies += @{ id = $depId; required = $true }
        }
    }
}

# Build the plugins/<id>.yaml content.
$shaBlock = "<!-- SECRANDOM_SHA256: $sha256 -->"
$lines = [System.Collections.Generic.List[string]]::new()
[void]$lines.Add('id: ' + $pluginId)
[void]$lines.Add('name: ' + $pluginName)
[void]$lines.Add('description: ' + $pluginDescription)
[void]$lines.Add('author: ' + $pluginAuthor)
[void]$lines.Add('version: ' + $pluginVersion)
[void]$lines.Add('apiVersion: "' + $pluginApiVersion + '"')
[void]$lines.Add('repoOwner: ' + $RepoOwner)
[void]$lines.Add('repoName: ' + $RepoName)
if ($ProjectUrl) { [void]$lines.Add('projectUrl: ' + $ProjectUrl) }
if ($ReadmeUrl) { [void]$lines.Add('readmeUrl: ' + $ReadmeUrl) }
if ($IconUrl) { [void]$lines.Add('iconUrl: ' + $IconUrl) }
if ($dependencies.Count -gt 0) {
    [void]$lines.Add('dependencies:')
    foreach ($dep in $dependencies) {
        $required = if ($dep.required) { 'true' } else { 'false' }
        [void]$lines.Add('  - id: ' + $dep.id)
        [void]$lines.Add('    required: ' + $required)
    }
}

$outputDirectory = [IO.Path]::GetDirectoryName($srpxFullPath)
$yamlPath = Join-Path $outputDirectory ($pluginId + '.yaml')
Set-Content -LiteralPath $yamlPath -Value ($lines -join "`n") -Encoding utf8

Write-Host ''
Write-Host 'Plugin metadata written to: ' $yamlPath
Write-Host ''
Write-Host '1. Create a GitHub release in ' $RepoOwner '/' $RepoName ' with tag v'$pluginVersion '.'
Write-Host '   Upload the .srpx asset and include this block in the release note:'
Write-Host '   ' $shaBlock
Write-Host '2. Fork SECTL/SecRandom-PluginIndex, add ' $yamlPath ' under plugins/, and open a PR.'
Write-Host '   The index CI rebuilds and signs index.json automatically.'
