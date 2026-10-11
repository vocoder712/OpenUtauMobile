using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reactive;
using Avalonia.Input.TextInput;
using OpenUtau.Core.ExpressionGraph;
using OpenUtauMobile.Helpers;
using ReactiveUI;

namespace OpenUtauMobile.ViewModels;

/// <summary>参数候选保存文件键值；显示名称不会写入工程。</summary>
public sealed record GraphParameterChoice(string Value, string Label);

/// <summary>单个字段的本地编辑缓冲，刷新时保留草稿并检测同节点冲突。</summary>
public sealed class GraphParameterEdit : ReactiveObject
{
    private readonly ExpressionGraphEditorViewModel _owner;
    private UGraphNode _baseline;
    private string _text = string.Empty;
    private string _error = string.Empty;
    private bool _conflict;
    private bool _syncing;
    private bool _sliding;
    private string _sliderStartText = string.Empty;
    private GraphParameterChoice? _choice;
    private IReadOnlyList<GraphParameterChoice> _choices = [];
    public GraphNodeParameter Definition { get; }
    public string Name => Definition.Name;
    public int NodeId => _baseline.id;
    public string Label { get; private set; }
    public string Hint { get; private set; } = string.Empty;
    public bool IsChoice => Definition.Kind is GraphParameterKind.Choice or GraphParameterKind.Expression;
    public bool IsBool => Definition.Kind == GraphParameterKind.Bool;
    public bool IsSlider => Definition.Kind == GraphParameterKind.Slider;
    public bool IsText => !IsChoice && !IsBool;
    public TextInputContentType InputContentType => Definition.Kind is GraphParameterKind.Number or GraphParameterKind.Slider
        ? TextInputContentType.Number : TextInputContentType.Normal;
    /// <summary>只比较字段缓冲与基线，不扫描图或产生工程变更。</summary>
    public bool IsDirty => Text != Effective(_baseline);
    public bool HasError => Error.Length > 0;
    public bool IsSliding => _sliding;
    public double Minimum => Definition.Minimum;
    public double Maximum => Definition.Maximum;
    public double Step => Definition.Step;
    public IReadOnlyList<GraphParameterChoice> Choices => _choices;
    public string Error
    {
        get => _error;
        private set { this.RaiseAndSetIfChanged(ref _error, value); this.RaisePropertyChanged(nameof(HasError)); }
    }
    public string Text
    {
        get => _text;
        set
        {
            if (_syncing || value == _text) return;
            this.RaiseAndSetIfChanged(ref _text, value ?? string.Empty);
            if (!_conflict) Error = string.Empty;
            NotifyValue();
        }
    }
    public bool BoolValue
    {
        get => Text is "true" or "True" or "1";
        set => Text = value ? "true" : "false";
    }
    /// <summary>控件显示值限制在滑块范围；未编辑的原始文本不被钳制回写。</summary>
    public double SliderValue
    {
        get => float.TryParse(Text, NumberStyles.Float, CultureInfo.InvariantCulture, out float value) && float.IsFinite(value)
            ? Math.Clamp(value, Minimum, Maximum) : Minimum;
        set { if (!_syncing && double.IsFinite(value)) Text = ((float)value).ToString("R", CultureInfo.InvariantCulture); }
    }
    public GraphParameterChoice? SelectedChoice
    {
        get => _choice;
        set
        {
            // 控件重建候选时的临时 null 不代表用户修改，也不把缺失项替换为第一项。
            if (_syncing || value == null || !_choices.Contains(value)) return;
            Text = value.Value;
        }
    }
    public ReactiveCommand<Unit, Unit> ApplyCommand { get; }
    public ReactiveCommand<Unit, Unit> CancelCommand { get; }

    /// <summary>绑定稳定节点 ID、参数定义及当前候选，不持有工程可写引用。</summary>
    public GraphParameterEdit(ExpressionGraphEditorViewModel owner, UGraphNode node, GraphNodeParameter definition,
        string label, string hint, IReadOnlyList<GraphParameterChoice> choices)
    {
        _owner = owner; _baseline = node.Clone(); Definition = definition; Label = label;
        _text = Effective(node);
        ApplyCommand = ReactiveCommand.Create(() => { _owner.ApplyParameter(this); });
        CancelCommand = ReactiveCommand.Create(() => _owner.ReloadParameter(this));
        Synchronize(node, hint, choices);
    }

    private string Effective(UGraphNode node) => node.GetString(Name) ?? Definition.Default ?? string.Empty;

