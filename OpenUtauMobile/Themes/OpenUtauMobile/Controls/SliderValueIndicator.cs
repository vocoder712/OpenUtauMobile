using System.Collections.Specialized;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.VisualTree;

namespace OpenUtauMobile.Themes.OpenUtauMobile.Controls
{
    /// <summary>滑块选中时显示的数值标签；使用窗口内覆盖层，兼容移动端和浏览器。</summary>
    public class SliderValueIndicator : Popup
    {
        // 调用方提供最终显示文本；未提供文本的滑块不启用气泡。
        public static readonly AttachedProperty<string?> ValueTextProperty =
            AvaloniaProperty.RegisterAttached<SliderValueIndicator, Slider, string?>("ValueText");

        public static readonly StyledProperty<Slider?> SourceProperty =
            AvaloniaProperty.Register<SliderValueIndicator, Slider?>(nameof(Source));

        public static string? GetValueText(Slider slider)
        {
            return slider.GetValue(ValueTextProperty);
        }

        public static void SetValueText(Slider slider, string? value)
        {
            slider.SetValue(ValueTextProperty, value);
        }

        public Slider? Source
        {
            get => GetValue(SourceProperty);
            set => SetValue(SourceProperty, value);
        }

        private Slider? _subscribed;
        private Rect? _viewport;
        private bool _attached;

        public SliderValueIndicator()
        {
            ShouldUseOverlayLayer = true;
            IsLightDismissEnabled = false;
            Focusable = false;
            SetTakesFocusFromNativeControl(this, false);
        }

        protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
        {
            base.OnAttachedToVisualTree(e);
            _attached = true;
            Subscribe();
        }

        protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
        {
            _attached = false;
            Unsubscribe();
            base.OnDetachedFromVisualTree(e);
        }

        protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
        {
            base.OnPropertyChanged(change);
            if (change.Property == SourceProperty)
            {
                Unsubscribe();
                Subscribe();
            }
            else if (change.Property == PlacementTargetProperty)
            {
                UpdateState();
            }
        }

        private void Subscribe()
        {
            if (!_attached || Source == null)
            {
                return;
            }
            _subscribed = Source;
            _subscribed.PropertyChanged += OnSourceChanged;
            _subscribed.Classes.CollectionChanged += OnStateChanged;
            _subscribed.EffectiveViewportChanged += OnViewportChanged;
            UpdateState();
        }

        private void Unsubscribe()
        {
            Close();
            if (_subscribed != null)
            {
                _subscribed.PropertyChanged -= OnSourceChanged;
                _subscribed.Classes.CollectionChanged -= OnStateChanged;
                _subscribed.EffectiveViewportChanged -= OnViewportChanged;
            }
            _subscribed = null;
            _viewport = null;
        }

        private void OnSourceChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
        {
            UpdateState();
        }

        private void OnStateChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            UpdateState();
        }

        private void OnViewportChanged(object? sender, EffectiveViewportChangedEventArgs e)
        {
            _viewport = e.EffectiveViewport;
            UpdateState();
        }

        private void UpdateState()
        {
            Slider? slider = _subscribed;
            if (slider == null || PlacementTarget is not { } thumb)
            {
                Close();
                return;
            }
            bool vertical = slider.Orientation == Orientation.Vertical;
            Placement = vertical ? PlacementMode.Right : PlacementMode.Top;
            HorizontalOffset = vertical ? 8 : 0;
            VerticalOffset = vertical ? 0 : -8;

            // 跟随真实把手坐标，兼容方向反转；滚出视口后不在屏幕边缘留下气泡。
            Point? center = thumb.TranslatePoint(new Point(thumb.Bounds.Width / 2, thumb.Bounds.Height / 2), slider);
            bool inViewport = center.HasValue && (_viewport?.Contains(center.Value) ?? true);
            IsOpen = slider.IsEffectivelyEnabled && slider.IsEffectivelyVisible && inViewport &&
                !string.IsNullOrEmpty(GetValueText(slider)) &&
                (slider.Classes.Contains(":pressed") || slider.Classes.Contains(":focus-visible"));
        }
    }
}
