"""检查 APK 的原生库架构、GAME 页面对齐和许可证，不依赖 Android 设备。"""
import argparse
import struct
import zipfile

parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument('apk')
parser.add_argument('rid', choices=['android-arm64', 'android-arm', 'android-x64', 'android-x86'])
args = parser.parse_args()
abi, machine, elf_class = {
    'android-arm64': ('arm64-v8a', 183, 2),
    'android-arm': ('armeabi-v7a', 40, 1),
    'android-x64': ('x86_64', 62, 2),
    'android-x86': ('x86', 3, 1),
}[args.rid]

with zipfile.ZipFile(args.apk) as apk:
    names = apk.namelist()
    abis = {name.split('/')[1] for name in names if name.startswith('lib/') and name.endswith('.so')}
    if abis != {abi}:
        raise ValueError(f'Expected only {abi}, found {sorted(abis)}')
    for library in ('libworldline.so', 'libonnxruntime.so', 'libonnxruntime4j_jni.so', 'libopum_game.so'):
        entry = f'lib/{abi}/{library}'
        if names.count(entry) != 1:
            raise ValueError(f'{entry} must occur exactly once')
        data = apk.read(entry)
        if data[:6] != b'\x7fELF' + bytes([elf_class, 1]) or struct.unpack_from('<H', data, 18)[0] != machine:
            raise ValueError(f'{entry}: wrong ELF architecture')
        if library == 'libopum_game.so':
            # 仅对新 GAME 库强制 16 KB 对齐；既有依赖的迁移单独跟踪。
            if elf_class == 2:
                offset = struct.unpack_from('<Q', data, 32)[0]
                size, count = struct.unpack_from('<HH', data, 54)
                align_offset, align_format = 48, '<Q'
            else:
                offset = struct.unpack_from('<I', data, 28)[0]
                size, count = struct.unpack_from('<HH', data, 42)
                align_offset, align_format = 28, '<I'
            alignments = [struct.unpack_from(align_format, data, offset + i * size + align_offset)[0]
                          for i in range(count) if struct.unpack_from('<I', data, offset + i * size)[0] == 1]
            if not alignments or any(alignment < 16384 for alignment in alignments):
                raise ValueError(f'{entry}: LOAD segments lack 16 KB alignment: {alignments}')
    for license_name in ('game.cpp', 'ggml', 'pocketfft'):
        entry = f'assets/GameLicenses/LICENSE.{license_name}.txt'
        if names.count(entry) != 1 or not apk.read(entry).strip():
            raise ValueError(f'{entry}: missing, empty or duplicated license')
print(f'PASS: {args.apk}: {abi}, required native libraries, GAME alignment and licenses')
