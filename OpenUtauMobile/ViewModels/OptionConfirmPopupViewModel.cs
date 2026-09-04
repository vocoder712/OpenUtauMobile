using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive;
using ReactiveUI;

namespace OpenUtauMobile.ViewModels;

/// <summary>
/// 通用选项确认弹窗中的单个选项。
/// </summary>
public sealed class OptionConfirmOption
{
    public string Label { get; }
    public string Value { get; }
    public bool IsPrimary { get; }
    public bool IsDestructive { get; }

    public OptionConfirmOption(
        string label,
        string value,
        bool isPrimary = false,
        bool isDestructive = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(label);
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        Label = label;
        Value = value;
        IsPrimary = isPrimary;
        IsDestructive = isDestructive;
    }
}

/// <summary>
/// 展示标题、正文和若干选项，并返回所选项的字符串值。
/// </summary>
public sealed class OptionConfirmPopupViewModel : PopupViewModelBase
{
    public string Title { get; }
    public string Content { get; }
    public IReadOnlyList<OptionConfirmOption> Options { get; }

    public ReactiveCommand<OptionConfirmOption, Unit> SelectOptionCommand { get; }
    public ReactiveCommand<Unit, Unit> CancelCommand { get; }

    public OptionConfirmPopupViewModel(
        string title,
        string content,
        IEnumerable<OptionConfirmOption> options)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        ArgumentNullException.ThrowIfNull(options);

        List<OptionConfirmOption> optionList = options.ToList();
        if (optionList.Count == 0)
        {
            throw new ArgumentException("At least one confirmation option is required.", nameof(options));
        }

        Title = title;
        Content = content ?? string.Empty;
        Options = optionList;
        SelectOptionCommand = ReactiveCommand.Create<OptionConfirmOption>(SelectOption);
        CancelCommand = ReactiveCommand.Create(Cancel);
    }

    private void SelectOption(OptionConfirmOption option)
    {
        RaiseClose(option.Value);
    }

    private void Cancel()
    {
        RaiseClose(null);
    }

    public override void RequestBack()
    {
        Cancel();
    }
}
