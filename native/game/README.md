# GAME 音符提取原生库

Mobile 层通过 `IGameBackend` 选择 Core 的 ONNX 实现或这里的进程内 GGML 实现。
进程内 C ABI 的方案来自 Kakaru 的 PR；本实现采用独立 ABI `opum_game`，不能复用原 PR 的 `game_ggml_shared` 二进制。
Core 与 USTX 数据结构不需要改动。

## 文档分工

- 平台、渲染器、推理后端及硬件加速支持：[全局 README](../../README.md#feature-matrix)。
- 音符提取入口及模型包：[GAME 使用说明](../../README.md#game-note-extraction)。
- Windows 电脑的环境安装和 Windows / Android 调试：[贡献指南](../../CONTRIBUTING.md)。
- 本文面向维护原生桥接和构建逻辑的开发者，说明构建约定、ABI 和验证边界。

## 构建约定

`GameNative.targets` 在正常 Build / Publish 中调用 `build.py`，以 Bazelisk 1.29.0 / Bazel 9.2.0 直接编译 C/C++ 源码。

1. 在 `artifacts/game-bazel-build/<rid>/workspace` 准备独立构建入口，按 RID 选择工具链。每次由 Bazel 检查依赖图，同 RID 的构建由进程锁串行化。
2. `MODULE.bazel` 固定 game.cpp `97f92770704c154e1af4a9b3066b7701041dbc38`、ggml `v0.19.0` 和 pocketfft `32424d2067c2e8043dc646a4e49754b2b40cc549`，归档均带 SHA256。dr_libs 仅供上游 WAV/CLI 测试，不进入应用库依赖图。
3. 检查二进制架构、四个 C ABI 导出、运行时依赖和 Android 16 KB 对齐；安装到 `artifacts/game-native/<rid>`，同时收集许可证及 `game-build.json` 来源记录。
4. MSBuild 收集产物用于桌面输出或 Android 原生资产。缺少工具链、锁文件过时或下载失败会使构建失败。

Worldline 由 [WorldlineNative.targets](../worldline/WorldlineNative.targets) 使用同一版本的 Bazel 构建。
两者共用 Python 3.10+、Git、Bazelisk 和固定 Android NDK，不需要额外原生构建生成器。
用户与系统 `.bazelrc` 不参与构建；CPU 发布库不使用构建机的 AVX 指令集。

Windows x64/x86 使用 Visual Studio MSVC，ARM64 使用 ClangCL，因为 ggml 不支持 MSVC ARM。
Windows ARM64 需要 ARM64 C++ 工具、Windows SDK 及 C++ Clang 编译器。
Linux 使用本机 C/C++ 工具链；macOS 使用 Xcode Command Line Tools，CPU 后端保留 Accelerate。
桌面 Linux/macOS 使用匹配目标架构的 runner。

Android 先由 `AndroidNativeToolchain.targets` 解析工具链，显式 `AndroidNdkDirectory` 优先，
否则使用 SDK 下 `android-ndk-version.txt` 指定的 r30 LTS（`30.0.16248370`）。
NDK 路径通过 `--ndk` 传给脚本，并与 .NET 原生资产收集共用；Bazel 使用 NDK Clang，静态链接 libc++。
ARMv7 关闭不兼容的 llamafile，其他架构保留。工具链发生变化时 Bazel 重新编译受影响的目标。

可选 MSBuild 属性：`GamePythonExecutable`、`GameBazelExecutable`、`GameBuildRoot`、`GameNativeRoot`。
`EnableGameGgml=false` 可跳过构建和打包，但不会隐藏 UI 选项或删除旧输出；验证这种包时使用独立输出目录。
正常 Build / Publish 均包含原生构建，不将 `publish --no-build` 作为验证入口。

## CI 与本地验证

PR 矩阵包含桌面六个 RID、Android 四 ABI 的 Worldline 和 GAME 源码构建。
匹配宿主的桌面任务运行上游已有测试及无模型 ABI 检查；GAME 编译上游完整测试源文件，
`--test` 明确选择 19 项无需模型/参考数据的既有用例。其余 18 项数值对照/模型用例未验收：
其中 11 项在辅助函数中调用 `GTEST_SKIP` 后仍继续读缺失文件，不能当作自动跳过。
完整目标 `@game_cpp//:core_tests` 仍可在准备好上游参考数据后单独运行。Linux/macOS 真正执行结果以 CI 验收为准。
发布流程通过项目自身的 Build / Publish 规则收集库，验证发布目录加载及 Android APK 资产。

```sh
python native/game/build.py --rid win-x64 --test
python native/game/verify.py artifacts/game-native/win-x64 win-x64 --run
python native/verify-runtime.py OpenUtauMobile.Windows/bin/Debug/net10.0-windows
```

`--test` 和 `--run` 只能用于匹配宿主及 Python 架构的平台。交叉构建自动检查文件格式与导出，但不执行目标库。
更新依赖时显式传入 `--update-lock`，审查并提交 `MODULE.bazel.lock`；普通构建要求锁文件匹配。
这些检查不能代替真机加载、真实模型推理与识别质量验证。

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
