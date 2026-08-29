export function openExternalUrl(url) {
    const openedWindow = globalThis.open(url, "_blank", "noopener,noreferrer");
    return openedWindow !== null;
}
