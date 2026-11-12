# Wafer 风格控制面板

2026-11-10 依据 `https://app.wafer.ai/models` 公开返回的编译后页面资源，以及用户提供的模型页截图移植到 WPF。未取得 Wafer 的原始工程源码或登录后页面数据。

- 模型页脚本将 `/app/bg-clouds.webp` 指定为背景，桌面 CSS 以 1551×984 显示并从内容区顶部下移 190px。WPF 在圆角主画布中按同样尺寸居中摆放，并对图像顶部做透明渐入。
- 公开 CSS 的玻璃模式使用 `--surface-card:#25312f59`、37px 背景模糊、`--radius-card:24px`；外层主面板圆角 26px、导航宽 230px。WPF 沿用颜色、透明度、尺寸和层级。WPF 没有直接等价的 CSS `backdrop-filter`，因此卡片不执行背景模糊。
- 首屏使用居中标题、黑色彩边横幅、云层上的半透明内容卡片。原有六个功能页、操作按钮、键盘和无障碍名称继续可用。
- 后续按用户反馈移除了首屏居中标题与黑色横幅，侧栏改为浅色。顶部中央加入 WPF 悬浮胶囊 Dock，参考 [Great UI Floating Dock Menu](https://www.great-ui.com/components/floating-dock-menu) 的外壳、pill 按钮和悬浮阴影；显示、导入、收集状态、收起面板、退出五项操作在所有页面共用。未复制其 React 源文件。
- 云图原始资源的分发权尚未确认，见 [第三方说明](../THIRD_PARTY_NOTICES.md)。

验证：Release 构建无警告、无错误；控制面板诊断 47 项通过，包含小窗口和 11 种语言。预览位于 `artifacts/wafer-dashboard-validation/dashboard-shelf.png`。
