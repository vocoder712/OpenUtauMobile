# 下一会话接续文档（2026-08-24 归档）

> 用途：本文件是**跨会话唯一权威接续入口**。下一会话 agent 先读本文件 + native/HANDOFF.md。
> 本文的"已完成"项均以实际工具结果为准（已构建/已编译/已运行验证），"未完成"项是真实缺口。

---

## 0. 一句话状态

- M1 ✅、M2 ✅、M3 ✅ 全部完成并验证；**M4 代码实现 + 编译 + headless 端到端验证**（真实 10s 语音 → `GameGgmlMidiExtractor.Transcribe` → 30 音符 UVoicePart，4.5s CPU）。桌面 UI 已用 UI 自动化走过 More→导入→音频转写 → FilePicker 打开；FilePicker 默认 `UserProfile` 在当前受限 shell 报「无权访问此目录」为环境沙箱所致。完整「选文件→LoadingPopup→插入」桌面点击流程未再全跑（headless 已覆盖推理→part 组装）。模型已改为构建期从 game.cpp release `.oudep` 解出、不入 git。

## 1. 已完成的真相（全部有工具结果佐证）

### M2 — Android 交叉编译 4 ABI（完成）
- `native/CMakeUserPresets.json`：4 个 configure preset（android-arm64/arm/x86/x64 继承 android-common）+ 4 个 build preset；选项：NDK r28c toolchain、ninja、CMAKE_BUILD_TYPE=Release、ANDROID_PLATFORM=24、ANDROID_SUPPORT_FLEXIBLE_PAGE_SIZES=OFF、GGML_NATIVE=OFF、GGML_OPENMP=OFF、**BUILD_SHARED_LIBS=OFF**（非 GGML_SHARED）、GGML_BACKEND_DL=OFF、GAME_GGML_LLAMAFILE=OFF（armeabi-v7a 必须）、加速度全关。
- 4 ABI `.so` 已构建并放入 `OpenUtauMobile.Android/Libs/<abi>/libgame_ggml_shared.so`（csproj 的 AndroidNativeLibrary glob 自动打包）：
  - arm64-v8a 18.7MB / armeabi-v7a 14.3MB / x86 14.7MB / x86_64 17.5MB
- readelf 验证：全部 `Type=DYN`、`SONAME=game_ggml_shared.so`、`NEEDED` 只 libm/libdl/libc、无符号全版本化到 bionic、9 个 `game_capi_*` 导出可见。
- 关键经验：PowerShell 5.1 传 `-DCMAKE_TOOLCHAIN_FILE=` 不可靠 → 用 preset；cmake 4.2 preset 的 cacheVariables 必须字符串；ggml 用 **BUILD_SHARED_LIBS**（无 GGML_SHARED 变量）；独立构建目录 + 网络重试。

### M3 — C# GameGgml 封装 + 模型（完成，桌面 smoke 端到端验证）
- `OpenUtauMobile/Services/Game/`：`GameGgml.cs`（open/infer/backend/version）、`GameGgmlNative.cs`（9 个 LibraryImport，**显式 EntryPoint="game_capi_*"**）、`GameGgmlOptions.cs`、`GameGgmlNote.cs`、`GameModelResolver.cs`（EmbeddedResource → LocalApplicationData/game/game_medium.gguf）。
- 共享 `OpenUtauMobile.csproj`：`AllowUnsafeBlocks=true`（LibraryImport 必需）、`EnsureGameEmbeddedModel` 构建目标（从 release `.oudep` 解 gguf 到 obj 并注册为 EmbeddedResource）+ **`tools/extract_game_model.ps1`** + `GameOudepLocalPath`/`GameOudepUrl`/`GameModelLogicalName` 属性。
- 模型来源（按用户要求，不优雅但已实现）：**不入 git**；构建时从本机 `J:\GGML-GAME\vendor\game-cpp-release\game_ggml-windows-x64-vulkan-q8.oudep`（zip，内含 game_medium.gguf 57,754,848 B）解出；没有则从 URL `https://github.com/KakaruHayate/game.cpp/releases/download/v0.1.3/game_ggml-windows-x64-vulkan-q8.oudep` 下载（HEAD 已验证 200）。删除 `Models/` 目录。
- **桌面 C# smoke（native/script/game-capi-cs-smoke/）端到端通过**：GAME 0.1.0 / ggml v0.19.0 / backends cpu / decided cpu / infer 3s → totalDuration=3.000s；删掉本地模型后能重新从程序集解包（57.7MB）→ 验证封包链路完整。
- 本机装了 .NET SDK 10.0.300 + 10.0.400 到 `%USERPROFILE%\.dotnet`（无管理员）。后续 `dotnet` 必须用 `$env:USERPROFILE\.dotnet\dotnet.exe` 或加 PATH。

### M4 — 转写入口接线（实现 + 编译通过；未交互运行验证）
- `OpenUtauMobile/Services/Game/GameGgmlMidiExtractor.cs` **存在且编译通过**（这是本会话里"写文件失败→重新真实写入→build 0 错误"的最终状态）。
- `EditorMoreAction.TranscribeAudio` + `EditorMorePopup.axaml`「音频转写」按钮 + `EditorViewModel` case + `TranscribeAudio()` 方法（LoadingPopupService 进度 → Transcribe → UVoicePart 同轨插入，单 undo group）+ 新增两个 using（`OpenUtau.Core.Analysis`、`OpenUtauMobile.Services.Game`）。
- 5 个 resx（en/zh-Hans/ja/ru/uk）各加 4 键：TranscribeAudio / TranscribeProgress / Toast.TranscribeDone / Toast.TranscribeFailed。
- `dotnet build OpenUtauMobile.csproj` 最终 **0 错误、已成功生成**。→ 全部 M4 代码真实落盘且一致。

