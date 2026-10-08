# OpenUtau 8945ee388322 同步审计

## 冻结来源

- OPUM 基线：9499701eeab3b3671f2882a4130f35470145f9bc。
- 上次上游：9fe0e923aac9d14475f1473a7d86d9f042df979d。
- 本次上游：8945ee38832203caafccbd85ab4375783dc630f1。
- CoreVersion：0.1.572.4-alpha-8945ee388322。

| 单元 | 旧 split | 本次 split | 已核验远程 cache |
| --- | --- | --- | --- |
| Core | ab6fc4f11973beb46d7b1b6b014264a9d695af19 | ffadcacab60d1ae5ae32f502c45ea4cd37ea1f02 | 1e5c35274a16238e0be85b4a3dd68e99ae9a466c |
| Plugin | e4e99e8c1dc1caa419ba5bef21637ad39cc163b1 | e4e99e8c1dc1caa419ba5bef21637ad39cc163b1 | c211caf53a6b9e8e27657ff62c507a61a06f8edf |
| CPP | da63d770ccfbd9538d39bc77c2baedd0cdf72e7d | da63d770ccfbd9538d39bc77c2baedd0cdf72e7d | 19ecb976e4350566493540f77da2c0dd5fa94174 |

三个 cache 均 fast-forward 推送到 opu-sync；远程 cache SHA 与本地一致，三个实际 split 均有远程 synthetic 分支。本次只新增 Core synthetic 分支。

## 上游改动与兼容性

上游 Core 修改 ClassicRenderer、ResamplerManifest、VoicebankFiles、Onnx 四个文件；Plugin 和 CPP 无变化。正式合并使用 subtree merge --squash，没有冲突。

- Classic 渲染临时文件复制增加 IResampler 参数，manifest.files 支持自定义附属文件后缀。已检查全部 CopySourceTemp / CopyBackMetaFiles 调用，均在 ClassicRenderer 中同步更新；Mobile 层没有旧签名调用。
- ONNX 两种 CreateSession 重载和 CoreML 回退显式维持并释放 SessionOptions，避免 native 会话创建过程中选项被 GC 提前终结。
- ONNX 原有 DirectML 无设备隐藏、无效设备索引回退、顺序执行及禁用 memory pattern 均保留。比较旧上游到旧 OPUM、新上游到合并结果，定制差异相同。
- Core 项目、四类资源命名空间、包版本和 DryWetMidi 的 native/build 排除逐文件保持；ONNX Managed 依赖与宿主按平台分发方式没有变更。
- Preferences 字段级异常隔离、移动端默认值及全部 Mobile 特定选项保持原内容。
- Neutrino 六个文件、RenderPhone.noteIndex、RenderPhrase.availableLeadingMs 及相关缓存输入保持；上游 PhonemeSource 的 xsyAvailable 参数也保持。
- RenderPhraseEvents、GetSuggestions、EnsureAvatarLoaded、PartRenderedNotification 的声明与调用已检查；此次未改变这些 API。PhraseRenderedNotification 无匹配。
- Ustx 在本次同步中无任何修改；既有定制保持原内容。

### 上游问题的兼容修复

VoicebankFiles.GetMetaFiles 将 IResampler 转为 ExeResampler 后直接访问 exeResampler.Manifest。当内置 Worldline/Hifisampler 搭配外部 wavtool、进入 ClassicRenderer.RenderExternal 时，该转换返回 null 并抛出 NullReferenceException。使用修复前的真实 Core 程序集调用 CopySourceTemp 已复现两种内置 resampler 的异常；外部 ExeResampler 自定义附属文件用例正常。经用户批准，删除具体类型转换，改为直接读取 IResampler.Manifest.files，并用模式匹配处理 manifest/files 为 null 的情况。默认附属文件列表和上游新增的自定义后缀支持保持。

## 全部既有定制清单

从旧 split 到 OPUM 基线列出所有差异，再比较基线到正式合并结果。下表“保留”表示文件 blob 完全一致；“适配”表示 ONNX 自动合并同时保留原定制和上游修复。没有删除任何定制。

