# PrintFlow operator terminology (zh-CN / en)

Status: active operator reference; SCRUM-11146 purpose-card wording added 2026-09-23; SCRUM-11145 final-save wording added 2026-09-24; SCRUM-11149 review-guidance wording added 2026-09-29; SCRUM-11150 print-size guidance added 2026-09-29; SCRUM-11151 failure-notice wording added 2026-09-29

Scope: beginner-facing operator copy; documentation only

Planning item: SCRUM-11140 / PF-OPUX-v1-glossary-decision

This reference keeps operator wording consistent without changing domain enums, failure codes, adapter identifiers, persisted values, file rules, or command legality. It is not an approval gate and blocks no other work. Meanings below come from the current product contract and the Wave 1 clarification; they do not add delivery evidence or new business rules.

## Treatment and allowed surfaces

- **Keep**: use the bilingual wording consistently on normal operator screens.
- **Rename in operator copy**: prefer the listed beginner-facing wording when a touched surface is updated. Existing untouched resources may retain their current wording until their own scoped copy task.
- **Technical details only**: keep off the primary instruction or status. It may appear in Error details, Production details, technical expanders, local audit/history, or diagnostic export where named below.

Internal English names may remain in code, persisted state, audit records, and tests. File-system area names such as `Working` and `Approved` remain exact technical names when a technical location must be shown.

## State and file meaning

| Concept | English operator wording | 简体中文操作员用语 | Treatment | Meaning and current references |
|---|---|---|---|---|
| Result generated | Result generated | 已生成结果 | Keep | Automation produced and validated a candidate Revision. It may still require review and may still be in `Working`. A generated result is not automatically approved, completed, or delivered. `StepState.ReviewRequired`; Session output list. |
| Waiting for review | Waiting for your review | 等待你检查 | Keep | A validated result awaits a human decision. Approval or rejection has not occurred, so this is not a finished or delivered file. Wave 1 resource: `Session_StatusReview`; state: `StepState.ReviewRequired`. |
| Approved | Approved | 已通过审核 | Keep with the qualifier where ambiguity is possible | The exact current result passed review, bound to its recorded identity/hash. For managed PNG/TIFF output, the internal file may be promoted to the `Approved` area. Approval is not evidence that a file was saved or delivered to a chosen external folder. `StepState.Approved`; `WorkspaceArea.Approved`. |
| Workflow completed | Completed | 已完成 | Keep with the qualifier where delivery could be inferred | All required outputs passed final review and the session reached `SessionState.Completed`. Completion is not delivery evidence. Wave 1 resource: `Session_StatusCompleted`. |
| Delivered / saved to chosen folder | Saved to your chosen folder | 已保存到你选择的文件夹 | Keep, but only when supported by explicit delivery evidence | This wording requires a successful delivery/export record for the exact approved result and destination. Never infer it from `Approved`, a file in the internal `Approved` area, or `Completed`. SCRUM-11145 shows it only after a verified delivery or a fresh verified check; an unverified or unreadable record is stated as such (`FinalSave_Uncertain`, `FinalSave_SavedPreviously`, `FinalSave_HistoryUnavailable`). |
| Internal Working area | Working area | Working 工作区 | Technical details only | Managed temporary or review-pending files used by processing. Allowed in Production details, Error details, technical location expanders, audit/history, and diagnostic export. Current resource: `OutputLocation_Working`. |
| Internal Approved area | Approved area | Approved 已审核区 | Technical details only | Internal managed location for approved PNG/TIFF results. Its name does not mean external delivery. Allowed on the same technical surfaces as Working. Current resource: `OutputLocation_Approved`. |
| Rejected result / Recycle Bin | Rejected result / sent to the Recycle Bin | 已拒绝的结果 / 已移入回收站 | Keep | A rejected generated TIFF follows the existing recycle rule. Do not imply the bytes remain available. Current resource: `OutputLocation_Recycled`. |
| Revision | Result version | 结果版本 | Rename in operator copy | Immutable identity of a validated result in the internal model. Use “Revision” only in Error details, Production details, technical expanders, audit/history, and diagnostic export. Current resources: `Session_LabelRevision`, background-removal authority/audit text. |
| SHA-256 / hash | File identity | 文件标识 | Technical details only | Exact content identity used to bind review and approval. SHA-256 may remain in Production details, Error details, technical expanders, audit/history, and diagnostic export. Current resources: `Session_LabelHash`, `Session_TiffLabelHash`. |

The generated → waiting for review → approved → completed sequence describes processing authority. “Saved to your chosen folder” is a separate delivery fact and must never be added to that sequence without delivery evidence.

## Routes, steps, status, and actions

