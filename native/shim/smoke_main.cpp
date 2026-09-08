// ---------------------------------------------------------------------------
// smoke_main.cpp — game_capi 原生冒烟验证（开发用, 不进 app 包）
//
// 用法: game_capi_check <model.gguf> <wav.wav> [nsteps]
//   - 读取 wav（48k/44.1k 单声道皆自动降采样到 44.1k… 简化：本工具只接受
//     PCM16 单声道 WAV，44.1kHz；由调用方准备好）。
//   - 调 game_capi_open/infer 打印结果。
//
// 真实 .NET 层走 MidiExtractor 的重采样/切片；此工具仅为快速验证 C ABI 正确性，
// 简化处理即可。
// ---------------------------------------------------------------------------

#include "game_capi.h"

#include <cstdint>
#include <cstdio>
#include <cstdlib>
#include <cstring>
#include <vector>

namespace {

// 读取 PCM16 单声道 44.1kHz WAV 的裸样本（仅 data chunk, 无解码库）。
// 简化：确认 fmt 是 PCM, channels=1, sample rate=44100。
bool load_wav_pcm16(const char * path, std::vector<float> & out, int & sr) {
    FILE * f = std::fopen(path, "rb");
    if (!f) { std::fprintf(stderr, "cannot open %s\n", path); return false; }
    // RIFF 头
    char riff[4]; std::fread(riff, 1, 4, f);
    std::uint32_t filelen; std::fread(&filelen, 4, 1, f);
    char wave[4]; std::fread(wave, 1, 4, f);
    if (std::memcmp(riff, "RIFF", 4) || std::memcmp(wave, "WAVE", 4)) {
        std::fprintf(stderr, "not a RIFF/WAVE file\n"); std::fclose(f); return false;
    }
    int format = 0, channels = 0, sampleRate = 0, bits = 0;
    bool found_fmt = false, found_data = false;
    while (!feof(f)) {
        char ck[4]; std::uint32_t sz = 0;
        if (std::fread(ck, 1, 4, f) != 4) break;
        if (std::fread(&sz, 4, 1, f) != 1) break;
        if (std::memcmp(ck, "fmt ", 4) == 0) {
            std::uint16_t fmt=0, ch=0, bits16=0;
            std::uint32_t srate=0;
            std::fread(&fmt, 2, 1, f); std::fread(&ch, 2, 1, f);
            std::fread(&srate, 4, 1, f); std::fseek(f, 6, SEEK_CUR);
            std::fread(&bits16, 2, 1, f);
            format=fmt; channels=ch; sampleRate=(int)srate; bits=bits16;
            found_fmt = true;
            // 跳到 chunk 末尾
            std::fseek(f, (long)(sz - 16), SEEK_CUR);
        } else if (std::memcmp(ck, "data", 4) == 0) {
            out.clear(); out.reserve(sz / 2);
            long remaining = (long)sz;
            while (remaining >= 2) {
                std::int16_t s;
                std::fread(&s, 2, 1, f);
                out.push_back((float)s / 32768.0f);
                remaining -= 2;
            }
            found_data = true;
            break;
        } else {
            std::fseek(f, (long)sz + (sz & 1), SEEK_CUR);
        }
    }
    std::fclose(f);
    if (!found_fmt || !found_data) { std::fprintf(stderr, "missing fmt/data\n"); return false; }
    if (format != 1) { std::fprintf(stderr, "not PCM\n"); return false; }
    if (bits != 16) { std::fprintf(stderr, "not PCM16\n"); return false; }
    if (channels != 1) { std::fprintf(stderr, "not mono\n"); return false; }
    if (sampleRate != 44100) { std::fprintf(stderr, "not 44100Hz (got %d)\n", sampleRate); return false; }
    sr = sampleRate;
    return true;
}

} // namespace

int main(int argc, char ** argv) {
    if (argc < 3) {
        std::fprintf(stderr, "usage: %s <model.gguf> <input.wav> [nsteps] [seed]\n", argv[0]);
        return 2;
    }
    const char * model_path = argv[1];
    const char * wav_path    = argv[2];
    int nsteps  = argc > 3 ? std::atoi(argv[3]) : 1;
    std::uint64_t seed = argc > 4 ? std::strtoull(argv[4], nullptr, 10) : 42;

    std::vector<float> wav; int sr = 0;
    if (!load_wav_pcm16(wav_path, wav, sr)) return 2;

    char version[64] = {0};
    (void)game_capi_version(version, sizeof(version));
    char backends[128] = {0};
    (void)game_capi_available_backends(backends, sizeof(backends));
    std::printf("version=%s backends=[%s]\n", version, backends);
    std::printf("loading model %s ...\n", model_path);
    std::fflush(stdout);

    char err[GAME_CAPI_ERRBUF] = {0};
    game_capi_model * m = game_capi_open(model_path, nullptr, err, (int)sizeof(err));
    if (!m) { std::fprintf(stderr, "open failed: %s\n", err); return 1; }

    char decided[64] = {0};
    (void)game_capi_backend_decided(m, decided, (int)sizeof(decided));
    std::printf("decided backend = %s\n", decided);

    std::vector<game_capi_note> notes(4096);
    int count = 0, frames = 0;
    int rc = game_capi_infer(m, wav.data(), (std::size_t)wav.size(),
                             0 /*universal*/, nsteps,
                             0.2f, 2, 0.2f, seed,
                             notes.data(), (int)notes.size(),
                             &count, &frames);
    if (rc != GAME_CAPI_OK) {
        const char * le = game_capi_last_error(m);
        std::fprintf(stderr, "infer failed rc=%d err=%s\n", rc, le ? le : "(none)");
        game_capi_close(m);
        return 1;
    }

    std::printf("frames=%d notes=%d\n", frames, count);
    float total_s = 0.0f;
    for (int i = 0; i < count; ++i) {
        const game_capi_note & n = notes[(std::size_t)i];
        std::printf("  [%02d] %.3fs + %.3fs  pitch=%6.2f  voiced=%d\n",
                    i, n.offset_seconds, n.duration_seconds, n.pitch_midi, n.voiced);
        total_s += n.duration_seconds;
    }
    std::printf("total duration %.3f s\n", total_s);
    game_capi_close(m);
    return 0;
}
