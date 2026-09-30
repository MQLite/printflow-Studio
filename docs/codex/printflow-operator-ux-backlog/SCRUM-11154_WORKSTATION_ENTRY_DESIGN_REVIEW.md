# SCRUM-11154 workstation entry — independent design review

**Design findings corrected and rechecked; AWAITING_OWNER_REVIEW.** One genuinely fresh read-only native subagent (`fork_turns=none`) reviewed the original design-only prompt, exact four AC against the authenticated initial Jira response, proposed isolation/W01–W10 contract, relevant current source and the supplied historical image. The same reviewer rechecked corrections. This is source review, not construction or runtime isolation proof.

Source baseline: `5e6adcf3251bded0ce3df2d127d933eecde71969`. [Final design](SCRUM-11154_WORKSTATION_ENTRY_DESIGN.md) SHA-256: `E31BE7B9435F457A12A525D82117D891E16FD05A77A935CA670DE7BD6C273DA3`. Initial reviewer-read design hash: `2A2D6DDFE728447A1C315E0264E51534A39E2E9E65B433662E81FD0B6287FBF6`; differences are the recorded design corrections below, not product changes.

## Findings first

| ID / severity | Finding and source evidence | Correction / same-reviewer disposition |
|---|---|---|
| D-01 / P2 | Package containment was too late for diagnostic image reads. `DiagnosticPackageService.BuildPlanAsync` calls `LoadErrorDetailsAsync` first. `SessionService.ErrorDetails.cs` takes absolute screenshot paths from correlated logs or failure context and sends them to `IDiagnosticImagePreviewDecoder`; `WicImagePreviewDecoder.DecodeDiagnosticAsync` validates absolute syntax only. A package inspector/writer wrapper cannot prevent that earlier read. | Added explicit contained diagnostic decoder registration; refuse outside/reparse/changed-identity evidence before inner file reads, including both persisted sources. Guard full Details and package planning, prohibit exposed unwrapped diagnostic interface, and require future refusal checks for both routes. **Resolved in design.** |
| D-02 / P2 | Promise to retain staged/partial ZIPs contradicted unchanged `DiagnosticPackageArchiveWriter`: caught failures delete its published file, and `finally` deletes staging and adjacent temporary files. An outer wrapper cannot preserve files already removed internally. | Matrix now states real cleanup behavior. Retain source evidence and operation outcome; surviving ambiguous files remain for review. Preserve-all-partials is explicitly unsupported without a separately approved seam. Future checks verify cleanup only affects owned paths. **Resolved in design.** |
| D-03 / P3 | Fixture API was incorrectly named `FakePhotoshopOutputProcessor.ValidTiff`. | Corrected to `FakePhotoshopOutputProcessor.SetTiffOutput(FakePhotoshopTiffOutput.ValidTiff)`. **Resolved.** |
| D-04 / evidence clarification | `SessionService.RunProducingStepAsync` skips workstation-lease acquisition for Fake adapters. Visible Fake operation can exercise the session DB automation lock, not certify workstation lease token/reclaim/Alive/Unknown behavior. | Separate future isolated real-manager validation, using explicit test DB/resource; never change processor to Production to force coverage. **Resolved.** |

The author also corrected the trim class reference to the actual `SessionViewModel.TrimAdjust.cs` partial and removed the assumption that `PhotoshopRequest` exposes a Stop property. The reviewer rechecked both. Current Photoshop cancellation/phase limitations remain explicit; unsupported phases cannot be represented by fabricated view-model state.

## Review conclusion and limits

The principal recommendation is source-supported: installed Fake startup does not isolate persistence or OS boundaries; Wave1A is a single Session view; real `MainWindow`/navigation composition is materially different. The design preserves command eligibility, exact revision/output/hash authority, positive fresh input, enlargement authorization, correction binding, approval-before-save and known PNG/legacy/recovery/diagnostic gaps. The full W01–W10 map distinguishes supported proposed paths from unsupported slices, and forbids approval backfill or hidden navigation to complete a demonstration.

The reviewer independently verified the supplied screenshot hash `385199FB97911CD8104C6AAB92835F230C31542F9F6925C2C71B28282B125D28` and its source trace: 6×5 preparation pixels at 300 DPI yield 0.508×0.423 mm, while the output row displays retained 200×150 maximum-box dimensions. No claim of historical DB or executed-binary reconstruction, real Photoshop fault, or product correction follows.

Final recheck: **no remaining actionable findings in the rechecked scope**. This closes D-01–D-04 as design findings only. It does not approve the architecture on the owner's behalf, authorize implementation, certify the future host, close AC1–3 or earlier Tasks, or begin SCRUM-11155. The original checklist and open-items record remain unchanged.

Reviewer performed no file edits, builds, tests, DI construction, migrations, application run, new screenshot capture, desktop input or external writes. Root validation is likewise limited to source/document evidence and the separately authorized publication/export/packet operations. All future runtime/refusal/navigation/physical acceptance checks remain **NOT RUN**.

Routing: installed policy 2.4, route_offset 0. Fresh independent safety/composition review requested `gpt-6-astra/high` through the supported native mechanism. Actual reviewer model/effort **UNVERIFIED**; accepted request is not runtime metadata. Same context was reused for correction recheck. No additional reviewer or historical Epic re-review.

## 业主结论

两项 P2 设计问题已修正，并由同一独立审阅者复查；另修正夹具引用及租约证据范围。建议将[设计第9节](SCRUM-11154_WORKSTATION_ENTRY_DESIGN.md#9-owner-decisions--业主待决定事项)作为下一次授权的具体决策项。当前仅完成设计及复核，未经运行验证；**AWAITING_OWNER_REVIEW**。
