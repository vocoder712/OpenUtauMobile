"""在发布目录验证原生库的实际加载和接口；不需要模型或用户数据。"""
import ctypes
import os
import sys
from pathlib import Path

root = Path(sys.argv[1]).resolve(strict=True)
if sys.platform == 'win32':
    names = ('worldline.dll', 'opum_game.dll')
    # Windows 同时核对非系统依赖能否从发布目录解析。
    search = os.add_dll_directory(str(root))
elif sys.platform == 'darwin':
    names = ('libworldline.dylib', 'libopum_game.dylib')
else:
    names = ('libworldline.so', 'libopum_game.so')
worldline = ctypes.CDLL(str(root / names[0]))
getattr(worldline, 'F0')
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
