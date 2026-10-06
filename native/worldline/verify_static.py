"""验证静态归档的每个对象架构和 Worldline ABI，不把交叉编译当作设备验收。"""
import argparse
import hashlib
import json
from pathlib import Path
import struct
import subprocess
import plistlib

from verify import EXPORTS


def verify_archive(path, rid, nm):
    data = Path(path).read_bytes()
    if data[:8] != b'!<arch>\n':
        raise ValueError('Expected a self-contained ar archive, not a thin archive')
    cursor, count = 8, 0
    while cursor < len(data):
        header = data[cursor:cursor + 60]
        if len(header) != 60 or header[58:] != b'`\n':
            raise ValueError('Invalid ar member header')
        size = int(header[48:58])
        name = header[:16].strip()
        content = data[cursor + 60:cursor + 60 + size]
        if len(content) != size:
            raise ValueError('Truncated archive')
        cursor += 60 + size + size % 2
        if name.startswith(b'#1/'):
            length = int(name[3:])
            name, content = content[:length].rstrip(b'\0'), content[length:]
        if name in (b'/', b'//', b'/SYM64/') or name.startswith(b'__.SYMDEF'):
            continue
        count += 1
        if rid == 'browser-wasm':
            if content[:8] != b'\0asm\x01\0\0\0':
                raise ValueError(f'Expected WASM object: {name!r}')
        else:
            magic, cpu, _, kind, commands = struct.unpack_from('<5I', content)
            expected_cpu = 0x1000007 if rid.endswith('-x64') else 0x100000c
            if (magic, cpu, kind) != (0xfeedfacf, expected_cpu, 1):
                raise ValueError(f'Wrong iOS Mach-O object: {name!r}')
            offset, found = 32, False
            for _ in range(commands):
                command, length = struct.unpack_from('<II', content, offset)
                if command == 0x32:
                    sdk_platform = struct.unpack_from('<I', content, offset + 8)[0]
                    if sdk_platform != (2 if rid == 'ios-arm64' else 7):
                        raise ValueError(f'Wrong iOS device/simulator platform: {name!r}')
                    found = True
                offset += length
            if not found:
                raise ValueError(f'Missing iOS deployment platform: {name!r}')
    if not count:
        raise ValueError('Empty native archive')
    command = [str(nm), '-g', '--defined-only', str(path)] if rid == 'browser-wasm' else [str(nm), '-gU', str(path)]
    result = subprocess.run(command, check=True, capture_output=True, text=True)
    symbols = [line.split()[-1] for line in result.stdout.splitlines() if len(line.split()) >= 2]
    if rid != 'browser-wasm':
        symbols = [name.removeprefix('_') for name in symbols]
    exports = set(symbols)
    missing = EXPORTS - exports
    if missing:
        raise ValueError(f'Missing static Worldline ABI: {sorted(missing)}')
    duplicates = [name for name in EXPORTS if symbols.count(name) != 1]
    if duplicates:
        raise ValueError(f'Duplicate static Worldline ABI definitions: {duplicates}')
    return {'object_count': count, 'verified_exports': sorted(EXPORTS), 'rid': rid, 'linkage': 'static'}


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('library', type=Path)
    parser.add_argument('rid', choices=('browser-wasm', 'ios-arm64', 'iossimulator-arm64', 'iossimulator-x64'))
    parser.add_argument('--nm', required=True)
    parser.add_argument('--app-bundle', type=Path, help='Also verify exported ABI and provenance in an iOS app bundle')
    args = parser.parse_args()
    print(json.dumps(verify_archive(args.library, args.rid, args.nm), indent=2))
    if args.app_bundle:
        if args.rid == 'browser-wasm':
            parser.error('--app-bundle is only valid for iOS')
        info = plistlib.loads((args.app_bundle / 'Info.plist').read_bytes())
        binary = args.app_bundle / info['CFBundleExecutable']
        symbols = subprocess.run([args.nm, '-gU', str(binary)], check=True, text=True, capture_output=True).stdout
        exported = {line.split()[-1].removeprefix('_') for line in symbols.splitlines() if len(line.split()) >= 2}
        if EXPORTS - exported:
            raise ValueError(f'Linked iOS app is missing ABI exports: {sorted(EXPORTS - exported)}')
        packaged = json.loads((args.app_bundle / 'WorldlineLicenses/worldline-build.json').read_text(encoding='utf-8'))
        original = json.loads((args.library.parent / 'worldline-build.json').read_text(encoding='utf-8'))
        if packaged != original or packaged['library_sha256'] != hashlib.sha256(args.library.read_bytes()).hexdigest():
            raise ValueError('iOS app Worldline provenance mismatch')
        for license_file in args.library.parent.glob('LICENSE.*.txt'):
            if license_file.read_bytes() != (args.app_bundle / 'WorldlineLicenses' / license_file.name).read_bytes():
                raise ValueError(f'iOS app license mismatch: {license_file.name}')
        print('iOS app Worldline exports, provenance and licenses verified.')
