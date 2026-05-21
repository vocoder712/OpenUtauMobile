#if IOS
using System;
using System.Collections.Generic;
using System.Threading;
using Foundation;
using AVFoundation;
using OpenUtau.Audio;
using NAudio.Wave;
using Serilog;
using System.Runtime.InteropServices;
using System.IO;

namespace OpenUtauMobile.Platforms.iOS.Utils.Audio {
    public class AVAudioEngineOutput : IAudioOutput, IDisposable {
        private ISampleProvider sampleProvider = null!;
        private AVAudioPlayer? player;
        private string? tmpFilePath;
        private volatile bool running = false;
        private int sampleRate = 44100;
        private int channels = 1;

        public PlaybackState PlaybackState { get; private set; } = PlaybackState.Stopped;
        public int DeviceNumber { get; private set; } = 0;

        public AVAudioEngineOutput() {
            // noop
        }

        public void SelectDevice(Guid guid, int deviceNumber) {
            DeviceNumber = deviceNumber;
        }

        public void Init(ISampleProvider sampleProvider) {
            this.sampleProvider = sampleProvider;
            sampleRate = sampleProvider.WaveFormat.SampleRate;
            channels = sampleProvider.WaveFormat.Channels;

            Log.Information("AVAudioEngineOutput.Init: sampleRate={SampleRate}, channels={Channels}", sampleRate, channels);

            // Configure AVAudioSession
            try {
                var session = AVAudioSession.SharedInstance();
                session.SetCategory(AVAudioSessionCategory.Playback);
                session.SetActive(true);
                Log.Information("AVAudioSession activated for AVAudioEngineOutput");
            } catch (Exception ex) {
                Log.Warning(ex, "Failed to activate AVAudioSession in AVAudioEngineOutput");
            }

            // Render entire sampleProvider to a temporary WAV file for AVAudioPlayer playback
            try {
                string tmpDir = Path.GetTempPath();
                string file = Path.Combine(tmpDir, $"openutaumobile_render_{Guid.NewGuid()}.wav");
                using (var fs = System.IO.File.Create(file)) {
                    // write WAV header later; use WaveFileWriter from NAudio to simplify
                    using (var writer = new NAudio.Wave.WaveFileWriter(fs, NAudio.Wave.WaveFormat.CreateIeeeFloatWaveFormat(sampleRate, channels))) {
                        float[] buffer = new float[4096 * Math.Max(1, channels)];
                        int read;
                        while ((read = sampleProvider.Read(buffer, 0, buffer.Length)) > 0) {
                            writer.WriteSamples(buffer, 0, read);
                        }
                    }
                }
                tmpFilePath = file;
                Log.Information("Rendered temp WAV to {Path}", tmpFilePath);
            } catch (Exception ex) {
                Log.Error(ex, "Failed to render temp WAV for AVAudioEngineOutput");
            }
        }

        public void Play() {
            if (PlaybackState == PlaybackState.Playing) return;
            // Ensure Init rendered a temp file
            // if no tmpFilePath available, warn
            if (tmpFilePath == null) {
                Log.Warning("AVAudioEngineOutput.Play called before Init or render failed");
                return;
            }
            running = true;
            if (tmpFilePath != null && System.IO.File.Exists(tmpFilePath)) {
                try {
                    NSError err;
                    var url = NSUrl.FromFilename(tmpFilePath);
                    player = AVAudioPlayer.FromUrl(url, out err);
                    if (err != null) Log.Error("AVAudioPlayer init error: {Err}", err);
                    player.NumberOfLoops = 0;
                    player.PrepareToPlay();

                    // Attach finished handler to cleanup temp file when playback ends
                    player.FinishedPlaying += (s, e) => {
                        try {
                            PlaybackState = PlaybackState.Stopped;
                            running = false;
                            Log.Information("AVAudioPlayer finished playing, cleaning up temp file");
                            if (!string.IsNullOrEmpty(tmpFilePath) && System.IO.File.Exists(tmpFilePath)) {
                                try { System.IO.File.Delete(tmpFilePath); Log.Information("Deleted temp WAV {Path}", tmpFilePath); } catch (Exception ex) { Log.Warning(ex, "Failed to delete temp WAV {Path}", tmpFilePath); }
                                tmpFilePath = null;
                            }
                        } catch (Exception ex) {
                            Log.Warning(ex, "Exception in FinishedPlaying handler");
                        }
                    };

                    player.Play();
                } catch (Exception ex) {
                    Log.Error(ex, "Failed to start AVAudioPlayer");
                }
            } else {
                Log.Warning("No rendered WAV available for playback");
            }
            PlaybackState = PlaybackState.Playing;
        }

        public void Pause() {
            if (PlaybackState != PlaybackState.Playing) return;
            try { player?.Pause(); } catch (Exception ex) { Log.Error(ex, "Pause failed"); }
            PlaybackState = PlaybackState.Paused;
            running = false;
        }

        public void Stop() {
            if (PlaybackState == PlaybackState.Stopped) return;
            running = false;
            try {
                player?.Stop();
            } catch (Exception ex) { Log.Error(ex, "Stop failed"); }
            // try to delete tmp file after stop
            try {
                if (!string.IsNullOrEmpty(tmpFilePath) && System.IO.File.Exists(tmpFilePath)) {
                    try { System.IO.File.Delete(tmpFilePath); Log.Information("Deleted temp WAV {Path} on Stop", tmpFilePath); } catch (Exception ex) { Log.Warning(ex, "Failed to delete temp WAV {Path} on Stop", tmpFilePath); }
                    tmpFilePath = null;
                }
            } catch (Exception) { }
            PlaybackState = PlaybackState.Stopped;
        }

        private void FillLoop() {
            // no-op: rendering performed in Init for AVAudioPlayer-based playback
            return;
        }

        public long GetPosition() {
            try {
                if (player != null && player.Playing) {
                    // Return bytes-per-channel to match other outputs: samples * sizeof(float)
                    double seconds = player.CurrentTime;
                    long samples = (long)(seconds * sampleRate);
                    long bytesPerChannel = samples * sizeof(float);
                    return bytesPerChannel;
                }
            } catch (Exception ex) {
                Log.Warning(ex, "GetPosition() failed");
            }
            return 0;
        }

        public List<AudioOutputDevice> GetOutputDevices() {
            return new List<AudioOutputDevice> { new AudioOutputDevice { name = "iOS AVAudioEngine", api = "AVAudioEngine", deviceNumber = 0, guid = Guid.Empty } };
        }

        public void Dispose() {
            try { Stop(); } catch { }
            try { player?.Dispose(); } catch { }
            try {
                if (!string.IsNullOrEmpty(tmpFilePath) && System.IO.File.Exists(tmpFilePath)) {
                    try { System.IO.File.Delete(tmpFilePath); Log.Information("Deleted temp WAV {Path} on Dispose", tmpFilePath); } catch (Exception ex) { Log.Warning(ex, "Failed to delete temp WAV {Path} on Dispose", tmpFilePath); }
                    tmpFilePath = null;
                }
            } catch { }
        }
    }
}
#endif
