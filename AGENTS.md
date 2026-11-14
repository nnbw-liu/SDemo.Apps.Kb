# AGENTS · sdemo.kb 子仓

> 路由文件。跨产品文档在 `../docs/`（相对本仓根）。

## 职责

文档实体 + 不可变版本链 + 块级内容寻址 + 差异与引用追溯；只存只找，**不持有模型凭证**。

## 层级

本仓挂在**主仓根级**（`Kb/`），不在 `Apps/` 下：

| 层 | 成员 | 含义 |
| --- | --- | --- |
| 根级 | `DesktopAgent/` `DesktopAssembly/` `Kb/` | 可独立部署的进程级产品；KB 是数据面服务 |
| `Apps/` | `Planner/` `Meeting/` | 插件式应用，各自一摊业务数据 |
| `Shared/` | SDemo.UI.* | 共享类库，不是产品，没有进程与数据库 |

判断标准：**有独立进程、独立数据目录、要单独发版 → 根级；只是被引用的类库 → Shared；用户点开用的插件 → Apps。**

## 现状（2026-11-14）

✅ **P4-a 地基 + P4-b 保留已落地并跑通验收**：五件套 + `SDemo.Kb.slnf` + 49 个测试。

| 已做 | 说明 |
| --- | --- |
| 版本链 | `kb_doc` / `kb_version`（DAG，`parent_id` 可分叉）/ `kb_block` / `kb_block_edge`；版本只增不改 |
| 块级内容寻址 | 空白/换行归一化后 hash；同文档同 hash 复用块；`content_hash` 相同直接跳过，不建版本 |
| 三索引面 + 路由 | 当前态 / 历史态 / 变更事实（+ 暂存需 `scope=recent`）；关键词路由「现在 / 以前 / 为什么 / 最近」 |
| 中文检索 | FTS5 + 2-gram 预分词；`hitTerms` / `noHit` 随结果返回 |
| 差异 | 块级 LCS 对齐 + 词级 diff；`kb_change` 永久保留 before/after/rationale |
| 引用 | `docId@v<seq>#<blockId>`；`/v1/resolve` 只回元数据；`kb_refs` 反查影响面 |
| 写面 | `/v1/ingest` `/v1/revise` `/v1/promote` `/v1/retract`；per-app Bearer token；幂等 |
| MCP | 11 个 `kb_*` 工具 + 风险/审批元数据；`kb_update` / `kb_delete` 高风险 |
| 写入方 outbox | `KbOutbox` + `KbIngestClient`；KB 离线时写入落本地队列，不抛回业务事务 |
| 导入 | md / txt；导入即建 `v1`，再次导入同一路径按块 diff 建新版本 |
| **保留与 GC** | staging TTL（会议 30 / Agent 90，可被 `ttlDays` 覆盖）→ 软删 → 回收站 7 天 → 块/原件 GC；被引用文档整篇保护 |
| **收件箱与统计** | `/v1/inbox`（到期倒计时、正文/原件体积）、`/v1/promote-batch`、`/v1/stats`、`/v1/settings/retention`；Host 每小时维护，`--maintain` 手动跑 |

**未做（按分期）**：docx / pptx / xlsx / pdf 解析与 anchor（P4-d）；KB 查看器界面（P4-e，spike 见 [docs/kb-ui-spike.md](docs/kb-ui-spike.md)）；向量检索（P4+）。命名修订与多父合并（P4-c）尚未做。

## 工程结构

| 工程 | 职责 | 依赖 |
| --- | --- | --- |
| `src/SDemo.App.Kb.Contracts` | 写面 DTO、MCP 入参、`KbUri`、枚举、错误码、JSON/路径 | — |
| `src/SDemo.App.Kb.Storage` | SQLite schema、FTS5、2-gram、块/版本/引用/审计的 SQL | Contracts + `Microsoft.Data.Sqlite` |
| `src/SDemo.App.Kb.Core` | 版本链、切块、hash、diff、检索路由、refs/resolve、保留、outbox 客户端 | Contracts + Storage |
| `src/SDemo.App.Kb.Mcp` | `kb_*` 工具目录 + JSON-RPC 处理 | Contracts + Core |
| `src/SDemo.App.Kb.Host` | 回环 HTTP（写面 / resolve / MCP）、runtime 文件、CLI | 全部 |
| `tests/SDemo.Kb.Tests` | 版本链 / 检索 / diff / 引用 / MCP / 回环 HTTP / outbox | 全部 |

依赖方向单向：`Contracts ← Storage ← Core ← Mcp / Host`。`Mcp` 不直接碰 SQL；`Host` 不写业务规则。

## 命令

```powershell
dotnet build SDemo.Kb.slnf -c Release
dotnet test  tests\SDemo.Kb.Tests -c Release

# 启动数据面（默认 %APPDATA%\SDemo\Apps\sdemo.kb）
src\SDemo.App.Kb.Host\bin\Release\net10.0\SDemo.Kb.exe
SDemo.Kb.exe --data-dir <目录> --port <端口>
SDemo.Kb.exe --issue-token sdemo.meeting     # 签写入 token（stdout）
SDemo.Kb.exe --rebuild-index                 # 整库重建 FTS
SDemo.Kb.exe --maintain                      # 跑一次 TTL / 回收站 / GC
```

