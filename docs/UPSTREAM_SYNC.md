# OpenUtau 上游内核同步指南

OpenUtauMobile（OPUM）中的以下两个项目来源于 OpenUtau（OPU）上游，并作为一个整体进行同步：

* `OpenUtau.Core/`
* `OpenUtau.Plugin.Builtin/`

二者必须始终同步到**同一个 OpenUtau commit/tag**。

本文规定统一的同步流程，目标是：

1. 保留 OPUM 对 Core 的移动端修改；
2. 能持续吸收 OpenUtau 上游更新；
3. 使用 `--rejoin` 避免每次重新扫描数千个上游提交；
4. **禁止将 OpenUtau 的完整提交历史导入 OPUM 的 `dev` 分支；**
5. 正式同步一律使用 subtree squash。

---

# 1. 基本原则

## 1.1 上游 remote

OpenUtau 官方仓库统一命名为：

```bash
opu
```

检查：

```bash
git remote -v
```

如果没有：

```bash
git remote add opu https://github.com/openutau/OpenUtau.git
```

获取最新上游：

```bash
git fetch opu --tags
```

---

## 1.2 两个项目必须同步到同一个 commit

例如准备同步：

```text
8bef4418feb2
```

则必须同时同步：

```text
OpenUtau.Core           -> 8bef4418feb2
OpenUtau.Plugin.Builtin -> 8bef4418feb2
```

禁止：

```text
Core   -> commit A
Plugin -> commit B
```

因为 `OpenUtau.Plugin.Builtin` 直接依赖 `OpenUtau.Core` 的 API，不同版本组合可能导致编译错误甚至行为不一致。

---

# 2. 分支职责

同步过程中使用四类分支。

## 正式 OPUM 分支

```text
dev
```

正常开发分支。

**禁止把 OPU synthetic history 直接 merge 到该分支。**

---

## 同步工作分支

例如：

```text
chore/sync-opu-8bef4418feb2
```

每次上游同步建立一个。

最终通过 PR 合入 `dev`。

---

## 本地缓存分支

分别维护：

```text
cache/opu-core
cache/opu-plugin
```

用途：

* 保存 `git subtree split --rejoin` 的缓存记录；
* 加快未来 split；
* 不属于 OPUM 正式开发历史。

这些分支：

* 仅本地使用；
* 不合入 `dev`；
* 不提交 PR；
* 不建议推送到 OPUM 官方仓库。

---

## 临时 synthetic 分支

例如：

```text
opu-core/8bef4418feb2
opu-plugin/8bef4418feb2
```

它们是 `git subtree split` 的输出。

完成同步后可以删除。

---

# 3. 为什么必须使用 `--squash`

错误做法：

```bash
git subtree merge \
    --prefix=OpenUtau.Core \
    opu-core/<SHA>
```

这会把 synthetic subtree 的完整提交 ancestry 引入 OPUM。

结果可能一次带入上千个 OPU commit，污染：

* Git 历史；
* GitHub Contributors；
* PR commit 列表；
* 仓库统计。

因此正式同步必须使用：

```bash
git subtree merge \
    --prefix=OpenUtau.Core \
    --squash \
    opu-core/<SHA>
```

`--squash` 只把这次上游变化作为 squash history 引入，而不是把每一个上游 commit 都放入 OPUM 正式历史。

注意：

> `git subtree merge --squash` 与 GitHub 的 “Squash and merge” 不是一回事。

前者必须使用。

后者对内核同步 PR **禁止使用**。

---

# 4. 第一次在本机进行同步

第一次操作的主要目的，是建立本地 `--rejoin` 缓存。

第一次会比较慢，因为 Git 需要分析完整 OPU 历史。

之后同步会利用缓存，只处理上一次 rejoin 之后的新历史。

以下假设目标上游 commit 为：

```text
<OPU_SHA>
```

首先：

```bash
git fetch opu --tags
```

确定目标 commit。

如果同步最新 master：

```bash
git rev-parse opu/master
```

建议记录完整 SHA，并在整个同步过程中固定使用该 SHA。

---

# 5. 第一次建立 Core 缓存

创建缓存分支：

```bash
git branch cache/opu-core <OPU_SHA>
```

切换：

```bash
git switch cache/opu-core
```

第一次 split：

```bash
git subtree split \
    --prefix=OpenUtau.Core \
    --rejoin \
    --squash \
    --branch opu-core/<OPU_SHA>
```

