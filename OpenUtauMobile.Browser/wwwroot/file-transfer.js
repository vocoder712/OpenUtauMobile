let fs;
let nextId = 0;
const files = new Map();
const chunkSize = 128 * 1024;
const memoryThreshold = 8 * 1024 * 1024;
const temporary = new Map();
let databasePromise;

// 文件输入和下载单独注册，避免改变工作区持久化模块的职责。
export async function initialize(runtime) {
    fs = runtime.Module.FS;
    // 工作区锁已在前一初始化阶段取得，仅清理本应用上次中断留下的传输暂存。
    const db = await database();
    await transaction(db, store => store.clear());
    runtime.setModuleImports("OpenUtauMobileFileTransfer", {
        pick, readChunk, release, download, canPickFolder,
        createTemporary, writeTemporary, completeTemporary, disposeTemporary
    });
}

function database() {
    return databasePromise ??= new Promise((resolve, reject) => {
        const request = indexedDB.open("OpenUtauMobile.Transfers", 1);
        request.onupgradeneeded = () => request.result.createObjectStore("chunks");
        request.onsuccess = () => resolve(request.result);
        request.onerror = () => reject(request.error);
    });
}

function transaction(db, write) {
    return new Promise((resolve, reject) => {
        // 暂存也等待事务完成；配额或写入失败沿调用链返回，不静默退回无限内存缓冲。
        const tx = db.transaction("chunks", "readwrite");
        tx.oncomplete = () => resolve();
        tx.onabort = tx.onerror = () => reject(tx.error ?? new Error("Temporary storage transaction failed."));
        try { write(tx.objectStore("chunks")); }
        catch (error) { tx.abort(); reject(error); }
    });
}

function temporaryRange(id) {
    return IDBKeyRange.bound([id, 0], [id, Number.MAX_SAFE_INTEGER]);
}

function createTemporary() {
    const id = ++nextId;
    const item = { id, parts: [], length: 0, chunks: 0, disk: false, blob: null };
    temporary.set(id, item);
    files.set(id, item);
    return id;
}

async function writeTemporary(id, encoded) {
    const item = temporary.get(id);
    if (!item || item.blob) throw new Error("Temporary transfer is not writable.");
    const binary = atob(encoded);
    const bytes = new Uint8Array(binary.length);
    for (let index = 0; index < binary.length; index++) bytes[index] = binary.charCodeAt(index);
    const blob = new Blob([bytes]);
    const nextLength = item.length + bytes.length;
    if (item.disk || nextLength > memoryThreshold) {
        const db = await database();
        await transaction(db, store => {
            // 第一次溢写时将已有的小块一并提交，之后仅写入新块。
            if (!item.disk) item.parts.forEach((part, index) => store.put(part, [id, index]));
            store.put(blob, [id, item.chunks]);
        });
        item.parts = [];
        item.disk = true;
    } else item.parts.push(blob);
    item.chunks++;
    item.length = nextLength;
}

async function completeTemporary(id) {
    const item = temporary.get(id);
    if (!item) throw new Error("Temporary transfer is no longer available.");
    if (item.blob) return;
    let parts = item.parts;
    if (item.disk) {
        const db = await database();
        parts = await new Promise((resolve, reject) => {
            const tx = db.transaction("chunks", "readonly");
            const request = tx.objectStore("chunks").getAll(temporaryRange(id));
            tx.oncomplete = () => resolve(request.result);
            tx.onabort = tx.onerror = () => reject(tx.error);
        });
    }
    // 组合持久化 Blob 引用，不把整份文件读成 ArrayBuffer 或托管字节数组。
    item.blob = new Blob(parts);
    item.parts = [];
}

async function disposeTemporary(id) {
    const item = temporary.get(id);
    temporary.delete(id);
    files.delete(id);
    if (item?.disk) {
        const db = await database();
        await transaction(db, store => store.delete(temporaryRange(id)));
    }
}

function canPickFolder() {
    return typeof window.showDirectoryPicker === "function" || "webkitdirectory" in document.createElement("input");
}

async function pick(folder) {
    const entries = [];
    const owned = [];
    function add(path, file) {
        if (entries.length >= 50000) throw new Error("Too many files in the selected folder.");
        const id = file ? ++nextId : 0;
        if (file) { files.set(id, file); owned.push(id); }
        entries.push({ id, path, directory: !file, size: file?.size ?? 0 });
    }
    try {
        if (folder && typeof window.showDirectoryPicker === "function") {
            // 必须在点击处理的首次异步等待之前打开系统选择器。
            const handle = await window.showDirectoryPicker({ mode: "read" });
            async function visit(directory, path, depth) {
                if (depth > 64) throw new Error("The selected folder is too deep.");
                add(path, null);
                for await (const child of directory.values()) {
                    if (child.kind === "directory") await visit(child, `${path}/${child.name}`, depth + 1);
                    else add(`${path}/${child.name}`, await child.getFile());
                }
            }
            await visit(handle, handle.name, 0);
        } else {
            const selected = await new Promise((resolve, reject) => {
                const input = document.createElement("input");
                input.type = "file";
                input.multiple = true;
                input.hidden = true;
                if (folder) {
                    if (!("webkitdirectory" in input)) { reject(new Error("Folder selection is unavailable. Import a ZIP instead.")); return; }
                    input.webkitdirectory = true;
                }
                const finish = result => { input.remove(); resolve(result); };
                input.addEventListener("change", () => finish(Array.from(input.files ?? [])), { once: true });
                input.addEventListener("cancel", () => finish([]), { once: true });
                document.body.append(input);
                input.click();
            });
            for (const file of selected) add(folder ? file.webkitRelativePath : file.name, file);
        }
        return JSON.stringify(entries);
    } catch (error) {
        for (const id of owned) files.delete(id);
        if (error.name === "AbortError") return "[]";
        throw error;
    }
}

async function readChunk(id, offset, count) {
    const selected = files.get(id);
    const file = selected instanceof Blob ? selected : selected?.blob;
    if (!file) throw new Error("The selected file is no longer available.");
    const bytes = new Uint8Array(await file.slice(offset, offset + Math.min(count, chunkSize)).arrayBuffer());
    let binary = "";
    for (let start = 0; start < bytes.length; start += 8192)
        binary += String.fromCharCode(...bytes.subarray(start, start + 8192));
    return btoa(binary);
}

function release(ids) {
    for (const id of JSON.parse(ids)) files.delete(id);
}

// 由“保存到设备”的独立点击调用；不在异步打包后借用过期的用户手势。
function download(path, name) {
    let blob;
    if (path.startsWith("transfer:")) {
        blob = temporary.get(Number(path.substring("transfer:".length)))?.blob;
        if (!blob) throw new Error("The prepared export is no longer available.");
    } else {
        const stream = fs.open(path, "r");
        const parts = [];
        try {
            while (true) {
                const buffer = new Uint8Array(chunkSize);
                const count = fs.read(stream, buffer, 0, buffer.length);
                if (!count) break;
                parts.push(buffer.subarray(0, count));
            }
        } finally {
            fs.close(stream);
        }
        blob = new Blob(parts);
    }
    const url = URL.createObjectURL(blob);
    const anchor = document.createElement("a");
    anchor.href = url;
    anchor.download = name;
    document.body.append(anchor);
    try { anchor.click(); }
    finally {
        anchor.remove();
        // 下载导航仍可能使用该地址，稍后释放；不持有额外的托管文件副本。
        setTimeout(() => URL.revokeObjectURL(url), 60000);
    }
}