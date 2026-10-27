# Keyboard Layout Switcher

[![Build](https://github.com/KobeBrandt/Keyboard-Layout-Switcher/actions/workflows/build.yml/badge.svg)](https://github.com/KobeBrandt/Keyboard-Layout-Switcher/actions/workflows/build.yml)

Windows tray app that picks the keyboard layout based on which keyboards are connected.

## Features

- Assign a layout to a keyboard by pressing a key on it (Settings > Add keyboard…).
- When an assigned keyboard is connected, its layout becomes active. When several are
  connected, the one highest in the list wins.
- When none is connected, the default layout is used.
- The layout is applied again after unlocking the PC and after resuming from sleep.
- Optional start with Windows and switch notifications.

## Install

1. Install the [.NET 10 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/10.0) (x64).
   The exe is small because it uses this shared runtime; without it, Windows asks you to install it on first start.
2. Download `KeyboardLayoutSwitcher.exe` from the [latest release](https://github.com/KobeBrandt/Keyboard-Layout-Switcher/releases/latest).
3. Run it. The app lives in the notification area; open Settings from its tray icon.

Settings are stored in `%APPDATA%\KeyboardLayoutSwitcher\config.json`. If that file cannot be read,
it is kept as `config.bad.json` and the app starts with empty settings.

If the layout does not switch when you expect it to, turn on "Write a diagnostic log" in Settings.
The log (`log.txt` in the same folder) records each device change and which layout was applied and why.

## Build from source

Requires the .NET 10 SDK.

```
dotnet publish -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -o publish
```

The executable is written to `publish\KeyboardLayoutSwitcher.exe`.

Run the unit tests with:

```
dotnet test tests/KeyboardLayoutSwitcher.Tests
```

The app icon (`app.ico`) is drawn by `tools/make-icon.ps1`. Run `pwsh tools/make-icon.ps1` from the
repository root to draw it again after changing the script.

## Notes

- All layouts you assign must be installed in Windows (Settings > Time & language > Language & region).
- Keep "Let me use a different input method for each app window" turned off
  (Settings > Time & language > Typing > Advanced keyboard settings), so a switch applies to all windows.
- Windows cannot switch the layout of an app running as administrator from a normal app.
  When such a window has focus, the switch is applied as soon as you move to another window.
- Keyboards are recognized by model (vendor and product ID), not by individual device. Two keyboards
  of the same model count as one keyboard, so they cannot get different layouts.
- When several installed layouts share a country code, the tray icon numbers them, e.g. "US" and "US2".

## License

[MIT](LICENSE)
