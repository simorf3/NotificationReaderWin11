# Installation Guide

## ✅ Quick Install (Recommended)

1. **Download** `NotificationReader_Setup.exe`
2. **Double-click** to run the installer
3. Follow the wizard (it handles certificates automatically)
4. Launch from Start Menu
5. **Grant notification access** when prompted:
   - Go to `Settings > Privacy & security > Notifications`
   - Enable "Let apps access your notifications"
   - Make sure "Notification Reader" is enabled

## 🎉 That's it!

The app will appear in your system tray. Right-click the tray icon to:
- Toggle reading on/off
- Select a voice
- Adjust speech rate
- Configure filter rules
- Exit

---

## 🛠️ Building from Source

### Prerequisites
- Windows 11 (build 22000+)
- .NET 8 SDK
- Visual Studio 2022 with:
  - .NET desktop development
  - Universal Windows Platform development
  - Windows 11 SDK (10.0.22621+)
- **Inno Setup** (https://jrsoftware.org/isinfo.php) to build the installer

### Build Steps

```powershell
# Clone or extract the source code
cd NotificationReader

# Build everything (MSIX + setup.exe)
.\build.ps1

# The installer will be created at:
# installer_output\NotificationReader_Setup.exe
```

---

## 📦 What Gets Installed

- **Application:** Packaged as MSIX for Windows 11
- **Location:** Managed by Windows (AppData)
- **Start Menu:** Notification Reader shortcut
- **System Tray:** Icon appears on launch
- **Settings:** Stored in `%LOCALAPPDATA%\NotificationReader\`
  - `settings.json` (voice, rate, filter rules)
  - `error.log` (diagnostic logs)

---

## 🗑️ Uninstall

- **Method 1:** Windows Settings → Apps → Notification Reader → Uninstall
- **Method 2:** Run the installer again and choose "Uninstall"
- **Method 3:** PowerShell: `Get-AppxPackage *notificationreader* | Remove-AppxPackage`

---

## ❓ Troubleshooting

### "Notification access required" dialog appears
→ Go to `Settings > Privacy & security > Notifications` and enable access for Notification Reader

### No voices in the Voice menu
→ Install Natural voices: `Settings > Time & language > Speech > Manage voices > Add voices`

### Nothing is being spoken
1. Check the tray menu — is "Reading Notifications" checked?
2. Verify notification access is granted
3. Check if you have Include rules that may be blocking notifications
4. Make sure the sending app uses Windows toast notifications

### Certificate error during manual install
→ Use the `NotificationReader_Setup.exe` installer instead — it handles certificates automatically

---

## 📚 More Information

See [README.md](README.md) for:
- Architecture overview
- Regex filter examples
- Advanced configuration
- Full troubleshooting guide
