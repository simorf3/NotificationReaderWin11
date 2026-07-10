# Trusts the app's signing certificate so the MSIX package can be sideloaded.
# A self-signed certificate is its own root, so it must be present in BOTH
# TrustedPeople (required for MSIX sideloading) and Root (so the signature
# chain validates). If -CertPath is omitted, the script locates the .cer next
# to itself (the installer copies it into {tmp}).

param(
    [string]$CertPath
)

$ErrorActionPreference = "Stop"

try {
    if ([string]::IsNullOrWhiteSpace($CertPath) -or -not (Test-Path $CertPath)) {
        $here = Split-Path -Parent $MyInvocation.MyCommand.Path
        $found = Get-ChildItem -Path $here -Filter *.cer -Recurse -ErrorAction SilentlyContinue |
                 Select-Object -First 1
        if (-not $found) {
            Write-Host "ERROR: No .cer certificate found near '$here'."
            exit 1
        }
        $CertPath = $found.FullName
    }

    Write-Host "Trusting certificate: $CertPath"
    Import-Certificate -FilePath $CertPath -CertStoreLocation Cert:\LocalMachine\TrustedPeople | Out-Null
    Import-Certificate -FilePath $CertPath -CertStoreLocation Cert:\LocalMachine\Root | Out-Null
    Write-Host "Certificate installed to TrustedPeople and Root."
    exit 0
}
catch {
    Write-Host "Error installing certificate: $_"
    exit 1
}
