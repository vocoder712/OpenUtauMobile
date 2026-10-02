using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;

namespace OpenUtauMobile.DesktopUI.Views
{
    internal sealed class DesktopUtilityShell : UserControl
    {
        public DesktopUtilityShell(string titleKey, Control content, Control? actions = null, Control? footer = null, Thickness? contentInset = null)
        {
            DesktopDensity.Apply(this);
            Classes.Add("DesktopUtility");
            this.Bind(AutomationProperties.NameProperty, this.GetResourceObservable(titleKey));
            Grid root = new() { RowDefinitions = new RowDefinitions("Auto,*,Auto"), RowSpacing = 0 };
            if (actions != null)
                root.Children.Add(new Border { Classes = { "DesktopToolbar" }, Child = actions });
            Border body = new() { Child = content, Padding = contentInset ?? new Thickness(20), ClipToBounds = true };
            Grid.SetRow(body, 1);
            root.Children.Add(body);
            if (footer != null)
            {
                Grid.SetRow(footer, 2);
                root.Children.Add(footer);
            }
            Content = root;
        }

    }
}
