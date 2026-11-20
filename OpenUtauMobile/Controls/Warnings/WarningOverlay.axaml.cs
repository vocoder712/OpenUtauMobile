using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Platform;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using Avalonia.VisualTree;
using OpenUtau.Core.Util;
using OpenUtauMobile.Helpers;
using OpenUtauMobile.Services.Warnings;
using Serilog;
using SkiaSharp;

namespace OpenUtauMobile.Controls.Warnings
{
    /// <summary>导航页上方的告警展示会话，协调输入、消息队列及所有可取消动画。</summary>
    public partial class WarningOverlay : UserControl
    {
        private readonly ObservableCollection<WarningItem> _centerItems = [];
        private WarningInbox? _inbox;
        private TopLevel? _top;
        private IInsetsManager? _insets;
        private ContentControl? _page;
        private CancellationTokenSource? _arrivalAnimation;
        private CancellationTokenSource? _orbAnimation;
        private CancellationTokenSource? _centerAnimation;
        private CancellationTokenSource? _elasticAnimation;
        private Bitmap? _snapshot;
        private WarningItem? _arriving;
        private IPointer? _orbPointer;
        private Point _pressPoint;
        private Point _pressOrigin;
        private Point _orbPosition;
        private bool _dragging;
        private bool _dockedVisible;
        private bool _right = true;
        private double _yRatio = 0.6;
        private bool _closing;
        private bool _clearing;
        private bool _attached;
        private bool _presentationReady = true;
        private InputElement? _previousFocus;
        private bool _pageWasEnabled;
        private ScrollViewer? _scroll;
        private IPointer? _elasticPointer;
        private double _elasticStart;
        private double _elasticStartX;
        private double _elasticLastPoint;
        private bool _elasticCaptured;
        private double _stretch;
        private double _elasticPull;
        private double _elasticVelocity;
        private long _elasticLastInput;
        private bool _resourceRefreshQueued;
        private Rect _lastUsable;

        /// <summary>包含开场、关闭与清除动画在内的完整模态会话。</summary>
        public bool IsCenterOpen => Center.IsVisible;
        public event Action<bool>? CenterVisibilityChanged;

        /// <summary>建立功能层的局部输入路由，不监听页面上的起始手势。</summary>
        public WarningOverlay()
        {
            InitializeComponent();
            ClearButton.PropertyChanged += ClearFabStateChanged;
            WarningItems.ItemsSource = _centerItems;
            ArrivalCard.ShowIgnore = true;
            ArrivalCard.ShowClose = true;
            ArrivalCard.ShowCountdown = true;
            ArrivalCard.Ignore += IgnoreArrival;
            ArrivalCard.CloseRequested += CloseArrival;
            ArrivalCard.AddHandler(PointerPressedEvent, ResetArrivalCountdown, RoutingStrategies.Tunnel, true);
            // 键盘处理只作用于中心自身，不拦截上层弹窗或提示的输入。
            Center.AddHandler(KeyDownEvent, CenterKeyDown, RoutingStrategies.Bubble, true);
            Center.AddHandler(KeyUpEvent, CenterKeyUp, RoutingStrategies.Bubble, true);
            Orb.AddHandler(PointerPressedEvent, OrbPressed, RoutingStrategies.Tunnel);
            Orb.AddHandler(PointerMovedEvent, OrbMoved, RoutingStrategies.Tunnel);
            Orb.AddHandler(PointerReleasedEvent, OrbReleased, RoutingStrategies.Tunnel);
            Orb.PointerCaptureLost += (_, _) => { if (_orbPointer != null) CancelOrb(); };
            WarningItems.AddHandler(PointerPressedEvent, ElasticPressed, RoutingStrategies.Tunnel, true);
            WarningItems.AddHandler(PointerMovedEvent, ElasticMoved, RoutingStrategies.Tunnel, true);
            WarningItems.AddHandler(PointerReleasedEvent, ElasticReleased, RoutingStrategies.Tunnel, true);
            WarningItems.AddHandler(PointerCaptureLostEvent, ElasticCaptureLost, RoutingStrategies.Bubble, true);
            WarningItems.AddHandler(ScrollGestureEvent, ElasticGesture, RoutingStrategies.Bubble, true);
            WarningItems.AddHandler(ScrollGestureEndedEvent, ElasticGestureEnded, RoutingStrategies.Bubble, true);
            WarningItems.AddHandler(PointerWheelChangedEvent, ElasticWheel, RoutingStrategies.Bubble, true);
            SizeChanged += (_, _) => QueueViewportRefresh();
            ActualThemeVariantChanged += (_, _) => QueueViewportRefresh();
        }

