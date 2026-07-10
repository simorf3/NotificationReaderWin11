# Notification Reader

A lightweight, **system‑tray‑only** Windows 11 application that reads your incoming
Windows notifications aloud using high‑quality text‑to‑speech (Microsoft's modern
**Natural / Neural** voices when available).

It listens to the system notification stream via the
`UserNotificationListener` API, extracts the app name and text, optionally filters
notifications with your own **regex rules**, and speaks them through the
`Windows.Media.SpeechSynthesis` engine — one at a time, in order, never overlapping.

---

## 1. Prerequisites

- **Windows 11** (build 22000 or newer). The `userNotificationListener` capability
  and the neural voices require Windows 10 2004+ / Windows 11.
- **.NET 8 SDK** — <https://dotnet.microsoft.com/download/dotnet/8.0>
- **Visual Studio 2022** (17.8+) **or** the **Build Tools for Visual Studio 2022**,
  with these workloads/components:
  - *.NET desktop development*
  - *Universal Windows Platform development* (provides the **MSIX / Windows
    Application Packaging** targets used by the `.wapproj`)
  - *Windows 11 SDK (10.0.22621 or later)*
- (Recommended) One or more **Natural voices** installed:
  `Settings > Time & language > Speech > Manage voices > Add voices` — pick voices
  labelled *"Natural"* (e.g. *Microsoft Aria (Natural)*, *Microsoft Jenny (Natural)*).

---

## 2. NuGet packages

| Package | Why |
|---|---|
| `System.Text.Json` (8.0.5) | Serializes/deserializes `settings.json` (filter rules, selected voice, rate). |

Everything else is provided by the framework:

- **WPF** (`UseWPF`) — settings & rule‑editor windows.
- **Windows Forms** (`UseWindowsForms`) — `NotifyIcon` tray icon + context menu.
- **WinRT projections** — enabled automatically by targeting
  `net8.0-windows10.0.22000.0`, giving access to `Windows.UI.Notifications.Management`,
  `Windows.Media.SpeechSynthesis`, and `Windows.Data.Xml.Dom` **without** extra NuGet
  packages.

> **Why MSIX?** `UserNotificationListener` only works for apps that have a
> **package identity**. That is why the app ships as an MSIX rather than a bare
> `.exe`. Running the raw executable will fail to obtain notification access.

---

## 3. How to build

### Option A — one‑shot PowerShell script (recommended)

```powershell
# From the repository root:
.\build.ps1                 # build + create self-signed cert + produce setup.exe installer
```

The script will:

1. Find `msbuild.exe` (via `vswhere` or `PATH`).
2. Create/reuse a self‑signed code‑signing certificate `CN=NotificationReader`.
3. Export it to `NotificationReader.pfx` (and a public `NotificationReader.cer`).
4. Build the packaging project and produce an `.msix` under `packaging\AppPackages\`.
5. **Compile the Inno Setup installer** to produce `NotificationReader_Setup.exe` in `installer_output\`.

**Note:** To build the setup.exe installer, you need **Inno Setup** installed from https://jrsoftware.org/isinfo.php. If Inno Setup is not found, the script will still produce the MSIX package with manual installation instructions.

### Option B — Visual Studio

1. Open `NotificationReader.sln`.
2. Set the solution platform to **x64**.
3. Right‑click **NotificationReader.Package** → **Set as Startup Project**.
4. Right‑click the package project → **Publish → Create App Packages…** →
   *Sideloading* → create/select a signing certificate → **Create**.
5. Run `build.ps1` to compile the Inno Setup installer, or follow manual install steps below.

---

## 4. How to install

### Easy way: Use the installer (recommended)

Simply **double-click `NotificationReader_Setup.exe`** and follow the wizard. The installer will:

1. ✅ Automatically install the security certificate
2. ✅ Install the application
3. ✅ Create Start Menu shortcuts
4. ✅ Prompt you to grant notification access

**That's it!** No manual certificate steps, no PowerShell required.

### Manual way: Install MSIX directly (if you didn't build the installer)

1. **Enable Developer Mode / sideloading**
   `Settings > System > For developers > Developer Mode = On`.
2. **Trust the certificate** (one time). In an **elevated** PowerShell:
   ```powershell
   Import-Certificate -FilePath .\NotificationReader.cer -CertStoreLocation Cert:\LocalMachine\TrustedPeople
   ```
3. **Install the package** — double‑click the `.msix`, or:
   ```powershell
   Add-AppxPackage -Path .\packaging\AppPackages\<...>\NotificationReader.Package_1.0.0.0_x64.msix
   ```
4. Launch **Notification Reader** from the Start menu. It appears in the system tray.

To uninstall: Use Windows Settings, or run the installer again and choose "Uninstall".

---

## 5. Granting notification access

The first time it runs, the app calls `RequestAccessAsync()`. Approve the prompt, or
enable access manually:

- `Settings > Privacy & security > Notifications` → allow apps to access
  notifications (and specifically allow **Notification Reader**).
- Make sure notifications themselves are on:
  `Settings > System > Notifications`.

If access is **Denied**, the app shows a dialog with these steps and simply won't
speak until it's granted. Toggle it on and restart the app.

---

## 6. How to use the app

Right‑click the tray icon for the menu:

```
[✓] Reading Notifications      ← master on/off toggle
────────────────────────
    Voice ▶                    ← pick an installed natural voice (radio ✓)
    Speech Rate ▶
        Slow
        [✓] Normal
        Fast
