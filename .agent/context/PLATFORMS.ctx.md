# OpenUtau Mobile - Platform Status and Build Commands

## Platform Status Overview

| Platform         | Status               | Notes                     |
|------------------|----------------------|---------------------------|
| Android          | ✅ Builds and runs    | Primary target            |
| Windows          | ✅ Builds and runs    | Desktop target            |
| Linux            | ✅ Builds and runs    | Must run on Linux machine |
| iOS              | ❌ Does not build yet | In progress               |
| MacOS (Catalyst) | ❌ Does not build yet | In progress               |
| Browser          | ⚠️ Builds but hangs  | Initialization hangs      |

## Target OS Versions

- **Android**: Intended to support Android 5+, but Android 10 and below have known bugs (untested).
- **Windows**: Windows 10+.
- **Linux**: Unknown; needs clarification.
- **iOS**: N/A (not yet building).
- **MacOS (Catalyst)**: N/A (not yet building).

## Build and Run Commands

### Android
```
dotnet build -t:Run -c Debug -p:AndroidDebugger=true
```
Run from the `OpenUtauMobile.Android` project folder.

### Windows
```
dotnet build -t:Run -c Debug
```
Run from the `OpenUtauMobile.Windows` project folder.

### Linux
```
dotnet build -t:Run -c Debug
```
Run from the `OpenUtauMobile.Linux` project folder. Must execute on a Linux machine.

### iOS
Not yet building. See known issues.

### MacOS (Catalyst)
Not yet building. See known issues.

### Browser
```
dotnet build -t:Run -c Debug
```
Run from the `OpenUtauMobile.Browser` project folder. Note: initialization hangs.

## Platform-Specific Notes

### Android
- Audio integration lives in `OpenUtauMobile.Android/Audio`.
- Storage integration lives in `OpenUtauMobile.Android/Storage`.
- Resources in `OpenUtauMobile.Android/Resources`.
- Builds target `net10.0-android36.0`; Android SDK Platform 36 is required.
- Avalonia 12 uses an `AvaloniaAndroidApplication<App>` application class and a non-generic `AvaloniaMainActivity`.
- Android uses `IActivityApplicationLifetime.MainViewFactory`; iOS and browser continue to use `ISingleViewApplicationLifetime`.
- Android Debug builds embed managed assemblies. Fast Deployment produced startup and focus-event ANRs while loading a 151 MB override directory on an Android 14 device.
- Immersive-mode clicks still fail after the first click on the Android 12 emulator. The issue does not reproduce on an Android 14 physical device and is not caused by normalizing mouse, touch, or pen input.
- Known issue: undo/redo gesture invalid; triggers non-recoverable state machine fault.

### Windows
- Audio and storage integrations in respective folders under `OpenUtauMobile.Windows`.
- Runtime native dependencies in `OpenUtauMobile.Windows/runtimes`.

### Linux
- Similar structure to Windows.
- Runtime native dependencies in `OpenUtauMobile.Linux/runtimes`.

### iOS, MacOS (Catalyst), Browser
- Do not prioritize until core features stabilize on Android/Windows/Linux.

