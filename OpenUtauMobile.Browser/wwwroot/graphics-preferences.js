const storageKey = "OpenUtauMobile.GraphicsBackendFallbackOrder.v1";

export function readOrder() {
    return globalThis.localStorage.getItem(storageKey);
}

export function writeOrder(json) {
    globalThis.localStorage.setItem(storageKey, json);
}
