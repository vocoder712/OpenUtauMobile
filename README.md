# OpenUtau Mobile Developer Preview

## Overview

Developer UI reference: [token ownership and dialog sizing](OpenUtauMobile/Themes/OpenUtauMobile/Tokens/README.md).

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

This is a developer preview. The tables describe the current source and packaging configuration;
**integration does not mean every device, model, or accelerator has been tested**.

### Platform matrix

| Target | Configured release architectures | Status |
| --- | --- | --- |
| Windows | x64, ARM64 | Desktop host; x64 local build, native loading and GAME inference verified. |
| Android | ARM64 (`arm64-v8a`), x64 (`x86_64`) | Minimum Android 7 / API 24; ARM64 and x64 local APK builds verified. |
| Linux | x64, ARM64 | Desktop host and release jobs configured; use a matching Linux build host. |
| macOS | x64, ARM64 | Desktop host and release jobs configured; build on macOS. |
| iOS | Simulator build configuration exists | Experimental host with AVAudioEngine audio and ONNX initialization; full feature parity is not established. Requires macOS/Xcode for development. |
| Browser / WebAssembly | Browser host exists | Experimental; Dummy audio output, no complete singing synthesis runtime integration. |

Windows x86 and Android x86/ARM32 have some project/toolchain mappings but are not part of the current release matrix.
Frameworks and minimum OS versions are defined by the host projects; architectures above follow the
[release workflow](.github/workflows/build-all-platforms.yml).

### Feature matrix

**Integrated** means the application or native dependency is wired in. **Conditional** means external models,
tools, drivers or compatible hardware are required. **Experimental** means integration or end-to-end validation
is incomplete. **Unavailable** means the feature is not exposed or its required native runtime is not bundled.

| Feature | Windows | Android | Linux | macOS | iOS | Browser |
| --- | --- | --- | --- | --- | --- | --- |
| Shared project / piano-roll editor | Integrated | Integrated | Integrated | Integrated | Experimental | Experimental |
| Audio playback | Integrated | Integrated | Integrated | Integrated | Experimental (AVAudioEngine) | Unavailable (Dummy output) |
| Built-in worldline / WORLDLINE-R synthesis | Integrated | Integrated; page-size limitation below | Integrated | Integrated | Unavailable (worldline not bundled) | Unavailable |
| External UTAU resampler / wavtool programs | Conditional: matching Windows tools | No desktop executable compatibility guarantee | Conditional: matching tools / runtime | Conditional: matching tools / runtime | Unavailable | Unavailable |
| ONNX Runtime CPU inference | Integrated | Integrated (Android AAR) | Integrated | Integrated | Experimental | Unavailable in the current host |
| ONNX hardware acceleration | Conditional: DirectML | Conditional: NNAPI | Conditional: CUDA + cuDNN | Conditional: CoreML | Not exposed as an iOS accelerator by the current selector | Unavailable |
| DiffSinger / Vogen model-based synthesis | Conditional | Conditional | Conditional | Conditional | Experimental | Unavailable |
| HifiSampler renderer | Conditional | Conditional | Conditional | Conditional | Experimental | Unavailable |
| GAME note extraction: ONNX | Conditional | Conditional | Conditional | Conditional | Unavailable (entry hidden) | Unavailable (entry hidden) |
| RMVPE pitch curve after note extraction | Conditional (`rmvpe`) | Conditional (`rmvpe`) | Conditional (`rmvpe`) | Conditional (`rmvpe`) | Unavailable | Unavailable |
| GAME note extraction: GGML | Conditional, CPU | Conditional, CPU | Conditional, CPU | Conditional, CPU | Unavailable | Unavailable |
| GGML Vulkan / CUDA / Metal acceleration | Not delivered by default | Not delivered by default | Not delivered by default | Not delivered by default | Unavailable | Unavailable |
| Voicebank / `.oudep` dependency installation | Integrated | Integrated; storage permission required | Integrated | Integrated | Experimental | Experimental filesystem integration |
| Additional Core engines (ENUNU, VOICEVOX, NEUTRINO) | Conditional; external engine required, not verified here | Not verified | Conditional; external engine required, not verified here | Conditional; external engine required, not verified here | Not verified | Not integrated |