| 文件 | 结论 |
| --- | --- |
| OpenUtau.Core/.editorconfig | 保留：与 OPUM 基线 blob 一致 |
| OpenUtau.Core/Analysis/Crepe/Resources.Designer.cs | 保留：与 OPUM 基线 blob 一致 |
| OpenUtau.Core/Api/Phonemizer.cs | 保留：与 OPUM 基线 blob 一致 |
| OpenUtau.Core/Api/PhonemizerRunner.cs | 保留：与 OPUM 基线 blob 一致 |
| OpenUtau.Core/Audio/MiniAudioOutput.cs | 保留：与 OPUM 基线 blob 一致 |
| OpenUtau.Core/Classic/ClassicSinger.cs | 保留：与 OPUM 基线 blob 一致 |
| OpenUtau.Core/Classic/ClassicSingerLoader.cs | 保留：与 OPUM 基线 blob 一致 |
| OpenUtau.Core/Classic/Data/Resources.Designer.cs | 保留：与 OPUM 基线 blob 一致 |
| OpenUtau.Core/Classic/PresampWatcher.cs | 保留：与 OPUM 基线 blob 一致 |
| OpenUtau.Core/Classic/SharpWavtool.cs | 保留：与 OPUM 基线 blob 一致 |
| OpenUtau.Core/Classic/UnixWavtool.cs | 保留：与 OPUM 基线 blob 一致 |
| OpenUtau.Core/Classic/VoicebankInstaller.cs | 保留：与 OPUM 基线 blob 一致 |
| OpenUtau.Core/Classic/YamlWatcher.cs | 保留：与 OPUM 基线 blob 一致 |
| OpenUtau.Core/Commands/MixCommands.cs | 保留：与 OPUM 基线 blob 一致 |
| OpenUtau.Core/Commands/Notifications.cs | 保留：与 OPUM 基线 blob 一致 |
| OpenUtau.Core/DiffSinger/DiffSingerBasePhonemizer.cs | 保留：与 OPUM 基线 blob 一致 |
| OpenUtau.Core/DiffSinger/DiffSingerSpeakerEmbedManager.cs | 保留：与 OPUM 基线 blob 一致 |
| OpenUtau.Core/DocManager.cs | 保留：与 OPUM 基线 blob 一致 |
| OpenUtau.Core/Editing/NoteBatchEdits.cs | 保留：与 OPUM 基线 blob 一致 |
| OpenUtau.Core/Enunu/EnunuSinger.cs | 保留：与 OPUM 基线 blob 一致 |
| OpenUtau.Core/Format/Commonnote.cs | 保留：与 OPUM 基线 blob 一致 |
| OpenUtau.Core/G2p/Data/Resources.Designer.cs | 保留：与 OPUM 基线 blob 一致 |
| OpenUtau.Core/Neutrino/NeutrinoConfig.cs | 保留：与 OPUM 基线 blob 一致 |
| OpenUtau.Core/Neutrino/NeutrinoInferenceUtil.cs | 保留：与 OPUM 基线 blob 一致 |
| OpenUtau.Core/Neutrino/NeutrinoPhoneme.cs | 保留：与 OPUM 基线 blob 一致 |
| OpenUtau.Core/Neutrino/NeutrinoPhonemizer.cs | 保留：与 OPUM 基线 blob 一致 |
| OpenUtau.Core/Neutrino/NeutrinoRenderer.cs | 保留：与 OPUM 基线 blob 一致 |
| OpenUtau.Core/Neutrino/NeutrinoSinger.cs | 保留：与 OPUM 基线 blob 一致 |
| OpenUtau.Core/OpenUtau.Core.csproj | 保留：与 OPUM 基线 blob 一致 |
| OpenUtau.Core/PackageManager.cs | 保留：与 OPUM 基线 blob 一致 |
| OpenUtau.Core/Pipeline/PhraseSource.cs | 保留：与 OPUM 基线 blob 一致 |
| OpenUtau.Core/PlaybackManager.cs | 保留：与 OPUM 基线 blob 一致 |
| OpenUtau.Core/Render/RenderEngine.cs | 保留：与 OPUM 基线 blob 一致 |
| OpenUtau.Core/Render/RenderPhrase.cs | 保留：与 OPUM 基线 blob 一致 |
| OpenUtau.Core/Render/Renderers.cs | 保留：与 OPUM 基线 blob 一致 |
| OpenUtau.Core/SignalChain/ExportAdapter.cs | 保留：与 OPUM 基线 blob 一致 |
| OpenUtau.Core/SignalChain/Fader.cs | 保留：与 OPUM 基线 blob 一致 |
| OpenUtau.Core/SignalChain/MasterAdapter.cs | 保留：与 OPUM 基线 blob 一致 |
| OpenUtau.Core/SignalChain/MixFxSource.cs | 保留：与 OPUM 基线 blob 一致 |
| OpenUtau.Core/SignalChain/PlaybackMeters.cs | 保留：与 OPUM 基线 blob 一致 |
| OpenUtau.Core/SignalChain/PlaybackMixer.cs | 保留：与 OPUM 基线 blob 一致 |
| OpenUtau.Core/SingerManager.cs | 保留：与 OPUM 基线 blob 一致 |
| OpenUtau.Core/Ustx/UPart.cs | 保留：与 OPUM 基线 blob 一致 |
| OpenUtau.Core/Ustx/USinger.cs | 保留：与 OPUM 基线 blob 一致 |
| OpenUtau.Core/Util/NotePresets.cs | 保留：与 OPUM 基线 blob 一致 |
| OpenUtau.Core/Util/Onnx.cs | 适配：原 DirectML 定制保留，上游会话资源修复合入 |
| OpenUtau.Core/Util/PathManager.cs | 保留：与 OPUM 基线 blob 一致 |
| OpenUtau.Core/Util/Preferences.cs | 保留：与 OPUM 基线 blob 一致 |
| OpenUtau.Core/Vogen/Data/VogenRes.Designer.cs | 保留：与 OPUM 基线 blob 一致 |
| OpenUtau.Core/Voicevox/VoicevoxSinger.cs | 保留：与 OPUM 基线 blob 一致 |
| OpenUtau.Plugin.Builtin/ENtoJAPhonemizer.cs | 保留：与 OPUM 基线 blob 一致 |
| OpenUtau.Plugin.Builtin/OpenUtau.Plugin.Builtin.csproj | 保留：与 OPUM 基线 blob 一致 |
| OpenUtau.Plugin.Builtin/SyllableBasedPhonemizer.cs | 保留：与 OPUM 基线 blob 一致 |
| OpenUtau.Plugin.Builtin/ThaiVCCVPhonemizer.cs | 保留：与 OPUM 基线 blob 一致 |

