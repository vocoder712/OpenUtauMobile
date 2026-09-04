// ---------------------------------------------------------------------------
// game_capi.cpp — C ABI 桥接实现
//
// 目标：把 game_ggml::Model 的 C++ 面收敛成一组稳定的 extern "C" 函数，
// 供 .NET(Avalonia UI 层) 通过 P/Invoke 调用，避免把 C++ 类型/ggml 透传给托管层。
//
// 约束遵循 game.cpp 上游：
//   * Model 非线程安全 -> 句柄级串行由调用方保证。
//   * 44100Hz 单声道 float, 取值 [-1,1]。
//   * 默认 D3PM nsteps=1（最快）；8 更高质量。
//   * 所有 C++ 异常在边界转成 错误码 + last-error 消息。
// ---------------------------------------------------------------------------

#include "game_capi.h"

#include "game_ggml/config.h"
#include "game_ggml/errors.h"
#include "game_ggml/game_ggml.h"
#include "game_ggml/model.h"
#include "game_ggml/version.h"

#include <cstring>
#include <memory>
#include <string>
#include <vector>

namespace {

using game_ggml::Model;

struct ModelContext {
    std::unique_ptr<Model> model;
    std::string            config_json;     // 透传/备用（当前不解析, 仅为未来扩展留位）
    std::string            last_error;      // 最近一次 C++ 异常的析出文本
};

// 捕获当前挂起的异常 -> 文本。须在 catch 块内调用。
std::string exception_text(const char * prefix) {
    try { throw; } catch (const std::exception & e) {
        return std::string(prefix) + e.what();
    } catch (...) {
        return std::string(prefix) + "unknown C++ exception";
    }
}

// 写入 C 头约定的 errbuf（截断 + NUL 结尾）
void write_errbuf(char * errbuf, int errcap, const std::string & text) {
    if (!errbuf || errcap <= 0) return;
    std::size_t n = text.size();
    std::size_t bulk = static_cast<std::size_t>(errcap - 1);
    if (n > bulk) n = bulk;
    if (n > 0) std::memcpy(errbuf, text.data(), n);
    errbuf[n] = '\0';
}

int copy_to_buf(char * buf, int cap, const std::string & s) {
    if (!buf || cap <= 0) return 0;
    std::strncpy(buf, s.c_str(), static_cast<std::size_t>(cap - 1));
    buf[cap - 1] = '\0';
    return static_cast<int>(s.size() + 1);
}

} // namespace

extern "C" int game_capi_version(char * buf, int cap) {
    return copy_to_buf(buf, cap, game_ggml::version_string());
}

extern "C" int game_capi_ggml_version(char * buf, int cap) {
    return copy_to_buf(buf, cap, game_ggml::ggml_version_string());
}

extern "C" int game_capi_available_backends(char * buf, int cap) {
    std::string joined;
    const char * const * names = game_ggml::available_backends();
    const int count = game_ggml::available_backends_count();
    for (int i = 0; i < count; ++i) {
        if (i) joined += ',';
        joined += names[i];
    }
    return copy_to_buf(buf, cap, joined);
}

extern "C" game_capi_model * game_capi_open(
    const char * gguf_path, const char * config_json,
    char * errbuf, int errcap) {
    if (!gguf_path || !*gguf_path) {
        write_errbuf(errbuf, errcap, "gguf_path is empty");
        return nullptr;
    }

    auto * ctx = new (std::nothrow) ModelContext();
    if (!ctx) {
        write_errbuf(errbuf, errcap, "out of memory (ModelContext)");
        return nullptr;
    }
    if (config_json) ctx->config_json = config_json;

    try {
        ctx->model = std::make_unique<Model>(Model::load(std::string(gguf_path)));
    } catch (const game_ggml::BackendError & e) {
        ctx->last_error = exception_text("backend: ");
        write_errbuf(errbuf, errcap, ctx->last_error);
        delete ctx;
        return nullptr;
    } catch (const game_ggml::GgufError & e) {
        ctx->last_error = exception_text("gguf: ");
        write_errbuf(errbuf, errcap, ctx->last_error);
        delete ctx;
        return nullptr;
    } catch (const game_ggml::NotImplemented & e) {
        ctx->last_error = exception_text("not-implemented: ");
        write_errbuf(errbuf, errcap, ctx->last_error);
        delete ctx;
        return nullptr;
    } catch (const std::exception & e) {
        ctx->last_error = exception_text("load: ");
        write_errbuf(errbuf, errcap, ctx->last_error);
        delete ctx;
        return nullptr;
    } catch (...) {
        ctx->last_error = "load: unknown error";
        write_errbuf(errbuf, errcap, ctx->last_error);
        delete ctx;
        return nullptr;
    }

    return reinterpret_cast<game_capi_model *>(ctx);
}

