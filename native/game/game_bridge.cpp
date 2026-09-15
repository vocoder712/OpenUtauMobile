// 基于 Kakaru 的进程内 GAME C ABI 方案；结果由模型句柄持有，避免定长数组截断。
#include "game_ggml/model.h"
#include "game_ggml/config.h"
#include <algorithm>
#include <cstdint>
#include <cstdio>
#include <limits>
#include <memory>
#include <stdexcept>
#include <string>
#include <vector>

#ifdef _WIN32
#define OPUM_EXPORT extern "C" __declspec(dllexport)
#else
#define OPUM_EXPORT extern "C" __attribute__((visibility("default")))
#endif

struct OpumNote {
    float offset;
    float duration;
    float pitch;
    int32_t voiced;
};
static_assert(sizeof(OpumNote) == 16);

struct OpumGame {
    std::unique_ptr<game_ggml::Model> model;
    std::vector<OpumNote> notes;
    char error[1024]{};
};

// 错误路径不分配内存，C++ 异常不会穿越 P/Invoke 边界。
static void write_error(char* buffer, int capacity, const char* message) noexcept {
    if (buffer && capacity > 0) std::snprintf(buffer, static_cast<size_t>(capacity), "%s", message);
}

OPUM_EXPORT OpumGame* opum_game_open(const char* path, char* error, int capacity) noexcept {
    try {
        if (!path || !*path) throw std::invalid_argument("Missing GGUF path");
        auto context = std::make_unique<OpumGame>();
        context->model = std::make_unique<game_ggml::Model>(game_ggml::Model::load(std::string(path)));
        if (context->model->config().inference.audio_sample_rate != 44100)
            throw std::invalid_argument("GAME model must use 44100 Hz audio");
        return context.release();
    } catch (const std::exception& ex) {
        write_error(error, capacity, ex.what());
    } catch (...) {
        write_error(error, capacity, "Unknown GAME model loading error");
    }
    return nullptr;
}

OPUM_EXPORT void opum_game_close(OpumGame* context) noexcept { delete context; }

// 输出指针有效期到下次 infer 或 close；同一句柄的所有调用须串行执行。
OPUM_EXPORT int opum_game_infer(OpumGame* context, const float* samples, size_t count,
    const char* language, int steps, float boundary, int radius, float score, uint64_t seed,
    const OpumNote** notes, int* note_count) noexcept {
    if (notes) *notes = nullptr;
    if (note_count) *note_count = 0;
    if (!context) return -1;
    context->error[0] = '\0';
    try {
        if (!samples || !count || !notes || !note_count || steps <= 0 || radius < 0)
            throw std::invalid_argument("Invalid GAME inference arguments");
        game_ggml::InferParams parameters;
        parameters.language = 0;
        if (language && *language) {
            const auto& map = context->model->config().inference.lang_map;
            auto entry = map.find(language);
            if (entry == map.end()) throw std::invalid_argument("Unsupported GAME language");
            parameters.language = entry->second;
        }
        parameters.d3pm_nsteps = steps;
        parameters.boundary_threshold = boundary;
        parameters.boundary_radius = radius;
        parameters.note_threshold = score;
        parameters.seed = seed;
        auto result = context->model->infer(samples, count, parameters);
        if (result.notes.size() > static_cast<size_t>(std::numeric_limits<int>::max()))
            throw std::length_error("Too many GAME notes");
        context->notes.clear();
        context->notes.reserve(result.notes.size());
        for (const auto& note : result.notes) {
            context->notes.push_back({note.offset_seconds, note.duration_seconds, note.pitch_midi, note.voiced ? 1 : 0});
        }
        *notes = context->notes.data();
        *note_count = static_cast<int>(context->notes.size());
        return 0;
    } catch (const std::exception& ex) {
        write_error(context->error, sizeof(context->error), ex.what());
    } catch (...) {
        write_error(context->error, sizeof(context->error), "Unknown GAME inference error");
    }
    return -1;
}

OPUM_EXPORT const char* opum_game_error(const OpumGame* context) noexcept {
    return context ? context->error : "Invalid GAME handle";
}
