# PrintFlow Studio — Epic 11300 Part D1

## Meitu runtime failure, interruption and recovery

Date: 2026-08-25  
SDK: .NET 10.0.400  
Starting baseline: 7,450 passed, 0 failed, 0 warnings, 0 errors, no vulnerable packages  
Final result: 7,467 passed, 0 failed, 0 warnings, 0 errors, no vulnerable packages

Part D1 is complete as a small reliability slice around the existing Enhancement and reviewed-content Background Removal production routes. It does not add Stop, force termination, generic desktop automation, global Production mode or Photoshop.

## 1. Failure taxonomy

The taxonomy follows the real boundary that failed and reuses existing `FailureCode` values where they already say the right thing.

| Boundary | Failure code | D1 meaning |
|---|---|---|
| Accepted executable absent or changed | `MeituNotInstalled` | Preflight refusal; no Meitu input |
| Launch exits before presenting a verified window | `MeituLaunchFailed` | Readiness failure |
| No attachable top-level window | `MeituWindowNotFound` | Readiness failure |
| Verified process exits after the operation starts | `MeituTargetLost` + `Failure_MeituClosed` | Structured closed/unavailable result; no later input |
| Verified window disappears or its HWND changes owner | `MeituTargetLost` | Target identity lost; handle reuse is never trusted |
| Required foreground cannot be reacquired | `MeituTargetLost` | Input is abandoned unsent |
| Editor/dialog becomes unrecognised | `MeituUnknownState` | Immediate stop; no navigation back is attempted |
| Owned blocking modal | `MeituBlockingDialog` | Stop and leave it for the operator |
| Busy does not appear or does not complete | `Timeout` | Bounded failure; no second invocation or export |
| Signed Save/destination surface is missing | `MeituOpenInputFailed` | No guessed surface and no fallback input |
| Controlled output does not appear | `OutputMissing` | No Revision |
| Output remains empty, changing or unreadable | `OutputUnreadable` | No Revision; artefact is left alone while Meitu may hold it |
| Readable output fails format/dimension/alpha rules | `OutputValidationFailed` | No Revision |
| Orchestration cancellation | `Cancelled` + `Failure_MeituInterrupted` in production | No Meitu Cancel click; retained external state is recorded as unknown |
| PrintFlow restarts over persisted Running work | `Interrupted` attempt state | Startup recovery, not filesystem-inferred success |

No new failure code was added merely to distinguish branches. Runtime distinctions that share a stable code use persisted message keys and structured context.

## 2. Pre-attempt versus in-attempt

The existing split remains explicit:

- missing Background Removal reviewed-content authority is rejected by the workflow before `RecordAttemptStarted`;
- the production environment gate is evaluated before the opening attempt transaction;
- invalid Meitu request references and unsupported decisions remain precondition failures;
- once the opening transaction commits, process/window loss, timeout, cancellation and output failure close the existing attempt and cannot be reclassified as a product refusal.

Tests continue to assert that missing authority and gate refusal create no attempt. An authorised Background Removal adapter failure does create a failed attempt carrying the authority it actually used.

## 3. Process, window and foreground failures

`GuardedMeituUiDriver.RefreshOwnedWindow` now normalises every post-verification process/window loss:

- a dead process returns `MeituTargetLost` with `targetLoss=process-exited`, `retainedExternalState=gone` and the concise closed-Meitu message;
- a missing window returns `targetLoss=window-disappeared`;
- a reused HWND records expected and actual process ids and produces no input;
- foreground remains optional during read-only polling, but every next input still requires reacquisition and exact target verification.

The same process-exit treatment applies while signed export surfaces are open. Raw Win32 exceptions do not cross the adapter boundary.

## 4. Busy timeout and cancellation

Both operations now classify Busy-start and Busy-completion budget exhaustion as `Timeout`, rather than reporting an unknown screen. Each timeout records the wanted/last phase, confirms that no Revision was created, and records whether retained Meitu state may still be Busy.

Unknown state is separate: if no operation-specific phase is visible and the full editor inspection is `Unknown`, orchestration stops immediately. Known modals still stop without dismissal.

Cancellation behavior is pinned at three boundaries:

