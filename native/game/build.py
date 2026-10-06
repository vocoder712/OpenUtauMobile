"""使用固定 Bazel 和源码依赖构建 GAME/ggml CPU 原生库。"""
import argparse
import hashlib
import json
import locale
import os
from pathlib import Path
import platform
import shutil
import subprocess
import sys

HERE = Path(__file__).resolve().parent
ROOT = HERE.parent.parent
sys.path.insert(0, str(HERE.parent))
from bazel_common import build_lock, configure_windows_bash, write_changed
from worldline.verify import verify_binary

EXPORTS = {'opum_game_open', 'opum_game_close', 'opum_game_infer', 'opum_game_error'}
RIDS = ('win-x64', 'win-arm64', 'win-x86', 'linux-x64', 'linux-arm64',
        'osx-x64', 'osx-arm64', 'android-arm64', 'android-arm', 'android-x64', 'android-x86')
LICENSES = {'game_cpp': 'game.cpp', 'ggml': 'ggml', 'pocketfft': 'pocketfft'}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--rid', required=True, choices=RIDS)
    parser.add_argument('--build-root', type=Path, default=ROOT / 'artifacts/game-bazel-build')
    parser.add_argument('--install-root', type=Path, default=ROOT / 'artifacts/game-native')
    parser.add_argument('--ndk', type=Path)
    parser.add_argument('--bazel', default='bazel')
    parser.add_argument('--test', action='store_true', help='Run upstream tests that need no external model/reference data on a matching host')
    parser.add_argument('--update-lock', action='store_true')
    args = parser.parse_args()
    build = args.build_root.resolve() / args.rid
    for destination in (build, args.install_root.resolve() / args.rid):
        if destination.is_relative_to(HERE.parent) or HERE.parent.is_relative_to(destination):
            parser.error(f'Build output overlaps native sources: {destination}')
    with build_lock(build / '.build.lock'):
        build_native(args, parser, build)


