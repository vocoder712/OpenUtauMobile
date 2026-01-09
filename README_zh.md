# OpenUtau Mobile
[English](README.md) | [简体中文](README_zh.md)
## 特别感谢

- [OpenUtau](https://github.com/stakira/OpenUtau)

## OpenUtau Mobile 是什么？

OpenUtau Mobile 是一个面向移动端的开源免费歌声合成软件。

一个基于 [OpenUtau内核](https://github.com/stakira/OpenUtau/tree/master/OpenUtau.Core) ，并进行了一些修补的编辑器。完全支持 OpenUtau 的 USTX 工程文件。

## 兼容性

### 运行平台

- Android 5.0 及以上
- iOS 15.0 及以上（需自行构建）

### 歌手类型

- DiffSinger
- UTAU
- Vogen

其他未测试的类型不能保证正常工作

## 快速开始

1. 在本项目的[release](https://github.com/vocoder712/OpenUtauMobile/releases)下载对应平台和架构的安装包并安装
2. 下载一个声库。通常你可以在[DiffSinger自制声库分享页面](https://docs.qq.com/sheet/DQXNDY0pPaEpOc3JN?tab=BB08J2)或[UTAU wiki](https://utau.fandom.com/)找到下载地址。声库通常以zip格式打包。
3. 打开软件 → 点击首页的 `歌手` 按钮 → 点击右下角 `+` → 选择上一步下载的声库安装包，然后按照指引安装。
4. 返回首页，点击 `新建` 进入主界面，开始你的创作吧！

你也可以在首页的 `打开` 直接找到并打开 OpenUtau 的 USTX 工程文件。

软件现阶段很不稳定，特别是受限于开发框架，容易出现内存管理问题。因此记得随时点点保存。如果崩溃，可以在工程文件同目录找到以 `.autosave.ustx` 结尾的文件恢复。

## 软件界面简介

详见[编辑界面简介](./UIIntroductions_zh.md)

## 自行构建与贡献

如果你想让这个项目变得更好：

- 如果你发现了BUG或者有想实现的功能或者对操作逻辑UI有好的建议，可以先在[开发计划与已知BUG](#开发计划与已知BUG)看看有没有，如果没有，欢迎在[议题](https://github.com/vocoder712/OpenUtauMobile/issues)提出BUG，[讨论](https://github.com/vocoder712/OpenUtauMobile/discussion)提建议。

- 贡献代码：克隆本项目到本地后，使用 Visual Studio 打开项目根目录 `OpenUtauMobile.sln` 即可进入开发环境。建议新建分支操作。完成后可以发起 Pull Request。

## iOS 构建指南

由于 Apple 的政策限制，iOS 版本无法直接发布安装包，需要自行在 macOS 上构建。

### 环境要求

- macOS（必须）
- .NET 9.0 SDK
- Xcode 15 或更高版本
- Apple 开发者账号（免费账号即可用于个人设备）
- MAUI 工作负载

### 安装开发环境

```bash
# 安装 .NET 9.0 SDK（如果尚未安装）
brew install dotnet-sdk

# 安装 MAUI iOS 工作负载（仅构建 iOS）
dotnet workload install maui-ios

# 或者安装完整的 MAUI 工作负载（同时支持 Android 和 iOS）
# dotnet workload install maui
```

### 构建步骤

1. **克隆项目**
   ```bash
   git clone https://github.com/vocoder712/OpenUtauMobile.git
   cd OpenUtauMobile
   ```

2. **还原依赖**
   ```bash
   # 指定仅还原 iOS 平台，避免自动下载 Android workload
   dotnet restore -p:TargetFramework=net9.0-ios
   ```

3. **构建 IPA 安装包**
   ```bash
   dotnet publish OpenUtauMobile/OpenUtauMobile.csproj \
       -f net9.0-ios \
       -c Debug \
       -p:ArchiveOnBuild=true \
       -p:RuntimeIdentifier=ios-arm64 \
       -p:TargetFrameworks=net9.0-ios
   ```
   
   构建完成后，IPA 文件位于：
   `OpenUtauMobile/bin/Debug/net9.0-ios/ios-arm64/publish/OpenUtauMobile.ipa`

### 安装到 iPhone

1. **使用 USB 连接 iPhone 到 Mac**

2. **查看设备 ID**
   ```bash
   xcrun devicectl list devices
   ```

3. **安装应用**
   ```bash
   xcrun devicectl device install app \
       --device <你的设备ID> \
       OpenUtauMobile/bin/Debug/net9.0-ios/ios-arm64/publish/OpenUtauMobile.ipa
   ```

4. **信任开发者证书**
   
   首次安装后，在 iPhone 上前往：
   **设置 → 通用 → VPN与设备管理**，找到开发者证书并点击信任。

### 常见问题

**Q: 构建时间很长怎么办？**

A: Debug 模式构建通常需要 2-5 分钟。如果构建 Release 版本（启用 AOT），可能需要 20-40 分钟。建议日常开发使用 Debug 模式。

**Q: 证书过期了怎么办？**

A: 免费开发者证书有效期为 7 天，过期后需要重新构建安装。付费开发者账号（$99/年）证书有效期为 1 年。

**Q: 可以不用 Mac 构建吗？**

A: 不可以。Apple 要求 iOS 应用必须在 macOS 上使用 Xcode 工具链构建。

**Q: 如果我也想构建 Android 版本怎么办？**

A: 安装完整的 MAUI workload：
```bash
dotnet workload install maui
```

然后在构建命令中移除 `-p:TargetFrameworks=net9.0-ios` 参数，或改为 Android 的目标框架。

## 开源协议

本项目采用 [Apache 2.0](./LICENSE.txt) 许可证开源。

不是官方 OpenUtau ，不得冒充官方 OpenUtau 。
