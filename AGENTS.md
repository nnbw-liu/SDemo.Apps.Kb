# AGENTS · sdemo.kb 子仓

> 路由文件。跨产品文档在 `../../docs/`（相对本仓根）。

## 职责

文档实体 + 不可变版本链 + 块级内容寻址 + 差异与引用追溯；只存只找，不持有模型凭证。

## 现状（2026-10-31）

🚧 未开工：只有文档，`src/` 还不存在。需求计划见 [docs/requirements.md](docs/requirements.md)。

## 开工先做三件事

1. 按五件套建工程：`src/SDemo.App.Kb.{Contracts,Storage,Core,Mcp,Host}`。
2. 建 `SDemo.Kb.slnf`；在父仓 `workspaces/kb.json` 把 `status` 改 `active`。
3. 把 [docs/kb-versioning.md](docs/kb-versioning.md) 的 §10 工具面草案落成真实签名，再换掉本文件。

## 必读

| 改什么 | 读 |
| --- | --- |
| 需求与验收 | [docs/requirements.md](docs/requirements.md) |
| 版本链 / 块 / diff / 索引面 | [docs/kb-versioning.md](docs/kb-versioning.md) |
| 跨应用引用 / 统一搜索 | [../../docs/integration.md](../../docs/integration.md) |
| 组件与宿主 | [../../docs/contracts/](../../docs/contracts/) |

## 命令（建好工程后启用）

```powershell
dotnet build SDemo.Kb.slnf -c Release
dotnet test  tests -c Release
```

## 铁律

1. **零模型凭证**：不引入 `IChatClient` / `ApiKey`；回答由 DesktopAgent 生成。
2. **版本不可变**：`doc_version` 只增不改；编辑 = 新版本 + 新块。
3. **块级操作**：MCP 只暴露块级更新（`kb_update_block`），禁止整篇覆盖。
4. **引用带版本**：`docId@version#block`，不许只存 `docId`（否则历史引用会漂）。
5. **删除软删 + 保留策略**：requirement 全留、笔记留 30 版；回收站 7 天。

## 常见坑

- 中文检索必须用 2-gram 预分词 + FTS5；`unicode61` 会把整段中文当一个 token，`trigram` 不支持两字查询。
- 块 hash 要做内容归一化（空白/换行），否则"只改格式"也会生成新块。
- 导入 PDF/docx 的元素顺序会丢，导入即建 `v1`，不要试图还原原文档结构。
- 影响面反查依赖引用表，`kb_delete` 前必须先扫引用。

## 不要读

`obj/`、`bin/`、`TestResults/`、模型/索引缓存；`docs/requirements.md` 只通读一次。
