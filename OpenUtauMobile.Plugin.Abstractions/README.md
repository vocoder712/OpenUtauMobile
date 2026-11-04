# 歌手插件契约 v2

插件引用本程序集与 `OpenUtau.Core` 时设置 `Private="false"`，安装包只携带插件自身文件。

使用 `[assembly: SingerPlugin(typeof(PluginEntry))]` 声明实现 `ISingerPlugin` 的公开无参入口。在 `Register` 内同步登记类型、发现提供方和安装器，不执行扫描或安装。设置通过运行时 WebUI 会话提供，详见 [Web 设置契约](../docs/PLUGIN_WEB_SETTINGS.md)。

- 插件 ID 使用小写字母开头，最长 160 个字符，仅允许小写字母、数字、点、下划线和短横线，不能以点结尾或使用系统设备保留名；`opum.` 为保留前缀。`SingerPluginContract.ValidatePluginId` 保证它能直接作为跨平台单级目录名。能力 ID 使用 `<PluginId>:<名称>`。
- 数据目录为 `DataPath/PluginData/<PluginId>`，不迁移或回退读取旧哈希目录。
- 类型桥接值固定在 `0x10000000` 至 `int.MaxValue` 内，宿主检查冲突。
- 可用类型的 renderer 工厂须与入口同程序集；类型值、renderer 集合和默认值须与 Core 一致。不可用类型填写原因，renderer 集合与默认值留空。
- 安装器声明文件后缀，由用户先选安装器再选包；后缀允许重叠。
- 插件安装使用独立的声明式页面；内置安装由宿主实现。
- `PrepareAsync` 同时接收末步字段与目标；输入变化后须重新准备，计划只能提交一次。
- 可选集合的默认值按空集合处理。登记失败不发布本批能力；替换插件需要重启。

发现、删除、安装和 Web 设置上下文通过 `Notifications` 提供用户警告：

```csharp
context.Notifications.PublishWarning(new("catalog.fetch-failed", "获取歌手列表失败。", "请检查网络或打开插件设置重新认证。"));
// 成功恢复后清除同代码警告。
context.Notifications.ClearWarning("catalog.fetch-failed");
```

同插件同代码更新原警告，不重复堆积；宿主自动限定插件归属，允许后台线程调用。Message/Details 是插件本地化后的纯文本，不能包含凭据或认证 URL。`Log` 继续只负责诊断日志，发现结果的 Diagnostics 不会自动转成用户警告。

`Notifications` 使用宿主既有服务的通知接口，操作上下文的构造参数与 v2 契约版本保持不变。
