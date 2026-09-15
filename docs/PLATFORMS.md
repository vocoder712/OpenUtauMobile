# Platform integration

This reference describes source-verified responsibilities, not device support or
build-success certification. SDK, target frameworks and minimum OS values belong
to [global.json](../global.json) and the host project files; release packaging
belongs to the [build workflows](../.github/workflows).

## Bootstrap boundary

Desktop `Program` entry points and Android's `AndroidApp` / `MainActivity`
configure paths, logging and ServiceHub capabilities before shared application
initialization. Shared [App](../OpenUtauMobile/App.axaml.cs) chooses the lifetime:
desktop gets MainWindow, Android gets an activity main-view factory, and iOS/browser
get a single MainView. [SplashScreenViewModel](../OpenUtauMobile/ViewModels/SplashScreenViewModel.cs)
initializes Core with the captured UI thread/scheduler and invokes host audio setup.
Do not infer capability parity merely because hosts share App.

## Android

- [AndroidApp](../OpenUtauMobile.Android/AndroidApp.cs) delegates builder setup to
  [MainActivity](../OpenUtauMobile.Android/MainActivity.cs). Native immersive-mode
  visibility is activity-owned through AndroidX; avoid a second shared-UI owner.
- The internal picker uses raw filesystem paths, not Storage Access Framework URIs.
  [Storage service](../OpenUtauMobile.Android/Storage/AndroidExternalStorageService.cs)
  resolves the live activity for legacy runtime read/write permissions. Android 10
  uses the [manifest's](../OpenUtauMobile.Android/Properties/AndroidManifest.xml)
  legacy-storage opt-in; Android 11+ uses all-files access. Changing to URI-based
  storage is an integration change, not a permission-name substitution.
- The [project](../OpenUtauMobile.Android/OpenUtauMobile.Android.csproj) embeds Debug
  assemblies to avoid startup loading stalls from Fast Deployment. Debug has a
  distinct application ID, allowing side-by-side installation with Release.
- ONNX native libraries come only from the official Android AAR. Preserve the
  project's native/runtime/build asset exclusions: competing host-native assets
  can select a glibc library for the same APK path. The reusable
  [Android workflow](../.github/workflows/build-android.yml) restores for the final
  RID and Release configuration, publishes that same asset graph, and checks APK
  ABI/native payloads. Do not substitute successful managed compilation for this check.

## Desktop, iOS and browser distinctions

- [MacOS](../OpenUtauMobile.MacOS/OpenUtauMobile.MacOS.csproj) is an Avalonia desktop
  host, not Mac Catalyst; its Program uses the classic desktop lifetime.
- [Windows](../OpenUtauMobile.Windows/OpenUtauMobile.Windows.csproj) and
  [Linux](../OpenUtauMobile.Linux/OpenUtauMobile.Linux.csproj) publish targets copy
  RID-native files beside the executable for P/Invoke lookup. Preserve this during
  packaging changes; an assembly-only output is insufficient.
- [iOS AppDelegate](../OpenUtauMobile.iOS/AppDelegate.cs) does not register audio
  initialization or external storage. 
- [Browser Program](../OpenUtauMobile.Browser/Program.cs)
  uses silent Dummy audio and injects PathManager through private fields instead of
  its normal constructor. Core singleton/property changes therefore need browser
  bootstrap review. These are source limitations, not claims about current build
  failures or startup hangs.

## Local build/run

Use the pinned SDK. Android additionally needs the Android workload, SDK platform
matching its target framework (currently API 36), and JDK 17 as used by CI.
From the repository root, PowerShell examples for an installed toolchain:

```powershell
$env:AVALONIA_TELEMETRY_OPTOUT='1'
dotnet build OpenUtauMobile.Android/OpenUtauMobile.Android.csproj -t:Run -c Debug -p:AndroidDebugger=true
dotnet run --project OpenUtauMobile.Windows/OpenUtauMobile.Windows.csproj -c Debug
```

Android deployment needs a connected device/emulator. For Linux or macOS, run on
the corresponding OS and substitute that host's project in the desktop command.
For release RIDs, native dependencies and packaging commands use the current
[full-platform workflow](../.github/workflows/build-all-platforms.yml), rather than
assuming the solution can be built/run identically on every development host.
