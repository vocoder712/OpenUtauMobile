# Decisions Log

Record meaningful technical decisions here. Use one entry per decision.

## Template

- Date:
- Decision:
- Rationale:
- Alternatives considered:
- Impacted areas:

## Entries

- Date: 2026-08-25
- Decision: Persist PitchPen blank-area canvas dragging as an enabled-by-default preference, with independently configurable note-hit extensions of 0–960 ticks horizontally (default 240) and 0–12 semitones vertically (default 1). Each new PianoRollViewModel snapshots the clamped preference values in its constructor; disabling the feature bypasses the expanded hit test and retains full-canvas pitch drawing.
- Rationale: Tick/semitone ranges remain stable across zoom levels and let touch users tune intent recognition without changing global note hit testing. Constructor snapshots keep one editor session internally consistent while settings changes apply to subsequently created editors.
- Alternatives considered: Read preferences during every pointer gesture; update the active piano-roll view model live; keep fixed constants; express expansion in screen pixels.
- Impacted areas: Mobile preferences, Settings editing-and-behaviour controls and localization, PitchPen/eraser drag initiation, and PianoRollViewModel construction. Other edit modes and the original note hit test are unchanged.

- Date: 2026-08-25
- Decision: In PitchPen mode, lock the single-finger drag intent from the original press point: begin pitch drawing or erasing only when that point intersects a note rectangle expanded by 60 px horizontally on each side and 120 px vertically on each side; otherwise reuse the existing canvas-pan path.
- Rationale: The enlarged, screen-space note target makes near-note pitch editing touch-friendly while leaving blank-space drags available for navigation. Resolving the intent once at drag start prevents the gesture from switching between drawing and panning as the pointer moves.
- Alternatives considered: Always draw in PitchPen mode; switch intent continuously during movement; change the existing note hit test globally; add a new raw pointer-pressed callback to the shared gesture interpreter.
- Impacted areas: Piano-roll PitchPen and pitch-eraser single-finger drag initiation. Existing note selection, note movement, anchor editing, multi-touch zooming, magnifier lifecycle, and pan inertia paths are unchanged.

- Date: 2026-08-24
- Decision: Show a top-left reset target while an advanced phoneme timing handle is captured. Its idle geometry follows the MD3 icon-button proportions: a 48dp target, 24dp icon, 12dp internal padding, and 8dp canvas inset. Animate size and semantic error colors with a 150ms cubic ease-out transition when the pointer crosses the target boundary. Suspend value updates while the pointer is over it and reset only the active Offset, Preutter, or Overlap override when released there.
- Rationale: A capture-scoped target keeps the touch gesture discoverable without permanently consuming panel space. Top-left placement avoids the finger travel and bottom-edge conflict of the earlier centered placement. Resetting with the existing typed commands keeps the change in the same undo group as the drag and preserves command validation and undo behavior.
- Alternatives considered: Add permanent reset buttons for every phoneme; reset all timing fields together; cancel the whole drag by undoing intermediate commands.
- Impacted areas: Advanced phoneme-panel handle dragging, localized reset guidance, motion rendering, cached Phosphor Icons geometry, and semantic layout tokens. Core command behavior is unchanged.

- Date: 2026-08-24
- Decision: Reuse the editor's existing magnifier and unchanged `PART_PianoRollGrid` source for curve-parameter brush and eraser gestures, translating parameter-canvas pointer coordinates into that source's coordinate space at the view boundary.
- Rationale: The magnifier already owns source sampling and pitch editing behavior. Forwarding only curve-parameter gesture lifecycle events avoids duplicating the control, preserves numerical/options editing behavior, and keeps coordinates accurate across responsive piano-roll and parameter-panel sizes.
- Alternatives considered: Give the parameter panel a second magnifier; change the magnifier source to the parameter canvas; reuse pitch events while applying fixed row offsets.
- Impacted areas: Parameter-curve pointer lifecycle, phoneme-parameter panel event forwarding, and editor magnifier coordinate routing. Existing pitch editing and magnifier source selection are unchanged.

