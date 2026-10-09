using System;
using System.Collections;
using System.Collections.Specialized;
using System.Linq;
using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace OpenUtauMobile.Themes.OpenUtauMobile.Controls;

/// <summary>下拉框读取独立快照，集合变更在当前选择事务结束后统一送达。</summary>
public sealed class ComboBoxSourceSnapshot : AvaloniaObject
{
    public static readonly AttachedProperty<bool> IsEnabledProperty =
        AvaloniaProperty.RegisterAttached<ComboBoxSourceSnapshot, ComboBox, bool>("IsEnabled");
    private static readonly ConditionalWeakTable<ComboBox, State> States = new();

    static ComboBoxSourceSnapshot()
    {
        IsEnabledProperty.Changed.AddClassHandler<ComboBox>((control, change) =>
        {
            if (change.NewValue is true) States.GetValue(control, combo => new State(combo));
            else if (States.TryGetValue(control, out State? state))
            {
                state.Dispose();
                States.Remove(control);
            }
        });
    }

    public static bool GetIsEnabled(ComboBox control) => control.GetValue(IsEnabledProperty);
    public static void SetIsEnabled(ComboBox control, bool value) => control.SetValue(IsEnabledProperty, value);

    private sealed class State : IDisposable
    {
        private readonly ComboBox _control;
        private BufferedItems? _items;
        private bool _assigning;

        public State(ComboBox control)
        {
            _control = control;
            control.PropertyChanged += OnPropertyChanged;
            control.AttachedToVisualTree += OnAttached;
            control.DetachedFromVisualTree += OnDetached;
            SetSource();
        }

        private void OnPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs change)
        {
            if (change.Property == ItemsControl.ItemsSourceProperty && !_assigning) SetSource();
        }

        private void SetSource()
        {
            IEnumerable? source = _control.ItemsSource;
            if (ReferenceEquals(source, _items)) return;
            _items?.Dispose();
            _items = source == null ? null : new BufferedItems(source);
            if (_items == null) return;
            _assigning = true;
            try
            {
                // SetCurrentValue 保留调用方的 ItemsSource 绑定，不把快照回写给 ViewModel。
                _control.SetCurrentValue(ItemsControl.ItemsSourceProperty, _items);
            }
            finally { _assigning = false; }
            if (_control.IsAttachedToVisualTree()) _items.Activate();
        }

        private void OnAttached(object? sender, VisualTreeAttachmentEventArgs e) => _items?.Activate();
        private void OnDetached(object? sender, VisualTreeAttachmentEventArgs e) => _items?.Deactivate();

        public void Dispose()
        {
            _control.PropertyChanged -= OnPropertyChanged;
            _control.AttachedToVisualTree -= OnAttached;
            _control.DetachedFromVisualTree -= OnDetached;
            BufferedItems? items = _items;
            _items = null;
            items?.Dispose();
            if (items != null && ReferenceEquals(_control.ItemsSource, items))
                _control.SetCurrentValue(ItemsControl.ItemsSourceProperty, items.Source);
        }
    }

    private sealed class BufferedItems : IList, INotifyCollectionChanged, IDisposable
    {
        private object?[] _snapshot;
        private bool _active;
        private bool _pending;
        private bool _disposed;
        public IEnumerable Source { get; }
        public event NotifyCollectionChangedEventHandler? CollectionChanged;

        public BufferedItems(IEnumerable source)
        {
            Source = source;
            _snapshot = source.Cast<object?>().ToArray();
        }

        public void Activate()
        {
            if (_disposed || _active) return;
            _active = true;
            if (Source is INotifyCollectionChanged changes) changes.CollectionChanged += OnChanged;
            QueueRefresh();
        }

        public void Deactivate()
        {
            if (!_active) return;
            _active = false;
            if (Source is INotifyCollectionChanged changes) changes.CollectionChanged -= OnChanged;
        }

        private void OnChanged(object? sender, NotifyCollectionChangedEventArgs e) => QueueRefresh();

        private void QueueRefresh()
        {
            if (_pending || _disposed) return;
            _pending = true;
            Dispatcher.UIThread.Post(() =>
            {
                _pending = false;
                if (_disposed || !_active) return;
                object?[] latest = Source.Cast<object?>().ToArray();
                if (_snapshot.SequenceEqual(latest)) return;
                _snapshot = latest;
                CollectionChanged?.Invoke(this, new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
            });
        }

        public void Dispose() { Deactivate(); _disposed = true; }
        public int Count => _snapshot.Length;
        public object? this[int index] { get => _snapshot[index]; set => throw new NotSupportedException(); }
        public bool Contains(object? value) => IndexOf(value) >= 0;
        public int IndexOf(object? value) => Array.IndexOf(_snapshot, value);
        public IEnumerator GetEnumerator() => _snapshot.GetEnumerator();
        public void CopyTo(Array array, int index) => _snapshot.CopyTo(array, index);
        public bool IsReadOnly => true;
        public bool IsFixedSize => false;
        public bool IsSynchronized => false;
        public object SyncRoot => this;
        public int Add(object? value) => throw new NotSupportedException();
        public void Clear() => throw new NotSupportedException();
        public void Insert(int index, object? value) => throw new NotSupportedException();
        public void Remove(object? value) => throw new NotSupportedException();
        public void RemoveAt(int index) => throw new NotSupportedException();
    }
}