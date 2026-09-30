import { dotnet } from './_framework/dotnet.js'
import { initialize } from './file-system.js'

const is_browser = typeof window != "undefined";
if (!is_browser) throw new Error(`Expected to be running in a browser`);

try {
    const dotnetRuntime = await dotnet
        .withDiagnosticTracing(false)
        .withApplicationArgumentsFromQuery()
        .create();

    const config = dotnetRuntime.getConfig();

    await initialize(dotnetRuntime);
    await dotnetRuntime.runMain(config.mainAssemblyName, [globalThis.location.href]);
} catch (error) {
    console.error("Browser startup failed:", error);
    // 恢复失败时停止启动，避免用空文件系统覆盖已有数据。
    const message = document.createElement("p");
    message.setAttribute("role", "alert");
    message.textContent = `无法启动 / Unable to start: ${error.message ?? error}`;
    document.getElementById("out").replaceChildren(message);
    throw error;
}
