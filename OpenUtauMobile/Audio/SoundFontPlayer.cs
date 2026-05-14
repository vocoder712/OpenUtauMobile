using System;
using System.IO;
using System.Threading;
using NAudio.Wave;
using OpenUtau.Audio;
using OpenUtau.Core;
using OpenUtau.Core.Util;
using Serilog;

namespace OpenUtauMobile.Audio;

/// <summary>
/// SF播放器单例类
/// </summary>
public class SoundFontPlayer : IDisposable
{
    private static SoundFontPlayer? _instance;
    private static readonly Lock InstanceLock = new();

    /// <summary>
    /// SF 播放器实例
    /// </summary>
    public static SoundFontPlayer Instance
    {
        get
        {
            if (_instance == null)
            {
                lock (InstanceLock)
                {
                    _instance ??= new SoundFontPlayer();
                }
            }

            return _instance;
        }
    }

    private SoundFontSampleProvider? _provider;
    private SoundFontAdapter? _adapter;
    private string _currentSoundFontPath = string.Empty;
    private bool _disposed;

    private SoundFontPlayer()
    {
        TryLoadSoundFont();
    }

    /// <summary>
    /// 是否已加载
    /// </summary>
    public bool IsReady => _provider?.IsLoaded == true;

    /// <summary>
    /// Try to load the SoundFont 从用户设置的路径加载 SoundFont 文件
    /// TODO: 改成异步加载
    /// </summary>
    public void TryLoadSoundFont()
    {
        string path = Preferences.Default.SoundFontPath;

        if (string.IsNullOrEmpty(path) || !File.Exists(path))
        {
            Log.Warning("未找到位于 {Path} 的 SoundFont 文件", path);
            _provider = null;
            _adapter = null;
            return;
        }

        // Don't reload if same path
        if (path == _currentSoundFontPath && _provider?.IsLoaded == true)
        {
            return;
        }

        try
        {
            _provider = new SoundFontSampleProvider(path);
            if (_provider.IsLoaded)
            {
                _adapter = new SoundFontAdapter(_provider);
                _currentSoundFontPath = path;
                Log.Information("已加载 SoundFont: {Path}", path);
            }
            else
            {
                _provider = null;
                _adapter = null;
                Log.Warning("无法加载 SoundFont: {Path}", path);
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "加载 SoundFont 时出错: {Path}", path);
            _provider = null;
            _adapter = null;
        }
    }

    /// <summary>
    /// 播放一个音符 (MIDI note number).
    /// </summary>
    /// <param name="tone">MIDI note number (e.g., 60 = C4).</param>
    public void PlayNote(int tone)
    {
        if (_provider == null || _adapter == null)
        {
            return;
        }

        _provider.NoteOn(tone);
        // EnsurePlaying();
        IAudioOutput audioOutput = PlaybackManager.Inst.AudioOutput;
        if (audioOutput.PlaybackState == PlaybackState.Playing)
        {
            return;
        }

        audioOutput.Stop();
        audioOutput.Init(_adapter);
        audioOutput.Play();
    }

    /// <summary>
    /// 关闭指定音符的播放 (MIDI note number)。
    /// </summary>
    /// <param name="tone">MIDI note number.</param>
    public void StopNote(int tone)
    {
        _provider?.NoteOff(tone);
    }

    /// <summary>
    /// 关闭所有正在播放的音符
    /// </summary>
    public void StopAllNotes()
    {
        _provider?.AllNotesOff();
        // IAudioOutput audioOutput = PlaybackManager.Inst.AudioOutput;
        // audioOutput.Stop();
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        StopAllNotes();
        _provider = null;
        _adapter = null;
        _instance = null;
        GC.SuppressFinalize(this);
    }
}