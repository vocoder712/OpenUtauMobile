using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;

namespace OpenUtauMobile.Controls;

/// <summary>统一弹窗外框；业务视图只提供标题、内容、操作和关闭命令。</summary>
public class DialogShell : HeaderedContentControl
{
    public static readonly StyledProperty<ICommand?> CloseCommandProperty =
        AvaloniaProperty.Register<DialogShell, ICommand?>(nameof(CloseCommand));

    public static readonly StyledProperty<Control?> FooterProperty =
        AvaloniaProperty.Register<DialogShell, Control?>(nameof(Footer));

    public ICommand? CloseCommand
    {
        get => GetValue(CloseCommandProperty);
        set => SetValue(CloseCommandProperty, value);
    }

    public Control? Footer
    {
        get => GetValue(FooterProperty);
        set => SetValue(FooterProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == FooterProperty)
        {
            if (change.OldValue is Control oldFooter)
            {
                LogicalChildren.Remove(oldFooter);
            }
            if (change.NewValue is Control newFooter)
            {
                LogicalChildren.Add(newFooter);
            }
        }
    }
}

/// <summary>统一操作区，窄屏时自动换行。</summary>
public class DialogActions : WrapPanel
{
}
