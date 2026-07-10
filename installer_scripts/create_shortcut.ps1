# Creates a working Start Menu shortcut for the installed MSIX app.
# The package family name contains a publisher hash that depends on the signing
# certificate, so it must be looked up at runtime rather than hardcoded.

$ErrorActionPreference = 'Stop'

try {
    $pkg = Get-AppxPackage -Name "com.notificationreader.app" | Select-Object -First 1
    if (-not $pkg) {
        Write-Error "Notification Reader package not found."
        exit 1
    }

    # Application user model ID = <PackageFamilyName>!<Application Id from manifest>
    $aumid = "$($pkg.PackageFamilyName)!App"

    $startMenu = [Environment]::GetFolderPath('CommonStartMenu')
    $lnkPath = Join-Path $startMenu 'Programs\Notification Reader.lnk'

    # A shortcut whose target is an AUMID must be created via the shell "explorer"
    # protocol. We build a .lnk that runs: explorer.exe shell:AppsFolder\<AUMID>
    $shell = New-Object -ComObject WScript.Shell
    $lnk = $shell.CreateShortcut($lnkPath)
    $lnk.TargetPath = "explorer.exe"
    $lnk.Arguments = "shell:AppsFolder\$aumid"
    $lnk.Description = "Notification Reader"
    $lnk.Save()

    Write-Host "Created shortcut for $aumid at $lnkPath"
}
catch {
    Write-Error "Failed to create shortcut: $_"
    exit 1
}
