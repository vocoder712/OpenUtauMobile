# Worldline 源码适配

构建入口、参数与增量规则见[原生构建](../README.md)。本目录保存 Worldline 的 Bazel 入口、
固定依赖、上游修订号及构建适配器，源码来自 `../upstream_cpp`。
旧 BUILD 只在工作副本中补充 Bazel 9 的规则加载，冻结源码保持不变。

桌面与 Android 使用动态库，iOS 与 Browser 使用包含传递依赖对象的静态归档。
iOS 模块初始化器将 `DllImport("worldline")` 解析到主程序，SDK 符号项保留对应入口。
Browser 使用 `worldline.a` 和 `NativeFileReference` 生成静态 P/Invoke 表。
`TrackWorldlineWasmTrampolines` 补充 SDK 中 `runtime.c` 对跳板表的增量依赖。

依赖版本、归档完整性与上游补丁在 `MODULE.bazel` 中声明；更新依赖时同步审查锁文件及适配器。
