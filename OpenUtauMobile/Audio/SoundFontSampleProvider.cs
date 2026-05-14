using System;
using System.Collections.Generic;
using System.Threading;
using MeltySynth;
using OpenUtau.Core.SignalChain;

namespace OpenUtauMobile.Audio;

/// <summary>
/// 实现了 ISignalSource 接口的样本提供器
/// 双声道 44100Hz 32位浮点
/// An ISignalSource that renders MIDI notes using a SoundFont via MeltySynth.
/// Stereo output at 44100 Hz.
/// </summary>
public class SoundFontSampleProvider : ISignalSource
{
    private const int SampleRate = 44100;
    private const int Channels = 2;

    private readonly Synthesizer? _synth;
    private readonly Lock _lockObj = new();

    // 正在播放的音符集合 (MIDI note number)
    private readonly HashSet<int> _activeNotes = [];

    // Temporary buffer for mono/interleaved conversion
    private float[]? _tempLeft;
    private float[]? _tempRight;

    /// <summary>
    /// Whether the SoundFont was loaded successfully.
    /// </summary>
    public bool IsLoaded => _synth != null;

    /// <summary>
    /// 通过指定的 SF2 文件初始化
    /// </summary>
    /// <param name="soundFontPath">Path to the .sf2 file.</param>
    public SoundFontSampleProvider(string soundFontPath)
    {
        try
        {
            SoundFont sf2 = new(soundFontPath);
            SynthesizerSettings settings = new(SampleRate)
            {
                MaximumPolyphony = 64 // 最多允许 64 个同时发声的音符
            };
            _synth = new Synthesizer(sf2, settings);
            // 默认使用 bank 0, preset 0
            _synth.ProcessMidiMessage(0, 0xC0, 0, 0);
        }
        catch (Exception ex)
        {
            Serilog.Log.Error(ex, "初始化 SoundFont 样本提供器失败，sf2文件位于: {Path}", soundFontPath);
            _synth = null;
        }
    }

    /// <summary>
    /// 始终返回 true
    /// </summary>
    /// <param name="position"></param>
    /// <param name="count"></param>
    /// <returns></returns>
    public bool IsReady(int position, int count) => true;

    public int Mix(int position, float[] buffer, int offset, int count)
    {
        if (_synth == null)
        {
            return position + count;
        }

        lock (_lockObj)
        {
            // count is in stereo samples (left+right pairs)
            int frames = count / Channels;
            EnsureTempBuffers(frames);

            // Render stereo interleaved
            _synth.Render(_tempLeft!, _tempRight!);

            // Mix into output buffer (additive)
            for (int i = 0; i < frames; i++)
            {
                buffer[offset + (i * 2)] += _tempLeft![i];
                buffer[offset + (i * 2) + 1] += _tempRight![i];
            }
        }

        return position + count;
    }

    private void EnsureTempBuffers(int frames)
    {
        if (_tempLeft == null || _tempLeft.Length < frames)
        {
            _tempLeft = new float[frames];
        }

        if (_tempRight == null || _tempRight.Length < frames)
        {
            _tempRight = new float[frames];
        }
    }

    /// <summary>
    /// 开始演奏音符
    /// </summary>
    /// <param name="tone">MIDI note number (0-127).</param>
    /// <param name="velocity">击键速度 default 72.</param>
    public void NoteOn(int tone, int velocity = 72)
    {
        if (_synth == null || tone < 0 || tone > 127)
        {
            return;
        }

        lock (_lockObj)
        {
            if (_activeNotes.Add(tone))
            {
                _synth.NoteOn(0, tone, velocity);
            }
        }
    }

    /// <summary>
    /// 停止演奏音符
    /// </summary>
    /// <param name="tone">MIDI note number (0-127).</param>
    public void NoteOff(int tone)
    {
        if (_synth == null || tone < 0 || tone > 127)
        {
            return;
        }

        lock (_lockObj)
        {
            if (_activeNotes.Contains(tone))
            {
                _activeNotes.Remove(tone);
                _synth.NoteOff(0, tone);
            }
        }
    }

    /// <summary>
    /// Stop all active notes.
    /// </summary>
    public void AllNotesOff()
    {
        if (_synth == null)
        {
            return;
        }

        lock (_lockObj)
        {
            foreach (int tone in _activeNotes)
            {
                _synth.NoteOff(0, tone);
            }

            _activeNotes.Clear();
            _synth.Reset();
        }
    }
}