// This file contains code adapted from the hifisampler project.
// It was rewritten and modified for OpenUtauMobile2.
// See THIRD_PARTY_NOTICES.md and licenses/HifiSampler.Apache-2.0.txt.

using NWaves.Transforms;

namespace OpenUtauMobile.Plugin.Renderers.HifiSampler {
    /// <summary>
    /// DSP utilities for HifiSampler renderer.
    /// Matches hifisampler's wav2mel.py and util/audio.py preprocessing exactly.
    /// Parameters from config.default.yaml:
    ///   sample_rate=44100, n_fft=2048, win_size=2048, hop_size=512,
    ///   origin_hop_size=128, n_mels=128, mel_fmin=40, mel_fmax=16000
    /// </summary>
    public static class HifiSamplerDsp {
        public const int SampleRate = 44100;
        public const int NFft = 2048;
        public const int WinSize = 2048;
        public const int HopSize = 512;
        public const int OriginHopSize = 128;
        public const int NMels = 128;
        public const float MelFMin = 40f;
        public const float MelFMax = 16000f;
        public const int NFreqs = NFft / 2 + 1; // 1025

        private static float[,] melFilterbank;
        private static readonly object fbLock = new object();

        public static float[,] GetMelFilterbank() {
            if (melFilterbank != null) return melFilterbank;
            lock (fbLock) {
                if (melFilterbank != null) return melFilterbank;
                melFilterbank = CreateMelFilterbank(SampleRate, NFft, NMels, MelFMin, MelFMax);
                return melFilterbank;
            }
        }

        // Match librosa.filters.mel defaults used by upstream hifisampler: Slaney mel scale + Slaney norm.
        static float HzToMel(float hz) {
            const float fSp = 200f / 3f;
            const float minLogHz = 1000f;
            const float minLogMel = minLogHz / fSp;
            const float logStep = 0.06875178f; // ln(6.4) / 27
            if (hz < minLogHz) {
                return hz / fSp;
            }
            return minLogMel + MathF.Log(hz / minLogHz) / logStep;
        }

        static float MelToHz(float mel) {
            const float fSp = 200f / 3f;
            const float minLogHz = 1000f;
            const float minLogMel = minLogHz / fSp;
            const float logStep = 0.06875178f; // ln(6.4) / 27
            if (mel < minLogMel) {
                return mel * fSp;
            }
            return minLogHz * MathF.Exp(logStep * (mel - minLogMel));
        }

        /// <summary>
        /// Create mel filterbank [n_mels, n_fft/2+1] matching librosa.filters.mel defaults.
        /// </summary>
        static float[,] CreateMelFilterbank(int sr, int nFft, int nMels, float fMin, float fMax) {
            int nFreqs = nFft / 2 + 1;
            float[] fftFreqs = new float[nFreqs];
            for (int i = 0; i < nFreqs; i++) fftFreqs[i] = (float)sr * i / nFft;

            float melMin = HzToMel(fMin);
            float melMax = HzToMel(fMax);
            float[] melEdges = new float[nMels + 2];
            for (int i = 0; i < nMels + 2; i++) {
                melEdges[i] = MelToHz(melMin + (melMax - melMin) * i / (nMels + 1));
            }

            var fb = new float[nMels, nFreqs];
            for (int m = 0; m < nMels; m++) {
                float fLeft = melEdges[m], fCenter = melEdges[m + 1], fRight = melEdges[m + 2];
                for (int k = 0; k < nFreqs; k++) {
                    float f = fftFreqs[k];
                    if (f >= fLeft && f <= fCenter && fCenter > fLeft)
                        fb[m, k] = (f - fLeft) / (fCenter - fLeft);
                    else if (f > fCenter && f <= fRight && fRight > fCenter)
                        fb[m, k] = (fRight - f) / (fRight - fCenter);
                }
                float enorm = 2f / (fRight - fLeft);
                for (int k = 0; k < nFreqs; k++) fb[m, k] *= enorm;
            }
            return fb;
        }

