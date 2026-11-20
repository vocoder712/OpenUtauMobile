using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using AudioToolbox;
using Avalonia.Threading;
using AVFoundation;
using Foundation;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using OpenUtau.Audio;
using OpenUtauMobile.Tools;
using Serilog;

namespace OpenUtauMobile.iOS.Services.Audio;

/// <summary>
/// 使用 AVAudioEngine 输出 OpenUtau 的浮点音频流。
/// </summary>
internal sealed class IosAudioOutput : IAudioOutput, IDisposable
{
    private const int SampleRate = 44100;
    private const int ChannelCount = 2;
    private readonly object syncRoot = new object();
    private readonly AVAudioEngine engine;
    private readonly AVAudioSourceNode sourceNode;
    private readonly AVAudioFormat outputFormat;
    private readonly NSObject interruptionObserver;
    private readonly NSObject routeChangeObserver;
    private readonly NSObject configurationChangeObserver;
    private ISampleProvider? sampleProvider;
    private PlaybackState playbackState = PlaybackState.Stopped;
    private float[] interleavedBuffer = Array.Empty<float>();
    private float[] leftBuffer = Array.Empty<float>();
    private float[] rightBuffer = Array.Empty<float>();
    private long playedFrames;
    private bool interruptionActive;
    private bool resumeAfterInterruption;
    private bool disposed;

    public IosAudioOutput()
    {
        ConfigureAudioSession();
        outputFormat = new AVAudioFormat(SampleRate, ChannelCount);
        engine = new AVAudioEngine();
        sourceNode = new AVAudioSourceNode(
            outputFormat,
            new AVAudioSourceNodeRenderHandler3(RenderAudio));
        engine.AttachNode(sourceNode);
        engine.Connect(sourceNode, engine.MainMixerNode, outputFormat);
        engine.Prepare();
        interruptionObserver = AVAudioSession.Notifications.ObserveInterruption((_, args) =>
        {
            var type = (AVAudioSessionInterruptionType)GetNotificationNumber(
                args.Notification, "AVAudioSessionInterruptionTypeKey");
            var options = (AVAudioSessionInterruptionOptions)GetNotificationNumber(
                args.Notification, "AVAudioSessionInterruptionOptionKey");
            Dispatcher.UIThread.Post(() => OnAudioInterruption(type, options));
        });
        routeChangeObserver = AVAudioSession.Notifications.ObserveRouteChange(
            (_, _) => Dispatcher.UIThread.Post(TryRestartAfterOutputChange));
        configurationChangeObserver = AVAudioEngine.Notifications.ObserveConfigurationChange(engine, (_, _) =>
            Dispatcher.UIThread.Post(TryRestartAfterOutputChange));
        Log.Information("AVAudioEngine 音频后端初始化成功");
    }

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

    public int DeviceNumber => 0;

    /// <summary>读取当前系统路由的实际输出端口；可用路由由系统选择器展示。</summary>
    public List<AudioOutputDevice> GetOutputDevices()
    {
        return GetCurrentRouteOutputDevices();
    }

    /// <summary>为所有 iOS 音频后端提供同一份系统路由信息。</summary>
    internal static List<AudioOutputDevice> GetCurrentRouteOutputDevices()
    {
        AVAudioSessionPortDescription[] outputs = AVAudioSession.SharedInstance().CurrentRoute.Outputs;
        List<AudioOutputDevice> devices = [];
        for (int index = 0; index < outputs.Length; index++)
        {
            AVAudioSessionPortDescription output = outputs[index];
            devices.Add(new AudioOutputDevice
            {
                api = "AVAudioSession",
                name = output.PortName,
                deviceNumber = index,
                guid = GuidTools.CreateGuidFromStrings("AVAudioSession", output.UID),
            });
        }
        return devices;
    }

    public long GetPosition()
    {
        lock (syncRoot)
        {
            return playedFrames * ChannelCount * sizeof(short);
        }
    }

