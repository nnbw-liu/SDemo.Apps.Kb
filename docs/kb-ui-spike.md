# KB 查看器 UI 技术 spike（WPF + BlazorWebView）

> 状态：**已落地（P4-e 第一刀，2026-11-14）** · 服务于 P4-e
> 关联：[kb-ui.md](kb-ui.md)（7 屏设计）· [kb-design.md](kb-design.md) §1.1（常驻服务 + 查看器）

## 1. 问题

KB 的查看器要用和 Planner 一致的桌面技术栈：
**WPF 窗口 + BlazorWebView + Razor 组件 + `sdemo-ui.css`**。
需要验证三件事：

1. `net10.0` 的 Core 能否被 `net10.0-windows` 的 WPF 直接引用；
2. BlazorWebView 在 `SDemo` 现有的包版本（10.0.110）下能否构建；
3. 新增界面会给安装包带来多大增量。

## 2. 做法

在临时目录建一个一次性 spike（不入仓）：

| 工程 | TFM | 内容 |
| --- | --- | --- |
| `Spike.Components` | `net10.0`（Razor classlib） | `Home.razor` + `wwwroot/index.html` |
| `Spike.Viewer` | `net10.0-windows10.0.19041.0`（WPF） | `BlazorWebView` + `RootComponent`，引用 Components |

包：`Microsoft.AspNetCore.Components.WebView.Wpf 10.0.110`（Planner 已在用，缓存命中）。

## 3. 结果

- **构建通过**：0 警告 / 0 错误，`dotnet build -c Release`。
- **输出体积**：48 个文件，约 **28 MB**；其中 `Microsoft.Windows.SDK.NET.dll` 占 23.7 MB，
  WebView2 托管层约 0.6 MB，其余为 WPF/Blazor 组件。
- **WebView2 运行时**：本机已安装（Edge WebView2 Runtime 157.x），随 Windows 10/11 常见；
  安装包不需要自带浏览器内核。
- **引用链**：WPF Viewer 可以同时引用 `net10.0` 的 Core 与 Razor Components，无需改 Core TFM。

## 4. 结论与落地

**可行，且已按选项 A 落地。** 界面单独成工程，测试工程不受 WPF TFM 影响：

```
src/SDemo.App.Kb.Web       net10.0            Razor 组件 + wwwroot（四屏）
src/SDemo.App.Kb.Viewer    net10.0-windows    WPF + BlazorWebView，组合根；内嵌 KbServer
src/SDemo.App.Kb.Host      net10.0            保持无界面数据面 + CLI（测试用）
```

- Viewer 同时承担 UI 与数据面：进程内直接调 Core，不把特权 token 写进 UI 配置。
- 单实例互斥 `Local\SDemo.Kb.Viewer`；启动即起 KbServer、写 runtime 文件、跑一次维护，之后每小时一次。
- Viewer 用 `Microsoft.NET.Sdk.Razor`（**不是** `Microsoft.NET.Sdk`）；否则 `_framework/blazor.webview.js` 不随静态资产提供，界面会停在「正在启动」。
- 窗口 1180×740；≤1280 时右侧版本/引用栏折叠为抽屉，≤1080 时列表收窄。

## 5. 已验证 / 仍未做

已验证（构建 + 真实进程截图）：

- 七屏可打开：01 全部文档、02 文档详情、03 版本对比、04 待归档、05 导入、06 检索、07 设置。
- 阅读正文、版本链、引用与变更理由、待归档批量归档、检索结果与 `hitTerms`。
- 版本对比的块级摘要 + 词级高亮 + rationale 卡片；设置页读写保留策略、存储统计、重建索引与 token 目录提示。
- 导入页文件选择支持 md / txt / docx / pptx / xlsx / 文字层 pdf；单文件 100 MB 上限；anchor 位置在块右侧显示；原件按 sha256 存 `blobs\`。
- 扫描件 PDF 显示「待识别」，不进检索；Agent 用 `kb_update` 回写后自动转 ready。
- 块编辑进入编辑态；保存走 `kb_update`（`expectedVersion` 冲突提示）。
- 窗口进程同时提供 `/v1/health`，runtime 文件能写回。

仍未做：

- 图片 OCR / caption 与完整 Agent 派生入口（当前扫描件回写走 `kb_update`）。
- 拖拽入队（目前是文件选择）；深/浅色主题的逐屏像素校验、1440×900 截图回归。
- 无 WebView2 运行时机器上的引导安装（需要 bootstrapper 或安装器检查）。

