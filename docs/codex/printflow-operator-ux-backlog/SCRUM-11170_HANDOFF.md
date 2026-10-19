# SCRUM-11170 Native 执行交接

用户 2026-10-19：SCRUM-11169 通过，继续推进 SCRUM-11170，沿用已批准视觉与 Native。全局 development-routing v2.4 的 PLAN_EXECUTE 延续，RouteOffset=0；既有完整书面 v2 设计和本轮持续授权覆盖计划后执行，不重复索取设计、计划和方法批准。

## 工作区

本树 C:/Users/admin/.codex/worktrees/scrum-11170-guided-import/printflow-Studio/；分支 codex/scrum-11170-guided-import，基线 55657e37badb406cbc96a0d8db24abe6386d932d。已用原生 create_worktree 创建并附加。旧树属于另一聊天，只读；主检出 D:/Repositories/printflow-Studio 保留全部未提交文档。禁止合并、推送、部署、生产操作、后续 Epic。

精确 Jira 启动需求与状态变更在 SCRUM-11170_JIRA_SCOPE.json。11169 与11178～11181已按用户验收转 Done。最终11170、11182和11183已转In Review；11184保持To Do等待11166依赖。最终状态原始回执见 output/ui/SCRUM-11170/checks/jira-final-status.json；未发送Jira评论，E3尚待用户验收。

## 规范和范围

批准规范和原型只读路径：C:/Users/admin/.codex/worktrees/scrum-11177-guided-theme/printflow-Studio/output/imagegen/SCRUM-11168/2026-10-19-v2/ 下 design-spec.html、index.html、prototype-manifest.json、design-approval.json。本树已有已验收主题和E2壳，复用浅色青绿风格。

E3是开始任务、单文件导入、路线选择与来源确认；I01/11182和I02/11183完整实现。I03/11184只按实际依赖落实不可用入口与不阻塞三路，11166 To Do且本树未具备AI业务，不能移入其他未验收分支功能；完整AI步骤/手动交接接入依赖阻塞，单列BLOCKED_DEPENDENCY。

保留一任务一图、原件/InputSnapshot、合法命令、精确审批、下游失效、三路线、300PPI/CMYK/W1、环境门禁、自动化锁、回收保留本地隐私。素材无印刷白墨TIFF、直接TIFF无美图/默认裁切，TIFF白墨必须显式0/1/2px及理由无默认。仅本Epic必要App视图/导航改变，不重写业务、迁移、AI功能或外部传输。zh-CN/en均维护，中文视觉/人工验收。

## 执行

唯一 Native 执行者 /root/native_e3，请求和运行均 gpt-6-astra/high，父会话核实 turn_context 2026-10-19T04:37:27.245Z，session 01a15273-3593-7c80-af03-4203e055fc1e。控制会话实际Sol/xhigh且无原地切换。规划后及上下文/真实边界复评；可分离非UI若出现先报告真实边界，不把模型标签当切换。任务均围绕首页、路线、来源WPF交互和展示所有权，预计紧密耦合UI。

执行者先brainstorming核对已定设计，writing-plans具体计划 docs/superpowers/plans/2026-10-19-scrum-11170-guided-import.md，自审后按持续授权执行。使用executing-plans/TDD/verification-before-completion，必要时systematic-debugging。各任务顺序RED-GREEN、提交并记录Ruling和成本；只读前序已提交结果，不读写旧计划scratch。最终交回父会话安排唯一新鲜整体审查，再原执行者统一TDD修复重要问题；不派任务级子代理。

## 验证

禁止真实App/Window.Show/ShowDialog、opt-in、桌面输入、真实Meitu/Photoshop、客户文件、生产操作。可使用temp SQLite/files、synthetic GUID、fake adapter/picker及离屏WPF RenderTargetBitmap。先审计测试调用链，继承本树output/ui/SCRUM-11169/checks/safe-ui-exclusions.json的11项排除；E3安全脚本只能写E3路径。不得普通全量dotnet test误开窗口。

E2基线：安全广筛1344过14继承失败；277持久化过；85引导过；精确失败见output/ui/SCRUM-11169/checks/baseline-comparison.json。先运行适当鲜定向基线，最终相关安全广筛，新失败逐项归因，不放宽安全断言、不把偶然不复现称修复、不扩修无关旧缺陷。取消/迟到导入、旧Home与路线UIA/键盘余波、路线锁、PDF/PSD对应性按现实风险检查。

新证据output/ui/SCRUM-11170/：原始TRX、代表性中文离屏页1000x700/1280x720/1920x1080 DIP、逐图view_image、真实命令/几何信息、INDEX.md。真实键盘、多DPI、UIA客户端、新手和生产NOT RUN。E2遗留Tab/Space/推荐字段与旧故障明确保留。

完成前保存RESULTS、AC映射、维护说明、全部Ruling/成本、source diff和测试证据；不要删除scratch，前序清理被策略拒绝。交父HEAD、提交、计数、限制。未获用户验收的E3不得Done。
