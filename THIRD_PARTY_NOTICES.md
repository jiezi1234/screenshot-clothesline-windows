# 来源与授权说明

## Tendedero

- 原作者：Alejandro Buján
- 原仓库：https://github.com/alejandrobujan/tendedero
- 功能参考基线：`32a3e47187fdea7b2fd43b3ab5a913f0ed33724a`
- 许可证：https://github.com/alejandrobujan/tendedero/blob/32a3e47187fdea7b2fd43b3ab5a913f0ed33724a/LICENSE

本项目参考其 macOS 应用的截图晾衣绳概念及相关交互，独立实现 Windows 版本。阶段 0 的 C# 实现、托盘图形与三张示例图片在本仓库内新建，未复制原项目源码或美术素材。

原项目源码使用 MIT 许可；其名称、图标（包括生成该图标的图稿）和 docs 中的图片不在该授权范围内。Windows 应用将使用独立名称、独立图标与自制宣传素材，并不表示获得原作者背书。

后续如复制、移植或改写原版源码，必须记录相关文件及来源版本，保留原作者版权声明，并在源码及分发包中提供适用的完整 MIT 许可文本。修改后的源码不得被描述为全部原创。

## 其他 Windows 实现

已有 Windows 移植项目不作为本仓库的实现参考，不引入其源码、资源或方案。

## 本仓库与依赖

本仓库自身代码的发布许可证尚未确定。正式发布前必须明确许可，并补齐所有引入依赖的许可证及通知；本文件不替代第三方要求保留的原始许可文本。

阶段 0 使用 .NET 10、WPF、Windows Forms 的托盘组件及 Windows 系统接口，没有引入额外应用 NuGet 依赖。自包含便携包会附带 Microsoft.NETCore.App 与 Microsoft.WindowsDesktop.App 运行时；`scripts/publish.ps1` 从实际使用的运行时包复制完整许可证与通知到包内 `licenses/`。SDK 不随应用分发。
