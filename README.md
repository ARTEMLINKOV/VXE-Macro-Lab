# VXE Macro Lab

Windows 11 desktop utility with two independent input-recording modules: mouse and keyboard.

## Current features

### Mouse module
- Captures physical mouse movement using Windows Raw Input, including while another window is active.
- F8 starts/stops mouse recording when the global hotkey is available.
- Stores relative X/Y movement and elapsed timestamps.
- Displays a visual trajectory preview.
- Saves and loads mouse-only JSON profiles.

### Keyboard module
- Records key-down and key-up events, including elapsed timestamps.
- Keyboard recording is deliberately limited to the focused capture area inside VXE Macro Lab. Click **Start recording** and type in that capture area.
- Saves and loads keyboard-only JSON profiles.
- Keyboard profiles are separate from mouse profiles.

## Safety and scope

This application records and visualizes input events. It does not replay or inject synthetic mouse or keyboard input into other applications. Keyboard capture is not a system-wide keystroke logger: it only records input directed to the focused capture area in this app.

## Requirements

- Windows 10/11
- .NET 8 SDK

## Build

Run these commands from the repository root:

```powershell
dotnet restore src/VxeMacroLab/VxeMacroLab.csproj
dotnet build src/VxeMacroLab/VxeMacroLab.csproj -c Release
dotnet run --project src/VxeMacroLab/VxeMacroLab.csproj
```

## Profile formats

- Mouse: `*.mouse.json`
- Keyboard: `*.keyboard.json`

The two profile formats are intentionally independent and cannot overwrite each other's event data.
