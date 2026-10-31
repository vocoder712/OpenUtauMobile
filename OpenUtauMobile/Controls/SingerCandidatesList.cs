using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using OpenUtauMobile.Services.SingerPlugins;

namespace OpenUtauMobile.Controls
{
    /// <summary>管理页展示不可用候选的位置和诊断，不提供工程选择操作。</summary>
    public sealed class SingerCandidatesList : ItemsControl
    {
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
                return new Border { Padding = new Thickness(12), Child = text };
            });
        }
    }
}