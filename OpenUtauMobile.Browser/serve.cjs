const { createServer } = require("node:http");
const { createReadStream, readFileSync, statSync } = require("node:fs");
const { extname, resolve, sep } = require("node:path");

const [directory, port = "5237", mode] = process.argv.slice(2);
if (!directory) {
    console.error("node OpenUtauMobile.Browser/serve.cjs <wwwroot> [port] [--production]");
    process.exit(1);
}

const root = resolve(directory);
const production = mode === "--production";
const immutable = production
    ? new Set(JSON.parse(readFileSync(resolve(root, "version.json"), "utf8")).immutable)
    : new Set();
const types = {
    ".html": "text/html; charset=utf-8",
    ".js": "text/javascript",
    ".mjs": "text/javascript",
    ".css": "text/css",
    ".json": "application/json",
    ".wasm": "application/wasm",
    ".otf": "font/otf",
    ".ico": "image/x-icon",
};
const encodings = { br: ".br", gzip: ".gz" };

createServer((request, response) => {
    response.setHeader("Cross-Origin-Opener-Policy", "same-origin");
    response.setHeader("Cross-Origin-Embedder-Policy", "require-corp");
    response.setHeader("Cache-Control", "no-store");
    if (request.method !== "GET" && request.method !== "HEAD") {
        response.writeHead(405, { Allow: "GET, HEAD" }).end();
        return;
    }

    let pathname;
    try {
        pathname = decodeURIComponent(new URL(request.url, "http://localhost").pathname);
    } catch {
        response.writeHead(400).end();
        return;
    }
    const route = pathname === "/" ? "index.html" : pathname.slice(1);
    const original = resolve(root, route);
    let file = original;
    let stat = original.startsWith(root + sep) && statSync(original, { throwIfNoEntry: false });
    if (!stat?.isFile()) {
        response.writeHead(404).end();
        return;
    }

    const accepted = (request.headers["accept-encoding"] ?? "").split(",")
        .map(value => value.trim().split(/;\s*q=/))
        .filter(([encoding, quality = "1"]) => Object.hasOwn(encodings, encoding) && Number(quality) > 0)
        .sort((a, b) => Number(b[1] ?? 1) - Number(a[1] ?? 1) || Number(b[0] === "br") - Number(a[0] === "br"));
    for (const [encoding] of accepted) {
        const compressed = original + encodings[encoding];
        const compressedStat = statSync(compressed, { throwIfNoEntry: false });
        if (!compressedStat?.isFile()) continue;
        file = compressed;
        stat = compressedStat;
        response.setHeader("Content-Encoding", encoding);
        break;
    }

    response.setHeader("Vary", "Accept-Encoding");
    if (production && route !== "version.json") {
        response.setHeader("Cache-Control", immutable.has(route) ? "max-age=31536000, immutable" : "no-cache");
        const etag = `W/"${stat.size}-${stat.mtimeMs}-${file.slice(original.length)}"`;
        response.setHeader("ETag", etag);
        if (request.headers["if-none-match"]?.split(/,\s*/).some(tag => tag === etag || tag === "*")) {
            response.writeHead(304).end();
            return;
        }
    }

    response.writeHead(200, {
        "Content-Type": types[extname(original)] ?? "application/octet-stream",
        "Content-Length": stat.size,
    });
    if (request.method === "HEAD") response.end();
    else createReadStream(file).pipe(response);
}).listen(Number(port), "127.0.0.1", () => console.info(`http://127.0.0.1:${port}/`));