────────────────────────
    Filters & Settings...      ← opens the rule editor
────────────────────────
    Exit
```

- **Double‑click** the tray icon to open **Filters & Settings**.
- Voice and rate changes are saved immediately.
- The chosen voice, rate, on/off state, and all rules persist across restarts in
  `%LOCALAPPDATA%\NotificationReader\settings.json`.

---

## 7. How regex filters work

Each rule has: **Name**, **Pattern** (regex, case‑insensitive), **Target**
(`AppName` / `Body` / `Both`), **Action** (`Exclude` / `Include`), and **Enabled**.

Decision logic (in `FilterService.ShouldSpeak`):

1. **Exclude rules always win.** If any enabled Exclude rule matches, the
   notification is **not** spoken.
2. If there are **no** Include rules, everything else **is** spoken (allow by default).
3. If there **are** Include rules, a notification is spoken **only** if it matches at
   least one Include rule (allow‑list mode) — and still isn't Excluded.

### Examples

| Goal | Target | Action | Pattern |
|---|---|---|---|
| Never read anything from Outlook | `AppName` | Exclude | `Outlook` |
| Skip "You have a new like" spam | `Body` | Exclude | `new like|liked your` |
| Only read WhatsApp & Teams | `AppName` | Include | `WhatsApp|Teams` |
| Only read messages mentioning "URGENT" | `Both` | Include | `\bURGENT\b` |
| Mute one‑time passwords | `Body` | Exclude | `\b\d{6}\b.*code|verification code` |

Use **Test Pattern…** (in the settings window) or the **Test** button (in the rule
dialog) to check a pattern against sample text before saving.

---

## 8. Architecture overview

```
App.xaml.cs                     Composition root. Single-instance mutex,
                                OnExplicitShutdown, service lifecycle.

Models/
  AppSettings.cs                Serialized settings (enabled, voice, rate, rules).
  FilterRule.cs                 One regex rule (+ enums FilterTarget/FilterAction).

Services/
  Logger.cs                     File logger -> %LOCALAPPDATA%\...\error.log.
  SettingsService.cs            Load/Save settings.json (validates regexes).
  FilterService.cs              Compiles rules; ShouldSpeak(appName, body).
  SpeechService.cs              WinRT SpeechSynthesizer + queue worker.
                                Natural-voice filtering, rate, SoundPlayer playback.
  NotificationService.cs        UserNotificationListener; parses toast XML text;
                                filters, then enqueues speech.

UI/
  TrayIconManager.cs            NotifyIcon + ContextMenuStrip (toggle, voice,
                                rate, settings, exit); startup balloon.

Windows/
  SettingsWindow.xaml(.cs)      DataGrid of rules; Add/Edit/Delete/Test/Save/Cancel.
  FilterRuleDialog.xaml(.cs)    Add/Edit a single rule; validates the regex.
  TextInputDialog.cs            Tiny prompt used by "Test Pattern...".

packaging/
  Package.appxmanifest          MSIX manifest w/ userNotificationListener + runFullTrust.
  NotificationReader.Package.wapproj   Windows App Packaging project.
  Images/                       Store/tile logos.

build.ps1                       Cert + MSIX build/install automation.
```

**Speech pipeline:** `NotificationChanged (Added)` → extract app + text →
`FilterService.ShouldSpeak` → `SpeechService.Enqueue` → background worker
`SynthesizeTextToStreamAsync` → copy WinRT stream to `MemoryStream` →
`System.Media.SoundPlayer.PlaySync()` (serialized, never overlapping).

---

## 9. Troubleshooting

**No voices in the Voice menu / robotic voice**
Install natural voices: `Settings > Time & language > Speech > Manage voices > Add
voices` (choose *Natural* voices). If none are installed, the app falls back to all
installed SAPI voices. Restart the app after installing voices.

**Nothing is spoken**
- Check the tray menu — is **Reading Notifications** checked?
- Verify notification access (section 5). Denied access = no events.
- Make sure the sending app actually raises Windows toast notifications (some apps
  have their own in‑app notifications that never hit the system stream).
- Check your Exclude/Include rules — an Include rule turns on allow‑list mode.

**"Notification access required" dialog keeps appearing**
Access is Denied/Unspecified. Enable it in
`Settings > Privacy & security > Notifications`, then relaunch.

**App won't install (certificate error)**
The signing cert must be trusted. Import `NotificationReader.cer` into
`Cert:\LocalMachine\TrustedPeople` (elevated), and enable Developer Mode. See
section 4.

**`UserNotificationListener` throws / access never granted when run as a bare EXE**
This API requires **package identity**. Run the installed **MSIX**, not the raw
`bin\...\NotificationReader.exe`.

**Where are logs & settings?**
`%LOCALAPPDATA%\NotificationReader\settings.json` and `error.log`.

---

## License

Provided as‑is, for personal use. Adapt freely.
