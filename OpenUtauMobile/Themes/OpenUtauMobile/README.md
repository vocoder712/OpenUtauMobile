# 主题样式归属

`OpenUtauMobileTheme.axaml` 只加载全局资源、Avalonia 内置控件覆盖和已有跨功能复用的 OPUM 组件样式。`App.axaml` 中的 `FluentTheme` 继续提供基础主题。

## 目录职责

- `Resources/Color/`：原有颜色资源字典。保留明暗主题字典、资源键和加载关系；`TonalPaletteSlots.axaml` 仍是未加载的占位字典。
- `Styles/Controls/`：Button、TextBox、ComboBox、Slider、TabItem、ToggleSwitch、ProgressBar 的全局样式覆盖，不转换为完整 ControlTheme。
- `Styles/Components/`：Card、TopBar、FAB、Dialog、DawKnob、Icon，以及选项页和关于页共用的 OptionEntry。
- `Runtime/Generation/`：主题生成器、配色模型和轨道调色板。
- `Runtime/Resources/`：资源桥接和资源查询。
- `Runtime/Platform/`：系统强调色获取。
- `Runtime/`：主协调器 `ThemeManagerV2`、运行时事件和 `ThemeStaticTokens`。本次只移动文件，保留原命名空间和实现。

## 局部样式

- 页面样式位于 `Views/Styles/`，由对应页面的 `UserControl.Styles` 加载，包括 HomeActionButton。
- 业务控件和弹窗样式位于 `Controls/Styles/`，由对应控件加载。运行时生成的按钮仍在所属控件的样式作用域内。
- `Controls/Styles/EditModeSwitcher.axaml` 由钢琴卷帘、轨道、音素面板三个模式切换控件显式加载；这是编辑器内部复用，不提升为全局组件。
- `Styles/Components/Dialog.axaml` 统一持有 DialogShell、DialogActionRow、共享操作按钮及旧操作类的兼容样式。业务弹窗只持有业务内容样式。

## 本次兼容处理

- SettingsView 的 AccentBtn、PlaceholderText 原先也影响 ThemeColorPickerDialog；弹窗局部保留其使用的原始定义，不再依赖设置页的全局加载。
- SingerDetailView 和 EditorMorePopup 的 ActionBtn 曾通过全局样式叠加。各自局部样式保留迁移前叠加得到的属性和悬停效果，避免目录调整引入外观变化。
- 不删除缺少静态调用证据的选择器，不调整现有尺寸、间距、圆角、令牌或 Fluent 模板。

新增样式优先放在实际所有者附近；只有确认存在跨功能复用时才纳入全局组件。
