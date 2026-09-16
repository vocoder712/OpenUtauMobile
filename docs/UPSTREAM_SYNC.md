# OpenUtau 上游内核同步操作手册

本文是 OpenUtauMobile（OPUM）同步 OpenUtau（OPU）内核的标准流程。命令以 PowerShell 7 为准。

执行方式：同一 PowerShell 会话按顺序执行；每条 Git/dotnet 命令检查 `$LASTEXITCODE`。除文档明确预期的分支不存在、无差异或冲突检查外，非零立即停止，禁止继续下一条。会话中断后恢复已经记录的冻结 SHA/版本，不重新获取上游目标。

同步单元始终包含三个目录，并且必须使用同一个 OPU 完整提交 SHA：

```text
OpenUtau.Core/
OpenUtau.Plugin.Builtin/
native/upstream_cpp/  # 上游 cpp/
```

## 0. 固定远程和分支职责

| 名称 | URL | 用途 |
| --- | --- | --- |
| `origin` | `https://github.com/vocoder712/OpenUtauMobile.git` | OPUM 正式源码、`dev` 和同步 PR |
| `opu` | `https://github.com/openutau/OpenUtau.git` | OPU 官方上游 |
| `opu-sync` | `https://github.com/vocoder712/OpenUtauMobile-sync.git` | subtree 缓存和 synthetic 历史备份 |

| 分支 | 用途 | 推送位置 |
| --- | --- | --- |
| `dev` | OPUM 正式开发基线；保存已合并的 subtree ancestry | `origin` |
| `chore/sync-opu-<SHORT_SHA>` | 单次正式同步、冲突解析和兼容修复 | `origin`，PR 到 `dev` |
| `cache/opu-core` | OPU 完整历史加 Core 的 `--rejoin` 缓存 | `opu-sync` |
| `cache/opu-plugin` | OPU 完整历史加 Plugin 的独立 `--rejoin` 缓存 | `opu-sync` |
| `cache/opu-cpp` | OPU 完整历史加 CPP 的独立 `--rejoin` 缓存（prefix 为 `cpp`） | `opu-sync` |
| `opu-core/<SHORT_SHA>` | Core-only synthetic split 历史 | `opu-sync` |
| `opu-plugin/<SHORT_SHA>` | Plugin-only synthetic split 历史 | `opu-sync` |
| `opu-cpp/<SHORT_SHA>` | CPP-only synthetic split 历史 | `opu-sync` |

`<SHORT_SHA>` 固定为完整 OPU SHA 的前 12 位。三个 cache 分支不能混用 prefix。

上游 prefix 和正式工作区 prefix 必须区分：

| 单元 | cache 中的上游 prefix | OPUM 正式 prefix |
| --- | --- | --- |
| Core | `OpenUtau.Core` | `OpenUtau.Core` |
| Plugin | `OpenUtau.Plugin.Builtin` | `OpenUtau.Plugin.Builtin` |
| CPP | `cpp` | `native/upstream_cpp` |

CPP 的 split/rejoin 始终使用 `cpp`，正式 add/merge 始终使用 `native/upstream_cpp`。不能把正式 prefix 用于上游 cache。

缓存 SHA 以 `git ls-remote --heads opu-sync` 的实时结果为准；文档不维护易过期的检查点列表。

## 1. 永久规则

