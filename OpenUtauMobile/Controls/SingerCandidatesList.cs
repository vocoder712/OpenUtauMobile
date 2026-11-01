using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using System.Windows.Input;
using OpenUtauMobile.Services.SingerPlugins;

namespace OpenUtauMobile.Controls
{
    /// <summary>管理页展示不可用候选的位置和诊断，不提供工程选择操作。</summary>
    public sealed class SingerCandidatesList : ItemsControl
    {
        /// <summary>沿用宿主的 ItemsControl 模板，保证派生控件实际呈现条目。</summary>
        protected override Type StyleKeyOverride => typeof(ItemsControl);
        /// <summary>只有提供方允许的候选显示删除按钮。</summary>
        public static readonly StyledProperty<ICommand?> DeleteCommandProperty = AvaloniaProperty.Register<SingerCandidatesList, ICommand?>(nameof(DeleteCommand));
        public ICommand? DeleteCommand { get => GetValue(DeleteCommandProperty); set => SetValue(DeleteCommandProperty, value); }
        public SingerCandidatesList()
        {
            ItemTemplate = new FuncDataTemplate<SingerCandidate>((candidate, _) =>
            {
                if (candidate == null) return null;
                StackPanel text = new() { Spacing = 4 };
                text.Children.Add(new TextBlock
                {
                    Text = candidate.Entry.DisplayName,
                    FontWeight = Avalonia.Media.FontWeight.SemiBold,
                    TextWrapping = Avalonia.Media.TextWrapping.Wrap
                });
                TextBlock status = new()
                {
                    Text = Helpers.L.S($"SingerAvailability.{candidate.Entry.Availability}"),
                    TextWrapping = Avalonia.Media.TextWrapping.Wrap
                };
                status.Bind(TextBlock.ForegroundProperty, status.GetResourceObservable("Sem.Color.Warning"));
                text.Children.Add(status);
                text.Children.Add(new TextBlock { Text = candidate.Details, TextWrapping = Avalonia.Media.TextWrapping.Wrap });
                if (SingerDeletionService.CanDelete(candidate))
                {
                    Button delete = new() { CommandParameter = candidate,
                        HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right };
                    delete.Bind(Button.ContentProperty, delete.GetResourceObservable("Common.Uninstall"));
                    delete.Classes.Add("destructive");
                    delete.Bind(Button.CommandProperty, new Binding(nameof(DeleteCommand)) { Source = this });
                    text.Children.Add(delete);
                }
                return new Border { Padding = new Thickness(12), Child = text };
            });
        }
    }
}