运行期连接信息写到 `%LOCALAPPDATA%\SDemo\Hub\apps\sdemo.kb.json`（不含 token）。

## 契约（代码即契约）

### 写面（不经 Agent、不走 MCP）

| 动词 | 路径 | 说明 |
| --- | --- | --- |
| ingest | `POST /v1/ingest` | 新建；`appId + entityUri + contentHash` 幂等；重复投递不建版本 |
| revise | `POST /v1/revise` | 块级 ops + `expectedVersion`；只有 add / replace / remove |
| promote | `POST /v1/promote` | 改 `retention`；durable → staging 必须 `confirmed=true`；staging 重新算 `expires_at` |
| retract | `POST /v1/retract` | 软删；返回受影响引用清单 |
| inbox | `GET /v1/inbox` | 待归档列表：到期倒计时、正文/原件体积；仅 KB / Agent |
| promote-batch | `POST /v1/promote-batch` | 批量归档（最多 200）；逐条返回成功/失败 |
| stats | `GET /v1/stats` | 库体积、原件体积、可清理项、文档/块/版本计数 |
| retention | `GET/PUT /v1/settings/retention` | 版本保留数、会议/Agent TTL、回收站天数 |

鉴权：`Authorization: Bearer <app token>`；`source.appId` 必须与 token 绑定一致（服务端注入，不接受自报）。token 以 sha256 存 `kb_token`。

### MCP 工具面（读 + Agent 写入）

| 工具 | 风险 | 审批 |
| --- | --- | --- |
| `kb_search` / `kb_get` / `kb_outline` / `kb_diff` / `kb_changes` / `kb_refs` | 低 | 否 |
| `kb_annotate` | 低 | 否 |
| `kb_create` | 中 | 否 |
| `kb_promote` | 中 | 一次 |
| `kb_update` / `kb_delete` | **高** | **总是** |

写面给 App 投影；MCP 写只给 Agent 的块级修正。**禁止整篇正文提交。**

### 数据与 URI

- 表：`kb_doc` `kb_version` `kb_block` `kb_block_edge` `kb_change` `kb_link` `kb_blob` `kb_blob_ref` `kb_fts` `kb_change_fts` `kb_token` `kb_audit` `kb_meta`。
- 引用：`sdemo.kb/doc/<docId>@v<seq>#<blockId>`；必须带版本。
- 数据目录：`%APPDATA%\SDemo\Apps\sdemo.kb\`（`kb.db` + `blobs\` + `cache\`）。

## 必读

| 改什么 | 读 |
| --- | --- |
| 需求与验收 | [docs/requirements.md](docs/requirements.md) |
| **完整设计（写入面 / 保留 / 检索 / 产物）** | [docs/kb-design.md](docs/kb-design.md) |
| **界面设计与原型** | [docs/kb-ui.md](docs/kb-ui.md) · [docs/ui-mockups/](docs/ui-mockups/) |
| 版本链 / 块 / diff / 索引面 | [docs/kb-versioning.md](docs/kb-versioning.md) |
| 跨应用引用 / 统一搜索 | [../docs/integration.md](../docs/integration.md) |
| 组件与宿主 | [../docs/contracts/](../docs/contracts/) |

## 铁律

1. **零模型凭证**：不引入 `IChatClient` / `ApiKey`；回答由 DesktopAgent 生成。
2. **版本不可变**：`doc_version` 只增不改；编辑 = 新版本 + 新块。
3. **块级操作**：对外只暴露块级增删改，禁止整篇覆盖。
4. **引用带版本**：`docId@version#block`，不许只存 `docId`（否则历史引用会漂）。
5. **删除软删 + 保留策略**：requirement 全留、笔记留 30 版；回收站 7 天。

## 常见坑

- 中文检索必须用 2-gram 预分词 + FTS5；`unicode61` 会把整段中文当一个 token，`trigram` 不支持两字查询。
- 块 hash 要做内容归一化（换行、行内空白、连续空行），否则「只改格式」也会生成新块。
- `Search` 的 AND→OR 放宽只对长查询（>3 个 2-gram）生效，短查询放宽会明显掉 precision。
- 路由词（以前 / 为什么 / 最近）只用于选索引面，查询前要剥掉，否则正文里永远匹配不到它。
- 标题参与 FTS terms（×4）；改标题必须重建该文档所有块的索引。
- TTL 只对 staging 生效；durable / requirement 的 `expires_at` 必须为 NULL。
- 回收站物理清理前必须先看 `kb_link`：有入库引用的文档整篇保留，否则历史引用会变成死链。
- 块 GC 的条件是「无版本边 + 无引用」；只删版本边不删块，历史才读得到。
- Schema 变更必须走 `KbSchema.Migrate` + `PRAGMA user_version`，不能只改 `CREATE TABLE IF NOT EXISTS`。
- 写面的 `source.appId` 由 token 决定，客户端自报的值只用于比对，不能作为 owner。
- 测试里用完 SQLite 要 `SqliteConnection.ClearAllPools()`，否则临时目录删不掉。

## 不要读

`obj/`、`bin/`、`TestResults/`、模型/索引缓存；`docs/requirements.md` 只通读一次。


