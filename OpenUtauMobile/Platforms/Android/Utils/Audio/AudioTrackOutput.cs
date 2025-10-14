using Android.Content;
using Android.Media;
using NAudio.Wave;
using Android.OS;
using OpenUtau.Audio;
using OpenUtau.Core;
using OpenUtauMobile.Utils;
using Serilog;
using System.Diagnostics;
using Debug = System.Diagnostics.Debug;
using Stream = Android.Media.Stream;

namespace OpenUtauMobile.Platforms.Android.Utils.Audio
{
    public class AudioTrackOutput : IAudioOutput
    {
        private readonly AudioTrack? _audioTrack;
        private Thread? _playbackThread; // 播放线程
        private bool _isPlaying = false; // 播放线程是否正在进行
        private bool _isInitialized = false; // 是否已初始化

        private ISampleProvider? _sampleProvider; // 样本提供器成员
        private int _bufferSize; // 缓冲区大小
        private const Encoding _waveFormat = Encoding.PcmFloat;
        const int sampleRate = 44100;

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
        public List<AudioOutputDevice> GetOutputDevices()
        {
            if (Build.VERSION.SdkInt < BuildVersionCodes.M)
            {
                // Android6 以下版本不支持列出音频设备
                return [];
            }
            Context context = Platform.CurrentActivity ?? Platform.AppContext;
            Java.Lang.Object? audioManagerObject = context.GetSystemService(Context.AudioService);
            if (audioManagerObject is AudioManager audioManager)
            {
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
                        name = (device.ProductName ?? "") + " " + device.Type.ToString(),
                        deviceNumber = i,
                        guid = GuidTools.CreateGuidFromStrings(device.Id.ToString(), device.Type.ToString()),
                    });
                }
                foreach (var device in deviceList)
                {
                    Debug.WriteLine($"找到音频输出设备: {device.name}, guid: {device.guid}");
                }
                return deviceList;
            }
            return [];
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
            int bytesPerFrame = 2 * sizeof(float); // 立体声 * 每个样本4字节(float)

            // 返回采样数（以单个声道计）
            return framesPlayed * bytesPerFrame / 2;
        }

        public AudioTrackOutput()
        {
            GetOutputDevices();
            try
            {
                ChannelOut channelOut = ChannelOut.Stereo;
                // 获取最小缓冲区大小
                _bufferSize = AudioTrack.GetMinBufferSize(sampleRate, channelOut, _waveFormat);

                // 首先创建 AudioAttributes 实例
                AudioAttributes? audioAttributes = null;
                try
                {
                    audioAttributes = new AudioAttributes.Builder()?
                        .SetUsage(AudioUsageKind.Game)? // 用途为游戏
                        .SetContentType(AudioContentType.Music)? // 内容类型为音乐
                        .Build();
                }
                catch (Exception ex)
                {
                    DocManager.Inst.ExecuteCmd(new ErrorMessageNotification(ex));
                    Log.Error($"创建 AudioAttributes 失败: {ex.Message}");
                }

                // 创建 AudioFormat 实例
                AudioFormat? audioFormat = null;
                try
                {
                    audioFormat = new AudioFormat.Builder()?
                        .SetSampleRate(sampleRate)?
                        .SetChannelMask(channelOut)?
                        .SetEncoding(_waveFormat)?
                        .Build();
                }
                catch (Exception ex)
                {
                    DocManager.Inst.ExecuteCmd(new ErrorMessageNotification(ex));
                    Log.Error($"创建 AudioFormat 失败: {ex.Message}");
                }

                // 只有当必要的组件都成功创建后，才创建 AudioTrack
                if (audioAttributes != null && audioFormat != null)
                {
                    if (Build.VERSION.SdkInt >= BuildVersionCodes.M)
                    {
                        var builder = new AudioTrack.Builder();
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
                            sampleRate,
                            ChannelConfiguration.Stereo,
                            _waveFormat,
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

                Log.Information($"AudioTrackOutput初始化！: \nsampleRate={sampleRate}, channelOut={channelOut}, _waveFormat={_waveFormat}, _bufferSize={_bufferSize}");
            }
            catch (Exception ex)
            {
                DocManager.Inst.ExecuteCmd(new ErrorMessageNotification(ex));
                Log.Error($"AudioTrackOutput初始化失败: {ex.Message}");
                _isInitialized = false;
            }
        }

        /// <summary>
        /// 初始化
        /// </summary>
        /// <param name="sampleProvider"></param>
        public void Init(ISampleProvider sampleProvider)
        {
            _sampleProvider = sampleProvider; // 初始化样本提供器
        }

        public void Pause()
        {
            Debug.WriteLine("暂停方法调用");
            if (!_isPlaying || !_isInitialized) return; // 如果没有在播放或未初始化则返回
            _isPlaying = false; // 设为没在播放

            if (_playbackThread != null && _playbackThread.IsAlive)
            {
                _playbackThread.Join(); // 等待播放线程结束
            }
            _audioTrack?.Pause(); // 暂停
        }

        public void Play()
        {
            if (_isPlaying || !_isInitialized || _audioTrack == null || _sampleProvider == null) return; // 如果正在播放或未初始化则返回

            _isPlaying = true; // 设置为正在播放
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
                if (samplesRead > 0)
                {
                    if (_audioTrack != null) // 再次检查以确保在循环过程中没有被释放
                    {
                        int result = _audioTrack.Write(buffer, 0, buffer.Length, WriteMode.Blocking);
                        if (result < 0)
                        {
                            Debug.WriteLine($"AudioTrack异常: {result}");
                        }
                    }
                    else
                    {
                        _isPlaying = false;
                        break;
                    }
                }
            }
        }

        public void SelectDevice(Guid guid, int deviceNumber)
        {
            
        }

        public void Stop()
        {
            Debug.WriteLine("==========\nAudioTrack Stop方法调用\n");
            try
            {
                if (_playbackThread != null && _playbackThread.IsAlive)
                {
                    _isPlaying = false; // 设为没有播放
                    _playbackThread.Join(); // 等待播放线程结束
                }

                if (_audioTrack != null)
                {
                    _audioTrack.Stop(); // 停止
                }
            }
            catch (Exception e)
            {
                Debug.WriteLine($"AudioTrack停止时发生异常: {e}");
            }
        }
    }
}
