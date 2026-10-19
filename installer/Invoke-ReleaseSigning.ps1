param(
    [Parameter(Mandatory)][string]$Path,
    [Parameter(Mandatory)][string]$SigningScript,
    [Parameter(Mandatory)][string]$PublisherFile
)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'ReleaseSigning.ps1')
$publisher = (Get-Content -LiteralPath $PublisherFile -Raw).Trim()
if (-not $publisher) { throw 'Expected publisher is missing.' }
Invoke-ReleaseSigning -Path $Path -SigningScript $SigningScript -ExpectedPublisher $publisher
