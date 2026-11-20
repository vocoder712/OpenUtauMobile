using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using OpenUtau.Audio;
using Serilog;

namespace OpenUtauMobile.iOS.Services.Audio;

/// <summary>通过静态 AOT 回调使用 Worldline 内的 MiniAudio，并由系统管理输出路由。</summary>
internal sealed class IosMiniAudioOutput : IAudioOutput, IDisposable
{
    private const int SampleRate = 44100;
    private const int ChannelCount = 2;
    // Worldline 的音频回调为全局单例，必须在释放设备之后才能解除托管实例引用。
    private static IosMiniAudioOutput? activeOutput;
    private readonly object syncRoot = new();
    private IntPtr nativeContext;
    private ISampleProvider? sampleProvider;
    private float[] sampleBuffer = [];
    private PlaybackState playbackState;
    private long playedFrames;
    private bool deviceStarted;
    private bool disposed;

    /// <summary>启用播放会话，并创建使用默认系统路由的原生设备。</summary>
    public unsafe IosMiniAudioOutput()
    {
        IosAudioOutput.ConfigureAudioSession();
        if (Interlocked.CompareExchange(ref activeOutput, this, null) != null)
        {
            throw new InvalidOperationException("A MiniAudio output is already active.");
        }
        try
        {
            nativeContext = ou_init_audio_device_auto(&RenderAudio);
            if (nativeContext == IntPtr.Zero)
            {
                throw new InvalidOperationException("MiniAudio failed to initialize the system output.");
            }
            Log.Information("MiniAudio 音频后端初始化成功");
        }
        catch
        {
            Interlocked.CompareExchange(ref activeOutput, null, this);
            throw;
        }
    }

    /// <summary>读取当前播放状态。</summary>
    public PlaybackState PlaybackState
    {
        get
        {
            lock (syncRoot)
            {
                return playbackState;
            }
        }
    }

    /// <summary>系统管理的路由不使用持久设备编号。</summary>
    public int DeviceNumber => 0;

    /// <summary>返回系统当前实际使用的输出端口。</summary>
    public List<AudioOutputDevice> GetOutputDevices() => IosAudioOutput.GetCurrentRouteOutputDevices();

    /// <summary>路由选择由系统控件完成，拒绝对未激活的端口执行无效切换。</summary>
    public void SelectDevice(Guid guid, int deviceNumber)
    {
        if (guid != Guid.Empty && !GetOutputDevices().Exists(device => device.guid == guid))
        {
            throw new NotSupportedException("Select an iOS audio route using the system route picker.");
        }
    }

    /// <summary>将输入转换为原生设备要求的浮点立体声格式。</summary>
    public void Init(ISampleProvider provider)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        ISampleProvider preparedProvider = provider;
        if (provider.WaveFormat.SampleRate != SampleRate)
        {
            preparedProvider = new WdlResamplingSampleProvider(provider, SampleRate);
        }
        preparedProvider = preparedProvider.ToStereo();
        lock (syncRoot)
        {
            sampleProvider = preparedProvider;
            playedFrames = 0;
            playbackState = PlaybackState.Stopped;
        }
    }

    /// <summary>开始或继续播放，保留暂停前的采样位置。</summary>
    public void Play()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        lock (syncRoot)
        {
            if (sampleProvider == null || playbackState == PlaybackState.Playing)
            {
                return;
            }
            playbackState = PlaybackState.Playing;
            if (deviceStarted)
            {
                return;
            }
        }
        int result = ou_audio_device_start(nativeContext);
        if (result != 0)
        {
            Stop();
            throw new InvalidOperationException($"MiniAudio failed to start: {result}.");
        }
        deviceStarted = true;
    }

    /// <summary>暂停消费输入，原生回调继续输出静音。</summary>
    public void Pause()
    {
        lock (syncRoot)
        {
            if (playbackState == PlaybackState.Playing)
            {
                playbackState = PlaybackState.Paused;
            }
        }
    }

    /// <summary>停止消费输入并重置播放时钟。</summary>
    public void Stop()
    {
        lock (syncRoot)
        {
            playbackState = PlaybackState.Stopped;
            playedFrames = 0;
        }
    }

    /// <summary>按现有播放管理器约定返回每帧四字节的位置。</summary>
    public long GetPosition()
    {
        lock (syncRoot)
        {
            return playedFrames * ChannelCount * sizeof(short);
        }
    }

    /// <summary>先等待原生设备释放，再解除回调对托管对象的引用。</summary>
    public void Dispose()
    {
        lock (syncRoot)
        {
            if (disposed)
            {
                return;
            }
            disposed = true;
            playbackState = PlaybackState.Stopped;
            sampleProvider = null;
        }
        ou_free_audio_device(nativeContext);
        nativeContext = IntPtr.Zero;
        Interlocked.CompareExchange(ref activeOutput, null, this);
    }

    /// <summary>使用静态函数指针进入托管代码，避免 iOS AOT 不支持的实例委托跳板。</summary>
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static unsafe void RenderAudio(float* buffer, uint channels, uint frameCount)
    {
        Span<float> destination = new(buffer, checked((int)(channels * frameCount)));
        destination.Clear();
        IosMiniAudioOutput? output = Volatile.Read(ref activeOutput);
        if (output == null)
        {
            return;
        }
        try
        {
            output.ReadSamples(destination);
        }
        catch (Exception exception)
        {
            // 异常不能跨越原生回调边界；停止播放后保留静音输出。
            output.Stop();
            Log.Error(exception, "MiniAudio 音频回调失败");
        }
    }

    /// <summary>从当前采样源填充输出，并在输入结束后停止推进播放位置。</summary>
    private void ReadSamples(Span<float> destination)
    {
        lock (syncRoot)
        {
            if (disposed || playbackState != PlaybackState.Playing || sampleProvider == null)
            {
                return;
            }
            if (sampleBuffer.Length < destination.Length)
            {
                sampleBuffer = new float[destination.Length];
            }
            int read = sampleProvider.Read(sampleBuffer, 0, destination.Length);
            sampleBuffer.AsSpan(0, read).CopyTo(destination);
            playedFrames += read / ChannelCount;
            if (read < destination.Length)
            {
                playbackState = PlaybackState.Stopped;
            }
        }
    }

    [DllImport("__Internal", CallingConvention = CallingConvention.Cdecl)]
    private static extern unsafe IntPtr ou_init_audio_device_auto(delegate* unmanaged[Cdecl]<float*, uint, uint, void> callback);

    [DllImport("__Internal", CallingConvention = CallingConvention.Cdecl)]
    private static extern int ou_audio_device_start(IntPtr context);

    [DllImport("__Internal", CallingConvention = CallingConvention.Cdecl)]
    private static extern int ou_free_audio_device(IntPtr context);
}