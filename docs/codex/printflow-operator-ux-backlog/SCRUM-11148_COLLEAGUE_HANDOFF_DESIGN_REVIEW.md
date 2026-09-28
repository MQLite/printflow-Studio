# SCRUM-11148 colleague-correction design — independent review record

Task `PF-OPUX-v1-SCRUM-11148-design-v1`, 2026-09-28 NZ. The design under review is [SCRUM-11148_COLLEAGUE_HANDOFF_DESIGN.md](SCRUM-11148_COLLEAGUE_HANDOFF_DESIGN.md). **A technical review is not owner approval. Status stays AWAITING_OWNER_REVIEW.**

## Reviewer and independence

- **Who.** One `personal-dev-reviewer` subagent (read-only: Read/Glob/Grep), started fresh and so without the designer's conversation or self-assessment. The same context did the recheck.
- **Route.**
  - Requested: Opus High.
  - The reviewer reported running as `claude-opus-5-5`. That is not host telemetry; effort is UNVERIFIED.
- **Inputs.**
  - The unabridged SCRUM-11148 AC and scope/non-goal text from the authenticated read.
  - The task prompt file.
  - The design file on disk.
  - Direct source pointers, which the reviewer verified itself.
  - Focus areas from task prompt §4–§9.
- **Not supplied:** the designer's exploration history or the designer's own assessment of the design.
- **Read, not run.** The reviewer read source and documents only. It did not build, test, run the application, or use a picker or Explorer. It could not verify the Jira read itself.

## Round 1 — OPEN (2 × P1, 4 × P2, 6 × P3)

| ID | Sev | Finding (summary) | Disposition |
|---|---|---|---|
| P1-1 | P1 | The new commands had engine-only legality. `ExecuteAsync(ImportCorrectedImage)` bypassed the request binding, and the probe advertised the command on legacy generic review handoffs. A direct `RequestColleagueCorrection` could hand off with no files. | Both commands carry `CorrectionRequestId`. `ExecuteCoreAsync` refuses them unless given an internal `CorrectionContext` that only the dedicated entries build. Availability comes from the projection. A refusal test was added to the AC1 evidence (§6.1, §6.2, §9, §10). |
| P1-2 | P1 | An interrupted or stopped import leaves the session `Active` (F22), where Run and Retry would restart background removal. Stop is offered during an import. The S3 "Run refused" claim was false for that mode. The `ResolveRecoveryAsync` route does not filter the lock release. | Mode Active + Interrupted removed. New narrow rule D5: a correction-bound import closed Failed, Cancelled or Interrupted re-hands off in the same closing commit, with the lock release removed. The seams are `FailAttemptAsync`, `StopAttemptAsync` and the startup-recovery closing. Stop is kept. Modes Review / AfterUnfinishedImport (§6.4, §7.1, §7.2, §9, §10 AC4, §13). |
| P2-1 | P2 | Reject stayed hash-only although the design allows a same-hash R2. | `RejectExactReviewAsync` (id + hash, one gate, unchanged core) and fresh-gesture binding for background-removal Reject. The Trim Reject residual stays separate (§7.1, §7.5). |
| P2-2 | P2 | After a failed import, the generic Submit and Return to automation compete with Import and can orphan the request. | While `CanImportCorrectedImage`, generic Submit is suppressed, Import is the recommended command, and Return to automation moves under Other options (§6.4, §7.1). |
| P2-3 | P2 | AC5's next step was not shown during the review of R2. | The R2 review text names the next step, computed from the workflow definition, with a VM test (§8.1, copy, §10 AC5). |
| P2-4 | P2 | F16 was wrong: zh-CN Recent shows 已转交. "Details" wording. | F16 corrected. zh-CN `SessionState_HandedOff` is aligned to 已移交. The Recent waiting line is no longer optional. "Details" is recorded as a limitation (§2.1, §8.3, copy, §13). |
| P3-1 | P3 | The §7.3 key mapping was inaccurate. | Corrected to the importer's actual `Localise` mapping; an optional in-use message was added. |
| P3-2 | P3 | The integrity check of U at import was unspecified. | `EnsureIntegrityAsync` mapping added (§6.5). |
| P3-3 | P3 | A swap after the re-hash bypassed the preflight promise. | `ImportAsync` gains `expectedSelectedHash`; §9 reworded. |
| P3-4 | P3 | §9 and §6.2 were inconsistent on a source mutating during the copy. | Two-point integrity handling stated (§6.2, §9). |
| P3-5 | P3 | AC1 evidence omitted the two-click path; the S1 disabling was unstated. | VM test added; Approve/Reject/Run disabled while S1 is open (§7.1, §10). |
| P3-6 | P3 | A late failure overwrites `HandOffReason`. | The re-handoff uses the request's reason; the note survives in the row (§7.2, §13). |
| Obs | — | `PREPARING` partials persist. | Recorded residual (§13). |

