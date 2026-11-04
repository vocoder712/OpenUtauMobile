# 歌手插件契约 v2

插件引用本程序集与 `OpenUtau.Core` 时设置 `Private="false"`，安装包只携带插件自身文件。

使用 `[assembly: SingerPlugin(typeof(PluginEntry))]` 声明实现 `ISingerPlugin` 的公开无参入口。在 `Register` 内同步登记类型、发现提供方和安装器，不执行扫描或安装。设置通过运行时 WebUI 会话提供，详见 [Web 设置契约](../docs/PLUGIN_WEB_SETTINGS.md)。

- 插件 ID 使用小写字母开头，允许数字、点、下划线和短横线；`opum.` 为保留前缀。能力 ID 使用 `<PluginId>:<名称>`。
- 类型桥接值固定在 `0x10000000` 至 `int.MaxValue` 内，宿主检查冲突。
- 可用类型的 renderer 工厂须与入口同程序集；类型值、renderer 集合和默认值须与 Core 一致。不可用类型填写原因，renderer 集合与默认值留空。
- 安装器声明文件后缀，由用户先选安装器再选包；后缀允许重叠。
- 插件安装使用独立的声明式页面；内置安装由宿主实现。
- `PrepareAsync` 同时接收末步字段与目标；输入变化后须重新准备，计划只能提交一次。
- 可选集合的默认值按空集合处理。登记失败不发布本批能力；替换插件需要重启。
