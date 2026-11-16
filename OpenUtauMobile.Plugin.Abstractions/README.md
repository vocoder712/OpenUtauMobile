# OPUM 歌手插件开发

本文面向以 OPUM 宿主 **`dev` 分支**为目标的插件开发者。当前歌手插件契约版本为 **3**，由 `SingerPluginContract.Version` 定义。

OPUM 依旧兼容跨平台的 OpenUtau 传统插件，不需要遵守本契约，但是传统插件仅能提供音素器而不能获得更加丰富的歌手能力。

## 插件能力

OPUM 加载 .NET DLL 插件。歌手插件可以：

- 定义歌手类型、显示名称、明暗主题颜色和可选渲染器。
- 发现本地声库或从服务取得歌手列表，向宿主提供 `USinger` 实例。
- 实现渲染器，将宿主的合成短句交给本地引擎或远端服务，返回音频和支持的曲线数据。
- 提供声库安装器，由宿主展示多步表单、内容预览、安装目标和进度；插件负责包格式解析与安装逻辑。
- 提供歌手删除操作，以及通过内嵌 WebView 或系统浏览器打开的设置页面。
- 使用独立的数据目录、日志和用户警告通知。

扩展范围是歌手与合成流程；本契约不提供自定义原生页面、菜单命令或编辑器扩展接口。插件安装、更新和启停在重启后生效。

## 契约接口概况

`OpenUtauMobile.Plugin.Abstractions` 提供宿主接入契约，`OpenUtau.Core` 提供歌手和渲染接口。

| 接口或类型 | 用途 |
| --- | --- |
| `SingerPluginAttribute` | 在程序集上声明插件入口和契约版本。 |
| `ISingerPlugin` | 提供插件身份、登记能力，以及可选的 Web 设置入口。 |
| `ISingerPluginRegistrar` | 同步登记歌手类型、发现提供方和安装器。 |
| `SingerTypeDescriptor` | 描述歌手类型、桥接值、渲染器、颜色和本地化名称。 |
| `USinger`（Core） | 表示可供轨道选择和合成使用的歌手。 |
| `IRenderer`、`RendererAttribute`（Core） | 实现合成并声明渲染器 ID 与歌手类型的关联。 |
| `ISingerProvider` | 发现歌手，并按需实现删除。 |
| `ISingerPackageInstaller`、`ISingerInstallSession` | 描述安装入口，驱动包分析、步骤切换、预检与提交。 |
| `PluginFieldDescriptor`、`PluginValue` | 描述安装表单的文本、开关、数值、选项和文件路径字段。 |
| `IPluginWebSettingsSession` | 提供设置页地址，管理本次网页会话的资源。 |
| `PluginStorageContext` | 提供插件数据目录和资源目录。 |
| `IPluginLog`、`IPluginNotifications` | 记录诊断，发布、更新和清除用户警告。 |

### 身份与类型

插件 ID 是持久身份，例如 `org.example.dummy`。使用小写字母开头，总长最多 160 个字符，字符集为小写字母、数字、点、下划线和短横线；结尾使用字母、数字、下划线或短横线。`opum.` 前缀及系统设备名由宿主保留。可调用 `SingerPluginContract.ValidatePluginId` 检查 ID。

类型、提供方和安装器的 ID 使用 `<PluginId>:<名称>`，例如 `org.example.dummy:voice`。完整 ID 最长 160 个字符，字符集同插件 ID，另允许冒号。显示名称可随语言变化，ID 保持稳定。

歌手类型通过 `BridgeValue` 与 Core 的 `USingerType` 对接。选择 `0x10000000` 至 `int.MaxValue` 范围内的独立整数，并在歌手实例、渲染器和类型描述中使用同一值。渲染器 ID 也应保持稳定，并与其他插件区分。

### 发现与安装

`Register` 同步提交能力描述；目录扫描、网络访问和安装工作放在对应的运行时方法中。

`ISingerProvider.DiscoverAsync` 接收歌手根目录、语言、存储、日志和取消令牌，返回 `SingerDiscoveryResult`。每个条目包含位置、名称、可用状态和可选的 `USinger`。宿主将发现结果汇入歌手列表，位置和状态也用于安装定位与删除。实现 `CanDelete` 和 `DeleteAsync` 即可提供删除能力；删除时根据 `SingerDeletionContext.Roots` 中的当前路径与写入权限处理文件。

安装时，用户先选择安装器，再按其 `Extensions` 选择文件。安装器创建会话，宿主依次调用：

1. `AnalyzeAsync`：读取包并生成首个步骤。
2. `NextAsync` / `BackAsync`：处理表单和步骤切换，以 `Revision` 标识当前状态版本。
3. `PrepareAsync`：接收最终步骤字段和目标，生成包含摘要、目标路径及歌手引用的 `PreparedInstall`。
4. `InstallAsync`：使用 `PlanId` 提交该计划，返回实际发布位置及歌手引用。
5. `DisposeAsync`：释放会话资源，宿主清理临时工作区。

字段或目标变化后重新准备计划，每个计划提交一次。`SingerInstallContext` 提供文件流、本地包路径、临时工作区、可写目标和已安装歌手查询；使用 `Files.PublishAsync` 将准备好的文件或目录发布到目标。歌手引用由提供方 ID 和歌手 ID 组成，用于安装后刷新定位。

### 设置、存储与通知

实现 `GetWebSettingsAsync` 返回设置入口标题，用户打开时由 `OpenWebSettingsAsync` 创建 `IPluginWebSettingsSession`。查询方法只返回当前入口信息，网页服务在打开会话时启动。

