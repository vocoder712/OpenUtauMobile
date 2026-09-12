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

/// <summary>显式分组的一行选项。</summary>
public sealed class OptionConfirmRow
{
    public IReadOnlyList<OptionConfirmOption> Options { get; }

    internal OptionConfirmRow(List<OptionConfirmOption> options)
    {
        Options = options.AsReadOnly();
    }
}

/// <summary>展示标题、正文和显式分行的选项，并返回所选项的字符串值。</summary>
public sealed class OptionConfirmPopupViewModel : PopupViewModelBase
{
    public string Title { get; }
    public string Content { get; }
    public IReadOnlyList<OptionConfirmOption> Options { get; }
    public IReadOnlyList<OptionConfirmRow> OptionRows { get; }

    public ReactiveCommand<OptionConfirmOption, Unit> SelectOptionCommand { get; }
    public ReactiveCommand<Unit, Unit> CancelCommand { get; }

    public OptionConfirmPopupViewModel(
        string title,
        string content,
        IEnumerable<OptionConfirmOption> options)
        : this(title, content, new[] { options })
    {
    }

    public OptionConfirmPopupViewModel(
        string title,
        string content,
        IEnumerable<IEnumerable<OptionConfirmOption>> optionRows)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        ArgumentNullException.ThrowIfNull(optionRows);

        List<OptionConfirmRow> rows = new();
        foreach (IEnumerable<OptionConfirmOption> row in optionRows)
        {
            ArgumentNullException.ThrowIfNull(row);
            List<OptionConfirmOption> rowOptions = row.ToList();
            foreach (OptionConfirmOption option in rowOptions)
            {
                ArgumentNullException.ThrowIfNull(option);
            }
            // 空行不生成容器，避免在按钮之间留下空白。
            if (rowOptions.Count > 0)
            {
                rows.Add(new OptionConfirmRow(rowOptions));
            }
        }

        List<OptionConfirmOption> optionList = rows.SelectMany(row => row.Options).ToList();
        if (optionList.Count == 0)
        {
            throw new ArgumentException("At least one confirmation option is required.", nameof(optionRows));
        }

        Title = title;
        Content = content ?? string.Empty;
        Options = optionList.AsReadOnly();
        OptionRows = rows.AsReadOnly();
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
