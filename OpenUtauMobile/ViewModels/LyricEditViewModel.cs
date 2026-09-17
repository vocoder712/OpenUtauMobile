using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reactive;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using OpenUtau.Core;
using OpenUtau.Core.Ustx;
using OpenUtau.Core.Util;
using OpenUtauMobile.Helpers;
using ReactiveUI;
using ReactiveUI.Fody.Helpers;

namespace OpenUtauMobile.ViewModels;

public sealed class LyricSuggestion
{
    public string Alias { get; }
    public string Source { get; }

    public LyricSuggestion(string alias, string source)
    {
        Alias = alias;
        Source = source;
    }
}

/// <summary>
/// 歌词编辑弹窗 ViewModel。
/// 直接在 UI 中执行 ChangeNoteLyricCommand，关闭时不返回任何信息。
/// 维护当前编辑的音符索引，支持在 Part 的音符列表中导航。
/// </summary>
public class LyricEditViewModel : PopupViewModelBase, IDisposable
{
    private readonly UVoicePart _part;
    private readonly CompositeDisposable _disposables = new();
    private int _currentNoteIndex;
    private int _suggestionRequestVersion;
    private bool _disposed;

    /// <summary>
    /// 当前编辑的音符引用
    /// </summary>
    [Reactive]
    public UNote? CurrentNote { get; private set; }

    /// <summary>
    /// 当前编辑的歌词（输入框绑定）
    /// </summary>
    [Reactive]
    public string CurrentLyric { get; set; } = "";

    /// <summary>
    /// 当前音符的信息显示（如：音符1/5）
    /// </summary>
    [Reactive]
    public string CurrentNoteInfo { get; private set; } = "";

    /// <summary>
    /// "下一个"按钮的提示文本
    /// </summary>
    [Reactive]
    public string NextButtonTooltip { get; private set; } = "";

    public ObservableCollection<LyricSuggestion> Suggestions { get; } = [];

    [Reactive]
    public string SuggestionStatus { get; private set; } = "";

    [Reactive]
    public LyricSuggestion? SelectedSuggestion { get; set; }

    /// <summary>
    /// 取消命令
    /// </summary>
    public ReactiveCommand<Unit, Unit> CancelCommand { get; }

    /// <summary>
    /// "下一个"命令：保存当前歌词，移动到下一个音符，若无下一个则关闭
    /// </summary>
    public ReactiveCommand<Unit, Unit> NextCommand { get; }

    /// <summary>
    /// 确认命令：保存当前歌词并关闭
    /// </summary>
    public ReactiveCommand<Unit, Unit> ConfirmCommand { get; }

    /// <summary>
    /// 焦点请求事件：当加载新歌词时触发，通知 View 获取焦点并全选
    /// </summary>
    public event Action? FocusRequested;

    /// <summary>
    /// 初始化 LyricEditViewModel。
    /// </summary>
    /// <param name="part">要编辑的声部</param>
    /// <param name="currentNoteIndex">当前要编辑的音符在 Part.notes 中的索引</param>
    public LyricEditViewModel(UVoicePart part, int currentNoteIndex)
    {
        _part = part;
        _currentNoteIndex = currentNoteIndex;

        CancelCommand = ReactiveCommand.Create(OnCancel);
        NextCommand = ReactiveCommand.Create(OnNext);
        ConfirmCommand = ReactiveCommand.Create(OnConfirm);
        // 初始化加载第一个音符
        LoadCurrentNote();

        this.WhenAnyValue(x => x.CurrentLyric)
            .DistinctUntilChanged()
            .Subscribe(lyric => _ = RefreshSuggestionsAsync(lyric ?? string.Empty))
            .DisposeWith(_disposables);

        this.WhenAnyValue(x => x.SelectedSuggestion)
            .WhereNotNull()
            .Subscribe(suggestion =>
            {
                CurrentLyric = suggestion.Alias;
                SelectedSuggestion = null;
            })
            .DisposeWith(_disposables);
    }