    public void Init(ISampleProvider provider)
    {
        ArgumentNullException.ThrowIfNull(provider);
        ISampleProvider preparedProvider = provider;
        if (preparedProvider.WaveFormat.SampleRate != SampleRate)
        {
            preparedProvider = new WdlResamplingSampleProvider(preparedProvider, SampleRate);
        }
        preparedProvider = preparedProvider.ToStereo();

        lock (syncRoot)
        {
            sampleProvider = preparedProvider;
            playedFrames = 0;
            playbackState = PlaybackState.Stopped;
            resumeAfterInterruption = false;
        }
    }

    public void Play()
    {
        ThrowIfDisposed();
        lock (syncRoot)
        {
            if (sampleProvider == null || interruptionActive ||
                (playbackState == PlaybackState.Playing && engine.Running))
            {
                return;
            }
            playbackState = PlaybackState.Playing;
        }

        try
        {
            StartEngine();
        }
        catch
        {
            lock (syncRoot)
            {
                playbackState = PlaybackState.Stopped;
            }
            throw;
        }
    }

    public void Pause()
    {
        bool pauseEngine = false;
        lock (syncRoot)
        {
            if (interruptionActive)
            {
                resumeAfterInterruption = false;
                return;
            }
            if (playbackState == PlaybackState.Playing)
            {
                playbackState = PlaybackState.Paused;
                pauseEngine = true;
            }
        }
        if (pauseEngine)
        {
            engine.Pause();
        }
    }

    public void Stop()
    {
        lock (syncRoot)
        {
            playbackState = PlaybackState.Stopped;
            playedFrames = 0;
            resumeAfterInterruption = false;
        }
        engine.Pause();
    }

