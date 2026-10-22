# E6 验收条件与证据边界

原始条件：[JIRA_SCOPE](../../../docs/codex/printflow-operator-ux-backlog/SCRUM-11173_JIRA_SCOPE.json)。本文件记录技术证据；最终运行计数与候选见 [RESULTS](../../../docs/codex/printflow-operator-ux-backlog/SCRUM-11173_RESULTS.md)。`checks/coverage-map.json` 从最终 TRX 展开精确参数化测试名、结果与来源，不用文件存在代替执行。

## Q01 / SCRUM-11195

| AC | 具体覆盖 | 结论边界 |
| --- | --- | --- |
| 1 窗口及缩放 | `GuidedAcceptanceTests.Long_Chinese_source_keeps_image_and_primary_action_visible`、`Multiple_sizes_and_long_Chinese_filename_keep_current_save_target_reachable`；既有 GuidedSessionShell/GuidedProcessingCrop/VisualEvidence 三个 DIP 尺寸 | 1000×700、1280×720、1920×1080 是离屏 UserControl viewport；批准基线是外部窗口1000×700，实际客户区较小且未测；九格窗口/DPI矩阵 NOT RUN，不宣称所有显示器支持 |
| 2 图片/长中文/错误/多尺寸/固定栏 | 上述长中文及真实两尺寸任务；`GuidedProcessingReviewTests.Common_review_reserves_the_majority_for_the_image_and_keeps_save_form_secondary`、`GuidedCompletionSaveTests.Save_destination_and_name_are_visible_in_the_current_step_at_minimum_size`、`GuidedCompletionFeedbackTests.Uncertain_save_and_unavailable_history_remain_separate_visible_facts`；selected crop/correction-waiting/save-uncertain/readiness-blocked PNG | 自动几何只覆盖给定合成状态；真实字体/对话框/屏幕布局仍待实测 |
| 3 Tab/焦点/余波/双击/取消/读屏/隐藏 | 新增 `Current_actions_and_inputs_expose_names_and_hidden_steps_stay_out_of_candidate_tab_set`；既有 `GuidedSessionInputTests.Held_key_release_and_double_click_do_not_activate_new_context`、`Exact_review_provider_and_neutral_focus_follow_the_new_result`、`Hidden_or_unloaded_controls_refuse_retained_invocation`；`GuidedSessionEditorInputTests.Captured_text_input_cannot_edit_a_retired_or_hidden_presentation`；GuidedImportDraft/Route、GuidedCompletionSave 取消与 utility leases | 直接 peer、逻辑焦点和 routed events，不等于 Windows物理输入/UIA客户端/屏幕阅读器；早期 E4 两项按键不稳定根因仍未知 |
| 4 中文截图/英文结构 | PNG/geometry/hash/index；既有 `GuidedThemeResourceTests`、Architecture 资源/绑定约束；原 zh-CN/en 资源保留 | 中文离屏证据；不增加英文人工测试。广泛套件中语言泄漏/旧绑定失败仍报告 |

## Q02 / SCRUM-11196

| AC/合同 | 精确测试入口（参数化实例见 JSON） | 适用范围 |
| --- | --- | --- |
| 1 三路线 | `SessionServiceTests.Full_walkthrough_reaches_Completed_with_a_real_approved_png_on_disk`、`PrepareCustomerDesign_reaches_PhotoshopOutput_and_records_a_PrintOutput`；PhotoshopTiffWorkflowOutput/PhotoshopTiffFinalReview；GuidedSessionProjection/GuidedImportResume/GuidedProcessingPrint | 合成服务与 App 投影；包括旧命名 Production_smoke_A/C 的 fake路径，其名称不代表真实生产；不声称真实 Photoshop/Meitu/打印 |
| 可选跳过、拒绝重试 | `SessionSmokeTests.Smoke_C_skipping_both_skippable_steps_creates_no_revisions`、`SessionControlsTests.Skipping_a_skippable_step_records_it_without_creating_a_revision` 与不可跳过Trim；`RetryAndReviewTests.Reject_then_retry_keeps_the_rejected_Revision_audit_visible_and_produces_a_distinct_approved_one`、`Retry_after_a_fake_failure_gets_a_fresh_attempt_and_working_directory_then_succeeds` | 审核拒绝与重试保留审计，不把跳过等同批准 |
| 返回失效 | `ReturnToStepTests.Returning_upstream_of_a_produced_TIFF_invalidates_the_PrintOutput`、`Returning_upstream_of_the_shared_design_invalidates_both_sibling_outputs`；GuidedSessionNavigation/ReviewFix | 当前/下游身份和共享来源失效 |
| 修正回导 | ColleagueCorrectionTests；`GuidedCompletionRecoveryTests.Correction_prepare_wait_import_and_review_remain_explicit`、`Handoff_reopens_with_only_supported_manual_paths_and_never_restarts` | 支持的 Meitu/同事修正与合法重入；无 Photoshop手工TIFF导回 |
| 重启/恢复 | `StartupRecoveryTests.A_crashed_Running_attempt_recovers_to_Interrupted_and_fabricates_no_Revision`、`A_partial_file_left_by_a_crash_is_quarantined_and_protected_areas_are_untouched`；`GuidedSessionInputTests.Reopened_task_projects_persisted_page_without_starting_an_adapter` | 合成存储/受控服务重载；不是断电、当前host真实重启或generic recovery完整验收 |
| 多尺寸 | `AddAnotherSizeTests.Approving_the_second_size_leaves_both_outputs_valid_and_approved`、`Rejecting_the_second_size_leaves_the_first_output_untouched`；GuidedCompletionSave 各自状态、新增多尺寸保存布局 | 各输出独立审批/保存与共享上游失效 |
| 保存冲突/失败/不确定 | FinalSaveCoordinatorTests；`GuidedCompletionSaveTests.Uncertain_collision_and_picker_cancel_keep_their_real_save_state`、`Saving_is_a_transient_state_and_does_not_claim_delivery_before_acknowledgment`；GuidedSessionSaveOwnership | 合成文件、fake picker/受控故障；不覆盖真实网络盘/系统对话框操作 |
| 2 精确审批 | GuidedSessionInput/EditorInput、FinalSaveCoordinator、PhotoshopTiffFinalReview；`GuidedCompletionFeedbackTests.Refreshed_return_confirmation_cannot_invalidate_results` | 当前会话/修订/哈希/输出身份，不授予过期 provider 权限 |
| 原件/快照 | `SessionServiceTests.Import_produces_an_approved_root_revision_and_a_read_only_snapshot`、`Mutating_an_approved_file_on_disk_invalidates_it_and_refuses_consumption`；HomeInputAcceptance | 合成原件与受管副本不变性 |
| 环境门禁/锁 | `EnvironmentGateTests.SessionService_refuses_a_Production_mode_adapter_before_it_is_ever_invoked`、`SessionServiceTests.A_step_that_needs_the_automation_lock_is_refused_while_another_session_holds_it`；StartupRecovery 死亡/未知锁所有者；GuidedUtilityReturn | 生产模式被拒绝的合成验证，非工作站就绪证明 |
| 生产 TIFF 合同 | PhotoshopTiffWorkflowOutputTests、PhotoshopTiffFinalReviewTests、FlexibleSizeWorkflowTests；GuidedProcessingPrint 两条 TIFF 的明确白墨分支 | 受控编码器/假适配器的尺寸/300 PPI/CMYK/W1契约；F6/N4负例不被掩盖，实际生产输出 NOT RUN |
| 3 已知问题逐项分类 | 下表与 checks 的精确失败比较 | 未经根因与因果修复证明，不写“已确认解决” |
| 4 构建/执行分层 | TRX/log/filter、candidate build/provenance/Validate、visual manifest | 服务/离屏/静态host证据各自独立；实际桌面/新人/生产 NOT RUN |

