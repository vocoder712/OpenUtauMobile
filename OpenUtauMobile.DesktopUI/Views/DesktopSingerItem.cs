using System;
using System.Globalization;
using System.IO;
using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using IconPacks.Avalonia.PhosphorIcons;
using OpenUtau.Core.Ustx;
using OpenUtauMobile.Tools;
using Serilog;

namespace OpenUtauMobile.DesktopUI.Views
{
    /// <summary>歌手下拉项共用头像与名称布局；位图随控件挂载和移除释放。</summary>
    internal sealed class DesktopSingerItem : UserControl
    {
        private static readonly ConditionalWeakTable<USinger, Bitmap> AvatarThumbnails = new();
        private const int AvatarThumbnailSize = 64;
        private readonly USinger? _singer;
        private readonly Image _avatar = new() { Name = "SingerAvatarImage", Width = 40, Height = 40, Stretch = Stretch.UniformToFill };
        private readonly PackIconPhosphorIcons _placeholder = new() { Name = "SingerAvatarPlaceholder", Kind = PackIconPhosphorIconsKind.User, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        private Bitmap? _bitmap;

        public DesktopSingerItem(USinger? singer, double portraitSize = 40)
        {
            _singer = singer;
            _avatar.Width = _avatar.Height = portraitSize;
            _placeholder.Width = _placeholder.Height = portraitSize / 2;
            Grid row = new() { ColumnDefinitions = new ColumnDefinitions("Auto,*"), ColumnSpacing = 12 };
            Border portrait = new() { Width = portraitSize, Height = portraitSize, CornerRadius = new Avalonia.CornerRadius(8), ClipToBounds = true, Child = new Panel { Children = { _placeholder, _avatar } } };
            DesktopUi.Paint(portrait, Border.BackgroundProperty, "Sem.Color.SurfaceContainerHighest");
            DesktopUi.Paint(_placeholder, ForegroundProperty, "Sem.Color.OnSurfaceVariant");
            row.Children.Add(portrait);
            StackPanel text = new() { Spacing = 2, VerticalAlignment = VerticalAlignment.Center };
            if (singer == null)
            {
                TextBlock empty = DesktopUi.Label("TrackSettings.Singer");
                DesktopUi.Paint(empty, TextBlock.ForegroundProperty, "Sem.Color.OnSurfaceVariant");
                text.Children.Add(empty);
            }
            else
            {
                text.Children.Add(new TextBlock { Text = singer.LocalizedName, TextTrimming = TextTrimming.CharacterEllipsis });
                TextBlock type = new() { Text = SingerTypeToLabelConverter.Instance.Convert(singer.SingerType, typeof(string), null, CultureInfo.CurrentCulture) as string, FontSize = 12 };
                DesktopUi.Paint(type, TextBlock.ForegroundProperty, "Sem.Color.OnSurfaceVariant");
                text.Children.Add(type);
            }
            Grid.SetColumn(text, 1); row.Children.Add(text);
            Content = row;
            AttachedToVisualTree += (_, _) => LoadAvatar();
            DetachedFromVisualTree += (_, _) => { _avatar.Source = null; _bitmap = null; _placeholder.IsVisible = true; };
        }

        private void LoadAvatar()
        {
            if (_bitmap != null || _singer == null) return;
            try
            {
                _singer.EnsureAvatarLoaded();
                if (_singer.AvatarData is not { Length: > 0 } data) return;
                _bitmap = AvatarThumbnails.GetValue(_singer, _ => CreateThumbnail(data));
                _avatar.Source = _bitmap;
                _placeholder.IsVisible = false;
            }
            catch (Exception error) { Log.Warning(error, "读取歌手下拉项头像失败：{Singer}", _singer.Name); }
        }

        private static Bitmap CreateThumbnail(byte[] data)
        {
            using MemoryStream stream = new(data);
            Bitmap original = new(stream);
            double scale = Math.Min(AvatarThumbnailSize / original.Size.Width, AvatarThumbnailSize / original.Size.Height);
            if (scale >= 1) return original;
            try
            {
                int width = Math.Max(1, (int)Math.Round(original.Size.Width * scale));
                int height = Math.Max(1, (int)Math.Round(original.Size.Height * scale));
                return original.CreateScaledBitmap(new PixelSize(width, height));
            }
            finally { original.Dispose(); }
        }
    }
}