        /// <summary>
        /// Compute mel spectrogram matching hifisampler (origin_hop_size=128, center=false + manual reflect pad).
        /// Returns [NMels, nFrames] with log compression applied.
        /// genderShift in semitones adjusts FFT size (matching wav2mel.py key_shift parameter).
        /// </summary>
        public static float[,] ComputeMelSpectrogram(float[] audio, float genderShift = 0) {
            float factor = MathF.Pow(2f, genderShift / 12f);
            int nFftNew = (int)MathF.Round(NFft * factor);
            int winSizeNew = (int)MathF.Round(WinSize * factor);
            int hop = OriginHopSize;

            int padLeft = (winSizeNew - hop) / 2;
            int padRight = (winSizeNew - hop + 1) / 2;
            float[] padded = ReflectPad(audio, padLeft, padRight);

            int nFrames = (padded.Length - winSizeNew) / hop + 1;
            if (nFrames <= 0) return new float[NMels, 0];

            float[] window = CreateHannWindow(winSizeNew);

            // Power-of-2 FFT size
            int fftSize = 1;
            while (fftSize < nFftNew) fftSize <<= 1;

            var fft = new RealFft(fftSize);
            float[] fftBuf = new float[fftSize];
            float[] re = new float[fftSize / 2 + 1];
            float[] im = new float[fftSize / 2 + 1];

            int specBins = nFftNew / 2 + 1;
            float[,] spec = new float[specBins, nFrames];

            for (int fr = 0; fr < nFrames; fr++) {
                int start = fr * hop;
                Array.Clear(fftBuf, 0, fftSize);
                int copyLen = Math.Min(winSizeNew, padded.Length - start);
                for (int i = 0; i < copyLen; i++) fftBuf[i] = padded[start + i] * window[i];
                fft.Direct(fftBuf, re, im);
                for (int k = 0; k < specBins; k++) {
                    spec[k, fr] = MathF.Sqrt(re[k] * re[k] + im[k] * im[k]);
                }
            }

            // If gender shift applied, resize to standard n_fft/2+1 bins
            float[,] resizedSpec;
            if (genderShift != 0) {
                int stdBins = NFreqs;
                resizedSpec = new float[stdBins, nFrames];
                int copyBins = Math.Min(specBins, stdBins);
                float rescale = (float)WinSize / winSizeNew;
                for (int t = 0; t < nFrames; t++) {
                    for (int k = 0; k < copyBins; k++) {
                        resizedSpec[k, t] = spec[k, t] * rescale;
                    }
                }
            } else {
                resizedSpec = spec;
            }

            // mel = fb @ spec
            var fb = GetMelFilterbank();
            int nFreqsFinal = resizedSpec.GetLength(0);
            float[,] mel = new float[NMels, nFrames];
            for (int m = 0; m < NMels; m++) {
                for (int t = 0; t < nFrames; t++) {
                    float sum = 0;
                    for (int k = 0; k < nFreqsFinal && k < fb.GetLength(1); k++) {
                        sum += fb[m, k] * resizedSpec[k, t];
                    }
                    mel[m, t] = sum;
                }
            }

            // Log compression: ln(max(x, 1e-9))  matching dynamic_range_compression_torch(x, C=1, clip_val=1e-9)
            for (int m = 0; m < NMels; m++) {
                for (int t = 0; t < nFrames; t++) {
                    mel[m, t] = MathF.Log(MathF.Max(mel[m, t], 1e-9f));
                }
            }

            return mel;
        }

        #region STFT / ISTFT for HN-SEP and tension filter

        /// <summary>
        /// STFT producing complex spectrogram as separate real and imaginary arrays.
        /// Uses n_fft=2048, hop=512, hann window, constant zero-pad mode (matching CascadedNet.audio2spec).
        /// </summary>
        public static (float[,] real, float[,] imag, int nFrames) Stft(float[] audio) {
            int nFft = NFft;
            int hop = HopSize;
            int nFreqs = NFreqs;

            float[] window = CreateHannWindow(nFft);
            int padAmount = nFft / 2;
            float[] padded = new float[padAmount + audio.Length + padAmount];
            Array.Copy(audio, 0, padded, padAmount, audio.Length);

            int nFrames = (padded.Length - nFft) / hop + 1;
            if (nFrames <= 0) return (new float[nFreqs, 0], new float[nFreqs, 0], 0);

            var fft = new RealFft(nFft);
            float[] fftBuf = new float[nFft];
            float[] re = new float[nFreqs];
            float[] im = new float[nFreqs];
            float[,] outRe = new float[nFreqs, nFrames];
            float[,] outIm = new float[nFreqs, nFrames];

            for (int fr = 0; fr < nFrames; fr++) {
                int start = fr * hop;
                Array.Clear(fftBuf, 0, nFft);
                int copyLen = Math.Min(nFft, padded.Length - start);
                for (int i = 0; i < copyLen; i++) fftBuf[i] = padded[start + i] * window[i];
                fft.Direct(fftBuf, re, im);
                for (int k = 0; k < nFreqs; k++) {
                    outRe[k, fr] = re[k];
                    outIm[k, fr] = im[k];
                }
            }
            return (outRe, outIm, nFrames);
        }

