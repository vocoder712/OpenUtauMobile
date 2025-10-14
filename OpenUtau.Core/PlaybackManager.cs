using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using OpenUtau.Core.Render;
using OpenUtau.Core.SignalChain;
using OpenUtau.Core.Ustx;
using OpenUtau.Core.Util;
using Serilog;

namespace OpenUtau.Core {
    public class SineGen : ISampleProvider {
        public WaveFormat WaveFormat => waveFormat;
        public double Freq { get; set; }
        public bool Stop { get; set; }
        private WaveFormat waveFormat;
        private double phase;
        private double gain;
        public SineGen() {
            waveFormat = WaveFormat.CreateIeeeFloatWaveFormat(44100, 1);
            Freq = 440;
            gain = 1;
        }
        public int Read(float[] buffer, int offset, int count) {
            double delta = 2 * Math.PI * Freq / waveFormat.SampleRate;
            for (int i = 0; i < count; i++) {
                if (Stop) {
                    gain = Math.Max(0, gain - 0.01);
                }
                if (gain == 0) {
                    return i;
                }
                phase += delta;
                double sampleValue = Math.Sin(phase) * 0.2 * gain;
                buffer[offset++] = (float)sampleValue;
            }
            return count;
        }
    }

    public class PlaybackManager : SingletonBase<PlaybackManager>, ICmdSubscriber {
        private PlaybackManager() {
            DocManager.Inst.AddSubscriber(this);
            try {
                Directory.CreateDirectory(PathManager.Inst.CachePath);
                RenderEngine.ReleaseSourceTemp();
            } catch (Exception e) {
                Log.Error(e, "Failed to release source temp.");
            }
        }

        List<Fader> faders;
        MasterAdapter masterMix;
        double startMs;
        public int StartTick => DocManager.Inst.Project.timeAxis.MsPosToTickPos(startMs);
        CancellationTokenSource renderCancellation;

        public Audio.IAudioOutput AudioOutput { get; set; } = new Audio.DummyAudioOutput();
        public bool Playing => AudioOutput.PlaybackState == PlaybackState.Playing;
        public bool StartingToPlay { get; private set; }

        public void PlayTestSound() {
            masterMix = null;
            AudioOutput.Stop();
            AudioOutput.Init(new SignalGenerator(44100, 1).Take(TimeSpan.FromSeconds(1)));
            AudioOutput.Play();
        }

        public SineGen PlayTone(double freq) {
            masterMix = null;
            AudioOutput.Stop();
            var sineGen = new SineGen() {
                Freq = freq,
            };
            AudioOutput.Init(sineGen);
            AudioOutput.Play();
            return sineGen;
        }

        public void PlayOrPause(int tick = -1, int endTick = -1, int trackNo = -1) {
            if (Playing) {
                PausePlayback();
            } else {
                Play(
                    DocManager.Inst.Project,
                    tick: tick == -1 ? DocManager.Inst.playPosTick : tick,
                    endTick: endTick,
                    trackNo: trackNo);
            }
        }

        public void Play(UProject project, int tick, int endTick = -1, int trackNo = -1) {
            if (AudioOutput.PlaybackState == PlaybackState.Paused) {
                AudioOutput.Play();
                return;
            }
            AudioOutput.Stop();
            Render(project, tick, endTick, trackNo);
            StartingToPlay = true;
        }

        public void StopPlayback() {
            AudioOutput.Stop();
        }

        public void PausePlayback() {
            AudioOutput.Pause();
        }

        private void StartPlayback(double startMs, MasterAdapter masterAdapter) {
            this.startMs = startMs;
            var start = TimeSpan.FromMilliseconds(startMs);
            Log.Information($"StartPlayback at {start}");
            masterMix = masterAdapter;
            AudioOutput.Stop();
            AudioOutput.Init(masterMix);
            AudioOutput.Play();
        }

        private void Render(UProject project, int tick, int endTick, int trackNo) {
            Task.Run(() => {
                try {
                    RenderEngine engine = new RenderEngine(project, startTick: tick, endTick: endTick, trackNo: trackNo);
                    var result = engine.RenderProject(DocManager.Inst.MainScheduler, ref renderCancellation);
                    faders = result.Item2;
                    StartingToPlay = false;
                    StartPlayback(project.timeAxis.TickPosToMsPos(tick), result.Item1);
                } catch (Exception e) {
                    Log.Error(e, "Failed to render.");
                    StopPlayback();
                    var customEx = new MessageCustomizableException("Failed to render.", "<translate:errors.failed.render>", e);
                    DocManager.Inst.ExecuteCmd(new ErrorMessageNotification(customEx));
                }
            });
        }

        public void UpdatePlayPos() {
            if (AudioOutput != null && AudioOutput.PlaybackState == PlaybackState.Playing && masterMix != null) {
                double ms = (AudioOutput.GetPosition() / sizeof(float) - masterMix.Waited / 2) * 1000.0 / 44100;
                int tick = Math.Max(DocManager.Inst.Project.timeAxis.MsPosToTickPos(startMs + ms), 0);
                //Debug.WriteLine($"UpdatePlayPos毫秒: startms{startMs} ms{ms}, 等待渲染：{masterMix.IsWaiting}");
                if (masterMix.IsWaiting) {
                    tick = DocManager.Inst.playPosTick;
                }
                DocManager.Inst.ExecuteCmd(new SetPlayPosTickNotification(tick, masterMix.IsWaiting));
            }
        }

