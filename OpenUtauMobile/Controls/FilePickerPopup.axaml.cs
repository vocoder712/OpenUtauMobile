using Avalonia;
using OpenUtauMobile.ViewModels;

namespace OpenUtauMobile.Controls;

public partial class FilePickerPopup : PopupDialogControl
{
    private string? _displayedPath;

    protected override PopupDialogWidthPreset WidthPreset => PopupDialogWidthPreset.Wide;

    public FilePickerPopup()
    {
        InitializeComponent();
        DataContextChanged += (_, _) =>
        {
            _displayedPath = DataContext is FilePickerBaseViewModel { IsLoading: false } picker
                ? picker.CurrentPath
                : null;
            FileListScroll.Offset = default;
        };
        FileListScroll.PropertyChanged += OnFileListPropertyChanged;
    }

    /// <summary>新目录加载完成并显示列表时，将滚动位置重置到顶部。</summary>
    private void OnFileListPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property != IsVisibleProperty || !FileListScroll.IsVisible ||
            DataContext is not FilePickerBaseViewModel picker || _displayedPath == picker.CurrentPath) return;

        _displayedPath = picker.CurrentPath;
        FileListScroll.Offset = default;
    }
}
