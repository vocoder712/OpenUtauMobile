using System.Reactive;
using OpenUtauMobile.Helpers;
using OpenUtauMobile.Services;
using ReactiveUI;
using ReactiveUI.Fody.Helpers;

namespace OpenUtauMobile.ViewModels;

/// <summary>
/// 工程信息编辑弹窗的共用 ViewModel。
/// 支持三种模式：BPM / 拍号 / 音名，由 <see cref="Mode"/> 区分。
/// 取消时返回 false。
/// </summary>
public class ProjectInfoEditViewModel : PopupViewModelBase
{
    public enum EditMode
    {
        Bpm,
        TimeSignature,
        Key
    }

    // ── 公共属性 ──────────────────────────────────────────────────────
    public EditMode Mode { get; }

    // ── BPM 模式 ─────────────────────────────────────────────────────
    /// <summary>BPM 输入框文本（用户自由输入 double）。</summary>
    [Reactive]
    public string BpmText { get; set; } = "120";

    // ── 拍号模式 ─────────────────────────────────────────────────────
    /// <summary>拍号分子输入框文本。</summary>
    [Reactive]
    public string BeatPerBarText { get; set; } = "4";

    /// <summary>拍号分母输入框文本。</summary>
    [Reactive]
    public string BeatUnitText { get; set; } = "4";

    // ── 音名模式 ─────────────────────────────────────────────────────
    /// <summary>当前选中的 key 索引 (0=C … 11=B)。由音名按钮点击后立即关闭弹窗，无需单独确认。</summary>
    [Reactive]
    public int SelectedKey { get; set; }

    // ── 命令 ─────────────────────────────────────────────────────────
    /// <summary>确认命令（BPM / 拍号模式专用）。</summary>
    public ReactiveCommand<Unit, Unit> ConfirmCommand { get; }

    /// <summary>取消命令。</summary>
    public ReactiveCommand<Unit, Unit> CancelCommand { get; }

    /// <summary>音名按钮点击命令，参数为 key 索引字符串 "0"-"11"（AXAML CommandParameter 只能传 string）。</summary>
    public ReactiveCommand<string, Unit> SelectKeyCommand { get; }

    public ProjectInfoEditViewModel(EditMode mode, string currentValue)
    {
        Mode = mode;

        // 用当前工程值预填
        switch (mode)
        {
            case EditMode.Bpm:
                BpmText = currentValue;
                break;
            case EditMode.TimeSignature:
                {
                    // currentValue 格式 "4/4"
                    string[] parts = currentValue.Split('/');
                    if (parts.Length == 2)
                    {
                        BeatPerBarText = parts[0].Trim();
                        BeatUnitText = parts[1].Trim();
                    }

                    break;
                }
            case EditMode.Key:
                // currentValue 格式 "1 = C"，我们只需要知道当前索引
                // 从 MusicMath 反查
                if (int.TryParse(currentValue, out int keyIdx))
                    SelectedKey = keyIdx;
                break;
        }

        ConfirmCommand = ReactiveCommand.Create(OnConfirm);
        CancelCommand = ReactiveCommand.Create(Cancel);
        SelectKeyCommand = ReactiveCommand.Create<string>(OnSelectKey);
    }

    private void OnConfirm()
    {
        switch (Mode)
        {
            case EditMode.Bpm:
                {
                    if (!double.TryParse(BpmText, System.Globalization.NumberStyles.Any,
                            System.Globalization.CultureInfo.InvariantCulture, out double bpm)
                        || bpm < 10 || bpm > 1000)
                    {
                        ToastService.Enqueue(L.S("ProjectEdit.BpmRange"));
                        return;
                    }

                    break;
                }
            case EditMode.TimeSignature:
                {
                    if (!int.TryParse(BeatPerBarText, out int bpb) || bpb < 1 || bpb > 32)
                    {
                        ToastService.Enqueue(L.S("ProjectEdit.Toast.BeatsValidation"));
                        return;
                    }

                    if (!int.TryParse(BeatUnitText, out int bu) || bu < 1 || bu > 32
                        || (bu & (bu - 1)) != 0) // 必须是 2 的幂次（1,2,4,8,16,32）
                    {
                        ToastService.Enqueue(L.S("ProjectEdit.Toast.NoteValueValidation"));
                        return;
                    }

                    break;
                }
        }

        RaiseClose(true);
    }

    /// <summary>音名模式：点击音名按钮直接确认关闭。</summary>
    private void OnSelectKey(string keyStr)
    {
        if (int.TryParse(keyStr, out int key))
            SelectedKey = key;
        RaiseClose(true);
    }

    public void Cancel()
    {
        RaiseClose(false);
    }

    public override void RequestBack() => Cancel();


    // ── 便捷只读解析属性（供调用方在弹窗关闭后读取结果） ─────────────
    /// <summary>解析后的 BPM 值。仅在 Mode==Bpm 且校验通过后有效。</summary>
    public double ParsedBpm =>
        double.TryParse(BpmText, System.Globalization.NumberStyles.Any,
            System.Globalization.CultureInfo.InvariantCulture, out double v)
            ? v
            : 120;

    /// <summary>解析后的拍号分子。仅在 Mode==TimeSignature 且校验通过后有效。</summary>
    public int ParsedBeatPerBar =>
        int.TryParse(BeatPerBarText, out int v) ? v : 4;

    /// <summary>解析后的拍号分母。仅在 Mode==TimeSignature 且校验通过后有效。</summary>
    public int ParsedBeatUnit =>
        int.TryParse(BeatUnitText, out int v) ? v : 4;
}