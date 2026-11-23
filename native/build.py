"""使用 MSBuild 继承的目标和配置构建原生库。"""
import argparse
from contextlib import contextmanager
import json
import os
from pathlib import Path
import platform
import re
import shutil
import subprocess
import sys

HERE = Path(__file__).resolve().parent
ROOT = HERE.parent


def write_changed(path, content):
    """仅在内容变化时写入，保留未变化产物的时间戳。"""
    path.parent.mkdir(parents=True, exist_ok=True)
    if not path.exists() or path.read_bytes() != content:
        path.write_bytes(content)


@contextmanager
def build_lock(path):
    """串行保护同一原生工作副本，操作系统在进程退出时释放锁。"""
    path.parent.mkdir(parents=True, exist_ok=True)
    with path.open('a+b') as lock:
        if not lock.tell():
            lock.write(b'0')
            lock.flush()
        if os.name == 'nt':
            import msvcrt
            import time
            lock.seek(0)
            while True:
                try:
                    msvcrt.locking(lock.fileno(), msvcrt.LK_NBLCK, 1)
                    break
                except OSError:
                    time.sleep(0.25)
        else:
            import fcntl
            fcntl.flock(lock, fcntl.LOCK_EX)
        try:
            yield
        finally:
            if os.name == 'nt':
                lock.seek(0)
                msvcrt.locking(lock.fileno(), msvcrt.LK_UNLCK, 1)
            else:
                fcntl.flock(lock, fcntl.LOCK_UN)


def stage_sources(library, destination):
    """同步构建副本，仅适配冻结上游的 Bazel 规则加载。"""
    files = {}
    if library == 'worldline':
        source_root = HERE / 'upstream_cpp'
        for source in sorted(source_root.rglob('*')):
            if not source.is_file():
                continue
            relative = source.relative_to(source_root).as_posix()
            if relative in ('.bazelrc', '.bazelversion', 'MODULE.bazel', 'MODULE.bazel.lock', 'WORKSPACE.bazel'):
                continue
            content = source.read_bytes()
            if source.name == 'BUILD.bazel' or source.suffix == '.BUILD':
                rules = sorted(set(re.findall(rb'^\s*(cc_\w+)\(', content, re.MULTILINE)))
                content = b''.join(b'load("@rules_cc//cc:' + rule + b'.bzl", "' + rule + b'")\n' for rule in rules) + content
            files[relative] = content
    for source in (HERE / library).iterdir():
        if source.is_file() and (source.suffix in ('.bazel', '.BUILD', '.bzl', '.cpp', '.py') or source.name == 'MODULE.bazel.lock'):
            files[source.name] = source.read_bytes()
    for name in ('.bazelrc', '.bazelversion', 'android-ndk-inputs.patch'):
        files[name] = (HERE / name).read_bytes()
    manifest = destination / '.opum-files.json'
    if manifest.is_file():
        for relative in set(json.loads(manifest.read_text())) - files.keys():
            obsolete = (destination / relative).resolve()
            if not obsolete.is_relative_to(destination.resolve()):
                raise ValueError('Invalid generated-file manifest path')
            obsolete.unlink(missing_ok=True)
    for relative, content in files.items():
        write_changed(destination / relative, content)
    write_changed(manifest, json.dumps(sorted(files)).encode())


