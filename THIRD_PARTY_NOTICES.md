# 来源与授权说明

## Tendedero

- 原作者：Alejandro Buján
- 原仓库：https://github.com/alejandrobujan/tendedero
- 功能参考基线：`32a3e47187fdea7b2fd43b3ab5a913f0ed33724a`
- 许可证：https://github.com/alejandrobujan/tendedero/blob/32a3e47187fdea7b2fd43b3ab5a913f0ed33724a/LICENSE

本项目参考其 macOS 应用的截图晾衣绳概念及相关交互，独立实现 Windows 版本。阶段 0–1 的 C# 实现、托盘图形与测试图片在本仓库内新建，未复制原项目源码或美术素材。

原项目源码使用 MIT 许可；其名称、图标（包括生成该图标的图稿）和 docs 中的图片不在该授权范围内。Windows 应用将使用独立名称、独立图标与自制宣传素材，并不表示获得原作者背书。

后续如复制、移植或改写原版源码，必须记录相关文件及来源版本，保留原作者版权声明，并在源码及分发包中提供适用的完整 MIT 许可文本。修改后的源码不得被描述为全部原创。

## 其他 Windows 实现

已有 Windows 移植项目不作为本仓库的实现参考，不引入其源码、资源或方案。

## 本仓库与依赖

本仓库自身代码的发布许可证尚未确定。正式发布前必须明确许可，并补齐所有引入依赖的许可证及通知；本文件不替代第三方要求保留的原始许可文本。

应用使用 .NET 10、WPF、Windows Forms 的托盘组件及 Windows 系统接口。自包含便携包附带 Microsoft.NETCore.App 与 Microsoft.WindowsDesktop.App 运行时；`scripts/publish.ps1` 从实际使用的运行时包复制完整许可证与通知到包内 `licenses/`。SDK 不随应用分发。

阶段 1 新增以下依赖，用于本地记录、设置及文件操作日志：

| 依赖 | 版本 | 许可 |
|---|---|---|
| Microsoft.Data.Sqlite / Microsoft.Data.Sqlite.Core | 10.0.13 | MIT |
| SQLitePCLRaw.bundle_e_sqlite3 / core / lib.e_sqlite3 / provider.e_sqlite3 | 2.1.13 | Apache-2.0；包含的原生 SQLite 使用公共领域声明 |

完整文本及来源说明保存在 [licenses/](licenses/README.md)，分发包同时包含实际还原的 NuGet 包元数据。Microsoft.Data.Sqlite 许可来自对应的上游 v10.0.13 标签。SQLitePCLRaw 2.1.13 包声明 Apache-2.0，但包内未包含独立许可/通知文件，上游也无对应标签，因此保留其前一 v2.1.12 标签的完整许可和通知，并如实记录来源；其中对 SQLCipher、OpenSSL 等可选变体的说明不表示本应用使用了这些变体。未修改这些第三方源码。

## 阶段 3：Windows 系统媒体接口

v0.6.0 通过 Windows.Media.Editing 生成缩略图，并用 WPF MediaElement 播放。Windows 自身媒体组件提供编解码能力，应用不另外分发编码器。

- Microsoft.Windows.SDK.NET.Ref 10.0.19041.57 是 SDK 隐式引用的投影包；分发其中 Microsoft.Windows.SDK.NET.dll 与 WinRT.Runtime.dll。SDK 许可原文按包元数据指向的 [Windows SDK 许可](https://aka.ms/WinSDKLicenseURL) 获取，保存在 licenses/Windows-SDK-LICENSE.rtf；包元数据随便携包附带。
- WinRT.Runtime.dll 文件版本为 2.2.0.48161，产品版本对应 microsoft/CsWinRT 提交 8649ee3eeb2445ca2a36d80d878ef60b96a6c65d；该提交的完整 MIT 许可保存为 licenses/CsWinRT-LICENSE.txt。来源：[许可文件](https://github.com/microsoft/CsWinRT/blob/8649ee3eeb2445ca2a36d80d878ef60b96a6c65d/LICENSE)。

目标框架中的 Windows 10 API 版本用于接口绑定，不改变首发 Windows 11 x64 的产品范围。打包脚本额外复制隐式 SDK 投影包的实际版本元数据，避免仅枚举普通 NuGet 依赖时漏项。

## v0.8 图片格式检测样本

Fixtures 中的 WebP/HEIC/PNG 均为本项目生成的色块，不含用户照片、原项目素材或第三方样例图片。生成工具仅用于开发，版本和重建方式见 src/Clothesline.App/Fixtures/README.md；应用不随包分发 Pillow、libwebp、libheif、x265 或 Python。产品通过现有 Windows WIC 组件解码，是否可用取决于机器已安装的组件。本批未新增应用运行时依赖。

## v0.9 语言与桌面兼容性

原版提交 `32a3e47187fdea7b2fd43b3ab5a913f0ed33724a` 的语言目录仅用于确认覆盖范围。本批翻译依据本项目界面独立编写，未复制上游翻译或现有 Windows 移植代码；仍需母语复核。解码进程、无障碍和多桌面处理使用现有 .NET/WPF 与 Windows 公共接口，没有新增运行时第三方依赖。

## v0.16 剪贴板文字卡片

文字收集、缩略图、滚动预览、应用内编辑与保存恢复按用户需求独立实现，使用已有 .NET、WPF 与 SQLite 依赖；测试文字在本仓库生成。新增界面译文独立编写，未引入其他 Windows 移植源码、素材或运行时依赖。
