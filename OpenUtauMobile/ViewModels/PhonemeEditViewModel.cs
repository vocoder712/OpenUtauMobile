using System;
using System.Linq;
using System.Reactive;
using OpenUtau.Core;
using OpenUtau.Core.Ustx;
using OpenUtauMobile.Helpers;
using ReactiveUI;
using ReactiveUI.Fody.Helpers;

namespace OpenUtauMobile.ViewModels;

/// <summary>
/// 音素别名编辑弹窗 ViewModel。
/// 支持直接修改音素别名（ChangePhonemeAliasCommand），支持恢复默认别名，支持在声部的音素列表中切换导航。
/// </summary>
public class PhonemeEditViewModel : PopupViewModelBase, IDisposable, ICmdSubscriber
{
    private readonly UVoicePart _part;
    private int _currentPhonemeIndex;

    /// <summary>
    /// 当前编辑的音素引用
    /// </summary>
    [Reactive]
    public UPhoneme? CurrentPhoneme { get; private set; }

    /// <summary>
    /// 当前音素别名（输入框绑定）
    /// </summary>
    [Reactive]
    public string CurrentAlias { get; set; } = "";

    /// <summary>
    /// 当前音素信息显示
    /// </summary>
    [Reactive]
    public string CurrentPhonemeInfo { get; private set; } = "";

    /// <summary>
    /// 默认原始音素提示
    /// </summary>
    [Reactive]
    public string RawPhonemeHint { get; private set; } = "";

    /// <summary>当前音素是否存在可查看的错误，重新分词后同步更新。</summary>
    [Reactive]
    public bool HasError { get; private set; }

    /// <summary>请求查看错误详情，不提交当前别名草稿。</summary>
    public ReactiveCommand<Unit, Unit> ViewErrorCommand { get; }

    /// <summary>
    /// 取消命令
    /// </summary>
    public ReactiveCommand<Unit, Unit> CancelCommand { get; }

    /// <summary>
    /// 恢复默认别名命令
    /// </summary>
    public ReactiveCommand<Unit, Unit> ResetCommand { get; }

    /// <summary>
    /// "下一个"命令
    /// </summary>
    public ReactiveCommand<Unit, Unit> NextCommand { get; }

    /// <summary>
    /// 确认命令
    /// </summary>
    public ReactiveCommand<Unit, Unit> ConfirmCommand { get; }

    /// <summary>
    /// 焦点请求事件
    /// </summary>
    public event Action? FocusRequested;

    public PhonemeEditViewModel(UVoicePart part, UNote note, int phonemeIndex)
    {
        _part = part;

        // 查找对应音素在 Part.phonemes 中的序号
        int index = 0;
        for (int i = 0; i < _part.phonemes.Count; i++)
        {
            if (_part.phonemes[i].Parent == note && _part.phonemes[i].index == phonemeIndex)
            {
                index = i;
                break;
            }
        }
        _currentPhonemeIndex = index;

        CancelCommand = ReactiveCommand.Create(OnCancel);
        ResetCommand = ReactiveCommand.Create(OnReset);
        NextCommand = ReactiveCommand.Create(OnNext);
        ConfirmCommand = ReactiveCommand.Create(OnConfirm);
        ViewErrorCommand = ReactiveCommand.Create(OnViewError, this.WhenAnyValue(x => x.HasError));

        LoadCurrentPhoneme();
        DocManager.Inst.AddSubscriber(this);
    }

    private void LoadCurrentPhoneme()
    {
        if (_currentPhonemeIndex >= 0 && _currentPhonemeIndex < _part.phonemes.Count)
        {
            CurrentPhoneme = _part.phonemes[_currentPhonemeIndex];
            RefreshErrorState();
            string currentVal = CurrentPhoneme.phoneme != CurrentPhoneme.rawPhoneme
                ? CurrentPhoneme.phoneme
                : (!string.IsNullOrEmpty(CurrentPhoneme.phoneme) ? CurrentPhoneme.phoneme : "");

            CurrentAlias = currentVal;
            string lyric = CurrentPhoneme.Parent?.lyric ?? "";
            string raw = CurrentPhoneme.rawPhoneme;
            CurrentPhonemeInfo = string.Format(L.S("PhonemeEdit.Info"), lyric, CurrentPhoneme.index + 1);
            RawPhonemeHint = $"Default: [{raw}]";

            FocusRequested?.Invoke();
        }
    }

    private bool HasNext => _currentPhonemeIndex + 1 < _part.phonemes.Count;

    /// <summary>重新取得当前音素的错误状态，同时保留尚未提交的别名文本。</summary>
    private void RefreshErrorState()
    {
        if (CurrentPhoneme is { } current)
        {
            CurrentPhoneme = _part.phonemes.FirstOrDefault(phoneme =>
                phoneme.Parent == current.Parent && phoneme.index == current.index);
        }
        HasError = CurrentPhoneme is { Error: true } or { ErrorException: not null };
    }

    /// <summary>由调用方先结束编辑弹窗，再显示既有错误弹窗，避免同一模态宿主重入。</summary>
    private void OnViewError()
    {
        RefreshErrorState();
        if (!HasError) return;
        ErrorMessageNotification notification = CurrentPhoneme?.ErrorException is { } error
            ? new ErrorMessageNotification(error)
            : new ErrorMessageNotification(L.S("PhonemeEdit.UnknownError"));
        RaiseClose(new ErrorDialogViewModel(notification));
    }

    /// <summary>异步分词结果到达时刷新错误入口，不覆盖用户正在编辑的别名。</summary>
    public void OnNext(UCommand cmd, bool isUndo)
    {
        if (cmd is PhonemizedNotification notification && ReferenceEquals(notification.part, _part))
            RefreshErrorState();
    }

    private void SaveCurrentPhonemeEdit()
    {
        if (CurrentPhoneme?.Parent == null) return;

        string? newAlias = string.IsNullOrWhiteSpace(CurrentAlias) ? null : CurrentAlias.Trim();
        UPhonemeOverride o = CurrentPhoneme.Parent.GetPhonemeOverride(CurrentPhoneme.index);

        if (o.phoneme != newAlias)
        {
            DocManager.Inst.StartUndoGroup();
            DocManager.Inst.ExecuteCmd(new ChangePhonemeAliasCommand(_part, CurrentPhoneme.Parent, CurrentPhoneme.index, newAlias));
            DocManager.Inst.EndUndoGroup();
        }
    }

    private void OnCancel()
    {
        RaiseClose(null);
    }

    private void OnReset()
    {
        CurrentAlias = "";
        SaveCurrentPhonemeEdit();
        RaiseClose(null);
    }

    private void OnNext()
    {
        SaveCurrentPhonemeEdit();
        if (HasNext)
        {
            _currentPhonemeIndex++;
            LoadCurrentPhoneme();
        }
        else
        {
            RaiseClose(null);
        }
    }

    private void OnConfirm()
    {
        SaveCurrentPhonemeEdit();
        RaiseClose(null);
    }

    public void Dispose()
    {
        DocManager.Inst.RemoveSubscriber(this);
        ViewErrorCommand.Dispose();
        CancelCommand.Dispose();
        ResetCommand.Dispose();
        NextCommand.Dispose();
        ConfirmCommand.Dispose();
    }
}
