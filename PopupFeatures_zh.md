# 弹窗（Popup）高级特性文档

[English](PopupFeatures.md) | [简体中文](PopupFeatures_zh.md)

> 本文档基于 `OpenUtauMobile` 项目的源码以及项目所使用的 [`CommunityToolkit.Maui`](https://github.com/CommunityToolkit/Maui) 库（v11.2.0）整理而成。所有代码示例均来自 `OpenUtauMobile/Views/Controls/` 目录下的实际弹窗文件。

---

## 1. 框架概览

本项目所有弹窗均基于 **`CommunityToolkit.Maui.Views.Popup`**（命名空间 `CommunityToolkit.Maui.Views`），而非 Avalonia 的 `Window`。Toolkit 在 `MauiProgram.cs` 中注册：

```csharp
builder.UseMauiCommunityToolkit();
```

弹窗通过 `Page`（或 `ContentPage`）上的 `ShowPopupAsync` / `ShowPopup` 扩展方法调起：

```csharp
// 可等待 —— 阻塞直到弹窗关闭并返回结果
object? result = await this.ShowPopupAsync(new RenamePopup(oldName, title));

// 非等待 —— 显示后立即返回，不等待关闭
this.ShowPopup(new LoadingPopup(showProgressBar: true));
```

---

## 2. Popup 核心属性

### 2.1 `Color` — 遮罩背景色

`Color` 控制弹窗内容后方、页面内容前方渲染的**遮罩层（scrim）**颜色。

| 值 | 效果 |
|---|---|
| `Color="Transparent"` | 无可见遮罩，页面内容完全透过 |
| `Color="#80000000"` | 50% 黑色半透明遮罩（标准模态变暗） |
| `Color="Black"` | 完全不透明的黑色遮罩 |

项目中所有弹窗均设置 `Color="Transparent"`，使页面内容在弹窗背后可见：

```xml
<toolkit:Popup ...
    Color="Transparent"
    x:Class="OpenUtauMobile.Views.Controls.RenamePopup">
```

### 2.2 `HorizontalOptions` / `VerticalOptions` — 定位

这两个布局属性决定弹窗在屏幕上的锚定位置。

| 值 | 含义 |
|---|---|
| `Center`（默认） | 水平/垂直居中 |
| `Start` | 靠左/靠上对齐 |
| `End` | 靠右/靠下对齐 |
| `Fill` | 拉伸填满可用宽/高 |

`ErrorPopup.xaml` 显式指定了居中定位：

```xml
<toolkit:Popup ...
    HorizontalOptions="Center"
    VerticalOptions="Center">
```

项目中大多数弹窗省略了这两个属性，因此默认居中显示。

### 2.3 `Size` — 显式尺寸

可在代码后台通过 `Size` 属性指定固定像素尺寸：

```csharp
// 在 Popup 构造函数中
Size = new Size(400, 300);
```

也可以在内层 `Border` 上设置 `WidthRequest` / `HeightRequest` 来控制内容尺寸，而不设置 `Size`——这是本项目的通用做法：

```xml
<Border WidthRequest="400" ...>
```

### 2.4 `CanBeDismissedByTappingOutsideOfPopup` — 点击外部是否关闭

| 值 | 行为 |
|---|---|
| `True`（默认） | 点击弹窗内容外部时关闭弹窗 |
| `False` | 弹窗只能通过代码关闭 |

`LoadingPopup` 和 `ErrorPopup` 设置为 `False`，因为它们代表阻塞性操作：

```xml
<toolkit:Popup ...
    CanBeDismissedByTappingOutsideOfPopup="False">
```

### 2.5 `Anchor` — 相对/上下文定位

`CommunityToolkit.Maui.Views.Popup` 提供 `Anchor` 属性，接受一个 `View`。设置后，弹窗将相对于该控件定位，而非屏幕居中——适用于右键菜单或工具提示等场景。

```csharp
// 将弹窗锚定在按钮下方
var popup = new MyContextMenuPopup();
popup.Anchor = myButton;
await this.ShowPopupAsync(popup);
```

项目中目前没有弹窗使用 `Anchor`，但 Toolkit 完全支持该属性。

### 2.6 `CloseAsync` / 返回值 — 向调用方传递结果

弹窗通过 `CloseAsync` 将结果传递回调用页面：

```csharp
// 在 Popup 的代码后台
private void OnConfirmClicked(object sender, EventArgs e)
{
    CloseAsync(EntryName.Text);   // 传递结果值
}

private void OnCancelClicked(object sender, EventArgs e)
{
    CloseAsync(null);             // 传递 null 表示取消
}
```

---

## 3. 半透明弹窗

### 3.1 透明遮罩 + 不透明内容

最常见的模式：清除 `Color` 遮罩，给内容设置不透明背景。

```xml
<toolkit:Popup Color="Transparent">
    <Border Background="White" StrokeShape="RoundRectangle 10">
        <!-- 内容 -->
    </Border>
</toolkit:Popup>
```

项目中全部 16 个弹窗均采用此模式。

### 3.2 半透明内容

在 `Background` 颜色中使用 Alpha 通道（ARGB 十六进制：`#AARRGGBB`）：

```xml
<toolkit:Popup Color="Transparent">
    <Border Background="#CC444444">   <!-- 80% 不透明度的深灰色 -->
        <!-- 内容 -->
    </Border>
</toolkit:Popup>
```

或在任意元素上设置 `Opacity`：

```xml
<Border Background="White" Opacity="0.85">
```

### 3.3 半透明遮罩 + 不透明内容

```xml
<toolkit:Popup Color="#80000000">  <!-- 50% 黑色遮罩 -->
    <Border Background="White" StrokeShape="RoundRectangle 10">
        <!-- 内容 -->
    </Border>
</toolkit:Popup>
```

### 3.4 背景模糊

.NET MAUI 没有内置的模糊效果。需要通过平台特定代码实现（iOS 的 `UIBlurEffect`、Android 12+ 的 `RenderEffect Blur`），并封装为 `PlatformEffect` 或 `MauiContext` 自定义，应用于弹窗下方的页面内容。

---

## 4. 非矩形 / 不规则形状弹窗

`Popup` 控件本身始终是矩形的。不规则形状通过对弹窗**内容**进行样式定制来实现：

### 4.1 圆角 — `StrokeShape="RoundRectangle N"`

项目所有弹窗均使用带 `RoundRectangle` 描边形状的 `Border`：

```xml
<Border StrokeThickness="0"
        Background="{DynamicResource PopupBackground}"
        WidthRequest="400"
        StrokeShape="RoundRectangle 10">
    <!-- 内容 -->
</Border>
```

`Popup` 上的 `Color="Transparent"` 确保 `Border` 的圆角真正可见（遮罩层不会以纯色覆盖圆角区域）。

### 4.2 椭圆 / 圆形

```xml
<toolkit:Popup Color="Transparent">
    <Border StrokeShape="Ellipse"
            WidthRequest="200" HeightRequest="200"
            Background="LightBlue">
        <!-- 圆形内容 -->
    </Border>
</toolkit:Popup>
```

`ChooseSingerPopup.xaml` 中的歌手头像元素已经演示了 `StrokeShape="Ellipse"` 的用法。

### 4.3 自定义路径形状 — `Clip`

对于完全任意的形状，可在内容上应用几何裁剪：

```xml
<toolkit:Popup Color="Transparent">
    <ContentView WidthRequest="300" HeightRequest="200">
        <ContentView.Clip>
            <PathGeometry Figures="M 0,50 L 50,0 L 250,0 L 300,50 L 250,100 L 50,100 Z" />
        </ContentView.Clip>
        <Border Background="White" />
    </ContentView>
</toolkit:Popup>
```

注意：裁剪使角落透明，但在某些平台上，触摸事件仍可能被矩形命中测试区域捕获。

### 4.4 使用 SkiaSharp 绘制复杂形状

项目已依赖 `SkiaSharp.Views.Maui.Controls`。在弹窗内放置 `SKCanvasView` 可以渲染任意形状（星形、气泡对话框等），同时保持周围区域透明。

---

## 5. 可拖拽弹窗

`CommunityToolkit.Maui.Views.Popup` **不提供**内置的拖拽 API。拖拽需要使用 MAUI 手势识别器手动实现。

### 实现方案

```xml
<toolkit:Popup x:Name="ThisPopup" Color="Transparent">
    <Border x:Name="PopupContent"
            Background="White"
            StrokeShape="RoundRectangle 10"
            WidthRequest="300">
        <!-- 拖拽手柄 -->
        <Grid RowDefinitions="40,*">
            <BoxView Grid.Row="0" BackgroundColor="#66CCFF">
                <BoxView.GestureRecognizers>
                    <PanGestureRecognizer PanUpdated="OnDragHandlePanUpdated" />
                </BoxView.GestureRecognizers>
            </BoxView>
            <!-- 其余内容 -->
        </Grid>
    </Border>
</toolkit:Popup>
```

```csharp
private double _dragOffsetX, _dragOffsetY;

private void OnDragHandlePanUpdated(object sender, PanUpdatedEventArgs e)
{
    switch (e.StatusType)
    {
        case GestureStatus.Running:
            // 移动内容视图
            PopupContent.TranslationX = _dragOffsetX + e.TotalX;
            PopupContent.TranslationY = _dragOffsetY + e.TotalY;
            break;
        case GestureStatus.Completed:
            _dragOffsetX = PopupContent.TranslationX;
            _dragOffsetY = PopupContent.TranslationY;
            break;
    }
}
```

**局限性：** 由于 `TranslationX/Y` 是应用在内容视图而非弹窗本身上的，弹窗宿主区域仍然保持在原始位置。在某些平台上，内容视觉边界之外但原始弹窗矩形区域之内的触摸事件仍可能被捕获。

---

## 6. 可调整大小的弹窗

`CommunityToolkit.Maui.Views.Popup` **不提供**内置的缩放手柄。调整大小需要将自定义手势识别器附加到角部或边缘元素上。

### 实现方案

```xml
<toolkit:Popup Color="Transparent">
    <!-- 手动控制尺寸的外层容器 -->
    <Border x:Name="ResizableContent"
            Background="White"
            StrokeShape="RoundRectangle 10"
            WidthRequest="300" HeightRequest="200">
        <Grid>
            <!-- 主要内容 -->

            <!-- 右下角缩放手柄 -->
            <BoxView HorizontalOptions="End"
                     VerticalOptions="End"
                     WidthRequest="20" HeightRequest="20"
                     BackgroundColor="#66CCFF">
                <BoxView.GestureRecognizers>
                    <PanGestureRecognizer PanUpdated="OnResizeHandlePanUpdated" />
                </BoxView.GestureRecognizers>
            </BoxView>
        </Grid>
    </Border>
</toolkit:Popup>
```

```csharp
private double _baseWidth = 300, _baseHeight = 200;

private void OnResizeHandlePanUpdated(object sender, PanUpdatedEventArgs e)
{
    if (e.StatusType == GestureStatus.Running)
    {
        ResizableContent.WidthRequest  = Math.Max(200, _baseWidth  + e.TotalX);
        ResizableContent.HeightRequest = Math.Max(100, _baseHeight + e.TotalY);
    }
    else if (e.StatusType == GestureStatus.Completed)
    {
        _baseWidth  = ResizableContent.WidthRequest;
        _baseHeight = ResizableContent.HeightRequest;
    }
}
```

---

## 7. 模态 vs 非模态

所有 `CommunityToolkit.Maui.Views.Popup` 实例默认均为**模态**——弹窗打开期间会阻止与底层页面的交互。

`Popup` 不直接支持非模态的悬浮面板。在 .NET MAUI 中的替代方案：
- 在当前页面使用 `AbsoluteLayout` 覆盖层并手动管理其可见性和位置。
- 使用带透明 `ContentPage` 的 `Shell` 模态。
- 使用平台原生的悬浮窗口（需要 `DependencyService` 或平台特定的 Handler）。

---

## 8. 功能支持总结

| 功能 | 是否支持 | 实现方式 |
|---|---|---|
| 居中定位 | ✅ 框架内置 | 默认行为 |
| 自定义位置（水平/垂直选项） | ✅ 框架内置 | `HorizontalOptions` / `VerticalOptions` |
| 锚定到指定控件 | ✅ 框架内置 | `Popup.Anchor` 属性 |
| 透明遮罩 | ✅ 框架内置 | `Color="Transparent"` |
| 半透明遮罩 | ✅ 框架内置 | `Color="#80000000"` 等 |
| 不透明内容 | ✅ 框架内置 | 在内层 `Border` 上设置 `Background` |
| 半透明内容 | ✅ 框架内置 | Alpha 颜色或内容上的 `Opacity` |
| 背景模糊 | ⚠️ 需平台代码 | iOS `UIBlurEffect`，Android `RenderEffect` |
| 圆角 | ✅ 框架内置（通过内容） | `Border` 上的 `StrokeShape="RoundRectangle N"` |
| 椭圆/圆形 | ✅ 框架内置（通过内容） | `Border` 上的 `StrokeShape="Ellipse"` |
| 自定义路径形状 | ✅ 支持（通过内容） | `ContentView.Clip` + `PathGeometry` |
| SkiaSharp 绘制形状 | ✅ 支持 | 弹窗内使用 `SKCanvasView` |
| 点击外部关闭 | ✅ 框架内置 | `CanBeDismissedByTappingOutsideOfPopup` |
| 禁止点击外部关闭 | ✅ 框架内置 | `CanBeDismissedByTappingOutsideOfPopup="False"` |
| 向调用方返回值 | ✅ 框架内置 | `CloseAsync(result)` |
| 可拖拽弹窗 | ⚠️ 需自定义实现 | `PanGestureRecognizer` + `TranslationX/Y` |
| 可调整大小弹窗 | ⚠️ 需自定义实现 | `PanGestureRecognizer` + `WidthRequest/HeightRequest` |
| 非模态悬浮面板 | ❌ Popup 不支持 | 使用 `AbsoluteLayout` 覆盖层或平台代码 |
| 打开/关闭动画 | ⚠️ 有限支持 | 在 `OnAppearing`/`OnDisappearing` 中使用 `Animation` |

---

## 9. 演示弹窗

`OpenUtauMobile/Views/Controls/DemoAdvancedPopup.xaml` 在一个弹窗中演示了所有框架直接支持的高级特性：

- `Color="Transparent"` — 无遮罩背景
- `HorizontalOptions="End" VerticalOptions="Start"` — 定位在右上角
- `StrokeShape="RoundRectangle 16"` — 圆角
- `Background="#CC1A1A2E"` — 半透明深色内容背景
- `CanBeDismissedByTappingOutsideOfPopup="True"` — 点击外部可关闭
- 标题栏上的 `PanGestureRecognizer` — 拖拽以重新定位
