# Worldline 源码构建

`WorldlineNative.targets` 在 Windows、Linux、macOS、Android、iOS、Browser 的正常 Build / Publish 中运行
`build.py`，使用 Bazelisk 1.29.0 / Bazel 9.2.0 从 `native/upstream_cpp` 构建。
没有预编译库回退。iOS / Browser 使用同一入口构建静态库并链入宿主。

## 工具与平台

- 所有平台需要 Python 3.10+、Bazelisk 和 Git。Windows 的上游测试还需要 Git Bash。
- Windows 使用 Visual Studio C++ 工具和 Windows SDK，静态链接 MSVC 运行库；ARM64 需要 ARM64 C++ 组件。
- Linux 使用目标架构的本机 C/C++ 编译器；macOS 使用本机 Xcode Command Line Tools。两者在对应架构的 runner 构建。
- Android 使用 `../game/android-ndk-version.txt` 固定的 NDK r30，最低 API 24，支持四 ABI。
  静态链接 libc++，要求 ELF LOAD 对齐至少 16 KB。`AndroidNdkDirectory` 显式路径优先，否则使用 SDK 中的固定版本。
- iOS 必须在 macOS 上安装完整 Xcode，支持 `ios-arm64`、`iossimulator-arm64`、`iossimulator-x64`。
  使用所选 Xcode 的 Clang、ar 和对应 SDK；部署版本跟随宿主的 `SupportedOSPlatformVersion`（直接调用默认 15.0）。
- Browser 支持 `browser-wasm`，使用当前 .NET `wasm-tools` 工作负载中的 Emscripten、LLVM、Node 和 sysroot。
  线程、SIMD 和异常模式由 MSBuild 传入，与最终 .NET WASM 链接配置一致，不额外安装独立 Emscripten。

原生部分始终使用优化构建；应用 Debug / Release 不改变算法和浮点选项。不使用 `-march=native` 或额外的 fast-math。

## 构建隔离与依赖

冻结的 CPP subtree 不写入生成文件，也不直接修改。脚本在 `artifacts/worldline-build/<rid>/workspace`
创建构建副本，仅给旧 BUILD 补上 Bazel 9 要求的规则加载，并叠加本目录的 Bzlmod 入口。
同一 RID 的构建由进程锁保护；Bazel 每次检查依赖图，失败直接中止应用构建。

`MODULE.bazel` 保存上游依赖版本、下载校验和及 WORLD / libpyin / spline 补丁。
absl、gtest、re2、xxhash 的旧构建文件只在下载副本中适配，避免构建工具的传递依赖升级冻结依赖。
正常构建使用 `--lockfile_mode=error`；维护依赖时显式运行 `--update-lock`，审查本目录锁文件变更。
用户与系统 `.bazelrc` 不参与此构建，平台选项由入口统一提供。

产物安装到 `artifacts/worldline-native/<rid>`，在加入应用之前验证目标架构、20 个所需导出，
以及 Android 的动态依赖和段对齐。`worldline-build.json` 记录冻结提交、CPP 指纹、依赖与补丁指纹、
编译器、NDK、Bazel、构建选项和库哈希。这是来源追踪，不表示不同编译器下能生成相同字节。
许可证和来源清单进入应用的 `WorldlineLicenses`（Android 为 assets 下的同名目录）。

更新冻结上游时，也需同步审查 `upstream-revision.txt`、许可证、Bzlmod 依赖和适配器。
不执行 `publish --no-build` 来验证首次原生资产收集。

## 直接构建与验证

```powershell
python native/worldline/build.py --rid win-x64 --test
python native/worldline/verify.py artifacts/worldline-native/win-x64/worldline.dll win-x64 --run
python native/worldline/build.py --rid android-arm64
```

