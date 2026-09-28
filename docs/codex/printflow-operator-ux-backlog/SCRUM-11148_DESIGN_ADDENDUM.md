# SCRUM-11148 — design addendum: Home correction route (C1) and bound-closing scope (C2)

Task `PF-OPUX-v1-SCRUM-11148-design-boundary-closeout-v1` (PLAN_ONLY / REVIEW_ONLY), 2026-09-28 NZ.
**Status: PROPOSED — AWAITING_OWNER_REVIEW.** Nothing here authorizes implementation, migration, or approval of D1–D5. Review record: [SCRUM-11148_DESIGN_ADDENDUM_REVIEW.md](SCRUM-11148_DESIGN_ADDENDUM_REVIEW.md).

## 0. Base and scope

- **Base design:** [SCRUM-11148_COLLEAGUE_HANDOFF_DESIGN.md](SCRUM-11148_COLLEAGUE_HANDOFF_DESIGN.md), 604 lines, SHA-256 `8d4edaec9c9b02d94bde75ac18d85f7a1bbf609bdcd667dc2df21e0c316bd9b2`.
  - It was verified unchanged before this addendum was written. It and its review stay byte-for-byte as they are.
- **Candidate specification:** the base design plus this addendum. Where they differ, this addendum governs, but only for the clauses listed in §5. Everything else in the base design stands unchanged, including:
  - D1–D4;
  - the three product routes;
  - the ProducingWork/afterStart/persisted-request lookup;
  - exact R/U/R2 identity;
  - file protection;
  - the separate review of R2;
  - no automatic background-removal rerun.
- **Requirement source:** the eight AC as quoted in base §1, from the authenticated read-only Jira read of 2026-09-28. Jira was not re-read for this addendum, because the owner said no connector work was needed. No Jira write was made.
- **Evidence labels** are the same as the base design: [SRC] a current-source fact read on 2026-09-28 (`master@eea60198` with the accumulated uncommitted work); [PROP] a proposal; [UNV] unverified. Nothing was built, tested, or run.

## 1. Source findings used by this addendum [SRC]

