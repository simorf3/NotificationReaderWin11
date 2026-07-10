# How to get the installer (.exe)

**Important honesty note:** This repository contains the complete **source code**
plus everything needed to produce the installer. The compiled installer itself
(`NotificationReader_Setup.exe`) is **not** included, because a Windows
WPF + WinRT + MSIX app can **only be compiled on Windows** — it cannot be built
on Linux/Mac. Below are three ways to turn this source into a real installer,
easiest first.

---

## ✅ Option 1 — Let GitHub build it for you (no Visual Studio needed)

This is the easiest path if you don't want to install anything.

1. Create a new **GitHub** repository and push this folder to it:
   ```bash
   git init                       # (already done for you)
   git add -A && git commit -m "initial"
   git branch -M main
   git remote add origin https://github.com/<you>/notificationreader.git
   git push -u origin main
   ```
2. On GitHub, open the **Actions** tab. The workflow **“Build Windows Installer”**
   runs automatically. (You can also click **Run workflow** manually.)
3. Wait ~5 minutes. Open the finished run and download the artifact
   **`NotificationReader-Setup`** — it contains **`NotificationReader_Setup.exe`**.
4. Bonus: push a tag and GitHub also creates a **Release** with the installer attached:
   ```bash
   git tag v1.0.0 && git push origin v1.0.0
   ```

A free Microsoft-hosted **Windows** runner does all the compiling. You just download the result.

---

## 🖥️ Option 2 — Build it yourself on a Windows 11 PC (one command)

Requirements (install once):
- **.NET 8 SDK** — https://dotnet.microsoft.com/download/dotnet/8.0
- **Visual Studio 2022** (or Build Tools) with the *.NET desktop* and
  *Universal Windows Platform / MSIX packaging* workloads
- **Inno Setup 6** — https://jrsoftware.org/isinfo.php

Then, from this folder in PowerShell:
```powershell
.\build.ps1
```
Your installer appears at:
```
installer_output\NotificationReader_Setup.exe
```
Double-click it to install. It handles the certificate, installs the app,
and adds a Start-Menu shortcut automatically.

---

## 🧰 Option 3 — Open in Visual Studio

1. Open `NotificationReader.sln`, set platform to **x64**.
2. Set **NotificationReader.Package** as the startup project.
3. **Publish → Create App Packages… → Sideloading** to produce the `.msix`.
4. Run `.\build.ps1` (or Inno Setup on `installer.iss`) to wrap it into `Setup.exe`.

---

## Why can't you just give me the .exe directly?

The assistant that generated this project runs on **Linux**. Building a Windows
desktop app that uses:
- **WPF / Windows Forms** (Windows-only UI frameworks),
- **WinRT APIs** (`UserNotificationListener`, `SpeechSynthesizer`), and
- **MSIX packaging** (required so the app gets the *package identity* that the
  notification-listener API demands),

requires the **Windows** build toolchain (MSBuild + Windows SDK + packaging
targets). None of that exists or runs on Linux, so no Windows binary can be
produced here. **Option 1 above is the closest thing to “just give me the exe”** —
GitHub’s Windows servers compile it for free and you download the finished file.
