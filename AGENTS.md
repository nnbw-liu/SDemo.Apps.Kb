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

✅ **P4-a 地基 + P4-b 保留 + P4-e 查看器七屏 + P4-d 格式/原件/派生 + P4-c 语义版本/多父合并已落地**：五件套 + Web/Viewer + `SDemo.Kb.slnf` + 76 个测试。

| 已做 | 说明 |
| --- | --- |
| 版本链 | `kb_doc` / `kb_version`（DAG，`parent_id` 可分叉 + `kb_version_parent` 多父）/ `kb_block` / `kb_block_edge`；版本只增不改 |
| **语义版本** | requirement 从 v1.0 开始，缺省 minor 递增；写面/MCP 可显式 v2.0，但必须严格大于当前；普通文档不能命名 |
| 块级内容寻址 | 空白/换行归一化后 hash；同文档同 hash 复用块；`content_hash` 相同直接跳过，不建版本 |
| 三索引面 + 路由 | 当前态 / 历史态 / 变更事实（+ 暂存需 `scope=recent`）；关键词路由「现在 / 以前 / 为什么 / 最近」 |
| 中文检索 | FTS5 + 2-gram 预分词；`hitTerms` / `noHit` 随结果返回 |
| 差异 | 块级 LCS 对齐 + 词级 diff；`kb_change` 永久保留 before/after/rationale |
| 引用 | `docId@v<seq>#<blockId>`；`/v1/resolve` 只回元数据；`kb_refs` 反查影响面；外部实体可登记引用 |
| 写面 | `/v1/ingest` `/v1/revise` `/v1/promote` `/v1/retract`；per-app Bearer token；幂等 |
| 引用登记 | `/v1/refs` / `/v1/refs/retract`：Planner/Meeting 把实体 URI 对 KB 的引用写进 `kb_link`（`doc_id=''`），影响面/GC/清理保护才算数 |
| MCP | 12 个 `kb_*` 工具 + 风险/审批元数据；`kb_update` / `kb_delete` 高风险；`kb_merge` 中风险 |
| 写入方 outbox | `KbOutbox` + `KbIngestClient`；KB 离线时写入落本地队列，不抛回业务事务 |
| 导入 | md / txt / docx / pptx / xlsx / 文字层 pdf；导入即建 `v1`；再次导入按块 diff 建新版本 |
| **原件与派生** | 导入原件按 sha256 存 `blobs\` 并写 `kb_blob_ref`；扫描件 PDF 建 `derived_state=pending`，不进检索；`kb_get` 的 artifacts 带 `storedPath`，Agent OCR 后回写块自动转 ready |
| **anchor** | docx `para` / pptx `slide` / xlsx `sheet!range` / pdf `page` / md `offset`；查看器块右侧显示位置 |
| **保留与 GC** | staging TTL（会议 30 / Agent 90，可被 `ttlDays` 覆盖）→ 软删 → 回收站 7 天 → 块/原件 GC；被引用文档整篇保护 |
| **待归档与统计** | `/v1/inbox`（到期倒计时、正文/原件体积）、`/v1/promote-batch`、`/v1/stats`、`/v1/settings/retention`；Host 每小时维护，`--maintain` 手动跑 |
| **查看器** | WPF + BlazorWebView（选项 A：内嵌 KbServer）；01 全部文档 / 02 文档详情 / 03 版本对比 / 04 待归档 / 05 导入（Office/PDF）/ 06 检索 / 07 设置；深浅色、Ctrl K |
| **token 分发** | Host/Viewer 首次启动给白名单 App 各签一个 token，落到 `%LOCALAPPDATA%\SDemo\Hub\apps\sdemo.kb\tokens\<appId>.token`（ACL 仅当前用户）；`--tokens` 可列明文；`--issue-token` 轮换并覆盖 |
| **Planner 引用登记** | Planner 的 `KbRefSyncLoop` 监听 link 变化立即同步，30s 兜底轮询；通过 `/v1/refs` 登记/撤销；KB 未启动时自动跳过 |

**未做（按分期）**：图片 OCR / caption 与 Agent 派生回写的完整入口（当前回写走 `kb_update` 自动转 ready）；Meeting 侧的 `/v1/refs` 自动登记（Meeting 尚无 `src/`）；向量检索（P4+）。

## 工程结构

| 工程 | 职责 | 依赖 |
| --- | --- | --- |
| `src/SDemo.App.Kb.Contracts` | 写面 DTO、MCP 入参、`KbUri`、枚举、错误码、JSON/路径 | — |
| `src/SDemo.App.Kb.Storage` | SQLite schema、FTS5、2-gram、块/版本/引用/审计的 SQL | Contracts + `Microsoft.Data.Sqlite` |
| `src/SDemo.App.Kb.Core` | 版本链、切块、hash、diff、检索路由、refs/resolve、保留、outbox 客户端、OOXML / PDF 解析（OpenXml + PdfPig） | Contracts + Storage |
| `src/SDemo.App.Kb.Mcp` | `kb_*` 工具目录 + JSON-RPC 处理 | Contracts + Core |
| `src/SDemo.App.Kb.Host` | 回环 HTTP（写面 / resolve / MCP）、runtime 文件、CLI | 全部 |
| `src/SDemo.App.Kb.Web` | 查看器 Razor 组件（七屏）、`sdemo-ui.css` 组合 | Contracts + Core |
| `src/SDemo.App.Kb.Viewer` | WPF + BlazorWebView 组合根；内嵌 KbServer、单实例互斥、维护定时器 | Host + Web + Core |
| `tests/SDemo.Kb.Tests` | 版本链 / 检索 / diff / 引用 / MCP / 回环 HTTP / outbox / 保留 | 全部（**不含 Viewer**） |

依赖方向单向：`Contracts ← Storage ← Core ← Mcp / Host`。`Mcp` 不直接碰 SQL；`Host` 不写业务规则。

## 命令

```powershell
dotnet build SDemo.Kb.slnf -c Release
dotnet test  tests\SDemo.Kb.Tests -c Release

