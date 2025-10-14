using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace OpenUtauMobile.Utils
{
    public static class ThemeColorsManager
    {
        private static ThemeColors _current;
        public static ThemeColors Current { get
            {
                return _current;
            }
            private set
            {
                _current = value;
            }
        }
        static ThemeColorsManager()
        {
            _current = (App.Current?.RequestedTheme) switch
            {
                AppTheme.Dark => new DarkThemeColors(),
                AppTheme.Light => new LightThemeColors(),
                AppTheme.Unspecified => new LightThemeColors(),
                null => new LightThemeColors(),
                _ => new LightThemeColors(),
            };
        }
    }
}
