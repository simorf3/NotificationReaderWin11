# Launches the installed Notification Reader MSIX app by its real AUMID.

$ErrorActionPreference = 'SilentlyContinue'

$pkg = Get-AppxPackage -Name "com.notificationreader.app" | Select-Object -First 1
if ($pkg) {
    $aumid = "$($pkg.PackageFamilyName)!App"
    Start-Process "explorer.exe" "shell:AppsFolder\$aumid"
}