| Concept | English operator wording | 简体中文操作员用语 | Treatment | Meaning and current references |
|---|---|---|---|---|
| Workflow choice | Choose a route | 选择处理路线 | Rename in beginner guidance | One of the three fixed workflows. The persisted/internal term remains `WorkflowType`. Existing screen heading still uses “workflow/流程”; SCRUM-11146 cards now explain purpose, approved output and print-size requirements. Enhancement/background removal are optional; approved output does not mean chosen-folder delivery. |
| Prepare Design Asset | Prepare Design Asset | 准备设计素材 | Keep | Produces an approved transparent PNG and has no print-size or TIFF step. `Workflow_PrepareAsset`. |
| Prepare Customer Design | Prepare Customer Design | 处理客户设计 | Keep | Processes a customer-composed design through review, trim, dimensions, production TIFF, and final review. `Workflow_PrepareCustomerDesign`. |
| Generate Print TIFF | Generate Print TIFF | 生成打印 TIFF | Keep | Starts from a shop-created finished design, then dimensions, production TIFF, and final review. `Workflow_GeneratePrintTiff`. |
| Session | Processing job | 处理任务 | Rename in operator copy | One single-image processing flow. “Session” may remain in Error details, audit/history, and diagnostic export. Domain type: `ProcessingSession`. |
| Start step | Start this step | 开始此步骤 | Rename in operator copy | Starts the currently legal step. Current resource remains `Session_RunStep` (“Run step” / “执行步骤”) until its scoped copy change. |
| Processing | Processing automatically | 正在自动处理 | Keep | An attempt is running. It makes no claim that a result exists. `StepState.Processing`; Wave 1 resource: `Session_StatusProcessing`. |
| Needs input | Needs your input | 需要你操作 | Keep | PrintFlow is waiting for a currently offered operator action, such as confirming the original or setting dimensions. The specific next action must come from existing command availability. Wave 1 resource: `Session_StatusInput`. |
| Stopped / failed | Stopped or failed | 已停止或失败 | Keep | Automation did not produce a valid result or requires a new attempt. Retry is named only when currently legal. `Failed`, `Interrupted`, `RetryRequired`; Wave 1 resource: `Session_StatusStopped`. |
| Handed off | Handed off | 已移交 | Keep | Automation ended and control was transferred. It does not mean the work is complete or that a corrected file was returned. `SessionState.HandedOff`; Wave 1 resource: `Session_StatusHandedOff`. SCRUM-11148 aligned the Recent state value `SessionState_HandedOff` (zh-CN 已转交 → 已移交). |
| Approve | Approve | 通过 | Keep | Accepts the exact displayed current result. For final TIFF use “Approve this TIFF” / “批准此 TIFF”. Current resources: `Session_Approve`, `Session_ApproveTiff`. |
| Reject | Reject | 驳回 | Keep | Rejects the exact displayed result and follows current retry/regeneration rules. For final TIFF use “Reject this TIFF” / “驳回此 TIFF”; recycling precedes the recorded rejection, and a new TIFF requires the separate Run step. Current resources: `Session_Reject`, `Session_RejectTiff`. |
| Retry | Retry | 重试 | Keep | Starts a new attempt only when the workflow currently offers Retry. It does not resume a partially completed external-application action. `Session_Retry`. |
| Stop | Stop | 停止 | Keep | Requests the current external-application automation to stop through its existing behavior. The application-specific hint and stopping notice explain what happens; the button label and command legality stay unchanged. Current resource: `Session_Stop`; Wave 1 Unit A. |
| Take over in Meitu | Take Over in Meitu | 在美图秀秀中手动接管 | Keep with application name | Ends PrintFlow control of the current Meitu attempt and leaves Meitu as it is. It does not create a result version and is not completion. Current resources: `Session_TakeOver` and its confirmation/guidance keys. |
| Take over in Photoshop | Take Over in Photoshop | 在 Photoshop 中接管 | Keep with application name | Ends PrintFlow control of the current Photoshop attempt and leaves Photoshop as it is. Photoshop may still be working. A manually made result, including a TIFF, cannot be imported for these steps. Wave 1 resources: `Session_PhotoshopTakeOver`, `Session_PhotoshopTakeOverConfirmQuestion`, and related guidance keys. |
| Return to automation | Return to automation | 恢复自动处理 | Keep | After a supported handoff, when this action is offered, it returns the job to active processing and makes a new attempt available; it does not start that attempt or resume the handed-off attempt. The operator separately chooses Run step, which starts the new attempt from a fresh working copy. Current resource: `Session_ReenterAutomation`; transition: `WorkflowEngine.ReenterAutomation`. For Photoshop handoff, follow the application-specific guidance and do not promise a manual-result return. |
| Manual result | Submit manual result | 提交人工处理结果 | Keep only where currently eligible | Imports a supported manually prepared result through the current eligibility rules. Do not use this wording for Photoshop steps where manual result import is unsupported. `Session_SubmitManualResult`. |
| Ask a colleague to correct this image | Ask a colleague to correct this image | 请同事修正此图片 | Keep | SCRUM-11148: offered beside Approve and Reject on a background-removal review only. It prepares a reference copy and a working copy and hands the job off; the return is an explicit "Import corrected image" (导入修正后的图片), never a queue, account, watcher or automatic pickup. `Session_AskColleague`. |
| Confirm result and save | Confirm result and save | 确认结果并保存 | Keep | SCRUM-11145: offered only at a lawful final review (asset Trim, print TIFF). It checks the displayed file name and folder, approves exactly the displayed result, prepares the approved PNG once where needed, then saves; approval and saving are reported separately. `Approve` alone still records the review only. Resources: `FinalSave_ConfirmAndSave`, `FinalSave_ApproveOnlyHint`. |
| Open containing folder | Open containing folder | 打开所在文件夹 | Keep | SCRUM-11145: shown only for a recorded delivery whose file is verified at that moment; it asks Windows to select that exact file in its folder and never opens the image or the internal Approved area. A dispatched request is not proof that Explorer visibly selected it. Resource: `FinalSave_OpenFolder`. |
| What to check | What to check | 检查要点 | Keep | SCRUM-11149: read-only section beside a review (Enhancement, background removal, Trim, production TIFF). Two to four checks name the existing tools by their on-screen labels; one line each says what Approve and Reject do, and a help line names only an entry that exists for that result (Ask a colleague on background removal, Adjust trim edges on Trim). Approve is never described as saving; the TIFF Reject line says the TIFF goes to the Recycle Bin first and no new TIFF is made until Run step. Resources `Session_Guidance*`. |