| # | Fact | Location |
|---|---|---|
| S1 | `RecoveryOf` lists a `HandedOff` or `Active` session whose current step is `Interrupted`/`Failed`/`RetryRequired`/`Processing` and that has an unresolved interruption. It offers `ManualResult` whenever `ManualResultEligibility.CanSubmit` holds, first probing `HandOff` for an Active session. | `Services/SessionService.Recovery.cs` L39–68 |
| S2 | `HasUnresolvedInterruption` is true when the latest attempt of the step is `Interrupted`, or when a Failed/Running `ManualResultImport` chains (via `RetryOfAttemptId`) back to one. A **Cancelled** latest attempt is never listed. | `SessionService.Recovery.cs` L76–89 |
| S3 | `ResolveRecoveryAsync(ManualResult)` takes the gate once and recomputes `RecoveryOf` on a fresh load; a stale action is refused with "This recovery action is no longer available." For a HandedOff item it then runs `ExecuteCoreAsync(new SubmitManualResult(step, path))`, which carries no request identity. | `SessionService.Recovery.cs` L91–114 |
| S4 | `HomeViewModel.RecoverAsync` opens the picker, calls `ResolveRecoveryAsync`, and navigates to the session on success. `OpenRecoveryAsync` only calls `ISessionService.LoadAsync` and `GoToSession`. `LoadAsync` is read-only (load, snapshot, view). | `ViewModels/HomeViewModel.cs` L318–358; `SessionService.cs` L1020–1035 |
| S5 | The Home recovery card always shows Open (`Home.Recovery.Open`, en "Resume", zh-CN "继续"), plus Restart step, Import manual result and Abandon according to the row's flags. | `Views/HomeView.xaml` L124–135; `RecoverySessionRow.cs` |
| S6 | `ProducingWorkOf` builds the import's `ProducingWork` from `ImportManualResult` alone. Today it has no request field; base §6.5 adds `CorrectionRequestId`, which only the dedicated entry sets. | `SessionService.cs` L951–959, L1002–1005 |
| S7 | `ExecuteCoreAsync` already has a service-level `SubmitManualResult` guard before the engine runs. | `SessionService.cs` L519–522 |
| S8 | `SubmitManualResult` sets the session `Active` and the step `Processing`. | `Engine/WorkflowEngine.cs` L1606–1629 |
| S9 | In an `Active` session, `Interrupted` permits `StartStep`, `Retry` and `HandOff`, as well as `KeepOriginalExtent` and `Skip`. A `HandedOff` session refuses every step-scoped command (the only exception is the special Reject). | `TransitionTable.cs` L205–212; `WorkflowEngine.cs` L1719–1731 |
| S10 | `StopAttemptAsync` applies `AttemptCancelled`, which emits `ReleaseAutomationLock` unconditionally. Only `TakeOver` adds a `HandOff`, and that one uses `TakeOverHandOffReason`. `BuildMetadataMutation` turns any `ReleaseAutomationLock` into `LockChange = Release`. | `SessionService.cs` L2768–2821, L3148–3151; `WorkflowEngine.cs` L1585–1603 |
| S11 | A `Release` changes the lock row only if this session holds it, or if the row is fully empty. Otherwise `changed != 1` rolls back the **whole** commit. | `Sqlite/SqliteSessionRepository.cs` L278–287, L923–934 |
| S12 | `FailAttemptAsync`, for any `ManualResultImport`, **replaces** the transition's effects with the `HandOff`'s effects minus `ReleaseAutomationLock`. So `AttemptFailed`'s own release, which BackgroundRemoval emits because it is adapter-backed, is dropped too. The failure close of every manual import, generic or not, is lock-neutral today. | `SessionService.cs` L2682–2690; `WorkflowEngine.cs` L1534–1537 |
| S13 | The success close of every manual import sets `LockChange = null`. A manual import never acquires the lock, because `drivesExternalApplication` is false when a manual path is present. | `SessionService.cs` L2143–2144, L1726, L1811–1834 |
| S14 | `RequestStop` accepts `TakeOver` for **any** running attempt. Only the screen hides TakeOver for internal work. A TakeOver after a successful import triggers `HandOffAfterSuccessAsync`. | `AutomationRuntime.cs` L89–90, L208–243; `SessionService.cs` L2171–2174 |
| S15 | Startup recovery closes crashed attempts with `AttemptInterrupted` on a freshly loaded aggregate. It builds the session with `aggregate.Session with { State = snapshot.SessionState, … }`. It never applies engine lock effects: it releases the lock only when the liveness verdict says this same session holds a stale row. It runs before the shell exists, without the gate. | `StartupRecoveryService.cs` L97–115, L268–351 |
| S16 | Error Details offers Retry, ManualProcessing (`HandOff`) and ReenterAutomation. It offers no manual-result import. | `SessionService.ErrorDetails.cs` L160–197 |
| S17 | **Stop does not interrupt a manual import.** The importer receives only the caller's `cancellationToken`, never the stop signal. `RequestStop` only records the mode. `StopAttemptAsync` runs only when the work *returned a failure* while a stop mode was pending. After a successful import, a pending StopOperation is ignored, and a pending TakeOver goes to `HandOffAfterSuccessAsync`, whose `HandOff` emits `ReleaseAutomationLock`. | `SessionService.cs` L2196–2205, L2031–2047, L2171–2174, L2839–2861; `AutomationRuntime.cs` L208–243; `WorkflowEngine.cs` L1174 |
| S18 | `SessionView.CanSubmitManualResult` is `AvailableCommands.Contains(SubmitManualResult)`. | `SessionView.cs` L658 |

## 2. C1 — the Home recovery import bypass

### 2.1 Counterexample check

The sequence from the task, traced against base §6.4/§8.3 and the unchanged source:

1. **A correction import crashes; startup re-hands it off.** Base §6.4 re-hands off in the startup closing. The session becomes `HandedOff`, background removal `Interrupted`, and the request stays `READY` with `LastImportAttemptId = A1`. It is eligible in mode AfterUnfinishedImport.
2. **Home lists it.** S1 and S2 apply: the latest attempt is Interrupted. The actions are Restart, ManualResult (`CanSubmit` holds in `HandedOff` + `Interrupted`) and Abandon. Base §8.3 L439–441 keeps that ManualResult action and only relabels it.
3. **The Home import runs without the request.** S3 runs a generic `SubmitManualResult`. The request can only be threaded through the dedicated entry (base §6.1 L217 and §6.5), so `ProducingWork.CorrectionRequestId` is null (S6). The opening commit therefore does not set `LastImportAttemptId`. Attempt A2 is Running and the session is `Active` (S8).
4. **The replacement attempt ends unfinished.**
   - **Stop:** Stop takes effect only when the import then fails, or its token is cancelled, while the stop is pending (S17). A2 is not bound, so today's `StopAttemptAsync` applies (S10). The step becomes `Interrupted` and the session stays `Active`. If another holder owns the lock row, the commit rolls back instead (S11); A2 then stays Running until the next startup (the crash case).
   - **Crash:** the persisted row names A1, not A2, so startup treats A2 as unbound. It applies only `AttemptInterrupted`, and the session stays `Active` (S15).
   - **Late failure:** `FailAttemptAsync` re-hands off with the generic F8 text. The session is `HandedOff` + `Failed`. But the latest attempt (A2) no longer equals `LastImportAttemptId` (A1), so the request fails eligibility and becomes history. Run is refused, but the correction route is lost.
   - **Success:** R2 is created without `RETURNED`. The request is obsolete, and the review lacks the AC5 next-step text.
