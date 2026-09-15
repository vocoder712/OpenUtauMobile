# Contributing to OpenUtau Mobile

[简体中文](../CONTRIBUTING.md) | **English**

Thank you for your interest in contributing to OpenUtau Mobile!

Contributions of all kinds are welcome, including code, bug fixes, documentation, translations, UI/UX improvements, testing, and technical discussions.

OpenUtau Mobile is currently under active development. Architecture, APIs, and platform-specific implementations may change frequently, so please keep changes focused and coordinate large changes before investing significant work.

## Ways to contribute

You can contribute by:

* fixing bugs;
* implementing new features;
* improving performance;
* improving UI/UX;
* adding or improving platform support;
* improving documentation and translations;
* testing builds on different devices and platforms;
* reviewing Pull Requests;
* participating in Issues and Discussions.

For substantial new features or architectural changes, please discuss the proposal before starting implementation. This helps avoid duplicated work and ensures that the change fits the current project direction.

Other planned development tasks can be found in [TODO](https://docs.qq.com/sheet/DV2NuakZtQW1LZUNS).

---

## Setting up your development environment on Windows

The following assumes a Windows x64 PC and Rider. For the detailed walkthrough and troubleshooting,
see the [Chinese guide](../CONTRIBUTING.md). Platform, renderer and accelerator status belongs to the
[global feature matrix](../README.md#feature-matrix); native implementation details belong to
[native/game/README.md](../native/game/README.md).

### 1. Install the common tools

- Git for Windows, available on PATH.
- The **.NET SDK**, not only the runtime, matching [global.json](../global.json)
  (currently `10.0.400`, with `latestPatch` roll-forward).
- Visual Studio Build Tools: **Desktop development with C++**, including MSVC x64/x86 and Windows SDK.
  Rider does not replace the C++ compiler required by the default GGML build.
- CMake 3.24+ on PATH.
- Rider with support for the pinned .NET SDK.

Restart terminals and Rider after installing tools or changing PATH. The first build downloads fixed native
sources from GitHub and packages from NuGet; it also compiles GAME. Models are installed separately in the app.

### 2. Clone and create a branch

Fork the repository on GitHub, then run:

```powershell
git clone https://github.com/<your-username>/OpenUtauMobile.git
cd OpenUtauMobile
git remote add upstream https://github.com/vocoder712/OpenUtauMobile.git
git fetch upstream
git switch dev
git pull --ff-only upstream dev
git switch -c feature/your-feature
```

Preserve local changes before switching branches or syncing. Run the following from the repository root:

```powershell
git --version
dotnet --version
cmake --version
$env:AVALONIA_TELEMETRY_OPTOUT='1'
```

This environment variable applies to this shell and child processes. Set it as a Windows user environment
variable and restart Rider if you launch the IDE from a shortcut.

### 3. Debug Windows

```powershell
$env:AVALONIA_TELEMETRY_OPTOUT='1'
dotnet restore OpenUtauMobile.Windows/OpenUtauMobile.Windows.csproj
dotnet run --project OpenUtauMobile.Windows/OpenUtauMobile.Windows.csproj -c Debug
```

Use project-specific restore/build: restoring the whole solution may require unrelated platform workloads.
In Rider, open `OpenUtauMobile.sln`, check the .NET CLI path, and select the Windows project's **.NET Project**
run configuration and **Debug** build configuration. Build the target project and dependencies before launch,
set a C# breakpoint, then click Debug. If necessary, create the configuration under **Run → Edit Configurations**.

No explicit RID or `EnableGameGgml` flag is needed. GAME and worldline are placed in the correct output directory.
Default output is `OpenUtauMobile.Windows/bin/Debug/net10.0-windows/`; an explicit RID adds a RID subdirectory.

### 4. Prepare Android

Install `dotnet workload install android` from the repository root (use an elevated terminal if requested).
Install JDK 21 and use Android Studio's SDK Manager to install API 36, Build-Tools 36.0.0, Platform-Tools,
Command-line Tools (latest), and the NDK from [android-ndk-version.txt](../native/game/android-ndk-version.txt).
Install Ninja on PATH as well as CMake. Android uses the NDK's Clang compiler.
The Android project and installed workload remain the source of truth for SDK requirements.
Current CI uses JDK 17; local builds have also passed with JDK 21.

Replace the JDK placeholder and SDK path with your installation:

```powershell
$androidSdk = Join-Path $env:LOCALAPPDATA 'Android/Sdk'
$androidNdkVersion = (Get-Content native/game/android-ndk-version.txt -Raw).Trim()
$androidNdk = Join-Path $androidSdk "ndk/$androidNdkVersion"
$javaSdk = 'C:/replace-with-your-JDK21-directory'
$env:JAVA_HOME = $javaSdk
$env:ANDROID_HOME = $androidSdk
& "$javaSdk/bin/java.exe" -version
& "$androidSdk/cmdline-tools/latest/bin/sdkmanager.bat" --licenses
& "$androidSdk/cmdline-tools/latest/bin/sdkmanager.bat" "ndk;$androidNdkVersion"
ninja --version
Test-Path "$androidNdk/build/cmake/android.toolchain.cmake"
```

Read and accept required licenses. The final check should return `True`. If the NDK is installed separately,
set `$androidNdk` to that directory. In Rider settings, search for **Android SDK** and set SDK, NDK and Java SDK
roots to these actual locations. Shell variables do not update an already running IDE. SDK and NDK may be on
different drives; the GAME build uses the path resolved by the Android workload.

### 5. Debug Android

For a physical device, enable developer options and USB debugging, connect a data cable, accept the authorization
prompt, and install the manufacturer's ADB driver if needed. Alternatively, start an emulator from Android
Studio Device Manager (usually an x86_64 image on a Windows x64 PC).

```powershell
& "$androidSdk/platform-tools/adb.exe" devices -l
& "$androidSdk/platform-tools/adb.exe" -s <device-serial> shell getprop ro.product.cpu.abilist
```

The connection must report `device`, not `unauthorized` or `offline`. Match the RID to the device:
`arm64-v8a` → `android-arm64`, `x86_64` → `android-x64`, `armeabi-v7a` → `android-arm`.
To check the build before attaching a debugger:

```powershell
$env:AVALONIA_TELEMETRY_OPTOUT='1'
$androidRid = 'android-arm64'
dotnet build OpenUtauMobile.Android/OpenUtauMobile.Android.csproj -c Debug -r $androidRid "-p:AndroidSdkDirectory=$androidSdk" "-p:AndroidNdkDirectory=$androidNdk" "-p:JavaSdkDirectory=$javaSdk"
```

The signed APK is under `OpenUtauMobile.Android/bin/Debug/net10.0-android36.0/<RID>/`.
In Rider, choose or create an **Android** run configuration for `OpenUtauMobile.Android`, use **Debug** and
**Default APK**, select the device, keep the pre-launch build, set a C# breakpoint and click Debug.
Rider builds, installs, launches and attaches the debugger. No CI release-signing secrets are needed for Debug.
See [Rider's Android instructions](https://www.jetbrains.com/help/rider/Run_Debug_Configuration_Xamarin_Android.html).

If compilation fails, inspect the first error and resolved SDK/NDK/JDK paths. A missing native library differs
from a missing model; do not copy binaries from another RID. After changing CMake generators, use a fresh
`GameBuildRoot`. Check the [known limitations](../README.md#verification-and-known-limits), including Android
16 KB alignment warnings in existing dependencies. See the Chinese guide for the full troubleshooting table.

---

## Branches

### `dev`

`dev` is the main development and integration branch for OpenUtau Mobile V2.

Normal contributions should target this branch.

### `master`

`master` contains the legacy generation of OpenUtau Mobile.

It is not the base branch for normal V2 development.

Unless explicitly required, Pull Requests should not target `master`.

---

## Code style

Follow the existing code style and project architecture.

In particular:

* respect the repository's `.editorconfig`;
* keep platform-independent logic out of platform-specific projects when possible;
* prefer focused changes that are easy to review;
* preserve compatibility with the architectures and platforms affected by your change.

---

## Commit messages

OpenUtau Mobile follows a lightweight form of the Conventional Commits convention.

Preferred format:

```text
<type>: <description>
```

Common types include:

```text
feature
fix
performance
refactor
docs
test
build
ci
chore
```

Keep the first line short and describe what the commit changes.

For larger commits, add a body explaining why the change was necessary.

Example:

```text
feature: add a phoneme and parameter panel and translations

Add a collapsible phoneme and parameter panel beneath the piano roll to support editing phoneme timing, aliases, overlap, preutterance, and expression parameters.

The panel has four editing modes: Simple Phoneme (timing and aliases), Advanced Phoneme (timing, preutterance, and overlap handles), Parameter Draw, and Parameter Erase.
```

If a commit relates to an Issue or Pull Request, reference it where useful:

```text
Fixes #123
Refs #456
```

Perfect commit history is appreciated but not required for every contribution. Pull Requests may be squashed during merge.

---

## Pull Request process

### 1. Keep your branch up to date

Before opening or updating a Pull Request:

```bash
git fetch upstream
```

If the branch is private to you, rebasing onto the latest `dev` is preferred:

```bash
git rebase upstream/dev
```

If you have already pushed the rebased branch:

```bash
git push --force-with-lease
```

Use `--force-with-lease`, not plain `--force`.

If multiple developers are sharing the same branch, do not rewrite its history without coordinating with them.

### 2. Build and test your changes

Before submitting a Pull Request:

* restore dependencies successfully;
* build the affected project(s);
* follow the repository policy: do not create or run unit tests after implementing new features;
* manually verify the affected functionality where appropriate;
* test platform-specific changes on the corresponding platform whenever possible.

A Pull Request does not need to build every supported platform locally, but changes should not knowingly break unrelated platforms.

CI checks should pass before the Pull Request is merged. Documentation-only changes
need diff/link review rather than an application build. Before `dotnet build`, set
`AVALONIA_TELEMETRY_OPTOUT=1` in the environment.

Engineering references: [architecture boundaries](ARCHITECTURE.md) and
[platform integration and local run commands](PLATFORMS.md).

### 3. Keep the Pull Request focused

A Pull Request should normally represent one logical change.

Avoid combining:

* unrelated bug fixes;
* broad formatting changes;
* dependency upgrades unrelated to the feature;
* large refactors that are not required by the change.

Large changes are easier to review when divided into logically independent Pull Requests.

### 4. Write a useful description

The Pull Request description should explain:

* what changed;
* why the change is needed;
* how it was implemented when the implementation is not obvious;
* how it was tested;
* which platforms are affected.

Include screenshots or screen recordings for visible UI changes when useful.

Reference related Issues, Pull Requests, Discussions, or commits.

Examples:

```text
Fixes #123
Refs #456
```

If the Pull Request makes another Pull Request unnecessary, mention that explicitly as well.

### 5. Target `dev`

Normal Pull Requests should use:

```text
base: dev
```

Do not target `master` unless the change is specifically intended for the legacy branch.

---

## Merge, rebase, and squash policy

These operations serve different purposes and should not be used interchangeably.

### Rebase

Rebase is mainly used to update a topic branch onto the latest `dev` while keeping its history linear.

Typical use:

```bash
git fetch upstream
git rebase upstream/dev
```

### Squash

Squashing combines several commits into a smaller number of logical commits.

Ordinary Pull Requests are generally good candidates for **Squash and merge**, especially when the branch contains temporary commits such as:

```text
fix typo
address review
try another approach
fix build
```

The final commit should describe the complete logical change rather than the intermediate development process.

### Merge commits

Not recommended.

### Default policy

For normal contributions:

1. create a topic branch from `dev`;
2. commit normally while developing;
3. rebase the topic branch onto the latest `dev` when appropriate;
4. open a Pull Request targeting `dev`;
5. use **Squash and merge** for the final integration unless there is a specific reason to preserve the individual commits.

---

## AI-assisted contributions

Using coding assistants or other AI tools is recommended.

**However, the contributor must be responsible for the submitted code.**

Before submitting AI-assisted changes:

* review every relevant change;
* understand the behavior being modified;
* remove unrelated or speculative changes;
* verify the result according to the build/verification guidance above;
* verify that generated code follows existing architecture and style;
* do not include secrets, credentials, private data, or copyrighted material that cannot legally be contributed.

Large AI-generated rewrites without a clear reason or without verification may be rejected even if they compile.

AI tools can assist development, not replace review and engineering judgment.

---

## Upstream synchronization

OpenUtau Mobile periodically synchronizes code from the upstream OpenUtau repository.

Upstream synchronization is a repository maintenance operation and is different from normal feature development.

Do **not** manually merge arbitrary OpenUtau upstream commits into `dev`.

See [Upstream Synchronization Guide](UPSTREAM_SYNC.md) for the complete procedure.

---

## Version ownership

Product release versions and build numbers are resolved by
[main.yml](../.github/workflows/main.yml); local defaults and assembly metadata
are owned by [Directory.Build.props](../Directory.Build.props).
CoreVersion identifies the upstream snapshot and follows the upstream guide.
Do not infer product-version increments from an upstream sync or duplicate CI
version formulas in documentation.

## Reporting bugs

When reporting a reproducible bug, include as much relevant information as possible:

* OpenUtau Mobile version or commit;
* device model;
* operating system and version;
* affected platform;
* reproduction steps;
* expected behavior;
* actual behavior;
* screenshots or screen recordings where useful;
* logs or stack traces where available.

For Android crashes, `adb logcat` is often useful:

```bash
adb logcat > log.txt
```

Start logging, reproduce the issue, stop logging, and remove personal or unrelated information before sharing the log.

---

## Licensing

By contributing to OpenUtau Mobile, you agree that your contributions will be distributed under the repository's applicable license.

Do not submit code, assets, libraries, models, or other materials unless they can legally be included and redistributed by the project.

Third-party code and assets must retain any required notices and licensing information.