### Wave 1 next-step sentence patterns

These bilingual patterns were introduced with the current-status panel. They describe the existing command surface and must not manufacture a legal action.

| Situation | English | 简体中文 | Resource |
|---|---|---|---|
| Completed | The workflow is complete. | 此流程已完成。 | `Session_NextCompleted` |
| Abandoned | This job was abandoned; return to Home to choose another job. | 此任务已放弃；请返回主页选择其他任务。 | `Session_NextAbandoned` |
| Processing | Please wait while PrintFlow processes: {step}. | PrintFlow 正在处理：{步骤}，请稍候。 | `Session_NextProcessing` |
| Review | Inspect this result, then choose {approve} or {reject}. | 请检查当前结果，然后选择“{通过}”或“{驳回}”。 | `Session_NextReview` |
| Retry offered | This step needs another attempt; choose {retry} when ready. | 此步骤需要重新尝试；准备好后请选择“{重试}”。 | `Session_NextRetry` |
| Other offered action | When ready, choose {action}. | 准备好后，请选择“{操作}”。 | `Session_NextAction` |
| Stopped without a recommended command | This step did not finish; check the notices and available actions below. | 此步骤未完成；请查看下方提示和可用操作。 | `Session_NextStopped` |
| Original confirmation | Check the original image, then choose {confirm}. | 请检查原图，然后选择“{确认}”。 | `Session_NextOriginal` |
| White ink | Choose the white-ink option below and confirm it. | 请选择下方的白墨选项并确认。 | `Session_NextWhiteInk` |
| Dimensions | Set and confirm the print size below. | 请在下方设置并确认打印尺寸。 | `Session_NextDimensions` |
| Background-removal choice | Choose how to remove the background in the controls below. | 请在下方选择背景去除方式。 | `Session_NextBackground` |
| Other required input | Check the current step and complete the required input below. | 请查看当前步骤，并完成下方所需操作。 | `Session_NextInput` |
| Manual crop is open | Draw or adjust the crop boundary, then choose {Apply Crop}. | 请绘制或调整裁边边界，然后选择“{应用裁切}”。 | `Session_NextCrop`; the placeholder uses the current `ApplyManualCropLabel` |
| Trim adjustment is open | Drag the edges or corners, then choose {Use this trim}. | 拖动边线或角点，然后选择“{使用此裁切}”。 | `Session_NextTrimAdjust` (SCRUM-11147); the placeholder uses `UseThisTrimLabel` |

## Production and review terms

