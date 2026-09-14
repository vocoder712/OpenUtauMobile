using System;
using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using OpenUtau.Core;
using OpenUtau.Core.SignalChain;
using OpenUtau.Core.Ustx;
using OpenUtauMobile.Services;

namespace OpenUtauMobile.Controls;

public sealed partial class MixerFxGraph
{
    private readonly DispatcherTimer _audioTimer = new() { Interval = TimeSpan.FromMilliseconds(33) };
    private PlaybackMixer? _audioMixer;
    private UTrack? _audioTrack;
    private MixerAudioAnalysis? _analysis;
    private readonly double[] _spectrum = CreateSpectrum();
    private double _inputDb = -60;
    private long _lastAudioTick;
    private double _age;

    private static double[] CreateSpectrum()
    {
        double[] levels = new double[MixerAudioAnalysis.BandCount];
        Array.Fill(levels, -90);
        return levels;
    }

    private bool AudioVisible()
    {
        if (!_attached || !IsEffectivelyVisible || _channel?.FxEnabled != true) return false;
        foreach (Visual ancestor in this.GetVisualAncestors())
        {
            if (ancestor is ScrollViewer scroll && this.TranslatePoint(default, scroll) is Point point &&
                !new Rect(point, Bounds.Size).Intersects(new Rect(scroll.Bounds.Size))) return false;
        }
        return true;
    }

    private void UpdateAudio(object? sender, EventArgs e)
    {
        UTrack? track = _channel?.Track;
        PlaybackMixer? mixer = AudioVisible() ? PlaybackManager.Inst.LiveMixer : null;
        if (mixer?.Project != DocManager.Inst.Project || track == null ||
            !DocManager.Inst.Project.tracks.Contains(track) || (!track.Solo && (track.Mute || DocManager.Inst.Project.SoloTrackExist))) mixer = null;
        if (mixer == null) track = null;
        if (mixer != _audioMixer || track != _audioTrack)
        {
            ReleaseAudio();
            if (mixer != null && track != null)
            {
                _audioMixer = mixer;
                _audioTrack = track;
                _analysis = new MixerAudioAnalysis();
                mixer.SetSampleObserver(track, _analysis.Push);
            }
        }
        if (_analysis == null) return;
        long now = Stopwatch.GetTimestamp();
        double elapsed = _lastAudioTick == 0 ? 1.0 / 30 : Math.Min(.25, (double)(now - _lastAudioTick) / Stopwatch.Frequency);
        _lastAudioTick = now;
        AdvanceAudio(elapsed);
        InvalidateVisual();
    }

    private void AdvanceAudio(double elapsed)
    {
        if (_analysis == null) return;
        bool fresh = _analysis.Read(Kind == MixerFxGraphKind.Equalizer, PlaybackManager.Inst.AudibleSamplePosition);
        _age = fresh ? 0 : _age + elapsed;
        double target = _age < .15 ? Math.Clamp(_analysis.PeakDb, -60, 12) : -60;
        _inputDb = Math.Max(target, _inputDb - 36 * elapsed);
        if (Kind == MixerFxGraphKind.Equalizer)
        {
            for (int i = 0; i < _spectrum.Length; i++)
            {
                double db = _age < .15 ? _analysis.SpectrumDb[i] : -90;
                _spectrum[i] = Math.Max(db, _spectrum[i] - 40 * elapsed);
            }
        }
    }

    private void ReleaseAudio()
    {
        if (_audioTrack != null) _audioMixer?.SetSampleObserver(_audioTrack, null);
        _audioMixer = null;
        _audioTrack = null;
        _analysis = null;
        _inputDb = -60;
        _age = 0;
        _lastAudioTick = 0;
        Array.Fill(_spectrum, -90);
        InvalidateVisual();
    }

    private double SpectrumY(double db) => Plot.Bottom - Math.Clamp((db + 90) / 90, 0, 1) * Plot.Height;

    private void DrawSpectrum(DrawingContext context)
    {
        StreamGeometry geometry = new();
        using (StreamGeometryContext path = geometry.Open())
        {
            path.BeginFigure(new Point(Plot.Left, Plot.Bottom), true);
            for (int i = 0; i < _spectrum.Length; i++)
                path.LineTo(new Point(X((double)i / (_spectrum.Length - 1)), SpectrumY(_spectrum[i])));
            path.LineTo(new Point(Plot.Right, Plot.Bottom));
            path.EndFigure(true);
        }
        using (context.PushOpacity(.3)) context.DrawGeometry(CurveBrush, null, geometry);
    }

    private void DrawCompressorLevel(DrawingContext context)
    {
        if (_channel == null || _inputDb <= -60) return;
        StreamGeometry geometry = new();
        double width = Math.Clamp((_inputDb + 60) / 72, 0, 1);
        using (StreamGeometryContext path = geometry.Open())
        {
            path.BeginFigure(new Point(Plot.Left, Plot.Bottom), true);
            for (int i = 0; i <= 160; i++)
            {
                double t = width * i / 160;
                path.LineTo(new Point(X(t), Y(MixerFxResponse.Compressor(-60 + 72 * t, _channel.ThresholdDb, _channel.Ratio, Makeup))));
            }
            path.LineTo(new Point(X(width), Plot.Bottom));
            path.EndFigure(true);
        }
        using (context.PushOpacity(.3)) context.DrawGeometry(CurveBrush, null, geometry);
    }
}