1. 每次开始先运行 `git status --short`。有任何输出就停止，不 stash、不 clean、不覆盖，先向用户报告。
2. **一次同步只允许在启动阶段执行一次 `git fetch opu --tags --prune`。紧接着冻结 `$OpuSha`；直到该次同步彻底完成，禁止再次 fetch/pull `opu`、禁止 `git fetch --all`/`git remote update`，也禁止重新解析 `opu/master`。期间出现的新上游提交留给下一次同步。**
3. Core、Plugin、CPP、`Directory.Build.props` 和 PR 描述必须始终使用同一个冻结的 `$OpuSha`。
4. 同步完成前必须把 `Directory.Build.props` 的 `CoreVersion` 更新为 `<OPU_VERSION>-<SHORT_SHA>`。
5. 正式分支只使用 `git subtree merge --squash`，禁止省略 `--squash`。
6. `--rejoin` 只在三个 `cache/opu-*` 分支上执行。
7. 禁止手写 `git-subtree-*` 元数据、`commit-tree`、复制/移动缓存或混用 prefix。
8. cache 与 synthetic 分支只推送到 `opu-sync`，不合入 `dev`。
9. **每次使用三个 cache 分支完成 merge/split 后，必须执行第 6 节，把 cache 的最新位置备份到 `opu-sync` 并验证远程 SHA；未完成远程验证，不进入正式同步。**
10. 即使新 OPU 提交没有修改 Core/Plugin/CPP，也要推送 cache 分支；只有没有产生新 synthetic 分支时才省略该 synthetic 分支的 push。
11. 所有推送都应是新建或 fast-forward；禁止 force push。
12. 同步 PR 必须使用 **Create a merge commit**；禁止 **Squash and merge** 和 **Rebase and merge**。
13. 出现冲突时先列出冲突文件、三方含义和保留计划，取得用户确认后再修改；禁止机械选择全部 ours/theirs。
14. 构建 Avalonia 项目前设置 `$env:AVALONIA_TELEMETRY_OPTOUT='1'`。

## 2. 每次同步前

```powershell
git status --short
```

必须没有输出。以下命令会校验并补齐远程；发现同名 remote 指向其他 URL 时直接停止：

```powershell
$ExpectedOpu = 'https://github.com/openutau/OpenUtau.git'
$ExpectedSync = 'https://github.com/vocoder712/OpenUtauMobile-sync.git'

$OpuUrl = git remote get-url opu 2>$null
if ($LASTEXITCODE -ne 0) {
    git remote add opu $ExpectedOpu
} elseif ($OpuUrl.Trim() -ne $ExpectedOpu) {
    throw "remote opu points to $OpuUrl"
}

$SyncUrl = git remote get-url opu-sync 2>$null
if ($LASTEXITCODE -ne 0) {
    git remote add opu-sync $ExpectedSync
} elseif ($SyncUrl.Trim() -ne $ExpectedSync) {
    throw "remote opu-sync points to $SyncUrl"
}
```

先获取 OPUM 与缓存引用，然后执行本次同步唯一一次 OPU fetch：

```powershell
git fetch origin --prune
git fetch opu-sync --prune
git fetch opu --tags --prune
```

立即冻结目标和版本。执行本代码块后，直到 PR 以 merge commit 合入 `dev` 且本地 `dev` 更新完成，不得再运行任何会访问 `opu` 的 fetch/pull/update 命令，也不得重新赋值 `$OpuSha`：

```powershell
$OpuSha = (git rev-parse opu/master).Trim()
$Short = $OpuSha.Substring(0, 12)
$SyncBranch = "chore/sync-opu-$Short"
$CoreSplitBranch = "opu-core/$Short"
$PluginSplitBranch = "opu-plugin/$Short"
$CppSplitBranch = "opu-cpp/$Short"

$OpuVersion = git describe --tags --exact-match $OpuSha 2>$null
if ($LASTEXITCODE -ne 0) {
    $OpuVersion = git describe --tags --abbrev=0 $OpuSha
}
$OpuVersion = "$OpuVersion".Trim()
if (-not $OpuVersion) { throw 'Cannot determine OPU version tag.' }
$CoreVersion = "$OpuVersion-$Short"

Write-Output "OPU_SHA=$OpuSha"
Write-Output "SHORT_SHA=$Short"
Write-Output "OPU_VERSION=$OpuVersion"
Write-Output "CORE_VERSION=$CoreVersion"
```

记录以上四项。即使操作期间 GitHub 上的 OPU 主线前移，本次也继续使用冻结值。

