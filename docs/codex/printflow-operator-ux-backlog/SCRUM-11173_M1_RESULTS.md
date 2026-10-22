# SCRUM-11173 M1 候选重建证据保护

用户于2026-10-22要求修复候选重建脚本的轻微问题、沿用Native；真人测试仍稍后安排。本轮仅修复证据辅助脚本，不改产品、业务、host或原候选，不执行PrepareAndSmoke/Interactive。

## 计划与因果检查

1. 对原candidate、最终/修正前截图、原始TRX/log/filter建立103文件SHA-256基线。
2. 在独立合成文件夹中运行实际脚本副本；仅替换固定工作树绑定，构建和两个下游工具用受控夹具代替，避免真实构建、启动或触及原包。先观察失败，再修正证据路径及已有路径拒绝。
3. 完成一次真实隔离构建与静态Validate，复核原103文件不变；顺序实现后做一次新鲜独立范围审查并提交记录。

根因：CandidateId只隔离artifacts，证据始终写入固定candidate目录。修复后新证据位于 `output/ui/SCRUM-11173/candidates/<CandidateId>/`；保留原candidate，已有证据叶目录列入预检且创建叶目录不用Force。失败记录只能留在本次候选路径，不能与旧成功summary混用。

## 当前验证

- 修复前5/5失败：fresh成功、build失败、Validate失败、已有证据拒绝、两候选隔离/同ID重试。
- 修复后5/5通过、退出0，原103文件0变化。最初green断言全通过但夹具遗留LASTEXITCODE=9；测试入口补显式exit0后green-final成功。此为测试入口修正，不是第二个产品缺陷。
- 原始结果见 `output/ui/SCRUM-11173/checks/m1-fix/`；夹具目录留在本机scratch，不混入真实候选身份。
- 当前产品/src/tests/tools与24f2b3b冻结点相同；只需PowerShell文件行为验证，不重跑未变化的UI/工作流套件。

真实隔离构建、静态Validate和新鲜独立审查：待本修复提交后执行；当前记录不先行宣称完成。
