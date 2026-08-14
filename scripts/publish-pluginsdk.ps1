<#
.SYNOPSIS
Packs SecRandom.Shared, SecRandom.Core, and SecRandom.PluginSdk into a local NuGet feed
so plugin development can use PackageReference + ExcludeAssets before publishing.

.EXAMPLE
./scripts/publish-pluginsdk.ps1
./scripts/publish-pluginsdk.ps1 -Version 3.0.0 -Output artifacts/plugin-feed
#>
[CmdletBinding()]
param(
    [string]$Version = "3.0.0",
    [string]$Output = "artifacts/plugin-feed"
)

$ErrorActionPreference = 'Stop'
$repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$outputPath = [IO.Path]::GetFullPath((Join-Path $repoRoot $Output))
New-Item -ItemType Directory -Path $outputPath -Force | Out-Null

$projects = @(
    "SecRandom.Shared/SecRandom.Shared.csproj",
    "SecRandom.Core/SecRandom.Core.csproj",
    "SecRandom.PluginSdk/SecRandom.PluginSdk.csproj"
)

foreach ($project in $projects) {
    & dotnet pack (Join-Path $repoRoot $project) `
        -c Release `
        -o $outputPath `
        "-p:PackageVersion=$Version" `
        "-p:BuildInParallel=false" `
        "-p:UseSharedCompilation=false"
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet pack failed for $project with exit code $LASTEXITCODE."
    }
}

Write-Host ""
Write-Host "Plugin SDK packages (version $Version) written to $outputPath"
Get-ChildItem $outputPath -Filter *.nupkg | Select-Object Name, Length
