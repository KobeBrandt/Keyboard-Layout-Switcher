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
2. Download `KeyboardLayoutSwitcher.exe` from the [latest release](https://github.com/KobeBrandt/Keyboard-Layout-Switcher/releases/latest).
3. Run it. The app lives in the notification area; open Settings from its tray icon.

Settings are stored in `%APPDATA%\KeyboardLayoutSwitcher\config.json`.

## Build from source

Requires the .NET 10 SDK.

```
dotnet publish -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -o publish
```

The executable is written to `publish\KeyboardLayoutSwitcher.exe`.

## Notes

- All layouts you assign must be installed in Windows (Settings > Time & language > Language & region).
- Keep "Let me use a different input method for each app window" turned off
  (Settings > Time & language > Typing > Advanced keyboard settings), so a switch applies to all windows.
- Windows cannot switch the layout of an app running as administrator from a normal app.

## License

[MIT](LICENSE)
