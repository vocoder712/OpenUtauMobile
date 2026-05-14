using System;
using Avalonia.Media;

namespace OpenUtauMobile.Helpers;

public static class ColorMathHelper
{
    public static void RgbToHsv(Color color, out double hue, out double saturation, out double value)
    {
        double r = color.R / 255d;
        double g = color.G / 255d;
        double b = color.B / 255d;

        double max = Math.Max(r, Math.Max(g, b));
        double min = Math.Min(r, Math.Min(g, b));
        double delta = max - min;

        hue = 0;
        if (delta > 0)
        {
            if (max == r)
            {
                hue = 60d * (((g - b) / delta) % 6d);
            }
            else if (max == g)
            {
                hue = 60d * (((b - r) / delta) + 2d);
            }
            else
            {
                hue = 60d * (((r - g) / delta) + 4d);
            }
        }

        if (hue < 0)
        {
            hue += 360d;
        }

        saturation = max <= 0 ? 0 : delta / max;
        value = max;
    }

    public static Color HsvToRgb(double hue, double saturation, double value)
    {
        hue = NormalizeHue(hue);
        saturation = Math.Clamp(saturation, 0d, 1d);
        value = Math.Clamp(value, 0d, 1d);

        double c = value * saturation;
        double x = c * (1d - Math.Abs((hue / 60d) % 2d - 1d));
        double m = value - c;

        double r1;
        double g1;
        double b1;

        if (hue < 60d)
        {
            r1 = c;
            g1 = x;
            b1 = 0;
        }
        else if (hue < 120d)
        {
            r1 = x;
            g1 = c;
            b1 = 0;
        }
        else if (hue < 180d)
        {
            r1 = 0;
            g1 = c;
            b1 = x;
        }
        else if (hue < 240d)
        {
            r1 = 0;
            g1 = x;
            b1 = c;
        }
        else if (hue < 300d)
        {
            r1 = x;
            g1 = 0;
            b1 = c;
        }
        else
        {
            r1 = c;
            g1 = 0;
            b1 = x;
        }

        byte r = (byte)Math.Clamp((int)Math.Round((r1 + m) * 255d), 0, 255);
        byte g = (byte)Math.Clamp((int)Math.Round((g1 + m) * 255d), 0, 255);
        byte b = (byte)Math.Clamp((int)Math.Round((b1 + m) * 255d), 0, 255);
        return Color.FromRgb(r, g, b);
    }

    private static double NormalizeHue(double hue)
    {
        double normalized = hue % 360d;
        return normalized < 0 ? normalized + 360d : normalized;
    }
}