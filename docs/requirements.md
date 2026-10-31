# sdemo.kb 需求计划

> 状态：需求计划（v1），2026-10-31
> 范围：需求编号 KB-01–KB-13；分期见 §5。
> 关联：[品牌蓝图](../../../docs/sdemo-brand-blueprint.md) §C · [版本模型](kb-versioning.md) · [联动需求](../../../docs/integration.md)

---

## 1. 目标与边界

**一句话**：个人文档的统一管理与检索——版本、差异、引用追溯，全都本地。

- 不隶属 DesktopAgent；独立应用、独立库、独立索引生命周期。
- **零模型凭证**：只做"存 + 找"；回答与总结由 DesktopAgent 用自己的模型完成。
- 外部依赖只有 SQLite FTS5；v1 不做向量检索（embedding 后置）。
- 数据目录：`%APPDATA%\SDemo\Apps\sdemo.kb\kb.db`。

**v1 不做**：云端同步、团队共享、PDF 版面还原、自动摘要、AI 自动改写（改写必须经 DesktopAgent + 用户确认）。

## 2. 需求清单

| # | 需求 | 优先级 | 验收要点 |
| --- | --- | --- | --- |
| KB-01 | 文档实体 + 版本链（DAG，不可变版本） | P0 | 一次改动只新建一个版本 |
| KB-02 | 块级切分（标题 + 段落），内容寻址去重 | P0 | 改一段不复制整篇 |
| KB-03 | 块级 diff + 词级 diff（Myers/patience） | P0 | 界面能高亮"这一句改了" |
| KB-04 | 变更理由 `rationale`（用户 / AI / 会议 三入口） | P0 | 可回答"为什么改成 D" |
| KB-05 | 命名修订（requirement 强制语义版本） | P1 | v1.0 / v1.1 / v2.0 |
| KB-06 | FTS5 + 2-gram 预分词检索（复用已验证方案） | P0 | 中文两字查询命中 |
| KB-07 | 三个索引面：当前态 / 历史态 / 变更事实 + 查询路由 | P0 | "以前怎么写"能召回历史 |
| KB-08 | 导入 PDF / docx / md，导入即建 `v1` | P0 | 重新导入自动算 diff |
| KB-09 | 引用存 `docId@version#block` | P0 | 能反查受影响的待办 |
| KB-10 | 影响面清单：来源已变更的引用标记 | P1 | 供 DesktopAgent 复核 |
| KB-11 | MCP 工具面 `kb_*`（`kb_update` 高风险必须确认） | P0 | 块级操作，禁止整篇提交 |
| KB-12 | 保留策略：requirement 全留，笔记留 30 版 | P1 | 磁盘占用可见 |
| KB-13 | 向量检索（块级 + 按 hash 复用） | P2 | 改一段只重嵌一段 |

## 3. 数据与检索

| 项 | 约定 |
| --- | --- |
| 核心表 | `doc` / `doc_version`（不可变）/ `block`（内容寻址）/ `block_edge`（版本→块）/ `ref`（引用）/ `rationale` / `index_meta` |
| 版本模型 | DAG：一个版本可基于任意旧版本；`parent_id` 可空；不物理删版本 |
| 分块 | 按标题层级 + 段落；块 hash 内容归一化（空白/换行）后再寻址 |
| 检索 | FTS5 + 2-gram 预分词；索引分三面：当前态 / 历史态 / 变更事实；查询路由由 `kb_search` 决定走哪面 |
| 引用 | `sdemo.kb/doc/<docId>@<version>#<blockId>`；反查表按 blockId 建索引 |
| 保留 | `kind=requirement` 全留；其它留 30 版；回收站 7 天软删 |
| 导入 | md / docx / pdf；导入即建 `v1`；再次导入按块 diff 生成新版本 |

## 4. 工程约束（多仓）

- 五件套：`SDemo.App.Kb.{Contracts,Storage,Core,Mcp,Host}`；`Mcp` 与领域逻辑物理隔离。
- 不引用 Agent、不引用其它 App；跨应用只存 URI 字符串，解析责任在 Agent。
- 工具面：`kb_search` / `kb_get` / `kb_history` / `kb_diff` / `kb_create_block_draft` / `kb_update_block` / `kb_delete_doc` / `kb_import` / `kb_refs`；`kb_update_block` 与 `kb_delete_doc` 为高风险。
- 契约以主仓 [contracts](../../../docs/contracts/) 为准。

## 5. 分期与交付

| 期 | 覆盖需求 | 交付物 | 验收 |
| --- | --- | --- | --- |
| **P4** v1 | KB-01 ~ KB-04、KB-06 ~ KB-09、KB-11、KB-12 | 导入、版本链、块 diff、关键词检索、引用、`kb_*` MCP | 100 篇文档导入后检索命中；引用可跳回原文；改一段只新增一个版本 |
| **P4 尾** | KB-05、KB-10 | 语义版本、影响面清单 | 引用变更后能列出受影响待办 |
| **P4+** | KB-13 | 向量检索 | 同义查询命中，改一段只重嵌一段 |

## 6. 联动点

| 联动 | 做法 |
| --- | --- |
| 待办关联文档 | planner 存 `sdemo.kb/doc/<id>@v#b`；Agent 解析并调 `kb_get` 显示标题 |
| 统一搜索 | Agent fan-out 调 `kb_search`，片段合并排序 |
| 影响面 | 会议/需求改了文档 → `kb_refs` 反查引用 → Agent 提示受影响待办 |
| 变更理由 | `rationale.source = user / ai / meeting`，AI 改动必须由 Agent 记录会话与 prompt 版本 |

## 7. 风险与待定

| 项 | 说明 |
| --- | --- |
| R1 | 索引成本随文档量增长 → 增量索引 + 整库重建自愈；块级去重减少重复 |
| R2 | 块切分粒度难统一 → 先按标题+段落，允许手动合并/拆分块 |
| Q1 | `kind` 分类默认行为（note / reference / requirement / decision） |
| Q2 | 导入源优先级（md / docx / pdf 谁先） |
| Q3 | embedding 模型归属（本地 ONNX / 由 Agent 提供）——v1 不做，P4+ 再定 |
