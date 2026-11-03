# GAME 音符提取原生库

Mobile 层通过 `IGameBackend` 选择 Core 的 ONNX 实现或这里的进程内 GGML 实现。
进程内 C ABI 的方案来自 Kakaru 的 PR；本实现采用独立 ABI `opum_game`，不能复用原 PR 的 `game_ggml_shared` 二进制。
Core 与 USTX 数据结构不需要改动。

## 文档分工

- 平台、渲染器、推理后端及硬件加速支持：[全局 README](../../README.md#feature-matrix)。
- 音符提取入口及模型包：[GAME 使用说明](../../README.md#game-note-extraction)。
- Windows 电脑的环境安装和 Windows / Android 调试：[贡献指南](../../CONTRIBUTING.md)。
- 本文面向维护原生桥接和构建逻辑的开发者，说明构建约定和 ABI。

## 构建约定

构建入口、参数与增量规则见[原生构建](../README.md)。GAME 与 Worldline 共用同一个 MSBuild 接入和 Python 执行脚本。
`MODULE.bazel` 固定 game.cpp、ggml 和 pocketfft 的版本与归档；dr_libs 只参与上游 WAV/CLI 测试。
正常 Build / Publish 按项目的配置和 RID 生成并打包 `opum_game`，不收集许可证或生成来源清单。
CPU 发布库不使用构建机的 AVX 指令集。

Windows x64/x86 使用 Visual Studio MSVC，ARM64 使用 ClangCL，因为 ggml 不支持 MSVC ARM。
Windows ARM64 需要 ARM64 C++ 工具、Windows SDK 及 C++ Clang 编译器。
Linux 使用本机 C/C++ 工具链；macOS 使用 Xcode Command Line Tools，CPU 后端保留 Accelerate。
桌面 Linux/macOS 使用匹配目标架构的 runner。

Android 使用项目解析的 SDK 与 `../android-ndk-version.txt` 指定的 NDK。
Bazel 使用 NDK Clang，静态链接 libc++。
ARMv7 关闭不兼容的 llamafile，其他架构保留。工具链发生变化时 Bazel 重新编译受影响的目标。

## CI 与本地构建

手动使用 `--test` 可运行 19 项无需模型/参考数据的上游既有用例。其余 18 项数值对照/模型用例需要外部数据：
其中 11 项在辅助函数中调用 `GTEST_SKIP` 后仍继续读缺失文件，不能当作自动跳过。
完整目标 `@game_cpp//:core_tests` 仍可在准备好上游参考数据后单独运行。
发布流程通过项目自身的 Build / Publish 规则收集库、打包并上传。

```sh
python native/build.py artifacts/native/win-x64/game.inputs --test
```

`--test` 只能用于匹配宿主架构的平台。
更新依赖时显式传入 `--update-lock`，审查并提交 `MODULE.bazel.lock`；普通构建要求锁文件匹配。

## 生命周期与限制

- 每次提取持有独立模型和安全句柄；后台线程串行调用，结束后释放。
- C ABI 使用 `size_t` / `nuint`、固定布局音符和 C 调用约定。
  完整结果由原生句柄持有，托管端复制后才能再次调用；没有固定音符数上限。
- 复用 Core 的静音切片器；Mobile 层处理分片裁剪、淡入淡出、单声道转换和音符时间映射。
  不改变原分片 PCM。模型采样率限定 44100 Hz，与当前波形加载路径一致。
- 长于 30 秒的连续区间进一步分段，限制单次推理内存和取消等待时间；边界附近的持续音符可能被分开。
- ONNX 的取消传递给 RunOptions；GGML 暂无中断正在执行的 C++ 推理接口，等待当前段完成后丢弃结果。
- 弹窗传入语言、采样步数、边界阈值、检测半径和音符置信度阈值；默认通用语言、8 步、0.2、2 帧、0.2。
- ONNX 按片段长度组批；同时限制片段数和补齐到最长片段后的总时长，默认 1 段 / 60 秒。
  0 秒表示不限制批时长；单片段超过该限制仍单独推理。GGML 不使用批处理参数。
- 语言选项来自各自模型旁的 `config.json`。GGUF 没有配置文件时只提供通用语言；选择模型未声明的语言时禁用该后端按钮。
- 可选 RMVPE 需要 `rmvpe/rmvpe.onnx`。GAME 模型释放后分段提取音高，将 MIDI 音高转换为音符对应的 PITD 曲线。
  Mobile 直接修改尚未加入工程的结果，不调用 Core 中会提交全局撤销命令的 `ApplyToPart`。
- 本构建入口支持 CPU。GGML 不使用 ONNX 的硬件加速设置；Vulkan/CUDA/Metal 不在此构建图中，接入需另行实现和验证。