## 3. 本地缓存不存在时从 `opu-sync` 恢复

本地缓存已存在时也需检查远程是否领先：在对应 cache 分支执行 `git merge --ff-only opu-sync/cache/opu-core`（Plugin、CPP 使用各自的引用）。若本地独有提交或双方分叉，先检查 `git log --left-right --oneline cache/opu-core...opu-sync/cache/opu-core`；有分叉就停止协调，不把两份独立重建的缓存随意合并。

```powershell
git fetch opu-sync --prune

git show-ref --verify --quiet refs/heads/cache/opu-core
if ($LASTEXITCODE -ne 0) {
    git branch cache/opu-core opu-sync/cache/opu-core
}

git show-ref --verify --quiet refs/heads/cache/opu-plugin
if ($LASTEXITCODE -ne 0) {
    git branch cache/opu-plugin opu-sync/cache/opu-plugin
}
```

CPP 已建立缓存后，与另外两个目录使用同样的恢复规则：

```powershell
git show-ref --verify --quiet refs/heads/cache/opu-cpp
if ($LASTEXITCODE -ne 0) {
    git branch cache/opu-cpp opu-sync/cache/opu-cpp
}
```

仅首次接入 CPP、确认本地和远程均不存在 `cache/opu-cpp` 时，跳过该恢复命令，改按第 5.1 节初始化。远程已存在时必须恢复，不能重建。

恢复所有已备份 synthetic 分支名：

```powershell
$splitBranches = git for-each-ref --format='%(refname:strip=3)' `
    refs/remotes/opu-sync/opu-core `
    refs/remotes/opu-sync/opu-plugin `
    refs/remotes/opu-sync/opu-cpp

foreach ($name in $splitBranches) {
    git show-ref --verify --quiet "refs/heads/$name"
    if ($LASTEXITCODE -ne 0) {
        git branch $name "opu-sync/$name"
    }
}
```

## 4. 更新 Core 缓存

```powershell
git switch cache/opu-core
git merge --no-edit $OpuSha
git subtree split `
    --prefix=OpenUtau.Core `
    --rejoin `
    --squash `
    --branch $CoreSplitBranch
```

读取官方 rejoin 元数据中的实际 split SHA：

```powershell
$CoreMeta = git log cache/opu-core `
    --grep='git-subtree-dir: OpenUtau.Core' `
    --format='%B' -n 1
$CoreLine = $CoreMeta | Where-Object {
    $_ -match '^git-subtree-split: [0-9a-f]{40}$'
} | Select-Object -First 1
if (-not $CoreLine) { throw 'Core subtree split metadata not found.' }
$CoreSplit = ($CoreLine -replace '^git-subtree-split: ', '').Trim()
git cat-file -e "$CoreSplit^{commit}"
if ($LASTEXITCODE -ne 0) { throw "Core split object $CoreSplit is missing." }
Write-Output "CORE_SPLIT=$CoreSplit"
```

若目标之后没有修改 `OpenUtau.Core/`，subtree 会返回旧 split，且可能不创建新命名分支。这是正常行为；使用 `$CoreSplit`，不得伪造新 split。

## 5. 更新 Plugin 缓存

```powershell
git switch cache/opu-plugin
git merge --no-edit $OpuSha
git subtree split `
    --prefix=OpenUtau.Plugin.Builtin `
    --rejoin `
    --squash `
    --branch $PluginSplitBranch
```

读取实际 Plugin split：

```powershell
$PluginMeta = git log cache/opu-plugin `
    --grep='git-subtree-dir: OpenUtau.Plugin.Builtin' `
    --format='%B' -n 1
