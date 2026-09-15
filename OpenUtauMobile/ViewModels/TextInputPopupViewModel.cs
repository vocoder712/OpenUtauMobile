using System;
using System.Reactive;
using ReactiveUI;
using ReactiveUI.Fody.Helpers;

namespace OpenUtauMobile.ViewModels;

/// <summary>通用文本输入；校验失败时保留输入，取消与返回键返回 null。</summary>
public sealed class TextInputPopupViewModel : PopupViewModelBase
{
    public string Title { get; }
    public string Hint { get; }
    public string Watermark { get; }
    public bool HasHint => !string.IsNullOrEmpty(Hint);
    [Reactive] public string Text { get; set; }
    [Reactive] public string? Error { get; private set; }
    public ReactiveCommand<Unit, Unit> ConfirmCommand { get; }
    public ReactiveCommand<Unit, Unit> CancelCommand { get; }
    private readonly Func<string, string?>? _validate;

    public TextInputPopupViewModel(string title, string hint, string watermark, string initialText = "", Func<string, string?>? validate = null)
    {
        Title = title;
        Hint = hint;
        Watermark = watermark;
        Text = initialText;
        _validate = validate;
        ConfirmCommand = ReactiveCommand.Create(() =>
        {
            string value = Text ?? string.Empty;
            Error = _validate?.Invoke(value);
            if (Error == null) RaiseClose(value);
        });
        CancelCommand = ReactiveCommand.Create(() => RaiseClose(null));
        PropertyChanged += (_, e) => { if (e.PropertyName == nameof(Text)) Error = null; };
    }

    public override void RequestBack() => RaiseClose(null);
}
