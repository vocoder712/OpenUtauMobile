using System;
using System.Reactive.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using OpenUtauMobile.Services.Warnings;

namespace OpenUtauMobile.Controls.Warnings
{
    /// <summary>中心与顶部消息共用正文，顶部消息另提供关闭、忽略与边缘倒计时。</summary>
    public partial class WarningCard : UserControl
    {
        public event Action<WarningItem>? Ignore;
        public event Action<WarningItem>? CloseRequested;
        public event EventHandler? SwipeStarted;
        public event Action<WarningCard, WarningItem>? DismissRequested;
        private IDisposable? _shadowSubscription;
        private Avalonia.Media.Color _shadowColor;

        /// <summary>创建中心与顶部提示共用的卡片正文。</summary>
        public WarningCard()
        {
            InitializeComponent();
            InitializeSwipe();
        }

        /// <summary>仅中心卡片允许水平滑除；顶部卡片仍使用忽略操作。</summary>
        public bool CanSwipeDismiss { get; set; }

        /// <summary>收束动画复用卡片实际的主题表面色。</summary>
        internal Avalonia.Media.IBrush? SurfaceBackground => CardSurface.Background;

        protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
        {
            base.OnAttachedToVisualTree(e);
            _shadowSubscription = this.GetResourceObservable("Sem.Value.Shadow").Subscribe(value =>
            {
                if (value is Avalonia.Media.Color color)
                {
                    _shadowColor = color;
                    UpdateSwipeElevation();
                }
            });
        }

        protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
        {
            CancelSwipe();
            _shadowSubscription?.Dispose();
            _shadowSubscription = null;
            base.OnDetachedFromVisualTree(e);
        }

        /// <summary>只有顶部到达消息才显示忽略动作。</summary>
        public bool ShowIgnore
        {
            get => IgnoreButton.IsVisible;
            set => IgnoreButton.IsVisible = value;
        }

        /// <summary>顶部提示的即时关闭入口，标题为按钮预留空间，不影响中心卡片。</summary>
        public bool ShowClose
        {
            get => CloseArrivalButton.IsVisible;
            set
            {
                CloseArrivalButton.IsVisible = value;
                TitleText.Margin = value ? new Thickness(0, 0, 44, 0) : default;
            }
        }

        /// <summary>只有顶部到达消息显示边缘倒计时，中心卡片保持原有外观。</summary>
        public bool ShowCountdown
        {
            get => CountdownEdge.IsVisible;
            set => CountdownEdge.IsVisible = value;
        }

        /// <summary>当前驻留时间的剩余比例；与实际暂停、恢复和重播共用同一计时。</summary>
        public double RemainingFraction
        {
            get => ((ScaleTransform)CountdownEdge.RenderTransform!).ScaleX;
            set => ((ScaleTransform)CountdownEdge.RenderTransform!).ScaleX = Math.Clamp(value, 0, 1);
        }

        private void IgnoreClicked(object? sender, RoutedEventArgs e)
        {
            if (DataContext is WarningItem item) Ignore?.Invoke(item);
            e.Handled = true;
        }

        private void CloseArrivalClicked(object? sender, RoutedEventArgs e)
        {
            if (DataContext is WarningItem item) CloseRequested?.Invoke(item);
            e.Handled = true;
        }
    }
}
