"""链接完整 WASM ABI，并在 Node 中检查缓冲区调用；不代替浏览器音频验收。"""
import argparse
import hashlib
import json
import os
from pathlib import Path
import subprocess
import sys

from verify import EXPORTS
from verify_static import verify_archive


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('directory', type=Path)
    args = parser.parse_args()
    directory = args.directory.resolve()
    manifest = json.loads((directory / 'worldline-build.json').read_text(encoding='utf-8'))
    library = directory / 'worldline.a'
    if manifest['rid'] != 'browser-wasm' or hashlib.sha256(library.read_bytes()).hexdigest() != manifest['library_sha256']:
        raise ValueError('Browser library provenance mismatch')
    sdk = manifest['static_sdk']
    verify_archive(library, 'browser-wasm', sdk['nm'])
    output = directory / 'verification'
    output.mkdir(exist_ok=True)
    module = output / 'worldline.cjs'
    # 将所有 ABI 导出交给最终链接器，检查归档是否遗漏了传递依赖。
    command = [sys.executable, str(Path(sdk['sdk']) / 'emscripten/emcc.py'), str(library),
               '-O2', '--no-entry', '-sMODULARIZE=1', '-sEXPORT_NAME=WorldlineModule', '-sENVIRONMENT=node',
               '-sALLOW_MEMORY_GROWTH=1', '-sWASM_BIGINT=1', '-sEXIT_RUNTIME=1',
               '-sEXPORTED_FUNCTIONS=' + json.dumps(['_' + name for name in sorted(EXPORTS)] + ['_malloc', '_free']),
               '-o', str(module)]
    command += [flag for flag in sdk['flags'] if flag in ('-pthread', '-msimd128', '-fwasm-exceptions', '-fexceptions')]
    subprocess.run(command, env=os.environ | sdk['environment'], check=True)
    script = output / 'verify.cjs'
    script.write_text('''const assert = require('node:assert/strict');
require('./worldline.cjs')().then(m => {
    assert.equal(m._F0FrameCount(0, 44100, 5, 0), 0);
    const samples = m._malloc(8820 * 4);
    m.HEAPF32.fill(0, samples / 4, samples / 4 + 8820);
    for (const method of [-1, 0, 1, 2]) {
        const count = m._F0FrameCount(8820, 44100, 5, method);
        assert(count > 0 && count < 1000);
        const buffer = m._malloc((count + 2) * 8);
        m.HEAPF64.fill(NaN, buffer / 8, buffer / 8 + count + 2);
        m.HEAPF64[buffer / 8] = 123456.5;
        m.HEAPF64[buffer / 8 + count + 1] = 123456.5;
        const written = m._F0(samples, 8820, 44100, 5, method, buffer + 8);
        assert(written > 0 && written <= count);
        assert.equal(m.HEAPF64[buffer / 8], 123456.5);
        assert.equal(m.HEAPF64[buffer / 8 + count + 1], 123456.5);
        for (let i = 0; i < written; i++) assert.equal(m.HEAPF64[buffer / 8 + i + 1], 0);
        m._free(buffer);
    }
    const config = m._malloc(24);
    m._InitAnalysisConfig(config, 44100, 220, 2048);
    assert.equal(m.HEAP32[config / 4], 44100);
    assert.equal(m.HEAP32[config / 4 + 1], 220);
    assert.equal(m.HEAP32[config / 4 + 2], 2048);
    assert.equal(m.HEAPF64[(config + 16) / 8], 220 * 1000 / 44100);
    assert.equal(m._WorldSynthesisSampleCount(24, 5, 44100), 5072);
    m._free(config);
    m._free(samples);
    console.log('Worldline WASM: all ABI symbols linked; F0 four modes, buffer guards, config and synthesis sizing passed.');
    process.exit(0);
}).catch(error => { console.error(error); process.exit(1); });
''', encoding='utf-8')
    subprocess.run([sdk['environment']['DOTNET_EMSCRIPTEN_NODE_JS'], str(script)], check=True)


if __name__ == '__main__':
    main()
