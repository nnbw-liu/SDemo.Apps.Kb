# SDemo.Kb

**sdemo.kb**：个人文档的版本、差异、检索与引用追溯。只存只找，**不持有模型凭证**。

> 本仓是 [SDemo 主仓](https://github.com/nnbw-liu/SDemo) 的子模块（`Kb/`，主仓根级）。
> 状态：**P4-a 地基已实现并验收**（2026-11-14）——五件套工程、`SDemo.Kb.slnf`、36 个测试。
>
> **定位**：KB 不是一个应用，而是一块数据面 + 一个查看器。它与 DesktopAgent、DesktopAssembly 同级，**不挂在 `Apps/` 下**——写面是给所有应用用的协议，读面才是产品。

## 现在能做什么

| 能力 | 入口 |
| --- | --- |
| 导入 md / txt，建不可变 `v1` | `kb_import` / `POST /v1/ingest` |
| 块级修订（add / replace / remove） | `kb_update` / `POST /v1/revise` |
| 中文两字检索 + 当前 / 历史 / 变更三面路由 | `kb_search` |
| 块级 + 词级 diff | `kb_diff` |
| 变更理由 `rationale` | `kb_changes` |
| 引用 `docId@v<seq>#<blockId>` + 影响面反查 | `kb_refs` / `POST /v1/resolve` |
| AI 写摘要（不调模型） | `kb_annotate` |
| KB 离线时写入不丢 | `KbOutbox` + `KbIngestClient` |

未做：docx / pptx / xlsx / pdf 解析（P4-d）、TTL 与收件箱、KB 界面（P4-b）、向量检索（P4+）。

## 快速开始

```powershell
dotnet build SDemo.Kb.slnf -c Release
dotnet test  tests\SDemo.Kb.Tests -c Release

# 启动回环数据面；runtime 写到 %LOCALAPPDATA%\SDemo\Hub\apps\sdemo.kb.json
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
| `src/SDemo.App.Kb.Host` | 回环 HTTP + CLI |
| `tests/SDemo.Kb.Tests` | 版本链 / 检索 / diff / 引用 / MCP / HTTP / outbox |

## 文档

| 想知道 | 读 |
| --- | --- |
| 完整设计（写入面 / 保留 / 检索 / 产物） | [docs/kb-design.md](docs/kb-design.md) |
| 界面设计与原型 | [docs/kb-ui.md](docs/kb-ui.md) · [docs/ui-mockups/](docs/ui-mockups/) |
| 需求计划（KB-01–13、分期、验收） | [docs/requirements.md](docs/requirements.md) |
| 版本链、块级差异、三索引面 | [docs/kb-versioning.md](docs/kb-versioning.md) |
| 需求原文与优先级 | [../docs/sdemo-brand-blueprint.md](../docs/sdemo-brand-blueprint.md) §C |
| 联动（引用反查、统一搜索） | [../docs/integration.md](../docs/integration.md) |

## 边界

- **零模型凭证**：回答由 DesktopAgent 基于片段生成；本应用只负责"存 + 找"。
- **版本不可变**：一次改动新建一个版本；块级内容寻址去重；引用存 `docId@version#block`。
- **不做向量化差异**：只对块内容做 embedding（P2 需求，v1 可后置）。
- 数据目录 `%APPDATA%\SDemo\Apps\sdemo.kb\`（`kb.db` + `blobs\` + `cache\`）。


