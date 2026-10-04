# Worldline 源码构建

`WorldlineNative.targets` 在 Windows、Linux、macOS、Android 的正常 Build / Publish 中运行
`build.py`，使用 Bazelisk 1.29.0 / Bazel 9.2.0 从 `native/upstream_cpp` 构建。
没有预编译库回退。iOS / Browser 尚未接入。

## 工具与平台

- 所有平台需要 Python 3.10+、Bazelisk 和 Git。Windows 的上游测试还需要 Git Bash。
- Windows 使用 Visual Studio C++ 工具和 Windows SDK，静态链接 MSVC 运行库；ARM64 需要 ARM64 C++ 组件。
- Linux 使用目标架构的本机 C/C++ 编译器；macOS 使用本机 Xcode Command Line Tools。两者在对应架构的 runner 构建。
- Android 使用 `../game/android-ndk-version.txt` 固定的 NDK r30，最低 API 24，支持四 ABI。
  静态链接 libc++，要求 ELF LOAD 对齐至少 16 KB。`AndroidNdkDirectory` 显式路径优先，否则使用 SDK 中的固定版本。

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

可选 MSBuild 属性：`WorldlinePythonExecutable`、`WorldlineBazelExecutable`、`WorldlineBuildRoot`、
`WorldlineNativeRoot`。DesignTimeBuild 不编译或下载。

PR 工作流覆盖桌面六个 RID 的上游测试和实际 ABI 调用、Android 四 ABI 的源码构建。
发布工作流通过相同 MSBuild 入口构建并检查最终发布目录或 APK；Linux/macOS 的实际验证结果以 CI 为准。
Linux x64/ARM64 的 CI 编译环境固定为 Ubuntu 24.04，避免浮动 runner 自动抬高系统库基线。
实际所需的 glibc/libstdc++ 符号版本，以及 macOS 的依赖、部署版本和加载路径随 CI 验证产物保存；
这些结果核验完成前，不声明更老系统兼容性。
CI 暂不恢复或保存跨任务的 Bazel 编译缓存，每个矩阵任务独立构建；本地下载缓存与目标架构的编译目录分离。