    /// <summary>仅接受当前路由；切换路由必须由用户操作系统选择器。</summary>
    public void SelectDevice(Guid guid, int deviceNumber)
    {
        if (guid == Guid.Empty || GetOutputDevices().Exists(device => device.guid == guid))
        {
            return;
        }
        throw new NotSupportedException("Select an iOS audio route using the system route picker.");
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }
        disposed = true;
        interruptionObserver.Dispose();
        routeChangeObserver.Dispose();
        configurationChangeObserver.Dispose();
        engine.Stop();
        engine.DetachNode(sourceNode);
        sourceNode.Dispose();
        outputFormat.Dispose();
        engine.Dispose();
    }

    private int RenderAudio(
        ref bool isSilence,
        ref AudioTimeStamp timestamp,
        uint frameCount,
        AudioBuffers outputData)
    {
        int requestedFrames = checked((int)frameCount);
        int requestedSamples = checked(requestedFrames * ChannelCount);
        EnsureBufferCapacity(requestedFrames, requestedSamples);

        int samplesRead = 0;
        lock (syncRoot)
        {
            if (playbackState == PlaybackState.Playing && sampleProvider != null)
            {
                samplesRead = sampleProvider.Read(interleavedBuffer, 0, requestedSamples);
                playedFrames += samplesRead / ChannelCount;
                if (samplesRead < requestedSamples)
                {
                    playbackState = PlaybackState.Stopped;
                }
            }
        }

        Array.Clear(interleavedBuffer, samplesRead, requestedSamples - samplesRead);
        isSilence = samplesRead == 0;
        CopyToNativeBuffers(outputData, requestedFrames);
        return 0;
    }

    private void EnsureBufferCapacity(int requestedFrames, int requestedSamples)
    {
        if (interleavedBuffer.Length < requestedSamples)
        {
            interleavedBuffer = new float[requestedSamples];
        }
        if (leftBuffer.Length < requestedFrames)
        {
            leftBuffer = new float[requestedFrames];
            rightBuffer = new float[requestedFrames];
        }
    }

    private void CopyToNativeBuffers(AudioBuffers outputData, int frameCount)
    {
        if (outputData.Count == 1)
        {
            AudioBuffer buffer = outputData[0];
            Marshal.Copy(interleavedBuffer, 0, buffer.Data, frameCount * ChannelCount);
            return;
        }

        for (int frame = 0; frame < frameCount; frame++)
        {
            int sourceIndex = frame * ChannelCount;
            leftBuffer[frame] = interleavedBuffer[sourceIndex];
            rightBuffer[frame] = interleavedBuffer[sourceIndex + 1];
        }
        AudioBuffer left = outputData[0];
        AudioBuffer right = outputData[1];
        Marshal.Copy(leftBuffer, 0, left.Data, frameCount);
        Marshal.Copy(rightBuffer, 0, right.Data, frameCount);
    }

    /// <summary>为原生音频后端启用系统播放会话。</summary>
    internal static void ConfigureAudioSession()
    {
        AVAudioSession session = AVAudioSession.SharedInstance();
        NSString category = AVAudioSessionCategory.Playback.GetConstant()
            ?? throw new InvalidOperationException("AVAudioSession playback category is unavailable.");
        if (!session.SetCategory(category, out NSError? categoryError))
        {
            throw new InvalidOperationException(
                categoryError?.LocalizedDescription ?? "AVAudioSession category configuration failed.");
        }
        if (!session.SetActive(true, out NSError? activeError))
        {
            throw new InvalidOperationException(
                activeError?.LocalizedDescription ?? "AVAudioSession activation failed.");
        }
    }

    /// <summary>
    /// 启动或恢复 AVAudioEngine，并在系统中断结束后重新激活音频会话。
    /// </summary>
    private void StartEngine()
    {
        if (engine.Running)
        {
            return;
        }

        ConfigureAudioSession();
        bool started = engine.StartAndReturnError(out NSError? error);
        if (!started)
        {
            throw new InvalidOperationException(error?.LocalizedDescription ?? "AVAudioEngine failed to start.");
        }
    }

    /// <summary>
    /// 在通话等系统中断期间暂停播放，并仅在系统允许时恢复。
    /// </summary>
    private void OnAudioInterruption(AVAudioSessionInterruptionType type, AVAudioSessionInterruptionOptions options)
    {
        if (disposed)
        {
            return;
        }

        if (type == AVAudioSessionInterruptionType.Began)
        {
            lock (syncRoot)
            {
                interruptionActive = true;
                resumeAfterInterruption = playbackState == PlaybackState.Playing;
                if (resumeAfterInterruption)
                {
                    playbackState = PlaybackState.Paused;
                }
            }
            engine.Pause();
            Log.Information("iOS 音频会话被中断，已暂停回放");
            return;
        }

        if (type != AVAudioSessionInterruptionType.Ended)
        {
            return;
        }

        bool resume;
        lock (syncRoot)
        {
            interruptionActive = false;
            resume = resumeAfterInterruption && (options & AVAudioSessionInterruptionOptions.ShouldResume) != 0;
            resumeAfterInterruption = false;
        }

        if (resume)
        {
            try
            {
                Play();
            }
            catch (Exception exception)
            {
                lock (syncRoot)
                {
                    playbackState = PlaybackState.Paused;
                }
                Log.Warning(exception, "iOS 音频中断结束后无法恢复回放");
            }
        }
    }

    /// <summary>
    /// 输出路由或引擎配置变化后，仅在仍需播放时重启已停止的引擎。
    /// </summary>
    private void TryRestartAfterOutputChange()
    {
        lock (syncRoot)
        {
            if (disposed || interruptionActive || playbackState != PlaybackState.Playing || engine.Running)
            {
                return;
            }
        }

        try
        {
            StartEngine();
        }
        catch (Exception exception)
        {
            lock (syncRoot)
            {
                if (playbackState == PlaybackState.Playing)
                {
                    playbackState = PlaybackState.Paused;
                }
            }
            Log.Warning(exception, "iOS 音频输出变化后无法恢复 AVAudioEngine");
        }
    }

    /// <summary>
    /// 从系统音频通知中读取数值字段。
    /// </summary>
    private static long GetNotificationNumber(NSNotification notification, string key)
    {
        using NSString nativeKey = new(key);
        return (notification.UserInfo?.ObjectForKey(nativeKey) as NSNumber)?.Int64Value ?? 0;
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
    }
}