会话的 `Address` 为绝对 HTTP/HTTPS 地址，可指向本地或远端页面。离开设置页时，宿主调用 `DisposeAsync`；插件在此关闭监听和其他会话资源。`ReloadResourcesOnClose` 默认 `true`，关闭设置后会重新发现歌手。

发现、安装和设置上下文共享 `Storage`：

- `DataDirectory`：`DataPath/PluginData/<PluginId>`，用于配置、凭据和持久数据。
- `ResourceDirectory`：数据目录下的 `Resources`，用于插件资源。

插件自行创建所需子目录，管理配置格式和凭据存储。通过 `context.Log.Write(...)` 记录诊断；需要用户处理的问题通过 `context.Notifications.PublishWarning(new PluginWarning(code, message))` 展示，恢复后调用 `ClearWarning(code)`。相同代码更新同一条警告，宿主按插件隔离通知。

## 构建最小插件

以下示例先完成入口加载，再添加歌手能力。需要 .NET 10 SDK，以及 OPUM `dev` 分支源码。示例目录布局：

```text
工作目录/
├─ OpenUtauMobile/
└─ DummyPlugin/
   ├─ DummyPlugin.csproj
   └─ PluginEntry.cs
```

### 1. 创建项目

在 `DummyPlugin` 中创建 `DummyPlugin.csproj`：

```xml
<Project Sdk="Microsoft.NET.Sdk">
    <PropertyGroup>
        <TargetFramework>net10.0</TargetFramework>
        <ImplicitUsings>enable</ImplicitUsings>
        <Nullable>enable</Nullable>
        <OpumRoot Condition="'$(OpumRoot)' == ''">$(MSBuildThisFileDirectory)..\OpenUtauMobile</OpumRoot>
    </PropertyGroup>
    <ItemGroup>
        <ProjectReference Include="$(OpumRoot)\OpenUtauMobile.Plugin.Abstractions\OpenUtauMobile.Plugin.Abstractions.csproj"
                          Private="false" />
        <ProjectReference Include="$(OpumRoot)\OpenUtau.Core\OpenUtau.Core.csproj"
                          Private="false" />
    </ItemGroup>
</Project>
```

两份宿主程序集由 OPUM 提供，`Private="false"` 用于让插件引用宿主提供的实例。交付文件包含插件自身程序集、资源及所需的额外依赖。

### 2. 声明入口

创建 `PluginEntry.cs`：

```csharp
using OpenUtauMobile.Plugin.Abstractions;

[assembly: SingerPlugin(typeof(DummyPlugin.PluginEntry))]

namespace DummyPlugin
{
    /// <summary>最小插件入口。</summary>
    public sealed class PluginEntry : ISingerPlugin
    {
        /// <summary>插件的稳定身份与显示名称。</summary>
        public SingerPluginDescriptor Descriptor => new("org.example.dummy", "Dummy Plugin");

        /// <summary>在此登记插件提供的歌手能力。</summary>
        public void Register(ISingerPluginRegistrar registrar)
        {
        }
    }
}
```

入口为公开类，具有公开无参构造函数。`SingerPluginAttribute` 默认携带编译时的 `SingerPluginContract.Version`。此例完成插件身份和入口声明，`Register` 留作后续能力登记。

### 3. 构建与安装

在 `DummyPlugin` 目录运行：

```powershell
dotnet build DummyPlugin.csproj -c Release
```

产物为 `bin/Release/net10.0/DummyPlugin.dll`。宿主源码位于其他目录时，可在构建命令中传入 `-p:OpumRoot="宿主源码绝对路径"`。

在 OPUM 的“插件管理”中安装该 DLL，重启后查看插件详情中的身份和加载状态。也可将 DLL 放入 `DataPath/Plugins`，在插件管理中启用后重启。更新时先卸载旧 DLL 并重启，再安装新 DLL。

### 4. 接入可合成歌手

在最小入口上依次添加：

1. 实现 Core 的 `USinger`，提供歌手身份、显示信息、资源加载和类型桥接值。
2. 在入口所在程序集内实现公开、具有公开无参构造函数的 `IRenderer`，用 `[Renderer("org.example.dummy:render", (USingerType)0x10010001)]` 声明关联。`IRenderer.SingerType` 使用同一桥接值。
3. 在 `Register` 中调用 `RegisterSingerType`，将该渲染器 ID 填入 `RendererIds` 和 `DefaultRendererId`。多个渲染器时，默认值取 `Renderers.GetDefaultRenderer(bridgeValue)`，集合取 `Renderers.GetSupportedRenderers(bridgeValue)`。
4. 实现 `ISingerProvider`，以 `org.example.dummy:provider` 为 ID，`SingerTypeIds` 引用上述类型 ID；在 `DiscoverAsync` 中返回带有歌手实例的发现条目，再用 `RegisterProvider` 登记。
5. 按需实现安装器或 Web 设置，并重新构建、安装和重启。

单渲染器类型的登记示例（放入 `Register` 方法）：

```csharp
registrar.RegisterSingerType(new SingerTypeDescriptor(
    Id: "org.example.dummy:voice",
    BridgeValue: (OpenUtau.Core.Ustx.USingerType)0x10010001,
    DisplayName: "Example Voice",
    RendererIds: ["org.example.dummy:render"],
    DefaultRendererId: "org.example.dummy:render"));
```

完成渲染器和发现提供方后，刷新歌手列表，在轨道中选择歌手，输入音符和歌词，验证播放与音频导出。
