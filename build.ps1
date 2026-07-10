<#
.SYNOPSIS
    Builds the Notification Reader MSIX package, creates a self-signed
    certificate, and (optionally) installs everything for local sideloading.

.DESCRIPTION
    Run from an elevated PowerShell prompt for the -Install option to work.

    Steps:
      1. Locate msbuild.exe (via vswhere or PATH).
      2. Create / reuse a self-signed code-signing certificate.
      3. Export the certificate to a password-protected .pfx.
      4. Build the packaging project to produce a signed MSIX.
      5. Print sideloading instructions.
      6. Optionally import the cert to TrustedPeople and install the MSIX.

.PARAMETER Configuration
    Build configuration. Default: Release.

.PARAMETER CertPassword
    Password used to protect the exported .pfx. Default: "NotificationReader!1".

.PARAMETER Install
    If set, imports the cert to Cert:\LocalMachine\TrustedPeople and installs
    the produced MSIX with Add-AppxPackage. Requires an elevated shell.

.EXAMPLE
    .\build.ps1

.EXAMPLE
    .\build.ps1 -Install
#>
[CmdletBinding()]
param(
    [string]$Configuration = "Release",
    [string]$Platform = "x64",
    [string]$CertPassword = "NotificationReader!1",
    [switch]$Install
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $MyInvocation.MyCommand.Definition
$wapproj = Join-Path $root "packaging\NotificationReader.Package.wapproj"
$certSubject = "CN=NotificationReader"
$certFriendly = "NotificationReader"
$pfxPath = Join-Path $root "NotificationReader.pfx"

function Write-Section($text) {
    Write-Host ""
    Write-Host "==== $text ====" -ForegroundColor Cyan
}

# ---------------------------------------------------------------------------
# 1. Locate msbuild.exe
# ---------------------------------------------------------------------------
Write-Section "Locating MSBuild"

function Find-MSBuild {
    # Try PATH first.
    $cmd = Get-Command msbuild.exe -ErrorAction SilentlyContinue
    if ($cmd) { return $cmd.Source }

    # Try vswhere.
    $vswhere = Join-Path ${env:ProgramFiles(x86)} "Microsoft Visual Studio\Installer\vswhere.exe"
    if (Test-Path $vswhere) {
        $installPath = & $vswhere -latest -products * `
            -requires Microsoft.Component.MSBuild `
            -property installationPath
        if ($installPath) {
            $candidate = Join-Path $installPath "MSBuild\Current\Bin\MSBuild.exe"
            if (Test-Path $candidate) { return $candidate }
            $candidate = Join-Path $installPath "MSBuild\Current\Bin\amd64\MSBuild.exe"
            if (Test-Path $candidate) { return $candidate }
        }
    }
    return $null
}

$msbuild = Find-MSBuild
if (-not $msbuild) {
    throw "Could not find msbuild.exe. Install Visual Studio 2022 (with the .NET desktop + MSIX packaging workloads) or the Build Tools for Visual Studio 2022."
}
Write-Host "Using MSBuild: $msbuild" -ForegroundColor Green

# ---------------------------------------------------------------------------
# 2. Create / reuse the self-signed certificate
# ---------------------------------------------------------------------------
Write-Section "Preparing signing certificate"

$existing = Get-ChildItem "Cert:\CurrentUser\My" |
    Where-Object { $_.Subject -eq $certSubject } |
    Select-Object -First 1

if ($existing) {
    Write-Host "Reusing existing certificate with thumbprint $($existing.Thumbprint)." -ForegroundColor Green
    $cert = $existing
} else {
    Write-Host "Creating a new self-signed certificate..." -ForegroundColor Yellow
    $cert = New-SelfSignedCertificate `
        -Type Custom `
        -Subject $certSubject `
        -KeyUsage DigitalSignature `
        -FriendlyName $certFriendly `
        -CertStoreLocation "Cert:\CurrentUser\My" `
        -TextExtension @("2.5.29.37={text}1.3.6.1.5.5.7.3.3", "2.5.29.19={text}")
    Write-Host "Created certificate with thumbprint $($cert.Thumbprint)." -ForegroundColor Green
}

# ---------------------------------------------------------------------------
# 3. Export the certificate to .pfx
# ---------------------------------------------------------------------------
Write-Section "Exporting certificate to PFX"

$securePassword = ConvertTo-SecureString -String $CertPassword -Force -AsPlainText
Export-PfxCertificate -Cert $cert -FilePath $pfxPath -Password $securePassword | Out-Null
Write-Host "Exported PFX to: $pfxPath" -ForegroundColor Green

# Also export the public .cer so users can trust it without the private key.
$cerPath = Join-Path $root "NotificationReader.cer"
Export-Certificate -Cert $cert -FilePath $cerPath | Out-Null
Write-Host "Exported public certificate to: $cerPath" -ForegroundColor Green

# ---------------------------------------------------------------------------
# 4. Build the MSIX
# ---------------------------------------------------------------------------
Write-Section "Restoring and building MSIX package"

& $msbuild $wapproj `
    /t:Restore `
    /p:Configuration=$Configuration `
    /p:Platform=$Platform

& $msbuild $wapproj `
    /p:Configuration=$Configuration `
    /p:Platform=$Platform `
    /p:AppxBundle=Never `
    /p:UapAppxPackageBuildMode=SideloadOnly `
    /p:AppxPackageSigningEnabled=true `
    /p:PackageCertificateThumbprint=$($cert.Thumbprint) `
    /p:GenerateAppInstallerFile=false

if ($LASTEXITCODE -ne 0) {
    throw "MSBuild failed with exit code $LASTEXITCODE."
}

# ---------------------------------------------------------------------------
# 5. Locate the produced MSIX and print instructions
# ---------------------------------------------------------------------------
Write-Section "Locating output package"

$appPackages = Join-Path $root "packaging\AppPackages"
$msix = Get-ChildItem -Path $appPackages -Recurse -Include *.msix, *.msixbundle -ErrorAction SilentlyContinue |
    Sort-Object LastWriteTime -Descending |
    Select-Object -First 1

if ($msix) {
    Write-Host "MSIX package created:" -ForegroundColor Green
    Write-Host "  $($msix.FullName)"
} else {
    Write-Host "Build completed but no .msix was found under $appPackages." -ForegroundColor Yellow
}

Write-Host ""
Write-Host "----------------------------------------------------------------" -ForegroundColor DarkGray
Write-Host "SIDELOADING INSTRUCTIONS" -ForegroundColor Cyan
Write-Host "----------------------------------------------------------------" -ForegroundColor DarkGray
Write-Host @"
1. Enable Developer Mode (or sideloading):
     Settings > System > For developers > Developer Mode = On

2. Trust the signing certificate (one time). In an ELEVATED PowerShell:
     Import-Certificate -FilePath "$cerPath" -CertStoreLocation Cert:\LocalMachine\TrustedPeople

3. Install the package (double-click the .msix, or run):
     Add-AppxPackage -Path "<path-to-.msix>"

4. Grant notification access:
     Settings > Privacy & security > Notifications  (allow apps to access notifications)
     and make sure notifications are enabled in Settings > System > Notifications.
"@

# ---------------------------------------------------------------------------
# 6. Optional install
# ---------------------------------------------------------------------------
if ($Install) {
    Write-Section "Installing certificate and package"

    Write-Host "Importing certificate to LocalMachine\TrustedPeople (requires elevation)..." -ForegroundColor Yellow
    Import-Certificate -FilePath $cerPath -CertStoreLocation "Cert:\LocalMachine\TrustedPeople" | Out-Null

    if ($msix) {
        Write-Host "Installing MSIX package..." -ForegroundColor Yellow
        Add-AppxPackage -Path $msix.FullName
        Write-Host "Installed. Launch 'Notification Reader' from the Start menu." -ForegroundColor Green
    } else {
        Write-Host "No MSIX found to install." -ForegroundColor Red
    }
}

# ---------------------------------------------------------------------------
# 7. Compile Inno Setup installer (if available)
# ---------------------------------------------------------------------------
Write-Section "Building Setup.exe installer"

$innoSetup = $null
$innoSearchPaths = @(
    "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe"
    "${env:ProgramFiles}\Inno Setup 6\ISCC.exe"
    "${env:ProgramFiles(x86)}\Inno Setup 5\ISCC.exe"
    "${env:ProgramFiles}\Inno Setup 5\ISCC.exe"
)

foreach ($path in $innoSearchPaths) {
    if (Test-Path $path) {
        $innoSetup = $path
        break
    }
}

if ($innoSetup) {
    Write-Host "Found Inno Setup: $innoSetup" -ForegroundColor Green
    
    $issFile = Join-Path $root "installer.iss"
    if (Test-Path $issFile) {
        Write-Host "Compiling installer..." -ForegroundColor Yellow
        & $innoSetup $issFile
        
        if ($LASTEXITCODE -eq 0) {
            $setupExe = Join-Path $root "installer_output\NotificationReader_Setup.exe"
            if (Test-Path $setupExe) {
                Write-Host ""
                Write-Host "========================================" -ForegroundColor Green
                Write-Host "SUCCESS! Setup.exe created:" -ForegroundColor Green
                Write-Host "  $setupExe" -ForegroundColor Cyan
                Write-Host "========================================" -ForegroundColor Green
                Write-Host ""
                Write-Host "To install: Simply run NotificationReader_Setup.exe" -ForegroundColor Yellow
                Write-Host "The installer will:" -ForegroundColor White
                Write-Host "  1. Install the security certificate automatically" -ForegroundColor White
                Write-Host "  2. Install the MSIX package" -ForegroundColor White
                Write-Host "  3. Create Start Menu shortcuts" -ForegroundColor White
                Write-Host "  4. Prompt you to grant notification access" -ForegroundColor White
            }
        } else {
            Write-Host "Inno Setup compilation failed." -ForegroundColor Red
        }
    } else {
        Write-Host "installer.iss not found at $issFile" -ForegroundColor Yellow
    }
} else {
    Write-Host "Inno Setup not found. Install from https://jrsoftware.org/isinfo.php to build setup.exe" -ForegroundColor Yellow
    Write-Host "The MSIX package is ready, but you'll need to manually install the certificate." -ForegroundColor Yellow
}

Write-Host ""
Write-Host "Done." -ForegroundColor Green
