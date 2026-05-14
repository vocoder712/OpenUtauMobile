using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using IconPacks.Avalonia.PhosphorIcons;
using OpenUtauMobile.Themes.OpenUtauMobile.Runtime;
using OpenUtauMobile.ViewModels;

namespace OpenUtauMobile.Controls;

public partial class PianoRollEditModeSwitcher : UserControl
{
    // 依赖属性：当前编辑模式
    public static readonly StyledProperty<PianoRollEditMode> CurrentModeProperty =
        AvaloniaProperty.Register<PianoRollEditModeSwitcher, PianoRollEditMode>(nameof(CurrentMode));

    public PianoRollEditMode CurrentMode
    {
        get => GetValue(CurrentModeProperty);
        set => SetValue(CurrentModeProperty, value);
    }

    private bool _isExpand;

    public PianoRollEditModeSwitcher()
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
            case PianoRollEditMode.Hand:
                HandButton.Background = selectedBrush;
                SwitchButton.Content = new PackIconPhosphorIcons
                {
                    Kind = PackIconPhosphorIconsKind.Hand
                };
                break;
            case PianoRollEditMode.MultiSelect:
                MultiSelectButton.Background = selectedBrush;
                SwitchButton.Content = new PackIconPhosphorIcons
                {
                    Kind = PackIconPhosphorIconsKind.Selection
                };
                break;
            case PianoRollEditMode.Note:
                NoteButton.Background = selectedBrush;
                SwitchButton.Content = new PackIconPhosphorIcons
                {
                    Kind = PackIconPhosphorIconsKind.MusicNoteSimple
                };
                break;
            case PianoRollEditMode.PitchPen:
                PitchPenButton.Background = selectedBrush;
                SwitchButton.Content = new PackIconPhosphorIcons
                {
                    Kind = PackIconPhosphorIconsKind.Pencil
                };
                break;
            case PianoRollEditMode.Anchor:
                AnchorButton.Background = selectedBrush;
                SwitchButton.Content = new PackIconPhosphorIcons
                {
                    Kind = PackIconPhosphorIconsKind.Cursor
                };
                break;
            case PianoRollEditMode.Vibrato:
                VibratoButton.Background = selectedBrush;
                SwitchButton.Content = new PackIconPhosphorIcons
                {
                    Kind = PackIconPhosphorIconsKind.WaveSine
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
            CurrentMode = PianoRollEditMode.Hand;
        }
        else if (ReferenceEquals(sender, MultiSelectButton))
        {
            CurrentMode = PianoRollEditMode.MultiSelect;
        }
        else if (ReferenceEquals(sender, NoteButton))
        {
            CurrentMode = PianoRollEditMode.Note;
        }
        else if (ReferenceEquals(sender, PitchPenButton))
        {
            CurrentMode = PianoRollEditMode.PitchPen;
        }
        else if (ReferenceEquals(sender, AnchorButton))
        {
            CurrentMode = PianoRollEditMode.Anchor;
        }
        else if (ReferenceEquals(sender, VibratoButton))
        {
            CurrentMode = PianoRollEditMode.Vibrato;
        }

        UpdateCurrentModeVisualState();
    }

    private void ResetModeListVisualState()
    {
        IImmutableSolidColorBrush backgroundBrush = Brushes.Transparent;
        HandButton.Background = backgroundBrush;
        MultiSelectButton.Background = backgroundBrush;
        NoteButton.Background = backgroundBrush;
        PitchPenButton.Background = backgroundBrush;
        AnchorButton.Background = backgroundBrush;
        VibratoButton.Background = backgroundBrush;
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