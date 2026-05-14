using System;
using System.Collections.Generic;
using System.Threading;
using Android.App;
using Android.Content;
using Android.Media;
using Android.OS;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using OpenUtau.Audio;
using OpenUtau.Core;
using OpenUtauMobile.Tools;
using Serilog;
using Debug = System.Diagnostics.Debug;
using Stream = Android.Media.Stream;

namespace OpenUtauMobile.Android.Audio
{
    public class AudioTrackOutput : IAudioOutput
    {
        private readonly AudioTrack? _audioTrack;
        private Thread? _playbackThread; // 播放线程
        private bool _isPlaying; // 播放线程是否正在进行
        private readonly bool _isInitialized; // 是否已初始化

        private ISampleProvider? _sampleProvider; // 样本提供器成员
        private readonly int _bufferSize; // 缓冲区大小
        private const Encoding WaveFormat = Encoding.PcmFloat;
        private const int SampleRate = 44100;

        /// <summary>
        /// 获取当前播放状态
        /// </summary>
        public PlaybackState PlaybackState
        {
            get
            {
                if (_audioTrack == null) return PlaybackState.Stopped; // 如果AudioTrack为空则返回停止状态
                return _audioTrack.PlayState switch
                {
                    PlayState.Stopped => PlaybackState.Stopped,// 停止状态
                    PlayState.Paused => PlaybackState.Paused,// 暂停状态
                    PlayState.Playing => PlaybackState.Playing,// 播放状态
                    _ => PlaybackState.Stopped,// 默认返回停止状态
                };
            }
        }
        public int DeviceNumber { get; set; } = 0; // Android不能选择设备
        
        /// <summary>
        /// 列出可用播放设备
        /// </summary>
        /// <returns></returns>
        public List<AudioOutputDevice> GetOutputDevices()
        {
            if (Build.VERSION.SdkInt < BuildVersionCodes.M)
            {
                // Android6 以下版本不支持列出音频设备
                return [];
            }

            Context context = Application.Context;
            Java.Lang.Object? audioManagerObject = context.GetSystemService(Context.AudioService);
            if (audioManagerObject is not AudioManager audioManager) return [];
            AudioDeviceInfo[]? devices = audioManager.GetDevices(GetDevicesTargets.Outputs);
            if (devices == null || devices.Length == 0)
            {
                return [];
            }
            List<AudioOutputDevice> deviceList = [];
            for (int i = 0; i < devices.Length; i++)
            {
                AudioDeviceInfo device = devices[i];
                deviceList.Add(new AudioOutputDevice
                {
                    api = "AudioTrack",
                    name = (device.ProductName ?? "") + " " + device.Type,
                    deviceNumber = i,
                    guid = GuidTools.CreateGuidFromStrings(device.Id.ToString(), device.Type.ToString()),
                });
            }
            foreach (AudioOutputDevice device in deviceList)
            {
                Debug.WriteLine($"找到音频输出设备: {device.name}, guid: {device.guid}");
            }
            return deviceList;
        }

        /// <summary>
        /// 获取当前播放位置
        /// </summary>
        /// <returns></returns>
        public long GetPosition()
        {
            if (_audioTrack == null) return 0; // 如果AudioTrack为空则返回0

            // PlaybackHeadPosition返回的是帧数，一帧是所有声道的一个样本
            // 需要乘以每帧字节数来获得字节位置
            // 在PcmFloat格式下，每个样本是4字节(float)，立体声是2个通道
            long framesPlayed = _audioTrack.PlaybackHeadPosition;
            const int bytesPerFrame = 2 * sizeof(float); // 立体声 * 每个样本4字节(float)

            // 返回采样数（以单个声道计）
            return framesPlayed * bytesPerFrame / 2;
        }

