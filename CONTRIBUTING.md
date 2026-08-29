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

## 搭建开发环境

### 1. Fork 并克隆仓库

首先在 GitHub 上 Fork 本仓库，然后克隆自己的 Fork：

```bash
git clone https://github.com/<你的用户名>/OpenUtauMobile.git
cd OpenUtauMobile
```

将 OpenUtau Mobile 官方仓库添加为 `upstream`：

```bash
git remote add upstream https://github.com/vocoder712/OpenUtauMobile.git
```

可以通过以下命令检查远程仓库配置：

```bash
git remote -v
```

通常情况下：

```text
origin      -> 你自己的 Fork
upstream    -> OpenUtau Mobile 官方仓库
```

### 2. 切换到开发分支

OpenUtau Mobile V2 的日常开发在 `dev` 分支进行。

```bash
git switch dev
```

开始新的工作前，请先同步最新的 `dev`：

```bash
git fetch upstream
git pull --ff-only upstream dev
```

除非修改明确针对旧版本，否则不要基于 `master` 创建新的开发分支。

### 3. 安装所需工具链

项目所需的 .NET SDK 版本由：

```text
global.json
```

定义。

各项目的 Target Framework 和平台要求由对应的：

```text
*.csproj
```

文件定义。

这些仓库中的配置文件是 SDK、Target Framework 等开发环境信息的 **唯一可信来源**。

不要仅根据 README 或其他文档中可能已经过时的版本号配置开发环境。

可以使用：

```bash
dotnet --version
dotnet --info
```

检查当前安装的 .NET 环境。

进行 Android 开发时，还需要安装：

* Android SDK；
* 当前 .NET SDK 所需的 Android workload；

其他平台可能需要额外的平台相关开发环境。

### 4. 恢复依赖

在仓库根目录执行：

```bash
dotnet restore
```

### 5. IDE

常用开发环境包括：

* JetBrains Rider
* Visual Studio
* Visual Studio Code + C# 工具链

无论使用哪个编辑器，都请确保其遵循仓库中的：

```text
.editorconfig
```

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
