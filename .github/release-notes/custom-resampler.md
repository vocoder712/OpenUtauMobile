## Custom resamplers v0.4 + Auto pitch + Harmony generator

> **Vibe-coded with AI.** The Custom resamplers, Auto pitch and Harmony generator features in this build were written by [Claude](https://claude.com/claude-code) (Anthropic's AI, Claude Sonnet 5.5), directed by sparky-commits1637. The code has not been hand-reviewed or tested on a device yet, so please treat it as experimental.

Adds a **Custom resamplers** page to OpenUtau Mobile so you can use your own external resamplers.

### What's new
- **Options → Custom resamplers**: a new entry between *Plugin manager* and *Help & Tutorials*, styled like the rest of Options.
- **Add** a resampler with the **+** button. Its matching `.yaml` manifest (same name) is copied along with it.
- **Replace** an existing one by adding a file with the same name, or **remove** one with the trash button.
- Added resamplers show up in **Track render settings → Resampler**.
- Translations: English, 简体中文, 日本語, Русский, Українська.

### Good to know
- Resamplers are external programs that run on your device. Only add files you trust. The app asks you to confirm.
- Supported files on Android: `.sh` scripts or extensionless native binaries built for Android. `.exe` / `.bat` only load if a Wine path is set in Settings. The page shows what your device accepts.
- Being listed does not guarantee a program runs on your device.
- Close the open project before adding or removing a resampler.
- Names used by built-in resamplers (`worldline`, `hifisampler`) are not allowed.
- Not offered on iOS or in the browser.

### What's new in v0.4
- **Auto pitch** (Batch edit → Notes): generates a pitch curve from note data with an ONNX model, ported from [UtauV](https://github.com/emeraldsingers/UtauV). The model is **not included**. Download `autopitchcurve` (`.oudep`) from the [UtauV_Packages](https://github.com/emeraldsingers/UtauV_Packages) releases and install it via **Dependency manager → Install from file**. Select a whole phrase first. It runs on the CPU, so expect it to be slow on a phone.
- **Harmony generator** (Batch edit → Notes): adds harmony tracks for the current track (Pop style, automatic key, 1–8 voices), also ported from UtauV.
- **Signed with a fixed key**: this and later builds from this workflow are signed with the same key, so updates install over each other. **Uninstalling the previous copy is needed once**, because the key changed.
- Neither feature has been tested on a device.

### What was new in v0.3
- The app now **targets Android API 28**. On Android 10+, apps targeting API 29 or higher are blocked by SELinux from executing files in their own data folder, so starting a custom resampler failed with **"Permission denied"**. With API 28 that block no longer applies (Termux works the same way). This is the only code change since v0.2.
- **Not fixed / not confirmed:** some native resamplers still crash with **exit code 159 (SIGSYS)**, where Android's seccomp filter kills a process that uses a blocked system call. v0.3 does not address that, and a full render has not been confirmed to finish inside the app.
- Targeting API 28 changes some Android behaviors (for example storage and network defaults). Nothing has been tested on a device for those either.

### About this build
- Installs as a **separate app** named *OpenUtau Mobile (Custom Resampler)* (package `pers.vocoder712.openutaumobile.canary.resampler`), so it can sit next to the official build. It has its own settings, singers and plugins. Projects saved in shared folders can be opened from either app.
- **Updating from v0.3 or earlier:** those builds used a random debug key, so Android will refuse to install this over them. Uninstall that copy first, which erases its app data (imported voicebanks, projects), so back them up before you do. From this version on, builds share one key and update in place.
- Android **ARM64** only, **signed with a fixed (self-generated) key**.
- Built from upstream `dev` plus the custom resampler, Auto pitch and Harmony generator features. It compiles, but has not been tested on a device yet.
- Please report problems with the log from **Options → Export Log**.