$PluginLine = $PluginMeta | Where-Object {
    $_ -match '^git-subtree-split: [0-9a-f]{40}$'
} | Select-Object -First 1
if (-not $PluginLine) { throw 'Plugin subtree split metadata not found.' }
$PluginSplit = ($PluginLine -replace '^git-subtree-split: ', '').Trim()
git cat-file -e "$PluginSplit^{commit}"
if ($LASTEXITCODE -ne 0) { throw "Plugin split object $PluginSplit is missing." }
Write-Output "PLUGIN_SPLIT=$PluginSplit"
```

若新提交没有修改 Plugin，继续使用读取出的旧 `$PluginSplit`。

### 5.1 更新 CPP 缓存（首次接入也使用官方 subtree）

首次确认本地和远程都没有 CPP cache 后，只执行一次：

```powershell
git branch cache/opu-cpp $OpuSha
```

之后首次与日常同步完全使用相同的 split/rejoin 流程：

```powershell
git switch cache/opu-cpp
git merge --no-edit $OpuSha
git subtree split `
    --prefix=cpp `
    --rejoin `
    --squash `
    --branch $CppSplitBranch

$CppMeta = git log cache/opu-cpp `
    --grep='git-subtree-dir: cpp' `
    --format='%B' -n 1
$CppLine = $CppMeta | Where-Object {
    $_ -match '^git-subtree-split: [0-9a-f]{40}$'
} | Select-Object -First 1
if (-not $CppLine) { throw 'CPP subtree split metadata not found.' }
$CppSplit = ($CppLine -replace '^git-subtree-split: ', '').Trim()
git cat-file -e "$CppSplit^{commit}"
if ($LASTEXITCODE -ne 0) { throw "CPP split object $CppSplit is missing." }
Write-Output "CPP_SPLIT=$CppSplit"
```

第一次会扫描全部上游历史，后续复用 rejoin 缓存。没有 CPP 改动时复用旧 split；不要制造空提交或伪造元数据。

## 6. 每次使用 cache 后必须立即备份

本节是每次缓存更新的强制收尾步骤，不是可选归档。第 4、5、5.1 节执行完毕后立即执行本节；远程验证通过后，才能切回 `dev` 创建正式同步分支。

即使本次上游没有修改 Core/Plugin/CPP、实际 split SHA 没变，cache 分支也可能因为合入了新的 `$OpuSha` 而前移，因此仍要推送三个 cache 分支。

先 fast-forward 推送三个 cache 分支：

```powershell
git push opu-sync `
    cache/opu-core:cache/opu-core `
    cache/opu-plugin:cache/opu-plugin `
    cache/opu-cpp:cache/opu-cpp
```

条件推送本次 synthetic 分支：

```powershell
git show-ref --verify --quiet "refs/heads/$CoreSplitBranch"
if ($LASTEXITCODE -eq 0) {
    git push opu-sync "${CoreSplitBranch}:$CoreSplitBranch"
}

git show-ref --verify --quiet "refs/heads/$PluginSplitBranch"
if ($LASTEXITCODE -eq 0) {
    git push opu-sync "${PluginSplitBranch}:$PluginSplitBranch"
}
```

CPP synthetic 分支同样条件推送：

```powershell
git show-ref --verify --quiet "refs/heads/$CppSplitBranch"
if ($LASTEXITCODE -eq 0) {
    git push opu-sync "${CppSplitBranch}:$CppSplitBranch"
}
```

验证远程 cache SHA 与本地完全一致，并确认远程至少有一条 synthetic 分支指向实际 split SHA：

