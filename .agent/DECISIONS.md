# Decisions Log

Record meaningful technical decisions here. Use one entry per decision.

## Template

- Date:
- Decision:
- Rationale:
- Alternatives considered:
- Impacted areas:

## Entries

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

