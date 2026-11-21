## Custom resamplers (test build)

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
- Android **ARM64** only, **debug-signed**. If you have another build installed with a different signature, uninstall it first (this removes its app data, so back up your projects).
- Built from upstream `dev` (`e7cd451`) plus the custom resampler feature. It compiles, but has not been tested on a device yet.
- Please report problems with the log from **Options → Export Log**.
