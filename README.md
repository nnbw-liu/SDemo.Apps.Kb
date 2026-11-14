# SDemo.Kb

**sdemo.kb**：个人文档的版本、差异、检索与引用追溯。只存只找，**不持有模型凭证**。

> 本仓是 [SDemo 主仓](https://github.com/nnbw-liu/SDemo) 的子模块（`Kb/`，主仓根级）。
> 状态：**P4-a 地基 + P4-b 保留 + P4-e 查看器第一刀**（2026-11-14）——七工程、`SDemo.Kb.slnf`、52 个测试。
>
> **定位**：KB 不是一个应用，而是一块数据面 + 一个查看器。它与 DesktopAgent、DesktopAssembly 同级，**不挂在 `Apps/` 下**——写面是给所有应用用的协议，读面才是产品。

## 现在能做什么

| 能力 | 入口 |
| --- | --- |
| 打开本地库阅读文档 | `SDemo.Kb.Viewer.exe`（01 库 / 02 文档） |
| 导入 md / txt，建不可变 `v1` | `kb_import` / `POST /v1/ingest` |
| 块级修订（add / replace / remove） | 查看器「编辑块」 / `kb_update` / `POST /v1/revise` |
| 中文两字检索 + 当前 / 历史 / 变更三面路由 | 查看器「检索」 / `kb_search` |
| 块级 + 词级 diff | `kb_diff` |
| 变更理由 `rationale` | 文档右栏 / `kb_changes` |
| 引用 `docId@v<seq>#<blockId>` + 影响面反查 | 文档右栏 / `kb_refs` / `POST /v1/resolve` |
| AI 写摘要（不调模型） | `kb_annotate` |
| 暂存 TTL / 回收站 / 引用保护 / 块与原件 GC | 后台维护 / `--maintain` |
| 待归档收件箱 + 批量归档 + 存储统计 | 查看器「待归档」 / `/v1/inbox` `/v1/stats` |
| KB 离线时写入不丢 | `KbOutbox` + `KbIngestClient` |

未做：03 版本对比 / 05 导入 / 07 设置屏、docx / pptx / xlsx / pdf 解析（P4-d）、命名修订与多父合并（P4-c）、向量检索（P4+）。

## 快速开始

```powershell
dotnet build SDemo.Kb.slnf -c Release
dotnet test  tests\SDemo.Kb.Tests -c Release

# 打开查看器（内嵌数据面；默认 %APPDATA%\SDemo\Apps\sdemo.kb）
src\SDemo.App.Kb.Viewer\bin\Release\net10.0-windows10.0.19041.0\SDemo.Kb.Viewer.exe

# 无界面数据面（给 Agent / App 用）
src\SDemo.App.Kb.Host\bin\Release\net10.0\SDemo.Kb.exe

# 给某个 App 签写入 token（stdout）
SDemo.Kb.exe --issue-token sdemo.meeting
```

写一笔：

```http
POST /v1/ingest
Authorization: Bearer <token>
Content-Type: application/json

{
  "source": { "appId": "sdemo.meeting", "entityUri": "sdemo.meeting/meeting/01H8" },
  "kind": "decision",
  "title": "周会 · 部署方案评审",
  "blocks": [{ "op": "add", "text": "决议：部署改为本地双写。" }]
}
```

其余接口与工具见 [AGENTS.md](AGENTS.md) §契约。

## 工程

| 工程 | 职责 |
| --- | --- |
| `src/SDemo.App.Kb.Contracts` | DTO / URI / 枚举 / 错误码 |
| `src/SDemo.App.Kb.Storage` | SQLite + FTS5 + 2-gram |
| `src/SDemo.App.Kb.Core` | 版本链 / 块 / diff / 检索 / 引用 / outbox |
| `src/SDemo.App.Kb.Mcp` | 11 个 `kb_*` 工具 + JSON-RPC |
| `src/SDemo.App.Kb.Host` | 回环 HTTP（写面 / resolve / MCP）+ CLI |
| `src/SDemo.App.Kb.Web` | 查看器 Razor 组件（四屏） |
| `src/SDemo.App.Kb.Viewer` | WPF + BlazorWebView 组合根（内嵌 KbServer） |
| `tests/SDemo.Kb.Tests` | 版本链 / 检索 / diff / 引用 / MCP / HTTP / outbox / 保留 |

## 文档

| 想知道 | 读 |
| --- | --- |
| 完整设计（写入面 / 保留 / 检索 / 产物） | [docs/kb-design.md](docs/kb-design.md) |
| 界面设计与原型 | [docs/kb-ui.md](docs/kb-ui.md) · [docs/ui-mockups/](docs/ui-mockups/) · [UI 技术落地](docs/kb-ui-spike.md) |
| 需求计划（KB-01–13、分期、验收） | [docs/requirements.md](docs/requirements.md) |
| 版本链、块级差异、三索引面 | [docs/kb-versioning.md](docs/kb-versioning.md) |
| 需求原文与优先级 | [../docs/sdemo-brand-blueprint.md](../docs/sdemo-brand-blueprint.md) §C |
| 联动（引用反查、统一搜索） | [../docs/integration.md](../docs/integration.md) |

## 边界

- **零模型凭证**：回答由 DesktopAgent 基于片段生成；本应用只负责"存 + 找"。
- **版本不可变**：一次改动新建一个版本；块级内容寻址去重；引用存 `docId@version#block`。
- **不做向量化差异**：只对块内容做 embedding（P2 需求，v1 可后置）。
- 数据目录 `%APPDATA%\SDemo\Apps\sdemo.kb\`（`kb.db` + `blobs\` + `cache\`）。


