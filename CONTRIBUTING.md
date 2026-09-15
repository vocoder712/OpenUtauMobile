# 参与 OpenUtau Mobile 贡献

**简体中文** | [English](docs/CONTRIBUTING.en-US.md)

感谢你对 OpenUtau Mobile 的关注！

我们欢迎各种形式的贡献，包括代码、Bug 修复、文档、翻译、UI/UX 改进、测试以及技术讨论等。

OpenUtau Mobile 目前仍处于活跃开发阶段，项目架构、API 和平台相关实现可能会不断调整。因此，请尽量保持每次修改的目标明确、范围集中。对于较大的功能或架构调整，建议在投入大量开发工作前先进行讨论。

---

## 贡献方式

你可以通过以下方式参与 OpenUtau Mobile：

* 修复 Bug；
* 实现新功能；
* 优化性能；
* 改进 UI/UX；
* 改善不同平台的支持；
* 完善文档和翻译；
* 在不同设备和平台上测试构建；
* 审查 Pull Request；
* 参与 Issues 和 Discussions 中的技术讨论。

对于较大的新功能或架构调整，建议先通过 Issue 或 Discussion 讨论方案。

这样既可以避免重复开发，也有助于确认修改是否符合项目当前的发展方向。

其它已经计划的开发任务可以在 [TODO](https://docs.qq.com/sheet/DV2NuakZtQW1LZUNS) 中查看。

---

## 搭建开发环境（Windows 电脑）

本节按 **Windows x64 电脑 + Rider** 编写，先启动 Windows 应用，再按需准备 Android。
所有 PowerShell 命令都在仓库根目录执行；`<你的用户名>` 等占位符需替换。
功能、架构和硬件加速的支持情况见 [README 特性矩阵](README.md#feature-matrix)，
GAME 原生构建的维护细节见 [native/game/README.md](native/game/README.md)。

### 1. 安装基础工具

| 工具 | 安装内容与用途 |
| --- | --- |
| [Git for Windows](https://git-scm.com/downloads/win) | 克隆仓库；CMake 也通过 Git 下载固定版本的原生依赖。安装时允许命令行使用 Git。 |
| [.NET SDK](https://dotnet.microsoft.com/download/dotnet/10.0) | 安装 **SDK x64**，仅安装 Runtime 不够。版本以 [global.json](global.json) 为准，当前是 `10.0.400`，允许同一功能带内更新补丁。 |
| [Visual Studio Build Tools](https://visualstudio.microsoft.com/downloads/#build-tools-for-visual-studio) | 在安装器中选择“使用 C++ 的桌面开发”，包含 MSVC x64/x86 编译工具和 Windows SDK。即使使用 Rider，也需要 C++ 编译器来构建 GGML。 |
| [CMake](https://cmake.org/download/) | 安装 3.24 或更新版本，将其 `bin` 目录加入 PATH。安装器提供 PATH 选项；只在 IDE 内可用的 CMake 不一定能被项目构建找到。 |
| [Rider](https://www.jetbrains.com/rider/download/) | 使用支持本仓库 .NET SDK 的版本。Rider 是编辑器和调试器，不代替以上工具链。 |

安装或修改 PATH 后，关闭并重新打开 PowerShell 和 Rider。首次构建需要连接 NuGet 和 GitHub，
会下载并编译 GAME 依赖，耗时比后续增量构建长。模型不用提前下载，也不用手动复制 DLL。

### 2. Fork、克隆并创建开发分支

在 GitHub 上 Fork 仓库，然后执行：

```powershell
git clone https://github.com/<你的用户名>/OpenUtauMobile.git
cd OpenUtauMobile
git remote add upstream https://github.com/vocoder712/OpenUtauMobile.git
git fetch upstream
git switch dev
git pull --ff-only upstream dev
git switch -c feature/你的功能名
```

日常贡献基于 `dev`，向 `dev` 提交 PR。保留自己的未提交修改，再进行分支切换或同步。

在仓库根目录检查工具是否能被找到：

```powershell
git --version
dotnet --version
cmake --version
Get-Command git,dotnet,cmake
$env:AVALONIA_TELEMETRY_OPTOUT='1'
```

`dotnet --version` 应匹配 `global.json`。C++ Build Tools 通常由 CMake 自动发现，普通终端里
找不到 `cl.exe` 本身不代表安装失败。后续命令中的遥测变量只对当前 PowerShell 及其子进程有效；
要让桌面快捷方式启动的 Rider 也继承它，可在 Windows“编辑账户的环境变量”中添加
`AVALONIA_TELEMETRY_OPTOUT=1`，然后重启 Rider。

### 3. 启动 Windows 调试

先用命令行确认目标项目可以恢复和启动：

```powershell
$env:AVALONIA_TELEMETRY_OPTOUT='1'
dotnet restore OpenUtauMobile.Windows/OpenUtauMobile.Windows.csproj
dotnet run --project OpenUtauMobile.Windows/OpenUtauMobile.Windows.csproj -c Debug
```

出现应用窗口即完成第一次启动。这里启动的是 OpenUtau Mobile 的 Windows 宿主。
只做 Windows 开发时，按项目恢复即可；在根目录直接执行 `dotnet restore` 会处理整个解决方案，
可能要求安装 Android、iOS 或浏览器等无关工作负载。

在 Rider 中：

1. 打开根目录的 `OpenUtauMobile.sln`。在设置中搜索 `.NET CLI`，确认使用上述 .NET SDK。
2. 选择 `OpenUtauMobile.Windows` 的运行配置和 `Debug` 配置。若没有自动生成，
   在 **Run → Edit Configurations** 添加 **.NET Project**，启动项目选择 `OpenUtauMobile.Windows`。
3. 启动前构建目标项目及其依赖；只调试 Windows 时，不要将构建整个解决方案作为启动前任务。
4. 在 C# 代码行旁设置断点，点击虫子图标 **Debug**。需要执行到断点所在操作才会暂停。

普通 Windows 调试不必设置 `RuntimeIdentifier` 或 `EnableGameGgml`。
构建会自动生成 `opum_game.dll`，并将对应架构的 worldline 放到输出目录。
默认 Debug 输出位于 `OpenUtauMobile.Windows/bin/Debug/net10.0-windows/`。
如果显式使用 `-r win-x64`，输出会多一层 `win-x64/`；启动时应使用此次构建的目录。

### 4. 准备 Android 环境

Windows 已能启动后，再安装以下 Android 专用工具。Android 的 C++ 编译器由 NDK 提供。

| 组件 | 配置 |
| --- | --- |
| .NET Android workload | 在仓库根目录运行 `dotnet workload install android`；若提示权限不足，使用管理员 PowerShell。升级 .NET SDK 后重新检查 `dotnet workload list`。 |
| JDK | 建议安装 [Microsoft OpenJDK 21](https://learn.microsoft.com/java/openjdk/download)。填写 JDK 根目录，不是 `bin`；Android Studio 自带 JBR 只有版本兼容时才可复用。 |
| [Android Studio](https://developer.android.com/studio) | 用它的 SDK Manager 安装和管理 SDK，也可用 Device Manager 创建模拟器；C# 代码仍在 Rider 调试。 |
| Android SDK | SDK Manager 中安装 Android API 36、Build-Tools 36.0.0、Platform-Tools、Command-line Tools (latest)。具体目标以 [Android 项目](OpenUtauMobile.Android/OpenUtauMobile.Android.csproj) 和安装的 .NET Android 工作负载为准。 |
| Android NDK | 在 SDK Tools 中勾选 Show Package Details，安装 [android-ndk-version.txt](native/game/android-ndk-version.txt) 指定的版本，当前 `28.2.13676358`。 |
| [Ninja](https://github.com/ninja-build/ninja/releases) | 下载 Windows 版本，将 `ninja.exe` 所在目录加入 PATH，确认 `ninja --version` 成功。CMake 也必须在 PATH 中。 |

安装方法也可参考微软的 [.NET Android 依赖说明](https://learn.microsoft.com/dotnet/android/getting-started/installation/dependencies)。
项目当前 CI 使用 JDK 17，本地 JDK 21 已通过构建；不要直接使用未经当前工作负载支持的更高版本。

记下实际安装路径。以下示例采用 SDK 默认位置；**把 JDK 路径换成自己的目录**：

```powershell
$androidSdk = Join-Path $env:LOCALAPPDATA 'Android/Sdk'
$androidNdkVersion = (Get-Content native/game/android-ndk-version.txt -Raw).Trim()
$androidNdk = Join-Path $androidSdk "ndk/$androidNdkVersion"
$javaSdk = 'C:/替换为你的JDK21目录'
$env:JAVA_HOME = $javaSdk
$env:ANDROID_HOME = $androidSdk
& "$javaSdk/bin/java.exe" -version
& "$androidSdk/cmdline-tools/latest/bin/sdkmanager.bat" --licenses
& "$androidSdk/cmdline-tools/latest/bin/sdkmanager.bat" "ndk;$androidNdkVersion"
ninja --version
Test-Path "$androidNdk/build/cmake/android.toolchain.cmake"
```

阅读并接受所需 SDK 许可；最后的路径检查应返回 `True`。若 SDK Manager 安装的命令行工具目录
不同，按实际位置调整 `sdkmanager.bat` 路径。已有独立 NDK 时，直接将 `$androidNdk` 指向它。

在 Rider 设置中搜索 **Android SDK**，将 Android SDK、Android NDK、Java SDK 分别设为上述目录。
SDK 和 NDK 可以在不同磁盘；GAME 构建使用 .NET Android 解析出的 NDK，不要求搬到 SDK 下面。
PowerShell 的变量不会自动写入已经运行的 Rider，务必核对 IDE 中的路径。

### 5. 连接设备并启动 Android 调试

真机需要 Android 7 / API 24 或更新系统。开启开发者选项和 USB 调试，连接数据线，
在手机上允许这台电脑调试；Windows 必要时安装厂商 ADB 驱动。
参见 [Android 设备连接说明](https://developer.android.com/studio/run/device)。
模拟器可从 Android Studio Device Manager 创建并启动；Windows x64 电脑通常选择 x86_64 镜像。

```powershell
& "$androidSdk/platform-tools/adb.exe" devices -l
```

设备状态应是 `device`。`unauthorized` 表示需要在设备上授权；`offline` 表示连接尚不可用。
多台设备时，后续命令用 `-s <设备序列号>` 指定目标，例如：

```powershell
& "$androidSdk/platform-tools/adb.exe" -s <设备序列号> shell getprop ro.product.cpu.abilist
```

`arm64-v8a` 对应 RID `android-arm64`，`x86_64` 对应 `android-x64`，`armeabi-v7a` 对应 `android-arm`。
RID 是目标系统与 CPU 架构的标识；Android 应用按目标设备选择，不能按电脑架构猜测。

可先在命令行构建验证工具链（把 `$androidRid` 改成目标设备的 RID）：

```powershell
$env:AVALONIA_TELEMETRY_OPTOUT='1'
$androidRid = 'android-arm64'
dotnet build OpenUtauMobile.Android/OpenUtauMobile.Android.csproj -c Debug -r $androidRid "-p:AndroidSdkDirectory=$androidSdk" "-p:AndroidNdkDirectory=$androidNdk" "-p:JavaSdkDirectory=$javaSdk"
```

命令会自动恢复对应 RID 的依赖，签名 APK 位于
`OpenUtauMobile.Android/bin/Debug/net10.0-android36.0/<RID>/*-Signed.apk`。
这一步只构建，附加调试器使用 Rider：

1. 选择 `OpenUtauMobile.Android` 的 **Android** 运行配置，构建配置为 `Debug`。
   没有配置时，在 **Run → Edit Configurations → Add → Android** 创建，启动项目选 Android 项目。
2. 部署选择 **Default APK**，目标选择已连接设备或已启动模拟器，保留启动前构建步骤。
3. 设置 C# 断点并点击虫子。Rider 会构建、安装 APK、启动应用并附加调试器。
4. 构建日志里的 SDK、NDK、Java 路径应与设置一致，RID 应匹配设备。

菜单名称可能随 Rider 版本变化，参见 [Rider Android 调试说明](https://www.jetbrains.com/help/rider/Run_Debug_Configuration_Xamarin_Android.html)。
Debug 使用独立应用 ID 和本地调试签名，无需配置 CI 的发布签名密钥。

### 6. 常见问题

| 现象 | 检查与处理 |
| --- | --- |
| 找不到匹配的 .NET SDK | 在仓库根目录运行 `dotnet --version`，按 `global.json` 安装 SDK；核对 Rider 的 .NET CLI 路径。 |
| 要求安装 iOS 等无关 workload | 恢复、构建目标 `.csproj`，检查 Rider 是否将整个解决方案列为启动前构建任务。 |
| `cmake` / `ninja` 找不到 | 在新 PowerShell 中运行 `Get-Command cmake,ninja`；修正 PATH 后重启 Rider。Windows MSVC 构建不要求 Ninja，Android 要求。 |
| 找不到 C++ 编译器 | 检查 Build Tools 的 C++ 桌面开发组件和 Windows SDK；不要将只安装 Rider 视为已经安装编译器。 |
| NDK toolchain not found | 对日志里的 NDK 路径执行 `Test-Path`；确认安装完成，并核对 Rider 的独立 NDK 配置。 |
| Git / FetchContent / NuGet 下载失败 | 检查报错中的下载地址及网络、代理设置，修复后重试构建；IDE 和终端可能继承不同的代理环境。 |
| CMake generator 与旧缓存不一致 | 切换编译器时使用新的 `GameBuildRoot`，例如附加 `-p:GameBuildRoot=artifacts/game-build-msvc`；`dotnet clean` 不清除这里的原生缓存。 |
| `DllNotFoundException` / `BadImageFormatException` | 查看内部异常，核对运行目录和设备架构；重新构建当前目标，不要从另一个 RID 复制库。GGML 模型包不能替代应用原生库。 |
| 运行后提示缺少 GAME 模型 | 安装对应 `.oudep` 模型包，见 [GAME 使用说明](README.md#game-note-extraction)。普通编辑器调试不需要这些模型。 |
| Android 安装失败 | 检查 ADB 状态和 RID；签名冲突时先确认旧应用来源、备份应用数据，再决定是否卸载，勿直接清数据。 |
| Android 16 KB 页面对齐警告 | 参阅 [当前限制](README.md#verification-and-known-limits)；GAME 库已做对齐不等于 APK 中所有第三方库都已兼容。 |

报告调试问题时，请附上目标项目、RID、SDK/NDK/JDK 版本、第一处错误及完整内部异常。
首次构建成功后，正常启动会增量编译，无需预先运行专门的 GGML 构建命令。

---

## 分支说明

### `dev`

`dev` 是 OpenUtau Mobile V2 当前的主要开发和集成分支。

正常贡献都应该基于 `dev` 开发，并向 `dev` 提交 Pull Request。

---

### `master`

`master` 保存 OpenUtau Mobile 的旧版本代码。

它不是 V2 日常开发使用的基础分支。

除非修改明确针对旧版本，否则 Pull Request 不应提交到 `master`。

---

## 代码风格

请遵循项目现有的代码风格和架构设计。

请注意：

* 遵守仓库中的 `.editorconfig`；
* 在条件允许的情况下，将平台无关逻辑与平台相关实现分离；
* 尽量保持修改范围集中，便于审查；
* 修改公共代码时，应考虑其对其他平台和架构的影响。

---

## Commit Message 规范

OpenUtau Mobile 使用轻量的 **Conventional Commits** 风格。

推荐格式：

```text
<type>: <description>
```

常用类型：

```text
feature
fix
performance
refactor
docs
test
build
ci
chore
```

第一行应尽量简短，并直接描述这个 commit **做了什么**。

对于复杂修改，可以在标题后增加正文，解释修改的原因。

例如：

```text
fix(android): 支持 Android 10 及以前的旧存储模型

从 Android 10 开始，应用默认使用分区存储模型。需要申请 MANAGE_EXTERNAL_STORAGE 权限才能访问外部存储的所有文件。

之前的实现没有考虑旧存储模型，导致在 Android 10 及以前版本上无法访问外部存储。
```

如果提交与某个 Issue 或 Pull Request 有关，可以适当引用：

```text
Fixes #123
Refs #456
```

开发过程中的每一个 commit 不要求都达到最终发布质量。 临时 commit 在开发分支中是可以接受的。

Pull Request 最终合并时可以通过 Squash 将它们整理成一个完整的逻辑 commit。

---

# Pull Request 流程

## 1. 同步最新的 `dev`

提交或更新 Pull Request 前：

```bash
git fetch upstream
```

对于只有你自己使用的 Topic Branch，推荐将其 rebase 到最新的 `dev`：

```bash
git rebase upstream/dev
```

如果这个分支之前已经推送到自己的 Fork：

```bash
git push --force-with-lease
```

请优先使用：

```text
--force-with-lease
```

而不是：

```text
--force
```

如果一个分支正在由多名开发者共同使用，请不要在没有沟通的情况下修改它的历史。

---

## 2. 构建和测试

提交 Pull Request 前，请至少确认：

* 依赖可以正常恢复；
* 受影响的项目能够正常构建；
* 如果存在相关测试，应运行对应测试；
* 对受影响功能进行了必要的人工验证；
* 平台相关修改尽可能在对应平台上测试。

你不需要为了提交一个普通 Pull Request，在本地构建所有支持的平台。

但你的修改不应已知地破坏其他平台。

Pull Request 合并前，相关 CI 检查应该通过。

---

## 3. 保持 PR 范围集中

一个 Pull Request 通常应该只完成一个逻辑上的修改。

尽量避免在同一个 PR 中混入：

* 无关 Bug 修复；
* 大规模无关格式化；
* 与本次功能无关的依赖升级；
* 不必要的大型重构。

大型修改如果可以划分为多个相互独立的逻辑步骤，通常应该拆分成多个 Pull Request。

这样更容易：

* Review；
* 测试；
* 定位问题；
* 回滚修改。

---

## 4. 编写清晰的 PR 描述

Pull Request 描述应尽量说明：

* 修改了什么；
* 为什么需要修改；
* 如果实现方式不明显，简要说明实现方法；
* 如何测试；
* 影响哪些平台。

对于明显的 UI 修改，建议附上：

* 截图；
* GIF；
* 屏幕录制。

如果存在相关 Issue、PR、Discussion 或 commit，也应进行引用。

例如：

```text
Fixes #123
Refs #456
```

如果这个 Pull Request 会使另一个 Pull Request 不再需要，也请明确说明。

---

## 5. Target `dev`

正常 Pull Request 应设置：

```text
base: dev
```

除非修改明确针对旧版本，否则不要向 `master` 提交 Pull Request。

---

# Merge、Rebase 和 Squash

这三种 Git 操作解决的问题不同，不应混为一谈。

---

## Rebase

Rebase 主要用于将 Topic Branch 更新到最新的 `dev`，同时保持较为线性的提交历史。

典型操作：

```bash
git fetch upstream
git rebase upstream/dev
```

---

## Squash

Squash 用于把多个 commit 合并成更少、更完整的逻辑 commit。

例如一次开发中的多个 commit 适合在 PR 最终合并时整理为：

```text
fix(android): 修复 Android 10 及以下存储权限异常
```

对于普通功能和 Bug 修复 Pull Request，通常推荐使用：

**Squash and merge**

这样可以让 `dev` 的历史保持简洁，每个 PR 对应一个清晰的逻辑修改。

---

## Merge Commit

不推荐使用

---

## 默认策略

对于普通贡献，推荐流程：

1. 从最新的 `dev` 创建 Topic Branch；
2. 正常开发和提交 commit；
3. 需要时将 Topic Branch rebase 到最新 `dev`；
4. 创建指向 `dev` 的 Pull Request；
5. 最终默认使用 **Squash and merge** 合并。

---

# AI 辅助开发

推荐使用 Agent 编程工具进行编码。

但是，无论代码是否由 AI 生成，**提交者需要对自己提交的代码负责**。

提交 AI 辅助修改前，请至少确保：

* Review 实际修改的代码；
* 理解被修改部分的行为；
* 删除 AI 产生的无关修改；
* 成功构建并进行必要测试；
* 确认代码符合项目已有的架构和风格；
* 不要提交 Secret、Token、凭据或私人数据；
* 不要提交无权重新分发的受版权保护内容。

即使一份大规模 AI 生成的修改能够编译，如果：

* 修改目的不明确；
* 引入大量无关变化；
* 没有经过必要验证；
* 提交者无法解释其行为；

也可能不会被接受。

AI 工具可以开发，但不能替代代码审查和工程判断。

---

# 同步主线内核

OpenUtau Mobile 会定期从上游 OpenUtau 仓库同步代码。

**上游同步属于仓库维护操作，与普通 Feature 开发不同。**

请勿直接将任意 OpenUtau upstream commit 手动 merge 到 `dev`。

完整流程请参阅：

[Upstream Synchronization Guide](docs/UPSTREAM_SYNC.md)

---

## UI 本地化

OpenUtau Mobile 的 UI 本地化资源位于 `dev` 分支的以下目录：

```text
OpenUtauMobile/Assets/Lang/
```

当前支持：

| 语言 | Language Code | 资源文件 |
| --- | --- | --- |
| 简体中文 | `zh-Hans` | `Strings.zh-Hans.resx` |
| English | `en` | `Strings.en.resx` |
| 日本語 | `ja` | `Strings.ja.resx` |
| Русский | `ru` | `Strings.ru.resx` |
| Українська | `uk` | `Strings.uk.resx` |

语言资源文件采用：

```text
Strings.{language-code}.resx
```

的命名方式。

例如：

```text
Strings.en.resx
Strings.zh-Hans.resx
Strings.ja.resx
```

### 修改已有翻译

如果发现有错误或缺失的翻译，请修改对应的 `.resx` 文件。

每条 UI 文本由一个固定的 key 和对应翻译组成：

```xml
<data name="Editor.Save" xml:space="preserve">
  <value>Save</value>
</data>
```

简体中文中的相同 key：

```xml
<data name="Editor.Save" xml:space="preserve">
  <value>保存</value>
</data>
```

翻译时：

- **不要修改 `name` 中的 key**；
- 只修改 `<value>` 中的用户可见文本；
- 不要随意增加、删除或重命名已有 key；
- 不同语言文件中的 key 应尽量保持一致；

### 保留格式参数

部分字符串包含运行时参数，例如：

```xml
<value>{0} parts copied</value>
```

翻译时必须保留：

```text
{0}
```

例如：

```xml
<value>已复制 {0} 个分片</value>
```

如果字符串中存在：

```text
{0}
{1}
{2}
```

等占位符，翻译后必须保留所有占位符。可以根据目标语言语序调整它们的位置，但不能删除或修改编号。

同样，不应随意修改字符串中的：

- 文件扩展名；
- 快捷键；
- 产品名称；
- 格式标记；
- 代码或技术标识符。

---

### 添加新的 UI 语言

添加一个新的语言不仅需要创建翻译文件，还需要将它注册到应用中。

假设添加法语 `fr`。

#### 1. 创建语言资源

在：

```text
OpenUtauMobile/Assets/Lang/
```

中创建：

```text
Strings.fr.resx
```

建议从现有完整语言资源复制 key 集合，然后翻译所有 `<value>`。

不要为新语言重新设计 key。

#### 2. 将资源加入项目

在：

```text
OpenUtauMobile/OpenUtauMobile.csproj
```

的语言资源列表中加入：

```xml
<AvaloniaResource Include="Assets\Lang\Strings.fr.resx"/>
```

#### 3. 注册语言

在：

```text
OpenUtauMobile/Helpers/LocalizationManager.cs
```

的 `AvailableLanguages` 中加入该语言：

```csharp
("fr", "Français"),
```

语言名称应使用该语言自己的名称，例如：

```text
English
简体中文
日本語
Русский
Українська
Français
```

而不是全部写成英文。

#### 4. 测试

新增或修改 UI 翻译后，应至少：

- 构建并启动 OpenUtau Mobile；
- 在设置中切换到目标语言；
- 检查主要页面是否能够正常显示；
- 检查是否出现未翻译的 localization key；
- 检查较长文本是否导致按钮、菜单或其他控件布局异常；
- 检查格式参数是否能够正确显示。

建议特别检查：

- 首页；
- 设置页；
- 编辑器；
- Dialog、Toast 和错误提示。

如果添加的是一种全新的语言，还应确认应用重新启动后能够正确保存和恢复语言设置。

---

### 添加新的 UI 文本

开发新功能时，不应直接将需要翻译的用户可见文本硬编码在 UI 或业务代码中。

应先创建一个具有明确语义的 localization key，例如：

```text
Editor.Save
SingerDetail.Delete
PianoRoll.MultiSelect
```

然后将相同的 key 添加到语言资源文件。

新增 key 后，应尽量同步更新当前已有的语言文件：

```text
Strings.en.resx
Strings.zh-Hans.resx
Strings.ja.resx
Strings.ru.resx
Strings.uk.resx
```

在 axaml 文件中使用：

```xaml
<TextBlock Text="{DynamicResource Editor.Save}"/>
```

在代码中使用：

```csharp
string saveText = L.S("Editor.Save");
```

---

# 报告 Bug

提交可复现的 Bug 时，请尽量提供：

* OpenUtau Mobile 版本或 commit；
* 设备型号；
* 操作系统及版本；
* 受影响的平台；
* 复现步骤；
* 预期行为；
* 实际行为；
* 必要的截图或录屏；
* 可以提供的日志或 stack trace。

Android 崩溃问题通常可以通过：

```bash
adb logcat > log.txt
```

收集日志。

建议：

1. 开始记录日志；
2. 复现问题；
3. 停止记录；
4. 删除与问题无关的日志；
5. 检查并删除私人信息；
6. 再将日志附加到 Issue。

---

## 许可证

向 OpenUtau Mobile 提交贡献，即表示你同意你的贡献按照本仓库适用的许可证进行发布。

请不要提交无法合法包含或重新分发的：

* 源代码；
* 图片；
* 音频；
* 字体；
* 模型；
* 数据集；
* 第三方库；
* 其他受版权保护的材料。

使用第三方代码或资源时，请保留其许可证要求的版权和许可信息。
