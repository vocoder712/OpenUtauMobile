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
- iOS 15.0 and above (requires self-building)

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

- **Contributing Code:** Clone this project locally, then use Visual Studio to open `OpenUtauMobile.sln` in the project root directory to enter the development environment. It is recommended to create a new branch for your changes. Once completed, you can submit a Pull Request.

## iOS Build Guide

Due to Apple's policy restrictions, the iOS version cannot be distributed as a pre-built package and must be built on macOS by yourself.

### Requirements

- macOS (required)
- .NET 9.0 SDK
- Xcode 15 or later
- Apple Developer Account (free account works for personal devices)
- MAUI workload

### Setting Up the Development Environment

```bash
# Install .NET 9.0 SDK (if not already installed)
brew install dotnet-sdk

# Install MAUI iOS workload (for iOS only)
dotnet workload install maui-ios

# Or install the full MAUI workload (supports both Android and iOS)
# dotnet workload install maui
```

### Build Steps

1. **Clone the repository**
   ```bash
   git clone https://github.com/vocoder712/OpenUtauMobile.git
   cd OpenUtauMobile
   ```

2. **Restore dependencies**
   ```bash
   # Restore for iOS only, avoiding automatic Android workload download
   dotnet restore -p:TargetFramework=net9.0-ios
   ```

3. **Build the IPA package**
   ```bash
   dotnet publish OpenUtauMobile/OpenUtauMobile.csproj \
       -f net9.0-ios \
       -c Debug \
       -p:ArchiveOnBuild=true \
       -p:RuntimeIdentifier=ios-arm64 \
       -p:TargetFrameworks=net9.0-ios
   ```
   
   After the build completes, the IPA file will be located at:
   `OpenUtauMobile/bin/Debug/net9.0-ios/ios-arm64/publish/OpenUtauMobile.ipa`

### Installing on iPhone

1. **Connect your iPhone to Mac via USB**

2. **Find your device ID**
   ```bash
   xcrun devicectl list devices
   ```

3. **Install the app**
   ```bash
   xcrun devicectl device install app \
       --device <your-device-id> \
       OpenUtauMobile/bin/Debug/net9.0-ios/ios-arm64/publish/OpenUtauMobile.ipa
   ```

4. **Trust the developer certificate**
   
   After the first installation, go to your iPhone:
   **Settings → General → VPN & Device Management**, find the developer certificate and tap Trust.

### FAQ

**Q: The build takes too long, what can I do?**

A: Debug mode builds typically take 2-5 minutes. Release builds (with AOT enabled) may take 20-40 minutes. It's recommended to use Debug mode for daily development.

**Q: What if the certificate expires?**

A: Free developer certificates are valid for 7 days. After expiration, you need to rebuild and reinstall. A paid developer account ($99/year) provides certificates valid for 1 year.

**Q: Can I build without a Mac?**

A: No. Apple requires iOS apps to be built on macOS using the Xcode toolchain.

**Q: What if I also want to build the Android version?**

A: Install the full MAUI workload instead:
```bash
dotnet workload install maui
```

Then remove the `-p:TargetFrameworks=net9.0-ios` parameter from the build command, or change it to the Android target framework.

## License

This project is open-source under the [Apache 2.0](./LICENSE.txt) license.

This is NOT the official OpenUtau application and must not impersonate the official OpenUtau.
