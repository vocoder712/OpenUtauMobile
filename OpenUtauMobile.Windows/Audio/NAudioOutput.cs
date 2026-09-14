using OpenUtauMobile.Helpers.Audio;
using System;
using System.Collections.Generic;
using NAudio.Wave;
using OpenUtau.Audio;
using OpenUtau.Core.Util;
using OpenUtauMobile.Services.Performance;

namespace OpenUtauMobile.Windows.Audio;

public class NAudioOutput : IAudioOutput, IAudioLatencySource
{
    private const int Channels = 2;

    private readonly object lockObj = new object();
    private WaveOutEvent? waveOutEvent;
    private int deviceNumber;
    private CountingSampleProvider? countedSource;

    public NAudioOutput()
    {
        if (Guid.TryParse(Preferences.Default.PlaybackDevice, out var guid))
        {
            SelectDevice(guid, Preferences.Default.PlaybackDeviceNumber);
        }
        else
        {
            SelectDevice(new Guid(), 0);
        }
    }

    public PlaybackState PlaybackState
    {
        get
        {
            lock (lockObj)
            {
                return waveOutEvent == null ? PlaybackState.Stopped : waveOutEvent.PlaybackState;
            }
        }
    }

    public int DeviceNumber => deviceNumber;

    public double? OutputLatencyMilliseconds
    {
        get
        {
            lock (lockObj)
            {
                if (waveOutEvent?.PlaybackState != PlaybackState.Playing || countedSource == null) return null;
                long played = waveOutEvent.GetPosition() / waveOutEvent.OutputWaveFormat.BlockAlign;
                return Math.Max(0, countedSource.Frames - played) * 1000.0 / countedSource.WaveFormat.SampleRate;
            }
        }
    }

    public long GetPosition()
    {
        lock (lockObj)
        {
            return waveOutEvent == null
                ? 0
                : waveOutEvent.GetPosition() / Channels;
        }
    }

    public void Init(ISampleProvider sampleProvider)
    {
        lock (lockObj)
        {
            if (waveOutEvent != null)
            {
                waveOutEvent.Stop();
                waveOutEvent.Dispose();
            }
            waveOutEvent = new WaveOutEvent
            {
                DeviceNumber = deviceNumber,
                // 三个 20 ms 缓冲块，降低默认长缓冲带来的交互延迟。
                DesiredLatency = 60,
                NumberOfBuffers = 3,
            };
            countedSource = new CountingSampleProvider(sampleProvider);
            waveOutEvent.Init(countedSource);
        }
    }

    public void Pause()
    {
        lock (lockObj)
        {
            if (waveOutEvent != null)
            {
                waveOutEvent.Pause();
            }
        }
    }

    public void Play()
    {
        lock (lockObj)
        {
            if (waveOutEvent != null)
            {
                waveOutEvent.Play();
            }
        }
    }

    public void Stop()
    {
        lock (lockObj)
        {
            if (waveOutEvent != null)
            {
                waveOutEvent.Stop();
                waveOutEvent.Dispose();
                waveOutEvent = null;
                countedSource = null;
            }
        }
    }

    public void SelectDevice(Guid guid, int deviceNumber)
    {
        Preferences.Default.PlaybackDevice = guid.ToString();
        Preferences.Default.PlaybackDeviceNumber = deviceNumber;
        Preferences.Save();
        if (deviceNumber < WaveOut.DeviceCount && WaveOut.GetCapabilities(deviceNumber).ProductGuid == guid)
        {
            this.deviceNumber = deviceNumber;
            return;
        }

        this.deviceNumber = 0;
        for (int i = 0; i < WaveOut.DeviceCount; ++i)
        {
            if (WaveOut.GetCapabilities(i).ProductGuid == guid)
            {
                this.deviceNumber = i;
                break;
            }
        }
    }

    public List<AudioOutputDevice> GetOutputDevices()
    {
        var outDevices = new List<AudioOutputDevice>();
        for (int i = 0; i < WaveOut.DeviceCount; ++i)
        {
            var capability = WaveOut.GetCapabilities(i);
            outDevices.Add(new AudioOutputDevice
            {
                api = "WaveOut",
                name = capability.ProductName,
                deviceNumber = i,
                guid = capability.ProductGuid,
            });
        }
        return outDevices;
    }
}
