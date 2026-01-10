#include "worldline.h"
#include <cstdlib>
#include <cstring>

using namespace std;

struct PhraseSynthImpl {
};

extern "C" {

DLL_API int F0(float* samples, int length, int fs, double frame_period,
               int method, double** f0) {
  if (f0) *f0 = nullptr;
  return 0;
}

DLL_API int DecodeMgc(int f0_length, double* mgc, int mgc_size, int fft_size,
                      int fs, double** spectrogram) {
  if (spectrogram) *spectrogram = nullptr;
  return 0;
}

DLL_API int DecodeBap(int f0_length, double* bap, int fft_size, int fs,
                      double** aperiodicity) {
  if (aperiodicity) *aperiodicity = nullptr;
  return 0;
}

DLL_API void InitAnalysisConfig(AnalysisConfig* config, int fs, int hop_size,
                                int fft_size) {
  if (!config) return;
  config->fs = fs;
  config->hop_size = hop_size;
  config->fft_size = fft_size;
  config->f0_floor = 50.0f;
  config->frame_ms = 5.0;
}

DLL_API void WorldAnalysis(const AnalysisConfig* config, float* samples,
                           int num_samples, double** f0_out,
                           double** sp_env_out, double** ap_out,
                           int* num_frames) {
  if (f0_out) *f0_out = nullptr;
  if (sp_env_out) *sp_env_out = nullptr;
  if (ap_out) *ap_out = nullptr;
  if (num_frames) *num_frames = 0;
}

DLL_API void WorldAnalysisF0In(const AnalysisConfig* config, float* samples,
                               int num_samples, double* f0_in, int num_frames,
                               double* sp_env_out, double* ap_out) {
  if (sp_env_out) *sp_env_out = 0;
  if (ap_out) *ap_out = 0;
}

DLL_API int WorldSynthesis(double* const f0, int f0_length,
                           double* const mgc_or_sp, bool is_mgc, int mgc_size,
                           double* const bap_or_ap, bool is_bap, int fft_size,
                           double frame_period, int fs, double** y,
                           double* const gender, double* const tension,
                           double* const breathiness, double* const voicing) {
  if (y) *y = nullptr;
  return 0;
}

DLL_API int Resample(const SynthRequest* request, float** y) {
  if (y) *y = nullptr;
  return 0;
}

DLL_API PhraseSynth* PhraseSynthNew() {
  return reinterpret_cast<PhraseSynth*>(new PhraseSynthImpl());
}

DLL_API void PhraseSynthDelete(PhraseSynth* phrase_synth) {
  delete reinterpret_cast<PhraseSynthImpl*>(phrase_synth);
}

DLL_API void PhraseSynthAddRequest(PhraseSynth* phrase_synth,
                                   const SynthRequest* request, double pos_ms,
                                   double skip_ms, double length_ms,
                                   double fade_in_ms, double fade_out_ms,
                                   worldline::LogCallback logCallback) {
  (void)phrase_synth; (void)request; (void)pos_ms; (void)skip_ms;
  (void)length_ms; (void)fade_in_ms; (void)fade_out_ms; (void)logCallback;
}

DLL_API void PhraseSynthSetCurves(PhraseSynth* phrase_synth, double* f0,
                                  double* gender, double* tension,
                                  double* breathiness, double* voicing,
                                  int length,
                                  worldline::LogCallback logCallback) {
  (void)phrase_synth; (void)f0; (void)gender; (void)tension; (void)breathiness;
  (void)voicing; (void)length; (void)logCallback;
}

DLL_API int PhraseSynthSynth(PhraseSynth* phrase_synth, float** y,
                             worldline::LogCallback logCallback) {
  if (y) *y = nullptr;
  (void)phrase_synth; (void)logCallback;
  return 0;
}

} // extern "C"
