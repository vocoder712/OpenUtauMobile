using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using Avalonia.Threading;
using OpenUtau.Core;
using OpenUtauMobile.Helpers;

namespace OpenUtauMobile.Services.Warnings
{
    /// <summary>一代告警的展示数据；同键更新不改变身份，清除后重发则创建新身份。</summary>
    public sealed class WarningItem : INotifyPropertyChanged
    {
        private WarningNotification _notification;
        internal bool Presented { get; set; }
        public event PropertyChangedEventHandler? PropertyChanged;
        /// <summary>对应内核告警的唯一键。</summary>
        public string Key => _notification.Key;
        /// <summary>按当前界面语言格式化的标题。</summary>
        public string Title => Format(_notification.TitleKey, true);
        /// <summary>告警详情，保留完整目录和换行。</summary>
        public string Detail => _notification.DetailKey is { } key ? Format(key) : string.Empty;

        internal WarningItem(WarningNotification notification) => _notification = notification;

        internal void Update(WarningNotification notification)
        {
            _notification = notification;
            RefreshLanguage();
        }

        internal void RefreshLanguage()
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Title)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Detail)));
        }

        private string Format(string key, bool title = false)
        {
            string text;
            try
            {
                text = LocalizationManager.Get(key);
            }
            catch (InvalidOperationException)
            {
                // 当前资源解析器对未知键抛出异常，告警卡片局部降级即可。
                text = key;
            }
            if (text == key)
            {
                return title ? LocalizationManager.Get("Warning.FallbackTitle") : string.Join(Environment.NewLine, _notification.Args);
            }
            try
            {
                return string.Format(CultureInfo.CurrentCulture, text, _notification.Args);
            }
            catch (FormatException)
            {
                return text + Environment.NewLine + string.Join(Environment.NewLine, _notification.Args);
            }
        }
    }

    /// <summary>将内核告警投递到界面线程，并在 MainView 重建时保留本次运行的播报状态。</summary>
    public sealed class WarningInbox : ICmdSubscriber
    {
        private static WarningInbox? _instance;
        private readonly List<WarningItem> _items = [];
        /// <summary>界面线程上的应用级告警展示状态。</summary>
        public static WarningInbox Instance => _instance ??= new WarningInbox();
        /// <summary>由内核通知同步的当前展示项。</summary>
        public IReadOnlyList<WarningItem> Items => _items;
        public event Action? Changed;

        /// <summary>告警中心即将成为模态，供编辑器结束正在进行的输入。</summary>
        public static event Action? CenterOpening;

        private WarningInbox()
        {
            Dispatcher.UIThread.VerifyAccess();
            DocManager.Inst.AddSubscriber(this);
            foreach (WarningNotification notification in DocManager.Inst.Warnings)
            {
                _items.Add(new WarningItem(notification));
            }
        }

        /// <summary>按通知发生顺序同步；不把先清除再重发合并成一次同键更新。</summary>
        public void OnNext(UCommand cmd, bool isUndo)
        {
            if (cmd is not (WarningNotification or ClearWarningsNotification)) return;
            if (Dispatcher.UIThread.CheckAccess()) Apply(cmd);
            else Dispatcher.UIThread.Post(() => Apply(cmd));
        }

        private void Apply(UCommand cmd)
        {
            if (cmd is WarningNotification notification)
            {
                WarningItem? existing = _items.FirstOrDefault(item => item.Key == notification.Key);
                if (existing == null) _items.Add(new WarningItem(notification));
                else existing.Update(notification);
            }
            else if (cmd is ClearWarningsNotification clear)
            {
                _items.RemoveAll(item => clear.key == null || item.Key == clear.key);
            }
            Changed?.Invoke();
        }

        internal static void BeginCenter() => CenterOpening?.Invoke();

        /// <summary>通过既有内核通知清除一条；旧展示代次不能清除同键的新告警。</summary>
        public void Dismiss(WarningItem item)
        {
            if (_items.Contains(item)) DocManager.Inst.ExecuteCmd(new ClearWarningsNotification(item.Key));
        }
    }
}