5. **Stop and crash end in `Active` + `Interrupted`.** `StartStep` and `Retry` are legal there (S9). A normal Run issues `StartStep`, which runs Meitu background removal again. The request needs `HandedOff`, so it shows as obsolete.

**Verdict.** The counterexample is **confirmed as a defect of the base design**. The crash variant reaches `Active` + `Interrupted` unconditionally; the Stop variant reaches it when the stopped import then fails. It contradicts AC4 ("a normal Run does not resume it") and the AC5 intent ("not background removal again"). The failure and success variants do not expose Run, but they still silently drop the binding.
- **Nature:** a design inconsistency. Base §6.4 protects bound imports; §8.3 keeps an unbound entry into the same state.
- **Evidence:** every step's consequence follows from named, current source branches, together with the proposed components as written.
- **Runtime:** not tested. No build or run was performed.

### 2.2 Corrected call path [PROP]

The Home correction action **navigates**; it does not import. The existing `OpenRecoveryAsync` already does exactly the read-only load and navigation that is needed (S4). A direct Home call to `ImportCorrectedImageAsync` would duplicate, on Home, the whole S4 interaction of the Session screen: the picker's initial folder, preflight, "Checking the picture…", the refusal texts and focus. That is not simpler, so it is not proposed.

1. **Read model.** `RecoveryItem` gains `bool HasOpenCorrection`. `RecoveryOf` computes it with `CorrectionRequestEligibility.Resolve(state, aggregate)` on the aggregate it already loaded (true when mode AfterUnfinishedImport resolves).
   - When it is true, `RecoveryAction.ManualResult` is **not added**.
   - Restart and Abandon are computed as today.
   - The flag is display only: it grants nothing.
2. **Service authority (two guards, both on a fresh load under the gate that already exists).**
   - (a) `ResolveRecoveryAsync` recomputes `RecoveryOf` (S3). A stale Home click on Import manual result for such a session is therefore refused with the existing "This recovery action is no longer available."
   - (b) The `SubmitManualResult` guard in `ExecuteCoreAsync` (S7) gains one clause. When `CorrectionRequestEligibility.Resolve` yields mode AfterUnfinishedImport **and** no `CorrectionContext` was supplied, it refuses with `PreconditionNotMet` and a localized `messageKey` `Session_CorrectionUseImport`. It is shown on the Session screen (and returned to API callers). Home shows its existing recovery-failed notice, and a stale Home click is refused first by guard (a):
     - en: "This job is waiting for a colleague's corrected picture. Use Import corrected image in this job."
     - zh-CN: "此任务正在等待同事修正后的图片。请在此任务中使用“导入修正后的图片”。"
   - Guard (b) covers every generic caller: `ExecuteAsync`, `ResolveRecoveryAsync`, and a stale Session screen.
   - The dedicated entry supplies the context (base §6.4 AfterUnfinishedImport route), so it is unaffected.
   - `ManualResultEligibility.CanSubmit` and the engine are **unchanged**.
3. **View agreement.** `SessionView.CanSubmitManualResult` becomes today's value (S18) `∧ ¬(eligible request in AfterUnfinishedImport)`, so the generic button's availability matches what the service would accept. This refines base §6.4 L298. The UI suppression in base §6.4 L300–303 stays.
4. **Home UI.**
   - When `HasOpenCorrection` is true, the card shows a **first-position** button bound to the existing `OpenRecoveryCommand`: `Home.Recovery.OpenCorrection`, labelled `Home_RecoveryOpenCorrection` (en "Open job to import corrected image", zh-CN "打开任务以导入修正后的图片").
   - The plain Open button is hidden for that row, and the waiting line `Home_RecentWaitingForCorrection` (base §8.5) is shown under the description.
   - Restart step and Abandon stay visible after it. They are existing, explicitly chosen alternatives: never recommended and never a fallback.
   - Rows without `HasOpenCorrection` keep today's buttons, labels and AutomationIds.