## 2. 真实缺口（必须完成的）

1. **交互 UI 验证 M4**：✅ headless 端到端验证完成（真实 10s 语音 → Transcribe → 30 音符 UVoicePart，4.5s CPU）+ 桌面 UI 自动化走过 More→导入→音频转写→FilePicker 打开。遗留 = 完整「选文件→LoadingPopup 进度→插入」桌面/真机点击闭环未最终跑（headless 已覆盖推理与 part 组装）。桌面运行命令：在 `OpenUtauMobile.Windows` 项目 `dotnet build -t:Run -c Debug`（用 `%USERPROFILE%\.dotnet\dotnet.exe`）。
2. **HANDOFF.md 第 8 节 dotnet 9.0.317 描述**：✅ 已改为 10.0.300+10.0.400 用户级 SDK；APK 仍需 .NET 10 android workload + JDK。
3. **HANDOFF.md 第 9/10 节 Models/ 残留描述**：✅ 已核对并修复（不再提 Models/game_medium.gguf，改为 `.oudep` 构建期解包）。
4. **DECISIONS.md M3 模型决策条目**：✅ 已同步为「构建期 .oudep 解包、不入 git」（保留被取代的原条目 + 新条目）。

## 3. 明确的坑（避免重踩）

- **工具调用不可靠性**：本会话后半段多次出现 "写文件/编辑返回成功但实际未落盘" 的幻觉。**每次 write/edit 后用 `Test-Path` 或 grep 实体核验**，以磁盘为准，不轻信工具结果文本。
- 模型解包脚本独立可测：`powershell -NoProfile -ExecutionPolicy Bypass -File OpenUtauMobile\tools\extract_game_model.ps1 -Oudep <路径> -Target <目标>`。
- `cmake --preset` 要在 `native/` 目录下执行；cacheVariables 全部字符串。
- LibraryImport 必须带 `EntryPoint`，否则运行 EntryPointNotFoundException。

## 4. 未决 / 后续里程碑

- M5：Android Libs 已就位（4 ABI）；desktop runtimes/ 交付 + CI（把 4 .so + Windows dll 纳入产物与自动化）。
- M5 需决定建包路径：装 JDK + `dotnet workload install android`（用 10.0.300 SDK）→ `OpenUtauMobile.Android` 出 APK，验证 4 ABI 在真机/模拟器 dlopen + 转写。
- M4 之后可加：设置页显示 `backend_decided`（Vulkan/CPU）——目前 smoke 已验证该 API，未接 UI。
- 全程遵守：不修改 `OpenUtau.Core` / `OpenUtau.Plugin.Builtin`；不引入 player subprocess。

## 5. 下一会话 Prompt（可整段复制，粘贴给 agent）

"""
继续 OpenUtauMobile × game.cpp 深度植入，从 M4 交互验证继续。
【先读】读 J:\GGML-GAME\OpenUtauMobile\native\NEXT_SESSION.md 与 native\HANDOFF.md——这是权威接续文档，含全部已完成/缺口/坑。

【已验证前提】
- M1 桌面 build+smoke 完成；native/shim 9 个 C ABI 导出正常。
- M2 完成：native/CMakeUserPresets.json 4 ABI preset（BUILD_SHARED_LIBS=OFF、GGML_LLAMAFILE=OFF、NDK r28c、ninja），4 个 libgame_ggml_shared.so 已放 OpenUtauMobile.Android/Libs/<abi>/（readelf 验证过）。
- M3 完成：OpenUtauMobile/Services/Game/GameGgml*.cs 全套 + LibraryImport(EntryPoint) + 模型构建期从 game.cpp release .oudep 解包（tools/extract_game_model.ps1），桌面 C# smoke 端到端通过（3.000s）。
- .NET SDK 10.0.300 装在 %USERPROFILE%\.dotnet（dotnet 命令必须用它或加 PATH）。

【M4 当前缺口 → 你的任务】
1. 交互运行验证：本机跑 OpenUtauMobile.Windows（不需要 JDK），EditorMore → 音频转写 → 选音频 → 看进度/结果。修一切运行时问题。
2. 若有问题：排查 GameGgmlMidiExtractor（继承 Core.MidiExtractor<GameOptions>，只实现 TranscribeWaveform）与 EditorViewModel.TranscribeAudio（LoadingPopup 进度、undo group、track 插入）。
3. 同步 .agent/DECISIONS.md 的过时模型决策条目（→ 改 构建期 .oudep 解包），修 HANDOFF 里过时的 .NET 9/Models 残留描述。
4. 做完后：若需真机验证，安装 JDK +（用 10.0.300 SDK）dotnet workload install android，构建 OpenUtauMobile.Android 出 APK（Libs 已有 4 ABI .so），在模拟器/真机验证 dlopen + 转写。

【约束】不修改 OpenUtau.Core / OpenUtau.Plugin.Builtin；C# 集成全放 Mobile 层（OpenUtauMobile/）；GPU(若有) Vulkan 编译进库、运行时自动 fallback CPU，不引入子进程。
报告：运行验证结果 + 修的 bug + 遗留。"""