        /// <summary>
        /// ISTFT from real/imaginary spectrogram via overlap-add.
        /// </summary>
        public static float[] Istft(float[,] real, float[,] imag, int outputLength = -1) {
            int nFft = NFft;
            int hop = HopSize;
            int nFreqs = real.GetLength(0);
            int nFrames = real.GetLength(1);

            float[] window = CreateHannWindow(nFft);

            int padAmount = nFft / 2;
            int paddedLen = (nFrames - 1) * hop + nFft;
            float[] output = new float[paddedLen];
            float[] windowSum = new float[paddedLen];

            var fft = new RealFft(nFft);
            float[] reBuf = new float[nFreqs];
            float[] imBuf = new float[nFreqs];
            float[] timeBuf = new float[nFft];

            for (int fr = 0; fr < nFrames; fr++) {
                for (int k = 0; k < nFreqs; k++) {
                    reBuf[k] = real[k, fr];
                    imBuf[k] = imag[k, fr];
                }
                fft.Inverse(reBuf, imBuf, timeBuf);
                int start = fr * hop;
                for (int i = 0; i < nFft; i++) {
                    output[start + i] += timeBuf[i] * window[i];
                    windowSum[start + i] += window[i] * window[i];
                }
            }

            for (int i = 0; i < paddedLen; i++) {
                if (windowSum[i] > 1e-8f) output[i] /= windowSum[i];
            }

            int sigLen = outputLength > 0 ? outputLength : paddedLen - 2 * padAmount;
            sigLen = Math.Min(sigLen, paddedLen - padAmount);
            float[] result = new float[sigLen];
            Array.Copy(output, padAmount, result, 0, Math.Min(sigLen, output.Length - padAmount));
            return result;
        }

        #endregion

        #region HN-SEP ONNX packing / unpacking

        /// <summary>
        /// Pack complex STFT into ONNX input format for HN-SEP: [1, 2, maxBin, time].
        /// Mono: 2 channels (real, imag). Matching util/hnsep.py HnsepModel.forward().
        /// </summary>
        public static float[] PackStftForHnsep(float[,] real, float[,] imag, out int useBins) {
            int nFreqs = real.GetLength(0);
            int nFrames = real.GetLength(1);
            useBins = Math.Min(nFreqs, NFft / 2); // max_bin = 1024
            float[] packed = new float[2 * useBins * nFrames];
            for (int k = 0; k < useBins; k++) {
                for (int t = 0; t < nFrames; t++) {
                    packed[k * nFrames + t] = real[k, t];
                    packed[useBins * nFrames + k * nFrames + t] = imag[k, t];
                }
            }
            return packed;
        }

        /// <summary>
        /// Unpack HN-SEP ONNX output mask [1, 2, freq, time], apply bounded_mask, multiply with original spec,
        /// and ISTFT to get separated audio.
        /// </summary>
        public static float[] ApplyHnsepMask(float[] maskData, int useBins, int nFrames,
            float[,] specReal, float[,] specImag, int originalLength) {
            float[,] maskRe = new float[NFreqs, nFrames];
            float[,] maskIm = new float[NFreqs, nFrames];
            for (int k = 0; k < useBins; k++) {
                for (int t = 0; t < nFrames; t++) {
                    float mr = maskData[k * nFrames + t];
                    float mi = maskData[useBins * nFrames + k * nFrames + t];
                    float mag = MathF.Sqrt(mr * mr + mi * mi);
                    float scale = mag > 1e-8f ? MathF.Tanh(mag) / mag : 1f;
                    maskRe[k, t] = mr * scale;
                    maskIm[k, t] = mi * scale;
                }
            }
            // Replicate-pad remaining bins
            for (int k = useBins; k < NFreqs; k++) {
                for (int t = 0; t < nFrames; t++) {
                    maskRe[k, t] = maskRe[useBins - 1, t];
                    maskIm[k, t] = maskIm[useBins - 1, t];
                }
            }

            // Complex multiplication: pred = spec * mask
            float[,] predRe = new float[NFreqs, nFrames];
            float[,] predIm = new float[NFreqs, nFrames];
            for (int k = 0; k < NFreqs; k++) {
                for (int t = 0; t < nFrames; t++) {
                    float a = specReal[k, t], b = specImag[k, t];
                    float c = maskRe[k, t], d = maskIm[k, t];
                    predRe[k, t] = a * c - b * d;
                    predIm[k, t] = a * d + b * c;
                }
            }

            return Istft(predRe, predIm, originalLength);
        }

