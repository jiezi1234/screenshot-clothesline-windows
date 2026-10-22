# Screenshot Clothesline for Windows

一个正在开发中的 Windows 截图暂存工具：将截图挂在屏幕顶部，方便复制、预览、编辑、拖出和找回历史。

An independently maintained Windows implementation of a screenshot clothesline, inspired by [Tendedero](https://github.com/alejandrobujan/tendedero).

> 当前状态：**阶段 0 桌面原型可运行，完整桌面验收仍在进行。** 当前使用程序生成的示例图片，尚未收集真实截图，也未实现历史管理。仓库名称为开发工作名，正式应用名称与图标待定。

![阶段 0 示例晾衣绳](docs/images/stage0-shelf.png)

## 运行阶段 0

面向 Windows 11 x64。便携包解压后运行 `Clothesline.Prototype.exe`，无需安装 .NET。这是未签名的开发原型，尚未提供正式安装器。

- 鼠标在当前显示器顶部中央停留约 350 ms，或按 **Ctrl + Alt + T** 显示/收起。
- 单击卡片复制原图；双击用默认应用打开；拖出真实示例文件；右键可取下或移入回收站。
- 取下只移除卡片。示例保存在 `%LOCALAPPDATA%\ScreenshotClothesline\Prototype\Samples`。
- “恢复示例”和重新启动会生成新示例，不覆盖旧文件，也不自动清理它们。
- 关闭控制面板后应用留在托盘；使用托盘菜单或面板“退出原型”结束应用。快捷键被占用时可使用托盘。
- 同一会话内再次启动会唤起已有控制面板。

原型不保存设置和卡片状态；不监听剪贴板或截图目录。全屏判断目前是窗口几何启发式，实际游戏/视频兼容性与完整拖放矩阵尚待验收，详见 [验证报告](docs/STAGE0_VALIDATION.md)。

## 构建与验证

安装 .NET 10 SDK（x64），在仓库根目录执行：

```powershell
./scripts/build.ps1
./scripts/test.ps1
./scripts/run.ps1
./scripts/publish.ps1
```

所有脚本支持 `-Dotnet '完整路径\dotnet.exe'`。便携包输出到 `dist/Clothesline-Prototype-win-x64.zip`。

有交互式 Windows 桌面时运行 `./scripts/test.ps1 -Desktop`：会短暂显示本程序专用的测试窗口，输出跨进程窗口检查、测量和自身界面渲染到 `artifacts/validation/`。该命令不会采集屏幕图像或修改剪贴板。

GitHub Actions 尚未启用；[CI 模板](docs/ci/build.yml.example) 已准备好，将执行构建、核心规则检查与打包。当前推送凭据没有 `workflow` 权限，因此模板暂放在文档目录，待具备相应权限后再安装到 `.github/workflows/build.yml`。

## 已确定的方向

- 首发面向 **Windows 11 x64**。
- 参考原版 macOS Tendedero 的功能及交互，以 Windows 原生能力重新实现。
- 不参考、不复用现有 Windows 移植项目的源码或实现方案。
- 截图超出晾衣绳容量后进入本地历史，按独立的保留期限清理。
- 本地运行，无账号和遥测。

## 文档

- [需求规格](docs/REQUIREMENTS.md)：功能、文件生命周期、默认值及验收标准。
- [迁移计划](docs/MIGRATION_PLAN.md)：技术方案、模块边界和实施阶段。
- [阶段 0 验证报告](docs/STAGE0_VALIDATION.md)：已验证行为、性能采样与待人工验收项。
- [来源与授权说明](THIRD_PARTY_NOTICES.md)：原项目基线、参考范围和品牌限制。
- [贡献说明](CONTRIBUTING.md)：开发与贡献约定。

## 技术方案

C#、.NET 10 LTS、WPF、Win32/COM 接口；SQLite 计划在阶段 1 引入。

绳线和每张卡片各有独立窗口，避免透明大窗口拦截卡片之间的输入；隐藏时调用真正的窗口隐藏。平台实现目前收敛在 App 项目，纯坐标、显示时序和拖拽结果规则在 Core 中。阶段 0 完整验收后再接入截图收集和历史管理。

## 开发阶段

- [ ] 阶段 0：桌面交互原型（可运行，待完成验收矩阵）
- [ ] 阶段 1：截图收集、复制、拖拽与历史闭环
- [ ] 阶段 2：预览、标注、批量操作与设置
- [ ] 阶段 3：录像处理与兼容性
- [ ] 阶段 4：安装、升级与正式发布

## 项目关系与许可证

本项目由独立维护者开发，与 Tendedero 作者无隶属或背书关系。Tendedero 的名称、图标和文档图片不作为本项目的品牌或素材使用。

本仓库自身代码的发布许可证待确定；公开仓库不等于已授予开源许可。当前实现未复制原项目源码。后续如移植或改写原项目源码，将保留适用的原作者版权声明与完整 MIT 许可文本。详情见 [来源与授权说明](THIRD_PARTY_NOTICES.md)。
