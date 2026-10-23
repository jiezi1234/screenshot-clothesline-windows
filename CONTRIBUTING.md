# 贡献说明

项目目前处于阶段 3 录像首尾裁剪和扩展图片格式开发验证阶段；用户本机 v0.5 关键流程反馈正常，完整兼容性矩阵仍有待办。开始实现前请阅读 [需求规格](docs/REQUIREMENTS.md)、[迁移计划](docs/MIGRATION_PLAN.md) 和 [来源说明](THIRD_PARTY_NOTICES.md)。

- 实现参考限定为原版 macOS Tendedero、项目需求与微软官方文档，不参考已有 Windows 移植实现。
- 原版代码的复制、改写或移植须记录来源并保留许可。
- 不引入原项目名称、图标或受排除的文档图片作为本项目素材。
- 文件归属、取下、历史清理及显式删除必须保持独立语义。
- 文件操作失败不得以永久删除、静默丢弃记录等方式掩盖。
- 新增依赖需说明用途、许可和部署影响。
- PR 说明具体行为变化、验证环境、验证结果和已知限制。
- 对文件生命周期、去重、恢复和平台交互添加有实际价值的测试。
- 不提交截图内容、个人路径、账户凭据、构建产物或本地数据库。

建议分支前缀：`codex/` 或其他描述性功能分支。使用 Windows 11 x64 和 .NET 10 SDK：

```powershell
./scripts/build.ps1
./scripts/test.ps1
./scripts/run.ps1
./scripts/publish.ps1
```

交互式桌面可追加 `./scripts/test.ps1 -Desktop`；先退出已运行的应用，避免快捷键和浮层干扰测试。测试只用生成的图片与独立数据库，结果在忽略提交的 `artifacts/` 中。实际剪贴板、跨应用拖放和兼容性矩阵须独立记录，不能用规则测试替代。

脚本支持 `-Dotnet '完整路径\dotnet.exe'`。新增运行时依赖需更新 `licenses/`、`THIRD_PARTY_NOTICES.md` 与打包脚本；不得遗漏自包含运行时的许可证和通知。GitHub Actions 模板暂在 `docs/ci/`，当前未启用云端 CI。

WebP/HEIC 样本是本项目生成的纯色图，说明见 src/Clothesline.App/Fixtures/README.md。正常构建只需要 .NET；可选的 scripts/generate-format-fixtures.py 使用独立 Python 环境重建素材，不将 Python 或编码库加入应用运行时。
