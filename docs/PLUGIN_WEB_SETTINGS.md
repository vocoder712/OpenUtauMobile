# 插件 Web 设置（契约 v2）

插件管理卡片上的齿轮进入独立 WebView 页面。宿主使用 `Avalonia.Controls.WebView` 的系统适配器：Windows WebView2、Apple WKWebView、Linux WebKit。宿主不携带 Chromium/CEF；系统缺少对应运行库或加载失败时保留右上角的系统浏览器入口。

## 动态声明

在 `ISingerPlugin` 上实现两个方法：

```csharp
public ValueTask<PluginWebSettingsDescriptor?> GetWebSettingsAsync(
    PluginWebSettingsContext context, CancellationToken cancellationToken)
{
    // 返回 null 时不显示设置齿轮。查询不得启动 HTTP 服务或弹出页面。
    return ValueTask.FromResult<PluginWebSettingsDescriptor?>(
        new PluginWebSettingsDescriptor("账户与设置"));
}

public async ValueTask<IPluginWebSettingsSession> OpenWebSettingsAsync(
    PluginWebSettingsContext context, CancellationToken cancellationToken)
{
    // 插件自行提供 HTTP/HTTPS 页面，返回拥有服务生命周期的会话对象。
    return await CreateSessionAsync(context.Storage.DataDirectory, cancellationToken);
}
```

宿主在进入插件管理页或点击右上角刷新按钮时查询当前声明，没有定时轮询。点击齿轮时再次查询，创建页面会话之前也会确认能力仍可用。查询应快速完成并响应取消；查询失败或超时隐藏对应齿轮，不影响其他插件。

`GetWebSettingsAsync` 的默认实现返回 null，因此没有 Web 设置的插件不需要实现任何额外方法。尚未激活、登记失败和待删除的插件不会显示齿轮；安装插件后仍按既有流程重启激活。

## 会话与存储

- `IPluginWebSettingsSession.Address` 是绝对 HTTP/HTTPS 地址；可以是插件创建的本地服务，也可以是远端设置页。地址在每次打开时生成，不缓存登记时的 URL。
- `DisposeAsync` 负责关闭本地监听、轮询和临时资源。取消令牌在离开页面时取消；不要把监听器生命周期交给浏览器标签页。
- `ReloadResourcesOnClose` 默认 true，释放会话后由宿主刷新歌手发现；不需要刷新时返回 false。
- 右上角按钮使用系统浏览器打开会话地址。此时内嵌页面仍拥有会话；返回管理页会结束会话，本地网页在外部浏览器中也随之失效。
- `context.Storage.DataDirectory` 是插件独占的稳定目录，直接使用 `DataPath/PluginData/<PluginId>`；契约校验插件 ID 为合法的跨平台单级目录名。不迁移或读取旧哈希目录。`ResourceDirectory` 是其中的 `Resources` 子目录。
- 插件自行管理配置格式、凭据保护、读写并发和版本迁移；发现和安装上下文通过 `Storage` 提供相同目录。

宿主不解释网页的表单或账户协议，不通过 JavaScript 接收账户 token，也不提供可执行宿主命令的脚本桥。运行平台的文件和网络限制仍然适用。

## v1 迁移

本次为破坏性契约升级，`SingerPluginContract.Version` 为 2，插件需重新编译。

移除 `RegisterSettings`、`IPluginSettingsHandler`、`IPluginSettingsReader`、字段式设置描述、宿主自动保存与配置迁移回调。安装向导自己的字段描述和值类型仍保留，不属于设置契约。

发现和安装代码把原 `context.Settings` 改为 `context.Storage`，自行从插件目录读取配置。旧 `settings.json` 不由宿主删除或自动重写；需要延续数据时由插件自己的迁移逻辑处理。

用户界面入口统一在“插件管理”，通用“设置”页面不再包含插件设置分类。
