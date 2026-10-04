"""检查 Worldline 原生库架构、导出及调用方持有缓冲区的新 ABI。"""
import argparse
import ctypes
import json
import math
from pathlib import Path
import struct

if not __debug__:
    raise RuntimeError('Native verification requires Python without -O/PYTHONOPTIMIZE.')

EXPORTS = {
    'F0FrameCount', 'F0', 'DecodeMgc', 'DecodeBap', 'InitAnalysisConfig',
    'WorldAnalysisF0In', 'WorldSynthesisSampleCount', 'WorldSynthesis',
    'HnAnalysisF0In', 'WorldSynthesisContinuousNoise',
    'ou_get_audio_device_infos', 'ou_free_audio_device_infos',
    'ou_init_audio_device', 'ou_init_audio_device_auto', 'ou_get_audio_device_api',
    'ou_get_audio_device_name', 'ou_free_audio_device', 'ou_audio_device_start',
    'ou_audio_device_stop', 'ou_audio_get_error_message',
}


def verify_binary(path, rid):
    data = Path(path).read_bytes()
    unpack = lambda fmt, offset: struct.unpack_from('<' + fmt, data, offset)
    text = lambda offset: data[offset:data.index(b'\0', offset)].decode('utf-8')
    exports, dependencies = set(), []
    arch = rid.split('-')[1]
    if rid.startswith('win-'):
        assert data[:2] == b'MZ', 'Expected PE DLL'
        pe = unpack('I', 0x3c)[0]
        assert data[pe:pe + 4] == b'PE\0\0'
        machine, count = unpack('HH', pe + 4)
        assert machine == {'x64': 0x8664, 'x86': 0x14c, 'arm64': 0xaa64}[arch], 'Wrong PE architecture'
        optional_size, characteristics = unpack('HH', pe + 20)
        assert characteristics & 0x2000, 'Expected DLL'
        optional = pe + 24
        directories = optional + (112 if unpack('H', optional)[0] == 0x20b else 96)
        sections = [unpack('IIII', optional + optional_size + i * 40 + 8) for i in range(count)]

        def address(rva):
            for size, virtual, raw_size, raw in sections:
                if virtual <= rva < virtual + max(size, raw_size):
                    return raw + rva - virtual
            raise ValueError(f'Unmapped PE address {rva}')

        export = address(unpack('I', directories)[0])
        count = unpack('I', export + 24)[0]
        names = address(unpack('I', export + 32)[0])
        exports = {text(address(unpack('I', names + i * 4)[0])) for i in range(count)}
        imports = address(unpack('I', directories + 8)[0])
        while any(data[imports:imports + 20]):
            dependencies.append(text(address(unpack('I', imports + 12)[0])))
            imports += 20
        assert not any(name.lower().startswith(('libgcc', 'libstdc++', 'libwinpthread', 'vcruntime', 'msvcp')) for name in dependencies), 'Expected static Windows C++ runtime'
    elif rid.startswith(('android-', 'linux-')):
        assert data[:4] == b'\x7fELF' and data[5] == 1, 'Expected little-endian ELF'
        wide = arch in ('x64', 'arm64')
        assert data[4] == (2 if wide else 1), 'Wrong ELF class'
        assert unpack('HH', 16) == (3, {'arm64': 183, 'arm': 40, 'x64': 62, 'x86': 3}[arch]), 'Wrong ELF architecture/type'
        section_offset = unpack('Q' if wide else 'I', 40 if wide else 32)[0]
        section_size, section_count = unpack('HH', 58 if wide else 46)
        sections = [unpack('IIQQQQIIQQ' if wide else 'IIIIIIIIII', section_offset + i * section_size) for i in range(section_count)]
        for section in sections:
            if section[1] not in (6, 11):
                continue
            strings = sections[section[6]][4]
            for offset in range(section[4], section[4] + section[5], section[9]):
                if section[1] == 6:
                    tag, value = unpack('qQ' if wide else 'iI', offset)
                    if tag == 1:
                        dependencies.append(text(strings + value))
                else:
                    symbol = unpack('IBBHQQ' if wide else 'IIIBBH', offset)
                    info, index = (symbol[1], symbol[3]) if wide else (symbol[3], symbol[5])
                    if info >> 4 and index:
                        exports.add(text(strings + symbol[0]))
        if rid.startswith('android-'):
            program_offset = unpack('Q' if wide else 'I', 32 if wide else 28)[0]
            size, count = unpack('HH', 54 if wide else 42)
            alignments = [unpack('Q' if wide else 'I', program_offset + i * size + (48 if wide else 28))[0]
                          for i in range(count) if unpack('I', program_offset + i * size)[0] == 1]
            assert alignments and all(value >= 16384 for value in alignments), f'Missing 16 KB alignment: {alignments}'
            assert set(dependencies) <= {'libc.so', 'libm.so', 'libdl.so', 'liblog.so', 'libandroid.so'}, f'Unexpected Android dependency: {dependencies}'
    else:
        assert unpack('I', 0)[0] == 0xfeedfacf, 'Expected thin 64-bit Mach-O'
        assert unpack('I', 4)[0] == {'x64': 0x1000007, 'arm64': 0x100000c}[arch], 'Wrong Mach-O architecture'
        assert unpack('I', 12)[0] == 6, 'Expected Mach-O dylib'
        offset = 32
        for _ in range(unpack('I', 16)[0]):
            command, size = unpack('II', offset)
            if command == 2:
                symbols, count, strings, _ = unpack('IIII', offset + 8)
                for i in range(count):
                    name, kind, section, _, _ = unpack('IBBHQ', symbols + i * 16)
                    if kind & 1 and section:
                        exports.add(text(strings + name).removeprefix('_'))
            elif command in (0xc, 0x80000018, 0x8000001f):
                dependencies.append(text(offset + unpack('I', offset + 8)[0]))
            offset += size
    missing = EXPORTS - exports
    if missing:
        raise ValueError(f'Missing Worldline ABI exports: {sorted(missing)}')
    return {'dependencies': dependencies, 'verified_exports': sorted(EXPORTS)}


