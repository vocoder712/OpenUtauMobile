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

`GameNative.targets` 在正常 Build / Publish 中调用同一个 `Build.cmake`：

1. 按 RID 选择工具链，在 `artifacts/game-build/<rid>` 配置 CMake。
2. 构建原生库并安装到 `artifacts/game-native/<rid>`；后续构建由 CMake 增量检查源码。
3. 构建完成后收集文件，确保首次构建也能把库和许可证放入应用。

桌面端 worldline 由 `DesktopNative.targets` 在 Build 和 Publish 中按架构复制到程序目录。
macOS 使用仓库已有的通用 worldline 库。不会将多个架构的同名 DLL 依次覆盖到同一个路径。

CMake 固定 game.cpp 提交 `97f92770704c154e1af4a9b3066b7701041dbc38`；
上游固定 ggml 版本及 SHA256、pocketfft 和 dr_libs 提交。所有下载、缓存和二进制位于 Git 忽略目录。
缺少工具链或下载失败会使构建明确失败，不会生成缺少 GGML 运行库的“成功”构建。

Android 目标先执行工作负载的 `_ResolveSdks`，再将解析出的 NDK 路径传入
`Build.cmake` 的 `OPUM_ANDROID_NDK`。SDK 与 NDK 可以分开安装；未提供 NDK 时，脚本才从
SDK 的 `ndk/<固定版本>` 查找。Windows 路径传给 CMake 前转换为正斜杠，避免末尾反斜杠影响引号。

Windows 默认使用注册的 Visual Studio C++ 工具链；已配置的 MSVC 终端或 MinGW 也可使用。
Linux 需要本机 C/C++ 工具链；macOS 需要 Xcode Command Line Tools。Android 使用 NDK Clang 和 Ninja。
所有平台均需 Git 和 CMake 3.24+。原生部分固定构建 Release，应用的 Debug 配置仍可调试 C#。
当前自动化覆盖正常 Build / Publish，不将 `publish --no-build` 作为原生文件收集的验证路径。

可选构建属性：

- `GameCMakeExecutable`：CMake 可执行文件路径。
- `GameCMakeGenerator`：需要显式选择编译器时设置，如 `MinGW Makefiles`。变更生成器应使用新的 `GameBuildRoot`。
- `GameBuildRoot`、`GameNativeRoot`：原生缓存、产物根目录，可用于隔离验证。
- `EnableGameGgml=false`：跳过原生构建与打包；此时应使用 ONNX。当前选项不会隐藏 UI 中的 GGML 选项，也不会清除输出目录里的旧库；验证这种包时应使用独立输出目录。

## CI 验证

CI 只准备工具链，原生编译和打包走项目自身的 Build / Publish 规则。
PR 检查包含不传 RID、不传 GGML 开关的桌面构建，以及实际加载 worldline / GGML 的检查。
发布流程也检查发布目录的原生库加载。Android PR 与发布共用 `native/verify-android-runtime.py`，
检查 APK 的 ABI、worldline / ONNX / GAME 原生库及 ELF 架构、GAME 的 16 KB 对齐和许可证；
发布另外检查这些库不含 Linux/glibc 依赖。CI 显式传入刚安装的固定 NDK 路径，避免工作负载选到 runner 的其他版本。

本地可用 Python 3 执行同一检查（仅验证时需要 Python，不参与应用构建）：

```sh
python native/verify-runtime.py OpenUtauMobile.Windows/bin/Debug/net10.0-windows
```

它会实际加载两个库，验证 GAME 导出接口及错误返回，不需要模型或用户工程。
这不能代替各平台真机、GPU 和实际歌声识别质量测试。

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
- 发布流程默认 CPU。GGML 不使用 ONNX 的硬件加速设置。CMake 允许显式启用上游 Vulkan/CUDA/Metal，
  但加速器 SDK、运行时与附加资源的交付需要单独验证；本功能不承诺这些构建已通过设备测试。
