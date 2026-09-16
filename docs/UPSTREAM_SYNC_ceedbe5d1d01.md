# OpenUtau ceedbe5d1d01 同步兼容审计

## 冻结输入

- OPUM 基线：`5ca1fa71216bbda08ca294de82671b8ecd33df3e`。
- OPU 目标：`ceedbe5d1d019ab8f170240edd3532a71960e68c`（全程不再 fetch OPU）。
- CoreVersion：`0.1.570.4-alpha-ceedbe5d1d01`。
- 旧 Core split：`1ad190215d5383c630e2ba5db5fbf4fce49e070c`。
- 新 Core split：`f629157a4d9076b4c231cdfb152089a6df76cba5`。
- 旧/新 Plugin split：`4fd474d3b6dcfd6e41d1215c5872730b9e6a8f9f`，本次无上游变更。
- CPP：首次同步，无旧 OPUM 目录或正式祖先；使用独立 cache 的完整 split，再用官方 subtree add --squash 建立祖先。

## 三方比较和全部既有定制

比较旧 split → OPUM 基线列出完整定制清单；比较旧 split → 新 split 定位上游变更；最终比较 OPUM 基线 → 合并结果逐文件核对保留情况。以下覆盖全部 45 个 Core 定制文件，Plugin 只有项目警告设置定制。没有删除既有定制。

| Core 相对路径 | 决策 |
| --- | --- |
| `.editorconfig` | 保留；本次上游未修改该文件，已核对 blob 与 OPUM 基线一致。 |
| `Analysis/Crepe/Resources.Designer.cs` | 保留；本次上游未修改该文件，已核对 blob 与 OPUM 基线一致。 |
| `Api/Phonemizer.cs` | 保留完整描述符校验；已覆盖上游实际描述符取值修复。用户已确认冲突方案。 |
| `Api/PhonemizerRunner.cs` | 保留；本次上游未修改该文件，已核对 blob 与 OPUM 基线一致。 |
| `Audio/MiniAudioOutput.cs` | 保留；本次上游未修改该文件，已核对 blob 与 OPUM 基线一致。 |
| `Classic/ClassicSinger.cs` | 保留头像延迟加载；适配上游 FreeMemory 后 loaded=false。 |
| `Classic/ClassicSingerLoader.cs` | 保留；本次上游未修改该文件，已核对 blob 与 OPUM 基线一致。 |
| `Classic/Data/Resources.Designer.cs` | 保留；本次上游未修改该文件，已核对 blob 与 OPUM 基线一致。 |
| `Classic/SharpWavtool.cs` | 保留；本次上游未修改该文件，已核对 blob 与 OPUM 基线一致。 |
| `Classic/VoicebankInstaller.cs` | 保留；本次上游未修改该文件，已核对 blob 与 OPUM 基线一致。 |
| `Commands/MixCommands.cs` | 保留；本次上游未修改该文件，已核对 blob 与 OPUM 基线一致。 |
| `Commands/Notifications.cs` | 保留；本次上游未修改该文件，已核对 blob 与 OPUM 基线一致。 |
| `DiffSinger/DiffSingerBasePhonemizer.cs` | 保留；本次上游未修改该文件，已核对 blob 与 OPUM 基线一致。 |
| `DiffSinger/DiffSingerSpeakerEmbedManager.cs` | 保留；本次上游未修改该文件，已核对 blob 与 OPUM 基线一致。 |
| `DocManager.cs` | 保留；本次上游未修改该文件，已核对 blob 与 OPUM 基线一致。 |
| `Editing/NoteBatchEdits.cs` | 保留；本次上游未修改该文件，已核对 blob 与 OPUM 基线一致。 |
| `Enunu/EnunuSinger.cs` | 保留；本次上游未修改该文件，已核对 blob 与 OPUM 基线一致。 |
| `Format/Commonnote.cs` | 保留；本次上游未修改该文件，已核对 blob 与 OPUM 基线一致。 |
| `G2p/Data/Resources.Designer.cs` | 保留；本次上游未修改该文件，已核对 blob 与 OPUM 基线一致。 |
| `Neutrino/NeutrinoConfig.cs` | 保留；本次上游未修改该文件，已核对 blob 与 OPUM 基线一致。 |
| `Neutrino/NeutrinoInferenceUtil.cs` | 保留；本次上游未修改该文件，已核对 blob 与 OPUM 基线一致。 |
| `Neutrino/NeutrinoPhoneme.cs` | 保留；本次上游未修改该文件，已核对 blob 与 OPUM 基线一致。 |
| `Neutrino/NeutrinoPhonemizer.cs` | 保留；本次上游未修改该文件，已核对 blob 与 OPUM 基线一致。 |
| `Neutrino/NeutrinoRenderer.cs` | 保留；本次上游未修改该文件，已核对 blob 与 OPUM 基线一致。 |
| `Neutrino/NeutrinoSinger.cs` | 保留；本次上游未修改该文件，已核对 blob 与 OPUM 基线一致。 |
| `OpenUtau.Core.csproj` | 采用 DryWetMidi.Nativeless；保留 native/build 排除、RootNamespace、现有包版本和 ONNX Managed 分层。用户已确认冲突方案。 |
| `PackageManager.cs` | 保留；本次上游未修改该文件，已核对 blob 与 OPUM 基线一致。 |
| `Pipeline/PhraseSource.cs` | 保留；本次上游未修改该文件，已核对 blob 与 OPUM 基线一致。 |
| `PlaybackManager.cs` | 保留；本次上游未修改该文件，已核对 blob 与 OPUM 基线一致。 |
| `Render/RenderEngine.cs` | 保留；本次上游未修改该文件，已核对 blob 与 OPUM 基线一致。 |
| `Render/RenderPhrase.cs` | 保留；本次上游未修改该文件，已核对 blob 与 OPUM 基线一致。 |
| `Render/Renderers.cs` | 保留；本次上游未修改该文件，已核对 blob 与 OPUM 基线一致。 |
| `SignalChain/ExportAdapter.cs` | 保留；本次上游未修改该文件，已核对 blob 与 OPUM 基线一致。 |
| `SignalChain/Fader.cs` | 保留；本次上游未修改该文件，已核对 blob 与 OPUM 基线一致。 |
| `SignalChain/MasterAdapter.cs` | 保留；本次上游未修改该文件，已核对 blob 与 OPUM 基线一致。 |
| `SignalChain/MixFxSource.cs` | 保留；本次上游未修改该文件，已核对 blob 与 OPUM 基线一致。 |
| `SignalChain/PlaybackMeters.cs` | 保留；本次上游未修改该文件，已核对 blob 与 OPUM 基线一致。 |
| `SignalChain/PlaybackMixer.cs` | 保留；本次上游未修改该文件，已核对 blob 与 OPUM 基线一致。 |
| `SingerManager.cs` | 保留；本次上游未修改该文件，已核对 blob 与 OPUM 基线一致。 |
| `Ustx/USinger.cs` | 保留；本次上游未修改该文件，已核对 blob 与 OPUM 基线一致。 |
| `Util/NotePresets.cs` | 保留准确的错误日志；合入 VibratoPreset 无参构造以支持反序列化。 |
| `Util/PathManager.cs` | 保留；本次上游未修改该文件，已核对 blob 与 OPUM 基线一致。 |
| `Util/Preferences.cs` | 保留字段级异常隔离和全部移动端配置；随上游移除 DiffSingerLocalRetaking，Mobile 无引用。 |
| `Vogen/Data/VogenRes.Designer.cs` | 保留；本次上游未修改该文件，已核对 blob 与 OPUM 基线一致。 |
| `Voicevox/VoicevoxSinger.cs` | 保留；本次上游未修改该文件，已核对 blob 与 OPUM 基线一致。 |

