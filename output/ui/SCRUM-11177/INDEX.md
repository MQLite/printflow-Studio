# SCRUM-11177 离屏视觉证据索引

2026-10-19；中文；全部为真实 WPF 离屏渲染、96 DPI。视口单位为 DIP，在此捕获中一 DIP 对应一像素。21 张页面/组件图已逐项用本地图片工具检查；合成源文件不计入21张。

原型对照：Home 对应 [01-home.png](../../imagegen/SCRUM-11168/2026-10-19-v2/01-home.png)；WorkflowSelection 对应 [02-route.png](../../imagegen/SCRUM-11168/2026-10-19-v2/02-route.png)；组件依照 [设计规范 §4–6](../../imagegen/SCRUM-11168/2026-10-19-v2/design-spec.html)。原型材料只读保留，不在本证据提交中复制。

本轮对照的是变量、表面、字体、状态、可读性与可达性。原型里的完整侧栏、流程重组和选择后确认属于后续 Epic；当前两实页继续使用原有交互。

Home 的1000/1280图为滚动至导入按钮后的状态；上部环境内容在滚动区外属于滚动位置，不是被隐藏。1920图显示完整上区。路线图从列表顶部开始，长路线通过滚动阅读；固定返回动作完整可见。

组件 normal/disabled/error/long-text 的中文、步骤、提示与操作栏清晰；loading 的小窗口内容区可滚动，停止按钮始终在底栏。不定进度属性已验证，静态离屏帧不是动画或百分比证据。宽屏留白是组件合成布局，不代表完整生产工作区。

每张图旁的 JSON 包含状态、DIP、DPI、命令可用状态、布局边界与截图哈希。页面共用 [synthetic-source.png](synthetic-source.png)：12×10像素透明边缘/5×5不透明色块，SHA-256 `7C0C301F48992E292768A1156278CEAD3BF05829A792C53A80194E1F8981113B`；导入前后哈希一致。组件不使用图像源，SourceSha256 为 null。

| 状态 | DIP | 图像 | 测量 | 人工图像检查 |
| --- | --- | --- | --- | --- |
| components-disabled | 1000×700 | [PNG](components-disabled-1000x700.png) | [JSON](components-disabled-1000x700.json) | 已检查：文字/状态/滚动边界/主操作 |
| components-disabled | 1280×720 | [PNG](components-disabled-1280x720.png) | [JSON](components-disabled-1280x720.json) | 已检查：文字/状态/滚动边界/主操作 |
| components-disabled | 1920×1080 | [PNG](components-disabled-1920x1080.png) | [JSON](components-disabled-1920x1080.json) | 已检查：文字/状态/滚动边界/主操作 |
| components-error | 1000×700 | [PNG](components-error-1000x700.png) | [JSON](components-error-1000x700.json) | 已检查：文字/状态/滚动边界/主操作 |
| components-error | 1280×720 | [PNG](components-error-1280x720.png) | [JSON](components-error-1280x720.json) | 已检查：文字/状态/滚动边界/主操作 |
| components-error | 1920×1080 | [PNG](components-error-1920x1080.png) | [JSON](components-error-1920x1080.json) | 已检查：文字/状态/滚动边界/主操作 |
| components-loading | 1000×700 | [PNG](components-loading-1000x700.png) | [JSON](components-loading-1000x700.json) | 已检查：文字/状态/滚动边界/主操作 |
| components-loading | 1280×720 | [PNG](components-loading-1280x720.png) | [JSON](components-loading-1280x720.json) | 已检查：文字/状态/滚动边界/主操作 |
| components-loading | 1920×1080 | [PNG](components-loading-1920x1080.png) | [JSON](components-loading-1920x1080.json) | 已检查：文字/状态/滚动边界/主操作 |
| components-long-text | 1000×700 | [PNG](components-long-text-1000x700.png) | [JSON](components-long-text-1000x700.json) | 已检查：文字/状态/滚动边界/主操作 |
| components-long-text | 1280×720 | [PNG](components-long-text-1280x720.png) | [JSON](components-long-text-1280x720.json) | 已检查：文字/状态/滚动边界/主操作 |
| components-long-text | 1920×1080 | [PNG](components-long-text-1920x1080.png) | [JSON](components-long-text-1920x1080.json) | 已检查：文字/状态/滚动边界/主操作 |
| components-normal | 1000×700 | [PNG](components-normal-1000x700.png) | [JSON](components-normal-1000x700.json) | 已检查：文字/状态/滚动边界/主操作 |
| components-normal | 1280×720 | [PNG](components-normal-1280x720.png) | [JSON](components-normal-1280x720.json) | 已检查：文字/状态/滚动边界/主操作 |
| components-normal | 1920×1080 | [PNG](components-normal-1920x1080.png) | [JSON](components-normal-1920x1080.json) | 已检查：文字/状态/滚动边界/主操作 |
| home | 1000×700 | [PNG](home-1000x700.png) | [JSON](home-1000x700.json) | 已检查：文字/状态/滚动边界/主操作 |
| home | 1280×720 | [PNG](home-1280x720.png) | [JSON](home-1280x720.json) | 已检查：文字/状态/滚动边界/主操作 |
| home | 1920×1080 | [PNG](home-1920x1080.png) | [JSON](home-1920x1080.json) | 已检查：文字/状态/滚动边界/主操作 |
| workflow-selection | 1000×700 | [PNG](workflow-selection-1000x700.png) | [JSON](workflow-selection-1000x700.json) | 已检查：文字/状态/滚动边界/主操作 |
| workflow-selection | 1280×720 | [PNG](workflow-selection-1280x720.png) | [JSON](workflow-selection-1280x720.json) | 已检查：文字/状态/滚动边界/主操作 |
| workflow-selection | 1920×1080 | [PNG](workflow-selection-1920x1080.png) | [JSON](workflow-selection-1920x1080.json) | 已检查：文字/状态/滚动边界/主操作 |

NOT RUN：物理键盘焦点、屏幕阅读器、真实100%/125%/150%窗口缩放和跨显示器、中文新手、英文人工走查、工作站/生产应用/客户数据。焦点模板与合成事件测试不等同于这些验证。

回归原始记录见 [checks](checks/)；精确基线差异见 [baseline-comparison.json](checks/baseline-comparison.json)。
