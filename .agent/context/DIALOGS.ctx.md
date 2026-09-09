# Dialog component contract

## Single owner

`OpenUtauMobile/Themes/OpenUtauMobile/Styles/Controls/PopupCommon.axaml` owns
dialog surfaces, headers, close icons, footer spacing, action typography,
colors, geometry and interaction states. `Controls/DialogShell.cs` supplies
content slots and logical parenting only; it contains no design values.

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
        <dialogs:DialogActions>
            <Button Content="{DynamicResource Common.Cancel}"
                    Command="{Binding CancelCommand}" />
            <Button Classes="DialogPrimary"
                    Content="{DynamicResource Common.Confirm}"
                    Command="{Binding ConfirmCommand}" />
        </dialogs:DialogActions>
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
  actions or actions outside `DialogActions`. Do not use legacy `Primary`,
  `Secondary`, `PrimaryBtn`, `SecondaryBtn`, `AccentBtn` or `OptionAction`
  for new dialog footer actions.
- Use `DialogActions` rather than fixed-column grids or horizontal
  ScrollViewers: actions wrap at narrow widths and keep declaration order.
- Footer stays separate from scrollable business content. Put conditional
  footer visibility on its root control so no empty footer inset remains.
- For a composite header, use `DialogShell.Header` with `PopupTitle` and
  optional `DialogSubtitle`; supply text and visibility only, not typography.
- `DialogBody` supplies a shared content margin; `DialogBodyBorder` supplies
  the equivalent padding for a Border. Do not nest both for the same inset.
- Existing named inputs and event handlers stay in the view's namescope.
  Footer controls retain their ViewModel through the shell's logical tree.

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