- Date: 2026-08-24
- Decision: M4 — wire GAME transcription into the Mobile layer by subclassing Core's `MidiExtractor<GameOptions>` (`GameGgmlMidiExtractor`), replacing only `TranscribeWaveform` to call our `GameGgml.Infer`; add an EditorMore "Transcribe Audio" action + progress popup.
- Rationale: Core's base MidiExtractor already owns the battle-tested orchestration (mono conversion, 44.1k resample, AudioSlicer chunking, tick conversion, UVoicePart assembly). Subclassing lets us reuse all of it without touching Core, swapping only the inference backend ONNX→ggml C ABI. This is consistent with the "don't modify Core" constraint (read-only reuse).
- Alternatives considered: A standalone mobile-side transcriber duplicating mono/resample/part-assembly (more code, risk of subtle timing divergence from Core); modifying Core to swap backend (violates constraint).
- Impacted areas: OpenUtauMobile/Services/Game/GameGgmlMidiExtractor.cs; EditorMoreAction enum; EditorMorePopup.axaml; EditorViewModel; 5 resx files; M4 UI flow.

- Date: 2026-08-24
- Decision: (SUPERSEDED below) M3 originally bundled the Q8 GAME weights directly into the shared project as an EmbeddedResource copied from a checked-in `Models/game_medium.gguf`. This proved unworkable for repo hygiene (55 MB binary in git), so it was replaced by the build-time `.oudep` extraction decision immediately below. Net effect on impacted areas unchanged: model still ships with the app, still materialized to `LocalApplicationData/game/` at runtime, same `GameModelResolver` code path.
- Rationale: See superseding decision below.
- Alternatives considered: AndroidAsset + platform copy; runtime download.
- Impacted areas: OpenUtauMobile.csproj, Services/Game/*, M4 wiring, M5 packaging/APK size.

- Date: 2026-08-24
- Decision: Do NOT commit the model binary. Extract `game_medium.gguf` at **build time** from the local game.cpp release `.oudep` (zip; `J:\GGML-GAME\vendor\game-cpp-release\game_ggml-windows-x64-vulkan-q8.oudep`) into `obj/` and register that copy as an EmbeddedResource so the shared assembly still carries the model. If the local `.oudep` is absent, download it from the pinned GitHub release URL (HEAD-verified 200) via `tools/extract_game_model.ps1` (property `GameOudepLocalPath`/`GameOudepUrl`/`GameModelLogicalName` + `EnsureGameEmbeddedModel` MSBuild target). Delete the `Models/` directory. Desktop C# smoke verified: deleting the local model reproduces a fresh unpack from the assembly (57.7 MB) — packaging chain is complete.
- Rationale: The app needs the model in the assembly for zero-network installs, but a 57 MB binary doesn't belong in git. Building from the release `.oudep` recovers it deterministically and reproducibly without a checked-in blob, keyed to the pinned game.cpp v0.1.3.
- Alternatives considered (superseded): committing Models/game_medium.gguf into git; AndroidAsset-only packing; runtime download (needs network at first run, rejected).
- Impacted areas: OpenUtauMobile.csproj (`EnsureGameEmbeddedModel` target + properties), `tools/extract_game_model.ps1` (new), Services/Game/GameModelResolver.cs, M5 packaging/APK size (~55 MB in APK).

- Date: 2026-08-24
- Decision: Use `.NET 10 [LibraryImport]` source-generated P/Invoke with explicit `EntryPoint = "game_capi_*"` for the 9 C exports; enable `<AllowUnsafeBlocks>true</AllowUnsafeBlocks>` in the shared project.
- Rationale: LibraryImport is AOT/trim-safe on mobile, and sourcegen needs AllowUnsafeBlocks. Without `EntryPoint`, the generator resolves the native symbol from the C# method name (`Version`→`game_capi_version`), causing EntryPointNotFoundException at runtime — caught by the C# desktop smoke.
- Alternatives considered: Classic DllImport (works but trimming/AOT riskier on Android); no-entry-point naming trick (would obscure intent).
- Impacted areas: GameGgmlNative.cs, OpenUtauMobile/OpenUtauMobile.csproj, future Android interop.

- Date: 2026-08-24
- Decision: Installed user-scoped .NET SDK 10.0.300 (+10.0.400 via channel) to `%USERPROFILE%\.dotnet` using the official dotnet-install script (no admin), because the machine only had .NET 8.0.424/9.0.317 while global.json pins 10.0.300.
- Rationale: Without the matching SDK the shared project (net10.0) cannot compile at all, blocking M3+; the user asked to continue M3. User-scoped install avoids admin and Program Files writes.
- Alternatives considered: Relaxing global.json to accept 9.0.x (breaks the .NET 10 migration decision); changing global.json band to 10.0.4xx (premature — upstream pins 10.0.300).
- Impacted areas: Local toolchain, all future `dotnet build` commands must use `%USERPROFILE%\.dotnet\dotnet.exe` or PATH.

- Date: 2026-08-24
- Decision: Configure all Android ABIs through `native/CMakeUserPresets.json` + `cmake --preset`, with NDK toolchain / ninja / all `-D` (ANDROID_PLATFORM=24, ANDROID_SUPPORT_FLEXIBLE_PAGE_SIZES=OFF, GGML_NATIVE=OFF, GGML_OPENMP=OFF, BUILD_SHARED_LIBS=OFF, GGML_BACKEND_DL=OFF, GAME_GGML_LLAMAFILE=OFF, accelerators OFF) baked into one hidden `android-common` preset; `--fresh` + retry loop around each configure.
- Rationale: PowerShell 5.1 passing `-DCMAKE_TOOLCHAIN_FILE=...` on the cmake command line split/quoted args, producing empty CMAKE_MAKE_PROGRAM and broken configure. The preset route writes every option as cache variables in one validated block, is reproducible, and avoids fragile array/quoting.
- Alternatives considered: Start-Process -ArgumentList array; temporary .bat with quoted args — all more fragile than a checked-in preset.
- Impacted areas: native/CMakeUserPresets.json; build-android-{arm64,arm,x86,x64}; OpenUtauMobile.Android/Libs/<abi>/libgame_ggml_shared.so (4 files); HANDOFF.md.

- Date: 2026-08-24
- Decision: In ggml v0.19 use standard `BUILD_SHARED_LIBS=OFF` (not `GGML_SHARED`, which ggml v0.19 does not define; it was silently ignored). Also disable `GAME_GGML_LLAMAFILE` for all Android ABIs.
- Rationale: `GGML_SHARED=OFF` had no effect — ggml still emitted shared libs, contradicting the static-link plan. Setting the real option `BUILD_SHARED_LIBS=OFF` (plus GGML_BACKEND_DL=OFF) makes ggml static (.a) and merges everything into a single self-contained game_ggml_shared.so whose NEEDED is only libc/libm/libdl. LLAMAFILE must be off for armeabi-v7a (ARMv7 lacks the FP16 NEON intrinsics vld1q_f16/vld1_f16 used by llamafile sgemm.cpp), and off everywhere keeps the 4 ABIs consistent and smaller.
- Alternatives considered: Keep GGML_SHARED=OFF; build shared ggml and ship the extra .so.
- Impacted areas: native/CMakeUserPresets.json; all Android build outputs and the delivered .so; version-consistent reproducible builds.

- Date: 2026-08-22
- Decision: Deep-embed KakaruHayate/game.cpp (GAME ggml inference) into OpenUtauMobile as a `native/` submodule plus a C ABI shim, replacing the ONNX-based GAME path entirely; no plugin/switching framework, no subprocess, every platform ships the CPU backend with GPU-first runtime fallback (Vulkan/Metal/CUDA as compiled).
- Rationale: ONNX Runtime GAME is far too heavy for mobile and effectively infeasible to ship; embedding game.cpp natively is the only viable path. The per-platform accelerator is chosen at build time (Metal on Apple, Vulkan/CUDA on desktop, Vulkan on Android) and `init_best_backend()` provides GPU→CPU fallback automatically, so a single C ABI (`game_capi`) is all the .NET layer needs. Keep the user's fork as the build baseline and git-submodule the upstream game.cpp pinned to release v0.1.3.
- Alternatives considered: ONNX Runtime Electron/CLI subprocess (.oudep Executable) — rejected for iOS sandbox and mobile memory; plugin-switching framework — rejected as over-engineering; using only prebuilt release .oudep binaries — no long-term mobile control.
- Impacted areas: New `native/` tree (game.cpp submodule + shim + CMake host); C# `GameGgml` integration code placed in the Mobile layer (not `OpenUtau.Core`); future Android/Windows/Linux/macOS/iOS ship artifacts; CI additions.
- Date: 2026-08-22
- Decision: Give each rendered note pitch-bend curve its own finalized `StreamGeometry` instead of submitting the shared mutable `Points` and `PolylineGeometry` caches.
- Rationale: Every `RenderPitchBend` call cleared and repopulated the same point collection retained by earlier drawing commands, so the last visible note replaced the curves submitted for all preceding notes.
- Alternatives considered: Clone the shared point collection before every draw; aggregate every visible note curve in the caller; submit interpolated curve segments with individual `DrawLine` calls.
- Impacted areas: Anchor-mode pitch-bend curve rendering in `NotesCanvas`; anchor handle geometry and pitch interpolation remain unchanged.

- Date: 2026-08-21
- Decision: On Android, report the app's current resident memory from `RssAnon` in `/proc/self/status`, falling back to the shared `Process.WorkingSet64` path when procfs data is unavailable; stop using `ActivityManager.getProcessMemoryInfo()` and `TotalPss` for the one-second monitor.
- Rationale: Android Q and later significantly rate-limit `getProcessMemoryInfo()` and silently return cached samples when it is polled too frequently, which made the displayed app-memory value appear frozen. Reading the current process's own procfs status avoids that API cache, remains available across the project's Android 7+ target range, and has a compatible shared fallback.
- Alternatives considered: Increase PSS polling to several minutes; display cached PSS beside a separate RSS value; use only `Debug.getNativeHeapAllocatedSize()`, which excludes managed, graphics, code, and other resident mappings.
- Impacted areas: Android app-memory sampling and the meaning of the displayed app-memory metric, which is now resident set size rather than proportional set size.

- Date: 2026-08-21
- Decision: Implement the performance monitor as a shared sampling and presentation service with platform metric providers registered through `ServiceHub`; persist its enabled state in the OPUM-specific preferences region. Keep frame rate behind `IFrameRateProvider` without an implementation in this phase.
- Rationale: Process/GC collection and overlay presentation are portable, while system memory and CPU semantics require platform APIs. Avalonia 12.1 exposes a built-in renderer FPS debug overlay, but its public diagnostics surface only configures the overlay and does not expose the numeric FPS value. `IRenderTimer` is marked private API and measures render-loop ticks rather than guaranteed presentation, so binding production code to it now would create unstable semantics.
- Alternatives considered: Put all metric APIs in the shared project with runtime OS checks; enable Avalonia's built-in debug overlay directly; consume `IRenderTimer.Tick`; estimate FPS with `DispatcherTimer`.
- Impacted areas: Shared performance services and overlay, the always-visible global settings entry, OPUM preferences, Windows global memory/CPU sampling, Android PSS/global memory/CPU sampling, and the future FPS integration boundary.

- Date: 2026-08-21
- Decision: Supersede the earlier Android main-view-factory reapply as the startup fix: refresh follow-system colors from `MainView.OnAttachedToVisualTree`, subscribe to `IPlatformSettings.ColorValuesChanged`, and skip regeneration when the resolved seed and actual variant have not changed.
- Rationale: On Android 12, both application initialization and `IActivityApplicationLifetime.MainViewFactory` run before Avalonia exposes the Material You palette through `Application.PlatformSettings`. Theme resolution therefore falls back to the persisted `#66CCFF` seed until a later page, such as Settings, resolves it after visual-tree attachment.
- Alternatives considered: Reapply from the splash screen after a fixed delay; keep refreshing only when Settings opens; read Android dynamic-color resources directly in the shared UI project.
- Impacted areas: Application theme lifecycle and Android 12+ follow-system theme-color startup behavior.

- Date: 2026-08-21
- Decision: Resolve and apply the persisted theme seed through one `ThemeManagerV2.ApplyConfiguredTheme` entry point during both application startup and settings initialization, and reapply it when Android creates its activity-backed main view.
- Rationale: Startup previously applied the manager's red default seed before separately resolving preferences with the requested theme variant, while settings resolved preferences with the actual variant after the UI existed. Android creates its main view after application initialization, so replaying the same centralized operation at that boundary prevents the default seed from surviving until settings is opened.
- Alternatives considered: Duplicate the settings logic in `App`; delay every platform's theme setup until the splash screen; add Android-only color parsing outside the shared theme manager.
- Impacted areas: Shared runtime theme initialization, application startup, settings appearance initialization, and Android activity-backed main-view creation.

- Date: 2026-08-21
- Decision: Materialize singer discovery once for both manager indexes, and preload only singer avatar bytes during splash initialization instead of calling full `Reload()` for every discovered singer; retain full lazy loading through `EnsureLoaded()` when a singer is attached to a track.
- Rationale: The lazy discovery sequence previously scanned the singer directory tree twice and created different objects for the ID index and UI groups. Classic singer reload then recursively parsed all OTO files, built phoneme maps, validated samples, and started file watchers for every installed singer even though the initial UI only needs metadata and avatar bytes.
- Alternatives considered: Keep eager full reload and parallelize it; decode avatars lazily on the UI thread; skip avatar initialization.
- Impacted areas: Singer discovery/index identity, the singer avatar-loading contract in Core, and the Mobile splash initialization path. VOICEVOX reuses an existing generated icon when available and retains its reload fallback when engine-provided singer information is still required.

- Date: 2026-08-20
- Decision: Build final-pitch rendering as one per-frame `StreamGeometry` with a separate figure for each visible render phrase, and never mutate that geometry after submitting it to the drawing context.
- Rationale: Avalonia drawing commands retain mutable geometry resources rather than snapshotting a shared `Points` collection. Reusing and clearing the same `Points`/`PolylineGeometry` after `DrawGeometry` either joins independent phrases, changes already-submitted drawing, or leaves the retained geometry empty.
- Alternatives considered: Clear the shared point collection after every phrase; allocate a separate `Points` and `PolylineGeometry` for every phrase; submit every pitch segment as a separate `DrawLine` command.
- Impacted areas: Final pitch-line rendering in `NotesCanvas`; one geometry allocation is made per visible render pass instead of retaining mutable geometry across frames.

- Date: 2026-08-20
- Decision: Intercept the shared desktop `MainWindow` closing event and route editor close requests through the same save/discard/cancel decision used by back navigation, then allow exactly one confirmed close.
- Rationale: Native title-bar close requests otherwise bypass editor navigation and can terminate the process with unsaved project changes. A shared window hook covers Windows and other classic desktop hosts while keeping project-state decisions in the view models.
- Alternatives considered: Add separate platform hooks in the Windows and Linux hosts; always convert window close into back navigation; duplicate the confirmation UI in the window code-behind.
- Impacted areas: Desktop window lifetime, main navigation coordination, and editor exit confirmation.

- Date: 2026-08-19
- Decision: Expose batch editing from the piano-roll contextual action capsule with a `ListChecks` icon, and present operations in a responsive wide-preset popup organized into Lyrics, Notes, and Reset tabs. Register operations through a strongly typed static descriptor catalog with lazy factories instead of reflection.
- Rationale: Batch edits operate on the active voice part and its selected notes, so the piano-roll context is the correct semantic level. Explicit descriptors keep ordering, localization, parameters, confirmation, AOT trimming, and future availability rules deterministic on mobile. A tabbed single-column list remains readable on narrow screens while the existing wide popup preset uses additional desktop/tablet width.
- Alternatives considered: Place the entry in the project-level More popup; add batch editing as an edit mode; discover `BatchEdit` implementations through reflection; display equal-width action cards in two columns.
- Impacted areas: Piano-roll contextual actions, batch-edit popup and view models, localization resources, and Windows/mobile responsive popup layout.

- Date: 2026-08-19
- Decision: Keep the batch-edit selection popup free of execution state. It closes with an immutable execution request before a shared modal `LoadingPopup` starts. Completion and cancellation are reported through Toast. Loading ignores back/outside-close requests; a cancel button is exposed only for descriptors whose Core implementation explicitly consumes `CancellationToken`.
- Rationale: Running work inside the selection popup left progress non-modal, allowed competing interactions, and coupled task resources to a closable view model. Serial popup handoff makes DialogHost release the selection popup before work starts and gives one owner to progress, cancellation, and cleanup.
- Alternatives considered: Disable controls inside the batch popup while retaining inline progress; infer cancellation support from `BatchEdit.IsAsync`; expose cancellation for every operation.
- Impacted areas: Batch-edit execution lifecycle, shared loading popup cancellation support, completion feedback, and two cancellation-aware rendered-data operations.

- Date: 2026-05-13
- Decision: Initialize agent context and workflow documents for OpenUtau Mobile.
- Rationale: Provide a consistent, searchable context for AI agents and contributors.
- Alternatives considered: None.
- Impacted areas: `.agent` documentation only.

- Date: 2026-05-13
- Decision: Restructure context files into modular `.agent/context/` subfolder.
- Rationale: Reduce context loading overhead by splitting monolithic PROJECT_CONTEXT.md into focused files. Agents can now load only what they need (e.g., PLATFORMS.ctx.md only when working on platform-specific code). Improves scalability for future additions.
- Alternatives considered: Keep everything in one file; create separate platform files inside .agent root.
- Impacted areas: `.agent/` directory structure; README, AGENT_WORKFLOW.md updated to reflect new paths.

- Date: 2026-07-22
- Decision: Migrate the full solution from .NET 9 and Avalonia 11.3.9 to .NET 10 and Avalonia 12.1.0, pinning the SDK feature band with `global.json`.
- Rationale: The development machine has .NET 10 mobile workloads, while the unpinned .NET 10 SDK cannot build the existing `net9.0-android` target. Avalonia 12 supports Android and iOS only on .NET 10 and its Android package targets API 36.
- Alternatives considered: Install and pin a separate .NET 9 Android workload while remaining on Avalonia 11; target Android API 35 with Avalonia 12.
- Impacted areas: All project target frameworks, Avalonia and ReactiveUI package references, platform bootstrap code, Android application lifetime handling, Android SDK Platform 36 requirement.

- Date: 2026-07-22
- Decision: Use a user-scoped Android SDK at `%LOCALAPPDATA%/Android/Sdk` and set `ANDROID_HOME` and `ANDROID_SDK_ROOT` to that location.
- Rationale: Installing API 36 into the existing system-wide SDK under Program Files required administrator access. The user-scoped SDK supports IDE and CLI deployment without elevation.
- Alternatives considered: Run the SDK installer as administrator against the system-wide SDK; keep passing `AndroidSdkDirectory` on every command.
- Impacted areas: Local development environment only; Android build and deployment documentation.

- Date: 2026-07-22
- Decision: Keep IconPacks 2.0 icon data and controls, but replace its packaged Avalonia 11 XAML styles with a local Avalonia 12-compatible control theme.
- Rationale: IconPacks 2.0 still depends on Avalonia 11.0.13. Its compiled XAML calls a removed `DynamicResourceExtension.ProvideValue` signature, causing the Splash-to-Home transition to fail with a black screen.
- Alternatives considered: Remove all icons; migrate every icon usage to another library; remain on Avalonia 11.
- Impacted areas: Application styles and all PhosphorIcons/Fontaudio controls.

- Date: 2026-07-22
- Decision: Let Avalonia 12's `IInsetsManager` control Android system-bar visibility, while the native activity only selects transient-bar-by-swipe behavior.
- Rationale: Avalonia already owns Android safe-area and system-bar state. Keeping visibility in one layer avoids duplicate state management, although this was not the cause of the Android-x86 click failure.
- Alternatives considered: Continue hiding system bars entirely through native Android APIs; disable immersive mode; manually compensate pointer coordinates.
- Impacted areas: Android immersive-mode handling and the shared `MainView` attachment lifecycle.

- Date: 2026-07-22
- Decision: Normalize Android `Mouse` Down/Move/Up sequences to explicit Finger/Touchscreen events at the activity boundary while preserving non-primary mouse buttons.
- Rationale: The Android-x86 emulator changes host clicks to `MotionEventToolType.Mouse` Down/Up after returning to immersive mode. Avalonia's Android motion helper ignores mouse Down/Up and expects ButtonPress/ButtonRelease, so the events reach the application window but never become Avalonia pointer events.
- Alternatives considered: Disable immersive mode in emulators; patch and locally build Avalonia.Android; treat injected touchscreen events as sufficient validation.
- Impacted areas: Android emulator and mouse-primary-button input dispatch; physical touchscreen input is unchanged.

- Date: 2026-07-22
- Decision: Supersede the Avalonia `IInsetsManager` and mouse-event normalization approaches; manage immersive mode only in the Android activity using the official AndroidX `WindowInsetsControllerCompat` hide-and-transient-bars pattern.
- Rationale: `IInsetsManager.IsSystemBarVisible = false` makes Avalonia 12 content non-interactive in this app. The activity's Android 11+ branch previously set only the transient-bar behavior and never hid the bars. Input device types must remain distinct for mouse, touch, and pen handling.
- Alternatives considered: Continue using Avalonia's `IInsetsManager`; normalize mouse input as touch; maintain separate modern and legacy native implementations.
- Impacted areas: Android system-bar visibility and the shared `MainView` attachment lifecycle; input event dispatch is unchanged.

- Date: 2026-07-22
- Decision: Embed managed assemblies in Android Debug APKs instead of using Fast Deployment.
- Rationale: The 151 MB Fast Deployment override directory caused managed assembly loading to exceed Android's startup deadline. The system recorded a `failed to complete startup` ANR followed by a focus-event ANR even though the app eventually became usable. Embedded-assembly cold starts completed in 5.1-5.7 seconds without new ANRs.
- Alternatives considered: Keep Fast Deployment and ignore debug-only ANRs; move application initialization off the UI thread without evidence that it caused these ANRs.
- Impacted areas: Android Debug APK size, build/deployment time, and cold-start reliability; Release builds are unchanged.

- Date: 2026-07-23
- Decision: Add viewport-right pruning to PartsCanvas note preview iteration.
- Rationale: UVoicePart notes are position-sorted, so stopping when a note start exceeds the viewport right edge avoids scanning the remainder of long parts during each render frame.
- Alternatives considered: Continue scanning to the part boundary; binary-search the note set (not supported by the existing collection API).
- Impacted areas: `OpenUtauMobile/Controls/PartsCanvas.cs` note preview rendering performance.

- Date: 2026-07-23
- Decision: Compose CI versions as `MainVersion + Suffix + "." + GITHUB_RUN_NUMBER`, use `10000 + GITHUB_RUN_NUMBER` for Android version codes, and give Debug Android builds a `.debug` application ID suffix.
- Rationale: GitHub run numbers provide monotonic CI build identifiers, while distinct Debug and Release package IDs allow side-by-side installation and avoid Android downgrade conflicts.
- Alternatives considered: Commit a manually incremented build number; reuse the Release application ID for local Debug builds.
- Impacted areas: Shared build properties, Android packaging, Android development workflow, and About-page build metadata.

- Date: 2026-07-24
- Decision: Resolve legacy storage permissions through the live Android activity and opt Android 10 into the legacy external-storage model.
- Rationale: `Application.Context` is never an `Activity`, so Android 10 and earlier could neither check dangerous permissions nor open the runtime permission dialog. The internal file picker also requires raw-path access that Android 10 otherwise restricts under scoped storage.
- Alternatives considered: Launch the system Storage Access Framework picker; request permissions through an application context.
- Impacted areas: Android 10 and earlier storage permission checks, runtime permission requests, and raw external-storage access.

- Date: 2026-07-25
- Decision: Give the dependency manager a page-scoped MD3 component token set and render its tabs with a compact custom state-layer template.
- Rationale: The default desktop tab indicator, unconstrained count badges, and filled hover treatment produced inconsistent geometry and excessive contrast. Page-scoped tokens keep responsive spacing, badge shape, state opacity, and the short pill indicator consistent across generated color themes.
- Alternatives considered: Modify the global Avalonia TabItem theme; continue using per-control literal values.
- Impacted areas: Dependency manager layout and styles; shared static theme tokens.

- Date: 2026-07-27
- Decision: Supersede the dependency-manager-specific tab header template with a global MD3 secondary TabItem style.
- Rationale: Secondary tabs are a shared component pattern. A global style gives every TabItem the same 48dp container, Title Small label, state layer, semantic colors, and full-width 2dp active indicator while allowing each view to supply arbitrary header content.
- Alternatives considered: Keep the dependency manager's page-scoped short pill indicator; duplicate the secondary-tab template in each view.
- Impacted areas: Global OpenUtau Mobile TabItem headers; dependency manager tab header markup and tokens.

- Date: 2026-07-29
- Decision: Implement dependency installation from local files entirely in the Mobile layer without modifying the upstream-derived Core package manager.
- Rationale: The existing Core API already supports file installation, while keeping this feature's UI state and error presentation outside Core avoids increasing the cost of future upstream synchronization.
- Alternatives considered: Add transactional extraction, package metadata results, or typed installation errors to `OpenUtau.Core`; duplicate package parsing in a Mobile adapter.
- Impacted areas: Dependency manager ViewModel, view state, and localization resources. Core installation behavior remains unchanged.

- Date: 2026-07-29
- Decision: Run non-cancellable background UI operations through a reusable LoadingPopup service with determinate and indeterminate progress modes.
- Rationale: Centralizing first-frame yielding, UI-thread progress updates, and guaranteed dialog closure prevents operation-specific loading overlays from racing with completion or subsequent error dialogs.
- Alternatives considered: Keep page-local busy indicators; open and close DialogHost directly in each ViewModel; add loading notifications to the upstream-derived Core.
- Impacted areas: Shared popup controls and services; dependency installation now uses the indeterminate loading mode.

- Date: 2026-08-19
- Decision: Render each batch-edit execution action with a 48dp touch target containing a 40dp circular filled-tonal container and 24dp Play icon, and explicitly pair every new container color with its matching MD3 on-color.
- Rationale: Separating the 48dp hit target from the 40dp visual container preserves the MD3 icon-button geometry while remaining easy to acquire on touch screens. Semantic `SecondaryContainer`/`OnSecondaryContainer`, `PrimaryContainer`/`OnPrimaryContainer`, and surface/on-surface pairs remain legible across generated light and dark themes.
- Alternatives considered: Keep the 72x40 text button; use a 40dp visual target; use primary colors for every row action.
- Impacted areas: Batch-edit popup item actions, scope banner, item icon foreground, and batch-edit theme tokens.

- Date: 2026-08-20
- Decision: Persist batch-edit pins as category-keyed lists of stable catalog IDs, with the most recently pinned ID inserted first; render pinned items in a dedicated group above the remaining catalog-order items in each tab.
- Rationale: Stable IDs survive localization and title changes, per-category lists prevent cross-tab ordering leakage, and moving rather than duplicating an item keeps each operation available exactly once. Invalid or duplicate stored IDs are removed when the menu opens.
- Alternatives considered: Persist localized titles; store one global pin list; duplicate pinned operations while leaving them in the regular list; keep pins session-only.
- Impacted areas: Core preferences serialization, batch-edit ViewModels and popup layout, batch-edit semantic tokens/styles, and localization resources.

- Date: 2026-08-20
- Decision: Execute synchronous batch-edit orchestration on a thread-pool thread while synchronously marshalling `DocManager` command execution and undo-group boundaries back to the main thread through a thread-scoped dispatch mode.
- Rationale: A bare `Task.Run(BatchEdit.Run)` would let `StartUndoGroup` and `EndUndoGroup` race ahead of asynchronously posted commands, dropping commands or corrupting undo state. The scoped bridge keeps expensive operation traversal off the UI thread, preserves command order and exception propagation, and leaves unrelated background command dispatch unchanged.
- Alternatives considered: Keep the full operation on the UI thread; use a bare `Task.Run`; globally make every background `ExecuteCmd` blocking; refactor every Core batch edit into prepare/commit phases.
- Impacted areas: `DocManager` main-thread dispatch and the synchronous batch-edit execution path in `PianoRollViewModel`.

- Date: 2026-08-20
- Decision: Capture the registered toast consumer before posting UI work and validate that the same consumer remains registered before invocation.
- Rationale: Window shutdown detaches `MainView` and unregisters the toast consumer after a save notification can already have posted a dispatcher callback. Reading the mutable callback inside that delayed callback creates a check-then-use race and a null dereference.
- Alternatives considered: Clear pending toast messages during shutdown; keep invoking a captured consumer after its view detaches; move toast lifetime management into the Windows host.
- Impacted areas: Shared toast callback registration and shutdown behavior on all application hosts.

