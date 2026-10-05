"""核对 GAME 库的架构、来源记录、许可证及无模型 C ABI 调用。"""
import argparse
import ctypes
import hashlib
import json
from pathlib import Path
import platform
import sys

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
from worldline.verify import verify_binary

EXPORTS = {'opum_game_open', 'opum_game_close', 'opum_game_infer', 'opum_game_error'}


def verify(root, rid, run=False):
    root = Path(root).resolve()
    name = 'opum_game.dll' if rid.startswith('win-') else ('libopum_game.dylib' if rid.startswith('osx-') else 'libopum_game.so')
    path = root / name
    result = verify_binary(path, rid, EXPORTS)
    provenance = json.loads((root / 'game-build.json').read_text(encoding='utf-8'))
    if provenance['rid'] != rid or provenance['library_sha256'] != hashlib.sha256(path.read_bytes()).hexdigest():
        raise ValueError('GAME library does not match its build provenance')
    for dependency in ('game.cpp', 'ggml', 'pocketfft'):
        if not (root / f'LICENSE.{dependency}.txt').read_text(encoding='utf-8').strip():
            raise ValueError(f'Missing GAME license: {dependency}')
    if run:
        host = {'win32': 'win', 'darwin': 'osx'}.get(sys.platform, 'linux')
        arch = 'arm64' if platform.machine().lower() in ('arm64', 'aarch64') else 'x64'
        if ctypes.sizeof(ctypes.c_void_p) == 4:
            arch = 'x86'
        if rid != f'{host}-{arch}':
            raise ValueError('Runtime verification requires a matching host and Python architecture')
        library = ctypes.CDLL(str(path))
        library.opum_game_open.argtypes = [ctypes.c_char_p, ctypes.c_char_p, ctypes.c_int]
        library.opum_game_open.restype = ctypes.c_void_p
        library.opum_game_error.argtypes = [ctypes.c_void_p]
        library.opum_game_error.restype = ctypes.c_char_p
        library.opum_game_close.argtypes = [ctypes.c_void_p]
        library.opum_game_close.restype = None
        for model in (None, b'', b'opum-deliberately-missing.gguf'):
            error = ctypes.create_string_buffer(1024)
            handle = library.opum_game_open(model, error, len(error))
            if handle or not error.value:
                raise ValueError('Invalid model path must return an error without a handle')
        if not library.opum_game_error(None):
            raise ValueError('Missing invalid-handle error')
        library.opum_game_close(None)
    print(f'PASS: GAME {rid}; {result}')
    return result


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('directory')
    parser.add_argument('rid')
    parser.add_argument('--run', action='store_true')
    args = parser.parse_args()
    verify(args.directory, args.rid, args.run)
