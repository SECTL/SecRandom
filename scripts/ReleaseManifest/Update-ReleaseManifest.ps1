<#
.SYNOPSIS
Adds one artifact to an already signed release manifest and signs it again.

.DESCRIPTION
`build_publish.yml` generates and signs `SecRandom-update-manifest.json` while it creates the
release. iOS is built afterwards by `build_ios.yml`, so its IPA is missing from that signed manifest
until this script folds it in. The script keeps the release identity (`channel`, `tag`, `version`,
`publishedAt`) untouched and only refreshes the `artifacts` list, which is what an update client
reads after it verified `SecRandom-update-manifest.sig`.

It refuses to rewrite a manifest it cannot authenticate first:

1. rebuilds the Ed25519 public key from `UPDATE_MANIFEST_PRIVATE_KEY_PEM_BASE64` and requires it to
   match the key compiled into the shipped apps, exactly like the publish job does, so a release can
   never be re-signed with the wrong key;
2. verifies the signature that is already attached to the manifest, so a truncated or foreign
   manifest/signature pair is never re-signed;
3. upserts the artifact entry (`id` = `<os>-<arch>-<kind>-<runtimeKind>`) and writes the same compact
   JSON shape the publish job produces, so both paths converge on identical manifests;
4. signs the refreshed manifest with the same `openssl pkeyutl -sign -rawin` call the publish job
   uses.

.PARAMETER ManifestPath
Existing `SecRandom-update-manifest.json`. It is rewritten in place.

.PARAMETER SignaturePath
Existing `SecRandom-update-manifest.sig` for `-ManifestPath`. It is rewritten in place.

.PARAMETER ArtifactPath
Local copy of the artifact to describe. `byteLength` and `sha512` come from this file.

.PARAMETER Os
Manifest `os` value of the artifact (`ios` for the unsigned IPA).

.PARAMETER Arch
Manifest `arch` value of the artifact.

.PARAMETER Kind
Manifest `kind` value of the artifact (`ios-ipa` for the unsigned IPA).

.PARAMETER RuntimeKind
Manifest `runtimeKind` value of the artifact.

.PARAMETER AssetName
Release asset name; defaults to the file name of `-ArtifactPath`.

.PARAMETER ExpectedTag
When supplied, the manifest must belong to exactly this release tag.

.PARAMETER PublicKeyPath
Key compiled into the shipped apps; defaults to `SecRandom/Assets/Updates/release-public-key.txt`.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$ManifestPath,
    [Parameter(Mandatory = $true)][string]$SignaturePath,
    [Parameter(Mandatory = $true)][string]$ArtifactPath,
    [Parameter(Mandatory = $true)][string]$Os,
    [Parameter(Mandatory = $true)][string]$Arch,
    [Parameter(Mandatory = $true)][string]$Kind,
    [Parameter(Mandatory = $true)][string]$RuntimeKind,
    [string]$AssetName = '',
    [string]$ExpectedTag = '',
    [string]$PublicKeyPath = (Join-Path $PSScriptRoot '../../SecRandom/Assets/Updates/release-public-key.txt')
)

$ErrorActionPreference = 'Stop'

foreach ($requiredPath in @($ManifestPath, $SignaturePath, $ArtifactPath, $PublicKeyPath)) {
    if (-not (Test-Path -LiteralPath $requiredPath -PathType Leaf)) {
        throw "Required file not found: $requiredPath"
    }
}

$privateKeyBase64 = $env:UPDATE_MANIFEST_PRIVATE_KEY_PEM_BASE64
if ([string]::IsNullOrWhiteSpace($privateKeyBase64)) {
    throw 'UPDATE_MANIFEST_PRIVATE_KEY_PEM_BASE64 is required to re-sign the release manifest.'
}

if ([string]::IsNullOrWhiteSpace($AssetName)) {
    $AssetName = Split-Path -Leaf $ArtifactPath
}

