using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Threading;
using OpenUtau.Api;
using OpenUtau.Core.Ustx;
using OpenUtauMobile.Controls;
using OpenUtauMobile.Helpers;
using OpenUtauMobile.Services.Dialogs;
using OpenUtauMobile.ViewModels;

namespace OpenUtauMobile.Services;

public sealed record PhonemizerPickerRequest(
    bool AllowTrackDefault = false,
    string? CurrentName = null,
    string? TrackDefaultLabel = null,
    Control? Anchor = null,
    CancellationToken CancellationToken = default);

// 返回 null 表示取消；Factory 为 null 的结果表示明确恢复轨道继承。
public sealed record PhonemizerPickerResult(PhonemizerFactory? Factory);

public static class PhonemizerPickerService
{
    public static async Task<PhonemizerPickerResult?> PickAsync(PhonemizerPickerRequest request)
    {
        return await Dispatcher.UIThread.InvokeAsync(async () =>
        {
            if (request.CancellationToken.IsCancellationRequested) return null;
            if (ServiceHub.DesktopPhonemizerPicker is { } desktopPicker && request.Anchor is { } anchor)
            {
                // 桌面下拉只锚定实际入口；已关闭的对话框不能再作为锚点。
                if (TopLevel.GetTopLevel(anchor) == null) return null;
                return await desktopPicker(request);
            }
            using PhonemizerPickerViewModel viewModel = new(request);
            using CancellationTokenRegistration cancellation = request.CancellationToken.Register(() =>
                Dispatcher.UIThread.Post(viewModel.RequestBack));
            return await PopupService.Show<PhonemizerPickerResult>(new PhonemizerPickerPopup(), viewModel);
        });
    }
}

/// <summary>仅按完整注册名或旧版完整类型名解析，未知值保持原样。</summary>
public static class NotePhonemizerResolver
{
    public static PhonemizerFactory? Resolve(string? identifier)
    {
        if (string.IsNullOrEmpty(identifier)) return null;
        PhonemizerFactory[] factories = PhonemizerFactory.GetAll();
        PhonemizerFactory[] named = factories.Where(f => f.name == identifier).ToArray();
        if (named.Length > 0) return named.Length == 1 ? named[0] : null;
        PhonemizerFactory[] typed = factories.Where(f => f.type.FullName == identifier).ToArray();
        return typed.Length == 1 && factories.Count(f => f.name == typed[0].name) == 1 ? typed[0] : null;
    }

    public static string Display(string? identifier, string trackDefaultLabel)
    {
        if (string.IsNullOrEmpty(identifier)) return trackDefaultLabel;
        return Resolve(identifier)?.ToString() ?? string.Format(L.S("NoteProperties.PhonemizerUnavailable"), identifier);
    }

    public static bool Normalize(UProject project)
    {
        PhonemizerFactory[] factories = PhonemizerFactory.GetAll();
        ILookup<string, PhonemizerFactory> named = factories.ToLookup(factory => factory.name, StringComparer.Ordinal);
        Dictionary<string, string> legacyNames = factories.Where(factory => factory.type.FullName != null && named[factory.name].Count() == 1)
            .GroupBy(factory => factory.type.FullName!, StringComparer.Ordinal)
            .Where(group => group.Count() == 1)
            .ToDictionary(group => group.Key, group => group.Single().name, StringComparer.Ordinal);
        bool changed = false;
        foreach (UNote note in project.parts.OfType<UVoicePart>().SelectMany(part => part.notes))
        {
            if (note.PhonemizerOverride is { } identifier && !named.Contains(identifier) &&
                legacyNames.TryGetValue(identifier, out string? name))
            {
                note.PhonemizerOverride = name;
                changed = true;
            }
        }
        if (changed) project.Saved = false;
        return changed;
    }
}
