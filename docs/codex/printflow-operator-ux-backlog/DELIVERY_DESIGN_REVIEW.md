# PF-OPUX-v1 delivery design — independent review

2026-09-23 NZ. Task `PF-OPUX-v1-delivery-design-v1`.

**Disposition: technically ready for owner review; both P2 findings resolved.** Design status remains **PROPOSED / AWAITING_OWNER_REVIEW**. This is not owner approval, implementation PASS, operator acceptance or production readiness.

## Findings and resolutions

### P2-1 — persisted requests could not be discovered after restart — RESOLVED

The initial §7.1 exposed artifact metadata but no way to rediscover persisted DeliveryId/RequestId, original destination and interrupted attempt. Reconcile, Check and Open all required a DeliveryId the restarted UI might no longer know. Reconstructing it from the newly remembered destination could miss the published-but-unrecorded file or invite another copy.

Direct evidence: `src/PrintFlow.Workflow/Services/ISessionService.cs` loads SessionView; `Services/SessionView.cs` projects processing/output state; `src/PrintFlow.Infrastructure/Sqlite/SqliteSessionRepository.cs` loads processing records. None currently provides a delivery projection.

Correction in final design §§7.1–7.2: exact session/artifact-scoped `GetDeliveryStateAsync`, returning persisted identities, destinations, status/attempts, verification history and replacement links. Reopened views distinguish original requests from a new draft/default; unresolved requests are explicitly reconciled using recorded identity. Availability begins NotChecked and external checks stay on demand. §8 adds a focused restart case where the remembered destination differs from the interrupted one.

Independent focused recheck confirmed this finding resolved.

### P2-2 — ordinary retry and explicit replacement of a missing copy were indistinguishable — RESOLVED

Initial §4 required same-artifact/destination requests to coalesce, while §6 permitted a new generation at the same path after a missing delivery. The request had no purpose or prior-delivery reference to distinguish these intentions. Implementations could continually return Missing or treat ordinary retry as consent to copy again.

Correction in §§4, 6 and 7.1: a typed `ReplaceMissingAsync` carries the prior DeliveryId, a Missing observation, new RequestId and selection version. The source/destination remain bound to the prior evidence. A unique ReplacementOfDeliveryId permits one direct successor, with fresh eligibility/directory/absence checks before creating it. The old evidence remains immutable; a reappearing file is never overwritten. Future evidence covers no automatic recreation, one explicit successor and repeated/concurrent acceptance.

The first focused recheck caught an ordering issue in that correction: requiring the old path to be absent before retrieving an existing successor would reject a repeat after successful replacement. The final paragraph now **looks up and reconciles an existing successor first**. Only the no-successor branch requires fresh absence before creation; a concurrent winner returns through the existing-successor branch. This was corrected and rechecked only in the affected paragraph. The independent reviewer confirmed P2-2 resolved, with no new issue introduced.

Related clarifications reviewed without additional findings: explicit FinalSaveRequest identity/ApprovalOutcome/DeliveryOutcome, PNG reviewed-source to promoted-artifact linkage, and the disposable verified shell-selection lease held through dispatch.

No P0/P1 findings were identified in this design review. No actionable findings remain in the reviewed design scope. The owner decisions below are not deferred technical omissions.

## Scope and evidence

The reviewer independently read the original delivery-design prompt, the complete current authenticated SCRUM-11144/11145 descriptions and AC (`getJiraIssue`, `view=full`, both comment containers empty), the design, and relevant current repository source. The review did not rely on the author's self-assessment or historical PASS reports.

Inspected boundaries: PNG Revision/promotion and inherited human review; TIFF PrintOutput/twin Revision/review authority; valid sibling sizes and physical dimensions; existing mutation/retention synchronization; SQLite/settings/migration patterns; workspace/path primitives; final review/input and service interaction. Core evidence includes:

- `src/PrintFlow.Domain/Revisions/Revision.cs`, `Outputs/PrintOutput.cs`, `Reviews/ReviewDecision.cs`.
- `src/PrintFlow.Workflow/Definitions/WorkflowCatalog.cs`, `Engine/WorkflowEngine.cs`, `Engine/TransitionTable.cs`.
- `src/PrintFlow.Workflow/Services/SessionService.cs`, `SessionCompletionGate.cs`, `SessionRetentionService.cs`, `ProductionTiffReviewService.cs`.
- `src/PrintFlow.Infrastructure/Workspace/FileWorkspace.cs`, `PathGuard.cs`, `ReparsePointGuard.cs`; `Sqlite/MigrationRunner.cs`, `SqliteConnectionFactory.cs`, `SqliteSettingsRepository.cs`.
- `src/PrintFlow.App/ViewModels/SessionViewModel.cs`, `SessionViewModel.OperatorStatus.cs`, `Views/ReviewApprovalButton.cs`, `SessionScreenView.xaml.cs`.

