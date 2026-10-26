# 工具箱皮肤规范 v1

皮肤是工具箱核心功能，入口为 **状态 → 皮肤**。它不出现在工具模块目录中。

## 使用与优先级

- 内置暖白、深夜、海风三种皮肤；全局选择会应用到工具箱及跟随全局的模块。
- 每个模块可以单独覆盖。切换全局皮肤时保留这些覆盖；“全部跟随全局”一次清除覆盖。
- 解析顺序：模块覆盖 → 全局皮肤 → 内置暖白。皮肤不支持某个目标时使用内置暖白。
- 导入、导出统一使用 UTF-8 JSON 文件，推荐扩展名 `.faxskin`。导入后不会自动执行皮肤，选择应用后才启用。
- 再次导入同 ID 的皮肤时确认替换；内置皮肤的 ID 不能被覆盖。移除正在使用的导入皮肤时自动恢复默认或全局继承。
- 配置保存到 `%LOCALAPPDATA%\FaxToolbox\skins.json`，并同步到已打开的工具箱窗口。各模块任务状态、模型配置、账号数据不放入皮肤文件。
- “恢复默认”清除选择与覆盖，保留已导入文件。快捷键 **Ctrl+Shift+F12** 由 Windows 宿主处理，会恢复默认并重新加载工具箱界面以清除扩展；即使自定义界面无法使用，也可以恢复。

## 从一个可运行示例开始

复制 [studio.faxskin](../examples/skins/studio.faxskin)，编辑名称、设计变量、布局与交互后，在状态页导入。
[launchpad.faxskin](../examples/skins/launchpad.faxskin) 展示如何用独立 HTML/CSS/JavaScript 完全替换工具箱首页和启动操作。

```sh
node scripts/validate-skin.cjs examples/skins/studio.faxskin
node --test tests/skins.test.cjs
dotnet run --project tests/skins/SkinStore.Tests.csproj -- .
```

完整界面回归测试：

```sh
npm ci --ignore-scripts
npx playwright install chromium
npm run test:skins:ui
```

可用 `FAX_CHROMIUM_EXECUTABLE` 指定已安装的 Chromium 路径。界面测试会实际导入、选择、导出皮肤，检查跨模块隔离、自定义脚本与恢复流程。GitHub Actions 同时验证界面契约和 Windows 项目构建。

[JSON Schema](../schemas/fax-toolbox-skin.schema.json) 可关联到编辑器的 `*.faxskin` 文件。格式版本不兼容时拒绝导入，不猜测字段含义。

## 文件契约

| 字段 | 约束 |
| --- | --- |
| `format` / `version` | 固定为 `fax-toolbox.skin` / 整数 `1` |
| `id` | 稳定身份，`^[a-z][a-z0-9.-]{0,79}$`；更新文件保留 ID |
| `name` / `author` / `description` | 名称必填，最多 60 字符；作者、说明可选，最多 100 / 500 字符 |
| `appearance` | `light` 或 `dark`，同时用于原生宿主明暗模式 |
| `targets` | `shell`、模块 ID，或 `*`；只有支持 `shell` 的皮肤可作为全局皮肤 |
| `tokens` | 完整设计变量，见下表 |
| `layout` | 可选的导航、密度和动效约定 |
| `css` | 可选，作用于当前拥有的界面范围 |
| `bindings` | 可选，声明式事件与已注册操作绑定 |
| `extension` | 可选，在隔离界面中执行自定义 HTML/CSS/JavaScript |

单个皮肤最多 1 MiB，最多导入 32 个，总配置最多 4 MiB。未知顶层字段、无效颜色、非法目标和超限文件会被拒绝。失败的保存不会覆盖已有有效配置。

### 设计变量

`background`、`panel`、`surface`、`text`、`muted`、`accent`、`border`、`navigation` 都是完整的 `#RRGGBB` 颜色。`radius` 是 0–32 的整数，单位为 CSS 像素；`fontFamily` 可选，使用已安装的字体。

这些变量覆盖网页外观、窗口标题栏、浏览器工具栏、宿主提示及安卓投屏原生界面。界面部件使用 `--skin-background`、`--skin-accent` 等同名 CSS 变量；兼容现有的 `--bg`、`--panel`、`--accent` 等变量。原生安卓界面使用 `Skin.background`、`Skin.panel` 等动态资源。

开发者应提供可读的文字与背景对比、键盘焦点、禁用状态和窄屏布局。皮肤不修改第三方网站、系统对话框或投屏画面的内部界面。

### 布局与局部样式

```json
"layout": {"navigation": "top", "density": "compact", "motion": "system"},
"css": ".card { border-style: dashed; } :scope { gap: 12px; }"
```

`navigation` 为 `left`、`top` 或 `right`，只作用于工具箱导航；`density` 为 `comfortable` 或 `compact`；`motion` 为 `system` 或 `reduced`。

自定义 CSS 使用当前范围作为边界，`:scope` 表示拥有的根节点。模块 CSS 不会越过模块根节点；工具箱自定义 CSS 不会进入有独立皮肤的模块范围。自定义声明优先于内置样式。支持普通规则、`@media` 和 `@supports`；禁止网络资源、CSS 转义序列、`@import`、下载字体及全局 at-rule。图片可内嵌到扩展 HTML 的 data URI 中。