        public AudioTrackOutput()
        {
            GetOutputDevices();
            try
            {
                const ChannelOut channelOut = ChannelOut.Stereo;
                // 获取最小缓冲区大小
                _bufferSize = AudioTrack.GetMinBufferSize(SampleRate, channelOut, WaveFormat);

                // 首先创建 AudioAttributes 实例
                AudioAttributes? audioAttributes = null;
                try
                {
                    audioAttributes = new AudioAttributes.Builder()
                        .SetUsage(AudioUsageKind.Media)? // 用途为媒体播放
                        .SetContentType(AudioContentType.Music)? // 内容类型为音乐
                        .Build();
                }
                catch (Exception ex)
                {
                    DocManager.Inst.ExecuteCmd(new ErrorMessageNotification(ex));
                    Log.Error("创建 AudioAttributes 失败: {ExMessage}", ex.Message);
                }

                // 创建 AudioFormat 实例
                AudioFormat? audioFormat = null;
                try
                {
                    audioFormat = new AudioFormat.Builder()
                        .SetSampleRate(SampleRate)?
                        .SetChannelMask(channelOut)
                        .SetEncoding(WaveFormat)?
                        .Build();
                }
                catch (Exception ex)
                {
                    DocManager.Inst.ExecuteCmd(new ErrorMessageNotification(ex));
                    Log.Error("创建 AudioFormat 失败: {ExMessage}", ex.Message);
                }

                // 只有当必要的组件都成功创建后，才创建 AudioTrack
                if (audioAttributes != null && audioFormat != null)
                {
                    if (Build.VERSION.SdkInt >= BuildVersionCodes.M) // Android6及以上使用新的
                    {
                        AudioTrack.Builder builder = new();
                        builder.SetAudioAttributes(audioAttributes);
                        builder.SetAudioFormat(audioFormat);
                        builder.SetBufferSizeInBytes(_bufferSize);
                        builder.SetTransferMode(AudioTrackMode.Stream); // 流式传输模式
                        _audioTrack = builder.Build();
                        _isInitialized = _audioTrack != null;
                    }
                    else
                    {
                        // 对于Android6以下版本，使用旧的构造函数
                        _audioTrack = new AudioTrack(
                            Stream.Music,
                            SampleRate,
                            channelOut,
                            WaveFormat,
                            _bufferSize,
                            AudioTrackMode.Stream);
                        _isInitialized = _audioTrack.State == AudioTrackState.Initialized;
                    }

                }
                else
                {
                    Log.Error("创建 AudioTrack 所需组件不完整，初始化失败");
                    _isInitialized = false;
                }

                if (_audioTrack != null)
                {
                    _audioTrack.SetVolume(1f);
                }

                Log.Information("AudioTrackOutput初始化！: \nsampleRate={SampleRate}, channelOut={ChannelOut}, _waveFormat={WaveFormat}, _bufferSize={BufferSize}", SampleRate, channelOut, WaveFormat, _bufferSize);
            }
            catch (Exception ex)
            {
                DocManager.Inst.ExecuteCmd(new ErrorMessageNotification(ex));
                Log.Error("AudioTrackOutput初始化失败: {ExMessage}", ex.Message);
                _isInitialized = false;
            }
            
            // Only force a preferred output device when the user explicitly selected one.
            if (!string.IsNullOrEmpty(OpenUtau.Core.Util.Preferences.Default.PlaybackDevice))
            {
                SelectDevice(Guid.Empty, OpenUtau.Core.Util.Preferences.Default.PlaybackDeviceNumber);
            }
        }

        /// <summary>
        /// 切换样本提供器
        /// </summary>
        /// <param name="sampleProvider"></param>
        public void Init(ISampleProvider sampleProvider)
        {
            if (sampleProvider.WaveFormat.SampleRate != SampleRate)
            {
                sampleProvider = new WdlResamplingSampleProvider(sampleProvider, SampleRate);
            }
            _sampleProvider = sampleProvider.ToStereo();
        }