        /// <summary>只绑定导航页本身，快照绝不捕获告警或已有弹窗。</summary>
        public void SetPageHost(ContentControl page) => _page = page;

        /// <summary>启动页期间只积压告警，导航内容就绪后才开始顶部提示的展示和计时。</summary>
        public void SetPresentationReady(bool ready)
        {
            if (_presentationReady == ready) return;
            _presentationReady = ready;
            if (!ready) StopArrival();
            if (!_attached) return;
            UpdateOrb();
            QueueViewportRefresh();
        }

        protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
        {
            base.OnAttachedToVisualTree(e);
            _attached = true;
            AttachCardAppearance();
            _top = TopLevel.GetTopLevel(this);
            _insets = _top?.InsetsManager;
            if (_insets != null) _insets.SafeAreaChanged += SafeAreaChanged;
            if (Application.Current != null) Application.Current.ResourcesChanged += OnResourcesChanged;
            _right = Preferences.Default.WarningOrbDockSide != 0;
            _yRatio = double.IsFinite(Preferences.Default.WarningOrbDockYRatio)
                ? Math.Clamp(Preferences.Default.WarningOrbDockYRatio, 0, 1) : 0.6;
            _inbox = WarningInbox.Instance;
            _inbox.Changed += InboxChanged;
            // 已有告警重建宿主时只恢复球，不重复播报已展示的消息。
            _dockedVisible = _inbox.Items.Any(item => item.Presented);
            ViewportChanged();
            InboxChanged();
        }

        protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
        {
            _attached = false;
            _clearShadowSubscription?.Dispose();
            _clearShadowSubscription = null;
            if (_inbox != null) _inbox.Changed -= InboxChanged;
            if (Application.Current != null) Application.Current.ResourcesChanged -= OnResourcesChanged;
            if (_insets != null) _insets.SafeAreaChanged -= SafeAreaChanged;
            StopArrival();
            CancelOrb();
            FinishCenter(false);
            _top = null;
            base.OnDetachedFromVisualTree(e);
        }

        private static void Cancel(ref CancellationTokenSource? source)
        {
            CancellationTokenSource? old = source;
            source = null;
            old?.Cancel();
            old?.Dispose();
        }

        private bool HasWarnings => _inbox?.Items.Count > 0;

        private void InboxChanged()
        {
            if (!_attached || _inbox == null) return;
            if (_centerItems.Count != _inbox.Items.Count || _centerItems.Any(item => !_inbox.Items.Contains(item)))
                CancelCardInteractions();
            if (!_clearing)
            {
                foreach (WarningItem old in _centerItems.Where(item => !_inbox.Items.Contains(item)).ToArray()) _centerItems.Remove(old);
                foreach (WarningItem item in _inbox.Items.Where(item => !_centerItems.Contains(item))) _centerItems.Add(item);
                if (IsCenterOpen && !_closing)
                {
                    foreach (WarningItem item in _inbox.Items) item.Presented = true;
                }
            }
            if (_arriving != null && !_inbox.Items.Contains(_arriving)) StopArrival();
            if (!HasWarnings)
            {
                _dockedVisible = false;
                CancelOrb();
                if (IsCenterOpen && !_clearing) _ = CloseCenterAsync();
            }
            ResetElastic();
            UpdateOrb();
            StartNextArrival();
        }

        private void UpdateOrb()
        {
            Orb.IsVisible = _presentationReady && HasWarnings && _dockedVisible && !IsCenterOpen;
            AutomationProperties.SetName(Orb, string.Format(LocalizationManager.Get("Warning.OrbName"), _inbox?.Items.Count ?? 0));
        }

