# Reusable UI probes (Windows host)

Use when a UI change benefits from offscreen layout, theme, binding or persistence
verification. This is an optional disposable console harness, not a new test suite
or a requirement for every UI edit. Read current source before adapting a scenario.

## Reuse or generate

1. Reuse a known matching probe if its sources and dependencies still match the task.
   Do not assume historical `artifacts/` paths exist in another checkout.
2. Otherwise generate from the self-contained [assets](../assets/ui-probe/) using
   [new-ui-probe.ps1](../scripts/new-ui-probe.ps1). The generator queries evaluated
   `TargetFramework` and `AvaloniaVersion`; it does not hardcode a machine cache path.
3. Keep host plumbing in `ProbeHost.cs` and task-specific construction/assertions in
   `Scenarios.cs`. The shipped magnifier/settings examples reflect current source,
   not contracts for unrelated features. Adapt or remove them in the generated copy.

From the repository root, in PowerShell:

```powershell
& .agents/skills/opum-ui-design/scripts/new-ui-probe.ps1 -Name editor-review
$env:AVALONIA_TELEMETRY_OPTOUT = '1'
dotnet run --project artifacts/ui-probes/editor-review/Probe.csproj -- basic
# After the first successful restore, run each scenario in a separate process:
dotnet run --project artifacts/ui-probes/editor-review/Probe.csproj --no-restore -- magnifier
dotnet run --project artifacts/ui-probes/editor-review/Probe.csproj --no-restore -- settings
```

The generator accepts `-RepositoryRoot` and an absolute `-OutputDirectory`, but output
must be a new child directory under `<repo>/artifacts/ui-probes/`. Existing directories
and linked ancestors are rejected before writing. Reuse by running the existing project,
not regenerating over it. A failed restore does not imply a corrupt template; inspect the
actual exit status and use the environment's network approval procedure when needed.
`--no-restore` is for an already restored project. Do not suppress package audits globally.

## Bootstrap that was exercised

```csharp
AppBuilder.Configure<Application>()
    .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
    .UseSkia()
    .UseReactiveUI(_ => { })
    .SetupWithoutStarting();
```

Keep `using ReactiveUI.Avalonia;` for the extension. Current `UseReactiveUI` requires
the builder callback; omitting initialization breaks reactive ViewModels and omitting
the callback fails compilation. `PathManager` is in `OpenUtau.Core`, not `.Util`.
Visual traversal extensions require `Avalonia.VisualTree`. Before guessing a changed
API, inspect the current app entrypoint and restored package XML documentation.

Load OPUM's `OpenUtauMobileTheme.axaml` and the required language after starting the
minimal Application. Do not call the production App startup merely to get its styles;
that can initialize audio, navigation and background services. Do not substitute Fluent.
Use scoped `GetResourceObservable` bindings for live brushes/colors; dispose them.

## Isolation before ViewModels

The template validates the runtime binary, data, cache and preferences paths as children
of the generated project before any Preferences/ViewModel initialization. It rejects
installed-mode markers and linked paths. Canonical full paths plus a trailing directory
separator prevent sibling-prefix matches. Probe preferences live in its own build output.

Windows portable `PathManager` behavior is the verified baseline. Other host platforms
must review their initialization side effects and establish isolation before enabling the
template there; do not remove the platform guard as a shortcut. Fresh processes isolate
Avalonia/ReactiveUI singletons. Repeated settings runs explicitly seed disposable data.

`SettingsViewModel(null!)` is valid only for this example's display, bindings and settings;
it is not a generic ViewModel construction strategy. Do not invoke its navigation commands.
For another feature, inspect constructor dependencies and supply only the services needed.

## Layout and capture

- Mount controls in a finite Window; update layout, drain queued UI work, update again.
  For async loading/animations, wait for a bounded observable completion condition; queue
  draining alone does not demonstrate all background work has completed.
  `WaitUntil` pumps headless render ticks with a timeout; the settings example waits for
  the navigation width transition to reach its ViewModel target before capturing.
- Explicitly `Measure/Arrange` an unattached VisualBrush source.
- Screenshot size is `ceil(logical size * dpi / 96)`. Current PNG save uses
  `PngBitmapEncoderOptions.Default`, not the obsolete optional-quality overload.
- Locate a stable name, automation resource key or feature identity. For scrolling,
  reveal the whole card, calculate its offset, then assert it fits the viewport; do not
  hardcode an offset or a translated title. For a card taller than the viewport, capture
  intentional multiple sections instead of claiming full visibility.
- Include shadow padding in the captured parent. Reopen generated PNGs with the image
  viewer and inspect the target, clipping, theme, wrapping and relevant states. The helper's
  PNG decode/size check detects file issues, not visual correctness.
- Close windows and dispose ViewModels/subscriptions even on failure. Use a timeout when
  adding asynchronous scenarios; do not leave an invisible process running indefinitely.

## Evidence and bounded troubleshooting

Record the exact command, inputs, exit code, meaningful assertion output and screenshot
paths. Keep these evidence levels separate:

| Check | Demonstrates | Does not demonstrate |
| --- | --- | --- |
| Build | Current APIs/XAML compile | Appearance or interaction |
| Offscreen screenshot | Observed layout/rendering in that scenario | Physical device feel |
| Value/binding/persistence assertion | The executed data path | Pointer/keyboard event routing |
| Injected input | The specific input route exercised | All gestures or platform behavior |

If initialization fails, fix the missing namespace/builder/service in the host once;
do not patch production code to accommodate a harness. If a resource is missing, inspect
its actual theme/language owner. If a screenshot misses its target, fix layout/scrolling
before claiming visual success. Report unobserved device/gesture cases explicitly.
Stop after the task's relevant checks pass; avoid broadening into a UI test framework.