5. **Navigation writes nothing.**
   - Opening the job issues no command. It does not import, approve, re-hand-off, start a processor, or touch the request row.
   - The Session screen then loads a fresh view. If the request is still eligible, the S3 panel offers Import corrected image. If it became obsolete in the meantime, the panel shows `Session_CorrectionObsolete` (base §8.5) and only the lawful existing actions.

**Gate discipline.** Navigation takes no gate. `RecoveryOf` evaluates eligibility on the aggregate that is already loaded, with no nested acquisition. No gate is held while the picker is open. This path takes no automation lock.

### 2.3 Legacy and unchanged behaviour

- **Sessions with no eligible CorrectionRequest are unchanged.** This covers every legacy generic handoff, TakeOver handoff, crash-interrupted automated step, and obsolete request. Specifically unchanged:
  - `RecoveryOf` and its actions;
  - the Home labels and AutomationIds;
  - `ResolveRecoveryAsync`, including its Active-session `HandOff` + `SubmitManualResult` pair;
  - the Session screen's generic Submit;
  - F19 (Reject, then submit).
- **No request is inferred.** Nothing is derived from `HandOffReason` text (including a text equal to `DefaultReason`), from a file or folder name, or from `HasOpenCorrection` alone. Eligibility needs a persisted `READY` row that satisfies base §6.4.
- **Restart step on Home and Return to automation in Error Details or Session "Other options" stay lawful, explicit choices.** Choosing one turns the request into history; it is never done implicitly.
- **If D1 is declined, C1 cannot be closed reliably.** The base §4.2 fallback has no request row, so the guards above would have to key on reason text, which this addendum rejects. That is one more reason D1 is recommended.

## 3. C2 — bound closing scope

### 3.1 Inconsistency check

- **The documents disagree.** Base §6.4 L286 and D5 (§13 L589) restrict the new closing behaviour to attempts bound to an open request, and state that generic imports keep today's behaviour. Base §6.4 L293, and the review's N1 disposition, set `LockChange = null` "for a `ManualResultImport` attempt", without the request predicate. **This is a document inconsistency. It is confirmed.**
- **Source shows the broad wording is unnecessary.** Today the success close (S13) and the failure close (S12) of every manual import are already lock-neutral.
  - Only two closings of a manual import can release the lock: the **Stop** close (S10), and TakeOver-after-success (S17), which is reachable only through the service API. Either can roll back when another holder owns the row (S11).
  - Startup never applies engine lock effects (S15).
  - D5 therefore needs lock neutrality only on those seams, and only for bound attempts: a bound Stop filters the release, and a bound success does no handoff.
  - **A broader change is avoidable, and none is proposed as part of D5.**
- **Base F21 (L73) is only partly true.** Its clause "manual-import closings set `LockChange = null`" holds for the success and failure closes only.
- **Observation, not proposed.** A *generic* manual-import Stop close, or an API-only TakeOver after a generic success, can roll back while another session or environment verification holds the lock row. In the Stop case the attempt then stays Running until startup recovery closes it (source-derived; runtime [UNV]).
  - Making generic import closings lock-neutral would be the smallest fix. It is **not** part of D5 or this Task; if the owner wants it, it needs its own decision and task.
  - This parallels base §3's observation about the generic `HandOff` release.
- **Observation, not proposed.** Stop is offered during an import but does not interrupt it (S17). It takes effect only if the import then fails. Base §6.4 L296 calls it "a legitimate cancellation"; in practice a Stop against a successful import is ignored. This addendum does not change that.

### 3.2 Binding predicate [PROP]

`BoundCorrectionClose(A, r)` holds for the attempt A being closed now, in session S (the session whose attempt is being closed), and a request row r, only when **all** of the following hold:

1. `A.Operation == ManualResultImport`, `A.Step == BackgroundRemoval`, `A.Status == Running`, and `A.SessionId == S`.
2. `r.SessionId == S`, `r.StepKind == BackgroundRemoval`, and `r.Status == READY`.
3. `r.LastImportAttemptId == A.Id`.
4. `A.InputRevision == r.ReferenceRevisionId` (U).

Where r comes from:
- **Live closings (`FailAttemptAsync`, `StopAttemptAsync`, and the success close):** r is the row whose id is `ProducingWork.CorrectionRequestId`, read from the `afterStart` aggregate. That aggregate already includes the opening commit's request change (base §6.4 L291, retained).
- **Startup closing:** r is the unique `READY` row of the freshly loaded aggregate whose `LastImportAttemptId == A.Id`. There is at most one, because of the one-open-request index.
- **Never** from the pre-opening aggregate, and never from reason text, filenames or display flags.