`--test` 运行上游已有的 worldline、effects、continuous_noise 测试，仅适用于宿主架构。
音频设备测试需要硬件，不自动运行。`--run` 实际调用全部十个分析/合成导出：
F0 四种模式、MGC/BAP 解码、AnalysisConfig 初始化、普通/R1.1 分析和合成。
检查输出长度、有限数、缓冲区前后哨兵、编码/解码输入的一致性，以及连续噪声种子的高 32 位和输入不可变约定。
`--run --report <path.json>` 可保存确定性输入输出，用于与实际 Core 托管调用比较。
这些是小输入的 ABI 检查，不代替模型、真实音源渲染、内存压力或设备音频验收。
Android ELF 检查不能替代设备上的渲染和音频播放验收。

## iOS / Browser 静态链接

```sh
# macOS：真机和两种模拟器，分别生成 libworldline.a。
python3 native/worldline/build.py --rid ios-arm64
python3 native/worldline/build.py --rid iossimulator-arm64
python3 native/worldline/build.py --rid iossimulator-x64

# 通过 .NET 提供精确的 Emscripten 工具和编译配置，生成 worldline.a。
dotnet msbuild OpenUtauMobile.Browser/OpenUtauMobile.Browser.csproj -t:BuildWorldlineNative
python3 native/worldline/verify_browser.py artifacts/worldline-native/browser-wasm
```

`cc_static_library` 收集冻结上游的 Worldline、音频接口及传递依赖对象，生成独立归档。
平台工具链只在新目标上注册，不改变桌面或 Android 工具链。归档检查包含每个对象的文件格式、
iOS CPU 与真机/模拟器平台、全部 20 个 ABI 定义及重复定义；来源清单记录 SDK、目标三元组与选项。

iOS 使用 `NativeReference`、C++ 运行库和系统音频框架。宿主模块初始化器将 Core 的
`DllImport("worldline")` 解析到主程序；链接选项显式保留并导出这些函数。
IPA 验证还检查最终可执行文件导出和 `WorldlineLicenses`，避免仅归档成功而发布时被裁掉。

Browser 使用文件名 `worldline.a` 配合 `NativeFileReference`，让 .NET 生成静态 P/Invoke 表，
不修改 Core。许可证和来源清单位于 `wwwroot/licenses/Worldline`。
`verify_browser.py` 使用同一 SDK 链接全部 ABI，并在 Node 中运行四种 F0 模式、缓冲区哨兵、
AnalysisConfig 布局和合成长度检查。此检查不创建音频设备，也不验证 ONNX、模型渲染或浏览器播放。

本机 Windows 已验证 Browser 原生构建、Debug 应用链接、Release 发布与 Node 中的 WASM 调用；iOS 配置需在 Mac/CI 实测，
不能将 CI 任务定义视为构建或设备验收通过。现有 Browser Dummy 音频后端及 ONNX 能力边界不因静态库接入改变。

真实浏览器中也已通过 Core 的四种 F0 模式及配置初始化调用。接入时发现 .NET 10 的增量构建
遗漏了 `runtime.c` 对 `wasm_m2n_invoke.g.h` 的依赖，可能复用缺少新 P/Invoke 签名的旧运行时对象。
`TrackWorldlineWasmTrampolines` 在项目构建层补齐该依赖，无需修改 SDK 或定期手动清理 obj。

可选 MSBuild 属性：`WorldlinePythonExecutable`、`WorldlineBazelExecutable`、`WorldlineBuildRoot`、
`WorldlineNativeRoot`。DesignTimeBuild 不编译或下载。

PR 工作流覆盖桌面六个 RID 的上游测试和实际 ABI 调用、Android 四 ABI 的源码构建。
发布工作流通过相同 MSBuild 入口构建并检查最终发布目录或 APK；Linux/macOS 的实际验证结果以 CI 为准。
Linux x64/ARM64 的 CI 编译环境固定为 Ubuntu 24.04，避免浮动 runner 自动抬高系统库基线。
实际所需的 glibc/libstdc++ 符号版本，以及 macOS 的依赖、部署版本和加载路径随 CI 验证产物保存；
这些结果核验完成前，不声明更老系统兼容性。
CI 暂不恢复或保存跨任务的 Bazel 编译缓存，每个矩阵任务独立构建；本地下载缓存与目标架构的编译目录分离。
