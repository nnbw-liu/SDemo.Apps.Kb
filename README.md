# SDemo.Apps.Kb

**sdemo.kb**：个人文档的版本、差异、检索与引用追溯。只存只找，**不持有模型凭证**。

> 本仓是 [SDemo 主仓](https://github.com/nnbw-liu/SDemo) 的子模块（`Kb/`，主仓根级）。
> 状态：🚧 **规划中（P4）**，本仓目前只有文档，没有代码。
>
> **定位**：KB 不是一个应用，而是一块数据面 + 一个查看器。它与 DesktopAgent、DesktopAssembly 同级，**不挂在 `Apps/` 下**——因为它的写面是给所有应用用的协议，读面才是产品。

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
- 数据目录 `%APPDATA%\SDemo\Apps\sdemo.kb\kb.db`。