# 启动数据面（默认 %APPDATA%\SDemo\Apps\sdemo.kb）
src\SDemo.App.Kb.Host\bin\Release\net10.0\SDemo.Kb.exe
SDemo.Kb.exe --data-dir <目录> --port <端口>
SDemo.Kb.exe --issue-token sdemo.meeting     # 签/轮换 token 并写文件
SDemo.Kb.exe --tokens                        # 列出所有 App token 文件与明文
SDemo.Kb.exe --rebuild-index                 # 整库重建 FTS
SDemo.Kb.exe --maintain                      # 跑一次 TTL / 回收站 / GC
```

运行期连接信息写到 `%LOCALAPPDATA%\SDemo\Hub\apps\sdemo.kb.json`（不含 token）。

### token 分发

- 首次启动（Host 或 Viewer）给 `KbConstants.ProvisionedApps` 里每个 App 签 token，写到
  `%LOCALAPPDATA%\SDemo\Hub\apps\sdemo.kb\tokens\<appId>.token`，内容是 token 明文，一行。
- `sdemo.kb.json` 运行期文件里带 `tokenDirectory` 与 `refsUrl`，App 不需要猜端口。
- 已有文件且仍能通过 `KbService.Authenticate` 校验时不动；文件丢失 / DB 轮换后下次启动重新签发，旧 token 失效。
- `--tokens` 打印 `appId<TAB>路径<TAB>明文`；`--issue-token <appId>` 主动轮换并覆盖文件。
- 安全边界：文件落在 `%LOCALAPPDATA%`，写入时断开 ACL 继承并只授当前用户 FullControl（Windows；非 Windows 退化为 600），不写日志、不进仓；多用户/漫游场景要显式收紧 ACL 与轮换。

### 查看器

```powershell
# 选项 A：查看器内嵌 KbServer；单实例，同一库同时只允许一个 Viewer
src\SDemo.App.Kb.Viewer\bin\Release\net10.0-windows10.0.19041.0\SDemo.Kb.Viewer.exe
SDemo.Kb.Viewer.exe --data-dir <目录>
$env:SDEMO_KB_DATA_DIR = <目录>   # 或环境变量
```

## 契约（代码即契约）

### 写面（不经 Agent、不走 MCP）

| 动词 | 路径 | 说明 |
| --- | --- | --- |
| ingest | `POST /v1/ingest` | 新建；`appId + entityUri + contentHash` 幂等；重复投递不建版本 |
| revise | `POST /v1/revise` | 块级 ops + `expectedVersion`；只有 add / replace / remove |
| promote | `POST /v1/promote` | 改 `retention`；durable → staging 必须 `confirmed=true`；staging 重新算 `expires_at` |
| retract | `POST /v1/retract` | 软删；返回受影响引用清单 |
| refs | `POST /v1/refs` | 外部实体登记引用；`to` 必须带版本；`(from,to,rel)` 幂等 |
| refs/retract | `POST /v1/refs/retract` | 按来源实体撤销登记 |
| refs 查询 | `GET /v1/refs?fromUri=…` | 查该实体登记过的引用；普通 App 只能看自己的 |
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
| `kb_merge` | 中 | 一次 |
| `kb_update` / `kb_delete` | **高** | **总是** |

写面给 App 投影；MCP 写只给 Agent 的块级修正。**禁止整篇正文提交。**

### 数据与 URI

- 表：`kb_doc` `kb_version` `kb_version_parent` `kb_block` `kb_block_edge` `kb_change` `kb_link` `kb_blob` `kb_blob_ref` `kb_fts` `kb_change_fts` `kb_token` `kb_audit` `kb_meta`。
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
- 外部引用登记进 `kb_link` 时 `doc_id=''`；`ReplaceOutboundLinks` 只删 `doc_id=文档` 的行，不会误删外部登记。
- 只把引用存在 App 自己的 `link` 表里，KB 看不到——影响面和引用保护都只成立一半。
- 块 GC 的条件是「无版本边 + 无引用」；只删版本边不删块，历史才读得到。
- Schema 变更必须走 `KbSchema.Migrate` + `PRAGMA user_version`，不能只改 `CREATE TABLE IF NOT EXISTS`。
- Viewer 必须是 `Microsoft.NET.Sdk.Razor` + `UseWPF`，否则 `_framework/blazor.webview.js` 不会随静态资产提供，界面停在「正在启动」。
- 测试工程是 `net10.0`，不能引用 `net10.0-windows` 的 Viewer；UI 只能靠 Viewer 构建 + 人工/截图验收。
- 查看器与 Host 同时打开同一库会各自起 KbServer；当前只用 Viewer 进程内互斥，跨进程单实例留到 P4-f。
- 写面的 `source.appId` 由 token 决定，客户端自报的值只用于比对，不能作为 owner。
- 导入 PDF 只支持文字层；扫描件必须由 Agent OCR 后回写块，KB 不做 OCR。
- `derived_state=pending` 的文档不进检索；Agent 用 `kb_update` 替换占位块后 KB 自动转 ready。
- 导入原件走 `kb_blob` + `kb_blob_ref`；版本被保留策略裁掉时要同时删 `kb_blob_ref`，否则原件永远不可回收。
- 导入 Office 走 OOXML 解析；表格/行摘要的 anchor 是 `sheet!range`，不要只存 sheet 名。
- 测试里用完 SQLite 要 `SqliteConnection.ClearAllPools()`，否则临时目录删不掉。

## 不要读

`obj/`、`bin/`、`TestResults/`、模型/索引缓存；`docs/requirements.md` 只通读一次。



