using System;
using System.Linq;
using OpenUtau.Core.Ustx;

namespace OpenUtauMobile.Services;

/// <summary>使用桌面端的选择槽持久化主副表情，保留其他槽位。</summary>
public static class ExpressionSelectionState
{
    public static (string Primary, string Secondary) Read(UProject project)
    {
        string[] slots = project.expSelectors ?? Array.Empty<string>();
        string ReadSlot(int index, bool secondary)
        {
            string? key = index >= 0 && index < slots.Length ? slots[index] : null;
            if (secondary && key == string.Empty) return string.Empty;
            if (key != null && project.expressions.ContainsKey(key)) return key;
            return project.expressions.Values.ElementAtOrDefault(Math.Max(0, index))?.abbr
                ?? project.expressions.Keys.FirstOrDefault() ?? (secondary ? string.Empty : "vel");
        }
        return (ReadSlot(project.expPrimary, false), ReadSlot(project.expSecondary, true));
    }

    public static void Store(UProject project, string primary, string secondary)
    {
        string[] slots = project.expSelectors ?? Array.Empty<string>();
        if (slots.Length < 2)
        {
            Array.Resize(ref slots, 2);
        }
        int primaryIndex = project.expPrimary >= 0 && project.expPrimary < slots.Length ? project.expPrimary : 0;
        int secondaryIndex = project.expSecondary >= 0 && project.expSecondary < slots.Length
            && project.expSecondary != primaryIndex ? project.expSecondary : (primaryIndex == 0 ? 1 : 0);
        slots[primaryIndex] = primary;
        slots[secondaryIndex] = secondary;
        project.expSelectors = slots;
        project.expPrimary = primaryIndex;
        project.expSecondary = secondaryIndex;
    }
}