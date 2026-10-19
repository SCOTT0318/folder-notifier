function Assert-ReleaseSignature {
    param(
        [Parameter(Mandatory)][string]$Path,
        [Parameter(Mandatory)][string]$ExpectedPublisher
    )
    $signature = Get-AuthenticodeSignature -LiteralPath $Path
    if ($signature.Status -ne 'Valid') {
        throw "Invalid release signature ($($signature.Status)): $Path"
    }
    if ($signature.SignerCertificate.Subject -cne $ExpectedPublisher) {
        throw "Unexpected signing publisher: $Path"
    }
    if ($signature.SignerCertificate.Subject -eq $signature.SignerCertificate.Issuer) {
        throw "Self-signed certificates are not accepted for public releases: $Path"
    }
    if (-not $signature.TimeStamperCertificate) {
        throw "A trusted timestamp is required: $Path"
    }
}

function Invoke-ReleaseSigning {
    param(
        [Parameter(Mandatory)][string]$Path,
        [Parameter(Mandatory)][string]$SigningScript,
        [Parameter(Mandatory)][string]$ExpectedPublisher
    )
    # The provider adapter must accept -Path and throw on signing failure.
    # Keep provider credentials in its secure store, never in this repository.
    & $SigningScript -Path $Path
    Assert-ReleaseSignature -Path $Path -ExpectedPublisher $ExpectedPublisher
}
