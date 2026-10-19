# SCRUM-11169 验收入口

状态：SCRUM-11169、11178～11181均为 **In Review**。实现源提交aa3a3eb，提交后验证ea28185；其后控制会话只补文档、原始验证与Jira状态记录。分支 `codex/scrum-11169-guided-shell`。未合并/推送/部署，后续Epic未启动。

当前主图位于 **review-renders/**：13种状态×1000×700、1280×720、1920×1080 DIP，均96DPI。它们是实际WPF离屏截图，包含明确的合成处理模式提示。

- [当前审核页](review-renders/review-required-1280x720.png)
- [白墨明确选择](review-renders/white-ink-explicit-1000x700.png)
- [只读历史](review-renders/history-readonly-1000x700.png)
- [返回重做确认](review-renders/return-confirmation-1000x700.png)
- [草稿离开确认](review-renders/draft-leave-1000x700.png)
- [处理中停止与接管](review-renders/processing-stop-takeover-1000x700.png)

完整业务/代码/测试映射、裁定与限制见 [Results](../../../docs/codex/printflow-operator-ux-backlog/SCRUM-11169_RESULTS.md)，唯一整体审查与一次TDD修复处置见 [Review](../../../docs/codex/printflow-operator-ux-backlog/SCRUM-11169_REVIEW.md)。

验证：提交后重新编译GuidedSession **85/85**；控制会话独立离屏行为复验 **82/82**；流程/持久化 **277/277**；安全UI/Architecture集合 **1344通过、14继承失败**，名称与消息均同E1。显式排除11项，不能称全量通过。

原renders及16张补充审核图保留为审查前证据。新39张主图中37张字节相同，2张1920模态图的历史入口改为禁用；不要把重复图片累计成人工验收次数。源码/图片哈希、对应JSON、53原动作合同与原始TRX均在本目录，控制会话核验见checks/controller-final-audit.json。

保留限制：一次可见Tab焦点失败、一次Space新输入未激活尚未归因；1项Minor是部分JSON在命令null时误记Recommended=true，实际按钮样式不受影响。真实键盘/UIA客户端/DPI/新手/生产未验证。首轮误执行3个可见测试窗口均关闭，后续过滤已排除；不是全程离屏执行，也不是授权的人机验收。

