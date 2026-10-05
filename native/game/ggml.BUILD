load("@rules_cc//cc:cc_library.bzl", "cc_library")
package(default_visibility = ["//visibility:public"])

config_setting(name = "windows", constraint_values = ["@platforms//os:windows"])
config_setting(name = "macos", constraint_values = ["@platforms//os:macos"])
config_setting(name = "arm64", constraint_values = ["@platforms//cpu:arm64"])
config_setting(name = "armv7", constraint_values = ["@platforms//cpu:armv7"])

COMMON_DEFINES = ["GGML_SCHED_MAX_COPIES=4", "_XOPEN_SOURCE=600", 'GGML_VERSION=\\"0.19.0\\"', 'GGML_COMMIT=\\"unknown\\"'] + select({
    ":windows": ["_CRT_SECURE_NO_WARNINGS"],
    ":macos": ["_DARWIN_C_SOURCE"],
    "//conditions:default": ["_GNU_SOURCE"],
})

cc_library(
    name = "base",
    srcs = [
        "src/ggml.c", "src/ggml.cpp", "src/ggml-alloc.c", "src/ggml-backend.cpp",
        "src/ggml-backend-meta.cpp", "src/ggml-opt.cpp", "src/ggml-threading.cpp",
        "src/ggml-quants.c", "src/gguf.cpp",
    ],
    hdrs = glob(["include/*.h", "src/*.h", "src/ggml-cpu/**/*.h"]),
    includes = ["include", "src"],
    local_defines = COMMON_DEFINES,
    linkstatic = True,
)

cc_library(
    name = "cpu",
    srcs = [
        "src/ggml-cpu/ggml-cpu.c", "src/ggml-cpu/ggml-cpu.cpp",
        "src/ggml-cpu/repack.cpp", "src/ggml-cpu/hbm.cpp", "src/ggml-cpu/quants.c",
        "src/ggml-cpu/traits.cpp", "src/ggml-cpu/amx/amx.cpp", "src/ggml-cpu/amx/mmq.cpp",
        "src/ggml-cpu/binary-ops.cpp", "src/ggml-cpu/unary-ops.cpp",
        "src/ggml-cpu/vec.cpp", "src/ggml-cpu/ops.cpp",
    ] + select({
        ":arm64": ["src/ggml-cpu/arch/arm/quants.c", "src/ggml-cpu/arch/arm/repack.cpp"],
        ":armv7": ["src/ggml-cpu/arch/arm/quants.c", "src/ggml-cpu/arch/arm/repack.cpp"],
        "//conditions:default": ["src/ggml-cpu/arch/x86/quants.c", "src/ggml-cpu/arch/x86/repack.cpp"],
    }) + select({
        ":armv7": [],
        "//conditions:default": ["src/ggml-cpu/llamafile/sgemm.cpp"],
    }),
    hdrs = glob(["src/ggml-cpu/**/*.h"]),
    includes = ["src/ggml-cpu"],
    local_defines = COMMON_DEFINES + ["GGML_USE_CPU_REPACK"] + select({
        ":armv7": [],
        "//conditions:default": ["GGML_USE_LLAMAFILE"],
    }) + select({
        ":macos": ["GGML_USE_ACCELERATE", "ACCELERATE_NEW_LAPACK", "ACCELERATE_LAPACK_ILP64"],
        "//conditions:default": [],
    }),
    linkopts = select({":macos": ["-framework Accelerate"], "//conditions:default": []}),
    deps = [":base"],
    linkstatic = True,
)

cc_library(
    name = "ggml",
    srcs = ["src/ggml-backend-dl.cpp", "src/ggml-backend-reg.cpp"],
    local_defines = COMMON_DEFINES,
    defines = ["GGML_USE_CPU"],
    deps = [":base", ":cpu"],
    linkopts = select({
        ":windows": ["-DEFAULTLIB:advapi32.lib"],
        ":macos": ["-pthread"],
        "//conditions:default": ["-pthread", "-lm", "-ldl"],
    }),
    linkstatic = True,
)
filegroup(name = "license", srcs = ["LICENSE"])
