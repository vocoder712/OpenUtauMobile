cmake_minimum_required(VERSION 3.24)
project(OpumGame LANGUAGES C CXX)

# 显式 RID 构建必须匹配工具链；仅检查指针位数无法区分 x64 和 ARM64。
if(OPUM_RID)
    if(MSVC)
        set(actual_arch "${CMAKE_CXX_COMPILER_ARCHITECTURE_ID}")
    elseif(APPLE AND CMAKE_OSX_ARCHITECTURES)
        set(actual_arch "${CMAKE_OSX_ARCHITECTURES}")
    else()
        set(actual_arch "${CMAKE_SYSTEM_PROCESSOR}")
    endif()
    string(TOLOWER "${actual_arch}" actual_arch)
    if(OPUM_RID MATCHES "-arm64$" AND NOT actual_arch MATCHES "^(arm64|aarch64)$")
        message(FATAL_ERROR "${OPUM_RID} requires an ARM64 toolchain; actual: ${actual_arch}")
    elseif(OPUM_RID MATCHES "-x64$" AND NOT actual_arch MATCHES "^(amd64|x86_64|x64)$")
        message(FATAL_ERROR "${OPUM_RID} requires an x64 toolchain; actual: ${actual_arch}")
    elseif(OPUM_RID MATCHES "-(x86|arm)$" AND NOT CMAKE_SIZEOF_VOID_P EQUAL 4)
        message(FATAL_ERROR "${OPUM_RID} requires a 32-bit toolchain")
    endif()
endif()

include(FetchContent)
set(CMAKE_POSITION_INDEPENDENT_CODE ON)
set(CMAKE_MSVC_RUNTIME_LIBRARY "MultiThreaded$<$<CONFIG:Debug>:Debug>")
set(BUILD_SHARED_LIBS OFF CACHE BOOL "" FORCE)
set(GAME_GGML_BUILD_CLI OFF CACHE BOOL "" FORCE)
set(GAME_GGML_BUILD_TESTS OFF CACHE BOOL "" FORCE)
set(GAME_GGML_METAL OFF CACHE BOOL "Enable Metal")
set(GAME_GGML_CUDA OFF CACHE BOOL "Enable CUDA")
set(GAME_GGML_VULKAN OFF CACHE BOOL "Enable Vulkan")
set(GGML_NATIVE OFF CACHE BOOL "" FORCE)
set(GGML_OPENMP OFF CACHE BOOL "Enable OpenMP")
# 默认发布库不依赖构建机的 AVX 指令集；专用构建可逐项开启。
foreach(feature SSE42 AVX AVX2 AVX512 F16C FMA BMI2)
    set(GGML_${feature} OFF CACHE BOOL "Enable ${feature}")
endforeach()

# 上游 dr_libs 的测试子模块不参与应用构建，禁止递归下载。
FetchContent_Declare(dr_libs
    GIT_REPOSITORY https://github.com/mackron/dr_libs.git
    GIT_TAG 243e26ffa08a24dc8ae2e7a8c57123d9e504690c
    GIT_SUBMODULES "")

# 固定源码版本；依赖仅下载到构建目录，不向仓库写入二进制文件。
FetchContent_Declare(game_cpp
    GIT_REPOSITORY https://github.com/KakaruHayate/game.cpp.git
    GIT_TAG 97f92770704c154e1af4a9b3066b7701041dbc38
    GIT_SUBMODULES "")
FetchContent_MakeAvailable(game_cpp)

add_library(opum_game SHARED game_bridge.cpp)
target_compile_features(opum_game PRIVATE cxx_std_17)
target_link_libraries(opum_game PRIVATE game_ggml::game_ggml)
if(MSVC)
    target_compile_options(opum_game PRIVATE /utf-8)
endif()
if(MINGW)
    # 避免 IDE 的 PATH 与终端不同导致 libstdc++ / libgcc / winpthread 缺失。
    target_link_options(opum_game PRIVATE -static)
endif()
if(ANDROID)
    # 同时兼容 4 KB 和 16 KB 页大小的设备。
    target_link_options(opum_game PRIVATE "-Wl,-z,max-page-size=16384")
endif()
set_target_properties(opum_game PROPERTIES PREFIX "")
if(NOT WIN32)
    set_target_properties(opum_game PROPERTIES PREFIX "lib")
endif()
install(TARGETS opum_game RUNTIME DESTINATION . COMPONENT GameRuntime
        LIBRARY DESTINATION . COMPONENT GameRuntime)
FetchContent_GetProperties(ggml SOURCE_DIR ggml_source)
FetchContent_GetProperties(pocketfft SOURCE_DIR pocketfft_source)
install(FILES "${game_cpp_SOURCE_DIR}/LICENSE" DESTINATION . RENAME LICENSE.game.cpp.txt COMPONENT GameRuntime)
install(FILES "${ggml_source}/LICENSE" DESTINATION . RENAME LICENSE.ggml.txt COMPONENT GameRuntime)
install(FILES "${pocketfft_source}/LICENSE.md" DESTINATION . RENAME LICENSE.pocketfft.txt COMPONENT GameRuntime)
