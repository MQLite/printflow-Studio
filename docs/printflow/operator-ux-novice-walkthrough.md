# Beginner-operator discovery walkthrough

**Status: PREPARED / HUMAN WALKTHROUGH NOT RUN**

Planning item: SCRUM-11141 / PF-OPUX-v1-novice-walkthrough

Participant evidence: none

Operator validation of the Epic: not claimed

This document is a facilitator script and empty anonymized record. It is not evidence that a human session occurred. An agent or developer must not substitute for the novice participant. Do not close SCRUM-11141 from this preparation alone.

## Purpose and participant

Invite one real operator who does not need prior Photoshop, Meitu, colour-separation, or computer-science knowledge. Record a participant code such as `P01`; do not record a name, customer, order, source filename, or identifying screenshot. Use neutral prompts and let the participant decide what to do. Help only when needed for safety or after recording the point at which help was requested.

Do not measure task time, set a time target, calculate a success percentage, or revive a timing/KPI requirement. The purpose is to observe route choice, next-step comprehension, review judgement, trim, background-removal judgement, dimensions, failure recovery, file finding, and terminology.

## Safe setup checklist

Complete every item before inviting the participant.

- [ ] Schedule a quiet period when no live production work is running on the shared workstation.
- [ ] Use only synthetic images or an owner-agreed non-customer regression set. Remove customer names and identifying metadata from any visible fixture names.
- [ ] Confirm the selected harness uses an isolated temporary database, workspace, and output root. A normal application launch can run startup recovery and is not assumed read-only.
- [ ] Confirm fake/test adapters cannot acquire or control the production Meitu or Photoshop automation path and cannot touch any open customer document.
- [ ] Do not touch open customer work. Proceed only when the workstation owner has independently made the workstation safe and the facilitator has verified isolation. Confirm the displayed Recent, Recovery, Error details, and file-picker content contains no customer information.
- [ ] Record the fixture IDs, harness name, isolated roots, app build/commit, screen resolution/scaling, and UI language in the session record without private absolute paths.
- [ ] Confirm how the facilitator will safely stop the test and who is authorized to assess an unexpected workstation state.

Stop the walkthrough immediately if any isolation fact is unknown; a production path or database appears; startup recovery refers to production work; a real customer file/name/image appears; Meitu or Photoshop opens or controls an unexpected document; an unexpected write, deletion, move, or external-app action occurs; the app enters an unrecognized state; or the participant asks to stop. Preserve the screen without continuing commands, note only an anonymized safety finding, and ask the authorized workstation owner to assess it. Do not force-close external applications, bypass readiness/recovery guards, move customer files, or improvise a production fix.

## Facilitator rules

Read only the Chinese task text in each scenario. Do not teach the route, point at the expected control, translate internal terms, or explain what “correct” looks like before the participant acts. If the participant asks for help, first record the request and what prompted it, then give the smallest safe hint. Record quotations only when spoken by the participant and remove identifying details.

Use these result values:

- Independently completed: `YES`, `NO`, or `UNOBSERVED`.
- Help requests, wrong actions/routes, hesitation points, observed quotation: factual text or `UNOBSERVED`.
- Assumption outcome: `CONFIRMED`, `REFUTED`, or `NOT OBSERVED` only after real participant evidence.
- Availability: `CURRENT` or `FUTURE — DO NOT ATTEMPT`.

## Neutral zh-CN task script

