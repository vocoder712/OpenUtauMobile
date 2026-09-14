using System;
using System.Linq;
using OpenUtau.Core.Ustx;
using OpenUtauMobile.Helpers;
using ReactiveUI;
using ReactiveUI.Fody.Helpers;

namespace OpenUtauMobile.ViewModels;

/// <summary>与桌面端一致的表情定义草稿，不直接修改工程中的定义。</summary>
public class ExpressionBuilder : ReactiveObject
{
    [Reactive] public string Name { get; set; } = "new expression";
    [Reactive] public float Min { get; set; }
    [Reactive] public float Max { get; set; } = 100;
    [Reactive] public float DefaultValue { get; set; }
    [Reactive] public float CustomDefaultValue { get; set; }
    [Reactive] public bool IsFlag { get; set; }
    [Reactive] public string Flag { get; set; } = string.Empty;
    [Reactive] public string OptionValues { get; set; } = string.Empty;
    [Reactive] public bool SkipOutputIfDefault { get; set; }

    private string abbr = string.Empty;
    private int expressionType;
    public bool IsTrackOverride { get; }
    public string Abbr
    {
        get => abbr;
        set
        {
            this.RaiseAndSetIfChanged(ref abbr, value);
            this.RaisePropertyChanged(nameof(IsCustom));
            this.RaisePropertyChanged(nameof(IsRemovable));
            this.RaisePropertyChanged(nameof(DisplayAbbr));
        }
    }
    public int ExpressionType
    {
        get => expressionType;
        set
        {
            this.RaiseAndSetIfChanged(ref expressionType, value);
            this.RaisePropertyChanged(nameof(IsNumerical));
            this.RaisePropertyChanged(nameof(IsOptions));
            this.RaisePropertyChanged(nameof(IsCurve));
        }
    }
    public bool IsCustom => !OpenUtau.Core.Format.Ustx.required.Contains(Abbr);
    public bool IsRemovable => IsCustom || IsTrackOverride;
    public bool IsNumerical => ExpressionType == (int)UExpressionType.Numerical;
    public bool IsOptions => ExpressionType == (int)UExpressionType.Options;
    public bool IsCurve => ExpressionType == (int)UExpressionType.Curve;
    public string DisplayAbbr => Abbr.ToUpperInvariant();
    public string AddMenuLabel => string.IsNullOrEmpty(Abbr) ? Name : $"{Name}: {Abbr}";

    public ExpressionBuilder(bool isTrackOverride = false)
    {
        IsTrackOverride = isTrackOverride;
    }

    public ExpressionBuilder(UExpressionDescriptor descriptor, bool isTrackOverride = false) : this(isTrackOverride)
    {
        Name = descriptor.name;
        Abbr = descriptor.abbr;
        ExpressionType = (int)descriptor.type;
        Min = descriptor.min;
        Max = descriptor.max;
        DefaultValue = descriptor.defaultValue;
        CustomDefaultValue = descriptor.CustomDefaultValue;
        IsFlag = descriptor.isFlag;
        Flag = descriptor.flag ?? string.Empty;
        OptionValues = descriptor.options == null ? string.Empty : string.Join(',', descriptor.options);
        SkipOutputIfDefault = descriptor.skipOutputIfDefault;
    }

    public string? Validate()
    {
        if (string.IsNullOrWhiteSpace(Name)) return L.S("Expressions.Error.Name");
        if (string.IsNullOrWhiteSpace(Abbr)) return L.S("Expressions.Error.Abbr");
        // 桌面端仅对数值型执行范围和默认值校验。
        if (IsNumerical)
        {
            if (Abbr.Trim().Length > 4) return L.S("Expressions.Error.AbbrLength");
            if (Min >= Max) return L.S("Expressions.Error.Range");
            if (DefaultValue < Min || DefaultValue > Max) return L.S("Expressions.Error.Default");
            if (CustomDefaultValue < Min || CustomDefaultValue > Max) return L.S("Expressions.Error.CustomDefault");
        }
        return null;
    }

    public UExpressionDescriptor Build()
    {
        return (UExpressionType)ExpressionType switch
        {
            UExpressionType.Numerical => new UExpressionDescriptor(Name.Trim(), Abbr.Trim().ToLower(), Min, Max,
                DefaultValue, Flag, CustomDefaultValue, SkipOutputIfDefault),
            UExpressionType.Options => new UExpressionDescriptor(Name.Trim(), Abbr.Trim().ToLower(), IsFlag,
                (OptionValues ?? string.Empty).Split(',')),
            UExpressionType.Curve => new UExpressionDescriptor(Name.Trim(), Abbr.Trim().ToLower(), Min, Max,
                DefaultValue)
            {
                type = UExpressionType.Curve,
            },
            _ => throw new InvalidOperationException("Unexpected expression type")
        };
    }

    public override string ToString() => Name;
}
