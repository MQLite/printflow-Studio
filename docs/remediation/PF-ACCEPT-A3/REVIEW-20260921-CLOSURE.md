# Independent review record — SCRUM-11130 evidence closure, 21 September 2026

Recorded under Prompt 26. This is the review record only; the matrix, the identities and the
outcome are in `HANDOFF.md`, section "PF-ACCEPT-A3 closure handoff — 21 September 2026".

## Mechanism and its actual limits

| Fact | Value |
|---|---|
| Reviewer | One scoped `personal-dev-reviewer` subagent, isolated context, `Read`/`Glob`/`Grep` only |
| Isolation | Genuine: no shell, no execution, no hashing, no delegation, no access to the executor's reasoning or chat history. It received the verbatim criterion, an evidence index and source pointers |
| Rounds | Two, both by the same reviewer: one full review, then one scoped recheck of the rows that moved. No second full audit, no replacement reviewer |
| Real limitation | It could not execute, build, hash or re-run anything, and could not diff against `fee557e`. Every file-level hash in these documents is the executor's measurement, accepted by the reviewer as such |
| Status line | **INDEPENDENT READ-ONLY RECORD REVIEW COMPLETED; INDEPENDENT RE-EXECUTION NOT PERFORMED** |

## Findings, severity and disposition

| # | Severity | Finding | Disposition |
|---|---|---|---|
| F1 | MAJOR | The 2026-09-17 full suite ran from build pair `393c45f8` (`b2cb93b…`), not the accepted candidate pair `d915b1a6` (`6818757…`). The executor's premise was wrong | **Accepted and corrected.** Applicability re-established by an exact 614-input manifest comparison: the two pairs differ in exactly one file, `A2MeituCutoutValidationRecoverySmoke.cs`, which no matrix row cites |
| F2–F4 | verified | Golden path C1/C2/C3: one completed session, five SUCCEEDED attempts, zero retries; three same-session APPROVED decisions with ids inside the session's own window; `PROMOTE_APPROVED` SHA-256 identical to the reviewed Trim revision | No defect |
| F5 | MINOR | "Source remains untouched" is not establishable from the record: `InputSnapshot` holds no hash of the original file | **Accepted and carried forward** as an explicit reliance on file-level measurement |
| F6 | verified | All 53 `variant-contracts` names present and `Passed` in the retained suite | No defect |
| F7 | verified | `RecoverySurfaceLiveSmoke` returns immediately without its opt-in variable; its suite row (0.97 ms) is a no-op, not a re-execution | No defect; drove the one supplementary run |
| F8 | MAJOR → **withdrawn** | "Automatic retry has no evidence of anything automatic" | **Withdrawn by the reviewer** after the design was read: §20's 自动重试 is the retry of an automated step (§7.2 transition, invariant 8, the 自动化重试率 metric), which the retained contracts prove |
| F9 | MAJOR → **withdrawn** | "Unknown dialog is unsupported by anything readable" | **Withdrawn**: the executor had failed to stage `guarded-meitu-export-regression-disabled-welcome.trx` (40/40). With it staged and cross-checked, the row is supported at harness level |
| F10 | MINOR | `session-record.json` carries no `AutomationLock`/`AutomationLogEntry` rows; in-run lock state is not evidenced there | **Accepted and carried forward** |
| F11 | MINOR | The golden path's trim applied no crop (content bounds equal to the full canvas) | **Accepted**; already disclosed in §9 of the report |
| F12 | OBSERVATION | Operator attribution is documentary, not record-provable; the executor's own in-flow invocation of 对此图片使用自动选择 is disclosed | **Accepted**; not restated as record-proven |
| F13 | MINOR | The 10 September TRX files carry a working-tree `bin\Release` storage path, not a pinned build pair | **Repaired** for the recovery and takeover rows by the candidate-pinned re-execution; **still stands** for the guarded-Meitu TRX, where the retained-suite cross-check carries applicability instead |
| F14 | OBSERVATION | No raw A3 artefact names the binary that produced the session; the binding is a document claim | **Accepted and carried forward** |
| F15 / N5 | limitation | The reviewer cannot execute or hash | **Recorded**, and it is why the banner is amended rather than removed |
| N1 | MINOR | The executor over-read `IMeituUiDriver.cs:321`: its `§9` is Epic 11300 Part D2A §9 "Restart" and the remark governs the cancel control, not workflow retry | **Accepted and corrected** in the closure documents |
| N2 | OBSERVATION | The focused TRX has 40 `GuardedMeituExportTests`; the retained suite has 41 (one added later). 41 ⊇ 40 | **Accepted**; the two counts are never written as the same set |
| N3 | MINOR | The guarded-Meitu TRX is itself unpinned | **Accepted**; applicability comes from the suite cross-check |
| N4 | OBSERVATION | The closure re-execution is genuinely new work, pinned to the accepted candidate pair, with new session ids | **Accepted** |
| — | withdrawn | "The 'changing output' sub-claim is unsupported" | **Withdrawn** after the 27 `MeituOutputValidationTests` settle rows were read |

## Reviewer conclusion, as delivered

The golden-path clause is supported. All six variant clauses are supported — four at
harness/contract level (rejected review, automatic retry, unknown dialog, output-validation
failure), two at harness plus candidate-pinned synthetic-live level (restart recovery, manual
takeover). None is an ordinary production observation on the fixed workstation. SCRUM-11130 must not
be moved to FULL. No demonstrated Product defect was found, and **nothing blocks starting
SCRUM-11131**.