```powershell
$LocalCoreCache = (git rev-parse cache/opu-core).Trim()
$LocalPluginCache = (git rev-parse cache/opu-plugin).Trim()
$LocalCppCache = (git rev-parse cache/opu-cpp).Trim()

$RemoteCoreCacheLine = git ls-remote --heads opu-sync cache/opu-core
$RemotePluginCacheLine = git ls-remote --heads opu-sync cache/opu-plugin
$RemoteCppCacheLine = git ls-remote --heads opu-sync cache/opu-cpp
$RemoteCoreCache = ($RemoteCoreCacheLine -split '\s+')[0]
$RemotePluginCache = ($RemotePluginCacheLine -split '\s+')[0]
$RemoteCppCache = ($RemoteCppCacheLine -split '\s+')[0]

if ($RemoteCoreCache -ne $LocalCoreCache) {
    throw "Remote Core cache mismatch: local=$LocalCoreCache remote=$RemoteCoreCache"
}
if ($RemotePluginCache -ne $LocalPluginCache) {
    throw "Remote Plugin cache mismatch: local=$LocalPluginCache remote=$RemotePluginCache"
}

if ($RemoteCppCache -ne $LocalCppCache) {
    throw "Remote CPP cache mismatch: local=$LocalCppCache remote=$RemoteCppCache"
}

$RemoteCoreSplits = git ls-remote --heads opu-sync 'opu-core/*'
$RemotePluginSplits = git ls-remote --heads opu-sync 'opu-plugin/*'
$RemoteCppSplits = git ls-remote --heads opu-sync 'opu-cpp/*'
if (-not ($RemoteCoreSplits -match "^$CoreSplit\s")) {
    throw "Remote Core synthetic branch for $CoreSplit is missing."
}
if (-not ($RemotePluginSplits -match "^$PluginSplit\s")) {
    throw "Remote Plugin synthetic branch for $PluginSplit is missing."
}

if (-not ($RemoteCppSplits -match "^$CppSplit\s")) {
    throw "Remote CPP synthetic branch for $CppSplit is missing."
}

Write-Output "REMOTE_CORE_CACHE=$RemoteCoreCache"
Write-Output "REMOTE_PLUGIN_CACHE=$RemotePluginCache"
Write-Output "REMOTE_CPP_CACHE=$RemoteCppCache"
Write-Output "REMOTE_CORE_SPLIT=$CoreSplit"
Write-Output "REMOTE_PLUGIN_SPLIT=$PluginSplit"
Write-Output "REMOTE_CPP_SPLIT=$CppSplit"
```

只有以上六项检查全部通过，缓存使用才算完成。如果 push 被拒绝或 SHA 不一致，停止并 fetch/审查远程差异；禁止 `--force`。

## 7. 创建正式同步分支

```powershell
git switch dev
git pull --ff-only origin dev
git status --short
git switch -c $SyncBranch
```

`git status --short` 必须为空。如果同步分支已存在，不删除、不 reset；确认它是否为未完成的同一次同步。

## 8. 正式合并

使用前面解析的实际 split SHA：

```powershell
git subtree merge `
    --prefix=OpenUtau.Core `
    --squash `
    $CoreSplit `
    -m "chore: sync OpenUtau.Core to $Short"

git subtree merge `
    --prefix=OpenUtau.Plugin.Builtin `
    --squash `
    $PluginSplit `
    -m "chore: sync OpenUtau.Plugin.Builtin to $Short"
```

CPP 已建立正式祖先后，与另外两个目录一样合并：

```powershell
git subtree merge `
    --prefix=native/upstream_cpp `
    --squash `
    $CppSplit `
    -m "chore: sync upstream cpp to $Short"
```

**首次接入 CPP**：确认 `native/upstream_cpp` 不存在，且正式历史没有该 prefix 的 subtree 元数据；用下面的命令替代此次 CPP merge，建立由 Git 官方命令产生的 squash 祖先：

```powershell
git subtree add `
    --prefix=native/upstream_cpp `
    --squash `
    $CppSplit `
    -m "chore: add upstream cpp at $Short"
```

不删除或覆盖已存在的目录。如果目录已存在但没有祖先，先审计来源和本地定制，再按第 13 节处理；不能把普通文件复制当作 subtree 初始化。首个 PR 必须保留 merge commit，后续才能直接执行三目录 merge。

某个 prefix 无变化时可能报告已处于该提交，不制造空提交。

### 冲突门禁

出现冲突后先检查，不修改：

```powershell
git status --short
git diff --name-only --diff-filter=U
git diff --cc
```

向用户报告每个冲突的上游语义、OPUM 定制语义和合并计划；确认后才解决。完成后：