Plugin 的 `OpenUtau.Plugin.Builtin.csproj` 保留既有 NoWarn，整个 Plugin tree 已核对与 OPUM 基线一致。

### 重点语义

- `UPart.cs` 采用上游轨道表达式描述符解析和未知曲线清理。用户明确授权 Ustx 按上游更改同步。`USinger` 的既有 Neutrino/头像接口定制不与本次协议修改冲突，继续保留。
- `GetParentVoiceColor` 保留 OPUM 空值、Options 类型、有限数、索引范围及返回值空值检查；上游改用实际解析描述符的修复已包含于该实现。
- Core 继续仅引用 ONNX Managed 1.29.0，平台工程提供 native 包；不把上游基于宿主 OS 的 GPU 包选择带回共享 Core。DryWetMidi 改用上游 Nativeless 7.2.0，同时保留 native/build 排除设置。
- Preferences 保留 ValidatePreference 的字段级隔离、移动端默认值、GAME、主题、自动保存、编辑器等字段；上游移除的 LocalRetaking 不是 OPUM 定制，Mobile 与扩展渲染器未引用它。
- `DiffSingerRenderer` 跟随上游始终局部重预测；`DiffSingerG2pPhonemizer` 跟随上游修复无语言 ID 时的映射，与既有配置路径回退及 speaker embedding 定制并存。
- Neutrino 的全部六个独有文件、RenderPhone.noteIndex 赋值、RenderPhrase.availableLeadingMs、PositionOverridden 保留；现有 xsyAvailable 流程保留。
- RenderPhraseEvents、GetSuggestions、EnsureAvatarLoaded、PartRenderedNotification 的接口及调用端已交叉检查；现有混音、计量、波形通知、同步 UI 命令派发、剪贴板注入与外部渲染器注册继续保留。
- 四组资源 Designer 的 ResourceManager 名称和 Core 的空 RootNamespace 配套保留。
- CPP 仅导入原样上游源码和 Bazel 配置到 native/upstream_cpp。本次没有改变应用已有 native 二进制分发或 GAME 构建来源。

