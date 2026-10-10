# ☕ CoffeeCup

A tiny Windows tray app that keeps your PC awake, in the spirit of Caffeine.
**Double-click the mug** to stay awake, then double-click again to let the PC sleep.

![Tray icon states on dark and light taskbars](assets/tray-preview.png)

- **Empty mug:** Windows may sleep as usual.
- **Coffee pours in** (with steam) when you activate it.
- **Timed sessions drain the mug** as the time runs out. When it's empty, CoffeeCup lets the PC sleep again.

## Install

1. Open **Releases** (or the latest **Actions → Build** run) and download `CoffeeCup-Setup-x.y.z.exe`.
2. Run it. It installs just for you, so no admin prompt. You can pick "Start CoffeeCup when I sign in".
3. Look for the mug in the notification area. On Windows 11 it may be hidden under the **^** arrow at first. To keep it visible, drag it onto the taskbar, or turn it on under *Settings → Personalization → Taskbar → Other system tray icons*.

It needs Windows 10 or 11. The app is a single ~50 KB .NET Framework 4.8 executable, which Windows already includes.

Uninstall it from *Settings → Apps*. That also removes the startup entry and settings.

## Using it

| Action | What happens |
|---|---|
| Double-click the mug | Toggles staying awake, for your chosen duration |
| Hover | Shows the status and the time left |
| Right-click → **Stay awake for** | Starts a session of 15 min to 8 h, or indefinitely. The ✓ choice becomes the double-click default |
| Right-click → **Keep screen on** | On: the display stays on too. Off: only the system stays awake, so the screen can still turn off |
| Right-click → **Start with Windows** | Launches CoffeeCup when you sign in |
| Right-click → **Stay awake when CoffeeCup starts** | Activates straight away at launch |

Settings live in `HKCU\Software\CoffeeCup`.

**How it works:** CoffeeCup calls
[`SetThreadExecutionState`](https://learn.microsoft.com/windows/win32/api/winbase/nf-winbase-setthreadexecutionstate),
the same API PowerToys Awake uses. It doesn't fake key presses. Closing the lid, pressing the power button, or choosing Sleep yourself still puts the PC to sleep. Some organisations enforce screen-lock policies that this doesn't override.

## Building

GitHub Actions (`.github/workflows/build.yml`) builds the app and the Inno Setup installer on every push. Pushing a tag such as `v1.0.1` also publishes a GitHub Release with the installer attached.

To build locally on Windows:

```powershell
dotnet build src/CoffeeCup/CoffeeCup.csproj -c Release
& "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe" /DAppVersion=1.0.0 installer\CoffeeCup.iss   # → dist\
```

The tray icon is drawn at runtime by `src/CoffeeCup/MugRenderer.cs`, so it stays crisp at any DPI and follows the light or dark taskbar.
`tools/make_icon.py` (Pillow) draws the same design and regenerates `assets/CoffeeCup.ico` (the .exe and installer icon) and the preview above.
