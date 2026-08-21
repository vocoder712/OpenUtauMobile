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
public class PhonemeEditViewModel : PopupViewModelBase, IDisposable
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

        LoadCurrentPhoneme();
    }

    private void LoadCurrentPhoneme()
    {
        if (_currentPhonemeIndex >= 0 && _currentPhonemeIndex < _part.phonemes.Count)
        {
            CurrentPhoneme = _part.phonemes[_currentPhonemeIndex];
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
        CancelCommand.Dispose();
        ResetCommand.Dispose();
        NextCommand.Dispose();
        ConfirmCommand.Dispose();
    }
}
