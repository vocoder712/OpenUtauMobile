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
            if (!DesktopDialogCatalog.TryGet(view.GetType(), out DesktopDialogProfile profile))
            {
                Serilog.Log.Warning("No desktop dialog presentation registered for {Dialog}", view.GetType().FullName);
                view = new DesktopUnavailablePage(view.GetType().Name, vm.RequestBack);
                profile = DesktopDialogProfile.Standard;
            }
            else if (view is PopupDialogControl popup)
            {
                profile = DesktopDialogCatalog.For(popup, view.GetType());
            }
            view.Classes.Add("DesktopPopup");
            DesktopDensity.Apply(view);
            if (profile.DenseFields) DesktopDensity.ApplyDenseDialogFields(view);
            StyleInclude popupStyles = new StyleInclude(new Uri("avares://OpenUtauMobile.DesktopUI/")) { Source = new Uri("avares://OpenUtauMobile.DesktopUI/Views/DesktopStyles.axaml") };
            view.Styles.Add(popupStyles);
            using IDisposable minimumHeight = view.Bind(Control.MinHeightProperty, Observable.Return(0d), BindingPriority.LocalValue);
            double preferredWidth = profile.Width;
            Size screen = parent.Screens.ScreenFromWindow(parent) is { } monitor
                ? new Size(monitor.WorkingArea.Width / monitor.Scaling, monitor.WorkingArea.Height / monitor.Scaling)
                : new Size(parent.Bounds.Width, parent.Bounds.Height);
            double safeWidth = Math.Max(1, screen.Width - 24);
            double safeHeight = Math.Max(1, screen.Height - 24);
            Window window = new()
            {
                Width = Math.Min(safeWidth, preferredWidth),
                Height = Math.Min(safeHeight, 600),
                MaxHeight = Math.Min(safeHeight, profile.MaxHeight),
                SizeToContent = profile.Scrolls ? SizeToContent.Manual : SizeToContent.Height,
                MinWidth = Math.Min(safeWidth, profile.MinWidth),
                MinHeight = Math.Min(safeHeight, 80),
                WindowStartupLocation = WindowStartupLocation.CenterScreen,
                ShowInTaskbar = false,
                Content = view,
                DataContext = vm
            };
            DesktopDensity.Apply(window);
            window.Classes.Add("DesktopDialog");
            window.Styles.Add(new StyleInclude(new Uri("avares://OpenUtauMobile.DesktopUI/")) { Source = new Uri("avares://OpenUtauMobile.DesktopUI/Views/DesktopStyles.axaml") });
            DesktopUi.Paint(window, Window.BackgroundProperty, "Sem.Color.SurfaceContainerLow");
            if (shell != null) window.Bind(Window.TitleProperty, shell.GetObservable(DialogShell.HeaderProperty));
            // 显示前按内容确定初始高度，使屏幕居中使用最终尺寸。
            window.Measure(new Size(window.Width, window.MaxHeight));
            view.Measure(new Size(window.Width, window.MaxHeight));
            DesktopDensity.ApplyPopupGeometry(view);
            view.Measure(new Size(window.Width, window.MaxHeight));
            if (view is OptionConfirmPopup or ExitEditorConfirmPopup or LoadingPopup)
            {
                window.Width = Math.Clamp(view.DesiredSize.Width, window.MinWidth, window.Width);
                view.Measure(new Size(window.Width, Math.Min(screen.Height, 600)));
            }
            if (profile.Scrolls)
                window.MinHeight = Math.Min(safeHeight, Math.Min(360, Math.Max(160, view.DesiredSize.Height)));
            window.Height = profile.Scrolls ? Math.Clamp(view.DesiredSize.Height, window.MinHeight, Math.Min(safeHeight, profile.MaxHeight))
                : Math.Min(screen.Height, Math.Clamp(view.DesiredSize.Height, Math.Min(80, window.MaxHeight), window.MaxHeight));
            if (!profile.Scrolls) window.MinHeight = window.Height;
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
            _windows.Add(window);
            try { return await window.ShowDialog<object?>(parent); }
            finally
            {
                vm.ClosingEvent -= close;
                _windows.Remove(window);
                window.Content = null;
                view.Styles.Remove(popupStyles); view.Classes.Remove("DesktopPopup");
                if (shell != null) shell.IsWindowHosted = false;
            }
        }

        public void Dispose()
        {
            _disposed = true;
            foreach (Window window in _windows.ToArray().Reverse()) window.Close();
        }
    }
}
