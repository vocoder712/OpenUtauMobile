using System;
using System.Collections.Generic;
using System.Threading;
using Avalonia.Threading;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using OpenUtau.Audio;
using OpenUtauMobile.Services.Performance;
using Serilog;

namespace OpenUtauMobile.Browser.Services.Audio;

/// <summary>后台直接向 WASM 共享队列生产采样，浏览器音频线程独立消费。</summary>
internal sealed class BrowserAudioOutput : IAudioOutput, IAudioLatencySource, IDisposable
{
    private const int Channels = 2;
    private const int BlockSamples = 1024 * Channels;
    private static BrowserAudioOutput? active;
    private static int nextGeneration;
    private readonly object syncRoot = new();
    private BrowserAudioBuffer buffer = new();
    private ISampleProvider? sampleProvider;
    private int generation = Interlocked.Increment(ref nextGeneration);
    private int requestedState;
    private double transportLatency;
    private bool inputEnded;
    private bool synchronizationPosted;
    private bool disposed;

    /// <summary>原生音频线程不会进入托管运行时，由这个后台线程读取采样源。</summary>
    public BrowserAudioOutput()
    {
        if (BrowserAudioInterop.SampleRate == 0)
        {
            throw new InvalidOperationException("Web Audio is unavailable.");
        }
        if (Interlocked.CompareExchange(ref active, this, null) != null)
        {
            throw new InvalidOperationException("A browser audio output is already active.");
        }
        Thread producer = new(ProduceSamples) { IsBackground = true, Name = "Browser audio producer" };
        try
        {
            producer.Start();
        }
        catch
        {
            Interlocked.CompareExchange(ref active, null, this);
            throw;
        }
    }

    /// <summary>主线程回调访问的唯一输出实例。</summary>
    internal static BrowserAudioOutput? Active => Volatile.Read(ref active);

    /// <summary>读取已同步的传输状态；等待用户激活时保持可继续的暂停状态。</summary>
    public PlaybackState PlaybackState
    {
        get
        {
            lock (syncRoot)
            {
                if (requestedState == 0 || (inputEnded && buffer.QueuedFrames == 0)) return PlaybackState.Stopped;
                return requestedState == 1 && buffer.IsRunning && buffer.IsPlaying
                    ? PlaybackState.Playing : PlaybackState.Paused;
            }
        }
    }

    /// <summary>路由由浏览器选择器管理，不持久化枚举序号。</summary>
    public int DeviceNumber => 0;

    /// <summary>直接读取共享队列水位，并加上浏览器报告的设备延迟。</summary>
    public double? OutputLatencyMilliseconds
    {
        get
        {
            lock (syncRoot)
            {
                return transportLatency + buffer.QueuedFrames * 1000.0 / BrowserAudioInterop.SampleRate;
            }
        }
    }

    /// <summary>设备列表仅反映已授权且实际选中的系统路由。</summary>
    public List<AudioOutputDevice> GetOutputDevices() => BrowserAudioInterop.GetCurrentDevices();

    /// <summary>设备授权与切换必须通过设置页的用户操作完成。</summary>
    public void SelectDevice(Guid guid, int deviceNumber)
    {
        if (guid != Guid.Empty && !GetOutputDevices().Exists(device => device.guid == guid))
        {
            throw new NotSupportedException("Select an audio output using the browser device picker.");
        }
    }

