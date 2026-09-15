using System;

namespace OpenUtau.Core.SignalChain {
    public class Fader : ISignalSource {
        private readonly ISignalSource source;
        private float pan = 0;
        private float scale = 1;
        private float scaleTarget = 1;
        private float[] scaleBuffer;
        private readonly bool balance;

        public Fader(ISignalSource source, bool balance = false) {
            this.source = source;
            this.balance = balance;
        }

        public float Scale {
            get => scaleTarget;
            set => scaleTarget = value;
        }

        public float Pan {
            get => pan;
            set => pan = value;
        }

        public void SetScaleToTarget() {
            scale = scaleTarget;
        }

        public bool IsReady(int position, int count) {
            return source.IsReady(position, count);
        }

        public int Mix(int position, float[] buffer, int index, int count) {
            if (scaleBuffer == null || scaleBuffer.Length < count) {
                scaleBuffer = new float[count];
            }
            for (int i = 0; i < count; ++i) {
                scaleBuffer[i] = 0;
            }
            (float volumeLeft, float volumeRight) = MusicMath.PanToChannelVolumes(pan);
            // 总线使用立体声平衡，居中不再叠加轨道声像的 -3 dB 衰减。
            if (balance) {
                volumeLeft = Math.Min(1, 1 - Math.Clamp(pan, -100, 100) / 100);
                volumeRight = Math.Min(1, 1 + Math.Clamp(pan, -100, 100) / 100);
            }
            int ret = source.Mix(position, scaleBuffer, 0, count);
            for (int i = 0; i < count; ++i) {
                if (scaleTarget > scale) {
                    scale = Math.Min(scaleTarget, scale + 0.0005f);
                } else if (scaleTarget < scale) {
                    scale = Math.Max(scaleTarget, scale - 0.0005f);
                }
                buffer[index + i] += scaleBuffer[i] * scale * (i % 2 == 0 ? volumeLeft : volumeRight);
            }
            return ret;
        }
    }
}
