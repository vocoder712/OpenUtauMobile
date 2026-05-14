using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Interactivity;
using Avalonia.Media;
using IconPacks.Avalonia.PhosphorIcons;
using OpenUtauMobile.Themes.OpenUtauMobile.Runtime;
using OpenUtauMobile.ViewModels;

namespace OpenUtauMobile.Controls;

public partial class TrackEditModeSwitcher : UserControl
{
    // 依赖属性：当前编辑模式
    public static readonly StyledProperty<TrackEditMode> CurrentModeProperty =
        AvaloniaProperty.Register<PianoRollEditModeSwitcher, TrackEditMode>(nameof(CurrentMode),
            defaultBindingMode: BindingMode.TwoWay);

    public TrackEditMode CurrentMode
    {
        get => GetValue(CurrentModeProperty);
        set => SetValue(CurrentModeProperty, value);
    }

    private bool _isExpand;

    public TrackEditModeSwitcher()
    {
        InitializeComponent();
        UpdateCurrentModeVisualState();
        UpdateExpandVisualState();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == CurrentModeProperty)
        {
            _isExpand = false;
            UpdateExpandVisualState();
            UpdateCurrentModeVisualState();
        }
    }

    private void UpdateCurrentModeVisualState()
    {
        IBrush selectedBrush = ThemeResources.GetBrush("Sem.Color.PrimaryContainer");
        ResetModeListVisualState();
        switch (CurrentMode)
        {
            case TrackEditMode.Normal:
                HandButton.Background = selectedBrush;
                SwitchButton.Content = new PackIconPhosphorIcons
                {
                    Kind = PackIconPhosphorIconsKind.Hand
                };
                break;
            case TrackEditMode.MultiSelect:
                MultiSelectButton.Background = selectedBrush;
                SwitchButton.Content = new PackIconPhosphorIcons
                {
                    Kind = PackIconPhosphorIconsKind.Selection
                };
                break;

            default:
                throw new ArgumentOutOfRangeException();
        }
    }


    private void OnSwitchButtonClick(object? sender, RoutedEventArgs e)
    {
        _isExpand = !_isExpand;
        UpdateExpandVisualState();
    }

    private void OnModeButtonClick(object? sender, RoutedEventArgs e)
    {
        if (ReferenceEquals(sender, HandButton))
        {
            CurrentMode = TrackEditMode.Normal;
        }
        else if (ReferenceEquals(sender, MultiSelectButton))
        {
            CurrentMode = TrackEditMode.MultiSelect;
        }

        UpdateCurrentModeVisualState();
    }

    private void ResetModeListVisualState()
    {
        IImmutableSolidColorBrush backgroundBrush = Brushes.Transparent;
        HandButton.Background = backgroundBrush;
        MultiSelectButton.Background = backgroundBrush;
    }

    private void UpdateExpandVisualState()
    {
        if (_isExpand)
        {
            double width = ModeButtonsPanel.DesiredSize.Width;
            ModeButtonsGrid.Width = width;
        }
        else
        {
            ModeButtonsGrid.Width = 0;
        }
    }
}