The reviewer confirmed as correct: F1–F15 and F17–F21, the §2.2 mismatch, the lineage U/R/R2, the no-manufactured-decision approach, the lock omission and the gate discipline.

## Round 2 — CLOSED, with three new P3 items

All round-1 findings were RESOLVED. P1-2 is resolved subject to owner decision D5.

The reviewer checked the re-handoff rule against source:
- `HandOff` is legal from `Interrupted`, and `CanSubmit` covers `Failed`/`Interrupted`.
- Home recovery lists `HandedOff` + `Interrupted` but not a Cancelled attempt.
- TakeOver already re-hands off, and TakeOver is not offered for an import.
- Startup recovery commits in one transaction and applies no engine lock effect.
- The `CorrectionContext` guard is consistent with the AfterUnfinishedImport route: it applies only to the two new commands.

The new items were folded in rather than deferred:

| ID | Sev | Finding | Disposition |
|---|---|---|---|
| N1 | P3 | `AttemptCancelled` releases the lock unconditionally, so a Stop close of an import could roll back while another holder owns the row. | `StopAttemptAsync` sets `LockChange = null` for a `ManualResultImport` close, as L2143 does (§6.4). |
| N2 | P3 | Startup recovery does not use `MergeSession`. How the seams find the bound attempt was unstated. | Recovery sets State/HandOffReason/HandedOffAtUtc explicitly. The bound-attempt lookup is specified (§6.4). |
| N3 | P3 | `NextStepText` has no branch for the review of R2. | A branch keyed on a `RETURNED` request's `ResultRevisionId` (§8.1). |

## Round 3 — OPEN on one P2

N1, N3 and the startup part of N2 were RESOLVED. New finding **NEW-1 (P2):** the lookup claimed to read the request "from the aggregate it already loads". In fact `FailAttemptAsync` and `StopAttemptAsync` receive only the in-memory `afterStart` (L1924–1929), which predates the opening commit's `LastImportAttemptId`. Implemented literally, a stopped correction import would stay Active. **Disposition:** those two seams take the request from `ProducingWork.CorrectionRequestId`, the opening commit's request change is also applied to `afterStart`, and startup recovery reads the persisted row (§6.4).

## Round 4 — CLOSED

NEW-1 was RESOLVED against `SessionService.cs` L2044/L2065/L1924–1929 and `StartupRecoveryService.cs` L272. No new defects. **No P0–P2 findings remain.**

## Final reviewed design identity

`SCRUM-11148_COLLEAGUE_HANDOFF_DESIGN.md`, 604 lines, SHA-256 `8d4edaec9c9b02d94bde75ac18d85f7a1bbf609bdcd667dc2df21e0c316bd9b2`. This is the version the round-4 recheck read. The design was not edited after that recheck.

## Remaining for the owner

D1–D5 in design §13. The largest decisions are the additive migration and the narrow closing-seam re-handoff. Residuals are in design §13. All future evidence in design §11 is NOT RUN.
