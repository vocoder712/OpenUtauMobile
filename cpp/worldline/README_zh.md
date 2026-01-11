# Worldline iOS 构建说明（中文）

## 🚀 快速开始 — 构建 iOS XCFramework

先决条件：
- macOS + Xcode（请用 `xcode-select -p` 确认 Xcode 路径）
- CMake（建议 >= 3.20；若使用旧版 WORLD，构建时可传 `-DCMAKE_POLICY_VERSION_MINIMUM=3.5`）
- Xcode 命令行工具（含 `xcrun`、`lipo`、`xcodebuild`）

一键构建（自动化脚本）：
```bash
cd cpp/worldline
./scripts/build_xcframework_all.sh
```
可选环境变量：
- `XCODEBUILD` — 覆盖 `xcodebuild` 路径
- `LIPO` — 覆盖 `lipo` 路径（脚本会自动使用 `xcrun -f lipo` 或 `which lipo`）

手动构建（用于调试或细粒度控制）：
```bash
cmake -S . -B build_xcode -G Xcode -DCMAKE_BUILD_TYPE=Release
./scripts/build_sim_x86.sh
./scripts/build_sim_arm64.sh
./scripts/build_device_arm64.sh
# 注意：为避免在模拟器上触发 dyld / codesign 的 "invalid page" 问题，脚本已改为**不再合并**模拟器切片为 fat dylib。
# 构建将优先使用 arm64 模式的模拟器切片（iossimulator-arm64），若不存在再回退到 x86_64。
# 打包 XCFramework，并拷贝到 runtimes/ios/
xcodebuild -create-xcframework 
  -library build_xcode/libworldline-iphoneos-arm64.dylib -headers build_xcode/include 
  -library build_xcode/libworldline-iphonesim-arm64.dylib -headers build_xcode/include 
  -output build_xcode/worldline.xcframework
cp -R build_xcode/worldline.xcframework runtimes/ios/
```

产物位置：
- `cpp/worldline/runtimes/ios/worldline.xcframework`（供 MAUI iOS 使用）

---

## 📦 依赖策略（Vendor 优先）
优先级：
1. `third_party/world/`（如果存在，优先使用）
2. 本地 `build_xcode/_deps/world-src`（若已存在）
3. FetchContent（默认会尝试 `https://github.com/mmorise/World.git`）

建议：为保证离线与可复现的构建，将 `third_party/world/` 作为 vendor 或在 CI 中发布预构建产物（Release artifact）。

---

## ⚙️ CI 建议（简短）
- 在 macOS runner 上执行：checkout → `./scripts/build_xcframework_all.sh` → 上传 `build_xcode/worldline.xcframework` 到 GitHub Releases 或作为 artifact。
- 推荐把二进制作为 release artifact 发布，避免把大量二进制直接提交到主仓库（除非你愿意把 `runtimes/` 保持在 repo）。

---

## 📌 故障排查小贴士
- 如果脚本提示找不到 `lipo`，请确保已安装 Xcode command line tools；脚本支持 `LIPO` 环境变量或使用 `xcrun -f lipo`。 
- 构建时如遇 CMake 策略错误，可尝试：
  - `cmake -S . -B build_xcode -G Xcode -DCMAKE_POLICY_VERSION_MINIMUM=3.5`（兼容旧 WORLD CMakeLists）。
- 构建输出目录 `cpp/worldline/build_xcode/` 已加入 `.gitignore`，不建议提交生成产物。

---

如果你需要，我可以把这份中文 README 同步到仓库根 README 或生成 PR 补丁供你审阅。