        /// <summary>计算本地坐标中的安全客户区，避免系统安全边距被重复应用。</summary>
        private Rect UsableRect()
        {
            // TopLevel 已应用的安全内边距不重复扣除；只排除实际落入本层的系统遮挡。
            Thickness safe = _insets?.SafeAreaPadding ?? default;
            Point origin = _top == null ? default : this.TranslatePoint(default, _top) ?? default;
            double left = Math.Max(0, safe.Left - origin.X);
            double top = Math.Max(0, safe.Top - origin.Y);
            double right = _top == null || _top.ClientSize.Width <= 0 ? 0 : Math.Max(0, origin.X + Bounds.Width - (_top.ClientSize.Width - safe.Right));
            double bottom = _top == null || _top.ClientSize.Height <= 0 ? 0 : Math.Max(0, origin.Y + Bounds.Height - (_top.ClientSize.Height - safe.Bottom));
            return new Rect(left, top, Math.Max(1, Bounds.Width - left - right), Math.Max(1, Bounds.Height - top - bottom));
        }

        private Point DockPoint()
        {
            Rect rect = UsableRect();
            return new Point(_right ? Math.Max(rect.Left, rect.Right - 56) : rect.Left,
                rect.Top + 8 + Math.Max(0, rect.Height - 72) * _yRatio);
        }

        private void PlaceOrb(Point point)
        {
            _orbPosition = point;
            // 平移实际绘制和命中位置，不在磁吸每帧重新布局整个覆盖层。
            Orb.RenderTransform = new TranslateTransform(point.X, point.Y);
            Orb.RenderTransformOrigin = new RelativePoint(0, 0, RelativeUnit.Absolute);
        }

        /// <summary>只有在球内开始的主指针可以建立拖动会话。</summary>
        private void OrbPressed(object? sender, PointerPressedEventArgs e)
        {
            if (_orbPointer != null || IsCenterOpen ||
                (e.Pointer.Type != PointerType.Touch && !e.GetCurrentPoint(Orb).Properties.IsLeftButtonPressed)) return;
            e.PreventGestureRecognition();
            Cancel(ref _orbAnimation);
            _orbPointer = e.Pointer;
            Orb.IsPointerHeld = true;
            _pressPoint = e.GetPosition(this);
            _pressOrigin = _orbPosition;
            _dragging = false;
            Orb.Focus(NavigationMethod.Pointer);
            // 仅捕获已从球轮廓开始的指针，球外手势不会到达这里。
            e.Pointer.Capture(Orb);
            e.Handled = true;
        }

        /// <summary>超过阈值才拖动，保持按下点偏移并限制在安全区。</summary>
        private void OrbMoved(object? sender, PointerEventArgs e)
        {
            if (e.Pointer != _orbPointer) return;
            Vector delta = e.GetPosition(this) - _pressPoint;
            if (!_dragging && delta.Length < 8) return;
            _dragging = true;
            Orb.Dock = 0;
            Rect rect = UsableRect();
            PlaceOrb(new Point(Math.Clamp(_pressOrigin.X + delta.X, rect.Left, Math.Max(rect.Left, rect.Right - 56)),
                Math.Clamp(_pressOrigin.Y + delta.Y, rect.Top + 8, Math.Max(rect.Top + 8, rect.Bottom - 64))));
            e.Handled = true;
        }

        /// <summary>把点击与拖放分开；选定近侧后仅在吸附稳定时保存。</summary>
        private void OrbReleased(object? sender, PointerReleasedEventArgs e)
        {
            if (e.Pointer != _orbPointer) return;
            _orbPointer = null;
            Orb.IsPointerHeld = false;
            e.Pointer.Capture(null);
            e.Handled = true;
            if (!_dragging)
            {
                PlaceOrb(DockPoint());
                Orb.Dock = _right ? 1 : -1;
                OpenCenter();
                return;
            }
            _dragging = false;
            Rect rect = UsableRect();
            double midpoint = rect.Center.X;
            if (Math.Abs(_orbPosition.X + 28 - midpoint) > 0.01) _right = _orbPosition.X + 28 > midpoint;
            _yRatio = Math.Clamp((_orbPosition.Y - rect.Top - 8) / Math.Max(1, rect.Height - 72), 0, 1);
            _ = SnapAsync(true);
        }

