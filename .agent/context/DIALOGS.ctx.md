# Dialog component contract

## Single owner

`OpenUtauMobile/Themes/OpenUtauMobile/Styles/Controls/PopupCommon.axaml` owns
dialog surfaces, headers, close icons, footer spacing, action typography,
colors, geometry and interaction states. `Controls/DialogShell.cs` supplies
content slots, logical parenting and the explicitly grouped equal-width action-row
layout algorithm; it contains no design values or per-dialog breakpoints.

Do not add per-dialog chrome tokens, copy a title bar, draw another X, or
override header/footer button geometry in a view. Reuse existing foundation
tokens inside the shared component. Business-content layout and viewport
size constraints remain the responsibility of the view and popup base class.

## New dialog

Keep the existing `PopupDialogControl` / `ImportDialogControl` base class and
ViewModel lifecycle. Put one `DialogShell` inside the AXAML UserControl:

```xml
<dialogs:DialogShell Header="{Binding Title}" CloseCommand="{Binding CancelCommand}">
    <dialogs:DialogShell.Footer>
        <dialogs:DialogActionRow>
            <Button Content="{DynamicResource Common.Cancel}"
                    Command="{Binding CancelCommand}" />
            <Button Classes="DialogPrimary"
                    Content="{DynamicResource Common.Confirm}"
                    Command="{Binding ConfirmCommand}" />
        </dialogs:DialogActionRow>
    </dialogs:DialogShell.Footer>
    <StackPanel Classes="DialogBody">
        <!-- 业务内容 -->
    </StackPanel>
</dialogs:DialogShell>
```

Declare `xmlns:dialogs="clr-namespace:OpenUtauMobile.Controls"`.

- Omit `CloseCommand` for a deliberately non-dismissible operation; do not
  invent a cancellation path. Omit `Footer` for immediate-selection dialogs.
- Footer buttons inherit the shared action style automatically. Specify only
  command, label, enabled/visible bindings and semantic emphasis:
  `DialogPrimary` or `DialogDestructive` (destructive takes precedence).
- `DialogAction` explicitly applies the same style to dynamically generated
  actions or actions outside `DialogActionRow`. Do not use legacy `Primary`,
  `Secondary`, `PrimaryBtn`, `SecondaryBtn`, `AccentBtn` or `OptionAction`
  for new dialog footer actions.
- Use `DialogActionRows` with explicit `DialogActionRow` children for multiple
  rows. One `DialogActionRow` is enough for a single-row footer. Each row's
  visible actions divide its full width equally. Width never changes row groups.
- Footer stays separate from scrollable business content. Put conditional
  footer visibility on its root control so no empty footer inset remains.
- For a composite header, use `DialogShell.Header` with `PopupTitle` and
  optional `DialogSubtitle`; supply text and visibility only, not typography.
- `DialogBody` supplies a shared content margin; `DialogBodyBorder` supplies
  the equivalent padding for a Border. Do not nest both for the same inset.
- Existing named inputs and event handlers stay in the view's namescope.
  Footer controls retain their ViewModel through the shell's logical tree.

## Explicit mobile footer rows

Row membership is a business decision, not a responsive-design heuristic.
`DialogActionRows` stacks rows vertically; each `DialogActionRow` distributes
its own visible buttons equally over the full available width. Different rows
may contain different numbers of buttons. A single-button row spans the width.

- No automatic button wrapping, merging or repartitioning on resize.
- Button **text** still wraps, and each row grows to its tallest button.
- Hidden actions do not reserve cells; disabled actions remain in place.
- An empty/all-hidden row measures to zero height. No extra row spacing is
  added: button spacing remains owned by the shared action style.
- Do not set per-view `Width`, `Height`, `HorizontalAlignment`, `Orientation`,
  or margins to control this layout. Use explicit row containers instead.
- Keep the footer outside the body ScrollViewer and give it a finite width.
  Do not put it inside a horizontal ScrollViewer/StackPanel.
- `DialogActions` remains a compatibility alias for **one** `DialogActionRow`.
  Existing dialogs keep their declared order, but no longer auto-split by width.
  Use the explicit names for new code; never nest row panels directly inside
  another row panel.

### Static AXAML: two buttons, then one full-width button

```xml
<dialogs:DialogShell.Footer>
    <dialogs:DialogActionRows>
        <dialogs:DialogActionRow>
            <Button Content="{DynamicResource Common.Cancel}"
                    Command="{Binding CancelCommand}" />
            <Button Content="{DynamicResource Common.Next}"
                    Command="{Binding NextCommand}" />
        </dialogs:DialogActionRow>
        <dialogs:DialogActionRow>
            <Button Classes="DialogPrimary"
                    Content="{DynamicResource Common.Confirm}"
                    Command="{Binding ConfirmCommand}" />
        </dialogs:DialogActionRow>
    </dialogs:DialogActionRows>
</dialogs:DialogShell.Footer>
```

The first row always stays two columns, even on a narrow phone. The second
row always stays one full-width button. To hide a complete row, bind its
`IsVisible`; to hide the whole footer (including its padding), bind visibility
on the outer `DialogActionRows`.

### Dynamic options: pass a two-dimensional list

`OptionConfirmPopupViewModel` accepts
`IEnumerable<IEnumerable<OptionConfirmOption>> optionRows`. The outer list
specifies rows; each inner list specifies that row's buttons:

```csharp
OptionConfirmOption[][] rows =
[
    [new("取消", "cancel"), new("跳过", "skip")],
    [new("确认", "confirm", isPrimary: true)],
];
OptionConfirmPopupViewModel popup = new("标题", "说明", rows);
```

Production callers should use localized labels. Selecting a button still
returns its `Value`; closing/back still returns null. The existing one-dimensional
`IEnumerable<OptionConfirmOption>` constructor remains supported and produces
one row. Empty rows are omitted; an entirely empty option set or null rows/items
are rejected. Input collections are snapshotted as read-only rows.

The working AXAML is `Controls/OptionConfirmPopup.axaml`:

1. Outer ItemsControl binds `OptionRows`; its ItemsPanel is `DialogActionRows`.
2. A typed `OptionConfirmRow` template contains another ItemsControl binding
   `Options`; its ItemsPanel is `DialogActionRow`.
3. The typed button template binds Label, the original SelectOptionCommand,
   CommandParameter and semantic classes. Shared ContentPresenter styles stretch
   generated content, just like directly declared buttons.

For other ViewModels, use the same nested ItemsControl pattern with typed row
models. No new style tokens, copied templates or custom button geometry are needed.

## Migrated inventory (2026-09-09)

BatchEdit, BulkLyricEdit, EditorMore, ErrorDialog, ExitEditorConfirm,
ExportAudio, FilePicker, ImportTracks, Loading, LyricEdit, MultiFilePicker,
OptionConfirm, PhonemeEdit, PhonemizerPicker, ProjectInfoEdit, RendererPicker,
SetupWizard, SingerPicker, TrackColorPicker, TrackRename, VoiceColorMapping,
and ThemeColorPickerDialog: all 22 AXAML dialogs use DialogShell.

MainView's DialogHost remains the transparent modal host. Context menus,
ComboBox dropdowns, toast and performance overlays are not modal dialogs
and intentionally keep their separate component styles.

## Verification

Use build and source review only; do not create or run unit tests.
Before `dotnet build`, set `$env:AVALONIA_TELEMETRY_OPTOUT='1'`.
Review light/dark colors, long localized titles/actions, narrow widths,
keyboard focus, disabled/busy actions, mode-specific footers and dynamic
option commands in the running application before visual acceptance.
