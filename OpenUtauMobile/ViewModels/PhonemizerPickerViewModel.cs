using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using DynamicData.Binding;
using OpenUtau.Api;
using OpenUtauMobile.Helpers;
using OpenUtauMobile.Services;
using ReactiveUI;
using ReactiveUI.Fody.Helpers;

namespace OpenUtauMobile.ViewModels;

/// <summary>轨道与音符共享列表；初始化当前值不会触发选择提交。</summary>
public sealed class PhonemizerPickerViewModel : PopupViewModelBase, IDisposable
{
    private readonly CompositeDisposable subscriptions = new();
    public ObservableCollectionExtended<KeyValuePair<IGrouping<string, PhonemizerFactory>, string>> Groups { get; } = [];
    public ObservableCollectionExtended<KeyValuePair<PhonemizerFactory, string>> PhonemizerFactories { get; } = [];
    [Reactive] public KeyValuePair<IGrouping<string, PhonemizerFactory>, string> CurrentGroup { get; set; }
    [Reactive] public KeyValuePair<PhonemizerFactory, string>? SelectedFactoryPair { get; set; }
    public bool AllowTrackDefault { get; }
    public bool HasNoFactories => Groups.Count == 0;
    public string TrackDefaultLabel { get; }
    public ReactiveCommand<Unit, Unit> UseTrackDefaultCommand { get; }
    public ReactiveCommand<Unit, Unit> CancelCommand { get; }

    public PhonemizerPickerViewModel() : this(new()) { }

    public PhonemizerPickerViewModel(PhonemizerPickerRequest request)
    {
        AllowTrackDefault = request.AllowTrackDefault;
        TrackDefaultLabel = request.TrackDefaultLabel ?? L.S("NoteProperties.TrackDefault");
        CancelCommand = ReactiveCommand.Create(RequestBack);
        UseTrackDefaultCommand = ReactiveCommand.Create(() =>
        {
            if (AllowTrackDefault) RaiseClose(new PhonemizerPickerResult(null));
        });
        foreach (IGrouping<string, PhonemizerFactory> group in PhonemizerFactory.GetAll()
            .GroupBy(f => f.language ?? string.Empty).OrderBy(g => g.Key))
        {
            Groups.Add(new(group, string.IsNullOrEmpty(group.Key) ? "General" : group.Key));
        }
        PhonemizerFactory? current = NotePhonemizerResolver.Resolve(request.CurrentName);
        if (Groups.Count > 0)
            CurrentGroup = Groups.FirstOrDefault(g => g.Key.Contains(current!), Groups[0]);
        this.WhenAnyValue(x => x.CurrentGroup).Subscribe(group =>
        {
            SelectedFactoryPair = null;
            PhonemizerFactories.Clear();
            if (group.Key == null) return;
            foreach (PhonemizerFactory factory in group.Key.OrderBy(f => f.name))
                PhonemizerFactories.Add(new(factory, factory.ToString()));
        }).DisposeWith(subscriptions);
        // 列表选中保持为空，避免初始绑定关闭弹窗。
        this.WhenAnyValue(x => x.SelectedFactoryPair).Where(pair => pair.HasValue).Subscribe(pair =>
            RaiseClose(new PhonemizerPickerResult(pair!.Value.Key))).DisposeWith(subscriptions);
    }

    public override void RequestBack() => RaiseClose(null);

    public void Dispose()
    {
        subscriptions.Dispose();
        CancelCommand.Dispose();
        UseTrackDefaultCommand.Dispose();
    }
}
