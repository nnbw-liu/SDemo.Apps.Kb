# sdemo.kb 完整设计（v1 提案）

> 状态：**已拍板；P4-a 地基已按本设计落地（2026-11-14）**。§2.1 schema、§3 写入面、§5.2 MCP 工具面均已实现并有测试覆盖；P4-b/c/d 见 §10。
> 取代/合并：[kb-versioning.md](kb-versioning.md)（版本与追溯，仍有效）· [requirements.md](requirements.md)（需求编号）
> 配套：[kb-ui.md](kb-ui.md)（界面设计）· [ui-mockups/](ui-mockups/)（可点原型）· [kb-ingest 契约](#3-写入面)
> 关联：[跨产品联动](../../docs/integration.md) · [Hub 契约](../../docs/contracts/hub-protocol.md) · [设计系统](../../docs/shared.md)

---

## 0. 一句话

**KB 不再是一个平级的 App，而是一块数据面 + 一个查看器。**

- **写**是普遍的：任何 App 都可能投递内容，走一条又窄又稳的协议，不经过 Agent、不需要模型。
- **读**是收敛的：正文检索只给 Agent 和 KB 自己的界面；其它 App 只能按 URI 取元数据。

这条不对称是整篇设计的地基。凡是与此冲突的旧表述，都要显式改写（见 §8）。

---

## 1. 定位与边界

### 1.1 它是什么

| 维度 | 结论 |
| --- | --- |
| 形态 | 常驻服务（回环 HTTP + MCP）+ 一个查看器界面；界面与服务一起发布 |
| 数据 | 文档实体 + 不可变版本链（DAG）+ 内容寻址块 + 原件 + 引用 + 变更理由 |
| 智能 | **零**：不持有模型凭证、不发起模型调用、可完全离线运行 |
| 强项 | 版本、差异、追溯、影响面、关键词检索 |
| 弱项（明确不做） | 语义检索（v1）、自动摘要、AI 改写、云端同步、文件同步盘、源码/日志存储 |

### 1.2 三条不可让的线

1. **零凭证**：任何 `ApiKey` / `IChatClient` / 模型端点出现在 KB 仓即为缺陷。
2. **版本不可变**：`doc_version` 只增不改；编辑 = 新版本 + 新块。
3. **块级操作**：对外只暴露块级增删改，禁止整篇正文覆盖（否则 diff 与增量索引全部失效）。

### 1.3 非目标

- 不做通用数据库：事务数据（待办状态机、日历 RRULE、录音二进制）留在各 App。
- 不做垃圾堆：`kind` 白名单 + 每 App 声明投影范围，KB 拒绝未知类型。
- 不做向量库（v1）：见 §6.4 的触发门。

---

## 2. 数据模型

### 2.1 七张表

| 概念 | 表 | 关键字段 | 说明 |
| --- | --- | --- | --- |
| 文档 | `kb_doc` | `id`(UUIDv7) `kind` `title` `owner_app` `uri` `retention` `derived_state` `current_version_id` `deleted_at` | 稳定身份；`retention` 与 `derived_state` 是本次新增 |
| 版本 | `kb_version` | `id` `doc_id` `seq` `parent_id` `content_hash` `author` `reason` `created_at` | 不可变；`parent_id` 允许分叉 |
| 块 | `kb_block` | `id` `doc_id` `content_hash` `text` `anchor_json` | 内容寻址；`anchor` 见 §2.4 |
| 版本→块 | `kb_block_edge` | `version_id` `block_id` `ordinal` | 版本只引用块，不复制正文 |
| 变更 | `kb_change` | `version_id` `op` `block_id` `before_text` `after_text` `rationale` `rationale_source` | 永久保留，历史可清但变更事实不清 |
| 引用 | `kb_link` | `from_uri` `to_uri`(`docId@version#block`) `owner_app` | 影响面反查的索引 |
| 原件 | `kb_blob` | `sha256` `mime` `size` `name` `stored_path` | 不可变；hash 即版本；去重免费 |
| 引用计数 | `kb_blob_ref` | `blob_sha` `doc_id` `version_id` | 归零才可回收 |
| 检索 | `kb_fts`（虚表） | 预分词 `terms` + `block_id` | 2-gram；见 §6 |
| 向量（预留） | `kb_vector` | `block_id` `content_hash` `model_id` `dim` `vec` | v1 不建表，只定形状（§6.5） |

### 2.2 四个层次

```
doc（实体）
 ├─ version（不可变，DAG）
 │   └─ edge → block（内容寻址，可被多版本共享）
 │        └─ anchor → 原件位置（page / slide / sheet!range / char offset）
 └─ link（指向别处的 URI，带版本）
```

### 2.3 为什么版本不需要"文件"概念

不按文件建模，按实体建模。同一份需求被导出成 docx、被会议引用、被 Agent 改写，仍然是**一个 doc** 的多条版本。文件只是 `artifact`。

### 2.4 `anchor`：跳回原文的唯一依据

`kb-versioning` §7 说"点开能跳到原文位置"，但没定义跳到哪。补上：

| 来源 | `anchor.kind` | `anchor.value` |
| --- | --- | --- |
| md / txt | `offset` | `{start, end}` |
| docx | `para` | `{index}` |
| pptx | `slide` | `{index}` |
| xlsx / csv | `sheet` | `{sheet, range:"A1:D20"}` |
| pdf | `page` | `{page, x?, y?}` |
| 图片 | `region` | `{x, y, w, h}` |

规则：**没有 anchor 的块不是导入产生的块**（手写块 `anchor=null`）。引用 `docId@version#block` 时，anchor 让"打开原文"可执行。

---

## 3. 写入面

> 目标：任何 App 都能写，**不需要模型、不需要 Agent 参与**。

### 3.1 为什么不能走 MCP

MCP 是"给模型看的工具面"。把写放进去，等于：

| 问题 | 后果 |
| --- | --- |
| Agent 成为写入代理 | Agent 没起来，Meeting 就存不进纪要 |
| 写被套上风险分级 | 一场会议 5 份纪要 = 5 次确认 |
| 写路径跟随 Agent 演化 | 排序/审批/toolHints 一改，全部写入方受影响 |

**结论：写面独立成契约，读面留在 MCP。两者的变更节奏不同，不许共用一层。**

### 3.2 传输与鉴权

复用 Hub 已验证的模式（`hub-protocol` §2/§9）：

| 项 | 约定 |
| --- | --- |
| 地址 | `http://127.0.0.1:<port>/v1/ingest`（端口由 KB 启动时确定并写回连接配置） |
| 鉴权 | `Authorization: Bearer <per-app token>`；token 由 KB 签发，与该 appId 绑定 |
| 发现 | `%LOCALAPPDATA%\SDemo\Hub\apps\sdemo.kb.json`（KB 是普通接入方，不搞特殊） |
| 只监听 | 回环；拒绝非 127.0.0.1 |

### 3.3 四个动词

```
POST /v1/ingest     新建（幂等）
POST /v1/revise     修订（块级 ops + expectedVersion）
POST /v1/promote    升格 / 降格保留等级
POST /v1/retract    软删
```

`promote` 是本次新增的第四个动词——**归档不是新建，而是同一个 docId 改 `retention`**。否则用户归档后库里会出现"三稿"和"三稿(归档)"两份。

### 3.4 请求形状

```jsonc
{
  "source": {                      // 必填，KB 侧由 token 校验 appId 一致性
    "appId": "sdemo.meeting",
    "entityUri": "sdemo.meeting/meeting/01H8…",
    "projectionVersion": 1
  },
  "kind": "decision",              // 白名单；见 §4.2
  "title": "2026-11-12 周会 · 部署方案评审",
  "retention": "staging",          // 默认由 kind 决定，见 §4.2
  "rationale": "周会决议，用户确认后转正式纪要",
  "idempotencyKey": "sdemo.meeting/01H8…/sha256:9f2c…",
  "blocks": [
    { "op": "add", "text": "…", "anchor": { "kind": "page", "value": { "page": 3 } } }
  ],
  "links": [
    { "to": "sdemo.kb/doc/01H9…@v7#b4", "rel": "based_on" }
  ],
  "artifacts": [
    {
      "role": "source",
      "sha256": "9f2c…",
      "mime": "application/pdf",
      "size": 2457600,
      "name": "评审材料.pdf",
      "path": "C:\\Users\\…\\评审材料.pdf"   // 或 uploadUrl，二选一
    }
  ]
}
```

### 3.5 六条硬约束

| # | 约束 | 理由 |
| --- | --- | --- |
| W-1 | `source.appId` 必须与 token 绑定一致 | 防冒名；`owner_app` 不接受自报 |
| W-2 | 幂等键 = `appId + entityUri + contentHash` | 重复投递不产生新版本；块 hash 相同即跳过 |
| W-3 | **写入方不能因 KB 离线而失败** | App 先落本地 outbox，后台补投（Hub 不自动拉起应用，见 `hub-protocol` §6） |
| W-4 | 写操作必须落审计 | 绕过 MCP 不等于绕过审计（对照铁律 5） |
| W-5 | App 只能改自己投影的内容 | 人工编辑的版本不得被投影覆盖（DAG 分叉 + `author` 区分） |
| W-6 | 未知 `kind` / 超出声明的投影范围 → 拒绝 | 防止 KB 变成垃圾桶 |

### 3.6 与本地 outbox 的关系

```
App 事务 ──► outbox 行（同库，事务内写） ──► 后台投递器 ──► /v1/ingest
                                                    │
                                         失败：指数退避，最长 7 天
                                         成功：标记 delivered，保留 30 天后清
```

App 侧只需要一张 `outbox` 表和一个后台循环——这在 Planner/Meeting 里已经是现成模式。

---

## 4. 保留模型

### 4.1 两级 + 一条保护

| 等级 | 谁写进来 | 版本保留 | Agent 默认检索 | 可被引用 | 可删 |
| --- | --- | --- | --- | --- | --- |
| **staging** | App 自动投影、AI 产物 | 最近 3–5 版 | ❌（`scope=recent` 才含） | ❌ | TTL 到期软删 → 回收站 7 天 |
| **durable** | 用户确认 / 手写 / 导入 | 全留（requirement 必全留） | ✅ | ✅ | 仅显式删除 |

> **引用保护（强制）**：任何被 `kb_link.to` 指向过的块，无论在哪一级，引用解除前不参与 GC。这是"其它内容可删除"的唯一安全底线。

### 4.2 默认值（写死在前端，不让用户选）

| 来源 | 默认 retention | kind 默认 | 理由 |
| --- | --- | --- | --- |
| KB 界面手写 / 导入 PDF·docx | `durable` | `note` / `reference` | 意图明确，本来就是文档 |
| Meeting 定稿纪要、确认的决议 | `staging` | `decision` | 等用户读完再定格 |
| Agent 生成的摘要 / 回答归档 | `staging` | `note` | 候选 ≠ 入库 |
| 用户点"归档会话" | `durable` | `note` | 动作本身即确认 |
| 会议原始转写 | **不进 KB** | — | 噪音 + 隐私 + 体积 |
| Planner 的 todo 行 | **不进 KB** | — | 事务数据；只投影周报/复盘 |

`kind` 白名单：`note` / `reference` / `requirement` / `decision`。**新增 kind 必须改契约 + schema**，不允许各 App 自造。

### 4.3 TTL（按 kind 与来源）

| 内容 | TTL |
| --- | --- |
| 会议纪要 / 转写摘要 | 30 天 |
| Agent 会话归档 | 90 天 |
| 图片 / 截图 | 30 天 |
| AI 摘要草稿 | 7 天 |

到期动作：`deleted_at` 置位 → 回收站 7 天 → 物理回收 blob（引用计数归零才动）。

### 4.4 为什么不是"只写归档内容"

| 场景 | 只写归档的后果 |
| --- | --- |
| 昨天开会，今天想搜"谁反对部署方案" | 没点归档 → KB 里没有 → Agent 答不出 |
| 批量回溯过去三个月的决策 | 只能一条条点 |
| Meeting 定稿纪要 | 弹第二个"是否归档"，用户在两个 App 里做同一个决定 |

**判断口诀：写入自动（像日志），定格显式（像档案）。**

---

## 5. 读取面

### 5.1 三个通道

| 通道 | 谁用 | 给什么 | 形态 |
| --- | --- | --- | --- |
| **MCP `kb_*`** | Agent | 检索、正文、历史、diff、影响面 | MCP over HTTP |
| **`/v1/resolve`** | 任何 App | **只给元数据**：标题、当前版本、是否存在、是否已变更 | 回环 HTTP + token |
| **KB 自身 Core** | KB 界面 | 全量 | 进程内 |

`resolve` 是这次补的缺口：Planner 的待办里存了 `sdemo.kb/doc/…@v3#b2`，界面要显示这条引用的标题——总不能渲染一次就找 Agent 转一趟。

`resolve` 返回：

```jsonc
{
  "uri": "sdemo.kb/doc/01H9…@v3#b2",
  "exists": true,
  "title": "工作区知识库 M3 详细设计",
  "currentVersion": 7,
  "stale": true,            // 引用的版本已不是当前版本
  "blockMissing": false,
  "deleted": false
}
```

**不含正文、不含摘要**——正文只能经 Agent 或 KB 界面获得。

### 5.2 MCP 工具面（v1）

| 工具 | 风险 | 审批 | 说明 |
| --- | --- | --- | --- |
| `kb_search(query, scope, filter)` | 低 | 否 | `scope` = `current`(默认) / `history` / `change` / `recent` |
| `kb_get(docId, version?)` | 低 | 否 | 正文 + 块 id + anchor |
| `kb_outline(docId)` | 低 | 否 | 目录，供浏览兜底 |
| `kb_diff(docId, from, to)` | 低 | 否 | 块级 + 词级 |
| `kb_changes(docId?, since?)` | 低 | 否 | 变更事实流（含 rationale） |
| `kb_refs(docId)` | 低 | 否 | 谁引用我 / 我引用谁 / 影响面 |
| `kb_annotate(docId, versionId, summary)` | 低 | 否 | 写"这段在讲什么"（摘要索引） |
| `kb_create(kind, title, blocks)` | 中 | 否 | 建文档 + v1 |
| `kb_update(docId, ops, rationale, expectedVersion)` | **高** | **总是** | 块级 ops，禁止整篇 |
| `kb_promote(docId, retention)` | 中 | 一次确认 | 归档 / 撤销归档 |
| `kb_delete(docId)` | **高** | **总是** | 软删 |

> `kb_update` 高风险、`kb_promote` 中风险，与 `integration.md` §4 审批矩阵一致。

### 5.3 权限矩阵

| 能力 | KB 界面 | Agent | 其它 App |
| --- | --- | --- | --- |
| 写入（ingest/revise） | ✅ | ✅（经 MCP） | ✅（限自己 namespace） |
| promote / retract | ✅ | ✅ | ❌ |
| 读正文 | ✅ | ✅ | ❌ |
| resolve 元数据 | ✅ | ✅ | ✅ |
| 改保留策略 / 重建索引 | ✅ | ❌ | ❌ |

---

## 6. 检索

### 6.1 四个索引面

| 面 | 内容 | 典型问法 |
| --- | --- | --- |
| A 当前态 | `current` 版本块 | 「现在怎么规定的」 |
| B 历史态 | 旧版本块（标 superseded） | 「以前是怎么写的」 |
| C 变更事实 | `kb_change` + rationale | 「为什么改成 D」 |
| D 暂存 | `retention=staging` 块 | 「最近那次会说了啥」（需显式扩） |

路由由 KB 用关键词判（不调模型）：出现「现在/最新」→ A；「以前/原来/第几版」→ B；「为什么/谁改的」→ C；「最近/昨天」→ A+D。

### 6.2 分层召回（v1）

```
L1 结构化过滤   retention / kind / 时间 / 来源 / 引用关系      SQL
L2 词法召回     2-gram FTS + 同义词扩展 + Agent 改写式         KB + Agent
L3 浏览兜底     命中不足 → kb_outline → 定点 kb_get            Agent
L4 重排         BM25 + 标题×4 + 引用热度 + 时间衰减            KB
```

### 6.3 两个必须落地的字段

`kb_search` 返回里加：

- `hitTerms`：命中了哪些词（可解释性；"为什么给我这条"）
- `noHit`：本次是否零命中（驱动 L3 兜底，并作为向量化的判据数据）

**这两个字段比上向量有用得多，而且几乎不花钱。**

### 6.4 要不要向量化：v1 不做，设一个触发门

中文 2-gram FTS 的 baseline 比英文高（近似 substring），且 KB 的唯一读者是模型——模型可以在**查询时**做同义词扩展与候选精排，不必在**索引时**付 embedding 成本。

| 观测 | 判断 |
| --- | --- |
| `noHit` 率 > 20% | 词法召回不够 |
| `noHit` 里"语义失败"占比 > 50%（人工判 50 条） | ✅ 该上向量 |
| 命中但选错文档 > 30% | 排序问题，上精排不上 embedding |

指标没到之前，先做排序权重（时间衰减、kind 加权、引用热度），收益更大。

### 6.5 将来接入：查询驱动的懒嵌入

不推翻零凭证原则——**向量是数据，不是模型**：

```
Agent: kb_search(q, mode=hybrid)
  → KB 词法召回候选 → 检查向量是否存在
      ├─ 齐全 → 融合返回
      └─ 缺失 → 返回 needsEmbedding[{blockId, contentHash, text}]
Agent: 调模型算向量 → kb_put_vectors(model_id, dim, vec[]) → KB 落库 → 重跑
```

| 好处 | 说明 |
| --- | --- |
| KB 永远不碰模型 | 一行模型调用都不进 KB 仓 |
| 只为"被检索到过"的块付成本 | 不需要后台全量嵌入 |
| Agent 离线自动降级 | `mode=lexical` |
| 换模型零迁移 | 新 `model_id` 向量天然不存在，懒生成，新旧可并行评估 |
| 天然可回滚 | 出问题就切 `lexical` |

契约现在预留、**代码现在不建**：

| 项 | 现在就要定 |
| --- | --- |
| 表形状 | `kb_vector(block_id, content_hash, model_id, dim, vec)`，PK `(block_id, model_id)` |
| 归一化 | 入库前 L2 归一化；距离固定 cosine；dim 与 `model_id` 不符直接拒 |
| 治理 | KB 有"当前生效 model_id"设置；不符的向量拒绝写入 |
| 范围 | 只嵌 `retention=durable` 的块 |

---

## 7. 产物与格式

### 7.1 谁来解析：确定性的归 KB，需要"看懂"的归 Agent

沿用铁律：**能写成确定性代码的全在 KB。**

| 格式 | 解析方 | 块策略 | anchor |
| --- | --- | --- | --- |
| md / txt / csv | KB | 标题 + 段落 | `offset` |
| docx | KB（zip+xml） | 标题层级 + 段落；表格单独成块 | `para` |
| pptx | KB | 一 slide 1–3 块（标题 / 要点 / 备注） | `slide` |
| xlsx / csv | KB | **不摊平**：sheet 概述 + 每行一句摘要 | `sheet!A1:D20` |
| pdf（文字层） | KB | 按页 + 段落 | `page` |
| pdf（扫描件） | Agent（OCR） | OCR 文本段 | `page` + 页图 |
| 图片 | Agent（OCR / caption） | 一图一块 = 文本投影 | `region` |
| 音频 / 视频 | App（Meeting/whisper） | 转写分段 | 时间码 |
| log | **不进 KB** | — | — |
| 源码 | **暂不进** | — | — |

### 7.2 图片：没有文本投影的二进制不是知识

| 图类型 | 处理 | 可检索 |
| --- | --- | --- |
| 截图（含文字） | OCR → 文本块 + 原件 | ✅ 好 |
| 白板 / 手写 | 多模态 → 描述 + 要点 | ⚠️ 中 |
| 照片 / 设计稿 | Agent 写 caption | ⚠️ 只能按描述搜 |
| 无 caption | 只进原件库 | ❌ |

### 7.3 派生状态机

OCR / caption / 大 pdf 解析都可能耗时数秒到数分钟，不能卡在 ingest 同步路径上：

```
derived_state: pending | ready | failed
```

| 时机 | 行为 |
| --- | --- |
| 块已就绪 | `ready`，可搜 |
| 需 Agent 派生 | `pending`，**不进检索面**；完成后写回块 → `ready` |
| 派生失败 | `failed`，UI 显示"未识别"，原件仍可打开 |
| promote | 昂贵派生只对 `durable` 付钱 |

**便宜派生（解压文本）随手做，昂贵派生（OCR/caption）只为归档内容付钱。**

### 7.4 log 为什么单独说

追加式、体量大、按时间序列、几乎不会被"引用某一段的某一版"。塞进版本链会崩：一天几千个版本、块 hash 全变、索引抖动。

> **log 不是知识；从 log 里得出的结论才是。** 结论进 KB（带 anchor 指回原文件行号），原始日志留在原处。

---

## 8. 对现有铁律的改写（必须显式执行）

| 现规则 | 改成 | 理由 |
| --- | --- | --- |
| 应用之间零引用 / 只经 MCP 访问 | 零**业务**调用；允许对 KB 的**单向 ingest** | 写是广播，读才是调用 |
| KB 挂在 `Apps/` 下（旧结构 `Apps/Kb`） | 迁到主仓根级 `Kb/`，与 Agent / Host 并列 | 发布节奏跟着契约走，而不是跟着应用走 |
| 宿主零业务数据 | 不变；Hub 只负责发现与用户授权拉起 | KB 不写 Hub 的业务表 |
| 统一搜索靠 Agent fan-out | 文本召回由 KB 一家承担；结构化过滤仍回各 App | 索引不该有三份 |
| `workspace-memory` 与 KB 并存 | **KB 是真源**，Agent 那份降级为工作区检索缓存 | 两份 FTS 必然漂移 |

最后一条是最大的重复建设风险，要在 P4 开工前定下来。

---

## 9. 存储与部署

| 项 | 约定 |
| --- | --- |
| 数据目录 | `%APPDATA%\SDemo\Apps\sdemo.kb\`（`kb.db` + `blobs\` + `cache\`） |
| 运行期 | `%LOCALAPPDATA%\SDemo\Hub\apps\sdemo.kb.json` |
| 原件 | 默认拷贝进 `blobs\<sha 前两位>\<sha>`；"仅索引不拷贝"作为高级选项 |
| 单文件上限 | 100 MB；超出只索引摘要 + 存路径 |
| 去重 | sha256 内容寻址，天然免费 |
| 页图 / 缩略图 | 视为缓存，可重建，不备份 |
| 密钥 | 无 |

磁盘粗算：1,000 份 PDF 平均 2 MB ≈ 2 GB；设置页必须显示"原件占用 / 可清理项"。

---

## 10. 分期

| 期 | 范围 | 验收 |
| --- | --- | --- |
| **P4-a 地基** | 版本链、块、FTS、`resolve`、写面契约 + outbox 投递 | 100 篇导入后关键词命中；改一段只增一个版本；KB 离线时 App 不报错 |
| **P4-b 保留** | staging/durable、promote、TTL、引用保护、待归档收件箱 | 归档后全量保留；到期清理不影响引用方 |
| **P4-c 追溯** | diff、rationale 三入口、影响面清单 | "为什么改成 D"可答；引用变更能列出受影响待办 |
| **P4-d 产物** | docx/pptx/xlsx/pdf 导入、anchor、派生状态机 | 表格命中行摘要并定位到单元格；图片待识别状态可见 |
| **P4+** | 向量化（仅当 §6.4 触发门达标） | 懒嵌入跑通，可一键回退 lexical |

---

## 11. 风险与未决

| # | 风险 / 未决 | 建议 |
| --- | --- | --- |
| R1 | KB 成为单点：KB 挂了全系统写不进 | 强制 outbox；KB 未启动不影响业务可用性 |
| R2 | 投影 schema 演进导致内容错乱 | `projectionVersion` + `kb_rebuild_projection(appId)` |
| R3 | 版本链 + 全量原件导致磁盘增长 | 配额告警 + "原件占用"可见 + 缓存可重建 |
| R4 | 格式解析库把安装包撑大 | `pack.ps1` 里量产物体积，docx/pptx 共用一个 OOXML 库 |
| R5 | 用户不点归档 → 内容全被 TTL 清 | 收件箱红点 + 侧栏提示 + "即将清理"批量入口 |
| Q1 | `workspace-memory` 何时并入 KB | P4 之后作为数据迁移，不混在功能开发里 |
| Q2 | 撤销归档是否允许 | 允许，但二次确认（可能已被引用） |
| Q3 | 团队版是否内嵌本地 ONNX | 读者是系统时走 A 方案；当前不适用 |

---

## 12. 相关文档

- 界面设计与原型：[kb-ui.md](kb-ui.md) · [ui-mockups/](ui-mockups/)
- 版本与追溯细节：[kb-versioning.md](kb-versioning.md)
- 需求编号：[requirements.md](requirements.md)
- 跨产品契约：[integration.md](../../docs/integration.md) · [hub-protocol.md](../../docs/contracts/hub-protocol.md)
