# SCRUM-11200：样板基线与全页面状态清单

日期：2026-10-22，Pacific/Auckland。依据：[已批准计划 U01](../PLAN.md)与[Jira 原始验收条件](https://yituoxx.atlassian.net/browse/SCRUM-11200)。

本次用户授权：“方案通过。请先执行 SCRUM-11200，建立样板基线和全页面状态清单，沿用 Native 执行方式，保留现有未提交改动。”仅执行 U01，后续迁移与文案任务没有启动。Native 顺序执行，无模型切换；实际模型/effort 无运行时证据，记 UNVERIFIED。未修改产品代码、依赖版本或锁文件，未提交、合并、推送、发布或改变 Jira 状态。

## 交付入口

- [逐项页面状态清单](PAGE_STATE_INVENTORY.md)：140 行，其中 7 行为条件 AI，不计作已实现页面。
- [完整字段 JSON](PAGE_STATE_INVENTORY.json)：每项有路线、PNG/TIFF、条件、源码行号和 SHA-256、迁移/文案任务、优先级、原型、样板、显隐及业务约束和证据状态。
- [可筛选 CSV](PAGE_STATE_INVENTORY.csv)与[清单核对结果](INVENTORY_VALIDATION.json)。
- [冻结候选身份](../../../../output/ui/SCRUM-11200/baseline-20261022/baseline.json)、[文件清单](../../../../output/ui/SCRUM-11200/baseline-20261022/files.json)与[重建/保留核对结果](../../../../output/ui/SCRUM-11200/baseline-20261022/verification.json)。
- [后续交接](HANDOFF.md)。

## 冻结来源

| 来源 | 已核实身份 |
| --- | --- |
| 当前主工作区 | `D:/Repositories/printflow-Studio`，master，`de7bca6e0a8462584b4d6bf4d16307d1335e2fb3` |
| 样板工作树 | `C:/Users/admin/.codex/worktrees/scrum-11173-guided-acceptance/printflow-Studio` |
| 样板分支与基线 | `codex/wpf-ui-sample-pages`，`2561dfd127e3336ec57b37fb3fe47709c1d81fcb` |
| 样板未提交实现 | 13 个受跟踪修改 + 3 个新源码/测试文件；共 16 项，逐项匹配历史 source-manifest SHA-256 |
| 依赖 | WPF-UI 4.3.0；中央包版本、App 项目文件及 App/Tests/WorkstationEntry 三份锁文件已冻结 |
| 主分支差异 | 16 项均与主工作区不同或在主工作区不存在，不能直接以 master 作为已有样板 |
| 原型/矩阵 | 冻结 SCRUM-11174 的设计规范、45 页面索引、三路线、状态显隐和两份 flow-map，以及批准记录和对应 45 张设计图 |
| 历史样板证据 | 原样复制 60 个历史报告/截图/日志/TRX/清单文件，旧目录不改写 |

候选目录为 `output/ui/SCRUM-11200/baseline-20261022`。`overlay/` 只含历史 source-manifest 指定的 16 个文件；`sample.patch` 为这些文件在基线上的受跟踪二进制补丁。无关未提交文件不进入候选。`checkout-bytes/` 只补充基线已跟踪文件的混合 CRLF/LF 字节形式；补充清单的每个哈希都受原始 baseline.json 的完整树哈希约束，不增加产品差异。

重建已在临时目录实际执行：导出基线提交 → 检查/应用补丁 → 复制 16 文件 overlay → 恢复 checkout 字节形式 → 对比全部 2298 个已跟踪文件及 16 个样板文件的 SHA-256。临时目录由 Python TemporaryDirectory 管理，两个真实工作树和 Git 索引不参与写入。候选仍是本地源码基线，不是已构建或已安装产品。

## 验收条件对照

| 原始条件 | 本项结果/证据 |
| --- | --- |
| 覆盖全部具体页面、分支和对话框，无笼统“其他” | PASS：复用45设计页面，补齐正常/空/加载/禁用/错误/确认/恢复/历史/审核，覆盖全部14个 GuidedSessionPage、15种 FinalSaveKind；PNG/TIFF分别列出适用保存状态。设置/环境/错误/最近任务及文件选择、目录选择、修正回导、诊断包保存和业务确认分别登记。 |
| 基线2561dfd及未提交身份，可重建且保留既有工作 | PASS：16文件来源哈希、全树重建、冻结资产清单及两个工作区原有文件/HEAD/分支/索引核对，见 verification.json。自己的新建capture helper不属于用户原有文件，核对中明确排除。 |
| AI按11184/11166实际可用性条件接入 | PASS：两项读回均To Do；当前样板没有可集成AI页面。需求/附件/Prompt/生成/候选/导入分列条件项，业务归原Epic，本次不新建AI能力。 |
| 每项含任务、优先级、原型/样板和证据，未执行标明 | PASS：JSON逐行字段及引用存在性核对；缺少实现样板为NOT RUN；源码定位不冒充产品/视觉验收。 |

三路线来源为 WorkflowCatalog，审核不是新增业务节点。素材路线排除印刷尺寸、白墨、TIFF和多印刷尺寸；GeneratePrintTiff排除美图增强、去背景和默认裁切。源PDF/PSD准备及生成打印TIFF原图审核另列，避免漏掉必要审核。

## 验证与实际限制

本次新验证只针对证据完整性、重建、清单结构与原有改动保护。没有新增业务功能，因此没有运行产品构建或产品测试、没有重新生成截图，也未运行 Photoshop/美图、真实窗口/DPI/物理键鼠/读屏或真人走查。

历史样板的171/171定向通过、构建0警告/错误仅作为封存历史；广泛1503项/35失败的原14项和21项定向复测映射保留在 historical-sample/validation.json。修正后完整广泛套件未重跑。六张实现图为96 DPI UserControl离屏合成素材；PNG图不代表TIFF验收。

对本交付可重新核对（仓库内运行；Python 3.12+）：

```powershell
$pfPython = 'C:/Users/admin/.cache/codex-runtimes/codex-primary-runtime/dependencies/python/python.exe'
& $pfPython docs/codex/printflow-wpfui-rollout/SCRUM-11200/freeze_baseline.py verify
& $pfPython docs/codex/printflow-wpfui-rollout/SCRUM-11200/build_inventory.py
git diff --check -- docs/codex/printflow-wpfui-rollout/SCRUM-11200
```

`verify`不修改来源，只更新本候选verification.json；`build_inventory.py`重新读取样板源码锚点并生成本任务清单。`capture`仅供新目录首次冻结，已有目录拒绝覆盖。重建所需基线Git对象必须在本仓库中，保留核对需要原工作树仍在；整个现有 Git 对象库/外部SDK/NuGet缓存没有打包。

后续若要运行产品验证，应先创建独立候选工作区，按冻结基线和overlay恢复，使用独立的新证据目录。冻结的safe-runner保留11项精确排除及全部Smoke排除；不要从 historical-sample 中原位重跑覆盖旧日志/截图。NuGet locked restore和新构建属于后续实际候选验证，不能从本项清单PASS推断通过。

## 执行记录与判断

1. 已读取用户路由规则。沿用用户指定 Native；未启用自动模型/聊天路由，不声称完成模型切换。
2. 本任务为来源冻结和文档清单，不修改生产代码；以哈希、实际重建和源枚举完整性核对替代不相关产品测试。沿用既有批准方案，不再设计界面。
3. 第一次重建发现历史工作树部分跟踪文本含混合行尾，git archive无法还原其原始字节。补充受原始树哈希约束的checkout-bytes后按字节验证，没有削弱源码断言；原冻结文件清单不覆盖。
4. 首次清单生成发现部分历史原型无hidden字段；明确回落到原设计规范第3节，而非省略显隐要求。
5. 未改变父计划与既有Jira快照，它们记录建单时状态。此目录记录后续U01执行；旧任务与后续Epics不自动关闭。
6. 独立只读审查指出美图接管缺项、HistoryUnknown含义错误及两项页面投影错误。对照真实源码后补齐美图接管确认/处理中，以及全部三路线PSD/PDF源准备的外部适配器接管分支；分清未知历史与已知历史保存，修正Ready/Review投影并删除重复项；原图Source投影仅列两条适用路线，直接TIFF原图审核另列。清单源投影错误会误导后续迁移，按Important修正。针对语义断言先失败、修正后通过，未涉及产品测试。
7. 一次隔离上下文只读审查独立核实132个冻结文件、50个checkout补充、2298个跟踪文件和16个原样板哈希，并核对两个工作区保护状态。修正后对受影响语义再次审查，结论无剩余可操作问题；详情见[REVIEW.md](REVIEW.md)。
