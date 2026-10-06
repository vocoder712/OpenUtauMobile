"""将平台 SDK 的 Clang/LLVM 接入静态库构建，不引入另一套 SDK。"""

load("@rules_cc//cc:cc_toolchain_config_lib.bzl", "action_config", "env_entry", "env_set", "feature", "flag_group", "flag_set", "tool")
load("@rules_cc//cc/common:cc_common.bzl", "cc_common")
load("@rules_cc//cc/toolchains:cc_toolchain_config_info.bzl", "CcToolchainConfigInfo")

_COMPILE = ["c-compile", "c++-compile", "assemble", "preprocess-assemble"]

def _flags(name, actions, flags, **kwargs):
    if "iterate_over" in kwargs:
        kwargs["expand_if_available"] = kwargs["iterate_over"]
    return feature(name = name, enabled = True, flag_sets = [flag_set(
        actions = actions,
        flag_groups = [flag_group(flags = flags, **kwargs)],
    )])

def _impl(ctx):
    features = [
        feature(name = "sdk_environment", enabled = True, env_sets = [env_set(
            actions = _COMPILE,
            env_entries = [env_entry(key = key, value = value) for key, value in ctx.attr.environment.items()],
        )]),
        feature(name = "no_legacy_features", enabled = True),
        feature(name = "archive_param_file", enabled = ctx.attr.archive_param_file),
        _flags("sdk", _COMPILE, ctx.attr.flags),
        _flags("source", _COMPILE, ["-c", "%{source_file}"]),
        _flags("output", _COMPILE, ["-o", "%{output_file}"]),
        _flags("dependency", _COMPILE, ["-MD", "-MF", "%{dependency_file}"], expand_if_available = "dependency_file"),
        _flags("defines", _COMPILE, ["-D%{preprocessor_defines}"], iterate_over = "preprocessor_defines"),
        _flags("includes", _COMPILE, ["-I%{include_paths}"], iterate_over = "include_paths"),
        _flags("quote_includes", _COMPILE, ["-iquote", "%{quote_include_paths}"], iterate_over = "quote_include_paths"),
        _flags("system_includes", _COMPILE, ["-isystem", "%{system_include_paths}"], iterate_over = "system_include_paths"),
        _flags("forced_includes", _COMPILE, ["-include", "%{includes}"], iterate_over = "includes"),
        _flags("user_compile_flags", _COMPILE, ["%{user_compile_flags}"], iterate_over = "user_compile_flags"),
        _flags("archive", ["c++-link-static-library"], ["rcs", "%{output_execpath}"]),
    ]
    return cc_common.create_cc_toolchain_config_info(
        ctx = ctx,
        features = features,
        action_configs = [action_config(action_name = name, enabled = True,
                                        tools = [tool(path = ctx.attr.compiler_path)]) for name in _COMPILE] + [
            action_config(action_name = "c++-link-static-library", enabled = True,
                          tools = [tool(path = ctx.attr.archiver_path)]),
        ],
        cxx_builtin_include_directories = ctx.attr.builtin_includes,
        toolchain_identifier = "opum-worldline-static",
        host_system_name = "local",
        target_system_name = ctx.attr.target,
        target_cpu = ctx.attr.cpu,
        target_libc = "sdk",
        compiler = "clang",
        abi_version = "sdk",
        abi_libc_version = "sdk",
    )

static_toolchain_config = rule(
    implementation = _impl,
    attrs = {
        "compiler_path": attr.string(mandatory = True),
        "archiver_path": attr.string(mandatory = True),
        "target": attr.string(mandatory = True),
        "cpu": attr.string(mandatory = True),
        "flags": attr.string_list(),
        "builtin_includes": attr.string_list(),
        "environment": attr.string_dict(),
        "archive_param_file": attr.bool(),
    },
    provides = [CcToolchainConfigInfo],
)
