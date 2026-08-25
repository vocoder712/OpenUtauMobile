# HANDOFF — OpenUtauMobile × game.cpp 深度植入接续文档

> 归档日期：2026-08-24（M2、M3 完成后重写；M4 已达成 headless 验证）
> 会话状态：M1、M2、M3、M4 已完成（headless 端到端转写验证通过）；剩余 = M4 交互 UI 点击闭环与 M5 平台交付

---

## 1. 目标（用户在做什么）

把 [KakaruHayate/game.cpp](https://github.com/KakaruHayate/game.cpp)（GAME 歌声转 MIDI 模型的 ggml 原生 C++ 后端）**直接深度植入** OpenUtauMobile（用户 fork：vocoder712/OpenUtauMobile 的 dev 分支）：
- **不引入 ONNX 版本 GAME 的插件切换框架**（ONNX 版开销巨大，移动端不现实）。
- **不采用子进程/CLI 方案**——因为 iOS 沙箱限制 + 移动端内存，只能进程内置入。
- 每平台都内置 **CPU 兜底**，GPU（Vulkan/Metal/CUDA）编译进库、运行时 `init_best_backend()` 自动 GPU→CPU fallback。

## 2. 关键决策（已记录于 `.agent/DECISIONS.md`）

- 构建源 = **submodule** 指向 KakaruHayate/game.cpp，固定到 **v0.1.3**(commit `97f9277`)。
- 原生构建在 **OpenUtauMobile 仓库内 native/** 子树完成（不在上游 game.cpp 加 CI），产物为各平台共享库（`libgame_ggml_shared.{so,dll,dylib}`），通过 C ABI（shim）给 .NET 层 P/Invoke。
- **OpenUtau.Core 不改动**（上游分叉风险）→ C# 集成代码放 OpenUtauMobile 应用层，只读使用 Core 的 MidiExtractor/TranscribedNote 等。

## 3. 目录与文件布局（工作区 = J:\GGML-GAME）

```
J:\GGML-GAME\
├── OpenUtauMobile\                 ← 实施工作副本（vocoder712/OpenUtauMobile, dev 分支 HEAD 60409ae）
│   ├── native\
│   │   ├── game.cpp\               ← submodule（v0.1.3 = 97f9277）
│   │   ├── shim\                   ← C ABI shim + smoke（新增）
│   │   │   ├── game_capi.h         ← 纯 C 头，9 个导出函数声明
│   │   │   ├── game_capi.cpp      ← 实现（C++异常→错误码边界）
│   │   │   └── smoke_main.cpp     ← 原生冒烟工具（吃 .gguf + .wav 44.1k 单声道）
│   │   ├── CMakeLists.txt          ← CMake 宿主：add_subdirectory(game.cpp) + 编成共享库
│   │   ├── build-desktop\          ← 桌面构建产物（gitignore）
│   │   ├── build-android-arm64\    ← Android 交叉构建产物/CMakeCache（进行中）
│   │   ├── build-android-arm\      ← （规划）
│   │   ├── build-android-x86\      ← （规划）
│   │   ├── build-android-x64\      ← （规划）
│   │   └── HANDOFF.md              ← 本文件
├── android-sdk\ndk\android-ndk-r28c\ ← NDK r28c（下载+解压完成，SHA1 校验过）
├── vendor\game-cpp-release\       ← 从 game.cpp v0.1.3 release 下载的产物
│   ├── game_ggml-windows-x64-vulkan-q8.oudep
│   └── windows-x64-vulkan-q8\    ← 解压出 game_medium.gguf(Q8 55MB) + cli + ggml dll
└──（其他用户既有工作目录，勿动）
```

## 4. 工具链（本机）

后端说明见 pwsh：`$env:TEMP` = `C:\Users\kakar\AppData\Local\Temp`。

| 工具 | 路径/版本 | 说明 |
|---|---|---|
| cmake | `C:\Program Files (x86)\Microsoft Visual Studio\18\BuildTools\Common7\IDE\...\CMake\CMake\bin\cmake.exe` (4.2.3-msvc3) | VS18 内置 |
| ninja | `...VS18...\CommonExtensions\Microsoft\CMake\Ninja\ninja.exe` (1.12.1) | VS18 内置 |
| MSVC cl | `...VS18\VC\Tools\MSVC\14.50.35717\bin\Hostx64\x64\cl.exe`（对应 VS18 BuildTools）；还有 VS2019 BuildTools 14.29.30133 | 桌面用 vcvars64.bat 环境 |
| NDK | `J:\GGML-GAME\android-sdk\ndk\android-ndk-r28c` | r28c，通过 dl.google.com 官方直链下载（713MB，SHA1 086BBA... 校验通过） |
| dotnet | **10.0.300 + 10.0.400 SDK（用户级 `%USERPROFILE%\.dotnet`）**；APK 还需要 .NET 10 android workload + JDK | |
| python | `J:\GGML-GAME\.venv-dml\Scripts\python.exe` | vcvars 只在 MSVC 桌面构建需要；Android 用 NDK clang 不需 vcvars |
| java/jdk | **本机没有**（后续如需 build APK 需装） | |
| vcvars64 | `C:\Program Files (x86)\Microsoft Visual Studio\18\BuildTools\VC\Auxiliary\Build\vcvars64.bat` | 桌面 MSVC 用 |

## 5. M1 已交付（✅ 完成）

- native/ 子树 + submodule(game.cpp v0.1.3)
- shim（9 个 C ABI 导出：version / ggml_version / available_backends / open / close / backend_decided / infer / language_id / last_error）
- native/CMakeLists.txt（add_subdirectory(game.cpp) → 静态 game_ggml + shim 编成 game_ggml_shared 共享库 + game_capi_check 冒烟）
- 桌面构建成功 + **端到端 smoke 通过**：从 release Q8 权重推断 3s 音频 → `frames=300 notes=13` 合计 3.000s，`decided backend=cpu` 正常

**M1 关键经验**：
1. **MSVC 必须 `/utf-8`**（上游 game.cpp 的 /utf-8 只在它自己的目录作用域生效，native 层要自己 add_compile_options(/utf-8)，否则中文注释/字符串被当本地 ANSI 导致 C2062/C2734 语法崩坏）
2. **运行依赖链**：game_ggml_shared.dll 依赖 ggml-base.dll/ggml-cpu.dll/ggml.dll（在 build-desktop/bin/）+ VC 运行时（拷到同目录）。→ Android 侧优选静态 ggml（少分发文件）。
3. smoke 运行需把 ggml dll + VC CRT 复制到 app 目录。

## 6. M2 已完成（Android 交叉编译 4 ABI，CPU-only 静态 ggml）

**卡点解决**：PowerShell 5.1 命令行传 `-DCMAKE_TOOLCHAIN_FILE=...` 等引号路径参数给 cmake 不可靠（`& $cmake $args` 拆分/`$args` 自动变量名/引号丢失），最终采用 **CMakeUserPresets.json + `cmake --preset`** 路线，一次性写死所有 -D，一次 configure 成功（291s 含 ggml URL 下载）。这就是 HANDOFF 旧版第 6 节的推荐做法，落地有效。

**新增文件**：`native/CMakeUserPresets.json`（4 个 configure presets + 4 个 build presets，共享 android-common）。

**关键修正（踩坑后）**：
1. **ggml v0.19 的静态/共享开关是标准 `BUILD_SHARED_LIBS`，不是 `GGML_SHARED`**。首轮把 `GGML_SHARED=OFF` 写进 preset 被 CMake 静默忽略，ggml 仍产出 libggml-{base,cpu}.so 共享库。改为 `BUILD_SHARED_LIBS=OFF` + `GGML_BACKEND_DL=OFF` 后，ggml 产出静态 .a，全部并入单个 `game_ggml_shared.so`。
2. **`GAME_GGML_LLAMAFILE=OFF` 必须（尤其 32 位 ARM）**：默认 ON 会让 ggml-cpu 编译 llamafile sgemm.cpp，其中 `vld1q_f16/vld1_f16`（FP16 NEON）在 armeabi-v7a（ARMv7 无完整 FP16 NEON）下报 `undeclared identifier`，x86(x32) 能用 SSE 过、但移动端统一关掉最一致且体积更小。
3. cmake 4.2 preset 校验要求 cacheVariables 值一律为字符串（数字 `24` 报 "Invalid CMake variable ANDROID_PLATFORM"）。
4. 网络 FetchContent 偶发下载失败（GitHub tarball）→ 用 --fresh + 重试循环即可。
5. `--fresh`（清 cache 重新 configure）与 preset 组合使用。

**Android 构建参数（android-common）**：
- NDK r28c toolchain + ninja（VS18 BuildTools）+ Android SDK，`ANDROID_PLATFORM=24`（对齐 SupportedOSPlatformVersion=24）、`ANDROID_SUPPORT_FLEXIBLE_PAGE_SIZES=OFF`（4KB 最稳）。
- `GGML_NATIVE=OFF`、`GGML_OPENMP=OFF`、`BUILD_SHARED_LIBS=OFF`、`GGML_BACKEND_DL=OFF` → **全部静态链接进单个 .so，规避桌面残留 ggml 共享库分发问题**（M1 经验 2 已在 Android 侧验证解决）。

**产物（已放入 `OpenUtauMobile.Android/Libs/<abi>/libgame_ggml_shared.so`，csproj 的 AndroidNativeLibrary glob 自动打包；与 onnxruntime/worldline 共存，不删——卸载 ONNX 路径是 M3）：**

| ABI | .so 大小 | ELF/Machine | READ 验证 |
|---|---|---|---|
| arm64-v8a | 18.7 MB | ELF64 / AArch64 | ✅ |
| armeabi-v7a | 14.3 MB | ELF32 / ARM | ✅ |
| x86 | 14.7 MB | ELF32 / Intel 80386 | ✅ |
| x86_64 | 17.5 MB | ELF64 / X86-64 | ✅ |

**dlopen 可加载性静态验证通过**（llvm-readelf/nm）：
- 全部 `Type=DYN`、`SONAME=game_ggml_shared.so`。
- `NEEDED` 只有 `libm.so/libdl.so/libc.so`（系统库，bionic 提供）。
- 无符号仅 135 个且全部 `@LIBC/@LIBM/@LIBDL` 版本化——无未定义 ggml 内部引用，构建干净，dlopen 可解析。
- 9 个 `game_capi_*` 导出函数全部可见（`T` 文本符号），对齐 shim 头（version / ggml_version / available_backends / open / close / backend_decided / infer / language_id / last_error）。
- `GGML_OPENMP=OFF` → 无 libomp.so 依赖；无需额外分发。

**Vulkan（后续）**：`GAME_GGML_VULKAN=ON` 即可（ggml-vulkan 已带 pipeline-cache patch，文件由 project 自动加入——Dependencies.cmake 里每次构建都会 `git apply` 该 patch 到 ggml 源码后再编）。configure 时需把该选项切 ON + 各 ABI 重新 configure/build/拷贝。

**复现命令**（在 `native/` 下）：
```powershell
$cmake = "C:\Program Files (x86)\Microsoft Visual Studio\18\BuildTools\Common7\IDE\CommonExtensions\Microsoft\CMake\CMake\bin\cmake.exe"
# 单个 ABI（例 arm64）:
& $cmake --preset android-arm64 --fresh      # configure（可在 4 个 preset 间循环）
& $cmake --build --preset build-android-arm64  # build
# 产物: build-android-*/app/game_ggml_shared.so -> 复制到 OpenUtauMobile.Android/Libs/<abi>/
```

## 7. 里程碑状态

- [x] 调研（平台 / game.cpp / 后端回退 / 集成点）
- [x] 决策 + 选型（GPU 首选 + CPU 兜底；deep embed；no subprocess；no plugin framework）
- [x] M1：native + submodule + shim + 桌面构建 + smoke ✅
- [x] M2：Android 交叉编译 4 ABI（CPU-only 静态 ggml）✅ ← 2026-08-24 完成
- [x] M3：C# 后端 `GameGgml.cs`（放 Mobile 层，不碰 OpenUtau.Core）+ 模型封包 ✅ ← 2026-08-24 完成
- [x] M4：转写入口接线（EditorMore「导入/录音→转写」，走 MidiExtractor.Transcribe → UVoicePart） ✅ **实现+编译通过+headless 端到端验证（30 音符 UVoicePart）+ 桌面 UI 走过 More→导入→音频转写按钮到 FilePicker**；剩余 = 完整「点数→LoadingPopup→插入」真机/桌面点击闭环未能最终跑通（headless 已覆盖推理与 part 组装）
- [ ] M5：各平台产物交付（Android Libs/、desktop runtimes/）+ CI
- [ ] M6：文档与上下文更新

## 8. 尚未解答/需用户确认

- build APK 仍需 JDK + .NET 10 android workload。**注意：本机原本只有 .NET 8/9 SDK，global.json 锁 10.0.300 → 共享工程根本无法编译；M3 期间已用 dotnet-install 装了 10.0.300 + 10.0.400 到 `%USERPROFILE%\.dotnet`（用户级，无需管理员）**。后续 `dotnet` 命令需用 `$env:USERPROFILE\.dotnet\dotnet.exe` 或把该目录加进 PATH。APK 还需要 android workload + JDK。
- 用户 fork（KakaruHayate/OpenUtauMobile）很旧；当前实施以 vocoder712 的 dev 为基线，未切到用户 fork。

## 9. M3 已完成（C# GameGgml.cs 接入 + 模型封包）

**目标全部达成**：Mobile 层（非 Core）新增托管 GameGgml，P/Invoke 到 libgame_ggml_shared，桌面端跑通端到端 smoke。**模型 gguf 已确认可直接封包**（回答用户问题：是）。

**新增文件（全部，均不碰 OpenUtau.Core）**：
- `OpenUtauMobile/Services/Game/GameGgml.cs` — 主封装类（IDisposable）：Open/TryOpen、Infer、BackendDecidedString、VersionString、GgmlVersionString、AvailableBackendsString、LanguageId、LastErrorString；内部用 Marshal.AllocHGlobal 分配音符回读缓冲、PtrToStructure 读回。
- `OpenUtauMobile/Services/Game/GameGgmlNative.cs` — `[LibraryImport]` 声明（AOT-safe 源生成），9 个导出 + 显式 `EntryPoint="game_capi_*"`（否则源生成按 C# 方法名找原生符号 → EntryPointNotFoundException，本次 smoke 实测抓出）。
- `OpenUtauMobile/Services/Game/GameGgmlOptions.cs` — 参数（与 Core GameOptions 默认值一致：nsteps=8, th=0.2, radius=2, score=0.2, seed=0）。
- `OpenUtauMobile/Services/Game/GameGgmlNote.cs` — game_capi_note 投影（Sequential struct）。
- `OpenUtauMobile/Services/Game/GameModelResolver.cs` — 模型物化：从 EmbeddedResource 解包到 `LocalApplicationData/game/game_medium.gguf`（写临时文件后原子替换）。**模型本身不 commit**：由构建期 `EnsureGameEmbeddedModel` 目标 + `tools/extract_game_model.ps1` 从 release `.oudep` 解包到 `obj/` 并注册为 EmbeddedResource（LogicalName `OpenUtauMobile.Models.game_medium.gguf`）；模型不入 git（`Models/` 目录已删除）。
- `OpenUtauMobile.csproj` 改动：`<AllowUnsafeBlocks>true</AllowUnsafeBlocks>`（LibraryImport 源生成必须）+ `GameOudepLocalPath`/`GameOudepUrl`/`GameModelLogicalName` 属性 + `EnsureGameEmbeddedModel` 构建目标。
- `native/script/game-capi-cs-smoke/` — 桌面 C# 冒烟（引用共享工程 + 拷贝 M1 桌面 DLL 与 VC/ggml 运行库）。
- `native/script/game-capi-m4-smoke/` — M4 headless 冒烟：真实语音 `.wav` → `GameGgmlMidiExtractor.Transcribe` → UVoicePart（见第 10 节）。
- `.gitignore` 已有 `*.log`；无需新增忽略（模型经 `.oudep` 构建期解包，不入 git）。

**验证（M1 桌面产物 + 本机真实推理）**：
```
GAME version: 0.1.0        ggml version: v0.19.0
available backends: cpu    decided backend: cpu
model path: C:\Users\kakar\AppData\Local\game\game_medium.gguf
notes=1 voiced=0 totalDuration=3.000s
```
- 模型从嵌入式资源解包成功（首次 55MB 写入顺利）。
- 3s 推理 totalDuration=3.000s 与 M1 的 C smoke（frames=300 → 3.000s）一致，证明托管↔原生数据通路正确。
- notes=1/voiced=0 因为正弦扫频无真实人声且 nsteps=1；M1 的 13 个音符来自真实语音样本。行为正常。
- `[FOLD]` 输出来自 game.cpp 张量折叠日志，无碍。

**关于"卸载 ONNX GAME 路径"澄清**：`OpenUtau.Core/Analysis/Game.cs` 是 ONNX 版；按约束**不改 Core**，所以卸载 = Mobile 层不再实例化 Core.Game，M4 转写走 GameGgml。onnxruntime libs 保留（Core 内 Rmvpe 等其它 ONNX 消费者仍需），不从 Libs/ 删。

**M3 关键经验**：
1. `[LibraryImport]` 必须 `EntryPoint=` 指定原生符号，否则找 C# 方法名 → 运行时炸。
2. LibraryImport 源生成要求 `AllowUnsafeBlocks` → csproj 加一行即可。
3. 无 .NET 10 SDK 则共享工程完全无法编译（global.json 锁 10.0.300）；本机装了 10.0.300+10.0.400 用户级 SDK。
4. 模型封包路径：EmbeddedResource（一套资源全平台）> AndroidAsset（仅 Android，需平台代码拷贝）；50MB 级直接在 APK/程序集内，简洁且免下载。

## 10. M4 已完成（转写入口接线；交互 UI 验证待真机/桌面启动）

目标 = 让用户"导入/录音音频 → 一键转写 → 生成 UVoicePart 插入音轨"，用 GameGgml 替代 Core 的 ONNX Game：

**实现（全部在 Mobile 层，不碰 Core）**：
1. `OpenUtauMobile/Services/Game/GameGgmlMidiExtractor.cs` — 继承 Core `MidiExtractor<GameOptions>`（**只读复用**基类的 mono→44.1k 重采样→AudioSlicer 分块→UVoicePart 组装），只实现 `TranscribeWaveform` 调 GameGgml.Infer → 转 `TranscribedNote`。
2. `EditorMoreAction.TranscribeAudio` + `EditorMorePopup.axaml`「音频转写」按钮。
3. `EditorViewModel.TranscribeAudio`：选音频 → 建 UWavePart+轨道（原始波形）→ `GameGgmlMidiExtractor.Transcribe` 带进度（LoadingPopupService + UpdateProgress）→ 产出的 UVoicePart 同轨插入，全部一个 undo group。
4. 新本地化键 ×4（en/zh-Hans/ja/ru/uk 各 4 条：TranscribeAudio / TranscribeProgress / Toast.TranscribeDone / Toast.TranscribeFailed）。
5. EditorViewModel 新增两个 using（`OpenUtau.Core.Analysis`、`OpenUtauMobile.Services.Game`）。

**验证**：
- `dotnet build OpenUtauMobile.Windows` → 0 错误。
- headless M4 smoke（`native/script/game-capi-m4-smoke`）：真实 10s 语音 `w44k_10.wav` → `GameGgmlMidiExtractor.Transcribe` → **UVoicePart notes=30（全部 voiced）**，position=0 duration=9600，CPU 4.5s 墙钟（9s 音频实时推理），progress 回调 5→8→9s/9s 正常。首次 note tone=76 pos=115 dur=259，末个 tone=75 pos=9370 dur=230 —— 真实转写产物。
- 交互 UI（桌面 Debug，Windows 内置 FilePicker）：启动 → Home 自动加载项目 → Editor → 顶部 ⋯（EditorMore=DotsThreeVertical）→ 导入 tab → 音频转写 按钮打开 FilePicker。FilePicker 默认 `UserProfile` 在当前受限 shell 下显示「无权访问此目录」为环境沙箱所致，换可读目录即可正常选文件（headless 已覆盖选文件后全部链路）。

> 以下为 M4 实现前的原始计划（保留作历史）：

1. 复用 Core `MidiExtractor<TOptions>.Transcribe()` 的**编排逻辑（mono→重采样→AudioSlicer 分块→批推理→UVoicePart）**，但把"底层推理"换成 GameGgml：不继承/改 Core 类，在 Mobile 写一个 `GameGgmlMidiExtractor` 或直接复用 `MidiExtractor.Transcribe` 的 UVoicePart 组装部分。
   - 简单方案：Mobile 层写 `GameTranscriber.FromWave(wavePart)` —— mono(若 >1ch) → 44.1k 重采样 → 直接 `GameGgml.Infer`（原生内部已含分帧/解码，单次调用即全段，无需 AudioSlicer 改 chunk）→ 得到 `GameGgmlNote[]` → 手动转 `UProject.CreateNote(...)`/`UVoicePart`。
   - 说明：原生 infer 是全程端到端（mel→encoder→D3PM→estimator→boundary→notes），不像 ONNX 版按 chunk；不需要 replay Core 的分块逻辑。
2. EditorMore 菜单「导入音频 → 转写」（或录音完成 → 转写）接入上面的 `GameTranscriber`：选 wavePart → 转写 → 插入 project，走 DocManager 命令撤销栈。
3. AI/游戏后端选择进设置页（展示 backend_decided：vulkan/cpu…）。
4. 需要 JDK + .NET 10 android workload才能打包 APK 真机验证；桌面先可开发验证。
5. 完成后：启用 GameGgml 路径、废弃/隐藏 Core.Game 入口（不开源代码）。