        private async Task SnapAsync(bool save)
        {
            Cancel(ref _orbAnimation);
            CancellationTokenSource session = _orbAnimation = new();
            Point from = _orbPosition;
            double dockFrom = Orb.Dock;
            Point target = DockPoint();
            try
            {
                await WarningMotion.Run(420, t =>
                {
                    double position = WarningMotion.Spring(t);
                    PlaceOrb(new Point(WarningMotion.Lerp(from.X, target.X, position), WarningMotion.Lerp(from.Y, target.Y, position)));
                    Orb.Dock = WarningMotion.Lerp(dockFrom, _right ? 1 : -1, WarningMotion.Ease(t));
                }, session.Token);
                if (save)
                {
                    Preferences.Default.WarningOrbDockSide = _right ? 1 : 0;
                    Preferences.Default.WarningOrbDockYRatio = _yRatio;
                    Preferences.Save();
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex) { Log.Warning(ex, "告警悬浮球位置保存失败"); }
            finally
            {
                if (_orbAnimation == session)
                {
                    Cancel(ref _orbAnimation);
                }
            }
        }

        private void CancelOrb()
        {
            Cancel(ref _orbAnimation);
            IPointer? pointer = _orbPointer;
            _orbPointer = null;
            Orb.IsPointerHeld = false;
            pointer?.Capture(null);
            _dragging = false;
            PlaceOrb(DockPoint());
            Orb.Dock = _right ? 1 : -1;
        }

        private void OrbClicked(object? sender, RoutedEventArgs e) => OpenCenter();

        /// <summary>返回键在中心开关及清除过程中均被消费，不进入导航栈。</summary>
        public bool RequestBack()
        {
            if (!IsCenterOpen) return false;
            if (!_clearing) _ = CloseCenterAsync();
            return true;
        }

        private void OpenCenter()
        {
            if (!_presentationReady || !HasWarnings || IsCenterOpen || _page == null || _inbox == null) return;
            WarningInbox.BeginCenter();
            _previousFocus = _top?.FocusManager?.GetFocusedElement() as InputElement;
            CancelOrb();
            StopArrival();
            foreach (WarningItem item in _inbox.Items) item.Presented = true;
            _dockedVisible = true;
            CapturePage();
            _pageWasEnabled = _page.IsEnabled;
            _page.IsEnabled = false;
            Center.IsVisible = true;
            Center.Opacity = 0;
            CenterVisibilityChanged?.Invoke(true);
            CloseButton.Focus();
            UpdateOrb();
            _ = EnterCenterAsync();
        }

        private async Task EnterCenterAsync()
        {
            Cancel(ref _centerAnimation);
            CancellationTokenSource session = _centerAnimation = new();
            try
            {
                await WarningMotion.Run(200, t => Center.Opacity = WarningMotion.Ease(t), session.Token);
                SetupScroll();
            }
            catch (OperationCanceledException) { }
        }

        private void SetupScroll()
        {
            _scroll = WarningItems.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault();
        }

        /// <summary>只捕获有限像素的导航页，一次性模糊快照，不依赖显示控件的实时特效。</summary>
        private void CapturePage()
        {
            if (_page == null || _page.Bounds.Width < 1 || _page.Bounds.Height < 1) return;
            try
            {
                double width = _page.Bounds.Width;
                double height = _page.Bounds.Height;
                double scale = Math.Min((_top?.RenderScaling ?? 1) * 0.5,
                    Math.Min(1280 / Math.Max(width, height), Math.Sqrt(1_000_000 / (width * height))));
                using RenderTargetBitmap snapshot = new(new PixelSize(Math.Max(1, (int)Math.Ceiling(width * scale)),
                    Math.Max(1, (int)Math.Ceiling(height * scale))), new Vector(96 * scale, 96 * scale));
                snapshot.Render(_page);
                Bitmap blurred = BlurSnapshot(snapshot, scale);
                Bitmap? previous = _snapshot;
                _snapshot = blurred;
                SnapshotImage.Source = blurred;
                previous?.Dispose();
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "告警中心导航页快照更新失败，保留已有快照");
            }
        }

