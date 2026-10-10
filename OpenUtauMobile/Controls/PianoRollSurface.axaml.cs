using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using OpenUtauMobile.ViewModels;

namespace OpenUtauMobile.Controls
{
    public partial class PianoRollSurface : UserControl
    {
        public Grid LayoutGrid => PART_PianoRollGrid;
        public NotesCanvas NotesCanvas => NotesInputCanvas;
        public Border Ruler => PianoInputRuler;
        public PhonemeParamPanel ParameterPanel => PhonemeParamPanel;
        private bool _desktopResizeGrip;
        private bool _panelClampQueued;
        public void SetDesktopChrome(Control? resizeGrip = null)
        {
            FloatingActions.IsVisible = false;
            if (resizeGrip == null) return;
            _desktopResizeGrip = true;
            PhonemeSplitHandle.Height = 6;
            PhonemeSplitHandle.Margin = new Thickness(0, -3);
            PhonemeSplitHandle.Children.Clear();
            PhonemeSplitHandle.Children.Add(resizeGrip);
            resizeGrip.PointerPressed += OnPhonemeSplitHandlePointerPressed;
            resizeGrip.PointerMoved += OnPhonemeSplitHandlePointerMoved;
            resizeGrip.PointerReleased += OnPhonemeSplitHandlePointerReleased;
            resizeGrip.PointerCaptureLost += OnPhonemeSplitHandlePointerCaptureLost;
        }
        public PianoRollSurface()
        {
            InitializeComponent();
            SizeChanged += (_, _) =>
            {
                if (!_desktopResizeGrip || _panelClampQueued) return;
                _panelClampQueued = true;
                Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                {
                    _panelClampQueued = false;
                    // 导航和布局切换的临时零尺寸不能覆盖用户的面板高度。
                    if (TopLevel.GetTopLevel(this) != null && Bounds.Height > ViewConstants.PianoRollTickRulerHeight + 3 && DataContext is EditorViewModel vm)
                        vm.PianoRollViewModel.PhonemePanelHeight = Math.Min(vm.PianoRollViewModel.PhonemePanelHeight, Math.Max(0, Bounds.Height - ViewConstants.PianoRollTickRulerHeight - 3));
                }, Avalonia.Threading.DispatcherPriority.Loaded);
            };
            DataContextChanged += (_, _) =>
            {
                if (DataContext is EditorViewModel vm) PART_PianoRollGrid.DataContext = vm.PianoRollViewModel;
            };
            PhonemeSplitHandlePill.PointerPressed += OnPhonemeSplitHandlePointerPressed;
            PhonemeSplitHandlePill.PointerMoved += OnPhonemeSplitHandlePointerMoved;
            PhonemeSplitHandlePill.PointerReleased += OnPhonemeSplitHandlePointerReleased;
            PhonemeSplitHandlePill.PointerCaptureLost += OnPhonemeSplitHandlePointerCaptureLost;
        }
        #region 音素与参数面板分割手势

        private bool _phonemeSplitDragging;
        private double _phonemeSplitPressX;
        private double _phonemeSplitPressY;
        private double _phonemeSplitPressHeight;
        private double _phonemeHandleXOffset;
        private double _phonemeSplitPressXOffset;

        private void OnPhonemeSplitHandlePointerPressed(object? sender, PointerPressedEventArgs e)
        {
            if (DataContext is not EditorViewModel vm || !e.GetCurrentPoint(sender as Control ?? PhonemeSplitHandlePill).Properties.IsLeftButtonPressed)
            {
                return;
            }

            Point pos = e.GetPosition(this);

            _phonemeSplitDragging = true;
            _phonemeSplitPressX = pos.X;
            _phonemeSplitPressY = pos.Y;
            _phonemeSplitPressHeight = vm.PianoRollViewModel.PhonemePanelHeight;
            _phonemeSplitPressXOffset = _phonemeHandleXOffset;
            e.Pointer.Capture(sender as Control ?? PhonemeSplitHandlePill);
            e.Handled = true;
        }

        private void OnPhonemeSplitHandlePointerMoved(object? sender, PointerEventArgs e)
        {
            if (!_phonemeSplitDragging || DataContext is not EditorViewModel vm)
            {
                return;
            }

            if (!e.GetCurrentPoint(null).Properties.IsLeftButtonPressed)
            {
                e.Pointer.Capture(null);
                _phonemeSplitDragging = false;
                return;
            }

            Point currentPoint = e.GetPosition(this);
            double deltaY = _phonemeSplitPressY - currentPoint.Y;

            double availableHeight = PART_PianoRollGrid.Bounds.Height > 0 ? PART_PianoRollGrid.Bounds.Height : Bounds.Height;
            double maxPanelHeight = Math.Max(0, availableHeight - ViewConstants.PianoRollTickRulerHeight - (_desktopResizeGrip ? 3 : 0));
            if (maxPanelHeight <= 0 && !_desktopResizeGrip)
            {
                maxPanelHeight = 2000.0;
            }

            double newHeight = Math.Clamp(_phonemeSplitPressHeight + deltaY, 0.0, maxPanelHeight);
            vm.PianoRollViewModel.PhonemePanelHeight = newHeight;

            if (_desktopResizeGrip) { e.Handled = true; return; }
            // 水平滑动药丸手柄位置
            double deltaX = currentPoint.X - _phonemeSplitPressX;
            double maxOffset = Math.Max(10.0, (Bounds.Width - ViewConstants.EditorSplitHandleWidth) * 0.5 - 50.0);
            _phonemeHandleXOffset = Math.Clamp(_phonemeSplitPressXOffset + deltaX, -maxOffset, maxOffset);
            PhonemeSplitHandlePill.RenderTransform = new TranslateTransform(_phonemeHandleXOffset, 0);

            e.Handled = true;
        }

        private void OnPhonemeSplitHandlePointerReleased(object? sender, PointerReleasedEventArgs e)
        {
            if (_phonemeSplitDragging)
            {
                _phonemeSplitDragging = false;
                e.Pointer.Capture(null);
                e.Handled = true;
            }
        }

        private void OnPhonemeSplitHandlePointerCaptureLost(object? sender, PointerCaptureLostEventArgs e)
        {
            _phonemeSplitDragging = false;
        }

        #endregion
    }
}
