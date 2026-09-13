using System.Collections.Generic;
using System.Threading;
using OpenUtau.Core.Ustx;

namespace OpenUtau.Core.SignalChain {
    /// <summary>播放会话的混音节点；界面提交快照，音频线程独占 DSP 状态。</summary>
    public sealed class PlaybackMixer {
        public UProject Project { get; }
        private readonly Dictionary<UTrack, MixerChannelSource> tracks = new Dictionary<UTrack, MixerChannelSource>();
        private MixerChannelSource master;
        private readonly bool applyFx;

        public PlaybackMixer(UProject project, bool applyFx = true) {
            Project = project;
            this.applyFx = applyFx;
        }

        internal MixerChannelSource WrapTrack(ISignalSource source, UTrack track) {
            var channel = new MixerChannelSource(source, false, TrackSettings(track));
            tracks.Add(track, channel);
            return channel;
        }

        internal ISignalSource WrapMaster(ISignalSource source) {
            master = new MixerChannelSource(source, true, MasterSettings());
            return master;
        }

        public void Refresh() {
            lock (Project) {
                foreach (var pair in tracks) {
                    pair.Value.Update(Project.tracks.Contains(pair.Key)
                        ? TrackSettings(pair.Key)
                        : new MixerChannelSource.Settings(0, 0, true, null));
                }
                master?.Update(MasterSettings());
            }
        }

        private MixerChannelSource.Settings TrackSettings(UTrack track) => new MixerChannelSource.Settings(
            track.Volume, track.Pan, !track.Solo && (track.Mute || Project.SoloTrackExist),
            applyFx ? track.MixFx : null);

        // 总线只用于汇总和电平监测，不保存或应用额外混音参数。
        private static MixerChannelSource.Settings MasterSettings() => new MixerChannelSource.Settings(
            0, 0, false, null);
    }

    internal sealed class MixerChannelSource : ISignalSource {
        internal sealed class Settings {
            public readonly double Volume, Pan;
            public readonly bool Muted;
            public readonly UMixFx Fx;
            public Settings(double volume, double pan, bool muted, UMixFx fx) {
                Volume = volume; Pan = pan; Muted = muted; Fx = fx?.Clone();
            }
        }

        public Fader Fader { get; }
        private readonly MixFxSource effects;
        private readonly Fader output;
        private Settings pending;

        public MixerChannelSource(ISignalSource source, bool master, Settings settings) {
            if (master) {
                effects = MixFxSource.Create(source, settings.Fx);
                Fader = new Fader(effects, balance: true);
            } else {
                Fader = new Fader(source);
                effects = MixFxSource.Create(Fader, settings.Fx);
            }
            // 静音在效果器后作用，混响尾音也被静音；恢复时不需要重建渲染。
            output = new Fader(master ? (ISignalSource)Fader : effects, balance: true);
            Apply(settings);
            Fader.SetScaleToTarget();
            output.SetScaleToTarget();
        }

        public void Update(Settings settings) => Interlocked.Exchange(ref pending, settings);
        public bool IsReady(int position, int count) => output.IsReady(position, count);

        public int Mix(int position, float[] buffer, int index, int count) {
            var settings = Interlocked.Exchange(ref pending, null);
            if (settings != null) Apply(settings);
            return output.Mix(position, buffer, index, count);
        }

        private void Apply(Settings settings) {
            Fader.Scale = PlaybackManager.DecibelToVolume(settings.Volume);
            Fader.Pan = (float)settings.Pan;
            output.Scale = settings.Muted ? 0 : 1;
            effects.Configure(settings.Fx);
        }
    }
}
