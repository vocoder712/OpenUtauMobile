const root = "/OpenUtauMobile";
let fs;
let revision = 0;
let persistedRevision = 0;
let restoring = true;
let flushing;
let timer;
let releaseLock;
let dirtyBytes = 0;
let firstChangeAt = 0;
const activeWrites = new Map();
const flushBytes = 32 * 1024 * 1024;
const flushInterval = 2000;

// 串行同步并追踪同步期间的新写入，避免旧快照覆盖新修改。
export async function flush() {
    if (!fs || restoring) throw new Error("Browser file storage is not ready.");
    if (flushing) {
        await flushing;
        return flush();
    }
    clearTimeout(timer);
    timer = undefined;
    flushing = (async () => {
        while (persistedRevision < revision) {
            const targetRevision = revision;
            const targetBytes = dirtyBytes;
            await sync(false);
            persistedRevision = targetRevision;
            dirtyBytes = Math.max(0, dirtyBytes - targetBytes);
        }
        firstChangeAt = 0;
    })();
    try {
        await flushing;
    } finally {
        flushing = undefined;
    }
}

function sync(populate) {
    return new Promise((resolve, reject) => fs.syncfs(populate, error =>
        error ? reject(error) : resolve()));
}

function scheduleFlush() {
    if (timer !== undefined || restoring) return;
    const delay = hasRecentWrites() ? flushInterval : dirtyBytes >= flushBytes ? 0 :
        Math.max(0, flushInterval - (Date.now() - firstChangeAt));
    timer = setTimeout(() => {
        timer = undefined;
        // 连续写入时等待文件关闭或空闲，避免每写一小段就重新保存整份快照。
        // 日志等长期打开的句柄在停止写入后也能保存，不会永久阻挡自动同步。
        if (hasRecentWrites()) { scheduleFlush(); return; }
        void flush().catch(error => console.error("Virtual file storage sync failed:", error));
    }, delay);
}

function hasRecentWrites() {
    const now = Date.now();
    for (const [stream, changedAt] of activeWrites) {
        if (now - changedAt >= flushInterval) activeWrites.delete(stream);
    }
    return activeWrites.size > 0;
}

function changed(bytes = 0) {
    if (restoring) return;
    revision++;
    dirtyBytes += bytes;
    if (!firstChangeAt) firstChangeAt = Date.now();
    scheduleFlush();
}

// 在挂载节点上追踪修改，不修改其他临时文件系统的操作。
function trackNode(node) {
    const operations = node.node_ops;
    node.node_ops = { ...operations };
    if (operations.mknod) {
        node.node_ops.mknod = (...args) => {
            const child = operations.mknod(...args);
            trackNode(child);
            changed();
            return child;
        };
    }
    for (const name of ["setattr", "rename", "unlink", "rmdir"]) {
        if (!operations[name]) continue;
        node.node_ops[name] = (...args) => {
            const result = operations[name](...args);
            changed();
            return result;
        };
    }
    const streams = node.stream_ops;
    node.stream_ops = { ...streams };
    node.stream_ops.close = stream => {
        try { return streams.close?.(stream); }
        finally {
            if (activeWrites.delete(stream)) {
                if (!activeWrites.size && dirtyBytes >= flushBytes) {
                    clearTimeout(timer);
                    timer = undefined;
                }
                changed();
            }
        }
    };
    for (const name of ["write", "allocate", "msync"]) {
        if (!streams[name]) continue;
        node.stream_ops[name] = (...args) => {
            const result = streams[name](...args);
            if (name === "write") activeWrites.set(args[0], Date.now());
            changed(name === "write" ? result : 0);
            return result;
        };
    }
}

// 只清理本功能保留的隐藏暂存目录，保留中断前已改名提交的用户文件。
function cleanupTransfers(directory) {
    for (const name of fs.readdir(directory)) {
        if (name === "." || name === "..") continue;
        const path = `${directory}/${name}`;
        if (!fs.isDir(fs.lstat(path).mode)) continue;
        if (/^\.opum-transfer-[0-9a-f]{32}$/.test(name)) removeTree(path);
        else cleanupTransfers(path);
    }
}

function removeTree(path) {
    if (fs.isDir(fs.lstat(path).mode)) {
        for (const name of fs.readdir(path))
            if (name !== "." && name !== "..") removeTree(`${path}/${name}`);
        fs.rmdir(path);
    } else fs.unlink(path);
}

export async function initialize(runtime) {
    // IDBFS 的各页面内存副本互不相通，同一工作区一次只允许一个页面写入。
    if (!navigator.locks) throw new Error("This browser does not support workspace locking.");
    await new Promise((resolve, reject) => {
        navigator.locks.request("OpenUtauMobile.Workspace", { ifAvailable: true }, lock => {
            if (!lock) {
                reject(new Error("The workspace is already open in another tab. Close that tab and reload."));
                return;
            }
            resolve();
            return new Promise(release => { releaseLock = release; });
        }).catch(reject);
    });
    fs = runtime.Module.FS;
    const idbfs = fs.filesystems.IDBFS;
    if (!idbfs) throw new Error("IDBFS is missing from the WebAssembly build.");
    fs.mkdirTree(root);
    fs.mount({
        ...idbfs,
        mount(mount) {
            const node = idbfs.mount(mount);
            trackNode(node);
            return node;
        }
    }, {}, root);
    await sync(true);
    restoring = false;
    cleanupTransfers(root);
    fs.mkdirTree(`${root}/Projects`);
    fs.mkdirTree(`${root}/Data`);
    await flush();
    runtime.setModuleImports("OpenUtauMobileFileSystem", { flush });
    console.info("Virtual file storage restored from IndexedDB.");

    // 已保存数据无需卸载时补写；仅在仍有未同步修改时提示，避免静默丢失。
    window.addEventListener("beforeunload", event => {
        if (persistedRevision < revision) {
            event.preventDefault();
            event.returnValue = "";
        }
    });
    document.addEventListener("visibilitychange", () => {
        if (document.visibilityState === "hidden")
            void flush().catch(error => console.error("Virtual file storage sync failed:", error));
    });
    // 返回缓存页面时重新加载，防止使用已失去独占锁的旧内存副本。
    window.addEventListener("pagehide", () => releaseLock?.());
    window.addEventListener("pageshow", event => { if (event.persisted) location.reload(); });
    if (navigator.storage?.persist) {
        void navigator.storage.persist().then(granted =>
            console.info("Browser persistent storage protection:", granted))
            .catch(error => console.warn("Storage protection request failed:", error));
    }
}