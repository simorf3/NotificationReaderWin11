; Inno Setup Script for Notification Reader
; Creates a setup.exe that installs the MSIX package with certificate handling

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
; Include the MSIX package (adjust path if build output differs)
Source: "packaging\AppPackages\*\*.msix"; DestDir: "{tmp}"; Flags: ignoreversion recursesubdirs
; Include the certificate
Source: "NotificationReader.cer"; DestDir: "{tmp}"; Flags: ignoreversion
; Include helper PowerShell scripts
Source: "installer_scripts\install_cert.ps1"; DestDir: "{tmp}"; Flags: ignoreversion
Source: "installer_scripts\install_msix.ps1"; DestDir: "{tmp}"; Flags: ignoreversion
Source: "installer_scripts\uninstall_msix.ps1"; DestDir: "{tmp}"; Flags: ignoreversion

[Icons]
Name: "{autoprograms}\{#MyAppName}"; Filename: "shell:AppsFolder\com.notificationreader.app_8wekyb3d8bbwe!App"; WorkingDir: "{app}"

[Run]
; Install the certificate to TrustedPeople
Filename: "powershell.exe"; Parameters: "-ExecutionPolicy Bypass -File ""{tmp}\install_cert.ps1"" ""{tmp}\NotificationReader.cer"""; StatusMsg: "Installing security certificate..."; Flags: runhidden waituntilterminated
; Install the MSIX package (finds the .msix file in temp directory)
Filename: "powershell.exe"; Parameters: "-ExecutionPolicy Bypass -Command ""$msix = Get-ChildItem -Path '{tmp}' -Filter *.msix -Recurse | Select-Object -First 1; if ($msix) {{ Add-AppxPackage -Path $msix.FullName }} else {{ Write-Error 'MSIX not found' }}"""; StatusMsg: "Installing Notification Reader..."; Flags: runhidden waituntilterminated
; Show completion message
Filename: "{cmd}"; Parameters: "/c echo Installation complete! Notification Reader will appear in your system tray. && pause"; StatusMsg: "Installation complete"; Flags: postinstall skipifsilent nowait

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
begin
  if CurStep = ssPostInstall then
  begin
    // Grant notification access instructions
    MsgBox('Installation complete!' + #13#10 + #13#10 + 
           'Important: You need to grant notification access:' + #13#10 +
           '1. Go to Settings > Privacy & security > Notifications' + #13#10 +
           '2. Enable "Let apps access your notifications"' + #13#10 +
           '3. Make sure "Notification Reader" is enabled' + #13#10 + #13#10 +
           'Then launch Notification Reader from the Start Menu.', 
           mbInformation, MB_OK);
  end;
end;