        public static float DecibelToVolume(double db) {
            return (db <= -24) ? 0 : (float)MusicMath.DecibelToLinear((db < -16) ? db * 2 + 16 : db);
        }

        // Exporting mixdown
        public async Task RenderMixdown(UProject project, string exportPath) {
            await Task.Run(() => {                    
                string id = Guid.NewGuid().ToString();
                try {
                    DocManager.Inst.ExecuteCmd(new ExportingNotification(0, $"导出 {exportPath} 中...", id));

                    RenderEngine engine = new RenderEngine(project);
                    var projectMix = engine.RenderMixdown(DocManager.Inst.MainScheduler, ref renderCancellation, wait: true).Item1;
                    DocManager.Inst.ExecuteCmd(new ExportedNotification($"成功导出 {exportPath}.", id));

                    CheckFileWritable(exportPath);
                    WaveFileWriter.CreateWaveFile16(exportPath, new ExportAdapter(projectMix).ToMono(1, 0));
                    DocManager.Inst.ExecuteCmd(new ExportedNotification($"成功导出 {exportPath}.", id));
                } catch (IOException ioe) {
                    var customEx = new MessageCustomizableException($"Failed to export {exportPath}.", $"<translate:errors.failed.export>: {exportPath}", ioe);
                    DocManager.Inst.ExecuteCmd(new ErrorMessageNotification(customEx));
                    DocManager.Inst.ExecuteCmd(new ExportedNotification($"失败导出 {exportPath}.", id));
                } catch (Exception e) {
                    var customEx = new MessageCustomizableException("Failed to render.", $"<translate:errors.failed.render>: {exportPath}", e);
                    DocManager.Inst.ExecuteCmd(new ErrorMessageNotification(customEx));
                    DocManager.Inst.ExecuteCmd(new ExportedNotification($"失败导出 {exportPath}.", id));
                }
            });
        }

        // Exporting each tracks
        public async Task RenderToFiles(UProject project, string exportPath) {
            await Task.Run(() => {
                string file = "";
                string id = Guid.NewGuid().ToString();
                try {
                    DocManager.Inst.ExecuteCmd(new ExportingNotification(0, $"导出到 {exportPath} 中...", id));

                    RenderEngine engine = new RenderEngine(project);
                    var trackMixes = engine.RenderTracks(DocManager.Inst.MainScheduler, ref renderCancellation);
                    for (int i = 0; i < trackMixes.Count; ++i) {
                        if (trackMixes[i] == null || i >= project.tracks.Count || project.tracks[i].Muted) {
                            continue;
                        }
                        file = PathManager.Inst.GetExportPath(exportPath, project.tracks[i]);
                        DocManager.Inst.ExecuteCmd(new ExportedNotification($"成功导出 {file}.", id));

                        CheckFileWritable(file);
                        WaveFileWriter.CreateWaveFile16(file, new ExportAdapter(trackMixes[i]).ToMono(1, 0));
                        DocManager.Inst.ExecuteCmd(new ExportedNotification($"成功导出 {file}.", id));
                    }
                } catch (IOException ioe) {
                    var customEx = new MessageCustomizableException($"Failed to export {file}.", $"<translate:errors.failed.export>: {file}", ioe);
                    DocManager.Inst.ExecuteCmd(new ErrorMessageNotification(customEx));
                    DocManager.Inst.ExecuteCmd(new ExportedNotification($"失败导出 {file}.", id));
                } catch (Exception e) {
                    var customEx = new MessageCustomizableException("Failed to render.", "<translate:errors.failed.render>", e);
                    DocManager.Inst.ExecuteCmd(new ErrorMessageNotification(customEx));
                    DocManager.Inst.ExecuteCmd(new ExportedNotification($"失败导出 {file}.", id));
                }
            });
        }

        private void CheckFileWritable(string filePath) {
            if (!File.Exists(filePath)) {
                return;
            }
            using (FileStream fp = File.Open(filePath, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite)) {
                return;
            }
        }

        void SchedulePreRender() {
            Log.Information("SchedulePreRender");
            var engine = new RenderEngine(DocManager.Inst.Project);
            engine.PreRenderProject(ref renderCancellation);
        }

        #region ICmdSubscriber

        public void OnNext(UCommand cmd, bool isUndo) {
            if (cmd is SeekPlayPosTickNotification) {
                var _cmd = cmd as SeekPlayPosTickNotification;
                StopPlayback();
                int tick = _cmd!.playPosTick;
                DocManager.Inst.ExecuteCmd(new SetPlayPosTickNotification(tick, false, _cmd.pause));
            } else if (cmd is VolumeChangeNotification) {
                var _cmd = cmd as VolumeChangeNotification;
                if (faders != null && faders.Count > _cmd.TrackNo) {
                    faders[_cmd.TrackNo].Scale = DecibelToVolume(_cmd.Volume);
                }
            } else if (cmd is PanChangeNotification) {
                var _cmd = cmd as PanChangeNotification;
                if (faders != null && faders.Count > _cmd!.TrackNo) {
                    faders[_cmd.TrackNo].Pan = (float)_cmd.Pan;
                }
            } else if (cmd is LoadProjectNotification) {
                StopPlayback();
                DocManager.Inst.ExecuteCmd(new SetPlayPosTickNotification(0));
            }
            if (cmd is PreRenderNotification || cmd is LoadProjectNotification) {
                if (Util.Preferences.Default.PreRender) {
                    SchedulePreRender();
                }
            }
        }

        #endregion
    }
}
