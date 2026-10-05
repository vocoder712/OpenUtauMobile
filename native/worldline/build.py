"""在独立构建副本中用固定 Bazel 编译冻结的 Worldline 源码。"""
import argparse
import hashlib
import json
import locale
import os
from pathlib import Path
import platform
import re
import shutil
import subprocess
import sys

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
from bazel_common import build_lock, write_changed
from verify import verify_binary

HERE = Path(__file__).resolve().parent
ROOT = HERE.parent.parent
SOURCE = HERE.parent / 'upstream_cpp'
RIDS = ('win-x64', 'win-arm64', 'win-x86', 'linux-x64', 'linux-arm64',
        'osx-x64', 'osx-arm64', 'android-arm64', 'android-arm', 'android-x64', 'android-x86')
LICENSES = ('world', 'libgvps', 'libnpy', 'libpyin', 'spline', 'miniaudio', 'xxhash')


def stage_sources(destination):
    files = {}
    source_hash = hashlib.sha256()
    for source in sorted(SOURCE.rglob('*')):
        if not source.is_file():
            continue
        relative = source.relative_to(SOURCE).as_posix()
        content = source.read_bytes()
        source_hash.update(relative.encode() + b'\0' + content + b'\0')
        if relative in ('.bazelrc', '.bazelversion', 'MODULE.bazel', 'MODULE.bazel.lock', 'WORKSPACE.bazel'):
            continue
        if source.name == 'BUILD.bazel' or source.suffix == '.BUILD':
            # 只补 Bazel 9 的显式规则加载；源文件、目标依赖和上游补丁保持原样。
            rules = sorted(set(re.findall(rb'^\s*(cc_\w+)\(', content, re.MULTILINE)))
            loads = b''.join(b'load("@rules_cc//cc:' + rule + b'.bzl", "' + rule + b'")\n' for rule in rules)
            content = loads + content
            if source.suffix == '.BUILD':
                pattern = '["*.c", "*.h"]' if source.stem == 'libpyin' else '["LICENSE*"]'
                content += ('\nfilegroup(name = "opum_license", srcs = glob(' + pattern + '))\n').encode()
        files[relative] = content
    for name in ('.bazelversion', '.bazelrc', 'MODULE.bazel', 'BUILD.bazel', 'MODULE.bazel.lock',
                 'frozen_archive.bzl', 'adapt_build.py', 'xxhash.BUILD'):
        source = HERE / name
        if source.is_file():
            files[name] = source.read_bytes()
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
    return source_hash.hexdigest()


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--rid', required=True, choices=RIDS)
    parser.add_argument('--build-root', type=Path, default=ROOT / 'artifacts/worldline-build')
    parser.add_argument('--install-root', type=Path, default=ROOT / 'artifacts/worldline-native')
    parser.add_argument('--ndk', type=Path)
    parser.add_argument('--bazel', default='bazel')
    parser.add_argument('--test', action='store_true')
    parser.add_argument('--update-lock', action='store_true')
    args = parser.parse_args()
    build = args.build_root.resolve() / args.rid
    for destination in (build / 'workspace', args.install_root.resolve() / args.rid):
        for protected in (SOURCE, HERE):
            if destination.is_relative_to(protected) or protected.is_relative_to(destination):
                parser.error(f'Build output overlaps source: {destination}')
    with build_lock(build / '.build.lock'):
        build_native(args, parser, build)


