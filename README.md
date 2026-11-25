# VXE Macro Lab

Windows 10/11 desktop utility with independent mouse and keyboard modules.

## Global Snap-Tap

While VXE Macro Lab is running, releasing **A** produces a short **D** tap; releasing **D** produces **A**. Releasing **W/S** similarly produces **S/W**. The rule is active on startup and can be paused/resumed with **F9**. The generated tap lasts 90 ms. Close the app to remove the keyboard hook and stop the feature.

The global feature handles only those four key-up events and F9 for its toggle. It does not record, save, or transmit typed text. It uses a Windows low-level keyboard hook to recognize the selected key releases and the documented `SendInput` API to emit the brief opposite-key tap; this is synthetic input, not process injection. The original physical key events are not suppressed. If the opposite physical key is already held, that pulse is skipped.

## Existing modules

- Mouse module: captures movement with Windows Raw Input while recording; F8 starts/stops recording while the app is open. Mouse profiles can be saved and loaded.
- Keyboard module: the existing recorder captures key events only while its in-app capture area has focus. It is not a global recorder.
- Snap-Tap test tab: retains its visual in-app demonstration and timing controls; the global rule currently uses a fixed 90 ms pulse.

## Privacy and profile protection

- Profiles are protected with Windows DPAPI for the current Windows user.
- Legacy JSON profiles can be imported; new profiles use `.vxeprofile`.
- No driver, process injection, remote thread, service, telemetry, or network feature is used.

## Requirements and build

- Windows 10/11
- .NET 8 SDK

```powershell
dotnet restore src/VxeMacroLab/VxeMacroLab.csproj
dotnet build src/VxeMacroLab/VxeMacroLab.csproj -c Release
dotnet run --project src/VxeMacroLab/VxeMacroLab.csproj
```
