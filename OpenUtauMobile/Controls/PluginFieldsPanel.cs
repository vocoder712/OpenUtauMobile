using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Data.Converters;
using Avalonia.Layout;
using OpenUtauMobile.Plugin.Abstractions;
using OpenUtauMobile.ViewModels;

namespace OpenUtauMobile.Controls
{
    /// <summary>使用宿主标准控件呈现声明式字段，不加载插件视图。</summary>
    public sealed class PluginFieldsPanel : StackPanel
    {
        public static readonly StyledProperty<IReadOnlyList<PluginFieldViewModel>?> FieldsProperty =
            AvaloniaProperty.Register<PluginFieldsPanel, IReadOnlyList<PluginFieldViewModel>?>(nameof(Fields));
        public IReadOnlyList<PluginFieldViewModel>? Fields { get => GetValue(FieldsProperty); set => SetValue(FieldsProperty, value); }

        protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
        {
            base.OnPropertyChanged(change);
            if (change.Property != FieldsProperty) return;
            Children.Clear();
            Spacing = Themes.OpenUtauMobile.Tokens.Foundation.LayoutTokens.SpaceM;
            foreach (PluginFieldViewModel field in Fields ?? [])
            {
                PluginFieldDescriptor descriptor = field.Descriptor;
                if (!descriptor.IsVisible) continue;
                StackPanel row = new() { DataContext = field, Spacing = 8, IsEnabled = descriptor.IsEnabled };
                if (descriptor.Kind != PluginFieldKind.Boolean)
                    row.Children.Add(new TextBlock { Text = descriptor.Label, TextWrapping = Avalonia.Media.TextWrapping.Wrap });
                if (!string.IsNullOrEmpty(descriptor.Description))
                    row.Children.Add(new TextBlock { Text = descriptor.Description, TextWrapping = Avalonia.Media.TextWrapping.Wrap });
                switch (descriptor.Kind)
                {
                    case PluginFieldKind.Boolean:
                        CheckBox check = new() { Content = descriptor.Label };
                        check.Bind(CheckBox.IsCheckedProperty, new Binding(nameof(field.Checked)) { Mode = BindingMode.TwoWay });
                        row.Children.Add(check);
                        break;
                    case PluginFieldKind.Number:
                        NumericUpDown number = new() { Minimum = descriptor.Minimum ?? decimal.MinValue, Maximum = descriptor.Maximum ?? decimal.MaxValue };
                        number.Bind(NumericUpDown.ValueProperty, new Binding(nameof(field.Number)) { Mode = BindingMode.TwoWay });
                        row.Children.Add(number);
                        break;
                    case PluginFieldKind.Selection:
                        ComboBox combo = new()
                        {
                            ItemsSource = descriptor.Options.IsDefault ? [] : descriptor.Options,
                            HorizontalAlignment = HorizontalAlignment.Stretch,
                            ItemTemplate = new FuncDataTemplate<PluginFieldOption>((option, _) => new TextBlock { Text = option?.Label })
                        };
                        combo.Bind(ComboBox.SelectedItemProperty, new Binding(nameof(field.Selected)) { Mode = BindingMode.TwoWay });
                        row.Children.Add(combo);
                        break;
                    case PluginFieldKind.MultipleSelection:
                        foreach (PluginFieldOption option in descriptor.Options.IsDefault ? [] : descriptor.Options)
                        {
                            CheckBox choice = new() { Content = option.Label };
                            choice.Bind(CheckBox.IsCheckedProperty, new Binding(nameof(field.SelectedIds))
                            {
                                Source = field,
                                Mode = BindingMode.OneWay,
                                Converter = new FuncValueConverter<System.Collections.Immutable.ImmutableArray<string>, bool>(
                                    ids => !ids.IsDefault && ids.Contains(option.Id))
                            });
                            choice.IsCheckedChanged += (_, _) =>
                            {
                                bool selected = field.SelectedIds.Contains(option.Id);
                                if (choice.IsChecked == true && !selected) field.SelectedIds = field.SelectedIds.Add(option.Id);
                                else if (choice.IsChecked != true && selected) field.SelectedIds = field.SelectedIds.Remove(option.Id);
                            };
                            row.Children.Add(choice);
                        }
                        break;
                    default:
                        TextBox text = new() { IsReadOnly = descriptor.Kind is PluginFieldKind.File or PluginFieldKind.Directory };
                        text.Bind(TextBox.TextProperty, new Binding(nameof(field.Text)) { Mode = BindingMode.TwoWay });
                        row.Children.Add(text);
                        if (text.IsReadOnly)
                        {
                            Button browse = new() { Command = field.BrowseCommand };
                            browse.Bind(ContentControl.ContentProperty, browse.GetResourceObservable("Common.Browse"));
                            row.Children.Add(browse);
                        }
                        break;
                }
                TextBlock error = new() { TextWrapping = Avalonia.Media.TextWrapping.Wrap };
                error.Bind(TextBlock.TextProperty, new Binding(nameof(field.Error)));
                error.Bind(TextBlock.ForegroundProperty, error.GetResourceObservable("Sem.Color.Error"));
                row.Children.Add(error);
                Children.Add(row);
            }
        }
    }
}
