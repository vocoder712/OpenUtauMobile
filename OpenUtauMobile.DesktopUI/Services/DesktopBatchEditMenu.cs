using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Layout;
using IconPacks.Avalonia.PhosphorIcons;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using OpenUtau.Core;
using OpenUtau.Core.Util;
using OpenUtau.Core.Ustx;
using OpenUtauMobile.DesktopUI.Views;
using OpenUtauMobile.Helpers;
using OpenUtauMobile.Services.Dialogs;
using OpenUtauMobile.ViewModels;

namespace OpenUtauMobile.DesktopUI.Services;

internal static class DesktopBatchEditMenu
{
    public static MenuItem Create(Func<PianoRollViewModel?> getPiano, Func<bool>? canExecute = null)
    {
        MenuItem root = new() { Header = DesktopUi.Label("BatchEdit.Title") };
        Refresh(root, getPiano(), canExecute);
        return root;
    }

    public static void Refresh(MenuItem root, PianoRollViewModel? piano, Func<bool>? canExecute = null)
    {
        root.Items.Clear();
        var (_, part, targets) = GetTargets(piano);
        bool available = CanRun(canExecute);
        foreach (BatchEditCategory category in Enum.GetValues<BatchEditCategory>())
        {
            MenuItem categoryMenu = new() { Header = DesktopUi.Label(CategoryTitleKey(category)) };
            List<string> pinned = Preferences.Default.PinnedBatchEdits?.GetValueOrDefault(category.ToString()) ?? [];
            BatchEditDescriptor[] descriptors = BatchEditCatalog.Items.Where(item => item.Category == category).ToArray();
            foreach (BatchEditDescriptor descriptor in descriptors.OrderBy(item =>
                pinned.IndexOf(item.Id) is int index && index >= 0 ? index : int.MaxValue))
            {
                MenuItem command = new()
                {
                    Header = CreateCommandHeader(descriptor, pinned.Contains(descriptor.Id), canExecute),
                    IsEnabled = available && part != null && (!descriptor.RequiresNotes || targets.Length > 0),
                };
                command.Click += async (_, _) =>
                {
                    if (piano != null && CanRun(canExecute)) await InvokeAsync(descriptor, piano, canExecute);
                };
                categoryMenu.Items.Add(command);
            }

            root.Items.Add(categoryMenu);
        }
    }

    private static Grid CreateCommandHeader(BatchEditDescriptor descriptor, bool isPinned, Func<bool>? canExecute)
    {
        Grid row = new() { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        row.Children.Add(new TextBlock { Text = L.S(descriptor.TitleKey), VerticalAlignment = VerticalAlignment.Center });
        PackIconPhosphorIcons icon = new() { Width = 14, Height = 14 };
        Button pin = new()
        {
            Content = icon, Width = 22, Height = 22, MinWidth = 0, MinHeight = 0,
            Padding = new Thickness(3), Margin = new Thickness(12, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
        };
        pin.Classes.Add("icon");
        void UpdatePin(bool value)
        {
            icon.Kind = value ? PackIconPhosphorIconsKind.PushPinFill : PackIconPhosphorIconsKind.PushPin;
            string label = L.S(value ? "BatchEdit.Unpin" : "BatchEdit.Pin");
            ToolTip.SetTip(pin, label);
            AutomationProperties.SetName(pin, label + " " + L.S(descriptor.TitleKey));
        }
        UpdatePin(isPinned);
        pin.Click += (_, e) =>
        {
            e.Handled = true;
            if (!CanRun(canExecute)) return;
            Preferences.Default.PinnedBatchEdits ??= [];
            string key = descriptor.Category.ToString();
            if (!Preferences.Default.PinnedBatchEdits.TryGetValue(key, out List<string>? ids) || ids == null)
                Preferences.Default.PinnedBatchEdits[key] = ids = [];
            bool wasPinned = ids.Contains(descriptor.Id);
            ids.RemoveAll(id => id == descriptor.Id);
            if (!wasPinned) ids.Insert(0, descriptor.Id);
            UpdatePin(!wasPinned);
            Preferences.Save();
        };
        Grid.SetColumn(pin, 1);
        row.Children.Add(pin);
        return row;
    }
    private static bool CanRun(Func<bool>? canExecute) =>
        !PianoRollViewModel.IsBatchEditRunning && !DocManager.Inst.HasOpenUndoGroup && (canExecute?.Invoke() ?? true);

    private static string CategoryTitleKey(BatchEditCategory category) => category switch
    {
        BatchEditCategory.Lyrics => "BatchEdit.Tab.Lyrics",
        BatchEditCategory.Notes => "BatchEdit.Tab.Notes",
        _ => "BatchEdit.Tab.Reset",
    };

    private static (UProject Project, UVoicePart? Part, UNote[] Targets) GetTargets(PianoRollViewModel? piano)
    {
        UProject project = DocManager.Inst.Project;
        UVoicePart? part = piano?.EditingVoicePart;
        if (part == null || !project.parts.Contains(part)) return (project, null, []);
        UNote[] selected = piano!.SelectedNotes.Where(part.notes.Contains).OrderBy(note => note.position).ToArray();
        return selected.Length > 0 ? (project, part, selected) : (project, part, part.notes.ToArray());
    }

    private static async Task InvokeAsync(BatchEditDescriptor descriptor, PianoRollViewModel piano, Func<bool>? canExecute)
    {
        var (project, part, targets) = GetTargets(piano);
        if (part == null || descriptor.RequiresNotes && targets.Length == 0 || !CanRun(canExecute)) return;
        BatchEditExecutionRequest? request;
        if (descriptor.ParameterKind != BatchEditParameterKind.None)
        {
            request = await PopupService.Show<BatchEditExecutionRequest>(new DesktopBatchEditCommandPopup(),
                new DesktopBatchEditDialogViewModel(descriptor, project, part, targets));
            if (request == null) return;
        }
        else
        {
            if (!descriptor.TryCreate(descriptor.DefaultValueFactory(project), out var operation, out _) || operation == null) return;
            request = new BatchEditExecutionRequest(operation, L.S(descriptor.TitleKey), targets, descriptor.SupportsCancellation, descriptor.RequiresNotes);
        }
        await piano.ExecuteDesktopBatchEditAsync(project, part, request);
    }
}
