## Custom resamplers v0.2 (separate app)

> **Vibe-coded with AI.** The Custom resamplers feature in this build was written by [Claude](https://claude.com/claude-code) (Anthropic's AI, Claude Sonnet 5.5), directed by sparky-commits1637. The code has not been hand-reviewed or tested on a device yet, so please treat it as experimental.

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

### About this build
- Installs as a **separate app** named *OpenUtau Mobile (Custom Resampler)* (package `pers.vocoder712.openutaumobile.canary.resampler`), so it can sit next to the official build without replacing it. It has its own settings, singers and plugins. Projects saved in shared folders can be opened from either app.
- Android **ARM64** only, **debug-signed**.
- Built from upstream `dev` (`e7cd451`) plus the custom resampler feature. It compiles, but has not been tested on a device yet.
- Please report problems with the log from **Options → Export Log**.