$artifactId = "$Os-$Arch-$Kind-$RuntimeKind"
$tempRoot = Join-Path ([IO.Path]::GetTempPath()) ("secrandom-update-manifest-" + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $tempRoot -Force | Out-Null
$privateKeyPath = Join-Path $tempRoot 'update-ed25519.pem'
$publicDerPath = Join-Path $tempRoot 'update-ed25519-public.der'
$publicPemPath = Join-Path $tempRoot 'update-ed25519-public.pem'

try {
    [IO.File]::WriteAllBytes($privateKeyPath, [Convert]::FromBase64String(($privateKeyBase64 -replace '[^A-Za-z0-9+/=]', '')))

    & openssl pkey -in $privateKeyPath -pubout -outform DER -out $publicDerPath
    if ($LASTEXITCODE -ne 0) { throw "OpenSSL Ed25519 public-key export failed with exit code $LASTEXITCODE." }
    $publicDer = [IO.File]::ReadAllBytes($publicDerPath)
    if ($publicDer.Length -lt 32) { throw 'The update-signing public key is invalid.' }
    $derivedPublicKey = [Convert]::ToBase64String($publicDer[($publicDer.Length - 32)..($publicDer.Length - 1)])
    $embeddedPublicKey = (Get-Content -LiteralPath $PublicKeyPath -Raw).Trim()
    if ($derivedPublicKey -cne $embeddedPublicKey) {
        throw "UPDATE_MANIFEST_PRIVATE_KEY_PEM_BASE64 does not match $PublicKeyPath."
    }

    & openssl pkey -in $privateKeyPath -pubout -out $publicPemPath
    if ($LASTEXITCODE -ne 0) { throw "OpenSSL Ed25519 public-key export failed with exit code $LASTEXITCODE." }
    & openssl pkeyutl -verify -pubin -inkey $publicPemPath -rawin -in $ManifestPath -sigfile $SignaturePath
    if ($LASTEXITCODE -ne 0) { throw "The existing signature does not verify against $PublicKeyPath." }

    $manifest = Get-Content -LiteralPath $ManifestPath -Raw | ConvertFrom-Json
    if ($manifest.schemaVersion -ne 1) { throw "Unsupported manifest schemaVersion: $($manifest.schemaVersion)" }
    if ($manifest.product -ne 'SecRandom') { throw "Unexpected manifest product: $($manifest.product)" }
    if (-not [string]::IsNullOrWhiteSpace($ExpectedTag) -and $manifest.tag -cne $ExpectedTag) {
        throw "The release manifest belongs to tag '$($manifest.tag)', but '$ExpectedTag' was expected."
    }

    # Replace by identity and by asset name: a re-run of the iOS build must refresh its own entry
    # instead of adding a second one.
    $artifacts = @(@($manifest.artifacts) | Where-Object { $_ -and $_.id -and $_.id -ne $artifactId -and $_.assetName -cne $AssetName })
    $artifacts += [pscustomobject][ordered]@{
        id          = $artifactId
        os          = $Os
        arch        = $Arch
        kind        = $Kind
        runtimeKind = $RuntimeKind
        assetName   = $AssetName
        byteLength  = (Get-Item -LiteralPath $ArtifactPath).Length
        sha512      = (Get-FileHash -LiteralPath $ArtifactPath -Algorithm SHA512).Hash
    }

    $ordered = foreach ($artifact in ($artifacts | Sort-Object -Property id)) {
        [ordered]@{
            id          = $artifact.id
            os          = $artifact.os
            arch        = $artifact.arch
            kind        = $artifact.kind
            runtimeKind = $artifact.runtimeKind
            assetName   = $artifact.assetName
            byteLength  = $artifact.byteLength
            sha512      = $artifact.sha512
        }
    }

    $updated = [ordered]@{
        schemaVersion = $manifest.schemaVersion
        product       = $manifest.product
        channel       = $manifest.channel
        tag           = $manifest.tag
        version       = $manifest.version
        publishedAt   = $manifest.publishedAt
        artifacts     = @($ordered)
    }

    # utf8NoBOM keeps the signed bytes identical to the manifest the publish job produced.
    [IO.File]::WriteAllText($ManifestPath, ($updated | ConvertTo-Json -Depth 5 -Compress), [Text.UTF8Encoding]::new($false))

    & openssl pkeyutl -sign -rawin -inkey $privateKeyPath -in $ManifestPath -out $SignaturePath
    if ($LASTEXITCODE -ne 0) { throw "OpenSSL Ed25519 signing failed with exit code $LASTEXITCODE." }

    Write-Host "Manifest '$ManifestPath' now lists $($ordered.Count) artifact(s) including '$AssetName' ($artifactId)."
}
finally {
    Remove-Item -LiteralPath $tempRoot -Recurse -Force -ErrorAction SilentlyContinue
}
