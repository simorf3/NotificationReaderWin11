# Install MSIX package
param(
    [string]$MsixPath
)

$ErrorActionPreference = "Stop"

try {
    Write-Host "Installing MSIX package..."
    Add-AppxPackage -Path $MsixPath
    Write-Host "MSIX package installed successfully."
    exit 0
}
catch {
    Write-Host "Error installing MSIX package: $_"
    exit 1
}