    /// <summary>准备匹配上下文采样率的立体声源，并丢弃上一代缓冲。</summary>
    public void Init(ISampleProvider provider)
    {
        ISampleProvider prepared = provider;
        if (provider.WaveFormat.SampleRate != BrowserAudioInterop.SampleRate)
        {
            prepared = new WdlResamplingSampleProvider(provider, BrowserAudioInterop.SampleRate);
        }
        prepared = prepared.ToStereo();
        lock (syncRoot)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            ResetLocked();
            sampleProvider = prepared;
            ScheduleSynchronizationLocked();
        }
    }

    /// <summary>请求播放，等待上下文解锁及首批数据后再报告实际播放状态。</summary>
    public void Play()
    {
        lock (syncRoot)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (sampleProvider == null) return;
            requestedState = 1;
            buffer.IsPlaying = true;
            ScheduleSynchronizationLocked();
            Monitor.PulseAll(syncRoot);
        }
    }

    /// <summary>保留两侧缓冲和输入位置，暂停时不再读取采样源。</summary>
    public void Pause()
    {
        lock (syncRoot)
        {
            if (requestedState == 0 || disposed) return;
            requestedState = 2;
            buffer.IsPlaying = false;
            ScheduleSynchronizationLocked();
        }
    }

    /// <summary>切换播放代次，使旧的回调和未播放样本全部失效。</summary>
    public void Stop()
    {
        lock (syncRoot)
        {
            if (disposed) return;
            ResetLocked();
            ScheduleSynchronizationLocked();
        }
    }

    /// <summary>按播放管理器约定返回 44100 Hz 工程帧数乘四的字节位置。</summary>
    public long GetPosition()
    {
        lock (syncRoot)
        {
            return (long)(buffer.ConsumedFrames * 44100.0 / BrowserAudioInterop.SampleRate) * sizeof(float);
        }
    }

    /// <summary>唤醒并结束生产线程，释放缓冲；不在浏览器主线程等待线程退出。</summary>
    public void Dispose()
    {
        lock (syncRoot)
        {
            if (disposed) return;
            ResetLocked();
            sampleProvider = null;
            ScheduleSynchronizationLocked();
            disposed = true;
            Monitor.PulseAll(syncRoot);
        }
    }

    /// <summary>停止旧队列并切换独立内存，避免重置正在消费的读写指针。</summary>
    private void ResetLocked()
    {
        buffer.IsPlaying = false;
        buffer = new BrowserAudioBuffer();
        generation = Interlocked.Increment(ref nextGeneration);
        requestedState = 0;
        inputEnded = false;
        transportLatency = 0;
    }

    /// <summary>合并控制请求，主线程始终应用最新的播放意图。</summary>
    private void ScheduleSynchronizationLocked()
    {
        if (synchronizationPosted) return;
        synchronizationPosted = true;
        Dispatcher.UIThread.Post(SynchronizeTransport);
    }

    /// <summary>不持有状态锁调用 JS，防止浏览器回调与后台生产相互等待。</summary>
    private void SynchronizeTransport()
    {
        int version, state, address;
        lock (syncRoot)
        {
            synchronizationPosted = false;
            version = generation;
            state = requestedState;
            address = disposed ? 0 : buffer.Retain(version);
        }
        try
        {
            BrowserAudioInterop.SetPlayback(version, state, address);
        }
        catch (Exception exception) { Fail(version, exception, synchronize: false); }
        if (disposed) Interlocked.CompareExchange(ref active, null, this);
    }

    /// <summary>后台直接发布样本；满队列时短暂休眠，唤醒不依赖浏览器主线程。</summary>
    private void ProduceSamples()
    {
        float[] samples = new float[BlockSamples];
        while (true)
        {
            int version;
            ISampleProvider provider;
            BrowserAudioBuffer target;
            lock (syncRoot)
            {
                while (!disposed && (requestedState != 1 || inputEnded))
                    Monitor.Wait(syncRoot);
                if (disposed) return;
                if (buffer.QueuedFrames > BrowserAudioBuffer.TargetFrames - BlockSamples / Channels)
                {
                    // Worklet 不进入托管运行时；有界休眠独立检查它发布的读指针。
                    Monitor.Wait(syncRoot, 5);
                    continue;
                }
                version = generation;
                provider = sampleProvider!;
                target = buffer;
            }
            try
            {
                int count = provider.Read(samples, 0, BlockSamples);
                lock (syncRoot)
                {
                    if (!disposed && version == generation)
                    {
                        if (count > 0)
                        {
                            target.WriteSamples(samples.AsSpan(0, count));
                        }
                        else
                        {
                            inputEnded = true;
                            target.EndInput();
                        }
                    }
                }
            }
            catch (Exception exception) { Fail(version, exception); }
        }
    }

    /// <summary>只接受本代设备状态；采样水位和消费时钟直接读取共享内存。</summary>
    internal void UpdateStatus(int version, int state, double latency)
    {
        lock (syncRoot)
        {
            if (version != generation || disposed) return;
            if (state == 3)
            {
                ResetLocked();
                sampleProvider = null;
                return;
            }
            transportLatency = latency;
            if (state == 0 && inputEnded && buffer.QueuedFrames == 0)
            {
                requestedState = 0;
            }
        }
    }

    /// <summary>采样或传输失败后停止本代播放，并回到主线程报告异常。</summary>
    private void Fail(int version, Exception exception, bool synchronize = true)
    {
        lock (syncRoot)
        {
            if (version != generation || disposed) return;
            ResetLocked();
            sampleProvider = null;
            if (synchronize) ScheduleSynchronizationLocked();
        }
        Log.Error(exception, "浏览器音频输出失败");
        Dispatcher.UIThread.Post(() => BrowserAudioInterop.ReportError(version, exception.Message));
    }
}