除既有定制清单外，本次新增 VoicebankFiles.cs 的上述最小兼容修复。

## Native 源码与构建接入

CPP 无本地定制。冻结上游的 cpp、实际 CPP split 的 tree、HEAD:native/upstream_cpp 三者均为 ad5946971a956af2889166fe245a310a15ab701a。

实际构建已由 WorldlineNative.targets 接入 native/worldline/build.py，并从 native/upstream_cpp 创建隔离构建副本。此次仅更新 upstream-revision.txt；CPP 未变，Bzlmod 依赖、校验和、锁文件、补丁及许可证继续使用现有版本。源码 tree 校验与编译/测试结果分别记录。

## 验证结果

- Plugin：dotnet build OpenUtau.Plugin.Builtin/OpenUtau.Plugin.Builtin.csproj --nologo -v:minimal，exit 0；兼容修复后重建亦 exit 0（16 个现有 Core 告警）。
- Mobile：dotnet build OpenUtauMobile/OpenUtauMobile.csproj --nologo -v:minimal，exit 0；兼容修复后重建亦 exit 0（0 个告警）。第一次沙箱执行因 obj 文件写权限失败，沙箱外重试通过。
- 以上构建均设置 AVALONIA_TELEMETRY_OPTOUT=1，并实际执行默认 restore；本次没有依赖变更。
- Native：python native/worldline/build.py --rid win-x64 --test，exit 0。Bazel 9.2.0 / MSVC 19.51.36256；worldline、continuous_noise、effects 三个已有测试均通过，结果来自有效 Bazel 测试缓存，源码与测试输入未变。
- Native ABI：python native/worldline/verify.py artifacts/worldline-native/win-x64/worldline.dll win-x64 --run，exit 0，实际 F0/codec/config/analysis/synthesis/R1.1 调用、缓冲区哨兵和 64 位噪声种子验证通过。worldline-build.json 的 upstream_commit 为本次冻结完整 SHA。
- Android：按当前 pr-requirements.yml 执行 dotnet restore OpenUtauMobile.Android/OpenUtauMobile.Android.csproj -r android-arm64 -p:Configuration=Release，失败 NETSDK1147（选中的 .NET 10.0.400 未识别到所需 Android workload）；未继续执行 Android 构建、APK 内容验证及设备验收。由 PR 的 Android ARM64 云检查补齐，必需云检查通过前不合并。
- git diff --check 通过；无冲突标记或未合并索引。
- 附属文件复制运行验证：本地一次性 console harness（plans/resampler-copy-check，未新增仓库测试套件）直接调用真实 Core 的 CopySourceTemp / CopyBackMetaFiles。Worldline、Hifisampler、带 YAML 自定义后缀的 ExeResampler、空 files 数组、null files、null manifest 共六个用例全部通过。核对源 WAV 字节、默认 frq/llsm 正向复制及反向回写、自定义 .custom 正向复制及反向回写。修复前同一 harness 的两种内置 resampler 与三个接口边界用例均抛出 NullReferenceException，ExeResampler 用例通过；修复后 0/6 失败。未执行完整外部 wavtool 合成或设备播放验收。