extern "C" void game_capi_close(game_capi_model * m) {
    if (!m) return;
    delete reinterpret_cast<ModelContext *>(m);
}

extern "C" int game_capi_backend_decided(game_capi_model * m, char * buf, int cap) {
    if (!m || !buf || cap <= 0) return 0;
    ModelContext * ctx = reinterpret_cast<ModelContext *>(m);
    if (!ctx->model) return copy_to_buf(buf, cap, "?");
    // 实际选中后端在 Model::load 内部由 init_best_backend 决定（GPU→CPU fallback 后
    // 可能并非 available_backends()[0]），且 public API 未暴露该值的访问器；
    // 不触碰 internals()/Unstable API，因此无法可靠获知 -> 显式返回 unknown，避免误报。
    return copy_to_buf(buf, cap, "unknown");
}

extern "C" int game_capi_language_id(game_capi_model * m, const char * lang_code) {
    if (!m || !lang_code || !*lang_code) return -1;
    ModelContext * ctx = reinterpret_cast<ModelContext *>(m);
    if (!ctx->model) return -1;
    const auto & lang_map = ctx->model->config().inference.lang_map;
    auto it = lang_map.find(std::string(lang_code));
    if (it == lang_map.end()) return -1;
    return it->second;
}

extern "C" int game_capi_infer(
    game_capi_model * m,
    const float * waveform, std::size_t n,
    int language, int nsteps,
    float seg_threshold, int seg_radius,
    float est_threshold, std::uint64_t seed,
    game_capi_note * notes_out, int notes_capacity,
    int * notes_count, int * num_frames) {
    if (!m) return GAME_CAPI_ERR_HANDLE;
    ModelContext * ctx = reinterpret_cast<ModelContext *>(m);
    if (!ctx->model) return GAME_CAPI_ERR_HANDLE;
    if (notes_count) *notes_count = 0;
    if (num_frames) *num_frames = 0;
    if (!waveform || n == 0) return GAME_CAPI_ERR_INVALID_ARG;

    game_ggml::InferParams params;
    params.language = language;
    params.d3pm_nsteps = nsteps > 0 ? nsteps : 1;
    params.boundary_threshold = seg_threshold;
    params.boundary_radius = seg_radius;
    params.note_threshold = est_threshold;
    params.seed = seed;

    game_ggml::InferResult result;
    try {
        result = ctx->model->infer(waveform, n, params);
    } catch (const game_ggml::InvalidArgument & e) {
        ctx->last_error = exception_text("arg: ");
        return GAME_CAPI_ERR_INVALID_ARG;
    } catch (const std::exception & e) {
        ctx->last_error = exception_text("infer: ");
        return GAME_CAPI_ERR_INFER;
    } catch (...) {
        ctx->last_error = "infer: unknown error";
        return GAME_CAPI_ERR_INFER;
    }

    if (num_frames) *num_frames = result.num_frames;

    int total = static_cast<int>(result.notes.size());
    int copy = (total > notes_capacity) ? notes_capacity : total;
    if (notes_out && copy > 0) {
        for (int i = 0; i < copy; ++i) {
            const game_ggml::Note & src = result.notes[static_cast<std::size_t>(i)];
            notes_out[i].offset_seconds   = src.offset_seconds;
            notes_out[i].duration_seconds = src.duration_seconds;
            notes_out[i].pitch_midi       = src.pitch_midi;
            notes_out[i].voiced           = src.voiced ? 1 : 0;
        }
    }
    if (notes_count) *notes_count = copy;
    return GAME_CAPI_OK;
}

extern "C" const char * game_capi_last_error(game_capi_model * m) {
    if (!m) return nullptr;
    const ModelContext * ctx = reinterpret_cast<const ModelContext *>(m);
    return ctx->last_error.empty() ? nullptr : ctx->last_error.c_str();
}