        /// <summary>在受限分辨率的像素缓冲区内模糊，并补齐透明背景，避免下层原画面透出。</summary>
        private Bitmap BlurSnapshot(RenderTargetBitmap snapshot, double scale)
        {
            PixelSize size = snapshot.PixelSize;
            using WriteableBitmap pixels = new(size, snapshot.Dpi, PixelFormat.Bgra8888, AlphaFormat.Premul);
            using ILockedFramebuffer buffer = pixels.Lock();
            snapshot.CopyPixels(buffer);
            SKImageInfo info = new(size.Width, size.Height, SKColorType.Bgra8888, SKAlphaType.Premul);
            using SKImage source = SKImage.FromPixels(info, buffer.Address, buffer.RowBytes);
            using SKSurface surface = SKSurface.Create(info);
            Color background = this.FindResource("Sem.Value.Surface") is Color color ? color : Colors.Transparent;
            surface.Canvas.Clear(new SKColor(background.R, background.G, background.B, 255));
            float sigma = (float)(12 * scale);
            using SKImageFilter blur = SKImageFilter.CreateBlur(sigma, sigma, SKShaderTileMode.Clamp);
            using SKPaint paint = new() { ImageFilter = blur };
            surface.Canvas.DrawImage(source, 0, 0, paint);
            using SKImage blurred = surface.Snapshot();
            using SKPixmap result = blurred.PeekPixels();
            // 降采样只改变像素数；显示时由 Image.Stretch 拉伸，位图保持标准 DPI，避免源区域越界。
            return new Bitmap(PixelFormat.Bgra8888, AlphaFormat.Premul, result.GetPixels(),
                size, new Vector(96, 96), result.RowBytes);
        }

        private void ReleaseSnapshot()
        {
            SnapshotImage.Source = null;
            _snapshot?.Dispose();
            _snapshot = null;
        }

        private async Task CloseCenterAsync()
        {
            if (!IsCenterOpen || _closing || _clearing) return;
            _closing = true;
            Cancel(ref _centerAnimation);
            ResetElastic();
            CancelCardInteractions();
            WarningItems.IsHitTestVisible = false;
            CancellationTokenSource session = _centerAnimation = new();
            double from = Center.Opacity;
            try
            {
                await WarningMotion.Run(170, t => Center.Opacity = from * (1 - t), session.Token);
                FinishCenter(true);
            }
            catch (OperationCanceledException) { }
        }

        private void FinishCenter(bool restoreFocus)
        {
            Cancel(ref _centerAnimation);
            ResetElastic();
            CancelCardInteractions();
            bool wasOpen = IsCenterOpen;
            bool ownedFocus = IsKeyboardFocusWithin;
            Center.IsVisible = false;
            Center.Opacity = 1;
            CenterContent.IsEnabled = true;
            WarningItems.IsHitTestVisible = true;
            UpdateClearElevation();
            _closing = _clearing = false;
            foreach (Control container in WarningItems.GetRealizedContainers())
            {
                container.RenderTransform = null;
                container.Opacity = 1;
            }
            ReleaseSnapshot();
            if (wasOpen)
            {
                if (_page != null) _page.IsEnabled = _pageWasEnabled;
                CenterVisibilityChanged?.Invoke(false);
                if (restoreFocus && ownedFocus && _previousFocus is { IsEffectivelyEnabled: true, IsEffectivelyVisible: true } previous &&
                    TopLevel.GetTopLevel(previous) == _top) previous.Focus();
            }
            _previousFocus = null;
            if (_attached) InboxChanged();
        }

        private void CloseClicked(object? sender, RoutedEventArgs e) => RequestBack();

