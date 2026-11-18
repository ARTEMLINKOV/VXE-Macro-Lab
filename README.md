# VXE Macro Lab

Windows 10/11 desktop utility with independent mouse and keyboard recording modules.

## Current features

### Mouse module
- Captures physical mouse movement using Windows Raw Input while a recording session is active.
- Raw Input registration is removed when recording stops or the app closes.
- F8 starts/stops mouse recording while the app is open and the global hotkey is available.
- Stores relative X/Y movement and elapsed timestamps.
- Displays a visual trajectory preview.
- Saves and loads mouse-only profiles.

### Keyboard module
- Records key-down and key-up events, including elapsed timestamps.
- Keyboard recording is deliberately limited to the focused capture area inside VXE Macro Lab.
- Recording stops when the capture area loses keyboard focus.
- Saves and loads keyboard-only profiles.
- No system-wide keyboard hook or keylogger.

## GhostLayer privacy architecture

GhostLayer is the application's privacy and data-protection layer. It is not a stealth or anti-detection feature.

- Profile files are encrypted with Windows DPAPI using `DataProtectionScope.CurrentUser`; another Windows user account cannot normally decrypt them.
- New profiles use the `.vxeprofile` extension and contain encrypted data, not readable JSON.
- Older plain JSON profiles can still be imported for compatibility. Saving them again writes the encrypted format.
- Profile serialization happens in memory. The service writes only encrypted bytes to a temporary file, atomically replaces the destination, and clears managed byte buffers on a best-effort basis.
- Mouse Raw Input is registered only during an active recording session and unregistered on stop/close.
- No driver, process injection, remote thread, service, or synthetic input replay is used.
- Keyboard capture is limited to this app's focused capture surface.
- No telemetry or network functionality is implemented by the current application code.

### Important limits

A running Windows application is not invisible. Windows and security software can observe its process, memory, files, and normal system interactions. GhostLayer does not hide the process, bypass security products, or promise undetectability.

DPAPI encryption is tied to the current Windows user and device context. Back up important profiles and keep access to the Windows account that created them. Importing an old JSON file does not retroactively encrypt the original file; save it as a new `.vxeprofile` file and securely remove the old copy if appropriate.

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

- New encrypted mouse and keyboard profiles: `*.vxeprofile`
- Legacy import: `*.json`

Mouse and keyboard profile models remain independent.