    private async Task RefreshSuggestionsAsync(string lyric)
    {
        int requestVersion = Interlocked.Increment(ref _suggestionRequestVersion);
        USinger? singer = GetCurrentSinger();
        ILyricsHelper? helper = ActiveLyricsHelper.Inst.Current;
        bool addBrackets = Preferences.Default.LyricsHelperBrackets;
        string phoneticSource = L.S("LyricEdit.PhoneticSource");

        await Task.Delay(150);
        if (_disposed || requestVersion != _suggestionRequestVersion)
        {
            return;
        }

        List<LyricSuggestion> suggestions = await Task.Run(() =>
            BuildSuggestions(lyric, singer, helper, addBrackets, phoneticSource));

        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            if (_disposed || requestVersion != _suggestionRequestVersion)
            {
                return;
            }

            Suggestions.Clear();
            foreach (LyricSuggestion suggestion in suggestions)
            {
                Suggestions.Add(suggestion);
            }

            SuggestionStatus = suggestions.Count > 0
                ? string.Empty
                : singer is not { Found: true, Loaded: true }
                    ? L.S("LyricEdit.NoSinger")
                    : L.S("LyricEdit.NoSuggestions");
        });
    }

    private USinger? GetCurrentSinger()
    {
        UProject project = DocManager.Inst.Project;
        return _part.trackNo >= 0 && _part.trackNo < project.tracks.Count
            ? project.tracks[_part.trackNo].Singer
            : null;
    }

    private static List<LyricSuggestion> BuildSuggestions(
        string lyric,
        USinger? singer,
        ILyricsHelper? helper,
        bool addBrackets,
        string phoneticSource)
    {
        List<LyricSuggestion> suggestions = [];
        HashSet<string> seenAliases = [with(StringComparer.Ordinal)];

        // 尝试使用歌词助手转换歌词
        if (!string.IsNullOrEmpty(lyric) && helper != null)
        {
            try
            {
                string converted = helper.Convert(lyric);
                AddSuggestion(converted, helper.Source);
                if (addBrackets && !string.IsNullOrEmpty(converted)) // 如果启用括号选项，则添加带括号的候选
                {
                    AddSuggestion($"[{converted}]", helper.Source);
                }
            }
            catch
            {
                // 单个歌词助手失败时仍然保留音源候选。
            }
        }

        // 尝试使用音源获取候选
        if (singer is { Found: true, Loaded: true })
        {
            try
            {
                foreach (KeyValuePair<string, UOto> pair in singer.GetSuggestions(lyric, false).Take(32))
                {
                    UOto oto = pair.Value;
                    string source = string.IsNullOrEmpty(oto.Set)
                        ? singer.Id
                        : string.Equals(pair.Key, oto.Alias, StringComparison.Ordinal)
                            ? oto.Set
                            : phoneticSource;
                    AddSuggestion(pair.Key, source);
                }
            }
            catch
            {
                // 音源候选失败不应中断歌词编辑。
            }
        }

        return suggestions;

        void AddSuggestion(string alias, string source)
        {
            if (!string.IsNullOrEmpty(alias) && seenAliases.Add(alias))
            {
                suggestions.Add(new LyricSuggestion(alias, source));
            }
        }
    }

    /// <summary>
    /// 加载当前音符信息：预加载歌词，更新 UI 显示
    /// </summary>
    private void LoadCurrentNote()
    {
        if (_currentNoteIndex >= 0 && _currentNoteIndex < _part.notes.Count)
        {
            CurrentNote = _part.notes.ElementAt(_currentNoteIndex);
            CurrentLyric = CurrentNote.lyric;
            CurrentNoteInfo = string.Format(L.S("LyricEdit.NoteInfo"), _currentNoteIndex + 1, _part.notes.Count);

            // 更新"下一个"按钮提示
            int nextIndex = _currentNoteIndex + 1;
            if (nextIndex < _part.notes.Count)
            {
                NextButtonTooltip = string.Format(L.S("LyricEdit.NextNote"), _part.notes.ElementAt(nextIndex).lyric);
            }
            else
            {
                NextButtonTooltip = L.S("LyricEdit.LastNote");
            }

            // 触发焦点请求事件
            FocusRequested?.Invoke();
        }
    }

    /// <summary>
    /// 检查是否存在下一个音符
    /// </summary>
    private bool HasNext => _currentNoteIndex + 1 < _part.notes.Count;

    /// <summary>
    /// 保存当前音符的歌词编辑（执行命令）
    /// </summary>
    private void SaveCurrentNoteEdit()
    {
        if (CurrentNote == null) return;

        // 仅在歌词有改变时执行命令
        if (CurrentNote.lyric != CurrentLyric)
        {
            DocManager.Inst.StartUndoGroup();
            DocManager.Inst.ExecuteCmd(new ChangeNoteLyricCommand(_part, CurrentNote, CurrentLyric));
            DocManager.Inst.EndUndoGroup();
        }
    }

    /// <summary>
    /// 取消按钮：直接关闭
    /// </summary>
    private void OnCancel()
    {
        RaiseClose(null);
    }

    /// <summary>
    /// "下一个"按钮：保存当前，移动到下一个，若无下一个则关闭
    /// </summary>
    private void OnNext()
    {
        SaveCurrentNoteEdit();

        if (HasNext)
        {
            _currentNoteIndex++;
            LoadCurrentNote();
        }
        else
        {
            // 无下一个音符，关闭弹窗
            RaiseClose(null);
        }
    }

    /// <summary>
    /// 确认按钮：保存当前并关闭
    /// </summary>
    private void OnConfirm()
    {
        SaveCurrentNoteEdit();
        RaiseClose(null);
    }

    /// <summary>
    /// 实现 IDialogContext 接口
    /// </summary>
    public override void RequestBack()
    {
        RaiseClose(null);
    }

    /// <summary>
    /// 实现 IDisposable 接口，释放所有命令资源
    /// </summary>
    public void Dispose()
    {
        _disposed = true;
        Interlocked.Increment(ref _suggestionRequestVersion);
        _disposables.Dispose();
        CancelCommand.Dispose();
        NextCommand.Dispose();
        ConfirmCommand.Dispose();
        GC.SuppressFinalize(this);
    }
}
