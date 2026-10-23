#!/usr/bin/env python3
"""以草稿暂存附件，并通过远端归属标记提交或回滚发布事务。"""

import argparse
import base64
import hashlib
import json
import os
from pathlib import Path
import re
import subprocess
import time
from urllib.error import HTTPError, URLError
from urllib.parse import quote
from urllib.request import Request, urlopen


VERSION = re.compile(r"v(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)")
# 固定接口契约，不自动跟随最新版；迁移前需要核对官方破坏性变更说明。
API_VERSION = "2022-11-28"


def require_fields(value, fields, resource):
    """校验清理所依赖的字段；忽略新增字段，拒绝缺失字段或类型变化。"""
    if not isinstance(value, dict):
        raise RuntimeError(f"GitHub API 返回结构不兼容：{resource} 应为对象")
    for name, accepted in fields.items():
        types = accepted if isinstance(accepted, tuple) else (accepted,)
        if name not in value or type(value[name]) not in types:
            raise RuntimeError(f"GitHub API 返回结构不兼容：{resource}.{name} 缺失或类型变化")
    return value


class Transaction:
    """将运行次数和提交 SHA 写入远端，供失败清理核对资源归属。"""

    def __init__(self):
        self.repo = os.environ["GITHUB_REPOSITORY"]
        self.source = os.environ["SOURCE_SHA"]
        self.run_attempt = int(os.environ["RELEASE_RUN_ATTEMPT"])
        self.identity_prefix = f"opum-release:{self.repo}:{os.environ['RELEASE_RUN_ID']}:"
        self.identity = f"{self.identity_prefix}{self.run_attempt}"
        self.base = f"{os.environ['GITHUB_API_URL']}/repos/{self.repo}/"
        self.pending = f"<!-- {self.identity}:pending -->"
        self.committed = f"<!-- {self.identity}:committed -->"
        self.api_warnings = set()

    def validate_response(self, method, path, value):
        """检查关键接口契约，避免接口变化后把异常数据误判为没有待清理资源。"""
        endpoint = path.split("?", 1)[0]
        if (endpoint == "releases" and method == "GET") or endpoint == "tags" or endpoint.endswith("/assets"):
            if not isinstance(value, list):
                raise RuntimeError(f"GitHub API 返回结构不兼容：{endpoint} 应为列表")
            for item in value:
                if endpoint == "releases":
                    self.validate_release(item)
                elif endpoint == "tags":
                    require_fields(item, {"name": str, "commit": dict}, "Tag 列表")
                    require_fields(item["commit"], {"sha": str}, "Tag 提交")
        elif endpoint.startswith(("git/ref/", "git/refs")):
            require_fields(value, {"ref": str, "object": dict}, "Tag 引用")
            require_fields(value["object"], {"type": str, "sha": str}, "Tag 引用目标")
            if value["object"]["type"] not in ("tag", "commit", "tree", "blob"):
                raise RuntimeError("GitHub API 返回结构不兼容：未知的引用目标类型")
        elif endpoint.startswith("git/tags"):
            require_fields(value, {"sha": str, "tag": str, "message": str, "object": dict}, "Tag 附注")
            require_fields(value["object"], {"type": str, "sha": str}, "Tag 附注目标")
            if value["object"]["type"] not in ("tag", "commit", "tree", "blob"):
                raise RuntimeError("GitHub API 返回结构不兼容：未知的 Tag 附注目标类型")
        elif endpoint == "releases/generate-notes":
            require_fields(value, {"body": str}, "自动发布说明")
        elif endpoint == "releases" or re.fullmatch(r"releases/[0-9]+", endpoint):
            self.validate_release(value)

    @staticmethod
    def validate_release(value):
        """缺少正文或草稿状态时中止，避免误删已完成的发布。"""
        return require_fields(value, {"id": int, "tag_name": str, "body": (str, type(None)), "draft": bool}, "Release")

    def release_state(self, release):
        """识别同一运行已发生的发布事务，使单独重跑失败清理 job 也能处理之前的尝试。"""
        match = re.search(rf"<!-- {re.escape(self.identity_prefix)}([1-9][0-9]*):(pending|committed) -->\Z", release.get("body") or "")
        return match[2] if match is not None and int(match[1]) <= self.run_attempt else None

    def api(self, method, path, data=None, missing_ok=False):
        """限制请求时间；只自动重试可安全重复的读取、删除和更新。"""
        request = Request(
            self.base + path,
            data=None if data is None else json.dumps(data).encode(),
            method=method,
            headers={
                "Authorization": f"Bearer {os.environ['GH_TOKEN']}",
                "Accept": "application/vnd.github+json",
                "Content-Type": "application/json",
                "X-GitHub-Api-Version": API_VERSION,
            },
        )
        for attempt in range(3):
            try:
                with urlopen(request, timeout=60) as response:
                    for header in ("Deprecation", "Sunset"):
                        notice = response.headers.get(header)
                        if notice and (header, notice) not in self.api_warnings:
                            self.api_warnings.add((header, notice))
                            print(f"::warning::GitHub API {API_VERSION} 返回 {header}: {notice}；请核对官方迁移说明", flush=True)
                    expected_status = 204 if method == "DELETE" else 201 if method == "POST" and path != "releases/generate-notes" else 200
                    if response.status != expected_status:
                        raise RuntimeError(f"GitHub API 契约异常：{method} {path} 返回 {response.status}，预期 {expected_status}")
                    body = response.read()
                    if method == "DELETE":
                        return None
                    try:
                        value = json.loads(body)
                    except (ValueError, UnicodeDecodeError) as error:
                        raise RuntimeError(f"GitHub API 返回无效 JSON：{method} {path}") from error
                    self.validate_response(method, path, value)
                    return value
            except HTTPError as error:
                if error.code == 410:
                    raise RuntimeError(f"GitHub API {API_VERSION} 或接口 {path} 已不可用（HTTP 410）；请核对版本支持和迁移说明") from error
                if missing_ok and error.code == 404:
                    return None
                if error.code != 429 and error.code < 500:
                    raise
                failure = error
            except (URLError, TimeoutError, OSError) as error:
                failure = error
            # 创建请求的响应可能丢失，交给回滚从远端查找，不能盲目重发。
            if method == "POST" or attempt == 2:
                raise failure
            time.sleep(5 * (attempt + 1))

    def delete_and_verify(self, path, lookup, resource):
        """删除远端资源并重新读取确认；响应丢失或 DELETE 返回 404 均不能直接算成功。"""
        deletion_error = None
        try:
            self.api("DELETE", path, missing_ok=True)
        except (HTTPError, URLError, TimeoutError, OSError) as error:
            deletion_error = error
        for attempt in range(3):
            if self.api("GET", lookup, missing_ok=True) is None:
                return
            if attempt < 2:
                time.sleep(2 * (attempt + 1))
        raise RuntimeError(f"回滚未完成：远端 {resource} 仍然存在（{lookup}）") from deletion_error

    def pages(self, path):
        """遍历完整列表，避免草稿、历史版本或附件超过一页时漏查。"""
        for page in range(1, 10000):
            items = self.api("GET", f"{path}?per_page=100&page={page}")
            yield from items
            if len(items) < 100:
                return
        raise RuntimeError("API 分页数量超出预期")

    def git(self, *args):
        """通过环境配置传递临时认证，避免把令牌写入命令行、远端 URL 或 Git 配置文件。"""
        environment = os.environ.copy()
        count = int(environment.get("GIT_CONFIG_COUNT", "0"))
        authorization = base64.b64encode(f"x-access-token:{os.environ['GH_TOKEN']}".encode()).decode()
        configuration = {
            f"http.{os.environ['GITHUB_SERVER_URL']}/.extraheader": f"AUTHORIZATION: basic {authorization}",
            "credential.helper": "",
        }
        for offset, (key, value) in enumerate(configuration.items()):
            environment[f"GIT_CONFIG_KEY_{count + offset}"] = key
            environment[f"GIT_CONFIG_VALUE_{count + offset}"] = value
        environment["GIT_CONFIG_COUNT"] = str(count + len(configuration))
        environment["GIT_TERMINAL_PROMPT"] = "0"
        return subprocess.run(["git", *args], env=environment, check=True, capture_output=True, text=True, timeout=120).stdout

    def remote_tag_sha(self, tag):
        """直接通过 Git 协议检查远端引用，避免把 REST 接口异常返回的 404 当成删除成功。"""
        ref = f"refs/tags/{tag}"
        output = self.git("ls-remote", "--refs", "origin", ref).strip()
        if not output:
            return None
        fields = output.split()
        if len(fields) != 2 or fields[1] != ref or re.fullmatch(r"[0-9a-f]{40}", fields[0]) is None:
            raise RuntimeError(f"无法识别远端 Tag 引用：{tag}")
        return fields[0]

    def delete_tag(self, tag, expected_sha):
        """通过 Git 删除已推送的 Tag；服务端必须仍指向已核对归属的对象，删除后再查远端。"""
        ref = f"refs/tags/{tag}"
        for attempt in range(3):
            current = self.remote_tag_sha(tag)
            if current is None:
                return
            if current != expected_sha:
                raise RuntimeError(f"拒绝删除已被其他操作修改的远端 Tag：{tag}")
            try:
                self.git("push", "--porcelain", "--no-follow-tags", f"--force-with-lease={ref}:{expected_sha}", "origin", f":{ref}")
            except (subprocess.CalledProcessError, subprocess.TimeoutExpired) as error:
                print(f"::warning::删除远端 Tag 异常，将重新核对远端状态：{error}", flush=True)
            if self.remote_tag_sha(tag) is None:
                return
            if attempt < 2:
                time.sleep(5 * (attempt + 1))
        raise RuntimeError(f"回滚未完成：远端 Tag {tag} 仍然存在；请检查令牌权限和 Tag 保护规则")

    def tag_owner(self, tag):
        """同时检查附注、运行次数和目标提交，拒绝处理外部创建的 Tag。"""
        ref = self.api("GET", f"git/ref/tags/{quote(tag, safe='')}", missing_ok=True)
        if ref is None:
            if self.remote_tag_sha(tag) is not None:
                raise RuntimeError(f"GitHub API 返回 404，但 Git 远端仍存在 Tag {tag}；请检查接口或权限")
            return None
        if ref["object"]["type"] != "tag":
            return None
        if ref["ref"] != f"refs/tags/{tag}":
            raise RuntimeError(f"GitHub API 返回了错误的 Tag 引用：{tag}")
        annotation = self.api("GET", f"git/tags/{ref['object']['sha']}")
        if annotation["tag"] != tag or annotation["sha"] != ref["object"]["sha"]:
            raise RuntimeError(f"GitHub API 返回了不匹配的 Tag 附注：{tag}")
        if annotation["object"]["type"] != "commit" or annotation["object"]["sha"] != self.source:
            return None
        lines = annotation["message"].splitlines()
        match = re.fullmatch(rf"{re.escape(self.identity_prefix)}([1-9][0-9]*):(none|draft|publish)", lines[-1] if lines else "")
        if match is not None and int(match[1]) <= self.run_attempt:
            return match[2], ref["object"]["sha"], int(match[1])
        return None

    def rollback(self, tag=None):
        """先删除未提交的草稿及附件，再删除归属于本次运行的正式 Tag。"""
        releases = list(self.pages("releases"))
        candidates = {tag} if tag else {
            item["name"] for item in self.pages("tags")
            if VERSION.fullmatch(item["name"]) and item["commit"]["sha"] == self.source
        }
        # 即使 Tag 已被外部删除，仍能发现并清理本事务留下的草稿。
        candidates.update(
            release["tag_name"] for release in releases
            if self.release_state(release) is not None
        )
        committed = False
        for candidate in sorted(candidates):
            related = [release for release in releases if release["tag_name"] == candidate]
            if any(self.release_state(release) == "committed" for release in related):
                print(f"保留已提交的发布：{candidate}", flush=True)
                committed = True
                continue
            owner = self.tag_owner(candidate)
            if owner is None and not any(self.release_state(release) == "pending" for release in related):
                continue
            for release in related:
                # 删除前重新读取，避免列表快照掩盖期间已提交的发布。
                release = self.api("GET", f"releases/{release['id']}", missing_ok=True)
                if release is None:
                    continue
                if self.release_state(release) == "committed":
                    print(f"保留已提交的发布：{candidate}", flush=True)
                    committed = True
                    break
                # 有外部 Release 时保留 Tag，避免误删人工修复或其他运行的资源。
                if release["tag_name"] != candidate or self.release_state(release) != "pending" or not release["draft"]:
                    raise RuntimeError(f"拒绝回滚归属不符或已公开的 Release：{candidate}")
                self.delete_and_verify(f"releases/{release['id']}", f"releases/{release['id']}", f"Release {candidate}")
                print(f"已回滚草稿及附件：{candidate}", flush=True)
            else:
                if owner is not None:
                    self.delete_tag(candidate, owner[1])
                    print(f"已回滚远端 Tag：{candidate}", flush=True)
        return committed

    def assets(self, build_number):
        """核对每个平台的预期文件，下载或文件检查失败时不创建远端资源。"""
        packages = {
            **{f"android-{arch}": f"android-{arch}.apk" for arch in ("arm64", "armhf", "x64")},
            **{f"linux-{arch}": f"linux-{arch}.tar.gz" for arch in ("x64", "arm64")},
            **{f"windows-{arch}": f"windows-{arch}.zip" for arch in ("x64", "arm64")},
            **{f"macos-{arch}": f"macos-{arch}.tar.gz" for arch in ("x64", "arm64")},
            "ios-arm64-mono-aot-unsigned": "ios-arm64-mono-aot-unsigned.ipa",
            "browser-wasm": "browser-wasm.tar.gz",
        }
        result = []
        for artifact, suffix in packages.items():
            filename = f"OpenUtauMobile-build{build_number}-{suffix}"
            if artifact == "browser-wasm":
                filename = "OpenUtauMobile-browser-wasm.tar.gz"
            path = Path("release-assets") / f"OpenUtauMobile-{artifact}" / filename
            files = [path]
            if artifact.startswith(("android-", "ios-", "browser-")):
                files.append(path.with_name(path.name + ".sha256"))
            for file in files:
                if not file.is_file() or file.stat().st_size == 0:
                    raise RuntimeError(f"发布文件缺失或为空：{file}")
                with file.open("rb") as stream:
                    digest = hashlib.file_digest(stream, "sha256").hexdigest()
                result.append((file, file.stat().st_size, f"sha256:{digest}"))
            if len(files) == 2:
                checksum = files[1].read_text().split()
                if checksum != [result[-2][2].removeprefix("sha256:"), path.name]:
                    raise RuntimeError(f"SHA-256 校验失败：{path}")
        return result

    def upload(self, tag, release_id, asset):
        """每次只上传一个文件；响应丢失时先核对远端，残缺附件删除后再重试。"""
        path, size, digest = asset
        for attempt in range(3):
            remote = next((item for item in self.pages(f"releases/{release_id}/assets") if item["name"] == path.name), None)
            if remote is not None:
                if remote["state"] == "uploaded" and remote["size"] == size and remote.get("digest") == digest:
                    print(f"附件已完整上传：{path.name}", flush=True)
                    return
                self.api("DELETE", f"releases/assets/{remote['id']}", missing_ok=True)
            print(f"上传 {path.name}，第 {attempt + 1}/3 次", flush=True)
            try:
                subprocess.run(
                    ["gh", "release", "upload", tag, str(path), "--repo", self.repo],
                    check=True, timeout=600,
                )
            except (subprocess.CalledProcessError, subprocess.TimeoutExpired) as error:
                print(f"::warning::附件上传异常，将核对远端后重试：{error}", flush=True)
            # 命令成功也必须核对服务端状态、大小和摘要，不能只依赖退出码。
            remote = next((item for item in self.pages(f"releases/{release_id}/assets") if item["name"] == path.name), None)
            if remote is not None and remote["state"] == "uploaded" and remote["size"] == size and remote.get("digest") == digest:
                return
            if attempt < 2:
                time.sleep(10 * (attempt + 1))
        raise RuntimeError(f"附件上传或完整性检查连续失败：{path.name}")

    def release(self):
        """准备完成后创建 Tag 和草稿，最后以一次更新提交发布及完成标记。"""
        tag = os.environ["TAG"]
        mode = os.environ["RELEASE_MODE"]
        if VERSION.fullmatch(tag) is None or mode not in ("none", "draft", "publish"):
            raise RuntimeError("发布版本或发布模式无效")
        assets = self.assets(os.environ["BUILD_NUMBER"]) if mode != "none" else []
        if self.api("GET", f"git/ref/tags/{tag}", missing_ok=True) is not None:
            raise RuntimeError(f"Tag {tag} 已存在，拒绝覆盖")
        if any(item["tag_name"] == tag for item in self.pages("releases")):
            raise RuntimeError(f"Release {tag} 已存在，拒绝覆盖")
        versions = [
            tuple(map(int, match.groups())) for item in self.pages("tags")
            if (match := VERSION.fullmatch(item["name"])) is not None
        ]
        current = tuple(map(int, VERSION.fullmatch(tag).groups()))
        if versions and current <= max(versions):
            raise RuntimeError(f"版本状态在构建期间发生变化：{tag} 不再高于最高正式版本")
        notes = ""
        if mode != "none":
            generated = self.api("POST", "releases/generate-notes", {"tag_name": tag, "target_commitish": self.source})
            notes = "\n\n".join(filter(None, [os.environ.get("VERSION_LYRICS"), os.environ.get("CUSTOM_NOTES"), generated["body"]]))
        annotation = self.api("POST", "git/tags", {
            "tag": tag, "message": f"OpenUtau Mobile {tag[1:]}\n\n{self.identity}:{mode}",
            "object": self.source, "type": "commit",
        })
        self.api("POST", "git/refs", {"ref": f"refs/tags/{tag}", "sha": annotation["sha"]})
        if mode == "none":
            print(f"已创建 Tag：{tag}", flush=True)
            return
        release = self.api("POST", "releases", {
            "tag_name": tag, "target_commitish": self.source, "name": f"OpenUtau Mobile {tag[1:]}",
            "body": f"{notes}\n\n{self.pending}", "draft": True,
        })
        for asset in assets:
            self.upload(tag, release["id"], asset)
        remote = list(self.pages(f"releases/{release['id']}/assets"))
        expected = {(path.name, size, digest) for path, size, digest in assets}
        actual = {(item["name"], item["size"], item.get("digest")) for item in remote if item["state"] == "uploaded"}
        if len(remote) != len(assets) or actual != expected:
            raise RuntimeError("提交发布前的附件清单校验失败")
        staged = self.api("GET", f"releases/{release['id']}")
        owner = self.tag_owner(tag)
        if owner is None or owner[0] != mode or owner[2] != self.run_attempt or not staged["draft"] or not (staged.get("body") or "").endswith(self.pending):
            raise RuntimeError("提交发布前发现 Tag 或草稿的事务状态被修改")
        # 完成标记和公开状态在同一请求中写入。响应丢失时，回滚会保留已提交的结果。
        self.api("PATCH", f"releases/{release['id']}", {
            "body": f"{notes}\n\n{self.committed}", "draft": mode == "draft",
        })
        print(f"发布事务已提交：{tag} ({mode})", flush=True)


def main():
    """提供发布与幂等回滚入口；异常时保留失败状态，供清理 job 再次兜底。"""
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("command", choices=("release", "rollback"))
    args = parser.parse_args()
    transaction = Transaction()
    if args.command == "rollback":
        transaction.rollback(os.environ.get("TAG") or None)
        return
    try:
        transaction.release()
    except Exception:
        # HTTP 超时不代表服务端失败；远端完成标记是事务提交的最终依据。
        if transaction.rollback(os.environ["TAG"]):
            print("已从远端确认发布事务提交成功", flush=True)
            return
        raise


if __name__ == "__main__":
    main()
