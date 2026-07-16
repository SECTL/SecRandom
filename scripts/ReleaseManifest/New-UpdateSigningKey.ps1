[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$OutputDirectory
)

$ErrorActionPreference = 'Stop'
$OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)
New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null

$privateKeyPath = Join-Path $OutputDirectory 'secrandom-update-private.pem'
$publicKeyPath = Join-Path $OutputDirectory 'secrandom-update-public.der'

& openssl genpkey -algorithm ED25519 -out $privateKeyPath
if ($LASTEXITCODE -ne 0) { throw "OpenSSL failed to generate the Ed25519 private key: $LASTEXITCODE." }

& openssl pkey -in $privateKeyPath -pubout -outform DER -out $publicKeyPath
if ($LASTEXITCODE -ne 0) { throw "OpenSSL failed to export the Ed25519 public key: $LASTEXITCODE." }

$publicDer = [IO.File]::ReadAllBytes($publicKeyPath)
if ($publicDer.Length -lt 32) { throw 'The generated Ed25519 public key is invalid.' }

$publicKey = [Convert]::ToBase64String($publicDer[($publicDer.Length - 32)..($publicDer.Length - 1)])
$privateKeySecret = [Convert]::ToBase64String([IO.File]::ReadAllBytes($privateKeyPath))
Set-Content -LiteralPath (Join-Path $OutputDirectory 'release-public-key.txt') -Value $publicKey -NoNewline -Encoding utf8
Set-Content -LiteralPath (Join-Path $OutputDirectory 'github-actions-private-key.txt') -Value $privateKeySecret -NoNewline -Encoding utf8

Write-Host 'Generated update signing material.'
Write-Host 'Copy release-public-key.txt into SecRandom/Assets/Updates/release-public-key.txt.'
Write-Host 'Set github-actions-private-key.txt as the UPDATE_MANIFEST_PRIVATE_KEY_PEM_BASE64 GitHub Actions secret.'
Write-Host 'Do not commit the private PEM or GitHub Actions secret file.'