```powershell
git grep -n -E '^(<<<<<<<|=======|>>>>>>>)' -- OpenUtau.Core OpenUtau.Plugin.Builtin native/upstream_cpp
git diff --name-only --diff-filter=U
git add OpenUtau.Core OpenUtau.Plugin.Builtin native/upstream_cpp
git commit --no-edit
```

确认没有冲突标记和未合并索引。

## 9. 更新内核版本、兼容检查与构建

### 9.1 更新 `Directory.Build.props`

正式合并和冲突解决完成后，把内核版本更新为启动时计算的 `$CoreVersion`：

```powershell
$PropsPath = 'Directory.Build.props'
$PropsText = [System.IO.File]::ReadAllText($PropsPath)
$CoreVersionMatches = [regex]::Matches(
    $PropsText,
    '<CoreVersion>[^<]*</CoreVersion>')
if ($CoreVersionMatches.Count -ne 1) {
    throw "Expected exactly one CoreVersion, found $($CoreVersionMatches.Count)."
}
$PropsText = [regex]::Replace(
    $PropsText,
    '<CoreVersion>[^<]*</CoreVersion>',
    "<CoreVersion>$CoreVersion</CoreVersion>")
[System.IO.File]::WriteAllText(
    $PropsPath,
    $PropsText,
    [System.Text.UTF8Encoding]::new($false))

Select-String -Path $PropsPath -Pattern '<CoreVersion>'
git diff -- Directory.Build.props
```

结果必须严格为 `<OPU_VERSION>-<SHORT_SHA>`。例如本次冻结目标具有标签 `0.1.570.1-alpha`，短 SHA 为 `2b03ad562fa6`：

```text
<CoreVersion>0.1.570.1-alpha-2b03ad562fa6</CoreVersion>
```

必须在构建和 PR 前提交该修改。

### 9.2 兼容检查

必须审计全部既有定制，而不仅是 Git 报冲突的文件。记录上一正式同步的 Core/Plugin/CPP split SHA 和本次 OPUM 基线 SHA，分别比较「旧上游→旧 OPUM」「旧上游→新上游」「旧 OPUM→合并结果」。每个定制明确标为保留、适配或经用户批准删除。

CPP 首次引入时，比较 `$OpuSha:cpp`、`$CppSplit^{tree}` 和 `HEAD:native/upstream_cpp` 的 tree SHA，三者必须一致。后续也要审计 CPP 的全部本地定制；若无定制，继续要求 tree 完全一致。CPP 源码纳入版本管理不等于应用已切换 native 构建来源，需分别记录源码同步与实际构建接入状态。

重点：Core 项目资源命名空间、包版本与 native/build 排除；ONNX 按目标平台分发；Preferences 的字段级异常隔离和移动端默认值；Neutrino 的 noteIndex、availableLeadingMs；公共 API 和插件兼容。无文本冲突不代表行为正确。

禁止从先前失败的同步提交整文件复制解析结果。noteIndex 与上游 xsyAvailable 应同时保留；删除定制必须记录理由及受影响调用端。

至少检查：

```powershell
rg -n "RenderPhraseEvents|GetSuggestions\(|EnsureAvatarLoaded|PhraseRenderedNotification|PartRenderedNotification" `
    OpenUtau.Core OpenUtauMobile.Plugin.Renderers OpenUtauMobile
```

上游 API 引起的 OPUM 兼容修复作为普通提交提交。

### 9.3 构建

```powershell
$env:AVALONIA_TELEMETRY_OPTOUT='1'
dotnet build OpenUtau.Plugin.Builtin\OpenUtau.Plugin.Builtin.csproj `
    --nologo -v:minimal
if ($LASTEXITCODE -ne 0) { throw 'Plugin build failed.' }

$env:AVALONIA_TELEMETRY_OPTOUT='1'
dotnet build OpenUtauMobile\OpenUtauMobile.csproj `
    --nologo -v:minimal
