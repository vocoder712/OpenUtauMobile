using System.Collections.Generic;
using System.Reactive;
using DynamicData.Binding;
using ReactiveUI;
using ReactiveUI.Fody.Helpers;

namespace OpenUtauMobile.ViewModels;

/// <summary>
/// 渲染器选择弹窗 ViewModel。
/// </summary>
public class RendererPickerViewModel : PopupViewModelBase
{
    [Reactive] public ObservableCollectionExtended<string> Renderers { get; set; } = [];
    public ReactiveCommand<string, Unit> SelectRendererCommand { get; }
    public ReactiveCommand<Unit, Unit> CancelCommand { get; }

    public RendererPickerViewModel(IEnumerable<string> renderers)
    {
        Renderers.Load(renderers);

        SelectRendererCommand = ReactiveCommand.Create<string>(RaiseClose);

        CancelCommand = ReactiveCommand.Create(() => { RaiseClose(null); });
    }

    public override void RequestBack()
    {
        RaiseClose(null);
    }
}