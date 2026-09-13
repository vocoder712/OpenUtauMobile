using System;
using OpenUtau.Core.SignalChain.Effects;
using OpenUtau.Core.Ustx;

namespace OpenUtau.Core.SignalChain {
    /// <summary>
    /// ISignalSource wrapper that applies the user-configured post-FX chain
    /// (3-band EQ -> compressor -> reverb).  When all effects bypass, the
    /// wrapper still adds a small amount of work (one memcpy + a few branches);
    /// callers that want literal zero overhead should check
    /// <see cref="IsAnythingEnabled"/> and skip wrapping entirely.
    ///
    /// The wrapper is stateful (filter state, envelope follower, reverb
    /// buffers) and must be constructed fresh per playback session.
    /// </summary>
    public class MixFxSource : ISignalSource {
        public const int SampleRate = 44100;
        public const int Channels = 2;

        private readonly ISignalSource source;
        private readonly BiquadEQ eq;
        private readonly SimpleCompressor comp;
        private readonly Freeverb reverb;
        private bool enabled = true;

        // Scratch buffer.  The signal chain in MasterAdapter passes in a
        // zeroed buffer and we mix into it; we need a private writeable copy
        // because the inner source uses additive mixing.
        private float[] scratch;

        public MixFxSource(ISignalSource source,
                           BiquadEQ eq, SimpleCompressor comp, Freeverb reverb) {
            this.source = source;
            this.eq = eq;
            this.comp = comp;
            this.reverb = reverb;
        }

        public bool IsReady(int position, int count) => source.IsReady(position, count);

        public int Mix(int position, float[] buffer, int index, int count) {
            if (!enabled) return source.Mix(position, buffer, index, count);
            // Allocate / grow scratch as needed.  In the common case the
            // playback buffer size is constant so this is allocated once.
            if (scratch == null || scratch.Length < count) {
                scratch = new float[count];
            }
            Array.Clear(scratch, 0, count);
            int ret = source.Mix(position, scratch, 0, count);

            // Apply effects in series.  Each effect short-circuits internally
            // when its parameters are at unity so individually-disabled stages
            // cost effectively nothing.
            eq.Process(scratch, 0, count);
            comp.Process(scratch, 0, count);
            reverb.Process(scratch, 0, count);

            // Additive mix into output (matches Fader / WaveMix convention).
            for (int i = 0; i < count; i++) {
                buffer[index + i] += scratch[i];
            }
            return ret;
        }

        /// <summary>True iff at least one effect would change the signal.</summary>
        public bool IsAnythingEnabled => !eq.IsBypassed || !comp.IsBypassed || !reverb.IsBypassed;

        /// <summary>
        /// Per-track wrapper.  Returns the inner source unchanged when the
        /// track has no FX configured or has Enabled = false.
        /// </summary>
        public static ISignalSource WrapWith(ISignalSource inner, UMixFx fx) {
            if (fx == null || !fx.Enabled) {
                return inner;
            }
            var wrapper = Create(inner, fx);
            return wrapper.IsAnythingEnabled ? wrapper : inner;
        }

        internal static MixFxSource Create(ISignalSource inner, UMixFx fx) {
            var wrapper = new MixFxSource(inner, new BiquadEQ(SampleRate, Channels),
                new SimpleCompressor(SampleRate, Channels), new Freeverb(SampleRate, Channels));
            wrapper.Configure(fx);
            return wrapper;
        }

        // 仅由音频线程在块边界调用，保留滤波器和混响状态。
        internal void Configure(UMixFx fx) {
            bool wasEnabled = enabled;
            enabled = fx != null && fx.Enabled;
            if (!enabled) {
                if (wasEnabled) { eq.Reset(); comp.Reset(); reverb.Reset(); }
                return;
            }
            eq.Configure(fx.EqLowDb, fx.EqMidFreq, 0.707, fx.EqMidDb, fx.EqHighDb);

            FxPresets.CompParams cParams = FxPresets.Comp.TryGetValue(fx.CompPreset ?? FxPresets.Off, out var cp)
                ? cp
                : FxPresets.Comp[FxPresets.Off];
            comp.Configure(fx.CompThresholdDb, fx.CompRatio,
                           cParams.AttackMs, cParams.ReleaseMs, fx.CompMakeupDb);

            FxPresets.ReverbParams rParams = FxPresets.Reverb.TryGetValue(fx.ReverbPreset ?? FxPresets.Off, out var rp)
                ? rp
                : FxPresets.Reverb[FxPresets.Off];
            double userWet = Math.Clamp(fx.ReverbWet, 0.0, 2.0);
            reverb.Configure(fx.ReverbSize, fx.ReverbDamp, rParams.Width,
                             rParams.Wet * userWet, rParams.Dry, fx.ReverbPreDelayMs);

        }
    }
}
