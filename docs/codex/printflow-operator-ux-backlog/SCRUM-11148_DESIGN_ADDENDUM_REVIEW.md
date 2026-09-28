# SCRUM-11148 design addendum — independent targeted recheck

Task `PF-OPUX-v1-SCRUM-11148-design-boundary-closeout-v1`, 2026-09-28 NZ.
- **Reviewed document:** [SCRUM-11148_DESIGN_ADDENDUM.md](SCRUM-11148_DESIGN_ADDENDUM.md).
- **Base:** [SCRUM-11148_COLLEAGUE_HANDOFF_DESIGN.md](SCRUM-11148_COLLEAGUE_HANDOFF_DESIGN.md), SHA-256 `8d4edaec9c9b02d94bde75ac18d85f7a1bbf609bdcd667dc2df21e0c316bd9b2`, unchanged. Its [review](SCRUM-11148_COLLEAGUE_HANDOFF_DESIGN_REVIEW.md) is also unchanged.
- **A technical review is not owner approval.** Status stays AWAITING_OWNER_REVIEW.

## Reviewer and independence

- **Original reviewer unavailable.** The original reviewer context belonged to an earlier session and could not be reached; `ListAgents` showed only unrelated interactive peer sessions.
- **Who.** One fresh `personal-dev-reviewer` subagent (read-only: Read/Glob/Grep). The same context did the recheck.
- **Inputs.**
  - The task prompt (the addendum's acceptance criteria).
  - The base design with its AC quotation.
  - The base review.
  - The addendum.
  - Direct source and test pointers.
- **Not supplied:** the designer's exploration history or self-assessment.
- **Scope.** Only C1, C2, the supersession list, the future cases and the stated source facts. It did not re-review the whole base design.
- **Route.**
  - Requested: Opus High.
  - Model: `claude-opus-5-5` per this session's runtime context. The subagent's own model was not separately reported. Effort UNVERIFIED.
- **Read, not run.** No build, test, application, picker, Explorer or desktop action. The reviewer did not compute hashes; hashes below were computed by the coordinator.

## Round 1 — CLOSED (no P0–P2; 8 × P3)

**Checked with no finding.**
- **C1 counterexample.** Every step traces through the named source. The verdict "confirmed base-design defect" is correct.
- **C1 correction.**
  - Navigation (`OpenRecoveryAsync` → `LoadAsync` → `GoToSession` → `SessionViewModel.Open`/`Show`) writes nothing.
  - Guard (a) reuses the existing `RecoveryOf` recompute under the gate.
  - Guard (b) sits beside the existing `SubmitManualResult` check that every generic caller passes.
  - Error Details offers no import, and a grep found no other generic-import caller.
  - The guards only narrow. `CanSubmit`, the engine, F19 and legacy Home are unchanged.
- **C2.**
  - The scope mismatch (base L293 and review N1 versus D5) is correctly identified.
  - S12 (failure close lock-neutral) and S13 (success close lock-neutral; import never acquires) are true, so a broader change is avoidable.
  - The predicate is complete and sourced from `afterStart` or the persisted row.
  - Success and unfinished outcomes stay distinct, and no foreign or environment-verification lock can be touched.
  - No recursive gate, no lock for correction work, and no gate held during a picker.

| ID | Finding | Disposition |
|---|---|---|
| P3-1 | Stop never interrupts a manual import: the importer gets only the caller's token. The Stop close runs only when a stop is pending and the import then fails. The cited "hanging" importer fixture does not exist. | New fact S17. §2.1 step 4 and the verdict reworded (the crash variant is unconditional; the Stop variant needs a subsequent failure). New gated importer double in §6. §3.3 and §4 rows state the condition. Residual added. |
| P3-2 | `SimulatedProcessDeath` does not lose a closing commit; the environment-lock line reference was wrong. | §6 names `FaultingRepository { FailFromCommit }` and the `RecoverySurfaceTests.Seed` pattern; `SimulatedProcessDeath` is described correctly; the reference is corrected to `StartupRecoveryTests` L39–47. T3 and T8 updated. |
| P3-3 | "Only Stop releases the lock" left out TakeOver-after-success, which is API-only. Base F21 is only partly true. | §3.1 and §8 name both paths. F21 is added to §5 as clarified. A bound success does no handoff. |
| P3-4 | The fail-closed rule would hand off a success, the F8 text is untrue for Stop, and the startup asymmetry was unstated. | The rule now covers live unfinished closes only, with the neutral `CorrectionImportUnfinishedReason`. A success stays today's success. New §3.3 column. Startup asymmetry recorded as a residual. T9 covers both branches. |
| P3-5 | The `CanSubmitManualResult` refinement was worded against `CanSubmit`, not the actual property. | New fact S18; the refinement now reads "today's value ∧ ¬…". |
| P3-6 | The guard (b) refusal had no localization route. | `messageKey` `Session_CorrectionUseImport` with en/zh-CN text; slice 4 adds the strings. |
| P3-7 | T1 did not assert the request association after navigation. | T1 asserts `CorrectionHandoff.RequestId == r.Id` and `CanImportCorrectedImage` on the navigated view. |
| P3-8 | Supersession gaps (base §6.5 L320, §6.4 L289–292); `S` undefined; S9 incomplete; T2/T7/T9 unmapped; D5 scope not explicit about the no-handoff-after-success change. | All added: §5 rows, `S` defined, S9 completed, AC mapping extended (T7 marked a legacy regression guard), §9 lists what D5 approval covers. |

## Round 2 (same context) — CLOSED

- **Result.** All eight P3 findings are **RESOLVED**, each checked against source. The corrections introduced no P0–P2 defect.
- **New finding N1 (P3, wording only).** Guard (b)'s message was claimed to suit "both Home and Session". Home shows only its existing recovery-failed notice (`HomeViewModel.cs` L564–565), and guard (a) refuses a stale Home click first.
- **Disposition.** The reviewer's suggested wording was applied verbatim after the recheck: shown on the Session screen and to API callers; Home shows its existing notice. This one-sentence change was not re-reviewed.

## Final addendum identity

`SCRUM-11148_DESIGN_ADDENDUM.md`, 288 lines, SHA-256 `90f5d61a2b51f48188766d81d9dd5a652f11494bc576ae2aed643d6559ec5f28`. This is the round-2 text plus the N1 sentence.

## Limits

- **Source reading only.** Source-derived consequences, such as generic Stop rollback under a foreign lock holder or the Stop-without-failure no-op, are not runtime-verified.
- **Jira not re-read.** Jira was not re-read for this addendum; the AC come from the base design's dated 2026-09-28 authenticated read.
- **Future cases NOT RUN.** All future cases in addendum §6 are NOT RUN.
- **Decisions still pending.** D1–D5 remain pending owner approval.