        private async void ClearClicked(object? sender, RoutedEventArgs e)
        {
            if (_clearing || _closing || _inbox == null) return;
            Cancel(ref _centerAnimation);
            Center.Opacity = 1;
            ResetElastic();
            CancelCardInteractions();
            _clearing = true;
            CenterContent.IsEnabled = false;
            UpdateClearElevation();
            WarningItem[] cleared = _inbox.Items.ToArray();
            // 只清除点击时这一代，随后重发的同键通知也不会被动画结尾删除。
            Control[] cards = VisibleContainers().OrderByDescending(CardY).ToArray();
            foreach (WarningItem item in cleared) _inbox.Dismiss(item);
            CancellationTokenSource session = _centerAnimation = new();
            double stagger = cards.Length <= 1 ? 0 : Math.Min(65, 500.0 / (cards.Length - 1));
            double total = 300 + Math.Max(0, cards.Length - 1) * stagger;
            try
            {
                await WarningMotion.Run((int)total, t =>
                {
                    for (int i = 0; i < cards.Length; i++)
                    {
                        double local = Math.Clamp((t * total - i * stagger) / 300, 0, 1);
                        cards[i].RenderTransform = new TranslateTransform(WarningMotion.Ease(local) * (Bounds.Width + 40), 0);
                        cards[i].Opacity = 1 - local;
                    }
                }, session.Token);
                await WarningMotion.Run(150, t => Center.Opacity = 1 - t, session.Token);
                FinishCenter(true);
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                Log.Warning(ex, "告警清除动画中止");
                FinishCenter(true);
            }
        }

        private Control[] VisibleContainers() => WarningItems.GetRealizedContainers().Where(control =>
        {
            double y = CardY(control);
            return y + control.Bounds.Height >= 0 && y <= WarningItems.Bounds.Height;
        }).ToArray();

        private double CardY(Control card) => card.TranslatePoint(default, WarningItems)?.Y ?? 0;

        /// <summary>中心只处理从自身内容冒泡的按键，保留上层浮层独立的焦点与返回行为。</summary>
        private void CenterKeyDown(object? sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape && !e.Handled) RequestBack();
            e.Handled = true;
        }

        /// <summary>中心内的按键释放不继续触发导航页快捷键。</summary>
        private void CenterKeyUp(object? sender, KeyEventArgs e) => e.Handled = true;

        private void SafeAreaChanged(object? sender, SafeAreaChangedArgs e) => ViewportChanged();

        private void ViewportChanged()
        {
            if (!_attached) return;
            Rect rect = UsableRect();
            bool geometryChanged = rect != _lastUsable;
            _lastUsable = rect;
            if (geometryChanged)
            {
                CancelOrb();
                CancelCardInteractions();
            }
            CenterContent.Margin = new Thickness(rect.Left, rect.Top, Math.Max(0, Bounds.Width - rect.Right), Math.Max(0, Bounds.Height - rect.Bottom));
            ArrivalCard.Width = Math.Max(1, Math.Min(560, rect.Width - 32));
            ArrivalCard.MaxHeight = Math.Max(64, rect.Height - 32);
            Canvas.SetLeft(ArrivalCard, rect.Center.X - ArrivalCard.Width / 2);
            Canvas.SetTop(ArrivalCard, rect.Top + 16);
            if (_arriving != null && geometryChanged)
            {
                // 初次布局和安全区可能连续变化，重播未完成的提示，不能直接当作已经展示。
                StopArrival();
            }
            if (IsCenterOpen && !_clearing)
            {
                // 禁用状态不参与截图；先恢复原状态捕获，再立即恢复模态门禁。
                if (_page != null) _page.IsEnabled = _pageWasEnabled;
                CapturePage();
                if (_page != null) _page.IsEnabled = false;
            }
            UpdateOrb();
            StartNextArrival();
        }

        private void OnResourcesChanged(object? sender, ResourcesChangedEventArgs e)
        {
            if (_inbox != null) foreach (WarningItem item in _inbox.Items) item.RefreshLanguage();
            QueueViewportRefresh();
        }

        /// <summary>等顶层客户区完成本轮布局后重算，避免首次布局读到旧的零尺寸。</summary>
        private void QueueViewportRefresh()
        {
            // 资源合并可能连续触发，合并到下一次布局回合。
            if (_resourceRefreshQueued) return;
            _resourceRefreshQueued = true;
            Dispatcher.UIThread.Post(() =>
            {
                _resourceRefreshQueued = false;
                if (_attached) ViewportChanged();
            }, DispatcherPriority.Background);
        }
    }
}
