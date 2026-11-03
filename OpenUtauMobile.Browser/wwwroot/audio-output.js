// 此模块只在浏览器主线程访问 AudioContext，音频线程只消费共享帧队列。
import { Read, Write, Playing, Ended, Consumed, Requested, headerLength, capacity, targetFrames } from './audio-output-buffer.js';
const deviceStorageKey = "OpenUtauMobile.AudioOutput.Device";
let context, node, header, samples;
let generation = -1, intent = 0, consumedBase = 0, lastConsumed = 0;
let onNeedData, onStatus, onError, onDeviceChanged;
let selectedId = "", selectedName = "", selectionVersion = 0;
let resumePending = null;
let failed = false;

export async function initialize(runtime) {
    const exports = await runtime.getAssemblyExports("OpenUtauMobile.Browser");
    const bridge = exports.OpenUtauMobile.Browser.Services.Audio.BrowserAudioInterop;
    return initializeTransport(bridge.RequestData, bridge.UpdateStatus, bridge.ReceiveError, bridge.UpdateDevice);
}

export async function initializeTransport(needData, status, error, deviceChanged) {
    onNeedData = needData;
    onStatus = status;
    onError = error;
    onDeviceChanged = deviceChanged;
    if (!globalThis.crossOriginIsolated || typeof SharedArrayBuffer !== "function") {
        throw new Error("Browser audio requires cross-origin isolation.");
    }
    try {
        try { context = new AudioContext({ sampleRate: 44100, latencyHint: "interactive" }); }
        catch (error) {
            if (error.name !== "NotSupportedError") throw error;
            context = new AudioContext({ latencyHint: "interactive" });
        }
        await context.suspend();
        await context.audioWorklet.addModule(new URL("./audio-output-worklet.js", import.meta.url));
        node = new AudioWorkletNode(context, "openutau-audio-output", {
            numberOfInputs: 0, numberOfOutputs: 1, outputChannelCount: [2]
        });
        node.connect(context.destination);
        node.port.onmessage = ({ data }) => {
            if (data.generation !== generation) return;
            if (data.type === "need") notify(onNeedData, generation);
            if (data.type === "ended") intent = 0;
            report();
        };
        node.onprocessorerror = () => fail(new Error("The browser audio processor stopped."));
        context.onstatechange = report;
        document.addEventListener("pointerdown", unlock, true);
        document.addEventListener("keydown", unlock, true);
        document.addEventListener("visibilitychange", visibilityChanged);
        window.addEventListener("pagehide", pageHidden);
        window.addEventListener("pageshow", visibilityChanged);
        navigator.mediaDevices?.addEventListener("devicechange", devicesChanged);
        await restoreDevice();
        return context.sampleRate;
    } catch (error) {
        await dispose();
        throw error;
    }
}

export function canSelectOutput() {
    return typeof context?.setSinkId === "function"
        && typeof navigator.mediaDevices?.selectAudioOutput === "function";
}

// 在用户原始事件中恢复上下文，允许之后的后台渲染完成时直接开始播放。
function unlock(event) {
    if (event.isTrusted) resume(true);
}

function resume(force = false) {
    if (!context || context.state === "closed" || context.state === "running" || document.hidden) return;
    if (force || !resumePending) {
        const attempt = context.resume().catch(error => {
            if (intent === 1) notify(onError, generation, error.message ?? String(error));
        }).finally(() => {
            if (resumePending === attempt) resumePending = null;
            report();
        });
        resumePending = attempt;
    }
}

export function setPlayback(nextGeneration, nextIntent) {
    if (failed) throw new Error("Reload the page to restart the browser audio processor.");
    if (nextGeneration !== generation) {
        if (header) Atomics.store(header, Playing, 0);
        generation = nextGeneration;
        consumedBase = lastConsumed = 0;
        const buffer = new SharedArrayBuffer(headerLength * 4 + capacity * 2 * 4);
        header = new Int32Array(buffer, 0, headerLength);
        samples = new Float32Array(buffer, headerLength * 4);
        node.port.postMessage({ type: "configure", generation, buffer });
    }
    intent = nextIntent;
    Atomics.store(header, Playing, intent === 1 ? 1 : 0);
    if (intent === 1) resume();
    report();
}

export function writableFrames(expectedGeneration) {
    if (!header || expectedGeneration !== generation || intent !== 1) return 0;
    const queued = ((Atomics.load(header, Write) >>> 0) - (Atomics.load(header, Read) >>> 0)) >>> 0;
    return Math.max(0, targetFrames - queued);
}