def build_native(args, parser, build):
    host_os = {'Windows': 'win', 'Linux': 'linux', 'Darwin': 'osx'}[platform.system()]
    host_arch = 'arm64' if platform.machine().lower() in ('aarch64', 'arm64') else 'x64'
    if not args.rid.startswith('android-') and not args.rid.startswith(host_os + '-'):
        parser.error(f'Cannot build {args.rid} on {host_os}')
    if host_os != 'win' and not args.rid.startswith('android-') and args.rid != f'{host_os}-{host_arch}':
        parser.error('Use a native runner for this architecture')
    if args.test and args.rid != f'{host_os}-{host_arch}':
        parser.error('Tests require a matching native host')
    stage = build / 'workspace'
    digest = stage_sources(stage)
    adapter_hashes = {path.name: hashlib.sha256(path.read_bytes()).hexdigest() for path in sorted(HERE.iterdir())
                      if path.is_file() and (path.suffix in ('.py', '.bzl', '.bazel', '.BUILD') or path.name in ('.bazelrc', '.bazelversion'))}
    upstream_commit = (HERE / 'upstream-revision.txt').read_text().strip()
    version = (HERE / '.bazelversion').read_text().strip()
    # Windows 环境变量名不区分大小写，避免宿主注入 PATH/Path 导致 MSBuild 启动失败。
    env = dict(os.environ)
    if os.name == 'nt':
        path = os.environ.get('PATH', '')
        env = {key: value for key, value in env.items() if key.upper() != 'PATH'}
        env['Path'] = path
        if not env.get('BAZEL_SH'):
            git = shutil.which('git')
            bash = Path(git).resolve().parent.parent / 'bin/bash.exe' if git else None
            if bash and bash.is_file():
                env['BAZEL_SH'] = str(bash)
            elif args.test:
                parser.error('Bazel tests require Git Bash. Set BAZEL_SH to bash.exe.')
    env['USE_BAZEL_VERSION'] = version
    env['OPUM_WORLDLINE_PYTHON'] = sys.executable
    env.setdefault('BAZELISK_HOME', str(ROOT / 'artifacts/bazelisk'))
    options = [f'--platforms=//:{args.rid}', '--lockfile_mode=' + ('update' if args.update_lock else 'error')]
    if args.rid.startswith('win-'):
        options += ['--cxxopt=/std:c++17', '--copt=/utf-8', '--features=static_link_msvcrt']
        if args.rid == 'win-x86':
            options += ['--extra_toolchains=@local_config_cc//:cc-toolchain-x64_x86_windows']
    else:
        options += ['--cxxopt=-std=c++17']
    if args.rid.startswith('osx-'):
        options += ['--macos_minimum_os=10.12']
    if args.rid.startswith('linux-'):
        options += ['--linkopt=-lm']
    if args.rid.startswith('android-'):
        revision = (HERE.parent / 'game/android-ndk-version.txt').read_text().strip()
        sdk = os.environ.get('ANDROID_HOME') or os.environ.get('ANDROID_SDK_ROOT')
        if not sdk:
            sdk = str(Path(os.environ['LOCALAPPDATA']) / 'Android/Sdk') if os.name == 'nt' else str(Path.home() / ('Library/Android/sdk' if host_os == 'osx' else 'Android/Sdk'))
        ndk = (args.ndk or Path(sdk) / 'ndk' / revision).resolve()
        if not (ndk / 'source.properties').is_file():
            parser.error(f'Android NDK not installed: {ndk}')
        env['ANDROID_NDK_HOME'] = ndk.as_posix()
        options += ['--extra_toolchains=@androidndk//:all',
                    '--linkopt=-Wl,-z,max-page-size=16384', '--features=static_link_cpp_runtimes']
    bazel = shutil.which(args.bazel)
    if not bazel:
        parser.error('Bazelisk not found. Install Bazelisk or pass --bazel.')
    actual_version = subprocess.run([bazel, '--version'], cwd=stage, env=env, check=True,
                                    capture_output=True, text=True).stdout.strip()
    if actual_version != f'bazel {version}':
        parser.error(f'Expected Bazel {version}, got {actual_version!r}. Use Bazelisk.')
    command = [bazel, '--nohome_rc', '--nosystem_rc',
               f'--output_user_root={args.build_root.resolve() / "bazel"}']

    def run(action, targets, capture=False):
        return subprocess.run(command + [action] + options + targets, cwd=stage, env=env,
                              check=True, text=True, stdout=subprocess.PIPE if capture else None)

    try:
        run('build', ['//worldline:worldline'])
        if args.test:
            run('test', ['//worldline:worldline_test', '//worldline/model:effects_test', '//worldline/model:continuous_noise_test'])
        paths = run('cquery', ['set(//worldline:worldline ' + ' '.join('@' + name + '//:opum_license' for name in LICENSES) + ')', '--output=files'], capture=True).stdout.splitlines()
        license_paths = [path for path in paths if path.startswith('external/')]
        actions = json.loads(run('aquery', ['mnemonic("CppCompile", deps(//worldline:worldline))',
                                         '--output=jsonproto', '--include_artifacts=false'], capture=True).stdout)
        execution_root = Path(run('info', ['execution_root'], capture=True).stdout.strip())
    finally:
        generated_lock = stage / 'MODULE.bazel.lock'
        if args.update_lock and generated_lock.is_file():
            write_changed(HERE / 'MODULE.bazel.lock', generated_lock.read_bytes())
    name = 'worldline.dll' if args.rid.startswith('win-') else ('libworldline.dylib' if args.rid.startswith('osx-') else 'libworldline.so')
    libraries = [stage / path for path in paths if Path(path).name == name]
    if len(libraries) != 1 or not libraries[0].is_file():
        raise RuntimeError(f'Expected exactly one {name}; cquery returned {paths}')
    destination = args.install_root.resolve() / args.rid
    verification = verify_binary(libraries[0], args.rid)
    licenses = {name: [] for name in LICENSES}
    for path in license_paths:
        repo = next((name for name in LICENSES if '+' + name + '/' in path), None)
        if not repo:
            raise ValueError(f'Unrecognized license source: {path}')
        content = (execution_root / path).read_text(encoding='utf-8')
        if repo == 'libpyin':
            notices = re.findall(r'/\*.*?\*/', content, re.DOTALL)
            content = '\n\n'.join(notice for notice in notices if 'Copyright' in notice and 'Redistribution' in notice)
        if content.strip() and content not in licenses[repo]:
            licenses[repo].append(content)
    for license_name, contents in licenses.items():
        if not contents:
            raise ValueError(f'Missing license: {license_name}')
        write_changed(destination / f'LICENSE.{license_name}.txt', ('\n\n'.join(contents) + '\n').encode())
    write_changed(destination / 'LICENSE.worldline.txt', (HERE / 'LICENSE.worldline.txt').read_bytes())
    write_changed(destination / name, libraries[0].read_bytes())
    compiler_action = next(action for action in actions['actions'] if action['mnemonic'] == 'CppCompile')
    compiler = compiler_action['arguments'][0]
    compiler_path = Path(compiler) if Path(compiler).is_absolute() else execution_root / compiler
    compiler_env = env | {item['key']: item['value'] for item in compiler_action.get('environmentVariables', [])}
    compiler_result = subprocess.run([str(compiler_path), '/Bv' if compiler_path.name.lower() == 'cl.exe' else '--version'],
                                    cwd=execution_root, env=compiler_env, capture_output=True)
    compiler_info = (compiler_result.stdout + compiler_result.stderr).decode(locale.getpreferredencoding(False), errors='replace').strip()
    if compiler_path.name.lower() == 'cl.exe':
        # cl /Bv 输出版本后因没有输入文件返回非零；保留首行版本与目标架构。
        compiler_info = compiler_info.splitlines()[0]
    elif compiler_result.returncode:
        raise RuntimeError(f'Cannot identify compiler: {compiler_info}')
    if not compiler_info:
        raise RuntimeError('Compiler did not report its version')
    provenance = {'rid': args.rid, 'bazel': version, 'source_sha256': digest,
                  'upstream_commit': upstream_commit,
                  'compiler': compiler, 'compiler_version': compiler_info,
                  'compiler_arguments': compiler_action['arguments'],
                  'build_options': options, 'binary': verification,
                  'module_sha256': hashlib.sha256((stage / 'MODULE.bazel').read_bytes()).hexdigest(),
                  'lock_sha256': hashlib.sha256((stage / 'MODULE.bazel.lock').read_bytes()).hexdigest(),
                  'build_adapter_sha256': adapter_hashes,
                  'dependencies': (stage / 'MODULE.bazel').read_text(encoding='utf-8'),
                  'patches': {path.name: hashlib.sha256(path.read_bytes()).hexdigest() for path in sorted((SOURCE / 'third_party').glob('*.patch'))},
                  'library_sha256': hashlib.sha256(libraries[0].read_bytes()).hexdigest()}
    if args.rid.startswith('android-'):
        provenance['ndk'] = (ndk / 'source.properties').read_text().strip()
    write_changed(destination / 'worldline-build.json', (json.dumps(provenance, indent=2) + '\n').encode())
    print(f'Worldline {args.rid}: {destination / name}', flush=True)


if __name__ == '__main__':
    main()
