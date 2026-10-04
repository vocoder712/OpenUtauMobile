"""固定旧版源码归档，只适配其 Bazel 构建定义。"""

def _frozen_archive_impl(ctx):
    ctx.download_and_extract(
        url = ctx.attr.urls,
        integrity = ctx.attr.integrity,
        stripPrefix = ctx.attr.strip_prefix,
    )
    if ctx.attr.build_file:
        ctx.file("BUILD.bazel", ctx.read(ctx.attr.build_file))
    python = ctx.os.environ.get("OPUM_WORLDLINE_PYTHON")
    if not python:
        fail("Use native/worldline/build.py to provide the Python build adapter.")
    result = ctx.execute([python, ctx.path(ctx.attr._adapter)], timeout = 60)
    if result.return_code:
        fail("Bazel build adaptation failed: " + result.stderr)

frozen_archive = repository_rule(
    implementation = _frozen_archive_impl,
    attrs = {
        "urls": attr.string_list(mandatory = True),
        "integrity": attr.string(mandatory = True),
        "strip_prefix": attr.string(mandatory = True),
        "build_file": attr.label(allow_single_file = True),
        "_adapter": attr.label(default = "//:adapt_build.py", allow_single_file = True),
    },
    environ = ["OPUM_WORLDLINE_PYTHON"],
)