参数含义：

* `split`：从完整 OPU 历史抽取 Core-only synthetic history；
* `--prefix=OpenUtau.Core`：只处理 Core；
* `--rejoin`：把 split 信息记录回缓存分支，供下一次增量计算；
* `--squash`：因为正式导入始终使用 squash，所以 rejoin 也必须使用 squash；
* `--branch`：保存生成的 synthetic history。

第一次可能需要扫描数千个 commit，这是正常现象。

---

# 6. 第一次建立 Plugin.Builtin 缓存

从相同 OPU commit 创建另一个独立缓存分支：

```bash
git branch cache/opu-plugin <OPU_SHA>
```

切换：

```bash
git switch cache/opu-plugin
```

执行：

```bash
git subtree split \
    --prefix=OpenUtau.Plugin.Builtin \
    --rejoin \
    --squash \
    --branch opu-plugin/<OPU_SHA>
```

Core 和 Plugin 使用不同缓存分支，避免两个 prefix 的 rejoin 历史互相干扰，也更容易维护和排查。

至此得到：

```text
opu-core/<OPU_SHA>
opu-plugin/<OPU_SHA>
```

---

# 7. 正式同步到 OPUM

回到最新 `dev`：

```bash
git switch dev
git pull --ff-only origin dev
```

建立同步分支：

```bash
git switch -c chore/sync-opu-<OPU_SHA>
```

---

## 7.1 同步 Core

```bash
git subtree merge \
    --prefix=OpenUtau.Core \
    --squash \
    opu-core/<OPU_SHA> \
    -m "sync: OpenUtau.Core to <OPU_SHA>"
```

如果出现冲突：

```bash
git status
```

解决冲突后：

```bash
git add OpenUtau.Core
git merge --continue
```

必须保留 OPUM 有意进行的移动端修改。

不要为了与 upstream 完全一致而机械选择 incoming。

---

## 7.2 同步 Plugin.Builtin

Core 完成后：

```bash
git subtree merge \
    --prefix=OpenUtau.Plugin.Builtin \
    --squash \
    opu-plugin/<OPU_SHA> \
    -m "sync: OpenUtau.Plugin.Builtin to <OPU_SHA>"
```

解决冲突：

```bash
git add OpenUtau.Plugin.Builtin
git merge --continue
```

---

# 8. 构建与测试

两个项目必须全部同步完成后再判断构建结果。

至少执行：

```bash
dotnet build OpenUtau.Plugin.Builtin/OpenUtau.Plugin.Builtin.csproj
```

然后执行 OPUM 完整构建和测试。

特别注意 Core API 发生变化后，OPUM 自己的代码也可能需要 forward-port。

这种编译错误属于正常的 downstream API 适配，不应该通过回滚 Core 来解决。

---

# 9. 创建 PR

推送：

```bash
git push -u origin chore/sync-opu-<OPU_SHA>
```

PR：

```text
chore/sync-opu-<OPU_SHA>
        ↓
       dev
```

PR 标题建议：

```text
sync: OpenUtau upstream to <OPU_SHA>
```

PR 中应明确写：

```text
Upstream: openutau/OpenUtau@<OPU_SHA>
```

并说明：

```text
OpenUtau.Core           -> <OPU_SHA>
OpenUtau.Plugin.Builtin -> <OPU_SHA>
```

---

# 10. PR 合并方式

这是非常重要的规则。

## 必须：

```text
Create a merge commit
```

## 禁止：

```text
Squash and merge
Rebase and merge
```

原因：

`git subtree merge --squash` 自己已经建立了 subtree 所需的 squash 元数据和提交关系。

GitHub 再次 squash/rebase PR，可能破坏后续 `git subtree` 用于识别上一同步点的历史结构。

因此：

> 上游历史的 squash 由 `git subtree --squash` 完成，不能交给 GitHub PR 的 Squash and merge 完成。

---

# 11. 后续同步

假设上次同步：

```text
<OLD_SHA>
```

现在 upstream 更新到：

```text
<NEW_SHA>
```

首先：

```bash
git fetch opu --tags
```

确定并记录：

```text
<NEW_SHA>
```

Core 和 Plugin 必须都使用该 SHA。

---

# 12. 更新 Core 缓存

```bash
git switch cache/opu-core
```

把新的 OPU history 合入缓存分支：

```bash
git merge opu/master
```

如果同步的是明确 tag/commit，也可以：