        #endregion

        #region Tension filter (pre_emphasis_base_tension)

        /// <summary>
        /// Apply frequency-dependent gain in STFT domain, matching hifisampler's pre_emphasis_base_tension.
        /// tension: -100..100 from Ht flag.
        /// </summary>
        public static float[] ApplyTensionFilter(float[] audio, float tension) {
            if (MathF.Abs(tension) < 0.01f) return audio;

            float b = -tension / 50f;
            var (re, im, nFrames) = Stft(audio);
            if (nFrames == 0) return audio;

            float x0 = NFreqs / ((float)SampleRate / 2f / 1500f);
            for (int k = 0; k < NFreqs; k++) {
                float logGain = Math.Clamp((-b / x0) * k + b, -2f, 2f);
                float gain = MathF.Exp(logGain);
                for (int t = 0; t < nFrames; t++) {
                    re[k, t] *= gain;
                    im[k, t] *= gain;
                }
            }

            float[] filtered = Istft(re, im, audio.Length);

            float origMax = 0, filtMax = 0;
            for (int i = 0; i < audio.Length; i++) origMax = MathF.Max(origMax, MathF.Abs(audio[i]));
            for (int i = 0; i < filtered.Length; i++) filtMax = MathF.Max(filtMax, MathF.Abs(filtered[i]));

            if (filtMax > 1e-8f) {
                float boostFactor = Math.Clamp(b / -15f, 0f, 0.33f) + 1f;
                float normalizeScale = origMax / filtMax * boostFactor;
                for (int i = 0; i < filtered.Length; i++) filtered[i] *= normalizeScale;
            }

            return filtered;
        }

        #endregion

        #region Growl effect

        /// <summary>
        /// Growl effect matching hifisampler's util/growl.py.
        /// </summary>
        public static float[] ApplyGrowl(float[] audio, float strength, float frequency = 80f, float freqLow = 400f) {
            if (strength <= 0 || frequency <= 0) return audio;

            int len = audio.Length;
            float nyq = SampleRate / 2f;
            float normCut = Math.Clamp(freqLow / nyq, 0.01f, 0.99f);

            float[] highBand = new float[len];
            float[] lowBand = new float[len];
            ButterworthHighPass4(audio, highBand, normCut);
            for (int i = 0; i < len; i++) lowBand[i] = audio[i] - highBand[i];

            // Square-wave LFO
            float[] lfo = new float[len];
            for (int i = 0; i < len; i++) {
                float phase = 2f * MathF.PI * frequency * i / SampleRate;
                lfo[i] = MathF.Sin(phase) >= 0 ? 1f : -1f;
            }

            // Pitch modulation via resampling
            float maxCents = 100f;
            double[] cumRatio = new double[len];
            double ratioSum = 0;
            for (int i = 0; i < len; i++) {
                double ratio = Math.Pow(2, lfo[i] * strength * maxCents / 1200.0);
                ratioSum += ratio;
                cumRatio[i] = ratioSum;
            }
            double avgRatio = ratioSum / len;
            double[] drift = new double[len];
            for (int i = 0; i < len; i++) {
                drift[i] = cumRatio[i] - cumRatio[0] - (double)i * avgRatio;
            }
            if (len > 100) HighPassDouble(drift, 20.0 / (SampleRate / 2.0));

            float[] modulated = new float[len];
            float rmsOrig = 0, rmsNew = 0;
            for (int i = 0; i < len; i++) {
                double idx = Math.Clamp(i + drift[i], 0, len - 1);
                int lo = (int)idx;
                int hi = Math.Min(lo + 1, len - 1);
                float alpha = (float)(idx - lo);
                modulated[i] = highBand[lo] * (1 - alpha) + highBand[hi] * alpha;
                rmsOrig += highBand[i] * highBand[i];
                rmsNew += modulated[i] * modulated[i];
            }
            rmsOrig = MathF.Sqrt(rmsOrig / len);
            rmsNew = MathF.Sqrt(rmsNew / len);
            if (rmsNew > 1e-10f) {
                float rmsScale = rmsOrig / rmsNew;
                for (int i = 0; i < len; i++) modulated[i] *= rmsScale;
            }

            float[] result = new float[len];
            for (int i = 0; i < len; i++) result[i] = lowBand[i] + modulated[i];
            return result;
        }