if ($LASTEXITCODE -ne 0) { throw 'Mobile build failed.' }
```

依赖变更后必须实际 restore；旧 assets 上的 `--no-restore` 成功不是依赖正确性的证据。还需执行 PR 的目标平台构建（以仓库当前 workflow 为准）：

```powershell
$env:AVALONIA_TELEMETRY_OPTOUT='1'
dotnet restore OpenUtauMobile.Android/OpenUtauMobile.Android.csproj
if ($LASTEXITCODE -ne 0) { throw 'Android restore failed.' }
dotnet build OpenUtauMobile.Android/OpenUtauMobile.Android.csproj `
    -c Release -f net10.0-android36.0 -p:RuntimeIdentifier=android-arm64
if ($LASTEXITCODE -ne 0) { throw 'Android build failed.' }
```

本地缺少 SDK/JDK/workload 时明确记录未验证的目标，由 PR CI 补齐。所有必需云检查通过之前，不宣称同步验证完成，也不合并 PR。

CPP 验证：在 `native/upstream_cpp` 中按其 `README.md` 使用 Bazel/Bazelisk 构建 `//worldline`，有可用工具链时运行其已有测试。Windows 按上游说明省略目标开头的 `//`。缺少编译器或依赖下载失败时明确记录，不能把 tree 校验当作 native 构建通过。

终检：

```powershell
git status --short
git log --graph --oneline --decorate dev..HEAD
git diff --stat dev...HEAD
```

## 10. 推送 PR

```powershell
git push -u origin $SyncBranch
```

PR：`chore/sync-opu-<SHORT_SHA> -> dev`。描述必须记录：

```text
OpenUtau target: <FULL_OPU_SHA>
Core split:       <CORE_SPLIT>
Plugin split:     <PLUGIN_SPLIT>
CPP split:        <CPP_SPLIT>
CPP tree:         <OPU_SHA>:cpp == HEAD:native/upstream_cpp（无定制时）
CoreVersion:      <OPU_VERSION>-<SHORT_SHA>
Plugin build:     exit 0
Mobile build:     exit 0
```

GitHub 必须选择 **Create a merge commit**。禁止 Squash and merge、Rebase and merge。合并后：

```powershell
git switch dev
git pull --ff-only origin dev
```

至此本次冻结快照同步才算彻底完成。之后若要同步新的 `opu/master`，必须作为下一次独立同步从第 2 节重新开始。

## 11. 全新电脑/本地什么都没有

不要把 `OpenUtauMobile-sync` 当作源码工作区。先克隆正式仓库：

```powershell
git clone https://github.com/vocoder712/OpenUtauMobile.git
Set-Location OpenUtauMobile
git switch dev

git remote add opu https://github.com/openutau/OpenUtau.git
git remote add opu-sync https://github.com/vocoder712/OpenUtauMobile-sync.git

git fetch origin --prune
git fetch opu-sync --prune
```

这里故意不 fetch `opu`。缓存恢复完成后，从第 2 节启动一次新同步，并且只在第 2 节 fetch `opu` 一次。

接着完整执行第 3 节的恢复命令（三个 cache 和全部 synthetic 引用），不重复定义另一套恢复流程。第 4、5、5.1 节已经提供 split 元数据读取与对象存在性检查。

至此缓存已经恢复。继续执行第 2 节以后步骤时，只处理远程检查点之后的增量，不完整扫描旧历史，也不重新解决已经通过 merge commit 合入 `dev` 的旧冲突。

若上一次同步 PR 尚未合并，先 fetch/checkout `origin/chore/sync-opu-<SHA>`，不要重做同一次冲突解析。

## 12. 仅当 `opu-sync` 不可用时完整重建

第一次重建会扫描全部 OPU 历史。本节必须复用第 2 节已经冻结的 `$OpuSha` 和 `$Short`；不得再次 fetch `opu` 或重新解析 `opu/master`。先确保本地没有同名缓存：

