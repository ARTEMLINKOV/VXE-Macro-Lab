# VXE Macro Lab

Windows 11 desktop utility for recording and inspecting physical mouse movement.

## Current MVP

- Captures physical mouse deltas using Windows Raw Input while the app is running, including when another window is active.
- F8 starts and stops recording (global hotkey, if Windows allows it to be registered).
- Stores relative X/Y movement and elapsed timestamps.
- Saves and loads profiles as JSON.
- Draws the captured path in the app's preview area.

This version records and visualizes trajectories only. It does not inject synthetic mouse movement into other applications.

## Requirements

- Windows 11
- .NET 8 SDK

## Build

Run these commands from the repository root:

```powershell
dotnet restore src/VxeMacroLab/VxeMacroLab.csproj
dotnet build src/VxeMacroLab/VxeMacroLab.csproj -c Release
dotnet run --project src/VxeMacroLab/VxeMacroLab.csproj
```

## Controls

- **F8** — start/stop recording.
- **Stop** — stop the current recording.
- **Save profile** — export the captured movement to JSON.
- **Open profile** — load a saved JSON profile.
- **Clear** — clear the current trajectory.

## Notes

Raw Input records relative movement reported by Windows for attached mouse devices. It does not identify a specific mouse model or modify mouse firmware.
