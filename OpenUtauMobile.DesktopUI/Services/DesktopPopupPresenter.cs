using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive.Linq;
using Avalonia.Data;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.VisualTree;
using OpenUtauMobile.Controls;
using OpenUtauMobile.DesktopUI.Views;
using OpenUtauMobile.Services;
using OpenUtauMobile.ViewModels;

namespace OpenUtauMobile.DesktopUI.Services
{
    internal sealed class DesktopPopupPresenter(Func<Window?> owner) : IDisposable
    {
        private readonly List<Window> _windows = [];
        private bool _disposed;
        public bool IsOpen => _windows.Count > 0;
        public Window? ActiveWindow => _windows.LastOrDefault();

        public async Task<object?> ShowAsync(Control view, PopupViewModelBase vm)
        {
            if (_disposed || (ActiveWindow ?? owner()) is not { IsVisible: true } parent) return null;
            // 复用业务输入和确认逻辑，仅替换桌面宿主。
            DialogShell? shell = view.GetSelfAndVisualDescendants().OfType<DialogShell>().FirstOrDefault();
            shell ??= (view as ContentControl)?.Content as DialogShell;
            if (shell != null) shell.IsWindowHosted = true;
            DesktopDialogProfile profile;
            if (!DesktopDialogCatalog.IsRegistered(view.GetType()))
            {
                Serilog.Log.Warning("No desktop dialog presentation registered for {Dialog}", view.GetType().FullName);
                view = new DesktopUnavailablePage(view.GetType().Name, vm.RequestBack);
                profile = DesktopDialogProfile.Standard;
            }
            else if (view is PopupDialogControl popup)
            {
                profile = DesktopDialogCatalog.For(popup, view.GetType());
            }
            else
            {
                profile = DesktopDialogProfile.Standard;
            }
            view.Classes.Add("DesktopPopup");
            DesktopDensity.Apply(view);
            if (profile.DenseFields) DesktopDensity.ApplyDenseDialogFields(view);
            StyleInclude popupStyles = new StyleInclude(new Uri("avares://OpenUtauMobile.DesktopUI/")) { Source = new Uri("avares://OpenUtauMobile.DesktopUI/Views/DesktopStyles.axaml") };
            view.Styles.Add(popupStyles);
            using IDisposable minimumHeight = view.Bind(Control.MinHeightProperty, Observable.Return(0d), BindingPriority.LocalValue);
            bool usesAdaptiveHeight = view is PopupDialogControl { UsesAdaptiveHeight: true };
            double preferredWidth = profile.Width;
            Size available = GetAvailableSize(parent);
            double preferredHeight = double.IsNaN(view.Height) ? profile.MaxHeight : view.Height;
            Window window = new()
            {
                Width = Math.Min(available.Width, preferredWidth),
                SizeToContent = usesAdaptiveHeight ? SizeToContent.Height : SizeToContent.Manual,
                CanResize = !usesAdaptiveHeight,
                MinWidth = Math.Min(available.Width, profile.MinWidth),
                WindowStartupLocation = WindowStartupLocation.CenterScreen,
                ShowInTaskbar = false,
                Content = view,
                DataContext = vm
            };
            ApplyHeight(available.Height);
            void ApplyHeight(double availableHeight)
            {
                double maxHeight = Math.Min(availableHeight, profile.MaxHeight);
                double minHeight = Math.Min(maxHeight, 80);
                window.MinHeight = minHeight;
                window.MaxHeight = maxHeight;
                if (!usesAdaptiveHeight)
                    window.Height = Math.Clamp(preferredHeight, minHeight, maxHeight);
            }
            DesktopDensity.Apply(window);
            window.Classes.Add("DesktopDialog");
            window.Styles.Add(new StyleInclude(new Uri("avares://OpenUtauMobile.DesktopUI/")) { Source = new Uri("avares://OpenUtauMobile.DesktopUI/Views/DesktopStyles.axaml") });
            DesktopUi.Paint(window, Window.BackgroundProperty, "Sem.Color.SurfaceContainerLow");
            if (shell != null) window.Bind(Window.TitleProperty, shell.GetObservable(DialogShell.HeaderProperty));
            // 短消息弹窗仍可按内容收窄。
            if (view is OptionConfirmPopup or ExitEditorConfirmPopup or LoadingPopup)
            {
                Size constraint = new(window.Width, usesAdaptiveHeight ? window.MaxHeight : window.Height);
                window.Measure(constraint);
                view.Measure(constraint);
                window.Width = Math.Clamp(view.DesiredSize.Width, window.MinWidth, window.Width);
            }
            bool acceptedClose = false;
            EventHandler<object?> close = (_, result) => { acceptedClose = true; window.Close(result); };
            vm.ClosingEvent += close;
            window.Closing += (_, e) =>
            {
                if (acceptedClose || _disposed) return;
                e.Cancel = true;
                vm.RequestBack();
            };
            window.AddHandler(InputElement.KeyDownEvent, (_, e) =>
            {
                if (!e.Handled && e.Key == Key.Escape && !DialogShell.IsTextInputComposing(e.Source as Visual))
                { e.Handled = true; vm.RequestBack(); }
            }, RoutingStrategies.Bubble);
            Screens screens = window.Screens;
            void OnScreensChanged(object? sender, EventArgs e) => ApplyHeight(GetAvailableSize(window).Height);
            screens.Changed += OnScreensChanged;
            _windows.Add(window);
            try { return await window.ShowDialog<object?>(parent); }
            finally
            {
                screens.Changed -= OnScreensChanged;
                vm.ClosingEvent -= close;
                _windows.Remove(window);
                window.Content = null;
                view.Styles.Remove(popupStyles); view.Classes.Remove("DesktopPopup");
                if (shell != null) shell.IsWindowHosted = false;
            }
        }

        /// <summary>获取扣除屏幕外边距后的桌面弹窗可用尺寸。</summary>
        private static Size GetAvailableSize(Window window)
        {
            Size workingArea = window.Screens.ScreenFromWindow(window) is { } monitor
                ? new Size(monitor.WorkingArea.Width / monitor.Scaling, monitor.WorkingArea.Height / monitor.Scaling)
                : window.Bounds.Size;
            return new Size(Math.Max(1, workingArea.Width - 24), Math.Max(1, workingArea.Height - 24));
        }

        public void Dispose()
        {
            _disposed = true;
            foreach (Window window in _windows.ToArray().Reverse()) window.Close();
        }
    }
}
