#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
sdemo.kb 界面原型生成器。

用法：
    python build.py

产出 ui-mockups/*.html。样式直接复用 SDemo.UI.Blazor 的设计系统
（sdemo-ui.css 是从 Shared 子仓拷贝的只读副本，别在这里改）。
改版式请改 kb.css；改 token 请改 Shared 子仓并升版本。
"""

import pathlib

HERE = pathlib.Path(__file__).parent

# ── 图标：与 SDemo.UI.Blazor/Components/Icon.razor 同源 ──────────────────
ICONS = {
    "home": "<path d='M3 10.5 12 3l9 7.5'/><path d='M5 9.5V21h14V9.5'/><path d='M9.5 21v-6h5v6'/>",
    "folder": "<path d='M3 7.5A2.5 2.5 0 0 1 5.5 5H9l2 2.5h7.5A2.5 2.5 0 0 1 21 10v7.5a2.5 2.5 0 0 1-2.5 2.5h-13A2.5 2.5 0 0 1 3 17.5z'/>",
    "list": "<path d='M9 6h12M9 12h12M9 18h8'/><path d='M4 6h.01M4 12h.01M4 18h.01'/>",
    "calendar": "<rect x='3' y='5' width='18' height='16' rx='2.5'/><path d='M8 3v4M16 3v4M3 10h18'/>",
    "file": "<path d='M14 3H6.5A2.5 2.5 0 0 0 4 5.5v13A2.5 2.5 0 0 0 6.5 21h11a2.5 2.5 0 0 0 2.5-2.5V9z'/><path d='M14 3v6h6'/><path d='M8 13h8M8 17h5'/>",
    "trash": "<path d='M4 7h16'/><path d='M9 7V4h6v3'/><path d='M6 7l1 13h10l1-13'/><path d='M10 11v5M14 11v5'/>",
    "plus": "<path d='M12 5v14M5 12h14'/>",
    "search": "<circle cx='11' cy='11' r='7'/><path d='m20 20-4-4'/>",
    "check": "<path d='m5 12 4.5 4.5L19 7'/>",
    "clock": "<circle cx='12' cy='12' r='9'/><path d='M12 7v5l3.5 2'/>",
    "target": "<circle cx='12' cy='12' r='9'/><circle cx='12' cy='12' r='5'/><circle cx='12' cy='12' r='1'/>",
    "spark": "<path d='M12 3l1.8 5.2L19 10l-5.2 1.8L12 17l-1.8-5.2L5 10l5.2-1.8z'/>",
    "chevron-right": "<path d='m9 5 7 7-7 7'/>",
    "chevron-down": "<path d='m5 9 7 7 7-7'/>",
    "arrow-right": "<path d='M4 12h16M13 5l7 7-7 7'/>",
    "x": "<path d='M6 6l12 12M18 6 6 18'/>",
    "edit": "<path d='M12 20h9'/><path d='M16.5 3.5a2.1 2.1 0 0 1 3 3L7 19l-4 1 1-4z'/>",
    "more": "<circle cx='5' cy='12' r='1'/><circle cx='12' cy='12' r='1'/><circle cx='19' cy='12' r='1'/>",
    "inbox": "<path d='M4 13h4l1.5 3h5L16 13h4'/><path d='M5 5h14l2 8v4a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2v-4z'/>",
    "layers": "<path d='m12 3 9 5-9 5-9-5z'/><path d='m3 13 9 5 9-5'/><path d='m3 17 9 5 9-5'/>",
    "bell": "<path d='M18 9a6 6 0 1 0-12 0c0 6-3 7-3 7h18s-3-1-3-7'/><path d='M10 21h4'/>",
    "settings": "<circle cx='12' cy='12' r='3'/><path d='M19.4 15a1.7 1.7 0 0 0 .3 1.9l.1.1a2 2 0 1 1-2.8 2.8l-.1-.1a1.7 1.7 0 0 0-1.9-.3 1.7 1.7 0 0 0-1 1.5V21a2 2 0 1 1-4 0v-.1a1.7 1.7 0 0 0-1.1-1.5 1.7 1.7 0 0 0-1.9.3l-.1.1a2 2 0 1 1-2.8-2.8l.1-.1a1.7 1.7 0 0 0 .3-1.9 1.7 1.7 0 0 0-1.5-1H3a2 2 0 1 1 0-4h.1a1.7 1.7 0 0 0 1.5-1.1 1.7 1.7 0 0 0-.3-1.9l-.1-.1a2 2 0 1 1 2.8-2.8l.1.1a1.7 1.7 0 0 0 1.9.3H9a1.7 1.7 0 0 0 1-1.5V3a2 2 0 1 1 4 0v.1a1.7 1.7 0 0 0 1 1.5 1.7 1.7 0 0 0 1.9-.3l.1-.1a2 2 0 1 1 2.8 2.8l-.1.1a1.7 1.7 0 0 0-.3 1.9V9a1.7 1.7 0 0 0 1.5 1H21a2 2 0 1 1 0 4h-.1a1.7 1.7 0 0 0-1.5 1z'/>",
    "sun": "<circle cx='12' cy='12' r='4'/><path d='M12 2v2M12 20v2M4.9 4.9l1.4 1.4M17.7 17.7l1.4 1.4M2 12h2M20 12h2M4.9 19.1l1.4-1.4M17.7 6.3l1.4-1.4'/>",
    "moon": "<path d='M21 12.8A9 9 0 1 1 11.2 3a7 7 0 0 0 9.8 9.8z'/>",
    "info": "<circle cx='12' cy='12' r='9'/><path d='M12 11v5'/><circle cx='12' cy='7.8' r='.9'/>",
    "warning": "<path d='M12 4 21 20H3z'/><path d='M12 10v4'/><circle cx='12' cy='17.4' r='.9'/>",
    "link": "<path d='M10 13a4.5 4.5 0 0 0 6.4 0l2.6-2.6a4.5 4.5 0 0 0-6.4-6.4L11 5.6'/><path d='M14 11a4.5 4.5 0 0 0-6.4 0L5 13.6a4.5 4.5 0 0 0 6.4 6.4L13 18.4'/>",
    "external": "<path d='M14 4h6v6'/><path d='M20 4 11 13'/><path d='M19 14v4.5A2.5 2.5 0 0 1 16.5 21h-11A2.5 2.5 0 0 1 3 18.5v-11A2.5 2.5 0 0 1 5.5 5H10'/>",
    "download": "<path d='M12 3v12'/><path d='m7 10 5 5 5-5'/><path d='M4 20h16'/>",
    "upload": "<path d='M12 16V4'/><path d='m7 9 5-5 5 5'/><path d='M4 20h16'/>",
    "refresh": "<path d='M20 12a8 8 0 1 1-2.3-5.7'/><path d='M20 4v5h-5'/>",
    "filter": "<path d='M3 5h18l-7 8v6l-4 2v-8z'/>",
    "tag": "<path d='M3 12V4h8l9 9-8 8z'/><circle cx='7.5' cy='7.5' r='1.2'/>",
    "lock": "<rect x='4' y='10' width='16' height='11' rx='2.5'/><path d='M8 10V7a4 4 0 0 1 8 0v3'/>",
    "grid": "<rect x='3' y='3' width='7' height='7' rx='1.5'/><rect x='14' y='3' width='7' height='7' rx='1.5'/><rect x='3' y='14' width='7' height='7' rx='1.5'/><rect x='14' y='14' width='7' height='7' rx='1.5'/>",
}


def icon(name, size=16, cls=""):
    cls_attr = f' class="icon {cls}"' if cls else ' class="icon"'
    return (
        f'<svg{cls_attr} width="{size}" height="{size}" viewBox="0 0 24 24" fill="none" '
        "stroke=\"currentColor\" stroke-width=\"1.8\" stroke-linecap=\"round\" "
        f'stroke-linejoin=\"round\" aria-hidden=\"true\">{ICONS[name]}</svg>'
    )


# ── 侧栏 ─────────────────────────────────────────────────────────────────

def nav_item(name, label, active=False, badge=None, icon_name=None):
    cls = "nav-item active" if active else "nav-item"
    ic = icon(icon_name or "file", 16)
    bd = f'<span class="chip-count">{badge}</span>' if badge is not None else ""
    return f'<a class="{cls}" href="#">{ic}{label}{bd}</a>'


def sidebar(active):
    def n(label, ic, name=None, badge=None):
        key = name or label
        return nav_item(key, label, active=(key == active), badge=badge, icon_name=ic)

    return f"""
    <aside class="sidebar">
      <a class="brand" href="#">
        <span class="brand-mark">K<span>.</span></span>
        <span class="brand-copy"><strong>知识库</strong><small>sdemo.kb · 本地</small></span>
      </a>

      <div class="nav-group-label">库</div>
      <div class="nav">
        {n("全部文档", "file")}
        {n("待归档", "inbox", badge="12")}
        {n("检索", "search")}
        {n("导入", "upload")}
      </div>

      <div class="nav-group-label">分类</div>
      <div class="nav">
        {n("需求", "target")}
        {n("会议纪要", "calendar")}
        {n("决策", "check")}
        {n("资料", "folder")}
        {n("笔记", "edit")}
      </div>

      <div class="nav-group-label">其它</div>
      <div class="nav">
        {n("回收站", "trash")}
        {n("设置", "settings")}
      </div>

      <div class="sidebar-spacer"></div>

      <div class="sidebar-note">
        <div class="sidebar-note-icon">{icon("spark", 16)}</div>
        <div>
          <strong>12 条暂存待归档</strong>
          <p>30 天后自动清理；归档后全量保留。</p>
        </div>
      </div>

      <div class="sidebar-foot">
        <div class="avatar">L</div>
        <div class="sidebar-foot-copy">
          <strong>本地知识库</strong>
          <small>v0.1.0 · 2.4 GB</small>
        </div>
        <button class="icon-btn" title="主题">{icon("moon", 16)}</button>
      </div>
    </aside>
    """


def shell(title, desc, actions, body, active, eyebrow="Knowledge Base", theme="dark"):
    theme_attr = ' data-theme="light"' if theme == "light" else ""
    return f"""<!DOCTYPE html>
<html lang="zh-CN"{theme_attr}>
<head>
<meta charset="utf-8">
<title>sdemo.kb · {title}</title>
<link rel="stylesheet" href="sdemo-ui.css">
<link rel="stylesheet" href="kb.css">
</head>
<body>
<div class="app-shell">
  {sidebar(active)}
  <div class="main-column">
    <header class="topbar">
      <div class="topbar-title">
        <span class="eyebrow">{eyebrow}</span>
        <h1>{title}</h1>
        <p class="topbar-desc">{desc}</p>
      </div>
      <div class="topbar-actions">{actions}</div>
    </header>
    {body}
  </div>
</div>
</body>
</html>
"""


SEARCH_TOP = (
    '<label class="search-box">'
    + icon("search", 15)
    + '<input placeholder="搜索文档、块或变更理由">'
    + "<kbd>Ctrl K</kbd>"
    + "</label>"
)

# ── 数据 ─────────────────────────────────────────────────────────────────

DOCS = [
    ("工作区知识库 M3 详细设计", "file", "需求", "durable", "v7", "2 小时前",
     "归档会话 → 索引 → 按需检索；中文 2-gram 预分词，单次检索 ≤800 token。", True, "pinned"),
    ("2026-11-12 周会 · 部署方案评审", "calendar", "纪要", "staging", "v2", "昨天",
     "sdemo.meeting 投影 · 待你确认后归档", False, None),
    ("放弃云端 ASR，v1 全本地", "check", "决策", "durable", "v3", "11-10",
     "隐私与成本权衡；whisper 分发方案另立文档。", False, None),
    ("Q3 预算表.xlsx", "grid", "资料", "durable", "v1", "11-08",
     "sheet 概述 + 12 行摘要 · 原件 486 KB", False, None),
    ("沙箱 ACL 排障记录", "edit", "笔记", "staging", "v4", "3 天前",
     "DesktopAgent 投影 · 90 天后清理", False, None),
    ("150% DPI 下组件偏移复盘", "edit", "笔记", "durable", "v2", "11-05",
     "WM_GETMINMAXINFO 与 DIP 换算；截图 3 张。", False, None),
    ("需求：跨应用引用解析", "target", "需求", "durable", "v12", "11-02",
     "docId@version#block 三段式；影响面反查依赖引用表。", False, None),
    ("部署失败回滚手册.pdf", "file", "资料", "durable", "v1", "10-28",
     "PDF 文字层抽取 · 14 页", False, None),
]


def ret_badge(level):
    if level == "durable":
        return f'<span class="ret ret-durable"><span class="ret-dot"></span>归档</span>'
    if level == "staging":
        return f'<span class="ret ret-staging"><span class="ret-dot"></span>暂存</span>'
    return ""


def doc_list(active_idx=0):
    items = []
    for i, (title, ic, kind, level, ver, when, sub, active, pinned) in enumerate(DOCS):
        cls = "doc-item active" if i == active_idx else "doc-item"
        pin = f'<span class="ret ret-pinned"><span class="ret-dot"></span>引用中</span>' if pinned else ""
        items.append(f"""
        <div class="{cls}">
          <div class="doc-item-title">{icon(ic, 14)}<span>{title}</span></div>
          <div class="doc-item-sub">{sub}</div>
          <div class="doc-item-meta">
            {ret_badge(level)}{pin}
            <span>{kind}</span><span>·</span><span>{ver}</span><span>·</span><span>{when}</span>
          </div>
        </div>""")
    return "".join(items)


def list_column():
    return f"""
    <div class="kb-col kb-col-list">
      <div class="list-search">{icon("search", 14)}<span>搜索本库</span><kbd>Ctrl K</kbd></div>
      <div class="list-chips">
        <span class="chip active">全部<span class="chip-count">1284</span></span>
        <span class="chip">需求<span class="chip-count">96</span></span>
        <span class="chip">纪要<span class="chip-count">212</span></span>
        <span class="chip">决策<span class="chip-count">58</span></span>
      </div>
      <div class="list-group-label">最近更新</div>
      <div class="doc-list">{doc_list()}</div>
    </div>
    """


# ── 01 全部文档 ──────────────────────────────────────────────────────────

LIB_BODY = """
<div class="doc-head">
  <span class="eyebrow">需求 · v7 · 昨天 18:24 修订</span>
  <h2 class="doc-title">工作区知识库 M3 详细设计</h2>
  <div class="doc-meta">
    <span class="ret ret-durable"><span class="ret-dot"></span>归档</span>
    <span class="ret ret-pinned"><span class="ret-dot"></span>被 3 处引用</span>
    <span class="uri-chip">""" + "sdemo.kb/doc/01H9…@v7#b4" + """</span>
    <span class="meta-sep"></span>
    <span>01H9Q3M7</span><span>·</span><span>4 个块</span><span>·</span><span>由 DesktopAgent 修订</span>
  </div>
</div>

<div class="doc-toolbar">
  <button class="btn btn-primary btn-sm">""" + icon("edit", 14) + """编辑块</button>
  <button class="btn btn-ghost btn-sm">""" + icon("clock", 14) + """历史</button>
  <button class="btn btn-ghost btn-sm">""" + icon("link", 14) + """引用 3</button>
  <span class="push-right"></span>
  <button class="btn btn-ghost btn-sm">""" + icon("download", 14) + """导出</button>
  <button class="icon-btn">""" + icon("more", 16) + """</button>
</div>

<div class="body-blocks">
  <div class="blk"><h2>1. 目标与非目标</h2><span class="blk-anchor">#b1 · p.1</span></div>

  <div class="blk">
    <p>把当前工作区里<mark class="hit">已归档</mark>的会话变成可检索的知识，而不是每轮把历史塞进上下文。默认上下文成本为 0；开启后系统提示只多一句「本工作区有 N 条归档会话」。</p>
    <span class="blk-anchor">#b2 · p.1</span>
  </div>

  <div class="blk">
    <p>非目标：不做向量库、不做 embedding、不做跨工作区检索。检索与正文都走工具结果预算：单次检索 ≤800 token，正文按 <code>MidTurnToolResultChars</code> 分页。</p>
    <span class="blk-anchor">#b3 · p.2</span>
  </div>

  <div class="blk blk-changed">
    <h2>2.2 FTS5 索引</h2>
    <p>索引与归档会话放在<strong>同一个工作区 SQLite 文件</strong>里，避免跨库一致性问题。中文连续段按 <code>2-gram</code> 预分词；拉丁字母与数字小写化；标题 ×4、摘要 ×3、正文 ×1 重复写入，用 bm25 的 tf 近似实现「摘要优先」。</p>
    <span class="blk-anchor">#b4 · p.3</span>
  </div>

  <div class="blk">
    <h2>3. 数据模型</h2>
    <p>五个概念：文档 <code>kb_doc</code>、版本 <code>kb_version</code>、块 <code>kb_block</code>、变更 <code>kb_change</code>、引用 <code>kb_link</code>。版本只增不改，编辑 = 新版本 + 新块。</p>
    <span class="blk-anchor">#b5 · p.3</span>
  </div>

  <div class="blk">
    <p>内容寻址：块正文按 <code>content_hash</code> 存一份，版本只引用块 id。五十个版本不是五十倍存储——改一段只多一份块。</p>
    <span class="blk-anchor">#b6 · p.4</span>
  </div>
</div>

<div class="doc-foot">
  <div class="rail-label">相关</div>
  <div class="foot-chips">
    <span class="ref-chip">""" + icon("calendar", 13) + """2026-11-12 周会 · 部署方案评审 <small>@v2#b7</small></span>
    <span class="ref-chip">""" + icon("check", 13) + """放弃云端 ASR，v1 全本地 <small>@v3</small></span>
    <span class="ref-chip">""" + icon("target", 13) + """需求：跨应用引用解析 <small>@v12#b2</small></span>
  </div>
</div>
"""


def page_library(theme="dark"):
    return shell(
        "全部文档",
        "1,284 篇 · 12 条暂存待归档 · 原件 2.4 GB · 索引 41,209 块",
        SEARCH_TOP
        + f'<button class="btn btn-ghost">{icon("upload", 15)}导入</button>'
        + f'<button class="btn btn-primary">{icon("plus", 15)}新建文档</button>',
        f'<main class="content kb-content"><div class="kb-split">{list_column()}<div class="kb-col kb-col-main">{LIB_BODY}</div></div></main>',
        "全部文档",
        theme=theme,
    )


# ── 02 文档详情（含右栏） ────────────────────────────────────────────────

DETAIL_BODY = """
<div class="doc-head">
  <span class="eyebrow">需求 · 最新 v7 · 2026-11-13 18:24</span>
  <h2 class="doc-title">工作区知识库 M3 详细设计</h2>
  <div class="doc-meta">
    <span class="ret ret-durable"><span class="ret-dot"></span>归档</span>
    <span class="ret ret-pinned"><span class="ret-dot"></span>被 3 处引用</span>
    <span class="uri-chip">sdemo.kb/doc/01H9…@v7#b4</span>
    <span class="meta-sep"></span>
    <span>来源：手工撰写</span><span>·</span><span>4 块 · 1.2 KB 文本</span>
  </div>
</div>

<div class="doc-toolbar">
  <button class="btn btn-primary btn-sm">""" + icon("edit", 14) + """编辑块</button>
  <button class="btn btn-ghost btn-sm">""" + icon("clock", 14) + """与 v6 比较</button>
  <button class="btn btn-ghost btn-sm">""" + icon("link", 14) + """引用影响面</button>
  <span class="push-right"></span>
  <span class="muted" style="font-size:10.5px">派生状态</span>
  <span class="status-pill is-success">已就绪</span>
</div>

<div class="body-blocks">
  <div class="blk"><h2>1. 目标与非目标</h2><span class="blk-anchor blk-anchor-always">#b1 · p.1</span></div>
  <div class="blk">
    <p>把当前工作区里已归档的会话变成可检索的知识，而不是每轮把历史塞进上下文。默认上下文成本为 0；开启后系统提示只多一句「本工作区有 N 条归档会话」。</p>
    <span class="blk-anchor blk-anchor-always">#b2 · p.1</span>
  </div>
  <div class="blk">
    <p>非目标：不做向量库、不做 embedding、不做跨工作区检索。检索与正文都走工具结果预算：单次检索 ≤800 token，正文按 <code>MidTurnToolResultChars</code> 分页。</p>
    <span class="blk-anchor blk-anchor-always">#b3 · p.2</span>
  </div>
  <div class="blk blk-changed">
    <h2>2.2 FTS5 索引</h2>
    <p>索引与归档会话放在<strong>同一个工作区 SQLite 文件</strong>里，避免跨库一致性问题。中文连续段按 2-gram 预分词；拉丁字母与数字小写化；标题 ×4、摘要 ×3、正文 ×1 重复写入，用 bm25 的 tf 近似实现「摘要优先」。</p>
    <span class="blk-anchor blk-anchor-always">#b4 · p.3</span>
  </div>
  <div class="blk">
    <h2>3. 数据模型</h2>
    <p>五个概念：文档 kb_doc、版本 kb_version、块 kb_block、变更 kb_change、引用 kb_link。版本只增不改，编辑 = 新版本 + 新块；块正文按 content_hash 存一份，版本只引用块 id。</p>
    <span class="blk-anchor blk-anchor-always">#b5 · p.3</span>
  </div>
  <div class="blk">
    <p>内容寻址让「改一段」的存储代价变成 O(改动量)：五十个版本不是五十倍存储。</p>
    <span class="blk-anchor blk-anchor-always">#b6 · p.4</span>
  </div>
</div>

<div class="doc-foot">
  <div class="rail-label">相关</div>
  <div class="foot-chips">
    <span class="ref-chip">""" + icon("calendar", 13) + """2026-11-12 周会 · 部署方案评审 <small>@v2#b7</small></span>
    <span class="ref-chip">""" + icon("check", 13) + """放弃云端 ASR，v1 全本地 <small>@v3</small></span>
    <span class="ref-chip">""" + icon("target", 13) + """需求：跨应用引用解析 <small>@v12#b2</small></span>
  </div>
</div>
"""

DETAIL_RAIL = """
<div class="rail-block">
  <div class="rail-label">版本链<span class="muted" style="font-size:9.5px">7 版 · 1 分支</span></div>
  <div class="vtimeline">
    <div class="vtl-item current"><span>v7<small> 当前</small></span><span class="vtl-meta">11-13 · 你</span></div>
    <div class="vtl-item branch"><span>v6<small> 分叉</small></span><span class="vtl-meta">11-10 · Agent</span></div>
    <div class="vtl-item"><span>v5</span><span class="vtl-meta">11-08</span></div>
    <div class="vtl-item"><span>v3</span><span class="vtl-meta">11-02</span></div>
    <div class="vtl-item"><span>v2</span><span class="vtl-meta">10-28 · 会议投影</span></div>
    <div class="vtl-item"><span>v1</span><span class="vtl-meta">10-23 · 手工</span></div>
  </div>
</div>

<div class="rail-block">
  <div class="rail-label">谁引用了它</div>
  <div class="reason-item">
    <div class="reason-who">""" + icon("calendar", 13) + """事序 · 待办 3 条</div>
    <div class="reason-text">基于 @v5#b3 的验收标准</div>
  </div>
  <div class="reason-item">
    <div class="reason-who">""" + icon("layers", 13) + """桌面组件 · 今日</div>
    <div class="reason-text">显示 @v7#b1 标题</div>
  </div>
  <div class="reason-item">
    <div class="reason-who" style="color:var(--amber)">""" + icon("warning", 13) + """1 处来源已变更</div>
    <div class="reason-text">待办 PL-07 引用的 #b2 在 v7 已改写，建议复核</div>
  </div>
</div>

<div class="rail-block">
  <div class="rail-label">变更理由</div>
  <div class="reason-item">
    <div class="reason-who">你 <small>11-13 18:24 · v7</small></div>
    <div class="reason-text">把「按行切块」改成「按标题+段落」：行级 diff 在散文里全是噪音。</div>
  </div>
  <div class="reason-item">
    <div class="reason-who">DesktopAgent <small>11-10 · v6 · 分叉</small></div>
    <div class="reason-text">补充 2-gram 分词方案，与 workspace-memory 保持一致。</div>
  </div>
  <div class="reason-item">
    <div class="reason-who">会议投影 <small>10-28 · v2</small></div>
    <div class="reason-text">周会决议：v1 只做 FTS5，向量后置。</div>
  </div>
</div>

<div class="rail-block">
  <div class="rail-label">保留</div>
  <div class="rail-note">归档文档 · 版本全留 · 原件永久<br>最近一次清理检查：无候选</div>
</div>
"""


def page_detail():
    return shell(
        "工作区知识库 M3 详细设计",
        "需求 · v7 · 4 个块 · 由你与 DesktopAgent 共同修订",
        f'<div class="segmented"><button>阅读</button><button class="active">对比</button><button>引用</button></div>'
        + f'<button class="btn btn-ghost">{icon("external", 15)}在 Agent 中打开</button>',
        f'<main class="content kb-content"><div class="kb-split kb-split-wide"><div class="kb-col kb-col-main">{DETAIL_BODY}</div><div class="kb-col kb-col-rail">{DETAIL_RAIL}</div></div></main>',
        "需求",
    )


# ── 03 版本对比 ──────────────────────────────────────────────────────────

DIFF_BODY = """
<div class="doc-head">
  <span class="eyebrow">Block diff</span>
  <h2 class="doc-title">v6 → v7<span class="muted" style="font-size:13px;font-weight:400;margin-left:10px">工作区知识库 M3 详细设计</span></h2>
  <div class="doc-meta">
    <span class="uri-chip">@v6#b2</span><span>→</span><span class="uri-chip">@v7#b4</span>
    <span class="meta-sep"></span>
    <span>2 块新增</span><span>·</span><span>1 块替换</span><span>·</span><span>0 块删除</span>
  </div>
</div>

<div class="doc-toolbar">
  <button class="btn btn-ghost btn-sm">""" + icon("chevron-down", 14) + """选择版本</button>
  <span class="muted" style="font-size:11px">v6</span>
  <span class="varrow">""" + icon("arrow-right", 14) + """</span>
  <span class="muted" style="font-size:11px">v7</span>
  <span class="push-right"></span>
  <button class="btn btn-ghost btn-sm">块级</button>
  <button class="btn btn-ghost btn-sm">词级</button>
</div>

<div class="diff-summary">
  <span class="status-pill is-success">+ 2 块新增</span>
  <span class="status-pill is-warning">~ 1 块替换 · #b2 → #b4</span>
  <span class="status-pill is-muted">4 块复用</span>
  <span class="status-pill is-accent">理由已记录</span>
</div>

<div class="diff-grid">
  <div class="diff-pane">
    <div class="diff-pane-head"><span>v6 · 11-10 20:05</span><span class="muted">DesktopAgent</span></div>
    <div class="diff-body">
      <div class="diff-line"><span class="ln">1</span><span>2.2 FTS5 索引</span></div>
      <div class="diff-line diff-del"><span class="ln">2</span><span>索引与归档会话放在同一个 SQLite 文件里。</span></div>
      <div class="diff-line diff-del"><span class="ln">3</span><span>中文按 2-gram 预分词，标题与摘要加权。</span></div>
      <div class="diff-line"><span class="ln">4</span><span>拉丁字母与数字统一小写化。</span></div>
      <div class="diff-line"><span class="ln">5</span><span>单次检索上限 800 token。</span></div>
    </div>
  </div>
  <div class="diff-pane">
    <div class="diff-pane-head"><span>v7 · 11-13 18:24</span><span class="muted">你</span></div>
    <div class="diff-body">
      <div class="diff-line"><span class="ln">1</span><span>2.2 FTS5 索引</span></div>
      <div class="diff-line diff-add"><span class="ln">2</span><span>索引与归档会话放在<strong>同一个工作区 SQLite 文件</strong>里，避免跨库一致性问题。</span></div>
      <div class="diff-line diff-add"><span class="ln">3</span><span>中文连续段按 2-gram 预分词；标题 ×4、摘要 ×3、正文 ×1。</span></div>
      <div class="diff-line"><span class="ln">4</span><span>拉丁字母与数字统一小写化。</span></div>
      <div class="diff-line"><span class="ln">5</span><span>单次检索上限 800 token。</span></div>
    </div>
  </div>
</div>

<div class="card" style="margin-top:14px">
  <div class="btn-row">
    <span class="status-pill is-muted">4 块复用 · 未变</span>
    <span class="muted" style="font-size:10.5px">两版共享同一 content_hash，不产生新拷贝</span>
    <span class="push-right"></span>
    <button class="text-link">展开复用块 """ + icon("chevron-down", 12) + """</button>
  </div>
</div>

<div class="card" style="margin-top:14px">
  <div class="card-head">
    <div>
      <span class="eyebrow">Rationale</span>
      <h3 style="font-size:14px">这次改动意味着什么</h3>
    </div>
    <span class="status-pill is-accent">user</span>
  </div>
  <p class="muted" style="font-size:12px;line-height:1.75">把「按行切块」改成「按标题 + 段落」：需求文档是散文，行级 diff 全是噪音；整篇又说不清哪一句变了。段落是唯一同时适合人读和机器算的粒度。</p>
</div>
"""


def page_diff():
    return shell(
        "版本对比",
        "块级差异 + 词级高亮 · 变更理由永久保留，即使历史版本被清理",
        f'<button class="btn btn-ghost">{icon("download", 15)}导出 diff</button>'
        + f'<button class="btn btn-primary">{icon("check", 15)}标记已复核</button>',
        f'<main class="content kb-content"><div class="kb-col kb-col-main" style="padding-top:26px">{DIFF_BODY}</div></main>',
        "需求",
        eyebrow="History",
    )


# ── 04 待归档收件箱 ──────────────────────────────────────────────────────

INBOX_ROWS = [
    ("2026-11-12 周会 · 部署方案评审", "sdemo.meeting · 会议纪要", "纪要", "42 KB", "ready", "18 天",
     "is-info", "已识别", True),
    ("局域网握手失败排障", "DesktopAgent · 会话归档", "笔记", "8 KB", "ready", "27 天",
     "is-info", "已识别", True),
    ("回滚流程截图 03.png", "本地导入 · 图片", "资料", "1.2 MB", "pending", "27 天",
     "is-warning", "待识别", False),
    ("Q3 预算表.xlsx", "sdemo.planner · 附件", "资料", "486 KB", "ready", "12 天",
     "is-info", "已派生", False),
    ("周报 2026-W45", "DesktopAgent · 生成草稿", "笔记", "6 KB", "staging", "30 天",
     "is-accent", "AI 生成", False),
    ("客户访谈录音转写", "sdemo.meeting · 转写", "纪要", "310 KB", "pending", "3 天",
     "is-danger", "待识别", False),
]


def inbox_table():
    rows = []
    for title, src, kind, size, state, ttl, pill_cls, pill_txt, checked in INBOX_ROWS:
        rows.append(f"""
        <tr>
          <td><span class="check{' on' if checked else ''}"></span></td>
          <td>
            <div class="cell-title">{title}</div>
            <div class="cell-sub">{src}</div>
          </td>
          <td><span class="status-pill is-muted">{kind}</span></td>
          <td class="num">{size}</td>
          <td><span class="status-pill {pill_cls}">{pill_txt}</span></td>
          <td class="num">{ttl}后清理</td>
          <td style="text-align:right"><button class="icon-btn">{icon("more", 16)}</button></td>
        </tr>""")
    return "".join(rows)


INBOX_BODY = f"""
<div class="page-toolbar">
  <div class="segmented">
    <button class="active">全部 12</button>
    <button>待识别 2</button>
    <button>即将清理 3</button>
  </div>
  <span class="push-right"></span>
  <button class="btn btn-ghost btn-sm">{icon("filter", 14)}来源</button>
  <button class="btn btn-ghost btn-sm">{icon("trash", 14)}清理选中</button>
  <button class="btn btn-primary btn-sm">{icon("check", 14)}归档选中（2）</button>
</div>

<div class="card" style="padding:16px 8px 6px">
  <table class="kbtable">
    <thead>
      <tr>
        <th style="width:26px"></th>
        <th>暂存内容</th>
        <th>类型</th>
        <th>大小</th>
        <th>派生</th>
        <th>保留</th>
        <th></th>
      </tr>
    </thead>
    <tbody>{inbox_table()}</tbody>
  </table>
</div>

<div class="card" style="margin-top:14px;border-color:rgba(251,191,36,.2)">
  <div class="btn-row">
    <span class="status-pill is-warning">{icon("warning", 13)}2 条未识别，归档后才会做 OCR / 图片描述</span>
    <span class="push-right muted" style="font-size:10.5px">暂存内容默认不进 Agent 检索，也不可被引用</span>
  </div>
</div>

<div class="section-block">
  <div class="section-head">
    <div><span class="eyebrow">Rules</span><h3>这些为什么是「暂存」</h3></div>
    <span class="muted">归档是唯一让内容永久保留的动作</span>
  </div>
  <div class="split-2">
    <div class="card">
      <div class="setting-row">
        <div class="setting-copy"><strong>App 自动投影</strong><small>会议纪要、会话归档、附件由 App 主动投递，不等用户。</small></div>
        <span class="status-pill is-info">自动写入</span>
      </div>
      <div class="setting-row">
        <div class="setting-copy"><strong>AI 生成</strong><small>草稿与摘要是「候选」，不自动成为知识。</small></div>
        <span class="status-pill is-accent">候选</span>
      </div>
      <div class="setting-row">
        <div class="setting-copy"><strong>到期清理</strong><small>按 kind 设 TTL，软删进回收站保留 7 天。</small></div>
        <span class="status-pill is-warning">30 天</span>
      </div>
    </div>
    <div class="card">
      <div class="setting-row">
        <div class="setting-copy"><strong>引用保护</strong><small>一旦被引用，无论哪一级都不再参与清理。</small></div>
        <span class="status-pill is-success">强制</span>
      </div>
      <div class="setting-row">
        <div class="setting-copy"><strong>撤销归档</strong><small>归档后仍可降级，但可能造成死链，需二次确认。</small></div>
        <span class="status-pill is-muted">需确认</span>
      </div>
      <div class="setting-row">
        <div class="setting-copy"><strong>批量归档</strong><small>选中多项一次确认，只弹一次。</small></div>
        <span class="status-pill is-muted">无需审批</span>
      </div>
    </div>
  </div>
</div>
"""


def page_inbox():
    return shell(
        "待归档",
        "12 条暂存 · 到期自动清理 · 归档后全量保留并可被引用",
        f'<button class="btn btn-ghost">{icon("refresh", 15)}刷新</button>'
        + f'<button class="btn btn-primary">{icon("check", 15)}全部归档</button>',
        f'<main class="content kb-content"><div class="kb-col kb-col-main">{INBOX_BODY}</div></main>',
        "待归档",
    )


# ── 05 导入 ──────────────────────────────────────────────────────────────

QUEUE = [
    ("工作区知识库 M3 详细设计.docx", "248 KB · docx · 确定性解析", 100, "done", "已完成", "is-success"),
    ("部署失败回滚手册.pdf", "2.4 MB · pdf 文字层", 100, "done", "已完成", "is-success"),
    ("客户访谈录音.mp3", "38 MB · 音频", 42, "warn", "待 ASR", "is-warning"),
    ("架构评审白板.jpg", "3.1 MB · 图片", 12, "warn", "待识别", "is-warning"),
    ("服务端.log", "412 MB · 日志", 0, "", "已跳过", "is-muted"),
]


def queue_rows():
    out = []
    for name, meta, pct, cls, status, pill in QUEUE:
        out.append(f"""
        <div class="queue-row">
          <div>{icon("file", 16)}</div>
          <div class="queue-file"><strong>{name}</strong><small>{meta}</small></div>
          <div class="bar {('is-' + cls) if cls else ''}"><span style="width:{pct}%"></span></div>
          <div><span class="status-pill {pill}">{status}</span></div>
          <button class="icon-btn">{icon("x", 14)}</button>
        </div>""")
    return "".join(out)


IMPORT_BODY = f"""
<div class="dropzone">
  {icon("upload", 26)}
  <strong>拖入文件，或点击选择</strong>
  <small>md / txt / docx / pptx / xlsx / csv / pdf / 图片 · 单文件上限 100 MB</small>
</div>

<div class="btn-row" style="margin-top:10px">
  <span class="status-pill is-success">md · txt · csv 直接入库</span>
  <span class="status-pill is-info">docx / pptx / xlsx / pdf 文字层 · KB 确定性解析</span>
  <span class="status-pill is-warning">扫描件 / 图片 · 交给 Agent 识别</span>
  <span class="status-pill is-muted">log 与源码 · 不进知识库</span>
</div>

<div class="section-block">
  <div class="section-head">
    <div><span class="eyebrow">Queue</span><h3>本次导入</h3></div>
    <span class="muted">2 完成 · 2 待识别 · 1 跳过</span>
  </div>
  <div class="card" style="padding:4px 16px">
    {queue_rows()}
  </div>
</div>

<div class="card" style="margin-top:14px">
  <div class="btn-row">
    <span class="status-pill is-accent">{icon("info", 13)}导入即建 v1，原件按 sha256 去重</span>
    <span class="push-right"></span>
    <span class="muted" style="font-size:10.5px">重复导入同一文件 → 按块 diff 生成新版本；hash 相同则直接跳过</span>
  </div>
</div>
"""


def page_import():
    return shell(
        "导入",
        "解析归 KB，识别归 Agent · 派生未完成的内容暂不进检索面",
        f'<button class="btn btn-ghost">{icon("settings", 15)}导入设置</button>'
        + f'<button class="btn btn-primary">{icon("plus", 15)}选择文件</button>',
        f'<main class="content kb-content"><div class="kb-col kb-col-main">{IMPORT_BODY}</div></main>',
        "导入",
    )


# ── 06 检索 ──────────────────────────────────────────────────────────────

RESULTS = [
    ("工作区知识库 M3 详细设计", "需求", "v7", "@v7#b4", "p.3", "0.86",
     ["2-gram", "分词"],
     "中文连续段按 <mark class=\"hit\">2-gram</mark> 预分词；拉丁字母与数字小写化；标题 ×4、摘要 ×3、正文 ×1 重复写入，用 bm25 的 tf 近似实现「摘要优先」。",
     "标题命中 ×4 · 正文命中 ×1"),
    ("沙箱 ACL 排障记录", "笔记", "v4", "@v4#b2", "—", "0.71",
     ["分词"],
     "…真正的原因是 <mark class=\"hit\">分词</mark> 把整段中文当成了一个 token，导致 FTS 匹配不到两字词…",
     "由 DesktopAgent 投影 · 90 天后清理"),
    ("2026-11-12 周会 · 部署方案评审", "纪要", "v2", "@v2#b7", "12:40", "0.64",
     ["索引"],
     "…决定先把 <mark class=\"hit\">索引</mark> 与主库分开，避免重建索引时锁住会话列表…",
     "暂存内容 · 归档后才可被引用"),
]


def result_rows():
    out = []
    for title, kind, ver, uri, anchor, score, terms, snip, foot in RESULTS:
        chips = "".join(f'<span class="hit-term">{t}</span>' for t in terms)
        out.append(f"""
        <div class="result">
          <div class="result-head">
            {icon("file", 14)}
            <span class="result-title">{title}</span>
            <span class="status-pill is-muted">{kind}</span>
            <span class="status-pill is-muted">{ver}</span>
            <span class="result-score">{score}</span>
          </div>
          <div class="result-snip">{snip}</div>
          <div class="result-foot">
            {chips}
            <span class="uri-chip">{uri}</span>
            <span>·</span><span>{foot}</span>
            <span class="push-right"></span>
            <button class="text-link">打开原文 {icon("external", 12)}</button>
          </div>
        </div>""")
    return "".join(out)


SEARCH_BODY = f"""
<label class="search-box" style="width:100%;height:44px;padding:0 14px;font-size:14px">
  {icon("search", 17)}
  <input value="中文分词">
  <kbd>Enter</kbd>
</label>

<div class="btn-row" style="margin:14px 0 4px">
  <div class="segmented">
    <button class="active">当前态</button>
    <button>历史态</button>
    <button>变更事实</button>
    <button>含暂存</button>
  </div>
  <span class="push-right"></span>
  <span class="muted" style="font-size:10.5px">关键词路由：出现「以前 / 为什么」会自动切到历史或变更面</span>
</div>

<div class="card" style="padding:6px 20px 12px;margin-top:10px">
  {result_rows()}
</div>

<div class="card" style="margin-top:14px">
  <div class="btn-row">
    <span class="status-pill is-warning">{icon("info", 13)}3 条结果来自当前态 · 未命中率 12%</span>
    <span class="push-right"></span>
    <button class="btn btn-ghost btn-sm">{icon("layers", 14)}让 Agent 浏览目录兜底</button>
  </div>
</div>
"""


def page_search():
    return shell(
        "检索",
        "FTS5 + 2-gram · 三个索引面按意图路由 · 每条结果都能说清为什么命中",
        f'<button class="btn btn-ghost">{icon("filter", 15)}过滤</button>'
        + f'<button class="btn btn-primary">{icon("spark", 15)}问 Agent</button>',
        f'<main class="content kb-content"><div class="kb-col kb-col-main">{SEARCH_BODY}</div></main>',
        "检索",
    )


# ── 07 设置 ──────────────────────────────────────────────────────────────

SETTINGS_BODY = """
<div class="stat-grid">
  <div class="stat"><span>文档</span><strong>1,284</strong><small>96 需求 · 212 纪要</small></div>
  <div class="stat"><span>可检索块</span><strong>41,209</strong><small>近 7 天新增 1,204</small></div>
  <div class="stat"><span>原件占用</span><strong>2.4 GB</strong><small>去重后 · 实际 1.9 GB</small></div>
</div>

<div class="card" style="margin-top:16px">
  <div class="card-head"><div><span class="eyebrow">Retention</span><h3 style="font-size:15px">保留策略</h3></div></div>
  <div class="setting-row">
    <div class="setting-copy"><strong>归档文档</strong><small>版本全留；原件永久保留；只有显式删除才进回收站。</small></div>
    <div class="segmented"><button class="active">全留</button><button>留 30 版</button></div>
  </div>
  <div class="setting-row">
    <div class="setting-copy"><strong>暂存内容</strong><small>按来源设定 TTL，到期软删进回收站（保留 7 天）。</small></div>
    <div class="btn-row">
      <span class="chip">会议纪要 30 天</span>
      <span class="chip">会话 90 天</span>
      <span class="chip">图片 30 天</span>
    </div>
  </div>
  <div class="setting-row">
    <div class="setting-copy"><strong>引用保护</strong><small>被任何地方引用的块，在引用解除前不参与清理。</small></div>
    <label class="toggle"><input type="checkbox" checked><span class="toggle-track"></span>始终开启</label>
  </div>
</div>

<div class="card" style="margin-top:14px">
  <div class="card-head">
    <div><span class="eyebrow">Retrieval</span><h3 style="font-size:15px">检索</h3></div>
    <span class="status-pill is-success">FTS5 就绪</span>
  </div>
  <div class="setting-row">
    <div class="setting-copy"><strong>中文分词</strong><small>2-gram 预分词 + 标题 ×4 / 摘要 ×3 / 正文 ×1 加权。</small></div>
    <span class="status-pill is-info">已启用</span>
  </div>
  <div class="setting-row">
    <div class="setting-copy">
      <strong>向量检索<span class="ret ret-staging" style="margin-left:8px"><span class="ret-dot"></span>未启用</span></strong>
      <small>预留：块级向量按 content_hash 复用；由 Agent 计算、KB 只存不算。当前未接入任何模型。</small>
    </div>
    <label class="toggle"><input type="checkbox"><span class="toggle-track"></span>启用</label>
  </div>
  <div class="setting-row">
    <div class="setting-copy"><strong>未命中记录</strong><small>记录 noHit 与命中词，用于判断是否真的需要语义召回。</small></div>
    <span class="muted" style="font-size:11px">近 30 天 148 次</span>
  </div>
</div>

<div class="card" style="margin-top:14px">
  <div class="card-head"><div><span class="eyebrow">Storage</span><h3 style="font-size:15px">存储</h3></div></div>
  <div class="setting-row">
    <div class="setting-copy"><strong>数据目录</strong><small>%APPDATA%\\SDemo\\Apps\\sdemo.kb\\</small></div>
    <button class="btn btn-ghost btn-sm">打开</button>
  </div>
  <div class="setting-row">
    <div class="setting-copy"><strong>回收站</strong><small>3 篇文档 · 释放后可回收 12 MB</small></div>
    <button class="btn btn-ghost btn-sm">查看</button>
  </div>
  <div class="setting-row">
    <div class="setting-copy"><strong>完整性校验</strong><small>重建 FTS 索引；不改动版本链与原件。</small></div>
    <button class="btn btn-ghost btn-sm">重建索引</button>
  </div>
</div>
"""


def page_settings():
    return shell(
        "设置",
        "保留、检索、存储 · 模型相关的能力一律在 Agent 侧，KB 零凭证",
        f'<button class="btn btn-ghost">{icon("download", 15)}导出库</button>'
        + f'<button class="btn btn-primary">{icon("check", 15)}完成</button>',
        f'<main class="content kb-content"><div class="kb-col kb-col-main">{SETTINGS_BODY}</div></main>',
        "设置",
    )


# ── 输出 ─────────────────────────────────────────────────────────────────

PAGES = {
    "01-library.html": page_library(),
    "01-library-light.html": page_library(theme="light"),
    "02-document.html": page_detail(),
    "03-diff.html": page_diff(),
    "04-inbox.html": page_inbox(),
    "05-import.html": page_import(),
    "06-search.html": page_search(),
    "07-settings.html": page_settings(),
}

if __name__ == "__main__":
    for name, html in PAGES.items():
        (HERE / name).write_text(html, encoding="utf-8")
        print(f"wrote {name} ({len(html):,} bytes)")
