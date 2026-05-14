using System;
using System.Collections.Generic;
using Avalonia.Media;
using Avalonia.Media.TextFormatting;

namespace OpenUtauMobile.Controls
{
    public static class TextLayoutCache
    {
        private static readonly Dictionary<Tuple<string, IBrush, double, bool>, TextLayout> Cache = [];

        public static void Clear()
        {
            Cache.Clear();
        }

        public static TextLayout Get(string text, IBrush brush, double fontSize, bool bold = false)
        {
            Tuple<string, IBrush, double, bool> key = Tuple.Create(text, brush, fontSize, bold);
            if (Cache.TryGetValue(key, out TextLayout? textLayout))
            {
                return textLayout;
            }

            FontWeight fontWeight = bold ? FontWeight.Bold : FontWeight.Normal;
            textLayout = new TextLayout(
                text,
                new Typeface(FontFamily.Default, weight: fontWeight),
                fontSize,
                brush,
                TextAlignment.Left,
                TextWrapping.NoWrap);
            Cache.Add(key, textLayout);

            return textLayout;
        }
    }
}