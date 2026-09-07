# OpenUtau 上游内核同步操作手册

本文是 OpenUtauMobile（OPUM）同步 OpenUtau（OPU）内核的标准流程。命令以 PowerShell 7 为准。

同步单元始终包含两个目录，并且必须使用同一个 OPU 完整提交 SHA：

```text
OpenUtau.Core/
OpenUtau.Plugin.Builtin/
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
| `opu-core/<SHORT_SHA>` | Core-only synthetic split 历史 | `opu-sync` |
| `opu-plugin/<SHORT_SHA>` | Plugin-only synthetic split 历史 | `opu-sync` |

`<SHORT_SHA>` 固定为完整 OPU SHA 的前 12 位。两个 cache 分支不能混用 prefix。

当前已备份检查点：

```text
cache/opu-core             93824a6195d228936af4abe570934391341fd790
cache/opu-plugin           caa54281bef0b164c157104f80d9354943b8384d
opu-core/eaaf2e88a0be      3f58ccb44522b9d6560fb7cbe0a3a6b0261ae320
opu-plugin/eaaf2e88a0be    1e74269e69f9d721238f966e4e09841743b56375
```

## 1. 永久规则

1. 每次开始先运行 `git status --short`。有任何输出就停止，不 stash、不 clean、不覆盖，先向用户报告。
2. **一次同步只允许在启动阶段执行一次 `git fetch opu --tags --prune`。紧接着冻结 `$OpuSha`；直到该次同步彻底完成，禁止再次 fetch/pull `opu`、禁止 `git fetch --all`/`git remote update`，也禁止重新解析 `opu/master`。期间出现的新上游提交留给下一次同步。**
3. Core、Plugin、`Directory.Build.props` 和 PR 描述必须始终使用同一个冻结的 `$OpuSha`。
4. 同步完成前必须把 `Directory.Build.props` 的 `CoreVersion` 更新为 `<OPU_VERSION>-<SHORT_SHA>`。
5. 正式分支只使用 `git subtree merge --squash`，禁止省略 `--squash`。
6. `--rejoin` 只在两个 `cache/opu-*` 分支上执行。
7. 禁止手写 `git-subtree-*` 元数据、`commit-tree`、复制/移动缓存或混用 prefix。
8. cache 与 synthetic 分支只推送到 `opu-sync`，不合入 `dev`。
9. **每次使用两个 cache 分支完成 merge/split 后，必须执行第 6 节，把 cache 的最新位置备份到 `opu-sync` 并验证远程 SHA；未完成远程验证，不进入正式同步。**
10. 即使新 OPU 提交没有修改 Core/Plugin，也要推送 cache 分支；只有没有产生新 synthetic 分支时才省略该 synthetic 分支的 push。
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

恢复所有已备份 synthetic 分支名：

```powershell
$splitBranches = git for-each-ref --format='%(refname:strip=3)' `
    refs/remotes/opu-sync/opu-core `
    refs/remotes/opu-sync/opu-plugin

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

## 6. 每次使用 cache 后必须立即备份

本节是每次缓存更新的强制收尾步骤，不是可选归档。第 4、5 节执行完毕后立即执行本节；远程验证通过后，才能切回 `dev` 创建正式同步分支。

即使本次上游没有修改 Core/Plugin、实际 split SHA 没变，cache 分支也可能因为合入了新的 `$OpuSha` 而前移，因此仍要推送两个 cache 分支。

先 fast-forward 推送两个 cache 分支：

```powershell
git push opu-sync `
    cache/opu-core:cache/opu-core `
    cache/opu-plugin:cache/opu-plugin
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

验证远程 cache SHA 与本地完全一致，并确认远程至少有一条 synthetic 分支指向实际 split SHA：

```powershell
$LocalCoreCache = (git rev-parse cache/opu-core).Trim()
$LocalPluginCache = (git rev-parse cache/opu-plugin).Trim()

$RemoteCoreCacheLine = git ls-remote --heads opu-sync cache/opu-core
$RemotePluginCacheLine = git ls-remote --heads opu-sync cache/opu-plugin
$RemoteCoreCache = ($RemoteCoreCacheLine -split '\s+')[0]
$RemotePluginCache = ($RemotePluginCacheLine -split '\s+')[0]

if ($RemoteCoreCache -ne $LocalCoreCache) {
    throw "Remote Core cache mismatch: local=$LocalCoreCache remote=$RemoteCoreCache"
}
if ($RemotePluginCache -ne $LocalPluginCache) {
    throw "Remote Plugin cache mismatch: local=$LocalPluginCache remote=$RemotePluginCache"
}

$RemoteCoreSplits = git ls-remote --heads opu-sync 'opu-core/*'
$RemotePluginSplits = git ls-remote --heads opu-sync 'opu-plugin/*'
if (-not ($RemoteCoreSplits -match "^$CoreSplit\s")) {
    throw "Remote Core synthetic branch for $CoreSplit is missing."
}
if (-not ($RemotePluginSplits -match "^$PluginSplit\s")) {
    throw "Remote Plugin synthetic branch for $PluginSplit is missing."
}

