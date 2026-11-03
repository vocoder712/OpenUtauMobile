using System;
using System.Buffers;
using System.Collections.Generic;
using System.Threading;
using Avalonia.Threading;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using OpenUtau.Audio;
using OpenUtauMobile.Services.Performance;
using Serilog;

namespace OpenUtauMobile.Browser.Services.Audio;

/// <summary>后台生产浮点采样，通过主线程有界搬运到浏览器音频线程。</summary>
internal sealed class BrowserAudioOutput : IAudioOutput, IAudioLatencySource, IDisposable
{
    private const int Channels = 2;
    private const int BlockSamples = 1024 * Channels;
    private const int QueueBlockCount = 2;
    private static BrowserAudioOutput? active;
    private static int nextGeneration;
    private readonly object syncRoot = new();
    private readonly Queue<AudioBlock> blocks = new();
    private readonly byte[] transferBuffer = new byte[BlockSamples * sizeof(float)];
    private ISampleProvider? sampleProvider;
    private PlaybackState playbackState;
    private int generation = Interlocked.Increment(ref nextGeneration);
    private int requestedState;
    private int queuedSamples;
    private double consumedFrames;
    private double transportLatency;
    private bool inputEnded;
    private bool synchronizationPosted;
    private bool pumpPosted;
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
        get { lock (syncRoot) return playbackState; }
    }

    /// <summary>路由由浏览器选择器管理，不持久化枚举序号。</summary>
    public int DeviceNumber => 0;

    /// <summary>包括托管队列、音频线程队列及浏览器可报告的设备延迟。</summary>
    public double? OutputLatencyMilliseconds
    {
        get
        {
            lock (syncRoot)
            {
                return transportLatency + queuedSamples * 1000.0 / Channels / BrowserAudioInterop.SampleRate;
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
            playbackState = PlaybackState.Paused;
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
            playbackState = PlaybackState.Paused;
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
            return (long)(consumedFrames * 44100 / BrowserAudioInterop.SampleRate) * sizeof(float);
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

    /// <summary>清空本代输入与队列，调用者须持有状态锁。</summary>
    private void ResetLocked()
    {
        generation = Interlocked.Increment(ref nextGeneration);
        requestedState = 0;
        playbackState = PlaybackState.Stopped;
        inputEnded = false;
        consumedFrames = transportLatency = 0;
        queuedSamples = 0;
        while (blocks.TryDequeue(out AudioBlock block)) ArrayPool<float>.Shared.Return(block.Samples);
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
        int version, state;
        lock (syncRoot)
        {
            synchronizationPosted = false;
            version = generation;
            state = requestedState;
        }
        try
        {
            BrowserAudioInterop.SetPlayback(version, state);
            Pump(version);
        }
        catch (Exception exception) { Fail(version, exception, synchronize: false); }
        if (disposed) Interlocked.CompareExchange(ref active, null, this);
    }

    /// <summary>在后台读取采样源；任何时刻最多保留两块尚未传输的音频。</summary>
    private void ProduceSamples()
    {
        while (true)
        {
            int version;
            ISampleProvider provider;
            lock (syncRoot)
            {
                while (!disposed && (requestedState != 1 || inputEnded || blocks.Count >= QueueBlockCount))
                    Monitor.Wait(syncRoot);
                if (disposed) return;
                version = generation;
                provider = sampleProvider!;
            }
            float[] samples = ArrayPool<float>.Shared.Rent(BlockSamples);
            try
            {
                int count = provider.Read(samples, 0, BlockSamples);
                lock (syncRoot)
                {
                    if (!disposed && version == generation)
                    {
                        if (count > 0)
                        {
                            blocks.Enqueue(new AudioBlock(samples, count));
                            queuedSamples += count;
                            samples = null!;
                        }
                        else inputEnded = true;
                        if (!pumpPosted)
                        {
                            pumpPosted = true;
                            Dispatcher.UIThread.Post(PumpCurrentGeneration);
                        }
                    }
                }
            }
            catch (Exception exception) { Fail(version, exception); }
            finally { if (samples != null) ArrayPool<float>.Shared.Return(samples); }
        }
    }

    /// <summary>处理生产线程合并投递的数据通知。</summary>
    private void PumpCurrentGeneration()
    {
        int version;
        lock (syncRoot) { pumpPosted = false; version = generation; }
        Pump(version);
    }

    /// <summary>每次仅批量搬运有空位的样本，不在音频回调中等待后台线程。</summary>
    internal void Pump(int version)
    {
        try
        {
            for (int batch = 0; batch < 4; batch++)
            {
                int writable = BrowserAudioInterop.WritableFrames(version);
                AudioBlock block;
                bool ended;
                lock (syncRoot)
                {
                    if (disposed || version != generation || requestedState != 1) return;
                    ended = inputEnded && blocks.Count == 0;
                    if (!blocks.TryPeek(out block))
                    {
                        if (!ended) return;
                    }
                    else
                    {
                        if (writable < block.Count / Channels) return;
                        blocks.Dequeue();
                        queuedSamples -= block.Count;
                        Monitor.PulseAll(syncRoot);
                    }
                }
                if (ended) { BrowserAudioInterop.EndInput(version); return; }
                try
                {
                    Buffer.BlockCopy(block.Samples, 0, transferBuffer, 0, block.Count * sizeof(float));
                    BrowserAudioInterop.WriteSamples(version, transferBuffer, block.Count);
                }
                finally { ArrayPool<float>.Shared.Return(block.Samples); }
            }
        }
        catch (Exception exception) { Fail(version, exception); }
    }

    /// <summary>只接受本代消费时钟，输入结束须等浏览器排空后才进入停止状态。</summary>
    internal void UpdateStatus(int version, double frames, int state, double latency)
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
            consumedFrames = Math.Max(consumedFrames, frames);
            transportLatency = latency;
            playbackState = requestedState == 0 ? PlaybackState.Stopped
                : state == 1 && requestedState == 1 ? PlaybackState.Playing : PlaybackState.Paused;
            if (state == 0 && inputEnded && blocks.Count == 0)
            {
                requestedState = 0;
                playbackState = PlaybackState.Stopped;
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

    /// <summary>保存池化采样块及其有效交错样本数量。</summary>
    private readonly record struct AudioBlock(float[] Samples, int Count);
}