Model-based renderers require the appropriate voicebank and auxiliary models. HifiSampler additionally requires
its vocoder dependency package. A renderer appearing in the registry does not certify an external engine's
availability on the target OS. Phonemizers and renderers inherited from Core can have their own dependencies.

### Hardware acceleration

- **ONNX:** the current [provider selector](OpenUtau.Core/Util/Onnx.cs) offers DirectML on Windows,
  NNAPI on Android, CoreML on macOS, and CUDA on Linux when CUDA and cuDNN are detected.
  Availability also depends on the packaged ONNX Runtime, device drivers and model operators. Selecting a
  provider does not establish that every operation executes on the GPU/NPU or that it is faster than CPU.
- **GAME ONNX:** uses this ONNX configuration. Its encoder explicitly uses CPU when CoreML is selected;
  the other model sessions use the selected provider.
- **GAME GGML:** default builds use CPU on all four integrated platforms. ONNX settings do not control GGML.
  Upstream GPU build options exist, but GPU runtime packaging and device inference have not been validated
  for this integration. Installing an `.oudep` containing a desktop executable does not enable GPU execution.
- **worldline:** the bundled path is native CPU synthesis, independent of both inference backends.
  UI graphics acceleration is also separate from singing synthesis and model inference.

### GAME note extraction

Select one audio clip and choose **Note extraction** from its context menu. The dialog shows model dependency
status, language and decoding options, plus ONNX batch limits. Use the ONNX or GGML button to start.
Optional RMVPE pitch extraction requires the `rmvpe` dependency (`rmvpe.onnx`); its checkbox is disabled when missing.
It adds a pitch-deviation curve to the extracted notes. Batch settings affect ONNX only.
The result is added to a new track and can be undone in one operation. Empty results, cancellation and failures
create no track. Backend selection is explicit; a missing backend does not silently switch to the other one.

Install models through the application's dependency manager:

| Backend | Dependency package | Expected files |
| --- | --- | --- |
| ONNX | `game`, from [GAME releases](https://github.com/openvpi/GAME/releases/tag/oudep) | `config.json`, `encoder.onnx`, `segmenter.onnx`, `estimator.onnx`, `bd2dur.onnx` |
| GGML | `game-ggml-medium`, GAME GGML `.oudep` from [game.cpp releases](https://github.com/KakaruHayate/game.cpp/releases) | Exactly one compatible `.gguf` model in the installed package tree. If this package is absent, the legacy `game` directory is checked. |

Models are separate from application builds. Mobile uses its own in-process `opum_game` library, not the desktop
CLI from a model package. To test extraction, use a short vocal clip first; recognition quality and memory use
vary with the input and model. See [implementation details](native/game/README.md) for chunking and cancellation.

### Verification and known limits

For the current native integration, local checks on Windows have covered default Debug native loading,
Windows x64 Release publish, actual GGUF inference, and Android ARM64/x64 APK builds. SDK and NDK in separate
Windows directories were also tested. Android APK checks confirm the expected GAME ABI is packaged.
These checks do **not** establish Android device inference, debugger attachment, GPU performance, or successful
execution of all GitHub-hosted jobs. Check the actual workflow runs for CI results.

The new Android GAME libraries use 16 KB alignment. Existing dependencies can still emit `XA0141`:
`libworldline.so` in the ARM64 build and `libSDL3.so` in the x64 build were observed with this warning.
The whole APK is therefore not certified for 16 KB page-size devices.

Developer references: [environment setup and debugging](CONTRIBUTING.md),
[architecture boundaries](docs/ARCHITECTURE.md), [platform integration](docs/PLATFORMS.md),
and [GAME native maintenance](native/game/README.md).

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

## Building and debugging

Start with [CONTRIBUTING.md](CONTRIBUTING.md) for a Windows computer: tool installation, Windows debugging,
Android SDK/NDK/JDK setup, device connection and Rider debugging. Native GAME compilation is part of the normal
project build. Support and accelerator status are listed in the matrices above.
