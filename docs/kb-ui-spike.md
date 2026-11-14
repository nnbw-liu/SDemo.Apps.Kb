# KB 查看器 UI 技术 spike（WPF + BlazorWebView）

> 状态：spike 结论（2026-11-14）· 服务于 P4-e
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

## 4. 结论与推荐

**可行，但界面必须单独成工程。** 原因是测试工程是 `net10.0`，
不能引用 `net10.0-windows` 的 WPF 工程；把 Host 直接改成 WPF 会让
`tests/SDemo.Kb.Tests` 失去对 `KbServer` 的引用。

推荐结构（P4-e 落地时）：

```
src/SDemo.App.Kb.Web       net10.0            Razor 组件 + wwwroot（7 屏）
src/SDemo.App.Kb.Viewer    net10.0-windows    WPF + BlazorWebView，组合根
src/SDemo.App.Kb.Host      net10.0            保持无界面数据面 + CLI（测试用）
```

Viewer 与 Host 的关系有两个选项，**需要在 P4-e 开工前拍板**：

| 选项 | 做法 | 代价 |
| --- | --- | --- |
| A. Viewer 内嵌 KbServer（推荐） | Viewer 是唯一 UI 进程，进程内起回环服务并直接调 Core；Host 只做无界面/服务模式 | 两进程同时打开同一库需单实例互斥；runtime 文件只能一个 owner |
| B. Viewer 做 Host 的客户端 | Viewer 通过回环 HTTP 读正文，需要 `sdemo.kb` 级 token | token 分发与本地权限更复杂；多一次序列化 |

选项 A 与设计 §1.1「界面与服务一起发布」一致，也避免把特权 token 落到 UI 进程配置里。

## 5. 没验证的部分

- 没有真正启动窗口做渲染验收（spike 只到构建层）；P4-e 的第一件事应是跑通
  「01 库 + 02 文档」两屏并用真实库数据验收。
- 没测 1440×900 截图、深浅色、键盘与 `sdemo-ui.css` 的像素一致性。
- 没测 WebView2 在无运行时机器上的回退（需要 bootstrapper 或安装器检查）。