**Fail-closed rule (live closings only).** If `ProducingWork.CorrectionRequestId` is non-null but the predicate fails on `afterStart`, an internal invariant has been broken. This cannot happen by construction: the gate is held from the opening commit to the close, and only gated entries write request rows. Then:
- **An unfinished close (Failed or Cancelled)** still commits the attempt closure and sets `HandedOff` with the neutral stable reason `CorrectionImportUnfinishedReason` = "The correction import did not finish; manual processing remains authorised." It makes no lock change and no request change. The request then shows as history, and the job is never left `Active` with background removal restartable.
- **A success close** commits today's success unchanged: no `RETURNED` and no handoff. R2 goes to review; the request shows as history.

Startup has no `ProducingWork`. There, a crashed attempt whose persisted row fails the predicate is unbound, and today's startup close applies (`Active` + `Interrupted`). By construction this is also unreachable, and it is recorded as a residual (§8).

**Every other attempt is unbound,** including every generic `ManualResultImport`. It keeps exactly today's code path in all three seams.

### 3.3 Closing outcomes

| Outcome | Bound (predicate holds) | Live fail-closed (id set, predicate fails) | Unbound (unchanged) |
|---|---|---|---|
| **Success** | Existing success commit, plus row `RETURNED`, `ResultRevisionId = R2`, `ClosedAtUtc`, in one transaction. `LockChange = null` (already true today, S13). A stop request pending after success, in **either** mode, does not hand off: no `HandOffAfterSuccessAsync`. | Today's success commit; no `RETURNED`, no handoff | Today's behaviour, including TakeOver after success (S14, S17). |
| **Failed** | `AttemptFailed`, then `HandOff(step, r.EffectiveReason)`. The effects are replaced exactly as today (S12), so there is no lock change. Step `Failed`; session `HandedOff`. | `HandedOff` with `CorrectionImportUnfinishedReason`; no lock change; no request change | F8 generic text; lock-neutral as today. |
| **Cancelled (Stop; only when the import then failed with a stop pending, S17)** | `AttemptCancelled`, then **one** `HandOff(step, r.EffectiveReason)`, in either stop mode; `TakeOverHandOffReason` is not used. **All** `ReleaseAutomationLock` effects of both transitions are removed, so `LockChange = null`. Step `Interrupted`; session `HandedOff` with `HandedOffAtUtc` = close time. The automation-log row is written as today. | As in the Failed row, with step `Interrupted` | `AttemptCancelled` releases the lock, and TakeOver adds its own HandOff (S10). The session stays `Active` for StopOperation. |
| **Interrupted (startup)** | `AttemptInterrupted`, then the engine `HandOff(step, r.EffectiveReason)` applied to the snapshot (legal from Active + Interrupted, S9), so the transition stays engine-validated. Its effects are ignored, as startup already does. The session is built with `State = HandedOff`, `HandOffReason = r.EffectiveReason` and `HandedOffAtUtc = nowUtc`. `LockChange` stays the existing liveness decision (S15), which can only release a stale row held by this same crashed session. | Not applicable: startup has no `ProducingWork` (§3.2) | `AttemptInterrupted` only; the session stays `Active`. |

**Committed together.** In every bound unfinished close, one transaction holds:
- the attempt closure;
- the step state;
- the `HandedOff` session with the request's original `EffectiveReason`;
- a request assertion `CorrectionRequestChange.AssertBound(r.Id, READY, A.Id)`.

The assertion is a conditional no-op update: `changed != 1` rolls the commit back, the same pattern as the lock row (S11). The request stays `READY` with `LastImportAttemptId = A.Id`, and the note survives in the row. No lock row belonging to another session, or to environment verification, is released or changed.

**No new locking.**
- No recursive gate: the live closings run inside the dedicated entry's single acquisition, and startup runs before the shell, without the gate (S15).
- No automation lock is taken for correction-package work or for an import (S13).
- No gate is held while a person or picker is involved.

## 4. Compact updated transition table (correction-bound session)