```bash
git merge <NEW_SHA>
```

然后：

```bash
git subtree split \
    --prefix=OpenUtau.Core \
    --rejoin \
    --squash \
    --branch opu-core/<NEW_SHA>
```

因为缓存分支已经存在上一次 rejoin 信息，所以这次不应该再次完整计算数千个旧 commit，而主要处理新增历史。

---

# 13. 更新 Plugin 缓存

```bash
git switch cache/opu-plugin
```

更新：

```bash
git merge <NEW_SHA>
```

然后：

```bash
git subtree split \
    --prefix=OpenUtau.Plugin.Builtin \
    --rejoin \
    --squash \
    --branch opu-plugin/<NEW_SHA>
```

---

# 14. 再次同步到 OPUM

```bash
git switch dev
git pull --ff-only origin dev
git switch -c chore/sync-opu-<NEW_SHA>
```

然后：

```bash
git subtree merge \
    --prefix=OpenUtau.Core \
    --squash \
    opu-core/<NEW_SHA> \
    -m "sync: OpenUtau.Core to <NEW_SHA>"
```

再：

```bash
git subtree merge \
    --prefix=OpenUtau.Plugin.Builtin \
    --squash \
    opu-plugin/<NEW_SHA> \
    -m "sync: OpenUtau.Plugin.Builtin to <NEW_SHA>"
```

处理冲突、构建、测试、提交 PR。

---

# 15. 同步完成后的清理

PR 合并以后，临时 synthetic branches 可以删除：

```bash
git branch -d opu-core/<NEW_SHA>
git branch -d opu-plugin/<NEW_SHA>
```

不要删除：

```text
cache/opu-core
cache/opu-plugin
```

因为它们保存 `--rejoin` 加速信息。

---

# 16. 新电脑或重新 clone

缓存分支是本地维护状态。

重新 clone 后不存在：

```text
cache/opu-core
cache/opu-plugin
```

因此第一次执行同步需要重新建立缓存，并重新进行第一次完整 split。

这属于正常的一次性成本。

不要为了保存缓存而把完整 OPU history/cache branch 推入 OPUM 官方仓库，否则会重新增加仓库历史和 clone 负担。

---

# 17. 禁止操作

以下操作禁止用于正式 OPU 同步。

## 禁止不带 squash 的 subtree merge

错误：

```bash
git subtree merge \
    --prefix=OpenUtau.Core \
    opu-core/<SHA>
```

正确：

```bash
git subtree merge \
    --prefix=OpenUtau.Core \
    --squash \
    opu-core/<SHA>
```

---

## 禁止直接 merge synthetic branch

错误：

```bash
git merge opu-core/<SHA>
```

这会把 OPU synthetic history直接接入 OPUM。

---

## 禁止把 rejoin 写进 dev

不要在：

```text
dev
chore/sync-opu-*
```

上执行：

```bash
git subtree split --rejoin ...
```

`--rejoin` 只在：

```text
cache/opu-core
cache/opu-plugin
```

上使用。

---

## 禁止 Core 与 Plugin 使用不同 upstream SHA

它们属于同一个同步单元。

---

## 禁止 GitHub Squash and merge

subtree 的 squash 与 GitHub PR squash 是两个不同层次的操作。

正式同步必须由：

```text
git subtree ... --squash
```

完成。

PR 本身使用：

```text
Create a merge commit
```

---

# 18. 同步流程速查

## 第一次在本机

```text
fetch OPU
    ↓
建立 cache/opu-core
    ↓
Core split --rejoin --squash
    ↓
建立 cache/opu-plugin
    ↓
Plugin split --rejoin --squash
    ↓
建立 chore/sync-opu-<SHA>
    ↓
Core subtree merge --squash
    ↓
Plugin subtree merge --squash
    ↓
解决冲突
    ↓
build / test
    ↓
PR -> dev
    ↓
Create a merge commit
```

## 后续

```text
fetch OPU
    ↓
更新两个 cache 分支
    ↓
Core split --rejoin --squash
Plugin split --rejoin --squash
    ↓
建立同步 PR
    ↓
两个 subtree merge --squash
    ↓
build / test
    ↓
PR -> dev
    ↓
Create a merge commit
```

---

# 19. 一句话原则

> `rejoin` 只负责本地 split 加速，`squash` 负责防止上游历史污染 OPUM；Core 和 Plugin.Builtin 始终作为同一个 OPU 版本同步单元。