### 交互绑定

```json
"bindings": [{
  "event": "keydown", "selector": ":scope", "key": "Alt+1",
  "action": "navigation.home", "args": {}
}]
```

事件支持 `click` 和 `keydown`；快捷键由 `Ctrl`、`Alt`、`Shift`、`Meta` 与一个按键组成。绑定仅在当前可见范围生效，可以重组现有按钮与快捷键的操作逻辑。作用于禁用控件的绑定不会执行。

操作使用注册名，不依赖内部消息桥或函数实现。当前契约包括：

| 操作 | 允许范围 | 参数 |
| --- | --- | --- |
| `navigation.home/modules/activity/settings` | 全部 | `{}` |
| `module.ai/course/homework/proxy/demo/android.open` | 全部 | `{}` |
| `agent.focus`、`agent.new-chat`、`agent.send` | `ai` | `{}` |
| `agent.input` | `ai` | `{"text":"…"}`，最多 4000 字符 |
| `course.pause`、`course.stop` | `course` | `{}` |

以上带 `/` 的写法表示多个独立名称，例如 `module.ai.open`、`module.course.open`。单次操作参数最多 4096 个字符；自定义动作应返回可 JSON 序列化的结果。

## 自定义完整界面与工作流

`extension` 使用独立、无同源权限的 iframe。`mode: "replace"` 替换当前范围的可视界面；`append` 在原界面后追加内容。工具箱全局替换作用于首页、工具目录和工作台；状态、设置及恢复入口由核心保留。模块替换只影响拥有的模块。原始界面节点及事件监听仍保留，原模块任务不会因外观切换而被重新启动。

```json
"extension": {
  "mode": "replace",
  "title": "我的模块工作台",
  "html": "<button id=\"new\">新建对话</button><pre id=\"result\"></pre>",
  "css": "body { padding:24px; font:16px sans-serif; }",
  "actions": ["agent.new-chat"],
  "script": "document.getElementById('new').onclick=()=>ToolboxSkin.invoke('agent.new-chat'); ToolboxSkin.onState(state=>{document.getElementById('result').textContent=state.conversation;});"
}
```

这个例子应使用 `targets: ["ai"]`。`extension.html` 与 `script` 分别最多 128 KiB；脚本作为独立本地 Blob 加载，不在主界面执行。只允许通过声明过的已注册动作调用核心服务，每秒最多 16 次。

扩展 API：

- `ToolboxSkin.version`：接口版本 `1`。
- `ToolboxSkin.getState()`：返回当前只读快照副本。
- `ToolboxSkin.onState(callback)`：立即通知当前状态，返回解除订阅函数。
- `ToolboxSkin.invoke(name, args)`：返回 Promise；未知动作、跨模块动作、非法参数会被拒绝。

快照包含 `scope`、`view`、`skinId`、当前标题、后端连接提示、课程状态；Agent 范围另提供输入草稿及最近可见对话文字。快照不含模型密钥、密码箱、Cookie 或任意磁盘文件。扩展不能访问宿主 DOM、网络、父窗口存储或原始 WebView2 消息桥。扩展脚本加载失败、抛出异常或启动超时会恢复原界面。

## 新模块如何接入

模块完成 DOM 初始化后，注册自己的范围：

```js
const unregister = FaxToolboxSkins.registerScope('my-tool', '我的工具', '#view-my-tool');
const unregisterAction = FaxToolboxSkins.registerAction('my-tool.refresh', {
  scopes: ['my-tool'],
  validate: args => Object.keys(args).length === 0,
  run: () => refreshMyTool()
});
```

注册必须发生在应用皮肤前，或在模块注册后重新导入涉及新动作的皮肤。ID 不重复；模块卸载时调用返回的解除注册函数。新动作保留原有校验、任务生命周期和确认流程，不注册跳过文件写入审批、自动同意操作或暴露账号配置的接口。

原生模块通过 `SkinStore.Resolve(state, moduleId)` 获得自己的设计变量，并以动态资源映射到控件。原生模块不执行 HTML/CSS/JavaScript 扩展；需要完整可编程布局时，模块应暴露自己的 WebView 界面与操作契约。


## 共享控件规范

默认控件由 `design-system.css` 与 `UiResources.xaml` 统一绘制。模块结构放在各自的布局文件；模块不要重复定义全局按钮、字体或浮层颜色。皮肤只通过语义变量覆盖这些角色，原始表单 ID、值、宿主命令、任务状态和确认边界保持稳定。

网页下拉框的真实 `<select>` 保留为业务数据源；可见层提供统一的 combobox/listbox、键盘选择和焦点返回。动态更新选项后调用 `FaxUi.refresh()`。浮层打开前使用 `FaxUi.inherit(source, popup)` 继承来源模块的独立皮肤，不能从外壳浅色/深色状态猜测模块颜色。强调色文字 `--on-accent` 按对比度推导。

原生窗口共用 `Ui.*` 动态资源，使用 `NativeUi.Inherit` 继承所属窗口，使用 `NativeUi.Apply` 应用模块变量。可编辑 ComboBox 必须保留 `PART_EditableTextBox` 与 `PART_Popup`，密码输入必须保留 PasswordBox，不能因视觉模板丢失输入能力。系统文件选择器、第三方网页和手机图像保留平台与内容本身的显示。