| From | Event | To (session / step) | Request | Lock row |
|---|---|---|---|---|
| S3 Review or AfterUnfinishedImport | Session **Import corrected image** (dedicated entry) | S5: Active / Processing | `LastImportAttemptId = A` (opening commit, also applied to `afterStart`) | untouched |
| S3 AfterUnfinishedImport, on the Home card | **Open job to import corrected image** | unchanged; navigates to the Session S3 panel | unchanged | untouched |
| S3 AfterUnfinishedImport | generic Submit (Home, Session or API) | refused (§2.2 guards); unchanged | unchanged | untouched |
| S3 AfterUnfinishedImport | Home Restart step / Return to automation (explicit) | Active / Waiting (existing `ReenterAutomation`) | history | untouched |
| S5 | success | S6: Active / ReviewRequired(R2) | `RETURNED`, `ResultRevisionId = R2` | untouched |
| S5 | failure | S3 AfterUnfinishedImport: HandedOff / Failed | `READY`, asserted | untouched |
| S5 | Stop (either mode), and the import then fails | S3 AfterUnfinishedImport: HandedOff / Interrupted | `READY`, asserted | untouched |
| S5 | Stop (either mode), but the import succeeds | S6: Active / ReviewRequired(R2), no handoff | `RETURNED` | untouched |
| S5 | crash, then startup | S3 AfterUnfinishedImport: HandedOff / Interrupted | `READY`, asserted | only this session's stale row, if the liveness verdict says so (existing) |
| Obsolete request (for example after an explicit Restart) | any | existing generic paths, including F19 | history | existing |

Updated rows of base §7.1:
- **S3.**
  - *Session:* unchanged from base.
  - *Home:* only a crash-interrupted import, or a failed import chained to one, is listed (S2). The card offers **Open job to import corrected image** first, then Restart step and Abandon. It has **no** Import manual result.
  - *Recent:* "Details" plus the waiting line (base §8.3).
  - Writes: none. Home navigation writes nothing.
- **S4.** Reached only from the Session S3 panel; otherwise unchanged.
- **S5.** Stop is offered as today, and takes effect only if the import then fails (S17). TakeOver is not offered for internal work, and if a TakeOver request arrives through the service it is treated like Stop for a bound attempt. Closings follow §3.3.
- **S6.** As base, plus: a stop request that arrived during a successful bound import does not hand the session off.

## 5. Superseded and amended base clauses (exact)

| Base location | Change |
|---|---|
| §6.1 L217–218 (`CorrectionContext` guard) | **Amended.** `ExecuteCoreAsync` additionally refuses a generic `SubmitManualResult` when an eligible request in AfterUnfinishedImport exists and no `CorrectionContext` is supplied (§2.2 guard b). |
| §2.1 F21 (L73), clause "manual-import closings set `LockChange = null`" | **Clarified.** True of the success and failure closes only; the Stop close and TakeOver-after-success can release the lock (§3.1). |
| §6.4 L279–286 ("A narrow rule applies wherever …") | **Superseded** by §3.2–§3.3. The intent is kept; binding is now by the explicit predicate. |
| §6.4 L289–292 (identifying a bound attempt) | **Refined** by §3.2: same sources (`ProducingWork.CorrectionRequestId` with `afterStart`; the persisted row at startup), now inside the explicit predicate, with the fail-closed rule. |
| §6.5 L320 ("A late change or fault fails the attempt as today (F8)") | **Amended.** A bound failure re-hands off with `r.EffectiveReason`, not the F8 text (§3.3). |
| §6.4 L293 (`StopAttemptAsync` "For a `ManualResultImport` attempt, the whole closing commit sets `LockChange = null`") | **Superseded.** Lock neutrality applies only when `BoundCorrectionClose` holds. Unbound Stop is unchanged (§3.1, §3.3). |
| §6.4 L294 (startup recovery bullet) | **Superseded** by the §3.3 Interrupted row: engine `HandOff` on the snapshot, explicit session fields, existing liveness-based lock decision. |
| §6.4 L298 ("`CanSubmitManualResult` keeps its service meaning") | **Amended** as in §2.2 item 3. |
| §7.1 rows S3, S5, S6 | **Amended** as in §4. |
| §7.2 row "Late failure, Stop or crash during import" | **Amended.** Add: the lock row is untouched, and the request assertion is in the same transaction. |
| §8.3 Home recovery bullets L439–442 | **Superseded** by §2.2: no Home import for an eligible request; navigation instead. The "[PROP, small] Label that Home action 'Import corrected image'" item is **withdrawn**. |
| §9 rows "Stop pressed during import", "Crash or lost closing commit" | **Amended** per §3.3. New rows: "Home Import manual result on a correction job → refused, no longer available"; "generic Submit while a request is eligible → refused with the plain message". |
| §10 AC4 and AC5 evidence | **Extended** by §6. |
| §11 | **Extended** by §6. |
| §12 slices 1, 3, 4 | **Amended** by §7. |
| §13 residual "An import through Home recovery bypasses the request…" | **Removed** for the new request-bound route. Legacy Home import is unchanged (§2.3). D5's text stands; "bound" means `BoundCorrectionClose` (§3.2). |
| Review N1 disposition ("for a `ManualResultImport` close") | **Superseded** by §3. The review file itself is not edited; see the addendum review. |

