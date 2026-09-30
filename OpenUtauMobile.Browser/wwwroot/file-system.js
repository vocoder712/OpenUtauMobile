const root = "/OpenUtauMobile";
let fs;
let revision = 0;
let persistedRevision = 0;
let restoring = true;
let flushing;
let timer;
let releaseLock;

// 串行同步并追踪同步期间的新写入，避免旧快照覆盖新修改。
export async function flush() {
    if (!fs || restoring) throw new Error("Browser file storage is not ready.");
    if (flushing) {
        await flushing;
        return flush();
    }
    clearTimeout(timer);
    flushing = (async () => {
        while (persistedRevision < revision) {
            const targetRevision = revision;
            await sync(false);
            persistedRevision = targetRevision;
        }
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

function changed() {
    if (restoring) return;
    revision++;
    clearTimeout(timer);
    timer = setTimeout(() => flush().catch(error =>
        console.error("Virtual file storage sync failed:", error)), 100);
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
    for (const name of ["write", "allocate", "msync"]) {
        if (!streams[name]) continue;
        node.stream_ops[name] = (...args) => {
            const result = streams[name](...args);
            changed();
            return result;
        };
    }
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