- cancellation before Enhancement or Background Removal input produces no operation invocation;
- cancellation during read-only Busy observation produces no later input and never clicks Meitu's `取消`;
- cancellation after the destination is prepared but before export confirm wins before the irreversible confirm invocation.

`ProductionMeituProcessor` maps cancellation to a structured `Cancelled` result. `SessionService` uses an uncancelled metadata token only for the short closing transaction, so the attempt becomes Failed/Cancelled-by-code and the global automation lock is released. A process crash before that closing transaction is still handled by startup recovery.

## 5. Output and partial-file failures

The success boundary is unchanged: only a controlled output that appears, stabilises, reads end-to-end, passes operation-specific validation, and leaves its source unchanged can become a Revision.

Covered failure shapes include:

- no file at the controlled path;
- zero-byte file;
- partially written or never-stable file;
- corrupt PNG;
- non-PNG bytes under a `.png` name;
- invalid dimensions or Background Removal alpha/foreground content;
- output created somewhere other than the controlled path.

No failure path promotes these files. D1 does not hard-delete anything Meitu may still hold. On restart, existing recovery quarantines attributable unreferenced files from Failed/Interrupted/Cancelled attempt directories and never touches Source, Approved, Rejected or files referenced by a successful Revision.

## 6. Cleanup-warning boundary

Validated output success remains independent from UI cleanup success. A failure to dismiss the exact signed result surface or return the editor to neutral state becomes a `WARNING:` in adapter notes; it does not retroactively fail the output or remove the Revision.

D1 adds migration `0004_attempt_adapter_notes.sql` and persists those notes on the successful `ProcessingAttempt`. The warning therefore survives restart instead of existing only in the in-memory adapter result. A retained unsafe Meitu screen still causes the next attempt's ordinary safe-state inspection to fail closed.

## 7. Startup recovery

Recovery remains owned by `StartupRecoveryService` in Workflow. Infrastructure only reports runtime observations.

For both Meitu operations, a persisted Running attempt whose owner is confirmed dead is changed to Interrupted in the session transaction, with:

- `OutputRevisionId == null`;
- no fabricated Revision or PrintOutput;
- the step moved to Interrupted when the workflow accepts the recovery command;
- the stale automation lock released only after the existing liveness/ownership check;
- files considered for quarantine only after metadata commit.

Alive or unverifiable lock owners remain protected and are never stolen.

## 8. Partial-output recovery

The exercised Background Removal crash scenario leaves a working copy plus a partial PNG in the interrupted attempt directory. Restart recovery:

- changes the Running attempt to Interrupted;
- creates no cutout Revision;
- quarantines both attributable unreferenced files;
- leaves the successful Enhancement Revision, imported source and Approved area unchanged;
- never reconstructs success from a filename or apparently valid bytes.

## 9. Successful-result idempotency

A validated Revision and Succeeded attempt committed before UI refresh are already authoritative. A restarted service loads that one result; startup recovery is a no-op and neither creates a second Revision nor reruns Meitu.

Conversely, an output file present before the success/Revision transaction commits is not success. Startup recovery follows persisted Running state and never reconstructs a Revision from the filesystem.

## 10. Retry and path isolation

Retries now persist `RetryOfAttemptId` as well as the existing increasing `RetrySequence`. Earlier attempts are never rewritten.

For Enhancement and Background Removal:

- interrupted attempt A remains Interrupted and audit-visible;
- retry creates attempt B linked to A;
- B receives its own `Working/<attemptId>/` directory;
- A and B may use the same proposed filename inside their directories, but their complete paths differ;
- successful retry ends at ReviewRequired.

## 11. Background Removal authority after recovery

The existing reviewed-content authority rule is reused without a D1 policy fork. Startup recovery does not clear or manufacture authority.

- unchanged upstream Revision id and SHA-256 keep the persisted authority usable for retry;
- the retried attempt snapshots the same authority over the same input;
- replacement upstream content makes the old authority unusable through the existing workflow predicate.

## 12. Attempt audit and automation lock

Failure JSON is now deserialised back into the original `OperationFailure` rather than being flattened into a JSON string. Code, message key, technical detail, retryability and structured context survive reload.

