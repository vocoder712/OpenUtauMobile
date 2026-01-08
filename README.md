# OpenUtau Mobile
[English](README.md) | [简体中文](README_zh.md)
## Special Thanks

- [OpenUtau](https://github.com/stakira/OpenUtau)

## What is OpenUtau Mobile?

OpenUtau Mobile is an open-source, free singing synthesis software for mobile devices.

It is an editor based on the [OpenUtau Core](https://github.com/stakira/OpenUtau/tree/master/OpenUtau.Core) with some patches applied. It fully supports OpenUtau USTX project files.

## Compatibility

### Platforms

- Android 5.0 and above

### Singer Types

- DiffSinger
- UTAU
- Vogen

Other untested types are not guaranteed to work correctly.

## Quick Start

1. Download the installer for your platform and architecture from the [Releases](https://github.com/vocoder712/OpenUtauMobile/releases) page and install it.
2. Download a voicebank. You can usually find download links on the [DiffSinger Custom Voicebank Share Page](https://docs.qq.com/sheet/DQXNDY0pPaEpOc3JN?tab=BB08J2) or the [UTAU wiki](https://utau.fandom.com/). Voicebanks are usually packaged in ZIP format.
3. Open the software → Click the `Singer` button on the home page → Click the `+` in the bottom right → Select the voicebank package (ZIP) downloaded in the previous step, and follow the instructions to install.
4. Return to the home page, click `New` to enter the main interface, and start creating!

You can also use the `Open` button on the home page to directly find and open OpenUtau USTX project files.

**Note:** The software is currently very unstable. Due to limitations of the development framework, memory management issues may occur frequently. Therefore, **remember to save often**. If the app crashes, you can find a recovery file ending in `.autosave.ustx` in the same directory as your project file.

## Interface Introduction

See [Interface Introduction](./UIIntroductions.md) for details.

## Building & Contributing

If you want to help improve this project:

- If you find a bug, have a feature request, or have a suggestion for the UI/UX, please check [Planned Features & Known Bugs](#planned-features--known-bugs) first. If it is not listed there, feel free to report bugs in [Issues](https://github.com/vocoder712/OpenUtauMobile/issues) or discuss suggestions in [Discussions](https://github.com/vocoder712/OpenUtauMobile/discussion).

- **Contributing Code:** Clone this project locally, then use Visual Studio to open `OpenUtauMobile.sln` in the project root directory to enter the development environment. It is recommended to create a new branch for your changes. You can refer to the task list in [Planned Features & Known Bugs](#planned-features--known-bugs) to implement features or fix bugs. Once completed, you can submit a Pull Request.

## License

This project is open-source under the [Apache 2.0](./LICENSE.txt) license.

This is NOT the official OpenUtau application and must not impersonate the official OpenUtau.
