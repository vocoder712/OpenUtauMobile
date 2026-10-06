# 浏览器运行与调试

在仓库根目录使用 PowerShell：

```powershell
$env:AVALONIA_TELEMETRY_OPTOUT='1'
$env:ASPNETCORE_URLS='http://localhost:5235'
dotnet run --project OpenUtauMobile.Browser/OpenUtauMobile.Browser.csproj -c Debug --no-launch-profile
```

打开 `http://localhost:5235`。此方式只监听指定的本地端口，不占用 80/443，也不需要证书。首次构建需要安装 `wasm-tools` 工作负载。使用 IDE 中现有启动配置时，HTTP 地址仍是 `http://localhost:5235`，HTTPS 地址是 `https://localhost:7169`。

## 构建配置

- Worldline 在 Build / Publish 中通过 Bazel 源码构建为静态 WASM 归档，随后由 .NET 链入运行时。
  除 `wasm-tools` 外需要 Python 3.10+、Git 和 Bazelisk；Emscripten 使用工作负载配套版本。
  详见 [Worldline 构建说明](../native/worldline/README.md)。这不包含 ONNX 后端或浏览器音频播放接入。
- 启用 `WasmEnableThreads`，保留音素化与短语构建后台线程。SDK 开发服务器会自动附加跨源隔离响应头。
- Debug 的原生编译使用 `-Oz`，原生链接使用 `-O2`。本机 .NET 10.0.12 的 `-O0` 多线程原生构建在运行时初始化时断言失败；这些设置保留 C# 的 Debug 配置和调试符号。相关上游记录：<https://github.com/dotnet/runtime/issues/112926>。
- 浏览器发布关闭托管裁剪并启用 JSON 反射，保留现有序列化模型和通过反射发现的插件类型。代价是下载体积较大。
- CJK 使用随应用打包的 Noto Sans CJK SC，西文使用 Inter。

## 发布托管

```powershell
$env:AVALONIA_TELEMETRY_OPTOUT='1'
dotnet publish OpenUtauMobile.Browser/OpenUtauMobile.Browser.csproj -c Release
```

静态文件位于 `OpenUtauMobile.Browser/bin/Release/net10.0-browser/publish/wwwroot`。正式服务器需要 HTTPS（本机 localhost 可使用 HTTP），并发送：

```http
Cross-Origin-Opener-Policy: same-origin
Cross-Origin-Embedder-Policy: require-corp
```

浏览器控制台的 `crossOriginIsolated` 应为 `true`，`typeof SharedArrayBuffer` 应为 `"function"`。缺少隔离条件时，多线程运行时无法启动。参见 [.NET WASM 多线程说明](https://github.com/dotnet/runtime/blob/main/src/mono/wasm/features.md#multi-threading)；该支持目前仍标记为实验性。

## 虚拟文件与持久化

浏览器中的打开、保存和目录选择统一使用应用内置选择器，只能浏览 `/OpenUtauMobile`。这是网站的虚拟目录，与 Windows 磁盘路径无关。系统文件的导入、导出尚未实现。

持久化使用 Emscripten IDBFS：

1. 页面启动时挂载虚拟目录，并通过 `FS.syncfs(true)` 从 IndexedDB 恢复文件；恢复完成后才启动 .NET 应用，避免偏好初始化覆盖旧数据。恢复失败会显示启动错误并停止。
2. 应用继续通过 `System.IO` 操作内存中的文件。目录和文件修改会触发延迟 100 毫秒的同步，将改动写入 IndexedDB。同步期间的新修改会继续同步。
3. 工程保存、模板保存、音频与日志导出、歌手安装在完成前等待 `FS.syncfs(false)` 成功；工程在持久化成功后才提示“已保存”。写入失败会进入现有错误处理，保留内存数据供重试。
4. 刷新或下次打开相同站点时从 IndexedDB 恢复。同一工作区使用 Web Locks 限制为一个页面打开，避免多个页面的内存副本互相覆盖。仍有未同步改动时，页面离开提示取决于浏览器规则；不依赖关闭页面时才写入。

IndexedDB 数据由浏览器保存在本机，不会上传服务器。数据按站点来源隔离：协议、主机名或端口不同就是不同工作区，例如 `http://localhost:5235` 与 `https://localhost:7169` 不共享文件。应用会请求 `navigator.storage.persist()` 降低存储压力下被自动清理的风险，但浏览器可以拒绝；用户清除站点数据、移除浏览器配置或结束无痕会话仍可能删除数据。它不能替代之后的文件导出备份功能。

IDBFS 会把已保存的文件加载到内存，适合先接通现有同步文件 API；大型歌手库的容量和内存占用仍需在导入功能中单独评估。缓存当前也在工作区内持久化。

参考：[Emscripten IDBFS](https://emscripten.org/docs/api_reference/Filesystem-API.html#idbfs)、[浏览器持久存储权限](https://developer.mozilla.org/en-US/docs/Web/API/StorageManager/persist)。

## 已知范围

启动、托管外部音素器扫描与后台音素化可以运行，但这不代表所有桌面插件和音频引擎都适用于浏览器。当前音频输出仍是既有的 Dummy 后端。ONNX 原生库在浏览器中不可用，重新读取偏好时的 ONNX 设备校验会记录异常；这不影响已验证的普通托管音素器。

持久化已验证工程保存后的页面刷新与浏览器关闭后重新打开，以及中文路径、覆盖、重命名、删除和写入失败后的重试。实际歌手导入与安装流程尚未验证。