## 6. Future verification for the changed boundaries (NOT RUN)

**Fixtures.**
- `SessionServiceHarness`: GUID temp database and workspace, temp lease, fake Meitu with a call count, `CreateService()`, and `CreateRecoveryService(FakeProcessLiveness)`.
- **A new gated `IManualResultImporter` test double**, modelled on `TimedImporter` in `ManualResultImportTests` (L302–312).
  - It blocks until the test releases it, then returns a scripted outcome: success, or failure.
  - Stop cases call `RequestStop` while it is blocked, then release it with a failure (S17).
  - A double that waits on its token would never be released by Stop.
- **Running-attempt seeding for startup closes:**
  - `FaultingRepository { FailFromCommit = N }` (`Fixtures/FinalReviewFaults.cs` L139–173, as used in `RecoverySurfaceTests` L32), which loses the closing commit after a committed opening; or
  - a Running attempt committed directly (`RecoverySurfaceTests.Seed` pattern, L212–220).
  - Then `CreateRecoveryService(FakeProcessLiveness)`.
  - `SimulatedProcessDeath` only removes static gates after a simulated recovery. Use it only alongside a gate-holding blocked importer.
- `SyntheticImages` PNGs.
- The foreign-session lock seed from `ManualResultImportTests` L197–200.
- The environment-verification lock setup from `StartupRecoveryTests` L39–47 (`SqliteEnvironmentAutomationLock.TryAcquireAsync`).
- `HomeViewModel` with the fake navigation and picker used by `RecoverySurfaceTests`, with no `Window.Show`, UIA or `ApplicationStartup`.

**Cases.**

| # | Case | Assertions |
|---|---|---|
| T1 | Home navigation keeps the association and writes nothing | A bound import crashes, then recovery runs. The recovery item has `HasOpenCorrection` and Actions = [Restart, Abandon] (no ManualResult). `OpenRecoveryCommand` navigates to that session, and the navigated view keeps the association: `RecordingNavigation`'s view has `CorrectionHandoff.RequestId == r.Id`, and `CanImportCorrectedImage` is true. The picker call count is 0. All rows are identical before and after (session, steps, attempts, revisions, request, lock). |
| T2 | Stale generic import refused | On the T1 session: `ResolveRecoveryAsync(ManualResult, path)` gives "no longer available"; `ExecuteAsync(SubmitManualResult)` gives the plain refusal. No attempt is added; the request row is unchanged. |
| T3 | Return after a crash, then an unfinished close | T1, then the dedicated `ImportCorrectedImageAsync` in AfterUnfinishedImport. The new attempt A2 is then stopped (StopOperation, and TakeOver through `RequestStop`; the gated importer then fails), failed, or has its closing commit lost (`FaultingRepository`) and is closed by startup recovery. In each case: `HandedOff` with `r.EffectiveReason`; `LastImportAttemptId = A2`; `CanImportCorrectedImage` true; `StartStep` and `Retry` refused; fake Meitu calls = 0. |
| T4 | Successful return that started from Home | T1, then a successful dedicated import. Exactly one new Revision R2: `ManualResultImport`, `SourceRevisionId = U`. The request is `RETURNED` with `ResultRevisionId = R2`. The step is `ReviewRequired` with the next-step text. No processor call. A stop requested while the gated importer is blocked, in either mode, followed by success, does not hand off. The lock row is unchanged. |
| T5 | Stale or obsolete request | (a) Home Restart step makes the request obsolete. A later dedicated import with the old id is refused, the reload shows `Session_CorrectionObsolete`, and no generic import happens. (b) A Home card built before the request became obsolete navigates only. |
| T6 | Bound close with a foreign lock holder | The lock row is held by another session, and in a second run by environment verification. Bound Stop, failure and startup closing (liveness reports the holder alive) all commit. The lock row's fields are byte-identical (SessionId, AcquiredAtUtc, ProcessId, MachineName, Purpose, OwnerToken). |
| T7 | Unbound contract unchanged | A generic manual import's Stop leaves `Active` + `Interrupted` and still emits the release (`StopAndTakeOverTests.Stopping_releases_the_global_automation_lock` pattern). Generic failure uses the F8 text. F19 (`A_handed_off_review_offer_must_be_rejected_before_manual_replacement`) passes. A legacy session whose `HandOffReason` equals `DefaultReason`, and whose returned file is named `… - CORRECTED.png`, shows no correction, is offered the Home ManualResult, and keeps generic Stop behaviour. |
| T8 | Opening-commit propagation | *Live:* a recording repository decorator (a `FaultingRepository`-style wrapper) shows the Stop/failure closing mutation carries `AssertBound(r, A2)`. Use a second bound import after a crash, so an implementation reading the pre-opening `LastImportAttemptId = A1` fails the test. *Restart:* `FaultingRepository { FailFromCommit }` loses A2's closing commit after its opening commit; then startup recovery reads the persisted row and hands off. |
| T9 | Fail-closed predicate | A pure unit test of the closing builder. With a non-null `CorrectionRequestId` whose row does not match: an unfinished close gives `HandedOff` with `CorrectionImportUnfinishedReason`, no lock change and no request change; a success close gives today's success with no `RETURNED` and no handoff. |

