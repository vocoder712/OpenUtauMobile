using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive;
using System.Reactive.Linq;
using DynamicData.Binding;
using OpenUtau.Api;
using ReactiveUI;
using ReactiveUI.Fody.Helpers;

namespace OpenUtauMobile.ViewModels;

/// <summary>
/// 音素器选择弹窗 ViewModel。
/// 实现 <see cref="IDialogContext"/>，选中音素器后通过 <see cref="RequestClose"/> 返回选中的 <see cref="Phonemizer"/>；
/// 点取消时返回 <c>null</c>。
/// </summary>
public class PhonemizerPickerViewModel : PopupViewModelBase
{
    /// <summary>
    /// 所有分组
    /// </summary>
    [Reactive]
    public ObservableCollectionExtended<KeyValuePair<IGrouping<string, PhonemizerFactory>, string>>
        Groups
    { get; set; } = [];

    /// <summary>
    /// 右侧列表展示的音素器
    /// </summary>
    [Reactive]
    public ObservableCollectionExtended<KeyValuePair<PhonemizerFactory, string>> PhonemizerFactories { get; set; } = [];

    /// <summary>
    /// 当前选中的分组名称
    /// </summary>
    [Reactive]
    public KeyValuePair<IGrouping<string, PhonemizerFactory>, string> CurrentGroup { get; set; }

    /// <summary>
    /// 当前选中的音素器
    /// </summary>
    [Reactive]
    public KeyValuePair<PhonemizerFactory, string>? SelectedFactoryPair { get; set; }

    /// <summary>选中音素器命令：触发关闭并返回该音素器。</summary>
    public ReactiveCommand<Phonemizer, Unit> SelectPhonemizerCommand { get; }

    /// <summary>取消命令：返回 null。</summary>
    public ReactiveCommand<Unit, Unit> CancelCommand { get; }

    public PhonemizerPickerViewModel()
    {
        SelectPhonemizerCommand = ReactiveCommand.Create<Phonemizer>(phonemizer => { RaiseClose(phonemizer); });

        CancelCommand = ReactiveCommand.Create(() => { RaiseClose(null); });

        // 加载音素器分组
        PhonemizerFactory[] factories = PhonemizerFactory.GetAll();
        IEnumerable<IGrouping<string, PhonemizerFactory>> groups = factories
            .GroupBy(f => f.language)
            .OrderBy(group => group.Key ?? string.Empty);
        foreach (IGrouping<string, PhonemizerFactory> group in groups)
        {
            Groups.Add(new KeyValuePair<IGrouping<string, PhonemizerFactory>, string>(group, group.Key ?? "General"));
        }

        // 默认选中第一个分组
        if (Groups.Count > 0)
        {
            CurrentGroup = Groups[0];
        }

        // 当选中分组变化时，更新右侧列表
        this.WhenAnyValue(x => x.CurrentGroup)
            .Subscribe(group =>
            {
                PhonemizerFactories.Clear();
                foreach (PhonemizerFactory factory in group.Key.OrderBy(f => f.name))
                {
                    PhonemizerFactories.Add(new KeyValuePair<PhonemizerFactory, string>(factory, factory.ToString()));
                }
            });

        // 当选中音素器变化时，返回选中的音素器
        this.WhenAnyValue(x => x.SelectedFactoryPair)
            .Where(x => x.HasValue)
            .Subscribe(x =>
            {
                if (x.HasValue)
                {
                    PhonemizerFactory factory = x.Value.Key;
                    Phonemizer phonemizer = factory.Create();
                    SelectPhonemizerCommand.Execute(phonemizer).Subscribe();
                }
            });
    }

    public override void RequestBack()
    {
        RaiseClose(null);
    }
}