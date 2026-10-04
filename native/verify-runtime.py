"""在发布目录验证原生库的实际加载和接口；不需要模型或用户数据。"""
import ctypes
import hashlib
import json
import os
import platform
import sys
from pathlib import Path
from worldline.verify import verify_binary, verify_runtime as verify_worldline_runtime

root = Path(sys.argv[1]).resolve(strict=True)
if sys.platform == 'win32':
    names = ('worldline.dll', 'opum_game.dll')
    # Windows 同时核对非系统依赖能否从发布目录解析。
    search = os.add_dll_directory(str(root))
elif sys.platform == 'darwin':
    names = ('libworldline.dylib', 'libopum_game.dylib')
else:
    names = ('libworldline.so', 'libopum_game.so')
worldline = verify_worldline_runtime(root / names[0])
rid = {'win32': 'win', 'darwin': 'osx'}.get(sys.platform, 'linux') + '-' + ('arm64' if platform.machine().lower() in ('arm64', 'aarch64') else 'x64')
verify_binary(root / names[0], rid)
metadata = root / 'WorldlineLicenses'
provenance = json.loads((metadata / 'worldline-build.json').read_text(encoding='utf-8'))
if provenance['rid'] != rid or provenance['library_sha256'] != hashlib.sha256((root / names[0]).read_bytes()).hexdigest():
    raise ValueError('Worldline library does not match its build provenance')
for name in ('worldline', 'world', 'libgvps', 'libnpy', 'libpyin', 'spline', 'miniaudio', 'xxhash'):
    if not (metadata / f'LICENSE.{name}.txt').read_text(encoding='utf-8').strip():
        raise ValueError(f'Missing Worldline license: {name}')
game = ctypes.CDLL(str(root / names[1]))
game.opum_game_open.argtypes = [ctypes.c_char_p, ctypes.c_char_p, ctypes.c_int]
game.opum_game_open.restype = ctypes.c_void_p
getattr(game, 'opum_game_infer')
getattr(game, 'opum_game_close')
getattr(game, 'opum_game_error')
error = ctypes.create_string_buffer(1024)
handle = game.opum_game_open(b'', error, len(error))
assert not handle and error.value, 'Invalid model path must produce a managed error'
print(f'PASS: {names[0]} and {names[1]} load from {root}; GAME C ABI responds correctly')