class AnalysisConfig(ctypes.Structure):
    _fields_ = [('fs', ctypes.c_int), ('hop_size', ctypes.c_int),
                ('fft_size', ctypes.c_int), ('f0_floor', ctypes.c_float),
                ('frame_ms', ctypes.c_double)]


class GuardedBuffer:
    """用哨兵检查输出边界，用 NaN 检查未写入的元素。"""
    sentinel = 9.87654321e99

    def __init__(self, count):
        self.count = count
        self.storage = (ctypes.c_double * (count + 2))(
            self.sentinel, *([math.nan] * count), self.sentinel)
        self.pointer = ctypes.cast(ctypes.byref(self.storage, ctypes.sizeof(ctypes.c_double)),
                                   ctypes.POINTER(ctypes.c_double))

    def values(self):
        assert self.storage[0] == self.storage[-1] == self.sentinel, 'Native output exceeded its buffer'
        result = list(self.storage)[1:-1]
        assert all(math.isfinite(value) for value in result), 'Native output is unwritten or non-finite'
        return result


def verify_runtime(path, report_path=None):
    library = ctypes.CDLL(str(Path(path).resolve()))
    for name in EXPORTS:
        getattr(library, name)
    library.F0FrameCount.argtypes = [ctypes.c_int, ctypes.c_int, ctypes.c_double, ctypes.c_int]
    library.F0FrameCount.restype = ctypes.c_int
    library.F0.argtypes = [ctypes.POINTER(ctypes.c_float), ctypes.c_int, ctypes.c_int, ctypes.c_double, ctypes.c_int, ctypes.POINTER(ctypes.c_double)]
    library.F0.restype = ctypes.c_int
    double_p, float_p = ctypes.POINTER(ctypes.c_double), ctypes.POINTER(ctypes.c_float)
    config_p = ctypes.POINTER(AnalysisConfig)
    integer, real = ctypes.c_int, ctypes.c_double
    signatures = {
        'DecodeMgc': ([integer, double_p, integer, integer, integer, double_p], None),
        'DecodeBap': ([integer, double_p, integer, integer, double_p], None),
        'InitAnalysisConfig': ([config_p, integer, integer, integer], None),
        'WorldAnalysisF0In': ([config_p, float_p, integer, double_p, integer, double_p, double_p], None),
        'HnAnalysisF0In': ([config_p, float_p, float_p, integer, double_p, integer, double_p, double_p, double_p], None),
        'WorldSynthesisSampleCount': ([integer, real, integer], integer),
        'WorldSynthesis': ([double_p, integer, double_p, ctypes.c_bool, integer,
                           double_p, ctypes.c_bool, integer, real, integer,
                           double_p, double_p, double_p, double_p, double_p], integer),
        'WorldSynthesisContinuousNoise': ([double_p, integer, double_p, double_p, double_p,
                                          double_p, integer, integer, integer, ctypes.c_uint64,
                                          double_p, double_p, double_p, double_p, double_p], integer),
    }
    for name, (arguments, result) in signatures.items():
        getattr(library, name).argtypes = arguments
        getattr(library, name).restype = result

    def doubles(values):
        return (ctypes.c_double * len(values))(*values)

    fs, hop, fft, frames, mgc_size, bap_size = 44100, 220, 2048, 24, 24, 5
    bins = fft // 2 + 1
    report = {'fs': fs, 'hop': hop, 'fft': fft, 'frames': frames, 'mgc_size': mgc_size,
              'bap_size': bap_size, 'f0_cases': []}
    samples = (ctypes.c_float * 8820)()
    assert library.F0FrameCount(0, fs, 10, 0) == library.F0(None, 0, fs, 10, 0, None) == 0
    for method in (-1, 0, 1, 2):
        # 5 ms 在 44.1 kHz 下需要取整，覆盖 pyin 的容量上界与实际长度不同的情况。
        period = 5.0
        count = library.F0FrameCount(len(samples), fs, period, method)
        assert 0 < count < 1000
        output = GuardedBuffer(count)
        written = library.F0(samples, len(samples), fs, period, method, output.pointer)
        values = output.values()
        assert 0 < written <= count and all(value == 0 for value in values)
        report['f0_cases'].append({'method': method, 'capacity': count, 'written': written, 'values': values[:written]})

    assert ctypes.sizeof(AnalysisConfig) == 24
    assert [getattr(AnalysisConfig, name).offset for name, _ in AnalysisConfig._fields_] == [0, 4, 8, 12, 16]
    config = AnalysisConfig()
    library.InitAnalysisConfig(ctypes.byref(config), fs, hop, fft)
    assert (config.fs, config.hop_size, config.fft_size) == (fs, hop, fft)
    assert 0 < config.f0_floor < 220 and config.frame_ms == hop * 1000.0 / fs
    report['config'] = {name: getattr(config, name) for name, _ in AnalysisConfig._fields_}

    mgc = [(-4 + row * 0.015 if col == 0 else 0.01 / (col + 1))
           for row in range(frames) for col in range(mgc_size)]
    bap = [-25 - row * 0.1 - col for row in range(frames) for col in range(bap_size)]
    sp, ap = GuardedBuffer(frames * bins), GuardedBuffer(frames * bins)
    library.DecodeMgc(frames, doubles(mgc), mgc_size, fft, fs, sp.pointer)
    library.DecodeBap(frames, doubles(bap), fft, fs, ap.pointer)
    decoded_sp, decoded_ap = sp.values(), ap.values()
    assert all(value > 0 for value in decoded_sp)
    assert all(0 <= value <= 1 for value in decoded_ap)
    assert decoded_sp[:bins] != decoded_sp[-bins:] and decoded_ap[:bins] != decoded_ap[-bins:]
    report.update(mgc=mgc, bap=bap, decoded_sp=decoded_sp, decoded_ap=decoded_ap)

    f0 = [0 if row % 7 == 0 else 220.0 for row in range(frames)]
    harmonic = (ctypes.c_float * len(samples))(*[0.1 * math.sin(2 * math.pi * 220 * i / fs) for i in range(len(samples))])
    samples = (ctypes.c_float * len(samples))(*[harmonic[i] + 0.01 * math.sin(2 * math.pi * 1777 * i / fs) for i in range(len(samples))])
    sp, ap = GuardedBuffer(frames * bins), GuardedBuffer(frames * bins)
    library.WorldAnalysisF0In(ctypes.byref(config), samples, len(samples), doubles(f0), frames, sp.pointer, ap.pointer)
    report.update(samples=list(samples), harmonic=list(harmonic), f0=f0,
                  analysis_sp=sp.values(), analysis_ap=ap.values())
    hn_sp, hn_harmonic, hn_ap = [GuardedBuffer(frames * bins) for _ in range(3)]
    library.HnAnalysisF0In(ctypes.byref(config), samples, harmonic, len(samples), doubles(f0), frames,
                           hn_sp.pointer, hn_harmonic.pointer, hn_ap.pointer)
    report.update(hn_sp=hn_sp.values(), hn_harmonic=hn_harmonic.values(), hn_ap=hn_ap.values())
    assert all(0.001 <= value <= 0.999 for value in report['hn_ap'])
    for row in range(frames):
        if f0[row] == 0:
            assert all(value == 0.999 for value in report['hn_ap'][row * bins:(row + 1) * bins])

    count = library.WorldSynthesisSampleCount(frames, config.frame_ms, fs)
    assert count == 1 + int((frames - 1) * config.frame_ms / 1000 * fs)
    assert library.WorldSynthesisSampleCount(0, config.frame_ms, fs) == 0
    assert library.WorldSynthesisSampleCount(1, config.frame_ms, fs) == 1
    curves = [[0.4 + row * 0.005 for row in range(frames)],
              [0.45 + row * 0.003 for row in range(frames)],
              [0.3 + row * 0.01 for row in range(frames)],
              [0.8 + row * 0.005 for row in range(frames)]]
    report.update(curves=curves, sample_count=count, synthesis={})
    for is_mgc in (False, True):
        for is_bap in (False, True):
            output = GuardedBuffer(count)
            written = library.WorldSynthesis(doubles(f0), frames, doubles(mgc if is_mgc else decoded_sp),
                is_mgc, mgc_size, doubles(bap if is_bap else decoded_ap), is_bap, fft, config.frame_ms,
                fs, output.pointer, *(doubles(curve) for curve in curves))
            values = output.values()
            assert written == count and any(value != 0 for value in values)
            report['synthesis'][f'{int(is_mgc)}{int(is_bap)}'] = values
    baseline = report['synthesis']['00']
    for values in report['synthesis'].values():
        assert all(math.isclose(a, b, rel_tol=1e-10, abs_tol=1e-12) for a, b in zip(baseline, values)), 'Decoded and encoded synthesis disagree'

    # 纯噪声输入排除周期部分影响，单独验证种子高 32 位与输入不可变约定。
    inputs = [[0.0] * frames, [1e-4] * (frames * bins), [1e-4] * (frames * bins),
              [1.0] * (frames * bins), [1.0] * frames, *curves]
    arrays = [doubles(values) for values in inputs]
    seed = 0x12345678abcdef01
    noise_results = []
    for current_seed in (seed, seed, seed ^ (1 << 40)):
        output = GuardedBuffer(count)
        written = library.WorldSynthesisContinuousNoise(arrays[0], frames, *arrays[1:5], fft, hop, fs,
                                                        current_seed, output.pointer, *arrays[5:])
        assert written == count
        noise_results.append(output.values())
        assert all(list(array) == values for array, values in zip(arrays, inputs)), 'Continuous synthesis modified an input'
    assert noise_results[0] == noise_results[1] and noise_results[0] != noise_results[2], '64-bit seed is not respected'
    report.update(seed=seed, noise=noise_results[0], noise_high_seed=noise_results[2])
    if report_path:
        Path(report_path).write_text(json.dumps(report, allow_nan=False), encoding='utf-8')
    print('PASS: Worldline F0/codec/config/analysis/synthesis/R1.1 ABI, output guards and 64-bit seed')
    return library


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('library')
    parser.add_argument('rid')
    parser.add_argument('--run', action='store_true')
    parser.add_argument('--report', help='Write deterministic inputs/outputs for managed ABI comparison (requires --run)')
    args = parser.parse_args()
    if args.report and not args.run:
        parser.error('--report requires --run')
    print(verify_binary(args.library, args.rid))
    if args.run:
        verify_runtime(args.library, args.report)
    print('PASS: Worldline ' + args.rid)
