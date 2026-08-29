# OpenUtau Mobile Developer Preview

## Overview

To solve the performance issues, outdated UI design, and cross-platform limitations of the first-generation implementation, the project is being rebuilt from scratch.

The new version is based on:

* Avalonia 12
* .NET 10
* MVVM architecture

The goal is to provide a more modern, maintainable, and truly cross-platform mobile singing synthesis experience.

> [!WARNING]
> This branch is under heavy development.
>
> Architecture may change frequently.

---

## Current Status

### Working Platforms

* Android (7+ supported, 11+ tested)
  * arm64-v8a
  * armeabi-v7a
  * x86_64
* Windows
  * x64
  * arm64
* Linux
  * x64
  * arm64
* MacOS
  * x64
  * arm64

### Planned Platforms

* iOS (failed passing compilation stage)
* WebAssembly (obstacles in initializing stage)

**Read `.agent` for more information on development workflow and project context.**

---

## Contributing

Contributions are WELCOME!

Please read the [CONTRIBUTING.md](CONTRIBUTING.md) for details on how to contribute.

---

## Reporting Issues

When reporting bugs, please provide:

* Device model
* OS version (Whether is HarmonyOS or Android)
* App version
* Reproduction steps
* Screenshots or screen recordings
* Logs if available

---

## Android Log Collection
When encountering unexpected exits or crashes on Android, collecting logs can help identify the root cause.

### Using adb logcat

If you have Android platform tools installed:

```bash
adb logcat > log.txt
```

Reproduce the issue, then stop recording and upload the log file. Recommended to filter out personal information before sharing.

---

## Special Thanks

* [MysticILD](https://github.com/MysticILD) for earlier contributions (adding support for the HifiSampler resampler, vibrato and pitch anchor mode implementation, and finishing multi-selection mode. Provided full English, Ukrainian, and Russian localizations).

---

## License

This project is licensed under the Apache License 2.0. See the [LICENSE](LICENSE) file for details.

This project also includes third-party code with their own licenses. See [Third Party Notices](THIRD_PARTY_NOTICES.md) for details.