| ID | Scenario | Availability | Read this instruction to the participant |
|---|---|---|---|
| S1 | Choose the asset route | CURRENT | “这是一张以后还要用于设计的素材图片。请在 PrintFlow 中选择你认为合适的处理方式，并把它处理到系统认可的 PNG 结果。请按你认为正确的方式操作。” |
| S2 | Choose the customer-design route | CURRENT | “这是一张客户已经排好版的设计图。请在 PrintFlow 中选择你认为合适的处理方式，并把它处理到系统认可的打印 TIFF。请按你认为正确的方式操作。” |
| S3 | Choose Generate Print TIFF | CURRENT | “这是一张店内已经完成设计的图片，只需要制作打印 TIFF。请在 PrintFlow 中选择你认为合适的处理方式并继续。” |
| S4 | Follow current status and next action | CURRENT after Wave 1 Unit C is present | “请告诉我现在是什么状态、系统希望你下一步做什么，然后继续。” |
| S5 | Review an enhancement result | CURRENT | “请检查这个处理结果，并决定是否接受。请说出你会检查什么；如果不确定，请按你平时会采用的方式处理。” |
| S6 | Judge background removal | CURRENT | “请检查去除背景后的结果，并决定是否可以接受。请使用你认为需要的查看方式。” |
| S7 | Adjust a trim with the current manual-crop fallback | CURRENT only with an isolated predetermined fixture that legally offers manual crop | “系统现在要求你手动调整图片保留范围。请使用屏幕上现有的工具，把裁切范围调整到你认为合适的位置，然后继续。” Do not induce an unknown failure to expose this surface. |
| S7b | Adjust trim directly from review with edge/corner handles | FUTURE — DO NOT ATTEMPT; SCRUM-11147 | “请直接从审核页面调整裁切边界，确认保留的图片范围，然后继续。” The current manual-crop fallback is not evidence that this planned direct review flow exists. |
| S8 | Choose print dimensions | CURRENT on TIFF routes | “请把这张图片设置成任务卡上写明的印刷尺寸。请告诉我你认为尺寸会怎样影响图片，然后继续。” Use a synthetic task card with dimensions only; do not coach which edge governs or whether enlargement is acceptable. |
| S9 | Representative recoverable failure | CURRENT only in an isolated deterministic fixture | “处理没有成功。请根据屏幕上的信息判断发生了什么、文件是否还在，以及你下一步会怎么做。” Do not manufacture a live external-app failure. |
| S10 | Find the currently approved internal result | CURRENT | “请找到刚才通过审核的结果，并告诉我你在哪里找到它。请不要把文件移动到别处。” Record whether the participant distinguishes the internal Approved area from an externally delivered file. |
| S11 | Save to a chosen folder and open it | FUTURE — DO NOT ATTEMPT; SCRUM-11144/11145 | “请选择保存位置，保存最终结果，然后打开它所在的文件夹。” Do not present this as available until delivery persistence/export exists. An internal Approved file or completed job does not satisfy this scenario. |
| S12 | Ask a colleague to correct background removal and return it | FUTURE — DO NOT ATTEMPT; SCRUM-11148 | “这个去背景结果需要同事修正。请把它交给同事，并在修正文件准备好后把结果带回这个任务。” Do not run until the correction/re-import contract exists; current handoff alone does not satisfy it. |

For S1–S3, use separate fresh synthetic sessions so route choice and prior learning are visible. The asset route must not be asked for print dimensions or a TIFF. The two TIFF routes include dimensions and final TIFF review. For S9, the fixture and expected safe recovery must be predetermined by the test owner; the facilitator must not induce an unknown dialog, crash, timeout, or production-application takeover.

## Session record (English)

| Field | Record |
|---|---|
| Date | UNOBSERVED |
| Participant code | UNOBSERVED |
| Facilitator code | UNOBSERVED |
| Participant background relevant to the study | UNOBSERVED |
| Build / commit | UNOBSERVED |
| Harness and fake/test adapter evidence | UNOBSERVED |
| Isolated DB/workspace/output evidence | UNOBSERVED |
| Synthetic/regression fixture IDs | UNOBSERVED |
| Resolution and scaling | UNOBSERVED |
| Operator UI language | zh-CN (planned); UNOBSERVED |
| Safety checklist completed by | UNOBSERVED |
| Session result | HUMAN WALKTHROUGH NOT RUN |

## Findings record

All observation fields are intentionally empty of claimed evidence. Add one row per real attempt; never replace `UNOBSERVED` with a guess.