The review confirmed the proposed separation of approval, delivery and current availability; no-replace publication after a durable ready checkpoint; final-name verification and a separate durable delivery transaction; idempotency requiring live file evidence; and retries that do not approve or process again. The current session gate does not already cover all these races, so its proposed extension remains implementation work. Native handle/rename/sharing behavior still requires the specified future synthetic Windows adapter checks. The documented [Windows no-replace rename contract](https://learn.microsoft.com/en-us/windows/win32/api/winbase/ns-winbase-file_rename_info) supports the selected primitive; it is not evidence of a tested implementation.

The reviewer agreed the genuine owner decisions are the proposed architecture/storage, the proposed destination support envelope, and the no-human-review asset-path compatibility issue. The latter is supported by WorkflowCatalog.PrepareAsset, WorkflowEngine.ConfirmOriginal/KeepOriginalExtent and existing KeepOriginalExtent test source. The design does not silently grandfather that path into human approval.

## Independence, routing and candidate identity

- Mechanism: one fresh native subagent, `/root/delivery_design_review`, **fork_turns=none**. Only task restrictions, original requirements, design location and direct source pointers were supplied; no parent exploration history. The same independent context performed bounded correction checks. No additional reviewer or nested delegation.
- Policy loaded: approved local `C:/Users/admin/.codex/workflows/development-routing.md`, v2.3. Context FRESH_REQUIRED. Explicit inherited `ROUTE_PROFILE.route_offset: 0`.
- NormalRoute = RequestedRoute = ExecutionTarget = ActualRoute = **gpt-6-astra/high**; AdjustmentResult UNCHANGED. Safety/recovery and approval boundaries justify this review capability.
- Actual route verified from this reviewer's own runtime turn_context, task `01a0cc07-00d0-7c22-934c-ae9344b9d55c`; the parent also read that exact metadata. This is separate from the historical 11146 review's UNVERIFIED route, which was not investigated again.
- Repository: local Windows, `D:/Repositories/printflow-Studio`, `master`, HEAD `eea60198c5095243f0694717a7479b00c3e1cd73`, existing uncommitted work preserved.

| Candidate | Design SHA-256 | Review outcome |
|---|---|---|
| Initial | `D8CC52069070A4FE56BCF4887459F98ACD79A0396B5964CBFDA48D262F476262` | Two P2 contract omissions; corrections required. |
| Contract corrections | `CC1098452B595B219169F0A0C3CE1E228A620DFD5138647B5F97BA3F87EBD432` | P2-1 resolved; P2-2 required successor-first ordering clarification. |
| Final, independently rechecked | `FF4AEA4EDAAB78DCDB8C0A44D34AE51B41DBA33F5F5FB373830758531B930ACB` | Both P2 findings resolved; technically ready for owner review. |

The reviewer wrote no files. The author made all design corrections and captured this report. The final check was limited to the corrected paragraph; neither original Jira reads nor the full source review were repeated unnecessarily.

## Verification and change boundary

This task performed source/AC/document inspection and fresh file-integrity checks only. All implementation builds/tests, migrations, native UI/Explorer checks and operator validation are **NOT RUN**, as required. No application launch, desktop-input/capture probe, customer/production data, dependency installation or Jira write occurred. No commit, push or deployment.

Before/after scope: added DELIVERY_DESIGN.md and this review; prepended the concise current checkpoint to HANDOFF.md while preserving its prior historical text. The other **802 pre-existing tracked/unignored files** retain the same path/content SHA-256 aggregate:

`7C451DB278666B96F45B213A45B8C27AE0B8D616974B84DE301BD9AAA0CB77C2`

The digest is SHA-256 over `path|file-SHA256` lines, ordered with the same PowerShell `Sort-Object -Unique` call before and after, joined with LF and encoded UTF-8; HANDOFF and the two new design files are excluded. It covers the pre-existing source/test/configuration changes and initiative snapshots/CSVs without presenting those changes as this task's work. Git's directory-level untracked status alone would not prove this distinction.

The final design maps all seven 11144 and eight amended 11145 AC to future evidence. These are design mappings, not executed acceptance results. Existing 11146 and Wave1A human/native acceptance remains open under HANDOFF. Stop at **AWAITING_OWNER_REVIEW**.
