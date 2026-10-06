"""为 iOS 和 .NET Browser 生成使用宿主 SDK 的 Bazel 工具链。"""
import hashlib
import json
import os
import platform
from pathlib import Path
import subprocess
import sys

from bazel_common import write_changed

STATIC_RIDS = ('ios-arm64', 'iossimulator-arm64', 'iossimulator-x64', 'browser-wasm')


def configure(args, parser, stage):
    def output(command):
        return subprocess.run(command, check=True, capture_output=True, text=True).stdout.strip()

    environment = {}
    if args.rid == 'browser-wasm':
        if not args.emscripten or not args.emscripten_cache:
            parser.error('Browser requires --emscripten and --emscripten-cache from the .NET wasm-tools workload; use MSBuild BuildWorldlineNative.')
        sdk = args.emscripten.resolve()
        sysroot = args.emscripten_cache.resolve() / 'sysroot'
        suffix = '.exe' if platform.system() == 'Windows' else ''
        compiler = sdk / ('bin/clang' + suffix)
        driver = Path(sys.executable)
        archiver = sdk / ('bin/llvm-ar' + suffix)
        nm = sdk / ('bin/llvm-nm' + suffix)
        target = 'wasm32-unknown-emscripten'
        flags = [(sdk / 'emscripten/emcc.py').as_posix(), '-fno-fast-math']
        node = args.emscripten_node
        if node is None:
            node_pack = sdk.parent.parent.name.replace('.Sdk.', '.Node.')
            node = sdk.parents[2] / node_pack / sdk.parent.name / 'tools/bin' / ('node' + suffix)
        if not node.is_file():
            parser.error(f'Missing .NET Emscripten Node: {node}')
        environment = {'DOTNET_EMSCRIPTEN_LLVM_ROOT': (sdk / 'bin').as_posix(),
                       'DOTNET_EMSCRIPTEN_BINARYEN_ROOT': sdk.as_posix(),
                       'DOTNET_EMSCRIPTEN_NODE_JS': node.resolve().as_posix(),
                       'EM_CACHE': args.emscripten_cache.resolve().as_posix(),
                       'PYTHONUTF8': '1'}
        if args.wasm_threads == 'true':
            flags += ['-pthread']
        if args.wasm_simd == 'true':
            flags += ['-msimd128']
        flags += ['-fwasm-exceptions' if args.wasm_exceptions == 'true' else '-fexceptions']
        sdk_version = (sdk / 'emscripten/emscripten-version.txt').read_text().strip()
        constraints = ['@platforms//os:emscripten', '@platforms//cpu:wasm32']
    else:
        if platform.system() != 'Darwin':
            parser.error('iOS source builds require macOS and Xcode (including the iOS SDK).')
        sdk_name = 'iphoneos' if args.rid == 'ios-arm64' else 'iphonesimulator'
        sysroot = Path(output(['xcrun', '--sdk', sdk_name, '--show-sdk-path']))
        compiler = Path(output(['xcrun', '--sdk', sdk_name, '--find', 'clang']))
        driver = compiler
        archiver = Path(output(['xcrun', '--sdk', sdk_name, '--find', 'ar']))
        nm = Path(output(['xcrun', '--sdk', sdk_name, '--find', 'nm']))
        sdk = compiler.parent.parent
        arch = 'x86_64' if args.rid.endswith('-x64') else 'arm64'
        target = arch + '-apple-ios' + args.ios_minimum
        if sdk_name == 'iphonesimulator':
            target += '-simulator'
        flags = ['--target=' + target, '-isysroot', sysroot.as_posix(), '-fPIC', '-fno-fast-math']
        sdk_version = output(['xcrun', '--sdk', sdk_name, '--show-sdk-version'])
        constraints = ['@platforms//os:ios', '@platforms//cpu:' + arch,
                       '//:ios_device' if sdk_name == 'iphoneos' else '//:ios_simulator']
    for required in (compiler, archiver, nm, sysroot):
        if not required.exists():
            parser.error(f'Missing SDK component: {required}')
    # 查询实际的系统头文件搜索路径；不能用 / 放行所有未声明头文件。
    probe = subprocess.run([str(driver), *flags, '-x', 'c++', '-E', '-v', '-'],
                           env=os.environ | environment, input='', text=True, capture_output=True, check=True)
    includes = []
    collecting = False
    for line in probe.stderr.splitlines():
        if '#include <...> search starts here:' in line:
            collecting = True
        elif 'End of search list.' in line:
            collecting = False
        elif collecting:
            includes.append(Path(line.strip().removesuffix(' (framework directory)')).as_posix())
    if not includes:
        raise RuntimeError('Clang did not report system include directories')
    if args.rid != 'browser-wasm':
        # Xcode 27 将 SDKSettings.json 也写入依赖文件；限于选定 SDK，兼容 Xcode 路径符号链接。
        includes += [sysroot.as_posix(), sysroot.resolve().as_posix()]
    compiler_version = output([str(compiler), '--version'])
    # SDK 原地升级时也使 Bazel 编译动作失效，不仅依靠安装路径区分版本。
    environment['OPUM_WORLDLINE_SDK_FINGERPRINT'] = hashlib.sha256((sdk_version + compiler_version).encode()).hexdigest()
    flags += ['-O2', '-DNDEBUG']
    quote = json.dumps
    config = f'''load("@rules_cc//cc/toolchains:cc_toolchain.bzl", "cc_toolchain")
load("//:static_toolchain.bzl", "static_toolchain_config")
package(default_visibility = ["//visibility:public"])
filegroup(name = "empty")
static_toolchain_config(
    name = "config", compiler_path = {quote(driver.as_posix())},
    archiver_path = {quote(archiver.as_posix())}, target = {quote(target)},
    cpu = {quote(args.rid.split('-')[1])}, flags = {quote(flags)},
    builtin_includes = {quote(includes)},
    environment = {quote(environment)},
    archive_param_file = {args.rid == 'browser-wasm'},
)
cc_toolchain(
    name = "cc", toolchain_identifier = "opum-worldline-static",
    toolchain_config = ":config", all_files = ":empty", compiler_files = ":empty",
    linker_files = ":empty", ar_files = ":empty", as_files = ":empty",
    dwp_files = ":empty", objcopy_files = ":empty", strip_files = ":empty",
    supports_param_files = 1,
)
toolchain(name = "toolchain", toolchain = ":cc",
          toolchain_type = "@bazel_tools//tools/cpp:toolchain_type",
          target_compatible_with = {quote(constraints)})
'''
    write_changed(stage / 'sdk_toolchain/BUILD.bazel', config.encode())
    return {'sdk': str(sdk), 'sdk_version': sdk_version, 'sysroot': str(sysroot),
            'target': target, 'flags': flags, 'nm': str(nm), 'environment': environment,
            'compiler': str(compiler), 'compiler_version': compiler_version,
            'toolchain_sha256': hashlib.sha256(config.encode()).hexdigest()}
