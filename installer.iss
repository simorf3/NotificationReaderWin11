; Inno Setup Script for Notification Reader
; Creates a setup.exe that installs the MSIX package with certificate handling
; Changelog: removed create_shortcut.ps1 step (broken shell: .url) - MSIX registers its own Start Menu tile.

#define MyAppName "Notification Reader"
#define MyAppVersion "1.0.0"
#define MyAppPublisher "NotificationReader"
#define MyAppURL "https://github.com/yourusername/notificationreader"
#define MyAppExeName "NotificationReader.exe"

[Setup]
AppId={{B8F3A9E1-5C2D-4B6A-8F1E-9D4C7A6B3E2F}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}
AppUpdatesURL={#MyAppURL}
DefaultDirName={autopf}\{#MyAppName}
DisableProgramGroupPage=yes
LicenseFile=LICENSE.txt
PrivilegesRequired=admin
OutputDir=installer_output
OutputBaseFilename=NotificationReader_Setup
SetupIconFile=src\Assets\app.ico
Compression=lzma
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64
ArchitecturesInstallIn64BitMode=x64
MinVersion=10.0.22000

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Files]
; Include the MSIX package. The CI "Stage MSIX for installer" step copies the
; built package here to a stable, predictable location regardless of the exact
; AppPackages sub-folder layout produced by MSBuild.
Source: "dist\*.msix"; DestDir: "{tmp}"; Flags: ignoreversion recursesubdirs
; Include the certificate
Source: "NotificationReader.cer"; DestDir: "{tmp}"; Flags: ignoreversion
Source: "installer_scripts\install_cert.ps1"; DestDir: "{tmp}"; Flags: ignoreversion
Source: "installer_scripts\install_msix.ps1"; DestDir: "{tmp}"; Flags: ignoreversion
Source: "installer_scripts\uninstall_msix.ps1"; DestDir: "{tmp}"; Flags: ignoreversion
Source: "installer_scripts\launch_app.ps1"; DestDir: "{tmp}"; Flags: ignoreversion

; Note: A custom Start Menu shortcut is NOT created by this installer. The MSIX
; package automatically registers its own Start Menu tile from the manifest
; <uap:VisualElements>, which launches the packaged app correctly. A custom
; shortcut step was removed because it could produce a broken "shell:" .url.

[Run]
; Install the certificate to TrustedPeople
Filename: "powershell.exe"; Parameters: "-ExecutionPolicy Bypass -File ""{tmp}\install_cert.ps1"" ""{tmp}\NotificationReader.cer"""; StatusMsg: "Installing security certificate..."; Flags: runhidden waituntilterminated
; Install the MSIX package via the robust helper script (it locates the .msix
; in {tmp} itself, removes any previous version, then installs).
Filename: "powershell.exe"; Parameters: "-ExecutionPolicy Bypass -File ""{tmp}\install_msix.ps1"""; StatusMsg: "Installing Notification Reader..."; Flags: runhidden waituntilterminated
; Launch the app now (optional, ticked by default)
Filename: "powershell.exe"; Parameters: "-ExecutionPolicy Bypass -File ""{tmp}\launch_app.ps1"""; Description: "Launch Notification Reader now"; Flags: postinstall skipifsilent nowait runhidden

[UninstallRun]
; Uninstall the MSIX package
Filename: "powershell.exe"; Parameters: "-ExecutionPolicy Bypass -File ""{tmp}\uninstall_msix.ps1"""; Flags: runhidden waituntilterminated

[Code]
function InitializeSetup(): Boolean;
var
  ResultCode: Integer;
begin
  Result := True;
  
  // Check for Windows 11
  if not (GetWindowsVersion >= $0A00) then
  begin
    MsgBox('This application requires Windows 11 (build 22000) or later.', mbError, MB_OK);
    Result := False;
    Exit;
  end;
  
  // Check if already installed
  if RegKeyExists(HKLM, 'Software\Microsoft\Windows\CurrentVersion\Uninstall\{B8F3A9E1-5C2D-4B6A-8F1E-9D4C7A6B3E2F}_is1') then
  begin
    if MsgBox('Notification Reader is already installed. Do you want to reinstall it?', mbConfirmation, MB_YESNO) = IDNO then
    begin
      Result := False;
      Exit;
    end;
  end;
end;

procedure CurStepChanged(CurStep: TSetupStep);
var
  ResultCode: Integer;
  Installed: Boolean;
begin
  if CurStep = ssPostInstall then
  begin
    // Verify the MSIX package actually registered. Exit code 0 = found.
    Installed := Exec('powershell.exe',
      '-ExecutionPolicy Bypass -Command "if (Get-AppxPackage -Name ''com.notificationreader.app'') { exit 0 } else { exit 1 }"',
      '', SW_HIDE, ewWaitUntilTerminated, ResultCode) and (ResultCode = 0);

    if Installed then
    begin
      MsgBox('Installation complete!' + #13#10 + #13#10 +
             'Important: grant notification access so the app can read them:' + #13#10 +
             '1. Go to Settings > Privacy & security > Notifications' + #13#10 +
             '2. Enable "Let apps access your notifications"' + #13#10 +
             '3. Make sure "Notification Reader" is enabled' + #13#10 + #13#10 +
             'Then launch Notification Reader from the Start Menu' + #13#10 +
             '(it lives in the system tray, next to the clock).',
             mbInformation, MB_OK);
    end
    else
    begin
      MsgBox('Setup finished, but the Notification Reader app package did NOT '
             + 'register correctly.' + #13#10 + #13#10 +
             'This is usually a certificate-trust issue. Please try:' + #13#10 +
             '1. Re-run this installer (right-click > Run as administrator), or' + #13#10 +
             '2. Restart Windows and run it again.' + #13#10 + #13#10 +
             'If it still fails, contact support with this message.',
             mbError, MB_OK);
    end;
  end;
end;
