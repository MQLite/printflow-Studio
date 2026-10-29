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

## 真实隔离构建与保留验证

修复提交 `73caeb26d5c8611aa5b95e3e879f22a45a28b3da` 后运行 `-CandidateId e6-20261022-m1-fixed`：构建退出0、0警告/0错误，Validate退出0且STATIC_VALIDATED、GraphConstructed=false、runtime root不存在。新证据写入[candidates/e6-20261022-m1-fixed](../../../output/ui/SCRUM-11173/candidates/e6-20261022-m1-fixed/validation-summary.json)，未替换既有验收候选。

重复同ID调用在构建前拒绝，新证据0变化；原103文件0变化。原/新候选清单各863个文件哈希全部匹配。[直接核验](../../../output/ui/SCRUM-11173/checks/m1-fix/real-verification.json)保留结果。只构建与静态校验；没有运行App、PrepareAndSmoke、Interactive、真人或生产。

Native由控制会话顺序实现；实际gpt-6.1-sol/high，turn_context2026-10-22T01:22:50.675Z已核实。规则v2.4，RouteOffset=0；非UI证据保护修复及测试策略采用Sol High能力；无原位切换，未宣称切换至其他模型。最后一次新鲜[独立范围审查](SCRUM-11173_M1_REVIEW.md)已PASS：0 Critical、0 Important、0 Minor，并独立复核103原文件及两组863哈希。无产品/src/tests/tools变更，因此不重跑之前45/1494/277套件。

**M1已解决。** 原candidate目录（含历史README）、原始日志/TRX/filter、最终与修正前图片均保留原字节。旧README内“暂缓M1”的文字是当时快照，当前使用说明见[新候选证据指南](../../../output/ui/SCRUM-11173/candidates/README.md)。原E6整体仍部分完成，真人和实机验收稍后安排。分支保留，未推送、合并或部署。
