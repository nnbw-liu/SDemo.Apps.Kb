# sdemo.kb 界面原型

静态 HTML 原型 + 截图，用来看设计，不是可运行的应用界面。

## 文件

| 文件 | 说明 |
| --- | --- |
| `build.py` | 原型生成器（唯一需要手改的地方是它和 `kb.css`） |
| `kb.css` | KB 特有的版式补充（三栏、块锚点、版本链、diff、收件箱表格） |
| `sdemo-ui.css` | **只读副本**，来自 `Shared/UI/SDemo.UI.Blazor/wwwroot/css/sdemo-ui.css` |
| `01-library.html` / `.png` | 全部文档（深色） |
| `01-library-light.html` / `.png` | 全部文档（浅色） |
| `02-document.html` / `.png` | 文档详情 + 版本链 / 引用 / 理由 |
| `03-diff.html` / `.png` | 版本对比 |
| `04-inbox.html` / `.png` | 待归档收件箱 |
| `05-import.html` / `.png` | 导入与派生状态 |
| `06-search.html` / `.png` | 检索结果与命中理由 |
| `07-settings.html` / `.png` | 保留 / 检索 / 存储 |

设计说明见 [../kb-ui.md](../kb-ui.md)。

## 重新生成

```powershell
cd Kb\docs\ui-mockups
python build.py
```

## 重新截图

Chrome 无头模式，视口 1440×900：

```powershell
$chrome = "C:\Program Files\Google\Chrome\Application\chrome.exe"
$dir = (Resolve-Path .).Path -replace '\\','/'
$tmp = Join-Path $env:TEMP "kb-shot-profile"
foreach ($p in "01-library","01-library-light","02-document","03-diff","04-inbox","05-import","06-search","07-settings") {
  & $chrome --headless=new --disable-gpu --hide-scrollbars --no-first-run `
    --user-data-dir="$tmp" --force-device-scale-factor=1 --window-size=1440,900 `
    --virtual-time-budget=2500 --screenshot="$dir/$p.png" "file:///$dir/$p.html"
}
```

## 约束

1. **不要在这里改设计 token**。要改就改 `Shared` 子仓并升版本，然后重新拷 `sdemo-ui.css` 过来。
2. `kb.css` 里只允许用 `sdemo-ui.css` 定义的 `--*` 变量；浅色主题用 `[data-theme="light"]` 覆盖，不写两套组件样式。
3. 原型只表达**版式与状态**，交互是死的；真实实现走 Blazor + 设计系统的 `AppShell` / `Modal` / `SelectBox` / `ToastHost`。
