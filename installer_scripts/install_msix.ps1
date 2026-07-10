# Installs the Notification Reader MSIX package.
# If -MsixPath is not supplied, searches the script's own folder (the installer
# copies the .msix next to this script in {tmp}) for the package.

param(
    [string]$MsixPath
)

$ErrorActionPreference = "Stop"

try {
    if ([string]::IsNullOrWhiteSpace($MsixPath) -or -not (Test-Path $MsixPath)) {
        $here = Split-Path -Parent $MyInvocation.MyCommand.Path
        $found = Get-ChildItem -Path $here -Filter *.msix -Recurse -ErrorAction SilentlyContinue |
                 Select-Object -First 1
        if (-not $found) {
            Write-Host "ERROR: No .msix package found near '$here'."
            exit 1
        }
        $MsixPath = $found.FullName
    }

    Write-Host "Installing MSIX package: $MsixPath"

    # Remove any previous install first so re-installs/upgrades are clean.
    $existing = Get-AppxPackage -Name "com.notificationreader.app" -ErrorAction SilentlyContinue
    if ($existing) {
        Write-Host "Removing previous version $($existing.Version)..."
        Remove-AppxPackage -Package $existing.PackageFullName -ErrorAction SilentlyContinue
    }

    Add-AppxPackage -Path $MsixPath -ForceUpdateFromAnyVersion
    Write-Host "MSIX package installed successfully."
    exit 0
}
catch {
    Write-Host "Error installing MSIX package: $_"
    exit 1
}
