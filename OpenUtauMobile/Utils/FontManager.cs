using Serilog;
using SkiaSharp;

namespace OpenUtauMobile.Utils
{
    public static class FontManager
    {
        private static SKTypeface? _openSansTypeface;

        public static SKTypeface OpenSans
        {
            get
            {
                if (_openSansTypeface == null)
                {
                    try
                    {
                        using var stream = FileSystem.OpenAppPackageFileAsync("OpenSans-Regular.ttf").Result;
                        _openSansTypeface = SKTypeface.FromStream(stream);
                    }
                    catch (Exception ex)
                    {
                        Log.Error(ex, "无法加载OpenSans字体，使用默认字体代替。");
                        _openSansTypeface = SKTypeface.Default;
                    }
                }
                return _openSansTypeface;
            }
        }
    }
}