Write-Output "REMOTE_CORE_CACHE=$RemoteCoreCache"
Write-Output "REMOTE_PLUGIN_CACHE=$RemotePluginCache"
Write-Output "REMOTE_CORE_SPLIT=$CoreSplit"
Write-Output "REMOTE_PLUGIN_SPLIT=$PluginSplit"
```

只有以上四项检查全部通过，缓存使用才算完成。如果 push 被拒绝或 SHA 不一致，停止并 fetch/审查远程差异；禁止 `--force`。

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
git grep -n -E '^(<<<<<<<|=======|>>>>>>>)' -- OpenUtau.Core OpenUtau.Plugin.Builtin
git diff --name-only --diff-filter=U
git add OpenUtau.Core OpenUtau.Plugin.Builtin
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
    --no-restore --nologo -v:minimal
if ($LASTEXITCODE -ne 0) { throw 'Plugin build failed.' }

$env:AVALONIA_TELEMETRY_OPTOUT='1'
dotnet build OpenUtauMobile\OpenUtauMobile.csproj `
    --no-restore --nologo -v:minimal
if ($LASTEXITCODE -ne 0) { throw 'Mobile build failed.' }
```

若新环境尚未 restore，先联网运行不带 `--no-restore` 的同一 build，再重复上述验证。

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

恢复 cache：

```powershell
git branch cache/opu-core opu-sync/cache/opu-core
git branch cache/opu-plugin opu-sync/cache/opu-plugin
```

恢复所有 synthetic 本地分支：

```powershell
$splitBranches = git for-each-ref --format='%(refname:strip=3)' `
    refs/remotes/opu-sync/opu-core `
    refs/remotes/opu-sync/opu-plugin

foreach ($name in $splitBranches) {
    git branch $name "opu-sync/$name"
}
```

验证缓存引用的对象：

```powershell
$CoreMeta = git log cache/opu-core --grep='git-subtree-dir: OpenUtau.Core' --format='%B' -n 1
$CoreLine = $CoreMeta | Where-Object { $_ -match '^git-subtree-split: [0-9a-f]{40}$' } | Select-Object -First 1
if (-not $CoreLine) { throw 'Core metadata missing.' }
$CoreSplit = ($CoreLine -replace '^git-subtree-split: ', '').Trim()

$PluginMeta = git log cache/opu-plugin --grep='git-subtree-dir: OpenUtau.Plugin.Builtin' --format='%B' -n 1
$PluginLine = $PluginMeta | Where-Object { $_ -match '^git-subtree-split: [0-9a-f]{40}$' } | Select-Object -First 1
if (-not $PluginLine) { throw 'Plugin metadata missing.' }
$PluginSplit = ($PluginLine -replace '^git-subtree-split: ', '').Trim()

git cat-file -e "$CoreSplit^{commit}"
if ($LASTEXITCODE -ne 0) { throw "Missing Core split $CoreSplit" }
git cat-file -e "$PluginSplit^{commit}"
if ($LASTEXITCODE -ne 0) { throw "Missing Plugin split $PluginSplit" }
git status --short
```

至此缓存已经恢复。继续执行第 2 节以后步骤时，只处理远程检查点之后的增量，不完整扫描旧历史，也不重新解决已经通过 merge commit 合入 `dev` 的旧冲突。

若上一次同步 PR 尚未合并，先 fetch/checkout `origin/chore/sync-opu-<SHA>`，不要重做同一次冲突解析。

## 12. 仅当 `opu-sync` 不可用时完整重建

第一次重建会扫描全部 OPU 历史。本节必须复用第 2 节已经冻结的 `$OpuSha` 和 `$Short`；不得再次 fetch `opu` 或重新解析 `opu/master`。先确保本地没有同名缓存：

```powershell
git show-ref --verify --quiet refs/heads/cache/opu-core
if ($LASTEXITCODE -eq 0) { throw 'cache/opu-core exists; do not overwrite.' }
git show-ref --verify --quiet refs/heads/cache/opu-plugin
if ($LASTEXITCODE -eq 0) { throw 'cache/opu-plugin exists; do not overwrite.' }
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

完成后立即按第 6 节备份 cache 和 synthetic 分支。禁止手写元数据缩短首次扫描。

## 13. 正式 `dev` 缺少 subtree ancestry

当前同步 PR 正确合并后，未来不应进入本节。只有 subtree 明确报告 prefix 从未 add，且确认 `dev` 曾被 squash/rewrite 时才处理。

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
* Core/Plugin rejoin 混在同一缓存分支；
* 缓存曾由手写提交、移动引用或改名拼装。

失效时停止，不修补元数据。优先从 `opu-sync` 重新 fetch；只有远程也损坏时才按第 12 节完整重建。

## 15. 一句话流程

```text
status clean
→ fetch origin/opu/opu-sync
→ 立即冻结 OPU SHA 和版本；本次不再 fetch/读取 opu/master
→ 更新 Core cache + split
→ 更新 Plugin cache + split
→ fast-forward 备份 cache 和 synthetic 到 opu-sync
→ 从最新 dev 建 chore/sync-opu-<SHA>
→ 两次 subtree merge --squash
→ 冲突先报告并获确认
→ 更新 Directory.Build.props 的 CoreVersion
→ Plugin/Mobile build
→ 推送同步分支到 origin
→ PR 使用 Create a merge commit 合入 dev
```