## 历史问题与本轮新增项

历史源与哈希在 `checks/readonly-reference-hashes.json`；此处没有更新旧 Epic。

| 项目 | 历史事实/本轮判定 | 后续责任与门槛 |
| --- | --- | --- |
| N1 P2 | 旧 Recent Interrupted 与 Session 状态不一致。E5 修了局部 Recent 投影且窄测试通过，但没有关闭全局文化/旧验收；本轮仍是旧问题，不能由新投影覆盖宣称根因已解决 | 原 UI/旧验收负责人；重现原情境与真实工作站后裁定 |
| N2 P3 | 旧诊断预览项暴露 CLR record 名称。新测试检查的是当前 Session控件，不是同一诊断列表；仍旧缺陷/待专项验证 | 诊断 UI 负责人，准确列表与读屏回归 |
| N3 P3 | 旧尺寸长小数可读性；guided summary 的新格式不证明旧入口全覆盖。仍旧缺陷/待原场景验证 | 尺寸 UI 负责人；保留技术精度，验证所有相关呈现 |
| N4 P1待诊断 | 旧 Interactive fake将PNG字节复制成.tif，UI曾允许批准而交付拒绝。当前静态源码仍默认 CopyApprovedInput；只在准备过程显式 ValidTiff，不能推断下一 Interactive进程安全。未执行当前host，旧缺陷仍开放 | host/验证责任人另行诊断；未证明的完整TIFF新手场景 BLOCKED |
| F6 known negative | 200×150 mm最大边界与实际6×5 TIFF不一致。既有负例保留；没有业务层修复 | 工作流/输出合同负责人；实际元数据与解码一致性证明 |
| PNG KeepOriginalExtent审批/绑定 | 旧下游批准/提升与legacy绑定缺口未因常规裁切PNG链通过而解决 | 原业务/绑定任务；相同路径因果证据 |
| generic recovery/历史hang | 有受控重启恢复测试，不等于缺失恢复fixture、generic recovery或历史hang解决；安全排除不变 | 原恢复/host负责人；隔离复现，不取消安全排除 |
| E4早期两个按键激活异常 | 后续定向通过，但根因未知；保留未关闭风险 | 原输入安全责任人；因果复现与实机验证 |
| E5 M1 Minor | 从环境页返回任务时忙碌文案暂称“检查工作站”；本轮没有改变该行为，仍暂缓 | App文案后续；慢速重载时可能误导，实际不运行诊断 |
| E6发现的既有App布局缺陷 | 两尺寸Save在1000×700图片仅1DIP、1280×720约34DIP；TDD RED2/3后将输出历史移至Save/Completed右侧滚动区，保留原审核布局与事实；相关39/39通过，最终证据见RESULTS | 本轮修复的验收发现；初次15/15视觉套件缺图片断言，不能当作该AC通过；真人/设备仍未观察 |

## Q03 / SCRUM-11197

AC1 隔离候选/合成资料/场景准入已准备，静态验证不能替代运行准备；真实路线、审核、裁切、尺寸、保存、继续/恢复均 NOT RUN。AC2 独立完成、求助、误解、原话的12行模板为空。AC3 负责人视觉一致性和问题分级/负责人/回归记录表已备，负责人签收 NOT RUN。AC4 沿用11155 A01–A12，保留旧问题和缺失 discovery，不关闭旧Epic；无发布/部署/生产授权。

完整可填写材料：[中文 walkthrough](../../../docs/printflow/guided-acceptance-walkthrough.md)。没有真人与真实工作站证据时，E6 的技术准备可审阅，整体验收仍部分完成。