```powershell
git show-ref --verify --quiet refs/heads/cache/opu-core
if ($LASTEXITCODE -eq 0) { throw 'cache/opu-core exists; do not overwrite.' }
git show-ref --verify --quiet refs/heads/cache/opu-plugin
if ($LASTEXITCODE -eq 0) { throw 'cache/opu-plugin exists; do not overwrite.' }

git show-ref --verify --quiet refs/heads/cache/opu-cpp
if ($LASTEXITCODE -eq 0) { throw 'cache/opu-cpp exists; do not overwrite.' }
```

只用官方命令分别建立：

```powershell
git branch cache/opu-core $OpuSha
git switch cache/opu-core
git subtree split `
    --prefix=OpenUtau.Core `
    --rejoin `
    --squash `
    --branch "opu-core/$Short"

git switch dev
git branch cache/opu-plugin $OpuSha
git switch cache/opu-plugin
git subtree split `
    --prefix=OpenUtau.Plugin.Builtin `
    --rejoin `
    --squash `
    --branch "opu-plugin/$Short"
```

CPP 也从同一个冻结目标独立建立：

```powershell
git switch dev
git branch cache/opu-cpp $OpuSha
git switch cache/opu-cpp
git subtree split `
    --prefix=cpp `
    --rejoin `
    --squash `
    --branch "opu-cpp/$Short"
```

按第 4、5、5.1 节读取三项实际 split 并检查对象存在。

完成后立即按第 6 节备份 cache 和 synthetic 分支。禁止手写元数据缩短首次扫描。

## 13. 正式 `dev` 缺少 subtree ancestry

CPP 首次加入不存在的目录使用第 8 节的 add；本节仅用于已有目录丢失祖先的恢复。同步 PR 正确合并后，未来不应进入本节。只有 subtree 明确报告 prefix 从未 add，且确认 `dev` 曾被 squash/rewrite 时才处理。

不要手写元数据。在专用同步分支中：

1. `git rm -r <PREFIX>` 并提交；
2. 若只剩 `bin/obj`，执行 `git clean -fdx -- <PREFIX>`；
3. 用最后已知 split 执行 `git subtree add --prefix=<PREFIX> --squash <OLD_SPLIT>`；
4. 用 `git checkout dev -- <PREFIX>` 恢复 OPUM 定制并提交；
5. 再执行正常的 `git subtree merge --squash <NEW_SPLIT>`；
6. 冲突仍遵循第 8 节审批门禁。

该过程恢复正式 ancestry，与缓存恢复是两件事。

## 14. 故障判断

缓存正常：

* cache 分支能合入新 OPU SHA；
* split 只处理上次 rejoin 后的新增历史；
* 元数据中的 split SHA 能通过 `git cat-file -e`；
* cache 和对应 synthetic 分支都能从 `opu-sync` fetch。

缓存失效：

* 恢复后仍完整扫描全部历史；
* 元数据指向的 split 对象不存在；
* Core/Plugin/CPP rejoin 混在同一缓存分支；
* 缓存曾由手写提交、移动引用或改名拼装。

失效时停止，不修补元数据。优先从 `opu-sync` 重新 fetch；只有远程也损坏时才按第 12 节完整重建。

## 15. 一句话流程

```text
status clean
→ fetch origin/opu/opu-sync
→ 立即冻结 OPU SHA 和版本；本次不再 fetch/读取 opu/master
→ 更新 Core cache + split
→ 更新 Plugin cache + split
→ 更新 CPP cache + split（首次独立建立）
→ fast-forward 备份 cache 和 synthetic 到 opu-sync
→ 从最新 dev 建 chore/sync-opu-<SHA>
→ 三次 subtree merge --squash（CPP 首次用 add --squash 建立祖先）
→ 冲突先报告并获确认
→ 更新 Directory.Build.props 的 CoreVersion
→ Plugin/Mobile build
→ 推送同步分支到 origin
→ PR 使用 Create a merge commit 合入 dev
```