## 验证记录

- 未与本次上游变更重叠的 40 个 Core 定制文件：逐一比较 Git blob SHA，全部与 OPUM 基线相同。
- Plugin 完整 tree 与 OPUM 基线相同。
- UPart、DiffSingerRenderer、DiffSingerG2pPhonemizer 的 blob 与本次上游完全一致。
- CPP split：`9cf2c4326772a7de68b29fbe7cda1d8f37beb8d6`。
- CPP 上游/split/正式目录三个 tree 均为 `55c1af200c4a7020000ab1a8441b1c0b134d59d8`。
- 三个远程 cache 和对应 synthetic 已实时验证：
  - Core cache：`8cb6e24004345eb778de369909fa860b7c9edc62`。
  - Plugin cache：`5d3fc6697a507c1d4e839516cd0cd76e6f0f87fa`。
  - CPP cache：`14168d10c6fa497338c569033d65e680bbbc0cf9`。
- 同步指南 34 个 PowerShell 代码块通过语法解析。

### 本地构建（2026-09-16）

全部 dotnet 构建均设置 `AVALONIA_TELEMETRY_OPTOUT=1`。

| 检查 | 结果 |
| --- | --- |
| `dotnet build OpenUtau.Plugin.Builtin/OpenUtau.Plugin.Builtin.csproj --nologo -v:minimal` | exit 0；35 个警告、0 错误 |
| `dotnet build OpenUtauMobile/OpenUtauMobile.csproj --nologo -v:minimal` | exit 0；30 个警告、0 错误 |
| `dotnet restore OpenUtauMobile.Android/OpenUtauMobile.Android.csproj -r android-arm64 -p:Configuration=Release` | exit 0；实际完成依赖还原 |
| `dotnet build OpenUtauMobile.Android/OpenUtauMobile.Android.csproj -c Release -f net10.0-android36.0 --no-restore -p:RuntimeIdentifier=android-arm64` | exit 0；70 个警告、0 错误 |
| `python native/verify-android-runtime.py <signed-apk> android-arm64` | PASS：ABI、必需 native 库、GAME 对齐及许可证 |
| CPP `worldline`（Bazel 6.6.0 / MinGW64 GCC 14.2.0） | exit 0；88 actions；输出 `bazel-bin/worldline/libworldline.so`，未替换应用原有二进制 |
| 上游已有 `worldline/classic:timing_test` | 未能编译：上游测试引用了当前快照中不存在的 `worldline/model/model_utils.h`；0 个测试执行。未改写测试或源码掩盖问题 |

Core 的 project.assets.json 已确认解析到 `Melanchall.DryWetMidi.Nativeless/7.2.0`。Android 首次沙箱构建被阻止执行 NDK clang；沙箱外重试通过。构建仍提示应用原有 `Libs/arm64-v8a/libworldline.so` 非 16 KB 页面大小（XA0141），本次仅同步 CPP 源码，未改变该已有二进制。

MSVC 编译器位于自定义目录，但该安装缺少 Bazel 所需的 vcvarsall.bat 和常规头文件目录。最终使用已安装的 MinGW64 验证 CPP：临时 Bazel toolchain 把 compiler 和 builtin include 根目录指向实际 `C:/app/mingw64`；忽略默认 MSVC rc，显式启用 Bzlmod、cc_shared_library 和 C++17；仅通过 C 编译参数为 libpyin 补充其所需的 min 宏。成功命令的关键参数为：

```text
bazel --ignore_all_rc_files build worldline --enable_bzlmod --experimental_cc_shared_library --compiler=mingw-gcc --cxxopt=-std=c++17 --conlyopt=-Dmin(a,b)=((a)<(b)?(a):(b)) --lockfile_mode=off --override_repository=bazel_tools~cc_configure_extension~local_config_cc=<临时toolchain目录>
```

本地日志保存在 `artifacts/upstream-sync-ceedbe5d1d01/`，不纳入源码。临时工具链配置也仅用于本机验证；没有修改导入的 CPP 文件或系统工具安装。CPP 的 `.patch` 文件含上游原有的空格和空行，按 tree 一致性要求原样保留；排除该原样目录后 `git diff --check` 通过。

### 云检查

以本次同步 PR 的实时检查结果为准。只有必需检查通过，才使用 Create a merge commit 合入 dev。
