# OpenUtau Mobile Developer Preview

## Overview

To solve the performance issues, outdated UI design, and cross-platform limitations of the first-generation implementation, the project is being rebuilt from scratch.

The new version is based on:

* Avalonia 11
* .NET 9 Preview
* MVVM architecture

The goal is to provide a more modern, maintainable, and truly cross-platform mobile singing synthesis experience.

> [!WARNING]
> This branch is under heavy development.
>
> Architecture may change frequently.

---

# Current Status

### Working Platforms

* Android (11+ tested)
* Windows
* Linux

### Planned Platforms

* iOS (failed passing compilation stage)
* macOS (failed passing compilation stage)
* WebAssembly (obstacles in initializing stage)

**Rad `.agent` for more information on development workflow and project context.**

---

# Development Environment

## Requirements

### SDKs

* .NET 9 SDK
* Android SDK (for Android development)
* JDK (for Android development)
* Xcode (for iOS/macOS development, not yet working)

### Recommended IDEs

* Visual Studio (best recommended)
* JetBrains Rider
* VS Code (limited support)
* Xcode (for iOS/macOS development only)

---

# Getting Started

## Clone Repository

```bash
git clone https://github.com/vocoder712/OpenUtauMobile.git
cd OpenUtauMobile
```

## Checkout Development Branch

```bash
git checkout dev
```

## Restore Dependencies

```bash
dotnet restore
```

## Run Windows Version

```bash
cd OpenUtauMobile.Windows
dotnet build -t:Run -c Debug -f net9.0-windows
```

## Build Android Version

Change to the Android project directory and connect an Android device or start an emulator, then execute:

```bash
dotnet build -t:Run -c Debug
```

Use release configuration for better performance.

---

# Contributing

Contributions are WELCOME!

You can fork the repository, make changes, and submit a pull request. Make sure to follow the coding styles in `.editorconfig`.

---

# Reporting Issues

When reporting bugs, please provide:

* Device model
* OS version (Whether is HarmonyOS or Android)
* App version
* Reproduction steps
* Screenshots or screen recordings
* Logs if available

---

# Android Log Collection
When encountering unexpected exits or crashes on Android, collecting logs can help identify the root cause.

## Using adb logcat

If you have Android platform tools installed:

```bash
adb logcat > log.txt
```

Reproduce the issue, then stop recording and upload the log file. Recommended to filter out personal information before sharing.

---

# Special Thanks

* [Mystic](https://github.com/MysticILD) for earlier contributions.

---

# License

This project is licensed under the Apache License 2.0. See the [LICENSE](LICENSE) file for details.

This project also includes third-party code with their own licenses. See [Third Party Notices](THIRD_PARTY_NOTICES.md) for details.