        /// <summary>4th order Butterworth high-pass (2 cascaded biquads).</summary>
        static void ButterworthHighPass4(float[] input, float[] output, float normFreq) {
            float[] buf = (float[])input.Clone();
            float[] qValues = new float[] { 0.7654f, 1.8478f }; // Butterworth 4th-order Q factors
            foreach (float q in qValues) {
                float wc = MathF.Tan(MathF.PI * normFreq);
                float k = wc * wc;
                float norm = 1f / (1f + wc / q + k);
                float a0 = norm, a1 = -2f * norm, a2 = norm;
                float b1 = 2f * (k - 1f) * norm;
                float b2 = (1f - wc / q + k) * norm;

                float x1 = 0, x2 = 0, y1 = 0, y2 = 0;
                float[] next = new float[input.Length];
                for (int i = 0; i < input.Length; i++) {
                    float x = buf[i];
                    float y = a0 * x + a1 * x1 + a2 * x2 - b1 * y1 - b2 * y2;
                    next[i] = y;
                    x2 = x1; x1 = x;
                    y2 = y1; y1 = y;
                }
                buf = next;
            }
            Array.Copy(buf, output, input.Length);
        }

        static void HighPassDouble(double[] data, double normFreq) {
            double wc = Math.Tan(Math.PI * normFreq);
            double k = wc * wc;
            double q = 0.7071;
            double norm = 1.0 / (1.0 + wc / q + k);
            double a0 = norm, a1 = -2.0 * norm, a2 = norm;
            double b1 = 2.0 * (k - 1.0) * norm;
            double b2 = (1.0 - wc / q + k) * norm;
            double x1 = 0, x2 = 0, y1 = 0, y2 = 0;
            for (int i = 0; i < data.Length; i++) {
                double x = data[i];
                double y = a0 * x + a1 * x1 + a2 * x2 - b1 * y1 - b2 * y2;
                data[i] = y;
                x2 = x1; x1 = x;
                y2 = y1; y1 = y;
            }
        }

        #endregion

        #region Mel interpolation / F0 / utility

        /// <summary>Linear interpolation of mel spectrogram for time-stretching.</summary>
        public static float[,] InterpolateMel(float[,] mel, double[] tIn, double[] tOut) {
            int nMels = mel.GetLength(0);
            int nIn = mel.GetLength(1);
            int nOut = tOut.Length;
            float[,] result = new float[nMels, nOut];

            for (int m = 0; m < nMels; m++) {
                for (int i = 0; i < nOut; i++) {
                    double t = tOut[i];
                    if (t <= tIn[0]) { result[m, i] = mel[m, 0]; continue; }
                    if (t >= tIn[nIn - 1]) { result[m, i] = mel[m, nIn - 1]; continue; }
                    int lo = 0, hi = nIn - 1;
                    while (hi - lo > 1) { int mid = (lo + hi) >> 1; if (tIn[mid] <= t) lo = mid; else hi = mid; }
                    double alpha = (t - tIn[lo]) / (tIn[hi] - tIn[lo]);
                    result[m, i] = (float)((1 - alpha) * mel[m, lo] + alpha * mel[m, hi]);
                }
            }
            return result;
        }

        /// <summary>MIDI note → Hz: 440 * 2^((midi - 69) / 12).</summary>
        public static float MidiToHz(float midi) => 440f * MathF.Pow(2f, (midi - 69f) / 12f);

