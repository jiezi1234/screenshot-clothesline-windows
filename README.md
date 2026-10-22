# Screenshot Clothesline for Windows

一个正在规划中的 Windows 截图暂存工具：将截图挂在屏幕顶部，方便复制、预览、编辑、拖出和找回历史。

An independently maintained Windows implementation of a screenshot clothesline, inspired by [Tendedero](https://github.com/alejandrobujan/tendedero).

> 当前状态：需求与迁移方案已建立，尚无可运行应用或安装包。仓库名称为开发工作名，正式应用名称与图标待定。

## 已确定的方向

- 首发面向 **Windows 11 x64**。
- 参考原版 macOS Tendedero 的功能及交互，以 Windows 原生能力重新实现。
- 不参考、不复用现有 Windows 移植项目的源码或实现方案。
- 截图超出晾衣绳容量后进入本地历史，按独立的保留期限清理。
- 本地运行，无账号和遥测。

## 文档

- [需求规格](docs/REQUIREMENTS.md)：功能、文件生命周期、默认值及验收标准。
- [迁移计划](docs/MIGRATION_PLAN.md)：技术方案、模块边界和实施阶段。
- [来源与授权说明](THIRD_PARTY_NOTICES.md)：原项目基线、参考范围和品牌限制。
- [贡献说明](CONTRIBUTING.md)：开发与贡献约定。

## 技术方案（待原型验证）

C#、.NET 10 LTS、WPF、Win32 接口；SQLite 用于本地元数据。

优先验证透明浮层、跨应用鼠标穿透、混合 DPI、多屏与文件拖拽。技术原型通过后再实现截图收集和历史管理。

## 开发阶段

- [ ] 阶段 0：桌面交互原型
- [ ] 阶段 1：截图收集、复制、拖拽与历史闭环
- [ ] 阶段 2：预览、标注、批量操作与设置
- [ ] 阶段 3：录像处理与兼容性
- [ ] 阶段 4：安装、升级与正式发布

## 项目关系与许可证

本项目由独立维护者开发，与 Tendedero 作者无隶属或背书关系。Tendedero 的名称、图标和文档图片不作为本项目的品牌或素材使用。

本仓库自身代码的发布许可证待确定；公开仓库不等于已授予开源许可。当前仓库仅包含规划文档，未包含原项目源码。后续如移植或改写原项目源码，将保留适用的原作者版权声明与完整 MIT 许可文本。详情见 [来源与授权说明](THIRD_PARTY_NOTICES.md)。
