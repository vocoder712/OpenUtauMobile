# ---------------------------------------------------------------------------
# OpenUtauMobile native/ —— game.cpp (GAME ggml 推理) 深度植入的 CMake 宿主。
#
# 职责:
#   1. 以 submodule native/game.cpp 引入上游（add_subdirectory 不修改其源码）。
#   2. 把 game_ggml 静态库 + 本仓库的 C shim(game_capi) 联合编成一个
#      平台共享库 libgame_ggml_shared(.dll/.so/.dylib)，对外只暴露 C ABI，
#      供 .NET 层 P/Invoke。
#   3. 附带一个原生 smoke 验证入口 game_capi_check（不进 OPUM 主包，仅开发用）。
#
# 构造方式：直接 add_subdirectory(game.cpp)，复用其 game_ggml 静态目标。
# 后端编译选项透传（遵循上游命名）：
#   GAME_GGML_METAL / GAME_GGML_CUDA / GAME_GGML_VULKAN / (CPU 恒在)
# ---------------------------------------------------------------------------

cmake_minimum_required(VERSION 3.18)

project(OpenUtauMobileNative LANGUAGES C CXX)

# 透传并让用户在 configure 时决定要编哪些加速器（默认只 CPU，最安全）。
# 用法: -DGAME_GGML_VULKAN=ON -DGAME_GGML_METAL=ON ...
set(GAME_GGML_METAL   "unset" CACHE STRING "Enable ggml Metal backend (ON/OFF; default = Apple only)")
set(GAME_GGML_CUDA    OFF CACHE BOOL   "Enable ggml CUDA backend")
set(GAME_GGML_VULKAN  OFF CACHE BOOL   "Enable ggml Vulkan backend")
set(GAME_GGML_LLAMAFILE ON CACHE BOOL  "Enable ggml-llamafile sgemm kernels (CPU)")
set(GAME_GGML_BUILD_CLI   OFF CACHE BOOL "Build upstream game_ggml_cli (dev only)")
set(GAME_GGML_BUILD_TESTS OFF CACHE BOOL "Build upstream gtest suite")
set(OPUM_NATIVE_SMOKE     ON  CACHE BOOL "Build game_capi_check smoke tool")

if(GAME_GGML_METAL STREQUAL "unset")
    if(APPLE)
        set(GAME_GGML_METAL ON)
    else()
        set(GAME_GGML_METAL OFF)
    endif()
endif()

# 关闭上游 CLI/tests（由 OPUM_NATIVE_SMOKE 取代），并发正确选项到 game.cpp。
add_subdirectory(game.cpp)

# MSVC 默认按本地 ANSI 代码页处理源文件，本工程源码（shim 中文注释/字符串）
# 是 UTF-8 —— 上游 game.cpp 的 /utf-8 只在其目录作用域生效，这里对本目录
# 新建目标（game_ggml_shared 等）显式补充, 否则中文会被误读导致语法崩坏。
if(MSVC)
    add_compile_options(/utf-8)
endif()

set(GAME_CAPI_SOURCES
    shim/game_capi.cpp
    shim/game_capi.h
)

# ---- 平台共享库（只暴露 C ABI）------------------------------------------------
add_library(game_ggml_shared SHARED ${GAME_CAPI_SOURCES})
add_library(OpenUtauMobile::game_ggml ALIAS game_ggml_shared)

target_include_directories(game_ggml_shared
    PUBLIC  $<BUILD_INTERFACE:${CMAKE_CURRENT_SOURCE_DIR}/shim>
    PRIVATE ${CMAKE_CURRENT_SOURCE_DIR}
)

# game_ggml 是 static；其 PUBLIC 链接的 ggml 也会随链接进我们的 dll。
# 共享库被打包进各平台 runtimes/（.NET 层用 DllImport 加载）。
target_link_libraries(game_ggml_shared
    PRIVATE game_ggml::game_ggml
)

if(MSVC)
    # 明确导出 extern "C" 符号（Windows 需要 __declspec(dllexport) 或 .def；
    # 简单起见开启自动导出, 因为只导出这几个 extern "C" 函数不会污染）。
    # 也可改用生成 .def。自动导出对 /EXPORT 生成在 x86/x64 都 OK。
    set_target_properties(game_ggml_shared PROPERTIES WINDOWS_EXPORT_ALL_SYMBOLS ON)
endif()

set_target_properties(game_ggml_shared PROPERTIES
    OUTPUT_NAME            game_ggml_shared
    PREFIX                 ""       # Windows 无 lib 前缀；其它平台 libgame_ggml_shared
    RUNTIME_OUTPUT_DIRECTORY   "${CMAKE_BINARY_DIR}/app"
    LIBRARY_OUTPUT_DIRECTORY   "${CMAKE_BINARY_DIR}/app"
    ARCHIVE_OUTPUT_DIRECTORY   "${CMAKE_BINARY_DIR}/lib"
)
# 归档/库输出在 MSVC 多配置下需要逐配置路径
if(MSVC)
    foreach(_cfg IN ITEMS Debug Release RelWithDebInfo MinSizeRel)
        string(TOUPPER "${_cfg}" _CFG)
        set_property(TARGET game_ggml_shared PROPERTY
            ARCHIVE_OUTPUT_DIRECTORY_${_CFG} "${CMAKE_BINARY_DIR}/lib")
        set_property(TARGET game_ggml_shared PROPERTY
            RUNTIME_OUTPUT_DIRECTORY_${_CFG} "${CMAKE_BINARY_DIR}/app")
    endforeach()
endif()

# ---- 原生 smoke 验证入口（开发用, 不随 OPUM 主包发布）----------------------
if(OPUM_NATIVE_SMOKE)
    add_executable(game_capi_check shim/smoke_main.cpp)
    target_link_libraries(game_capi_check PRIVATE game_ggml_shared)
    # 会抓取 platform 前缀/后缀, 便于直接看结果
    set_target_properties(game_capi_check PROPERTIES
        RUNTIME_OUTPUT_DIRECTORY "${CMAKE_BINARY_DIR}/app")
    if(MSVC)
        foreach(_cfg IN ITEMS Debug Release RelWithDebInfo MinSizeRel)
            string(TOUPPER "${_cfg}" _CFG)
            set_property(TARGET game_capi_check PROPERTY
                RUNTIME_OUTPUT_DIRECTORY_${_CFG} "${CMAKE_BINARY_DIR}/app")
        endforeach()
    endif()
endif()

message(STATUS "")
message(STATUS "OpenUtauMobile native/ configuration:")
message(STATUS "  Metal   ........... ${GAME_GGML_METAL}")
message(STATUS "  CUDA   ........... ${GAME_GGML_CUDA}")
message(STATUS "  Vulkan ........... ${GAME_GGML_VULKAN}")
message(STATUS "  LlamaFile CPU .... ${GAME_GGML_LLAMAFILE}")
message(STATUS "  Smoke  ........... ${OPUM_NATIVE_SMOKE}")
