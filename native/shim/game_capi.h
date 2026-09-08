#pragma once
// ---------------------------------------------------------------------------
// game_capi — C ABI 桥接层：把 game.cpp (game_ggml::Model) 暴露给 .NET P/Invoke。
//
// 设计原则：
//  - 纯 C 头，可被 C# 以 [DllImport] / LibraryImport 直接引用（不含 C++ 类型）。
//  - 不透明句柄 game_capi_model*，进程内单/多实例都由本层管理。
//  - 所有函数返回 int（0 = 成功）；失败时把可读错误写入调用方提供的缓冲区。
//  - 语音波形为 float32 单声道、采样率 44100Hz、取值 [-1,1]（与上游一致）。
//  - Model::infer 非线程安全 -> 每个 game_capi_model 由调用方保证串行。
// ---------------------------------------------------------------------------

#include <stddef.h>
#include <stdint.h>

#ifdef __cplusplus
extern "C" {
#endif

// 一条转写音符（对应 game_ggml::Note 的 POD 投影）。
typedef struct game_capi_note {
    float offset_seconds;   // 起始时间（秒）
    float duration_seconds; // 持续时间（秒）
    float pitch_midi;       // 小数 MIDI 音高（仅 voiced 有效）
    int   voiced;           // 1=有声部 0=休止/无音高
} game_capi_note;

// 不透明模型句柄。
typedef struct game_capi_model game_capi_model;

// 返回字符串缓冲的推荐容量（含 NUL），调用方按此分配。
enum { GAME_CAPI_ERRBUF = 512 };

// ---- 版本 / 后端能力（编译期信息，无状态）-------------------------------
// 写产物版本号到 buf（如 "0.1.0"）。返回 buf 需要的长度。
int game_capi_version(char * buf, int cap);
// 写 ggml 版本（如 "v0.19.0"）。返回长度。
int game_capi_ggml_version(char * buf, int cap);

// 把编译期可用的后端名（小写, 逗号分隔, 如 "vulkan,cpu"）写入 buf。返回长度。
// 该列表来自 GAME_GGML_HAS_* 宏, 反映"此库编译进了哪些加速器"。
int game_capi_available_backends(char * buf, int cap);

// ---- 模型生命周期 ---------------------------------------------------------
// 打开 GGUF 权重并构建 backend（内部走 game_ggml::Model::load,
// 自动 GPU->CPU fallback）。返回非空句柄, 失败返回 NULL 并把错误写进 errbuf。
game_capi_model * game_capi_open(const char * gguf_path,
                                 const char * config_json,   // 可为 NULL; 供未来透传
                                 char * errbuf, int errcap);

// 关闭并释放。NULL 安全。不可与同句柄的 infer 并发。
void game_capi_close(game_capi_model * m);

// 返回该实例运行时实际选中的后端名（写 buf）。用于诊断/UI 展示。
// 注意：public API 未暴露 Model 实际选中的后端，因此无法可靠获知时返回 "unknown"，
// 而不是用 available_backends()[0] 猜测（GPU→CPU fallback 后可能与实际不符）。
int game_capi_backend_decided(game_capi_model * m, char * buf, int cap);

// ---- 推理（串行）----------------------------------------------------------
// 对 44100Hz 单声道 float 波形做端到端转写, 结果追加到 notes_out 数组。
//
// 参数:
//   m                 模型句柄（来自 game_capi_open）
//   waveform          波形指针; n 个样本
//   n                 样本数（> 0）
//   language         语言 id（0=universal; 用 game_capi_lang_map 查）
//   nsteps           D3PM 去噪步数（1=最快; 8=更高质量; 默认建议 8 或 1）
//   seg_threshold    边界解码阈值
//   seg_radius       边界解码半径（帧）
//   est_threshold    音符存在性门槛
//   seed             随机种子（0=自动/OS 随机）
//   notes_out        由调用方分配的 game_capi_note 数组
//   notes_capacity   notes_out 容量
//   notes_count      回填实际音符数（不会超过 capacity）
//   num_frames       回填 mel 帧数（诊断用, 可 NULL）
//
// 返回 0 = 成功(可能 0 个音符); 负值 = 错误码。错误码常量见下。
int game_capi_infer(game_capi_model * m,
                    const float * waveform, size_t n,
                    int language, int nsteps,
                    float seg_threshold, int seg_radius,
                    float est_threshold, uint64_t seed,
                    game_capi_note * notes_out, int notes_capacity,
                    int * notes_count, int * num_frames);

// 语言 id 查询: 把语言码（"zh","en",...）映射为数字 id。
// 返回 id; 未知名返回 -1（调用方可按 0/universal 处理）。
int game_capi_language_id(game_capi_model * m, const char * lang_code);

// ---- 方便的错误信息 ------------------------------------------------------
// 最近一次失败的详细消息（线程局部, 单实例够用）。可为 NULL。
const char * game_capi_last_error(game_capi_model * m);

// 错误码约定
#define GAME_CAPI_OK                0
#define GAME_CAPI_ERR_HANDLE       -1  // 空句柄
#define GAME_CAPI_ERR_INIT         -2  // backend/模型初始化失败
#define GAME_CAPI_ERR_INFER        -3  // 推理抛异常
#define GAME_CAPI_ERR_INVALID_ARG  -4  // 参数非法

#ifdef __cplusplus
}
#endif

