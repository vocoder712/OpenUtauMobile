import gzip
import hashlib
import json
import re
import sys
from functools import cache
from pathlib import Path


def configure_development(manifest_path):
    """为 WasmAppHost 使用的开发端点设置禁止缓存。"""
    manifest = json.loads(manifest_path.read_bytes())
    changed = False
    for endpoint in manifest["Endpoints"]:
        for header in endpoint["ResponseHeaders"]:
            if header["Name"] == "Cache-Control" and header["Value"] != "no-store":
                header["Value"] = "no-store"
                changed = True
    if changed:
        manifest_path.write_text(json.dumps(manifest, ensure_ascii=False), encoding="utf-8")


def fingerprint_assets(publish_dir, target_name, version, commit, build_number):
    """改写项目静态引用，复用 SDK 对程序集和运行时资源生成的指纹。"""
    root = publish_dir / "wwwroot"
    source = Path(__file__).parent / "wwwroot"
    manifest_path = publish_dir / f"{target_name}.staticwebassets.endpoints.json"
    manifest = json.loads(manifest_path.read_bytes())
    immutable = {
        item["Route"]
        for item in manifest["Endpoints"]
        if any(prop["Name"] == "fingerprint" for prop in item["EndpointProperties"])
        and item["Route"] == item["AssetFile"]
    }
    modules = {path.name for path in source.glob("*.js")}
    assets = modules | {"app.css", "_framework/dotnet.js"}
    relative_asset = re.compile(r"""(["'])\./([^"'\r\n]+)\1""")

    def rewrite(match):
        quote, name = match.groups()
        if name not in assets:
            return match.group()
        return f"{quote}./{fingerprint(name)}{quote}"

    @cache
    def fingerprint(name):
        path = root / name
        if name in modules:
            data = relative_asset.sub(rewrite, path.read_text(encoding="utf-8")).encode("utf-8")
        else:
            data = path.read_bytes()
        digest = hashlib.sha256(data).hexdigest()[:16]
        output = path.with_name(f"{path.stem}.{digest}{path.suffix}")
        output.write_bytes(data)
        if name == "_framework/dotnet.js":
            for extension in (".br", ".gz"):
                compressed = path.with_suffix(path.suffix + extension)
                if compressed.exists():
                    output.with_suffix(output.suffix + extension).write_bytes(compressed.read_bytes())
        route = output.relative_to(root).as_posix()
        immutable.add(route)
        return route

    # JSHost.ImportAsync 从文档解析绝对模块 URL，静态引用及 AudioWorklet 则直接使用指纹文件名。
    imports = {f"/{name}": f"./{fingerprint(name)}" for name in sorted(modules)}
    index = relative_asset.sub(rewrite, (source / "index.html").read_text(encoding="utf-8"))
    routes = sorted(immutable)
    build_id = hashlib.sha256("\n".join(routes).encode("utf-8")).hexdigest()[:16]
    build = {
        "version": version,
        "commit": commit,
        "buildNumber": build_number,
        "buildId": build_id,
        "runtimeLoader": fingerprint("_framework/dotnet.js"),
    }
    import_map = json.dumps({"imports": imports}, separators=(",", ":"))
    build_info = json.dumps(build, separators=(",", ":")).replace("<", "\\u003c")
    index = index.replace(
        "</head>",
        f'<script type="importmap">{import_map}</script>\n'
        f'<script type="application/json" id="opum-build">{build_info}</script>\n</head>',
        1,
    ).encode("utf-8")
    (root / "version.json").write_text(
        json.dumps({**build, "immutable": routes}, indent=2) + "\n", encoding="utf-8",
    )
    # 发布索引改写后，SDK 生成的压缩副本也必须对应新内容。
    (root / "index.html.br").unlink(missing_ok=True)
    (root / "index.html.gz").write_bytes(gzip.compress(index, mtime=0))
    (root / "index.html").write_bytes(index)


if __name__ == "__main__":
    mode, path, *args = sys.argv[1:]
    if mode == "dev":
        configure_development(Path(path))
    else:
        fingerprint_assets(Path(path), *args)
