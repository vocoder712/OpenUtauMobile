# Decisions Log

Record meaningful technical decisions here. Use one entry per decision.

## Template

- Date:
- Decision:
- Rationale:
- Alternatives considered:
- Impacted areas:

## Entries

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

