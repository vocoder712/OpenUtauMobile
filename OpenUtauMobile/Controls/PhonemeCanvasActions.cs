using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using OpenUtau.Core;
using OpenUtau.Core.Ustx;
using OpenUtauMobile.Helpers;
using OpenUtauMobile.ViewModels;

namespace OpenUtauMobile.Controls
{
    /// <summary>两种音素画布共用别名与重置命令；工具栏也可供触摸操作。</summary>
    public static class PhonemeCanvasActions
    {
        public static void Attach(Control canvas, Func<UVoicePart?> part, Func<Point, UPhoneme?> hit)
        {
            UPhoneme? target = null;
            canvas.AddHandler(InputElement.PointerPressedEvent, (_, e) =>
            {
                if (e.GetCurrentPoint(canvas).Properties.PointerUpdateKind == PointerUpdateKind.RightButtonPressed)
                    target = hit(e.GetPosition(canvas));
            }, RoutingStrategies.Tunnel);
            ContextMenu menu = new();
            menu.Opening += (_, e) =>
            {
                UVoicePart? current = part(); UPhoneme? phoneme = target;
                if (current == null || phoneme == null || !current.phonemes.Contains(phoneme)) { e.Cancel = true; return; }
                MenuItem edit = new() { Header = L.S("PhonemeEdit.Title"), IsEnabled = !DocManager.Inst.HasOpenUndoGroup };
                edit.Click += (_, _) => (canvas.DataContext as PianoRollViewModel)?.RaiseRequestEditPhoneme(current, phoneme.Parent, phoneme.index);
                MenuItem reset = new() { Header = L.S("BatchEdit.Action.ClearTimings"), IsEnabled = !DocManager.Inst.HasOpenUndoGroup };
                reset.Click += (_, _) => ResetTiming(current, new[] { phoneme });
                menu.ItemsSource = new[] { edit, reset };
            };
            canvas.ContextMenu = menu;
        }

        public static void ResetTiming(UVoicePart part, IEnumerable<UPhoneme> phonemes)
        {
            if (DocManager.Inst.HasOpenUndoGroup || !DocManager.Inst.Project.parts.Contains(part)) return;
            List<UCommand> commands = [];
            foreach (UPhoneme phoneme in phonemes.Distinct().ToArray())
            {
                if (!part.phonemes.Contains(phoneme) || !part.notes.Contains(phoneme.Parent)) continue;
                UPhonemeOverride? value = phoneme.Parent.phonemeOverrides.FirstOrDefault(o => o.index == phoneme.index);
                if (value == null) continue;
                if (value.offset.HasValue) commands.Add(new PhonemeOffsetCommand(part, phoneme.Parent, phoneme.index, 0));
                if (value.preutterDelta.HasValue) commands.Add(new PhonemePreutterCommand(part, phoneme.Parent, phoneme.index, phoneme, 0));
                if (value.overlapDelta.HasValue) commands.Add(new PhonemeOverlapCommand(part, phoneme.Parent, phoneme.index, phoneme, 0));
                if (value.attackTimeDelta.HasValue) commands.Add(new PhonemeAttackTimeCommand(part, phoneme.Parent, phoneme.index, phoneme, 0));
                if (value.releaseTimeDelta.HasValue) commands.Add(new PhonemeReleaseTimeCommand(part, phoneme.Parent, phoneme.index, phoneme, 0));
            }
            if (commands.Count == 0) return;
            DocManager.Inst.StartUndoGroup(deferValidate: true);
            try { foreach (UCommand command in commands) DocManager.Inst.ExecuteCmd(command); }
            finally { DocManager.Inst.EndUndoGroup(); }
        }
    }
}
