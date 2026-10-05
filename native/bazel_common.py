"""原生 Bazel 构建共用的进程锁和增量文件写入。"""
from contextlib import contextmanager
import os
import time

@contextmanager
def build_lock(path):
    """串行化同一 RID 的构建副本和安装，进程退出后由系统释放锁。"""
    path.parent.mkdir(parents=True, exist_ok=True)
    with path.open('a+b') as lock:
        if not lock.tell():
            lock.write(b'0')
            lock.flush()
        deadline = time.monotonic() + 900
        while True:
            try:
                if os.name == 'nt':
                    import msvcrt
                    lock.seek(0)
                    msvcrt.locking(lock.fileno(), msvcrt.LK_NBLCK, 1)
                else:
                    import fcntl
                    fcntl.flock(lock, fcntl.LOCK_EX | fcntl.LOCK_NB)
                break
            except OSError:
                if time.monotonic() > deadline:
                    raise TimeoutError(f'Another native build holds {path}')
                time.sleep(0.25)
        try:
            yield
        finally:
            if os.name == 'nt':
                lock.seek(0)
                msvcrt.locking(lock.fileno(), msvcrt.LK_UNLCK, 1)
            else:
                fcntl.flock(lock, fcntl.LOCK_UN)


def write_changed(path, content):
    path.parent.mkdir(parents=True, exist_ok=True)
    if not path.exists() or path.read_bytes() != content:
        path.write_bytes(content)