        public void Pause()
        {
            if (!_isPlaying || !_isInitialized) return; // 如果没有在播放或未初始化则返回
            _isPlaying = false; // 设为没在播放

            if (_playbackThread is { IsAlive: true })
            {
                _playbackThread.Join(); // 等待播放线程结束
            }
            _audioTrack?.Pause(); // 暂停
        }

        public void Play()
        {
            if (_isPlaying || !_isInitialized || _audioTrack == null || _sampleProvider == null) return; // 如果正在播放或未初始化则返回

            _isPlaying = true; // 设置为正在播放
            _audioTrack.SetVolume(1f);
            _audioTrack.Play(); // 播放

            _playbackThread = new Thread(PlaybackLoop); // 创建播放线程
            _playbackThread.Start(); // 启动播放线程
        }

        /// <summary>
        /// 核心播放线程
        /// </summary>
        private void PlaybackLoop()
        {
            if (_audioTrack == null || _sampleProvider == null)
            {
                _isPlaying = false;
                return;
            }

            float[] buffer = new float[_bufferSize / 4]; // 缓冲区大小

            while (_isPlaying)
            {
                int samplesRead = _sampleProvider.Read(buffer, 0, buffer.Length);
                if (samplesRead <= 0)
                {
                    _isPlaying = false;
                    _audioTrack?.Stop();
                    break;
                }
                if (_audioTrack != null) // 再次检查以确保在循环过程中没有被释放
                {
                    int result = _audioTrack.Write(buffer, 0, samplesRead, WriteMode.Blocking);
                    if (result < 0)
                    {
                        Log.Warning("AudioTrack异常: {Result}", result);
                    }
                }
                else
                {
                    _isPlaying = false;
                    break;
                }
            }
        }

        /// <summary>
        /// 选择音频输出设备
        /// </summary>
        /// <param name="guid"></param>
        /// <param name="deviceNumber">主要依据</param>
        public void SelectDevice(Guid guid, int deviceNumber)
        {
            if (Build.VERSION.SdkInt < BuildVersionCodes.M || _audioTrack == null)
            {
                Log.Warning("当前系统版本不支持手动选择音频设备或 AudioTrack 未初始化");
                return;
            }
            if (string.IsNullOrEmpty(OpenUtau.Core.Util.Preferences.Default.PlaybackDevice) && deviceNumber == 0)
            {
                Log.Information("未指定音频输出设备，保持系统默认路由");
                return;
            }
            try
            {
                // 获取当前所有可用的输出设备
                Context context = Application.Context;
                AudioManager? audioManager = context.GetSystemService(Context.AudioService) as AudioManager;
                AudioDeviceInfo[]? devices = audioManager?.GetDevices(GetDevicesTargets.Outputs);

                if (devices == null) return;

                // 根据传入的 deviceNumber 找到对应的设备对象
                if (deviceNumber < 0 || deviceNumber >= devices.Length) return;
                AudioDeviceInfo targetDevice = devices[deviceNumber];
            
                // 3. 设置首选设备
                bool success = _audioTrack.SetPreferredDevice(targetDevice);
            
                if (success)
                {
                    Log.Information("已成功切换音频输出设备至: {DeviceName}", targetDevice.ProductName);
                }
                else
                {
                    Log.Warning("无法设置首选设备: {DeviceName}", targetDevice.ProductName);
                }
            }
            catch (Exception ex)
            {
                Log.Error("SelectDevice 失败: {Message}", ex.Message);
            }
        }

        public void Stop()
        {
            Debug.WriteLine("==========\nAudioTrack Stop方法调用\n");
            try
            {
                if (_playbackThread is { IsAlive: true })
                {
                    _isPlaying = false; // 设为没有播放
                    _playbackThread.Join(); // 等待播放线程结束
                }

                _audioTrack?.Stop(); // 停止
            }
            catch (Exception e)
            {
                Log.Error("AudioTrack停止时发生异常: {Exception}", e);
            }
        }
    }
}
