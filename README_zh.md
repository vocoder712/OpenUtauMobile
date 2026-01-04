# OpenUtau Mobile
[English](README.md) | [简体中文](README_zh.md)
## 特别感谢

- [OpenUtau](https://github.com/stakira/OpenUtau)

## OpenUtau Mobile 是什么？

OpenUtau Mobile 是一个面向移动端的开源免费歌声合成软件。

一个基于 [OpenUtau内核](https://github.com/stakira/OpenUtau/tree/master/OpenUtau.Core) ，并进行了一些修补的编辑器。完全支持 OpenUtau 的 USTX 工程文件。

## 兼容性

### 运行平台

- Android5及以上

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

- 贡献代码：克隆本项目到本地后，使用 Visual Studio 打开项目根目录 `OpenUtauMobile.sln` 即可进入开发环境。建议新建分支操作。可以参考[开发计划与已知BUG](#开发计划与已知BUG)中的任务列表实现功能或修复BUG。完成后可以发起 Pull Request。

## 开源协议

本项目采用 [Apache 2.0](./LICENSE.txt) 许可证开源。

不是官方 OpenUtau ，不得冒充官方 OpenUtau 。
