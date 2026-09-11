# OpenUtau Mobile - Platform Status and Build Commands

## Platform Status Overview

| Platform         | Status               | Notes                     |
|------------------|----------------------|---------------------------|
| Android          | ✅ Builds and runs    | Primary target            |
| Windows          | ✅ Builds and runs    | Desktop target            |
| Linux            | ✅ Builds and runs    | Must run on Linux machine |
| iOS              | ⚠️ Builds and runs    | Simulator smoke-tested; feature validation in progress |
| MacOS (Catalyst) | ❌ Does not build yet | In progress               |
| Browser          | ⚠️ Builds but hangs  | Initialization hangs      |

## Target OS Versions

- **Android**: Intended to support Android 5+, but Android 10 and below have known bugs (untested).
- **Windows**: Windows 10+.
- **Linux**: Unknown; needs clarification.
- **iOS**: Minimum OS 13.0; currently validated with the iOS 26.5 simulator SDK.
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
On Intel macOS with Xcode outside the default developer directory:
```
AVALONIA_TELEMETRY_OPTOUT=1 \
DEVELOPER_DIR=/Users/vocoder712/Downloads/Xcode.app/Contents/Developer \
dotnet build OpenUtauMobile.iOS/OpenUtauMobile.iOS.csproj \
  -c Debug -f net10.0-ios -p:RuntimeIdentifier=iossimulator-x64
```
The iOS workload matching the installed .NET SDK and Xcode must be installed. Set `NUGET_PACKAGES` to a persistent writable package cache when the default user cache is unavailable.

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
- Android 10 and earlier use runtime `READ_EXTERNAL_STORAGE` / `WRITE_EXTERNAL_STORAGE` permissions through the current activity; Android 10 opts into legacy external storage for the internal raw-path file picker.
- Android 11 and later use `MANAGE_EXTERNAL_STORAGE`.
- Resources in `OpenUtauMobile.Android/Resources`.
- Builds target `net10.0-android36.0`; Android SDK Platform 36 is required.
- Avalonia 12 uses an `AvaloniaAndroidApplication<App>` application class and a non-generic `AvaloniaMainActivity`.
- Android uses `IActivityApplicationLifetime.MainViewFactory`; iOS and browser continue to use `ISingleViewApplicationLifetime`.
- Android Debug builds embed managed assemblies. Fast Deployment produced startup and focus-event ANRs while loading a 151 MB override directory on an Android 14 device.
- Immersive-mode clicks still fail after the first click on the Android 12 emulator. The issue does not reproduce on an Android 14 physical device and is not caused by normalizing mouse, touch, or pen input.
- Undo/redo multi-touch gesture state-machine fix is implemented and awaits Windows/Android device validation.

### Windows
- Audio and storage integrations in respective folders under `OpenUtauMobile.Windows`.
- Runtime native dependencies in `OpenUtauMobile.Windows/runtimes`.

### Linux
- Similar structure to Windows.
- Runtime native dependencies in `OpenUtauMobile.Linux/runtimes`.

### iOS
- Writable application data, preferences, logs, projects, and imported audio use the app's Documents directory. `UIFileSharingEnabled` and `LSSupportsOpeningDocumentsInPlace` expose this directory to the iOS Files app.
- Audio output uses AVAudioEngine with a 44.1 kHz stereo float source node. iOS controls the physical output route.
- Startup creates `OrtEnv` and `SessionOptions` and logs whether the ONNX Runtime native environment loads. This is only a runtime-load probe and does not test a model or singing synthesis.
- On 2026-09-11, an Intel `iossimulator-x64` Debug build completed with Xcode 26.6, launched on an iPhone 17 / iOS 26.5 simulator, wrote preferences and logs under Documents, and logged successful ONNX Runtime and AVAudioEngine initialization.
- Project selection, USTX loading, WAV decoding, waveform peak generation, and autosave also succeeded. Live output could not be completed on that host because macOS reported no audio devices: CoreAudio returned `kAudioQueueErr_InvalidDevice` (`-66680`) for `Default-InputOutput`, then `kAudioUnitErr_InvalidPropertyValue` (`-10851`) for the zero-Hz output node. Retest playback after attaching a physical or virtual CoreAudio device.
- Dynamic plugin DLL scanning may log a harmless missing-file warning for `OpenUtau.Plugin.Builtin.dll` in the application bundle; the statically linked built-in assembly is subsequently loaded successfully by name.

### MacOS (Catalyst), Browser
- Do not prioritize until core features stabilize on Android/Windows/Linux.
