using System;
using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Threading;
using OpenUtauMobile.ViewModels;

namespace OpenUtauMobile.DesktopUI.Views
{
    internal sealed partial class DesktopSingerSetupPage : UserControl
    {
        private readonly ClassicSingerSetupViewModel _model;
        private readonly Border[] _steps = new Border[4];
        private readonly Control[] _pages;
        private readonly Grid _layout;
        private readonly Border _rail;
        private readonly Panel _wizard;
        private bool _attached;

        public DesktopSingerSetupPage(ClassicSingerSetupViewModel model)
        {
            AvaloniaXamlLoader.Load(this);
            _model = model;
            DataContext = model;
            _layout = this.FindControl<Grid>("SetupLayout")!;
            _rail = this.FindControl<Border>("StepRail")!;
            _wizard = this.FindControl<Panel>("WizardPanel")!;
            _pages = [this.FindControl<Grid>("Step0")!, this.FindControl<Grid>("Step1")!, this.FindControl<Grid>("Step2")!, this.FindControl<Grid>("Step3")!];
            StackPanel steps = new() { Spacing = 8 };
            string[] titles = ["SingerSetup.Step1.Title", "SingerSetup.Step2.Title", "SingerSetup.Step3.SingerInfo", "SingerSetup.Step4.Title"];
            for (int index = 0; index < _steps.Length; index++)
            {
                Grid row = new() { ColumnDefinitions = new ColumnDefinitions("Auto,*"), ColumnSpacing = 10 };
                Border number = new() { Width = 24, Height = 24, CornerRadius = new CornerRadius(12), Child = new TextBlock { Text = (index + 1).ToString(), FontWeight = FontWeight.SemiBold, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center } };
                DesktopUi.Paint(number, Border.BackgroundProperty, "Sem.Color.SurfaceContainerHigh");
                row.Children.Add(number);
                TextBlock title = DesktopUi.Label(titles[index]);
                Grid.SetColumn(title, 1);
                row.Children.Add(title);
                _steps[index] = new Border { Classes = { "DesktopSetupStep" }, Padding = new Thickness(12), Child = row };
                steps.Children.Add(_steps[index]);
            }
            _rail.Child = steps;
            SizeChanged += (_, e) =>
            {
                bool showRail = e.NewSize.Width >= 640;
                _rail.IsVisible = showRail;
                _layout.ColumnDefinitions[0].Width = new GridLength(showRail ? 180 : 0);
                _layout.ColumnSpacing = showRail ? 20 : 0;
            };
            Refresh();
        }

        protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
        {
            base.OnAttachedToVisualTree(e);
            _attached = true;
            _model.PropertyChanged += OnModelChanged;
            Refresh();
        }

        protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
        {
            _attached = false;
            _model.PropertyChanged -= OnModelChanged;
            base.OnDetachedFromVisualTree(e);
        }

        private void OnModelChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName is nameof(_model.Step) or nameof(_model.ShowProgressState) or nameof(_model.ShowSuccessState) or nameof(_model.ShowErrorState))
            {
                if (Dispatcher.UIThread.CheckAccess()) Refresh();
                else Dispatcher.UIThread.Post(() => { if (_attached) Refresh(); });
            }
        }

        private void Refresh()
        {
            bool wizard = !_model.ShowProgressState && !_model.ShowSuccessState && !_model.ShowErrorState;
            _wizard.IsVisible = wizard;
            for (int index = 0; index < _steps.Length; index++)
            {
                _steps[index].Classes.Set("active", index == _model.Step);
                _pages[index].IsVisible = index == _model.Step;
            }
        }
    }
}