| Concept | English operator wording | 简体中文操作员用语 | Treatment | Meaning and current references |
|---|---|---|---|---|
| Trim | Trim image edges | 裁切图片边缘 | Keep with explanation | Changes image bounds; it is distinct from physical print size. Current automatic/manual crop surfaces, and direct handle adjustment from the trim review (SCRUM-11147). |
| Adjust trim edges | Adjust trim edges | 调整裁切边缘 | Keep | SCRUM-11147: opens the handle editor over the picture the trim was cut from; sends nothing. Asset copy never mentions print size; the customer-design route adds "The print size is set in a later step." / "印刷尺寸在后面的步骤中设置。" Resources `Session_TrimAdjust*`. |
| Use this trim | Use this trim | 使用此裁切 | Keep | Makes a new trim result that still waits for review. It never approves or saves. `Session_TrimAdjustUse`. |
| Restore automatic suggestion | Restore automatic suggestion | 恢复自动建议 | Keep | Puts the boundary back exactly on the automatic trim's recorded rectangle; unavailable, with an explanation, when there is none. `Session_TrimAdjustRestore`. |
| Print size | Print size | 印刷尺寸 | Keep | Physical dimensions in millimetres, available only on the two TIFF routes. Current dimensions/preflight surfaces. |
| Print size guidance | Print size check / Technical details (pixels and PPI) | 印刷尺寸核对 / 技术细节（像素与 PPI） | Keep | SCRUM-11150: one "choose this when" line beside each existing sizing choice (a preset is a **maximum**, never enlarged; Custom size fixes one chosen side, Long edge = the longer side of this picture), and a live sentence formatted from the shown preflight: the whole picture's approximate millimetres (including see-through edges), what decided them (the width or height reaching the maximum, the picture already within it, or the side the operator entered) and that proportions are kept. Drafts say "would"; nothing is described as saved or made. Enlargement is "may look soft or blurry" and still needs "Continue with this size"; the scale %, 300 PPI and pixel facts sit under the collapsed technical details. Resources `Session_Size*`; the heading `Session_PreflightHeading` and the zh-CN row label `Session_PreflightPrintSize` use 印刷尺寸. |
| Pixels / PPI | Pixel dimensions / source resolution | 像素尺寸 / 源图分辨率 | Technical details only on beginner flow | May appear in print-size technical details, Production details, Error details, and diagnostic/audit records. Do not remove or change the underlying values. |
| Preset | Approved production setup | 已批准的生产设置 | Rename in operator copy | Fixed validated workstation production configuration. “Preset” and its identifier may remain in Settings, Production details, Error details, and diagnostics. Current resources include `Settings_Preset` and `Preset_Verified`. |
| White ink / W1 | White ink (W1) | 白墨（W1） | Rename in operator copy | W1 is the technical white-ink branch/channel label. Use “W1” alone only in Production details, Error details, technical expanders, audit/history, or diagnostics. Current resource: `Session_LabelBranch`; TIFF review modes. |
| Error details | Error details | 错误详情 | Keep | Secondary technical surface for stable failure code and diagnostic facts. The primary message should explain the operator-safe next step. `ErrorDetails_Heading`. |
| Failure notice | What happened, what is known, then the next step; code under "Error details" | 发生了什么、已知情况、下一步；错误代码在“错误详情”中 | Keep | SCRUM-11151. The sentence carries no code. A collapsed "Error details / 错误详情" under the notice shows "Code / 错误代码" of that exact failure and "Quote this code when you ask for help." Shared `Failure_*` sentences avoid unconditional Run/Retry advice and end with "PrintFlow never changes your original file." / "PrintFlow 不会更改你的原始文件。"; they do not promise that no file was produced or that every failed action was rolled back. On the processing screen the next step is the status line, so Retry is named only when offered. Home, Workflow Selection and Settings name their own safe repeat ("choose Resume/Apply/a workflow again") or ask for "a colleague or supervisor / 同事或主管". Resources and final wording: `docs/codex/printflow-operator-ux-backlog/SCRUM-11151_COPY_REVIEW.md`. |

## Terms still requiring a later business decision

- The external-delivery nouns and verbs for SCRUM-11144/11145 remain unresolved until that delivery contract records a destination and successful export. This does not block current work.
- SCRUM-11148 defined the colleague-correction handoff and return wording (`Session_Correction*`, `Home_RecentWaitingForCorrection`, `Home_RecoveryOpenCorrection`). Final wording remains subject to the novice walkthrough.
- Whether route names themselves should be renamed after novice observation remains unobserved. Current fixed bilingual names stay authoritative for Wave 1.

No signature or sign-off is required for this reference. A future change that alters the business meaning of approved, completed, or delivered must record the product-owner decision in this document or the relevant issue; ordinary wording alignment does not require approval.
