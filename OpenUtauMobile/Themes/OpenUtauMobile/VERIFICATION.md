# Independent theme verification — 2026-09-11

## Source review

- Pinned Avalonia 12.1.0 source; 85 imported XAML files, including a complete
  79-control-dictionary include graph. No missing control includes.
- Reviewed the entry-point/palette ordering, button-family inheritance, nested
  pseudoclass selectors, required parts and bindings, editable ComboBox, TextBox
  IME/selection/clear/reveal/context-flyout behavior, check/radio/switch states,
  slider/scrolling templates, all tab placements and progress animations.
- All application AXAML files parse. All 122 keyed control themes, including
  private helpers, resolve in a real Avalonia runtime. This forces lazy resource
  construction that a successful build alone does not exercise.
- No production Fluent theme assembly reference or resource URI remains. Windows
  output contains no Fluent assembly; runtime reports `FLUENT_LOADED=False`.
- Core and Plugin source code is unchanged. No unit tests were created or run.

## Runtime diagnostic

Ignored, local diagnostic project: `.tmp/theme-probe/theme-probe.csproj`.
It uses Avalonia.Headless and Avalonia.Skia 12.1.0, not a unit-test framework.
The baseline project references the original pre-edit UI assembly and loads the
original Fluent + OPUM layers. Both runs initialize the semantic resource bridge.

```powershell
$env:AVALONIA_TELEMETRY_OPTOUT='1'
$env:THEME_BASELINE='1'
dotnet run --project .tmp/theme-baseline/baseline.csproj
# Exit 0; .tmp/theme-probe/baseline-corrected.log

$env:THEME_BASELINE='0'
dotnet run --project .tmp/theme-probe/theme-probe.csproj -p:OutputPath=bin/ThemeReview/
# Exit 0; .tmp/theme-probe/runtime.log
```

Representative literal outputs (brush string representations omit opacity):

```text
BUTTON Light Primary pointerover cursor=Hand root=#ff65558f/White presenter=Black/Black layer= opacity= focus=
BUTTON Light Primary disabled cursor=Hand root=#ff65558f/White presenter=#33000000/#66000000 layer= opacity= focus=
```

After migration:

```text
RESOLVED_THEMES=122
BUTTON Light Primary pointerover cursor=Hand root=#ff65558f/White presenter=/White layer=0.08 opacity=1 focus=False
BUTTON Light Primary pressed cursor=Hand root=#ff65558f/White presenter=/White layer=0.12 opacity=1 focus=False
BUTTON Light Primary disabled cursor=Arrow root=#ff65558f/White presenter=/White layer=0 opacity=0.38 focus=False
CHOICE Dark selected=True background=#ff4d3d75 foreground=#ffe9ddff sharedRoot=True
CHOICE Dark disabled cursor=Arrow opacity=0.38
LIVE seed=#6750A4 button=#ffcfbdfe light=#fffdf8fd/#ff1c1b1e dark=#ff141316/#ffe6e1e6
LIVE seed=#006D77 button=#ff81d3de light=#fff8fafa/#ff191c1d dark=#ff101414/#ffe0e3e3
POPUP combo=True firstItem=ComboBoxItem attached=True
INPUT focus=True selection=0..9 clearButtons=1
INPUT errorBorder=#ffffb4ab
FLUENT_LOADED=False
```

The diagnostic inspects seven button roles × five states × two variants. It also
lays out 33 control configurations in each variant, including mixed checkboxes,
both slider/progress orientations, editable and non-editable ComboBox, numeric
input, lists/tree/tabs, scrolling, date/time, menus and the custom radio-card theme.
Pseudoclasses are explicitly driven for repeatable visual-state inspection; this
does not claim that every device input path has been exercised.

Rendered Light/Dark galleries were opened and visually reviewed:
`.tmp/theme-probe/gallery-Light.png` and `gallery-Dark.png`. This caught missing
initial Default-variant color resources and a clipped focus outline; both were
fixed before the final render. The diagnostic also caught a template-selector
validation error missed by compilation, fixed by gating the owning control rather
than placing a negation after a template traversal.

## Build

```powershell
$env:AVALONIA_TELEMETRY_OPTOUT='1'
dotnet build OpenUtauMobile.Windows/OpenUtauMobile.Windows.csproj --nologo -v:minimal -p:OutputPath=bin/ThemeReview/
git diff --check
```

Both exited 0. Final Windows build: four existing NU1510 warnings, zero errors.
Isolated output avoids replacing assemblies used by a running application.
Full build output: `.tmp/theme-probe/windows-build.log`.

## Remaining acceptance

No claim of exhaustive platform validation: Android/touch/stylus, live IME,
screen readers, physical keyboard traversal, populated business pages, localized
labels and every uncommon control interaction still need manual acceptance.
The retained upstream templates preserve those contracts; resource resolution and
layout diagnostics are narrower evidence than end-to-end behavior.
