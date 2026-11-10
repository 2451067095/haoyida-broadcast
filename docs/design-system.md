# 设计规范落地（Dashboard Design System）

来源：[bravet/awesome-design-skills](https://github.com/bravet/awesome-design-skills) → `skills/dashboard/SKILL.md` + `DESIGN.md`（MIT，作者 Bergside / typeui.sh）。

原文是一套面向 Web 的设计系统。本项目是 WPF 桌面端，因此做了一次**有记录的移植**：保留 token 与规则，替换渲染技术。

## 1. Token 映射

| 规范 Token | 值 | WPF 资源键 |
| --- | --- | --- |
| primary | `#0C5CAB` | `PrimaryBrush` |
| secondary | `#0a4a8a` | 仅用于渐变，未单独暴露 |
| success | `#10b981` | `OkBrush` |
| warning | `#f59e0b` | `WarnBrush` |
| danger | `#ef4444` | `DangerBrush` |
| surface | `#09090b` | `Surface0Brush` |
| text | `#fafafa` | `TextBrush` |
| radius sm / md | 4 / 8 px | 卡片 12、控件 8、胶囊 999 |
| spacing | 8pt 栅格 | 页面 28/24、卡片 20、控件间距 12/14 |
| 字号阶梯 | 12 / 14 / 16 / 20 / 24 / 32 | `CaptionText` → `DisplayText` |

深色底需要额外的层级色，派生如下（规范允许在文档中说明后扩展）：

| 派生 Token | 值 | 用途 |
| --- | --- | --- |
| Surface1 / 2 / 3 | `#0D0D11` / `#121216` / `#17171D` | 导航栏 / 卡片 / 浮起元素 |
| Stroke / StrokeSoft | `#26262E` / `#1C1C22` | 1px 描边、分隔线 |
| TextDim / TextFaint | `#A1A1AA` / `#7A7A85` | 次级、辅助文字 |
| **Accent** | `#3B9EFF` | 深色底上的可读强调色 |

### 关于 Accent 的说明（可访问性妥协）

规范 primary `#0C5CAB` 在 `#121216` 上的对比度约 **2.9:1**，作为文字色不满足 WCAG 2.2 AA（4.5:1）。
规范本身写明“aesthetics 与 accessibility 冲突时优先 accessibility”，因此：

- **按钮底色**仍用 primary `#0C5CAB` + 白字（对比度约 7.5:1）；
- **文字/图标强调**改用派生 Accent `#3B9EFF`（对比度约 6.3:1）。

## 2. 组件规则落地

- **玻璃质感**：WPF 无 backdrop-filter，用 `CardGradient`（`#191920 → #111116`）+ 1px `Stroke` 描边 + `DropShadowEffect`（blur 28 / opacity 0.45）近似。
- **状态必须显式**：按钮具备 default / hover（opacity 0.88）/ pressed（0.72）/ disabled（0.38）四态；输入框具备 hover 描边与键盘聚焦 Accent 描边。
- **键盘可达**：所有按钮、复选、开关、滑杆使用统一的 `FocusRing`（2px Accent 外描边），Tab 顺序即视觉顺序。
- **空 / 加载 / 错误**：队列为空、日志为空、今日无播报均有占位文案；引擎不可用时红色原因文本；合成失败写日志并在队列项标注。

## 3. 与规范的偏离（均已记录理由）

| 规范 | 实际 | 理由 |
| --- | --- | --- |
| 字体 IBM Plex Sans | `IBM Plex Sans, Segoe UI, Microsoft YaHei UI` 回退链 | 中文界面必须有字形完整的中文字体；系统未装 Plex 时不能出现方块 |
| 44px+ 触控目标 | 桌面端以 36–40px 控件高度为主 | 鼠标/键盘场景，门店收银机分辨率通常 1366×768，需控制密度 |
| 动效 | 仅 Popup 淡入，无装饰性动画 | 规范要求“有目的的动效”；播放状态用文字与颜色表达，避免分散注意力 |

## 4. 视觉走查

`--shots` 会把 7 个页面渲染为 PNG（见 `docs/screenshots/`），用于逐页比对：

1. 层级：每页只有一个 `H1`（页面标题），卡片标题统一 `H3`。
2. 节奏：卡片间距 14px，栅格列间距 14px，与 8pt 基线兼容。
3. 对比：正文 `TextDim` 在卡片底上 ≥ 4.5:1；仅“辅助说明”使用 `TextFaint`（≤ 12px 时不承载关键信息）。
4. 状态色只用于状态：成功/警告/危险三色不做装饰。