Attempts retain adapter, operation, input Revision, trim parameters where applicable, Background Removal authority, failure context, retry linkage and successful adapter notes. Failed/interrupted attempts cannot carry an output Revision by both domain behavior and the database check.

The one-global-automation-lock invariant is exercised across ordinary failure, timeout, cancellation, startup recovery and retry. No tested path leaks the lock, and a possibly live owner remains protected.

## 13. Live failure smoke

After all automated gates passed, the existing controlled workstation smoke ran with only `PRINTFLOW_MEITU_SMOKE=1` enabled. No open, Enhancement, Background Removal, export or close flag was enabled.

Observed live:

- signed immutable preset v1.5.0 verified;
- accepted Meitu 7.8.7.5 executable/process verified;
- one accepted window identified;
- state classified `KnownWelcome`;
- the signed start-page card resolved structurally;
- Phase 2 explicitly reported skipped and produced no input.

The suggested input-producing live failure cases were not forced in D1. There was no operator-supervised safe process-close window, and programmatic force-kill is prohibited. Foreground refusal, cancellation during Busy, process exit during Busy, window destruction/handle reuse and no-later-input behavior are covered deterministically through the existing guarded-driver and fake-adapter seams. The smoke transcript was written only under the system temporary directory and was not committed.

## 14. Tests and gates

Seventeen focused test cases were added, bringing the suite from 7,450 to 7,467. They cover:

- Enhancement and Background Removal process exit during Busy;
- cancellation before both operation invocations;
- cancellation before export confirm;
- Busy-start and Busy-completion `Timeout` classification for both operations;
- immediate Unknown-state stop;
- structured production cancellation;
- process-loss workflow failure with no Revision and released lock;
- persisted structured failure context;
- persisted successful cleanup warning;
- successful-output/cleanup-warning separation;
- Enhancement recovery retry linkage, ReviewRequired and path isolation;
- Background Removal recovery, authority preservation, retry linkage and path isolation;
- committed-success restart idempotency;
- absence of force-termination APIs.

Existing shared tests continue to cover missing/unreadable/corrupt/unstable output, blocking modals, foreground refusal, handle reuse, startup quarantine, stale-lock ownership, integrity mismatch, happy Enhancement and Background Removal production routes, and the generic fake scenarios `FailWith`, `Timeout`, `ProduceUnreadableFile`, `ProduceMissingFile` and `HangUntilCancelled`.

Final gates using the installed .NET 10.0.400 SDK:

| Gate | Result |
|---|---|
| `dotnet restore --locked-mode` | passed; lock files honoured |
| `dotnet build` | passed; 0 warnings, 0 errors |
| `dotnet test` | 7,467 passed; 0 failed; 0 skipped |
| `dotnet list package --vulnerable --include-transitive` | no vulnerable packages in all five projects |
| controlled read-only Meitu workstation smoke | passed; no input produced |

## 15. Architecture and remaining D2 scope

Architecture checks confirm:

- recovery and retry policy remain in Workflow/Services;
- Infrastructure reports adapter/runtime results and cannot create workflow Revisions;
- UI Automation/process/window types do not leak into Domain, Workflow or App;
- ViewModels contain no `System.IO` usage;
- no generic macro/free-form input API was introduced;
- no `TerminateProcess`, `Process.Kill`, `.Kill(` or `taskkill` API appears in production source;
- global Production registration remains disabled;
- Photoshop and Epic 11500 remain untouched.

D2 still owns Stop, the Meitu Cancel-button policy, force termination, operator takeover, retained-state operator UI, and any decision about resuming or terminating externally continuing work.

## 16. Git state

Normal local commits, with no amend, rebase, history rewrite, push or force operation:

- `0d30555` — checkpointed the completed Background Removal workflow/operator UI before D1;
- `164935f` — D1 runtime failure, interruption, recovery, persistence and tests;
- this report is committed separately as a report-only commit.

Final expected status after the report commit: `master...origin/master [ahead 3]`, clean working tree. No synthetic images, partial exports, screenshots, UI dumps, runtime database, smoke transcript or external baseline artefact is tracked.

11300-D1 PASS WITH NOTES — READY FOR STOP AND OPERATOR TAKEOVER
