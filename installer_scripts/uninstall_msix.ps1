# Uninstall MSIX package
$ErrorActionPreference = "Stop"

try {
    Write-Host "Uninstalling Notification Reader..."
    $package = Get-AppxPackage -Name "com.notificationreader.app" -ErrorAction SilentlyContinue
    if ($package) {
        Remove-AppxPackage -Package $package.PackageFullName
        Write-Host "MSIX package uninstalled successfully."
    }
    else {
        Write-Host "Package not found, may already be uninstalled."
    }
    exit 0
}
catch {
    Write-Host "Error uninstalling MSIX package: $_"
    exit 1
}