| Scenario | Independently completed | Help requests | Wrong actions or routes | Hesitation points | Observed quotation | Assumption outcome | Severity | Covered by / proposed follow-up |
|---|---|---|---|---|---|---|---|---|
| S1 Asset route | UNOBSERVED | UNOBSERVED | UNOBSERVED | UNOBSERVED | UNOBSERVED | NOT OBSERVED | UNOBSERVED | Route choice / SCRUM-11146 if needed |
| S2 Customer-design route | UNOBSERVED | UNOBSERVED | UNOBSERVED | UNOBSERVED | UNOBSERVED | NOT OBSERVED | UNOBSERVED | Route choice / SCRUM-11146 if needed |
| S3 Generate Print TIFF route | UNOBSERVED | UNOBSERVED | UNOBSERVED | UNOBSERVED | UNOBSERVED | NOT OBSERVED | UNOBSERVED | Route choice / SCRUM-11146 if needed |
| S4 Status and next action | UNOBSERVED | UNOBSERVED | UNOBSERVED | UNOBSERVED | UNOBSERVED | NOT OBSERVED | UNOBSERVED | SCRUM-11143 |
| S5 Enhancement review | UNOBSERVED | UNOBSERVED | UNOBSERVED | UNOBSERVED | UNOBSERVED | NOT OBSERVED | UNOBSERVED | SCRUM-11149 |
| S6 Background-removal judgement | UNOBSERVED | UNOBSERVED | UNOBSERVED | UNOBSERVED | UNOBSERVED | NOT OBSERVED | UNOBSERVED | SCRUM-11148/11149 |
| S7 Current manual-crop adjustment | UNOBSERVED | UNOBSERVED | UNOBSERVED | UNOBSERVED | UNOBSERVED | NOT OBSERVED | UNOBSERVED | Current fallback; proposed guidance/interaction follow-up if needed |
| S7b Direct trim adjustment (future) | UNOBSERVED | UNOBSERVED | UNOBSERVED | UNOBSERVED | UNOBSERVED | NOT OBSERVED | UNOBSERVED | SCRUM-11147; not attempted |
| S8 Print dimensions | UNOBSERVED | UNOBSERVED | UNOBSERVED | UNOBSERVED | UNOBSERVED | NOT OBSERVED | UNOBSERVED | SCRUM-11150 |
| S9 Failure and recovery | UNOBSERVED | UNOBSERVED | UNOBSERVED | UNOBSERVED | UNOBSERVED | NOT OBSERVED | UNOBSERVED | SCRUM-11151 or proposed follow-up |
| S10 Find approved internal result | UNOBSERVED | UNOBSERVED | UNOBSERVED | UNOBSERVED | UNOBSERVED | NOT OBSERVED | UNOBSERVED | Terminology; delivery distinction |
| S11 Delivery/file finding (future) | UNOBSERVED | UNOBSERVED | UNOBSERVED | UNOBSERVED | UNOBSERVED | NOT OBSERVED | UNOBSERVED | SCRUM-11144/11145; not attempted |
| S12 Colleague correction (future) | UNOBSERVED | UNOBSERVED | UNOBSERVED | UNOBSERVED | UNOBSERVED | NOT OBSERVED | UNOBSERVED | SCRUM-11148; not attempted |

## Assumption verdicts

| Assumption | Verdict | Evidence / notes |
|---|---|---|
| The participant can choose among the three routes from the current names and explanations. | NOT OBSERVED | UNOBSERVED |
| The participant can identify the current status and next legal action. | NOT OBSERVED | UNOBSERVED |
| The participant understands what to inspect and what Approve/Reject mean. | NOT OBSERVED | UNOBSERVED |
| The participant can use the current manual-crop fallback without confusing trim bounds with print size. | NOT OBSERVED | UNOBSERVED |
| The participant can adjust trim directly from review with edge/corner handles. | NOT OBSERVED | Future scenario; SCRUM-11147 is not implemented in this Wave 1 scope. |
| The participant can judge a background-removal result or knows when to ask for help. | NOT OBSERVED | UNOBSERVED |
| The participant understands the print dimensions and enlargement decision. | NOT OBSERVED | UNOBSERVED |
| The participant can follow a representative failure’s safe next step. | NOT OBSERVED | UNOBSERVED |
| The participant can find the relevant file and distinguish internal approval from external delivery. | NOT OBSERVED | Delivery scenario remains future. |
| Beginner-facing terminology is understood consistently in zh-CN. | NOT OBSERVED | UNOBSERVED |

## Recording findings outside current Epic scope

If a real observation is not covered by an existing Epic task, record it here as a proposed addition only. Do not implement it during the walkthrough.

| Proposed addition | Participant evidence | Safety/business impact | Suggested owner/task |
|---|---|---|---|
| UNOBSERVED | UNOBSERVED | UNOBSERVED | UNOBSERVED |

## Closeout

After a real session, confirm the record contains no customer image, customer filename, participant name, private path, or identifying screenshot. Mark scenarios that were not safely available as `NOT OBSERVED`, never PASS. A prepared script, developer rehearsal, agent inspection, automated test, or synthetic screenshot is not a human walkthrough result.
