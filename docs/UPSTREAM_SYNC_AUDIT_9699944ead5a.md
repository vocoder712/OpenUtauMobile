# OpenUtau 9699944ead5a 同步兼容审计

## 固定快照

- OPU：`9699944ead5a3b27b59bdf5a35f73fada8c11b7b`
- OPU 标签：`0.1.570.4-alpha`
- CoreVersion：`0.1.570.4-alpha-9699944ead5a`
- OPUM 基线：`cb8668955ee042e2aedc3eb94f178c785d433a39`
- 旧 Core split：`3f58ccb44522b9d6560fb7cbe0a3a6b0261ae320`
- 新 Core split：`1ad190215d5383c630e2ba5db5fbf4fce49e070c`
- 旧 Plugin split：`1e74269e69f9d721238f966e4e09841743b56375`
- 新 Plugin split：`4fd474d3b6dcfd6e41d1215c5872730b9e6a8f9f`
- 已备份并校验远程 Core cache：`7b7ce7958f9f86f1e1b63bcec4c0e0ad5c421b57`
- 已备份并校验远程 Plugin cache：`969bdb05b3a6575054e6d0cc4eb7032cbfc0b771`

## 审计方法与授权

分别比较旧 split → OPUM 基线、旧 split → 新 split、OPUM 基线 → 合并结果。
以下表格覆盖旧 Core 与基线之间全部 43 个差异文件。标为保留的文件与基线 Git blob 一致；适配项逐项审查三方差异及调用端。无定制被批准删除或主动删除。

用户确认 USTX 格式以上游为准，允许同步 UCurve、UPart、UTrack；这三个文件与冻结上游完全一致。其余 Ustx 文件保持基线；USinger 中既有 Neutrino 和头像 API 扩展继续保留。没有添加新的 USTX 序列化字段，未改变上游格式版本。

用户已逐项批准 9 个 Core 冲突的合并方案。重新合并复用了同一冻结 SHA，没有再次获取 opu。

## Core 定制清单

| 文件（相对 OpenUtau.Core） | 结果 | 审查说明 |
| --- | --- | --- |
| `.editorconfig` | 保留 | 保留 Core 独立格式配置。 |
| `Analysis/Crepe/Resources.Designer.cs` | 保留 | 保留资源生成器命名空间与资源定位修复。 |
| `Api/Phonemizer.cs` | 适配 | 保留音色描述符、空值、有限数值和索引保护；覆盖上游新增的索引边界检查。 |
| `Api/PhonemizerRunner.cs` | 保留 | 保留无效声库提前退出和结构化异常日志。 |
| `Audio/MiniAudioOutput.cs` | 保留 | 仅既有文件末尾格式差异。 |
| `Classic/ClassicSinger.cs` | 适配 | 保留按需头像加载；纳入上游 SessionLock、Dispose 和 FreeMemory。 |
| `Classic/ClassicSingerLoader.cs` | 保留 | 保留 Neutrino 声库识别。 |
| `Classic/Data/Resources.Designer.cs` | 保留 | 保留资源生成器命名空间与资源定位修复。 |
| `Classic/SharpWavtool.cs` | 保留 | 保留公共类型，供外部渲染插件调用。 |
| `Classic/VoicebankInstaller.cs` | 保留 | 保留 SingerType 写入和临时文件原子替换配置。 |
| `Commands/MixCommands.cs` | 保留 | 保留混音参数、静音、独奏、效果器命令和撤销合并。 |
| `Commands/Notifications.cs` | 保留 | 保留通知静默标记和 WaveformReadyNotification。 |
| `DiffSinger/DiffSingerBasePhonemizer.cs` | 保留 | 保留根目录/dsdur 配置回退与 speaker 校验。 |
| `DiffSinger/DiffSingerSpeakerEmbedManager.cs` | 保留 | 保留移动端张量构造、空 speaker 防护和告警去重。 |
| `DocManager.cs` | 适配 | 保留批量编辑同步主线程分发和日志定制；纳入 revision、快照失效和异步乐句构建。 |
| `Editing/NoteBatchEdits.cs` | 适配 | 保留现有音高回写逻辑；纳入 voiced 掩码以跳过静音帧。 |
| `Enunu/EnunuSinger.cs` | 保留 | 保留按需头像加载。 |
| `Format/Commonnote.cs` | 适配 | 采用上游 Json；保留由 Mobile 处理剪贴板的边界，移除无用 using。 |
| `G2p/Data/Resources.Designer.cs` | 保留 | 保留资源生成器命名空间与资源定位修复。 |
| `Neutrino/NeutrinoConfig.cs` | 保留 | 保留模型配置。 |
| `Neutrino/NeutrinoInferenceUtil.cs` | 保留 | 保留推理实现。 |
| `Neutrino/NeutrinoPhoneme.cs` | 保留 | 保留音素数据与分帧逻辑。 |
| `Neutrino/NeutrinoPhonemizer.cs` | 保留 | 保留音素器。 |
| `Neutrino/NeutrinoRenderer.cs` | 适配 | 保留推理、timing 和缓存语义；WaveSource 写盘改为上游 Wave.WriteMono16Wav。 |
| `Neutrino/NeutrinoSinger.cs` | 保留 | 保留声库、头像与 GetSuggestions 实现。 |
| `OpenUtau.Core.csproj` | 适配 | 保留 RootNamespace、NoWarn、依赖版本、DryWetMidi native/build 排除和 ONNX Managed 1.29.0；增加 SharpJaad/AAC 0.1.1。语言版本继续继承仓库 preview。 |
| `PackageManager.cs` | 适配 | 保留文件处理定制；注册表 JSON 改用上游 System.Text.Json。 |
| `PlaybackManager.cs` | 适配 | 保留 AudibleSamplePosition、LiveMixer、Meters、实时刷新；加入 MixPlanner、分离取消源和循环播放 HoldWhenUnready。 |
| `Render/RenderEngine.cs` | 适配 | 用槽位源接入 PlaybackMixer，保留轨道电平、效果器、总线及播放时预备静音轨；等待异步乐句在项目锁之外执行。导出使用独立 planner。 |
| `Render/RenderPhrase.cs` | 适配 | 迁移到 PhraseSource；把分片索引映射为乐句内 noteIndex，保留 positionOverridden、availableLeadingMs；保留上游 XSY 快照和 variant 缓存隔离。 |
| `Render/Renderers.cs` | 适配 | 保留外部渲染器发现和 Neutrino；纳入上游按 ID 缓存。 |
| `SignalChain/ExportAdapter.cs` | 保留 | 保留公共导出适配器。 |
| `SignalChain/Fader.cs` | 保留 | 保留声道 balance 定制。 |
| `SignalChain/MasterAdapter.cs` | 适配 | 保留播放位置时间段、电平和混音器；合并 HoldWhenUnready，等待静音仍记录位置。 |
| `SignalChain/MixFxSource.cs` | 保留 | 保留实时效果器和采样观测。 |
| `SignalChain/PlaybackMeters.cs` | 保留 | 保留轨道/总线电平采样。 |
| `SignalChain/PlaybackMixer.cs` | 保留 | 保留轨道效果器快照、后级静音、总线和观测接口。 |
| `SingerManager.cs` | 适配 | 保留卸载、扫描列表实体化和资源释放；增加上游旧声库 Dispose。 |
| `Ustx/USinger.cs` | 保留 | 保留既有 Neutrino 类型及 EnsureAvatarLoaded API；未改动文件。 |
| `Util/PathManager.cs` | 适配 | 保留入口程序集路径异常防护；缓存清理由旧 LiveWaveformCache/Mix 改为上游 MixPlanner.Clear。 |
| `Util/Preferences.cs` | 适配 | 采用上游 Json 并新增 Recent/Favorite 清理；保留每字段校验异常隔离、全部 Mobile 字段与默认值。 |
| `Vogen/Data/VogenRes.Designer.cs` | 保留 | 保留资源生成器命名空间与资源定位修复。 |
| `Voicevox/VoicevoxSinger.cs` | 保留 | 保留按需头像加载。 |

