# Install certificate to TrustedPeople store
param(
    [string]$CertPath
)

$ErrorActionPreference = "Stop"

try {
    Write-Host "Installing certificate to TrustedPeople store..."
    Import-Certificate -FilePath $CertPath -CertStoreLocation Cert:\LocalMachine\TrustedPeople | Out-Null
    Write-Host "Certificate installed successfully."
    exit 0
}
catch {
    Write-Host "Error installing certificate: $_"
    exit 1
}