**Also.** Add the `RecoveryOf` rows to the Home recovery tests, and one localized Home render with the new label in en/zh-CN, using the existing off-screen renderer. Keep these existing tests green: `StopAndTakeOverTests`, `ManualResultImportTests`, `ManualResultEligibilityTests`, `RecoverySurfaceTests`, `StartupRecoveryTests`. The real picker, Explorer and physical keyboard checks stay human checks, NOT RUN.

**AC mapping additions.**
- **AC4** gains T1, T2, T3, T6, T8 and T9.
- **AC5** gains T2, T4 and T5.
- **T7** is a regression guard for the existing legacy contract (F19, generic import), not an SCRUM-11148 AC.

## 7. Amended implementation slices

- **Slice 1 (persistence, only if D1 is approved):** add the `CorrectionRequestChange.AssertBound` conditional update, rolled back on `changed != 1`.
- **Slice 3 (service):**
  - the `BoundCorrectionClose` helper, used by `FailAttemptAsync`, `StopAttemptAsync`, the success close and `StartupRecoveryService`;
  - bound Stop: one HandOff, all releases filtered, stop mode normalised;
  - no `HandOffAfterSuccessAsync` for a bound success;
  - the fail-closed rule;
  - `RecoveryOf`'s `HasOpenCorrection` and ManualResult omission;
  - the `ExecuteCoreAsync` generic-submit guard;
  - the `CanSubmitManualResult` refinement.
- **Slice 4 (App):** `RecoveryItem`/`RecoverySessionRow` flag and label; the HomeView first-position button `Home.Recovery.OpenCorrection` with the plain Open hidden for that row; `Home_RecoveryOpenCorrection` and `Session_CorrectionUseImport` in en/zh-CN; the waiting line on the card.
- **Unchanged:** slices 2 and 5.

## 8. Residuals after this addendum

- **Legacy Home import is unchanged.** Legacy sessions without a request keep today's Home import, including the Active-session `HandOff` + `SubmitManualResult` pair and its lock release [UNV at runtime].
- **Generic manual-import Stop and API-only TakeOver-after-success are unchanged.** They still release the lock and can roll back while another holder owns it (§3.1 observation; not fixed).
- **Stop does not interrupt a running import (S17).** It affects only an import that then fails. This is existing behaviour, unchanged.
- **Startup asymmetry.** A crashed correction attempt whose persisted row fails the predicate would close unbound, leaving `Active` + `Interrupted`. By construction this is unreachable (§3.2).
- **A stale Home card can open the picker before the refusal.** On a legacy-looking card that has since become a correction job, the picker opens before the service refuses (existing `RecoverAsync` order; the case is rare and nothing is written).
- **Explicit Restart step or Return to automation leaves the request as history.**
- **Every other base §13 residual stands.**

## 9. Owner-decision boundary (unchanged)

- **D1–D5 remain pending owner approval.**
- **D5 keeps its base wording.** "Bound" is now defined by §3.2, and D5 does not include any change for generic imports. Approving D5 now explicitly covers these changes, all limited to bound attempts:
  - the bound Stop close (one HandOff with the request's reason, in either stop mode, with no lock change);
  - the bound startup close;
  - the request's reason on a bound failure;
  - the fail-closed rule;
  - **a bound success with a pending stop request (either mode) does not hand off.** This is a change to today's TakeOver-after-success outcome.
- **The C1 changes belong to the candidate specification and add no new decision.** Their guards apply only when a persisted eligible request exists, so they have no effect on legacy behaviour. They depend on D1.
- **Generic manual-import Stop lock neutrality is not proposed.** It would need its own owner decision and task.
- **A favourable review decides nothing.** Implementation needs owner approval of the base design, this addendum and D1–D5, plus a separate authorization.

**Not run.** Builds, tests, the application, picker, Explorer, desktop, migrations, Jira writes and CSV regeneration.
