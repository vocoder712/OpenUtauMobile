load("@rules_cc//cc:cc_library.bzl", "cc_library")
load("@rules_cc//cc:cc_test.bzl", "cc_test")
package(default_visibility = ["//visibility:public"])

cc_library(
    name = "game",
    srcs = [
        "src/backend.cpp", "src/gguf_io.cpp", "src/tensor_utils.cpp",
        "src/ops_basic.cpp", "src/ops_ffn.cpp", "src/ops_rope.cpp",
        "src/ops_attn.cpp", "src/ops_joint_attn.cpp", "src/model_encoder.cpp",
        "src/model_segmenter.cpp", "src/model_estimator.cpp", "src/model.cpp",
        "src/mel.cpp", "src/decode.cpp", "src/rng.cpp", "src/d3pm.cpp",
    ] + glob(["src/*.h"]),
    hdrs = glob(["include/game_ggml/*.h"]),
    includes = ["include", "src"],
    defines = ["GAME_GGML_VERSION_MAJOR=0", "GAME_GGML_VERSION_MINOR=1", "GAME_GGML_VERSION_PATCH=0"],
    deps = ["@ggml//:ggml", "@pocketfft//:pocketfft"],
    linkstatic = True,
)

# 复用上游完整测试源文件；WAV/CLI 辅助代码仅参与测试链接。
cc_test(
    name = "core_tests",
    srcs = [
        "tests/test_backend.cpp", "tests/test_gguf_io.cpp", "tests/test_ops_basic.cpp",
        "tests/test_ops_ffn.cpp", "tests/test_ops_rope.cpp", "tests/test_encoder.cpp",
        "tests/test_mel.cpp", "tests/test_segmenter.cpp", "tests/test_estimator.cpp",
        "tests/test_pipeline_e2e.cpp", "tests/support/reference_io.cpp",
        "tests/support/ggml_test_env.cpp", "tests/test_cli.cpp",
        "src/cli/wav_io.cpp", "src/cli/slicer.cpp",
        "src/cli/midi_writer.cpp", "src/cli/text_writer.cpp",
    ] + glob(["tests/support/*.h", "src/cli/*.h"]),
    includes = [".", "tests"],
    deps = [":game", "@gtest//:gtest_main", "@dr_libs//:dr_wav"],
)
filegroup(name = "license", srcs = ["LICENSE"])