## Plugin 和 Mobile

- Plugin 对新 split 的差异仅为原有 csproj 的 NoWarn 配置，与旧定制一致；音素器和数据文件均直接来自同一 OPU SHA。
- HifiSampler 两处旧 WaveSource 缓存写入改为上游 Wave.WriteMono16Wav，继续使用 44.1 kHz 单声道 16-bit WAV。
- RenderPhraseEvents、GetSuggestions、EnsureAvatarLoaded、PartRenderedNotification 的现有调用端通过 Plugin/Mobile 编译检查；没有残留对已删除 WaveSource 的代码调用。
- Mobile NotesCanvas 读取 renderPhrases 已使用分片锁；异步构建结果由上游调度回主线程发布。新增生命周期内的 RenderView 订阅，并在绘制时登记当前分片，确保异步结果就绪后重绘最终音高；卸载控件时释放订阅。
- Core 仅引用 ONNX Managed 1.29.0；Android 保留从官方 AAR 分发原生库及 ExcludeAssets，iOS 保留目标项目中的 ONNX 1.29.0。

## 验证

- Plugin：`dotnet build OpenUtau.Plugin.Builtin/OpenUtau.Plugin.Builtin.csproj --nologo -v:minimal`，exit 0，73 warnings / 0 errors。
- Mobile：`dotnet build OpenUtauMobile/OpenUtauMobile.csproj --nologo -v:minimal`，exit 0；追加 NotesCanvas 重绘适配后再次构建通过。
- Android restore：`dotnet restore OpenUtauMobile.Android/OpenUtauMobile.Android.csproj`，exit 0。
- Android ARM64 Release（NotesCanvas 重绘适配前，最终提交由 PR CI 再验证）：`dotnet build OpenUtauMobile.Android/OpenUtauMobile.Android.csproj -c Release -f net10.0-android36.0 -p:RuntimeIdentifier=android-arm64`，exit 0，9 warnings / 0 errors。沙箱内首次构建因 NuGet 缓存写权限失败，获准在沙箱外重试后通过。
- Android 构建保留现有 XA0141 警告：`libworldline.so` 未满足 16 KB 页面大小要求；本次未更换该原生库。
- 构建均设置 `AVALONIA_TELEMETRY_OPTOUT=1`，未以 --no-restore 代替依赖还原。
- 当前仓库没有现成的测试 csproj；未新增测试套件。尚未做设备音频、声库推理和 USTX 文件读写的运行时验证。
- Windows 本机无法验证 iOS；PR 要求 Android ARM64 和 iOS Intel Simulator Mono AOT 两项 CI。全部云检查通过前不合并、不宣称完成验证。
- 本地构建日志：`artifacts/upstream-sync-9699944ead5a/`（Git 忽略）。

PR 必须使用 Create a merge commit，以保留 subtree ancestry。