        /// <summary>Simple RMS-based loudness normalization (fast approximation of pyloudnorm).</summary>
        public static void LoudnessNormalize(float[] audio, float targetDb = -16f, float strengthPercent = 100f) {
            if (audio.Length == 0) return;
            double rmsSum = 0;
            for (int i = 0; i < audio.Length; i++) rmsSum += audio[i] * (double)audio[i];
            double rmsDb = 10.0 * Math.Log10(rmsSum / audio.Length + 1e-10);
            double diff = targetDb - rmsDb;
            double gain = Math.Pow(10.0, (diff * strengthPercent / 100.0) / 20.0);
            for (int i = 0; i < audio.Length; i++) audio[i] *= (float)gain;
        }

        /// <summary>Apply amplitude modulation based on pitch derivative (A flag).</summary>
        public static void ApplyAmplitudeModulation(float[] render, float[] pitchMidi, double[] tFrames,
            double newStart, double newEnd, int aFlag) {
            if (aFlag == 0 || pitchMidi.Length < 2 || tFrames.Length < 2) return;

            int numSamples = render.Length;
            float aClamped = Math.Clamp(aFlag, -100, 100);

            // Compute pitch derivative at each mel frame
            float[] pitchDeriv = new float[pitchMidi.Length];
            for (int i = 1; i < pitchMidi.Length - 1; i++) {
                double dt = tFrames[Math.Min(i + 1, tFrames.Length - 1)] - tFrames[Math.Max(i - 1, 0)];
                if (dt > 1e-12) pitchDeriv[i] = (pitchMidi[Math.Min(i + 1, pitchMidi.Length - 1)] - pitchMidi[Math.Max(i - 1, 0)]) / (float)dt;
            }
            pitchDeriv[0] = pitchDeriv.Length > 1 ? pitchDeriv[1] : 0;
            pitchDeriv[^1] = pitchDeriv.Length > 1 ? pitchDeriv[^2] : 0;

            // Compute gain at mel frames: 5^(1e-4 * A * derivative)
            float[] gainAtFrames = new float[pitchMidi.Length];
            for (int i = 0; i < pitchMidi.Length; i++) {
                gainAtFrames[i] = MathF.Pow(5f, 1e-4f * aClamped * pitchDeriv[i]);
            }

            // Interpolate gain to sample level
            for (int i = 0; i < numSamples; i++) {
                double sampleTime = newStart + (newEnd - newStart) * i / numSamples;
                // Find gain by linear interpolation in tFrames
                float gain = gainAtFrames[0];
                if (tFrames.Length > 1) {
                    if (sampleTime <= tFrames[0]) {
                        gain = gainAtFrames[0];
                    } else if (sampleTime >= tFrames[^1]) {
                        gain = gainAtFrames[^1];
                    } else {
                        int lo = 0, hi = tFrames.Length - 1;
                        while (hi - lo > 1) { int mid = (lo + hi) >> 1; if (tFrames[mid] <= sampleTime) lo = mid; else hi = mid; }
                        float alpha = (float)((sampleTime - tFrames[lo]) / (tFrames[hi] - tFrames[lo]));
                        gain = gainAtFrames[lo] * (1 - alpha) + gainAtFrames[hi] * alpha;
                    }
                }
                render[i] *= gain;
            }
        }

        #endregion

        #region Pad / window helpers

        public static float[] ReflectPad(float[] audio, int padLeft, int padRight) {
            int len = audio.Length;
            float[] padded = new float[padLeft + len + padRight];
            for (int i = 0; i < padLeft; i++) {
                int srcIdx = Math.Min(padLeft - i, len - 1);
                padded[i] = audio[srcIdx];
            }
            Array.Copy(audio, 0, padded, padLeft, len);
            for (int i = 0; i < padRight; i++) {
                int srcIdx = Math.Max(len - 2 - i, 0);
                padded[padLeft + len + i] = audio[srcIdx];
            }
            return padded;
        }

        public static float[] CreateHannWindow(int size) {
            float[] w = new float[size];
            for (int i = 0; i < size; i++) w[i] = 0.5f * (1f - MathF.Cos(2f * MathF.PI * i / size));
            return w;
        }

        #endregion
    }
}