export function writeSamples(expectedGeneration, bytes, count) {
    if (expectedGeneration !== generation || intent !== 1) return;
    const data = new Float32Array(bytes.buffer, bytes.byteOffset, count);
    const write = Atomics.load(header, Write) >>> 0;
    for (let frame = 0; frame < count / 2; frame++) {
        const offset = ((write + frame) % capacity) * 2;
        samples[offset] = data[frame * 2];
        samples[offset + 1] = data[frame * 2 + 1];
    }
    Atomics.store(header, Write, (write + count / 2) | 0);
    Atomics.store(header, Requested, 0);
    report();
}

export function endInput(expectedGeneration) {
    if (expectedGeneration === generation) Atomics.store(header, Ended, 1);
}

function report() {
    if (!header || !onStatus) return;
    const consumed = Atomics.load(header, Consumed) >>> 0;
    if (consumed < lastConsumed) consumedBase += 0x100000000;
    lastConsumed = consumed;
    const queued = ((Atomics.load(header, Write) >>> 0) - (Atomics.load(header, Read) >>> 0)) >>> 0;
    const state = failed ? 3 : intent === 0 ? 0
        : intent === 1 && context.state === "running" && (queued > 0 || consumed > 0) ? 1 : 2;
    notify(onStatus, generation, consumedBase + consumed, state, latencyMilliseconds());
}

// .NET 多线程模式要求异步导出；通知不阻塞浏览器主线程，也不跨语言同步重入。
function notify(callback, ...args) {
    if (callback) Promise.resolve().then(() => callback(...args)).catch(error => console.error("Audio callback failed:", error));
}

export function latencyMilliseconds() {
    if (!header) return 0;
    const queued = ((Atomics.load(header, Write) >>> 0) - (Atomics.load(header, Read) >>> 0)) >>> 0;
    return (queued / context.sampleRate + (context.baseLatency || 0) + (context.outputLatency || 0)) * 1000;
}

function fail(error) {
    failed = true;
    intent = 0;
    if (header) Atomics.store(header, Playing, 0);
    notify(onError, generation, error.message ?? String(error));
    report();
}

function visibilityChanged() {
    if (document.hidden) context?.suspend().catch(fail);
    else if (intent === 1) {
        if (header) Atomics.store(header, Playing, 1);
        resume();
    }
}

function pageHidden() {
    if (header) Atomics.store(header, Playing, 0);
    context?.suspend().catch(fail);
}

async function restoreDevice() {
    if (!canSelectOutput()) return;
    let saved;
    try { saved = localStorage.getItem(deviceStorageKey); } catch { return; }
    if (!saved) return;
    try {
        const devices = await navigator.mediaDevices.enumerateDevices();
        const device = devices.find(item => item.kind === "audiooutput" && item.deviceId === saved);
        if (!device) {
            setDevice("", "");
            return;
        }
        await context.setSinkId(saved);
        setDevice(saved, device.label);
    } catch {
        // 失效的设备或权限不妨碍默认播放，也不在启动时弹出授权窗口。
        setDevice("", "");
    }
}

export async function selectOutput() {
    if (!canSelectOutput()) return;
    const version = ++selectionVersion;
    // 必须在首次 await 之前请求系统选择器，以保留用户激活。
    try {
        const device = await navigator.mediaDevices.selectAudioOutput();
        if (version !== selectionVersion) return;
        await context.setSinkId(device.deviceId);
        if (version === selectionVersion) setDevice(device.deviceId, device.label);
    } catch (error) {
        if (error.name !== "NotAllowedError" && error.name !== "AbortError") throw error;
    }
}

function setDevice(id, name) {
    selectedId = id;
    selectedName = name;
    try {
        if (id) localStorage.setItem(deviceStorageKey, id);
        else localStorage.removeItem(deviceStorageKey);
    } catch { /* 存储被禁用时，本次设备选择仍然有效。 */ }
    notify(onDeviceChanged, selectedId, selectedName);
}

async function devicesChanged() {
    if (!selectedId) return;
    try {
        const devices = await navigator.mediaDevices.enumerateDevices();
        if (!devices.some(device => device.kind === "audiooutput" && device.deviceId === selectedId)) {
            ++selectionVersion;
            await context.setSinkId("");
            setDevice("", "");
        }
    } catch (error) { notify(onError, generation, error.message ?? String(error)); }
}

export async function dispose() {
    intent = 0;
    if (header) Atomics.store(header, Playing, 0);
    document.removeEventListener("pointerdown", unlock, true);
    document.removeEventListener("keydown", unlock, true);
    document.removeEventListener("visibilitychange", visibilityChanged);
    window.removeEventListener("pagehide", pageHidden);
    window.removeEventListener("pageshow", visibilityChanged);
    navigator.mediaDevices?.removeEventListener("devicechange", devicesChanged);
    if (node) { node.port.onmessage = null; node.disconnect(); node.port.close(); }
    if (context) { context.onstatechange = null; await context.close(); }
    context = node = header = samples = null;
}