def build_native(args, parser, build):
    host_os = {'Windows': 'win', 'Linux': 'linux', 'Darwin': 'osx'}[platform.system()]
    host_arch = 'arm64' if platform.machine().lower() in ('aarch64', 'arm64') else 'x64'
    if not args.rid.startswith(('android-', host_os + '-')):
        parser.error(f'Cannot build {args.rid} on {host_os}')
    if host_os != 'win' and not args.rid.startswith('android-') and args.rid != f'{host_os}-{host_arch}':
        parser.error('Use a native runner for this architecture')
    if args.test and args.rid != f'{host_os}-{host_arch}':
        parser.error('Tests require a matching native host')
    stage = build / 'workspace'
    files = [p for p in HERE.iterdir() if p.is_file() and
             (p.suffix in ('.bazel', '.BUILD', '.cpp') or p.name in ('.bazelversion', '.bazelrc', 'MODULE.bazel.lock'))]
    for path in files:
        write_changed(stage / path.name, path.read_bytes())
    version = (HERE / '.bazelversion').read_text().strip()
    env = dict(os.environ)
    if os.name == 'nt':
        env = {key: value for key, value in env.items() if key.upper() != 'PATH'}
        env['Path'] = os.environ.get('PATH', '')
        configure_windows_bash(env)
        if args.test and not env.get('BAZEL_SH'):
            parser.error('Bazel tests require Git Bash. Set BAZEL_SH to bash.exe.')
    if host_os == 'osx':
        # Bazel 的 Apple 编译器包装器在独立版本探测时也需要 SDK 环境。
        env.setdefault('DEVELOPER_DIR', subprocess.run(['xcode-select', '-p'], check=True, capture_output=True, text=True).stdout.strip())
        env.setdefault('SDKROOT', subprocess.run(['xcrun', '--sdk', 'macosx', '--show-sdk-path'], check=True, capture_output=True, text=True).stdout.strip())
    env['USE_BAZEL_VERSION'] = version
    env.setdefault('BAZELISK_HOME', str(ROOT / 'artifacts/bazelisk'))
    options = [f'--platforms=//:{args.rid}', '--lockfile_mode=' + ('update' if args.update_lock else 'error')]
    if args.rid.startswith('win-'):
        options += ['--cxxopt=/std:c++17', '--conlyopt=/std:c11', '--copt=/utf-8', '--features=static_link_msvcrt']
        if args.rid == 'win-arm64':
            if not env.get('BAZEL_LLVM'):
                vswhere = Path(os.environ.get('ProgramFiles(x86)', 'C:/Program Files (x86)')) / 'Microsoft Visual Studio/Installer/vswhere.exe'
                if vswhere.is_file():
                    installation = subprocess.run([str(vswhere), '-latest', '-prerelease', '-products', '*', '-property', 'installationPath'],
                                                  check=True, capture_output=True, text=True).stdout.strip()
                    for arch in (host_arch, 'x64', 'ARM64'):
                        llvm = Path(installation) / 'VC/Tools/Llvm' / arch
                        if (llvm / 'bin/clang-cl.exe').is_file():
                            env['BAZEL_LLVM'] = str(llvm)
                            break
            if not env.get('BAZEL_LLVM'):
                parser.error('Windows ARM64 GAME requires LLVM. Install the VS C++ Clang component or set BAZEL_LLVM.')
            options += ['--copt=--target=aarch64-pc-windows-msvc', '--compiler=clang-cl', f'--host_platform=//:win-{host_arch}-clang-host',
                        '--extra_toolchains=@local_config_cc//:cc-toolchain-arm64_windows-clang-cl']
        elif args.rid == 'win-x86':
            options += ['--extra_toolchains=@local_config_cc//:cc-toolchain-x64_x86_windows']
    else:
        options += ['--cxxopt=-std=c++17', '--conlyopt=-std=c11', '--copt=-fPIC', '--copt=-fno-associative-math']
    if args.rid.startswith('osx-'):
        options += ['--macos_minimum_os=10.15']
    if args.rid.startswith('android-'):
        revision = (HERE / 'android-ndk-version.txt').read_text().strip()
        sdk = os.environ.get('ANDROID_HOME') or os.environ.get('ANDROID_SDK_ROOT')
        if not sdk:
            sdk = str(Path(os.environ['LOCALAPPDATA']) / 'Android/Sdk') if os.name == 'nt' else str(Path.home() / ('Library/Android/sdk' if host_os == 'osx' else 'Android/Sdk'))
        ndk = (args.ndk or Path(sdk) / 'ndk' / revision).resolve()
        if not (ndk / 'source.properties').is_file():
            parser.error(f'Android NDK not installed: {ndk}')
        env['ANDROID_NDK_HOME'] = ndk.as_posix()
        options += ['--extra_toolchains=@androidndk//:all', '--linkopt=-Wl,-z,max-page-size=16384',
                    '--features=static_link_cpp_runtimes']
    bazel = shutil.which(args.bazel)
    if not bazel:
        parser.error('Bazelisk not found. Install Bazelisk or pass --bazel.')
    actual_version = subprocess.run([bazel, '--version'], cwd=stage, env=env, check=True,
                                    capture_output=True, text=True).stdout.strip()
    if actual_version != f'bazel {version}':
        parser.error(f'Expected Bazel {version}, got {actual_version!r}. Use Bazelisk.')
    command = [bazel, '--nohome_rc', '--nosystem_rc', f'--output_user_root={args.build_root.resolve() / "bazel"}']

    def run(action, targets, capture=False):
        return subprocess.run(command + [action] + options + targets, cwd=stage, env=env,
                              check=True, text=True, stdout=subprocess.PIPE if capture else None)

    try:
        run('build', ['//:opum_game'])
        if args.test:
            # 上游数值对照用例依赖仓库外的 PyTorch 转储；辅助函数中的 GTEST_SKIP 不能终止测试体。
            # 默认明确选择无需外部数据的既有用例，完整测试目标仍保留全部源文件。
            run('test', ['@game_cpp//:core_tests', '--test_arg=--gtest_filter=Version.*:Backend.*:ParseFlatIntObject.*:GgufFile.RoundtripMockFile:WavIO.*:Decode.*:Slicer.*:MidiWriter.*:TextWriter.*'])
        paths = run('cquery', ['set(//:opum_game ' + ' '.join('@' + name + '//:license' for name in LICENSES) + ')', '--output=files'], True).stdout.splitlines()
        execution_root = Path(run('info', ['execution_root'], True).stdout.strip())
        actions = json.loads(run('aquery', ['mnemonic("CppCompile", deps(//:opum_game))', '--output=jsonproto', '--include_artifacts=false'], True).stdout)
    finally:
        if args.update_lock and (stage / 'MODULE.bazel.lock').is_file():
            write_changed(HERE / 'MODULE.bazel.lock', (stage / 'MODULE.bazel.lock').read_bytes())
    name = 'opum_game.dll' if args.rid.startswith('win-') else ('libopum_game.dylib' if args.rid.startswith('osx-') else 'libopum_game.so')
    libraries = [stage / path for path in paths if Path(path).name == name]
    if len(libraries) != 1 or not libraries[0].is_file():
        raise RuntimeError(f'Expected exactly one {name}; cquery returned {paths}')
    verification = verify_binary(libraries[0], args.rid, EXPORTS)
    destination = args.install_root.resolve() / args.rid
    for repo, license_name in LICENSES.items():
        matches = [execution_root / path for path in paths if '+' + repo + '/' in path]
        if len(matches) != 1 or not matches[0].read_bytes().strip():
            raise RuntimeError(f'Missing or ambiguous license for {repo}')
        write_changed(destination / f'LICENSE.{license_name}.txt', matches[0].read_bytes())
    compiler_action = next(action for action in actions['actions'] if action['mnemonic'] == 'CppCompile')
    compiler = compiler_action['arguments'][0]
    compiler_path = Path(compiler) if Path(compiler).is_absolute() else execution_root / compiler
    compiler_env = env | {item['key']: item['value'] for item in compiler_action.get('environmentVariables', [])}
    result = subprocess.run([str(compiler_path), '/Bv' if compiler_path.name.lower() == 'cl.exe' else '--version'],
                            cwd=execution_root, env=compiler_env, capture_output=True)
    compiler_info = (result.stdout + result.stderr).decode(locale.getpreferredencoding(False), errors='replace').strip()
    if compiler_path.name.lower() == 'cl.exe':
        compiler_info = compiler_info.splitlines()[0]
    elif result.returncode:
        raise RuntimeError(f'Cannot identify compiler: {compiler_info}')
    if not compiler_info:
        raise RuntimeError('Compiler did not report its version')
    if args.rid == 'win-arm64' and 'clang' not in compiler_path.name.lower():
        raise RuntimeError('Windows ARM64 GGML requires Clang')
    provenance = {
        'rid': args.rid, 'bazel': version, 'backend': 'cpu', 'compiler': compiler,
        'compiler_version': compiler_info, 'compiler_arguments': compiler_action['arguments'],
        'build_options': options, 'binary': verification,
        'dependencies': (HERE / 'MODULE.bazel').read_text(encoding='utf-8'),
        'lock_sha256': hashlib.sha256((stage / 'MODULE.bazel.lock').read_bytes()).hexdigest(),
        'build_adapter_sha256': {p.relative_to(HERE.parent).as_posix(): hashlib.sha256(p.read_bytes()).hexdigest()
                                 for p in files + [HERE / 'build.py', HERE.parent / 'bazel_common.py', HERE.parent / 'worldline/verify.py']},
        'library_sha256': hashlib.sha256(libraries[0].read_bytes()).hexdigest(),
    }
    if args.rid.startswith('android-'):
        provenance['ndk'] = (ndk / 'source.properties').read_text().strip()
    write_changed(destination / name, libraries[0].read_bytes())
    write_changed(destination / 'game-build.json', (json.dumps(provenance, indent=2) + '\n').encode())
    print(f'GAME {args.rid}: {destination / name}', flush=True)


if __name__ == '__main__':
    main()
