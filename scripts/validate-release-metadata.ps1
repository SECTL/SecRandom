# Release channel metadata gate.
#
# Update clients resolve a channel to a release tag from metadata.yaml and then reject the
# release manifest when its tag/channel do not match (the desktop UpdateCenterService and the
# mobile MobileUpdateService), so a stale channel entry publishes an update nobody can reach.
# The release tag must therefore already carry a metadata.yaml advertising itself. Run this
# before the build jobs so a mismatch fails in seconds instead of after a full build.
[CmdletBinding()]
param(
    [string]$Tag = $env:RELEASE_TAG,
    [string]$MetadataPath = 'metadata.yaml'
)

if ([string]::IsNullOrWhiteSpace($Tag)) {
    throw 'A release tag is required. Set the RELEASE_TAG environment variable or pass -Tag.'
}

$channel = if ($Tag -match '-alpha(?:\.|$)') { 'alpha' }
    elseif ($Tag -match '-beta(?:\.|$)') { 'beta' }
    elseif ($Tag -match '-') { throw "Unsupported prerelease tag: $Tag" }
    else { 'release' }

if (-not (Test-Path -LiteralPath $MetadataPath -PathType Leaf)) {
    throw "Release metadata file '$MetadataPath' was not found."
}

$metadata = Get-Content -LiteralPath $MetadataPath -Raw
$channelMatch = [regex]::Match($metadata, "(?ms)^  ${channel}:\s*\r?\n\s+tag:\s*(?<tag>\S+)\s*$")
if (-not $channelMatch.Success -or $channelMatch.Groups['tag'].Value -ne $Tag) {
    throw "metadata.yaml channel '$channel' must point to release tag $Tag."
}

if (-not [string]::IsNullOrWhiteSpace($env:GITHUB_OUTPUT)) {
    Add-Content -LiteralPath $env:GITHUB_OUTPUT -Value "channel=$channel"
}

Write-Host "metadata.yaml channel '$channel' -> $Tag"