def configure_static(settings, stage, environment):
    """将宿主 SDK 的实际编译与头文件参数接入 Bazel 静态工具链。"""
    def output(command):
        return subprocess.run(command, check=True, capture_output=True, text=True, env=environment).stdout.strip()

    rid = settings['RuntimeIdentifier']
    tool_environment = {}
    if rid == 'browser-wasm':
        sdk = Path(settings['EmscriptenSdkToolsPath']).resolve()
        cache = Path(settings['EmscriptenCacheSdkCacheDir']).resolve()
        sysroot = cache / 'sysroot'
        suffix = '.exe' if os.name == 'nt' else ''
        driver = Path(sys.executable)
        archiver = sdk / ('bin/llvm-ar' + suffix)
        node = Path(settings['EmscriptenNodeToolsPath']) / 'bin' / ('node' + suffix)
        target = 'wasm32-unknown-emscripten'
        flags = [(sdk / 'emscripten/emcc.py').as_posix(), '-fno-fast-math']
        tool_environment = {
            'DOTNET_EMSCRIPTEN_LLVM_ROOT': (sdk / 'bin').as_posix(),
            'DOTNET_EMSCRIPTEN_BINARYEN_ROOT': sdk.as_posix(),
            'DOTNET_EMSCRIPTEN_NODE_JS': node.resolve().as_posix(),
            'EM_CACHE': cache.as_posix(), 'PYTHONUTF8': '1',
        }
        if settings['WasmEnableThreads'].lower() == 'true':
            flags.append('-pthread')
        if settings['WasmEnableSIMD'].lower() == 'true':
            flags.append('-msimd128')
        flags.append('-fwasm-exceptions' if settings['WasmEnableExceptionHandling'].lower() == 'true' else '-fexceptions')
        constraints = ['@platforms//os:emscripten', '@platforms//cpu:wasm32']
    else:
        sdk_name = 'iphoneos' if rid == 'ios-arm64' else 'iphonesimulator'
        sysroot = Path(output(['xcrun', '--sdk', sdk_name, '--show-sdk-path']))
        driver = Path(output(['xcrun', '--sdk', sdk_name, '--find', 'clang']))
        archiver = Path(output(['xcrun', '--sdk', sdk_name, '--find', 'ar']))
        arch = 'x86_64' if rid.endswith('-x64') else 'arm64'
        target = arch + '-apple-ios' + settings['SupportedOSPlatformVersion']
        if sdk_name == 'iphonesimulator':
            target += '-simulator'
        flags = ['--target=' + target, '-isysroot', sysroot.as_posix(), '-fPIC', '-fno-fast-math']
        constraints = ['@platforms//os:ios', '@platforms//cpu:' + arch,
                       '//:ios_device' if sdk_name == 'iphoneos' else '//:ios_simulator']
    # 工具链需要实际的系统头文件搜索路径，不将文件系统根目录作为隐式依赖。
    probe = subprocess.run([str(driver), *flags, '-x', 'c++', '-E', '-v', '-'],
                           env=environment | tool_environment, input='', text=True, capture_output=True, check=True)
    includes = []
    collecting = False
    for line in probe.stderr.splitlines():
        if '#include <...> search starts here:' in line:
            collecting = True
        elif 'End of search list.' in line:
            collecting = False
        elif collecting:
            includes.append(Path(line.strip().removesuffix(' (framework directory)')).as_posix())
    if rid != 'browser-wasm':
        # Xcode 27 的依赖文件包含 SDKSettings.json，限定在所选 SDK 中。
        includes += [sysroot.as_posix(), sysroot.resolve().as_posix()]
    quote = json.dumps
    config = f'''load("@rules_cc//cc/toolchains:cc_toolchain.bzl", "cc_toolchain")
load("//:static_toolchain.bzl", "static_toolchain_config")
package(default_visibility = ["//visibility:public"])
filegroup(name = "empty")
static_toolchain_config(
    name = "config", compiler_path = {quote(driver.as_posix())},
    archiver_path = {quote(archiver.as_posix())}, target = {quote(target)},
    cpu = {quote(rid.split('-')[1])}, flags = {quote(flags)},
    builtin_includes = {quote(includes)}, environment = {quote(tool_environment)},
    archive_param_file = {rid == 'browser-wasm'},
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


def build(settings, request, run_tests, update_lock):
    """从统一参数构建一个库，直接安装 Bazel 的确定输出。"""
    library = settings['NativeLibrary']
    rid = settings['RuntimeIdentifier']
    build_root = ROOT / 'artifacts' / {'worldline': 'worldline-build', 'game': 'game-bazel-build'}[library]
    workspace = build_root / rid / 'workspace'
    stage_sources(library, workspace)
    env = dict(os.environ)
    if os.name == 'nt':
        # Windows 的 PATH 名称不区分大小写，子进程只保留一项。
        env = {key: value for key, value in env.items() if key.upper() != 'PATH'}
        env['Path'] = os.environ.get('PATH', '')
        if not env.get('BAZEL_SH'):
            git = Path(shutil.which('git')).resolve()
            for parent in git.parents[:3]:
                for relative in ('bin/bash.exe', 'usr/bin/bash.exe'):
                    candidate = parent / relative
                    if candidate.is_file():
                        env['BAZEL_SH'] = str(candidate)
                        break
                if env.get('BAZEL_SH'):
                    break
    env['OPUM_WORLDLINE_PYTHON'] = sys.executable
    # 原生图没有 Java 编译，Bazel 使用自带 JDK，避免 IDE 与 CLI 的 Android JDK 改变服务器启动参数。
    env.pop('JAVA_HOME', None)
    env['USE_BAZEL_VERSION'] = (HERE / '.bazelversion').read_text().strip()
    env.setdefault('BAZELISK_HOME', str(ROOT / 'artifacts/bazelisk'))
    windows = rid.startswith('win-')
    system = 'windows' if windows else 'unix'
    configs = [system, library] if library == 'worldline' else [system]
    if library == 'game' and not windows:
        configs.append('game-unix')
    target_os = rid.split('-')[0]
    if target_os in ('linux', 'osx') and (library == 'worldline' or target_os == 'osx'):
        configs.append(library + '-' + target_os)
    if rid.startswith('android-'):
        configs.append('android')
        env['ANDROID_NDK_HOME'] = Path(settings['AndroidNdkDirectory']).resolve().as_posix()
    if rid == 'win-x86':
        configs.append('win-x86')
    if library == 'game' and rid == 'win-arm64':
        host_arch = 'arm64' if platform.machine().lower() in ('aarch64', 'arm64') else 'x64'
        if not env.get('BAZEL_LLVM'):
            vswhere = Path(os.environ['ProgramFiles(x86)']) / 'Microsoft Visual Studio/Installer/vswhere.exe'
            installation = subprocess.run([str(vswhere), '-latest', '-prerelease', '-products', '*', '-property', 'installationPath'],
                                          check=True, capture_output=True, text=True).stdout.strip()
            for arch in (host_arch, 'x64', 'ARM64'):
                llvm = Path(installation) / 'VC/Tools/Llvm' / arch
                if (llvm / 'bin/clang-cl.exe').is_file():
                    env['BAZEL_LLVM'] = str(llvm)
                    break
        configs.append('game-win-arm64')
    static = rid.startswith(('ios-', 'iossimulator-')) or rid == 'browser-wasm'
    if static:
        configure_static(settings, workspace, env)
        configs.append('static')
        if rid != 'browser-wasm':
            configs.append('ios')
    options = ['--config=' + config for config in configs] + ['--platforms=//:' + rid]
    if library == 'game' and rid == 'win-arm64':
        options.append('--host_platform=//:win-' + host_arch + '-clang-host')
    if update_lock:
        options.append('--lockfile_mode=update')
    target = '//:opum_game' if library == 'game' else ('//:worldline_static' if static else '//worldline:worldline')
    command = [shutil.which('bazel'), '--nohome_rc', '--nosystem_rc', '--output_user_root=' + str(build_root / 'bazel')]
    subprocess.run(command + ['build', *options, target], cwd=workspace, env=env, check=True)
    if run_tests:
        targets = ['//worldline:worldline_test', '//worldline/model:effects_test', '//worldline/model:continuous_noise_test']
        if library == 'game':
            # 上游其余数值用例依赖模型和参考数据，只选择不需要外部数据的既有用例。
            targets = ['@game_cpp//:core_tests', '--test_arg=--gtest_filter=Version.*:Backend.*:ParseFlatIntObject.*:GgufFile.RoundtripMockFile:WavIO.*:Decode.*:Slicer.*:MidiWriter.*:TextWriter.*']
        subprocess.run(command + ['test', *options, *targets], cwd=workspace, env=env, check=True)
    if update_lock:
        write_changed(HERE / library / 'MODULE.bazel.lock', (workspace / 'MODULE.bazel.lock').read_bytes())
    filename = settings['NativeFileName']
    output = workspace / 'bazel-bin'
    if static:
        output /= 'libworldline_static.a'
    else:
        output /= 'worldline' if library == 'worldline' else '.'
        output /= filename
    write_changed(request.parent / filename, output.read_bytes())
    print(f'Native {library} {rid}: {request.parent / filename}', flush=True)


def main():
    """读取 MSBuild 生成的同一份参数，不再另设平台、架构或工具链默认值。"""
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('input', type=Path)
    parser.add_argument('--test', action='store_true')
    parser.add_argument('--update-lock', action='store_true')
    args = parser.parse_args()
    request = args.input.resolve()
    settings = dict(line.split('=', 1) for line in request.read_text(encoding='utf-8-sig').splitlines() if '=' in line)
    library = settings['NativeLibrary']
    rid = settings['RuntimeIdentifier']
    with build_lock(ROOT / 'artifacts/native-locks' / library / (rid + '.lock')):
        build(settings, request, args.test, args.update_lock)


if __name__ == '__main__':
    main()