    /// <summary>只读字段随文档更新，草稿字段保持输入；同节点变动要求明确重载。</summary>
    public void Synchronize(UGraphNode node, string hint, IReadOnlyList<GraphParameterChoice> choices, string? label = null)
    {
        if (label != null) { Label = label; this.RaisePropertyChanged(nameof(Label)); }
        Hint = hint; this.RaisePropertyChanged(nameof(Hint));
        if (IsDirty && !SameNode(_baseline, node))
        {
            _conflict = true; Error = L.S("GraphEdit.Conflict");
        }
        else if (!IsDirty)
        {
            _baseline = node.Clone(); _text = Effective(node); _conflict = false; Error = string.Empty;
            this.RaisePropertyChanged(nameof(Text));
        }
        List<GraphParameterChoice> available = choices.ToList();
        if (IsChoice && !available.Any(choice => choice.Value == Text))
            available.Insert(0, new(Text, string.Format(L.S("GraphEdit.MissingChoice"), Text.Length == 0 ? "—" : Text)));
        _syncing = true;
        try
        {
            if (!_choices.SequenceEqual(available))
            {
                _choice = null; this.RaisePropertyChanged(nameof(SelectedChoice));
                _choices = available; this.RaisePropertyChanged(nameof(Choices));
            }
            NotifyValue();
        }
        finally { _syncing = false; }
    }

    /// <summary>取消草稿并恢复最新工程值，显式解除冲突。</summary>
    public void Reload(UGraphNode node)
    {
        _sliding = false; _baseline = node.Clone(); _text = Effective(node); _conflict = false; Error = string.Empty;
        this.RaisePropertyChanged(nameof(Text)); NotifyValue();
    }

    /// <summary>本会话提交其他字段后重定位基线，保留其余草稿；既有外部冲突不被解除。</summary>
    public void RebaseOwnChange(UGraphNode node)
    {
        if (_conflict) return;
        if (!IsDirty) { Reload(node); return; }
        _baseline = node.Clone(); NotifyValue();
    }

    /// <summary>提交前再次检查最新节点与字段语义；不修复导入数据的未编辑字段。</summary>
    public bool TryPrepare(UGraphNode node, IReadOnlyList<GraphParameterChoice> choices, out string? value)
    {
        value = node.GetString(Name);
        if (!IsDirty) return true;
        if (_conflict || !SameNode(_baseline, node))
        {
            _conflict = true; Error = L.S("GraphEdit.Conflict"); return false;
        }
        if (Definition.Kind is GraphParameterKind.Number or GraphParameterKind.Slider)
        {
            if (Text.Length == 0 && Definition.Kind == GraphParameterKind.Number && Definition.Default == null)
            {
                value = null; return true;
            }
            if (!float.TryParse(Text, NumberStyles.Float, CultureInfo.InvariantCulture, out float number) || !float.IsFinite(number))
            {
                Error = L.S("GraphEdit.InvalidNumber"); return false;
            }
            if (IsSlider && (number < Minimum || number > Maximum))
            {
                Error = string.Format(L.S("GraphEdit.Range"), Minimum, Maximum); return false;
            }
            value = number.ToString("R", CultureInfo.InvariantCulture);
        }
        else if (IsChoice)
        {
            if (!choices.Any(choice => choice.Value == Text)) { Error = L.S("GraphEdit.InvalidChoice"); return false; }
            value = Text;
        }
        else if (IsBool)
        {
            if (!bool.TryParse(Text, out bool flag)) { Error = L.S("GraphEdit.InvalidChoice"); return false; }
            value = flag ? "true" : "false";
        }
        else value = Text;
        Error = string.Empty;
        return true;
    }

    /// <summary>同值确认不生成命令，包括默认值及等值数字的不同文本表示。</summary>
    public bool ChangesValue(UGraphNode node, string? value)
    {
        string? old = node.GetString(Name);
        if (value == old || old == null && value == Definition.Default) return false;
        if (IsBool && (value is "true" or "True" or "1") == ((old ?? Definition.Default) is "true" or "True" or "1")) return false;
        if (Definition.Kind is GraphParameterKind.Number or GraphParameterKind.Slider
            && float.TryParse(old ?? Definition.Default, NumberStyles.Float, CultureInfo.InvariantCulture, out float a)
            && float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out float b) && a == b) return false;
        return true;
    }

    /// <summary>滑块只更新字段预览，结束时才提交一次。</summary>
    public void BeginSlider()
    {
        if (_sliding) return;
        _sliderStartText = Text; _sliding = true;
    }
    /// <summary>正常释放提交一次；系统取消或离页恢复最新工程值。</summary>
    public void EndSlider(bool cancelled)
    {
        if (!_sliding) return;
        _sliding = false;
        if (cancelled)
        {
            Text = _sliderStartText;
            if (!IsDirty) _owner.ReloadParameter(this);
        }
        else _owner.ApplyParameter(this);
    }

    /// <summary>同步关联控件时抑制双向绑定回写，避免候选重建和滑块钳制改变草稿。</summary>
    private void NotifyValue()
    {
        bool syncing = _syncing; _syncing = true;
        try
        {
            _choice = _choices.FirstOrDefault(choice => choice.Value == Text);
            this.RaisePropertyChanged(nameof(SelectedChoice)); this.RaisePropertyChanged(nameof(BoolValue));
            this.RaisePropertyChanged(nameof(SliderValue)); this.RaisePropertyChanged(nameof(IsDirty));
        }
        finally { _syncing = syncing; }
    }

    private static bool SameNode(UGraphNode a, UGraphNode b) => a.id == b.id && a.type == b.type && a.x == b.x && a.y == b.y
        && a.parameters.Count == b.parameters.Count && a.parameters.All(pair => b.GetString(pair.Key) == pair.Value);
}
