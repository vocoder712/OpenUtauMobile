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

public partial class PhonemePanelModeSwitcher : UserControl
{
    // 依赖属性：当前面板编辑模式
    public static readonly StyledProperty<PhonemePanelMode> CurrentModeProperty =
        AvaloniaProperty.Register<PhonemePanelModeSwitcher, PhonemePanelMode>(
            nameof(CurrentMode),
            defaultBindingMode: BindingMode.TwoWay);

    public PhonemePanelMode CurrentMode
    {
        get => GetValue(CurrentModeProperty);
        set => SetValue(CurrentModeProperty, value);
    }

    private bool _isExpand;

    public PhonemePanelModeSwitcher()
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
            case PhonemePanelMode.PhonemeSimple:
                PhonemeSimpleButton.Background = selectedBrush;
                SwitchButton.Content = new PackIconPhosphorIcons
                {
                    Kind = PackIconPhosphorIconsKind.Rows
                };
                break;
            case PhonemePanelMode.PhonemeAdvanced:
                PhonemeAdvancedButton.Background = selectedBrush;
                SwitchButton.Content = new PackIconPhosphorIcons
                {
                    Kind = PackIconPhosphorIconsKind.WaveTriangle
                };
                break;
            case PhonemePanelMode.ParameterDraw:
                ParameterDrawButton.Background = selectedBrush;
                SwitchButton.Content = new PackIconPhosphorIcons
                {
                    Kind = PackIconPhosphorIconsKind.Pencil
                };
                break;
            case PhonemePanelMode.ParameterErase:
                ParameterEraseButton.Background = selectedBrush;
                SwitchButton.Content = new PackIconPhosphorIcons
                {
                    Kind = PackIconPhosphorIconsKind.Eraser
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
        if (ReferenceEquals(sender, PhonemeSimpleButton))
        {
            CurrentMode = PhonemePanelMode.PhonemeSimple;
        }
        else if (ReferenceEquals(sender, PhonemeAdvancedButton))
        {
            CurrentMode = PhonemePanelMode.PhonemeAdvanced;
        }
        else if (ReferenceEquals(sender, ParameterDrawButton))
        {
            CurrentMode = PhonemePanelMode.ParameterDraw;
        }
        else if (ReferenceEquals(sender, ParameterEraseButton))
        {
            CurrentMode = PhonemePanelMode.ParameterErase;
        }

        UpdateCurrentModeVisualState();
    }

    private void ResetModeListVisualState()
    {
        IImmutableSolidColorBrush backgroundBrush = Brushes.Transparent;
        PhonemeSimpleButton.Background = backgroundBrush;
        PhonemeAdvancedButton.Background = backgroundBrush;
        ParameterDrawButton.Background = backgroundBrush;
        ParameterEraseButton.Background = backgroundBrush;
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
