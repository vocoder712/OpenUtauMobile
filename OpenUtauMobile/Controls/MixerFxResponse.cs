using System;
using System.Numerics;
using OpenUtau.Core.SignalChain.Effects;
using OpenUtau.Core.Ustx;

namespace OpenUtauMobile.Controls;

/// <summary>仅供界面预览；不接入播放链，不修改效果器实例。</summary>
internal static class MixerFxResponse
{
    public static Func<double, double> Equalizer(double low, double frequency, double mid, double high)
    {
        if (Math.Abs(low) < .01 && Math.Abs(mid) < .01 && Math.Abs(high) < .01) return _ => 0;
        double[][] stages = [Coefficients(0, 200, low), Coefficients(1, frequency, mid), Coefficients(2, 8000, high)];
        return hz =>
        {
            Complex z = Complex.FromPolarCoordinates(1, -2 * Math.PI * hz / 44100);
            double db = 0;
            foreach (double[] c in stages)
                db += 20 * Math.Log10(((c[0] + c[1] * z + c[2] * z * z) / (1 + c[3] * z + c[4] * z * z)).Magnitude);
            return db;
        };
    }

    // 与当前三段 EQ 的 RBJ 系数一致；中频 Q 固定为播放链使用的 0.707。
    private static double[] Coefficients(int band, double frequency, double gain)
    {
        double a = Math.Pow(10, gain / 40), w = 2 * Math.PI * frequency / 44100;
        double c = Math.Cos(w), alpha = Math.Sin(w) / (2 * .707);
        double[] values;
        if (band == 1)
            values = [1 + alpha * a, -2 * c, 1 - alpha * a, 1 + alpha / a, -2 * c, 1 - alpha / a];
        else
        {
            double beta = Math.Sqrt(2 * a) * Math.Sin(w);
            values = band == 0
                ? [a * (a + 1 - (a - 1) * c + beta), 2 * a * (a - 1 - (a + 1) * c), a * (a + 1 - (a - 1) * c - beta),
                    a + 1 + (a - 1) * c + beta, -2 * (a - 1 + (a + 1) * c), a + 1 + (a - 1) * c - beta]
                : [a * (a + 1 + (a - 1) * c + beta), -2 * a * (a - 1 + (a + 1) * c), a * (a + 1 + (a - 1) * c - beta),
                    a + 1 - (a - 1) * c + beta, 2 * (a - 1 - (a + 1) * c), a + 1 - (a - 1) * c - beta];
        }
        return [(float)(values[0] / values[3]), (float)(values[1] / values[3]), (float)(values[2] / values[3]),
            (float)(values[4] / values[3]), (float)(values[5] / values[3])];
    }

    public static double Compressor(double input, double threshold, double ratio, double makeup)
    {
        double above = input - threshold, slope = 1 / Math.Max(1, ratio) - 1;
        double reduction = above <= -3 ? 0 : above >= 3 ? slope * above : slope * (above + 3) * (above + 3) / 12;
        return input + reduction + makeup;
    }

    public static double[][] Reverb(UMixFx fx)
    {
        // 用独立实例测量 4 秒单位脉冲的分窗峰值，保留固定幅度标尺，避免混响量变化被归一化抵消。
        const int bins = 240, framesPerBin = 735;
        Freeverb reverb = new();
        FxPresets.ReverbParams preset = FxPresets.Reverb.TryGetValue(fx.ReverbPreset ?? FxPresets.Off, out FxPresets.ReverbParams value)
            ? value : FxPresets.Reverb[FxPresets.Off];
        double wet = preset.Wet * Math.Clamp(fx.ReverbWet, 0, 2);
        double[][] result = [new double[bins], new double[bins]];
        Array.Fill(result[0], -90);
        Array.Fill(result[1], -90);
        if (wet <= 0) return result;
        reverb.Configure(fx.ReverbSize, fx.ReverbDamp, preset.Width, wet, 0, fx.ReverbPreDelayMs);
        float[] buffer = new float[framesPerBin * 2];
        for (int bin = 0; bin < bins; bin++)
        {
            Array.Clear(buffer);
            if (bin == 0) buffer[0] = buffer[1] = 1;
            reverb.Process(buffer, 0, buffer.Length);
            for (int channel = 0; channel < 2; channel++)
            {
                double peak = 0;
                for (int i = channel; i < buffer.Length; i += 2) peak = Math.Max(peak, Math.Abs(buffer[i]));
                result[channel][bin] = 20 * Math.Log10(Math.Max(1e-5, peak));
            }
        }
        return result;
    }
}
