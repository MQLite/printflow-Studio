# PrintFlow Studio — Phase 11100 Part 3B: Startup Recovery

| Item | Value |
| --- | --- |
| Document | Epic 11100 Part 3B implementation report |
| Report date | 19 August 2026 |
| Scope delivered | Startup recovery of persisted `Running` attempts; stale `AutomationLock` recovery; safe orphan working-file quarantine; focused integration tests |
| Scope **not** delivered | WPF operator UI, single-instance UX, real Meitu/Photoshop, real trimming, installer, Epic 11100 release gate — all explicitly deferred |
| Design authority | PrintFlow Studio MVP Design Document v1.0 (Confirmed, English); Epic 11100 Plan; Part 1, Part 2 and Part 3A reports |
| Repository | `D:\Repositories\printflow-Studio` — branch `master`, remote `origin` |

---

## 1. Preflight and scope

`git status -sb` showed `master...origin/master`, clean. `dotnet --version` reported `10.0.400`.
Baseline before any change:

```text
dotnet restore --locked-mode   succeeded
dotnet build                   succeeded, 0 warnings, 0 errors
dotnet test                    Passed! Failed: 0, Passed: 5421, Skipped: 0, Total: 5421
```

5,421 matches Part 3A's final count exactly.

Delivered:

* `IStartupRecoveryService` / `StartupRecoveryService` (`src/PrintFlow.Workflow/Services/`) —
  not in WPF, not in `WorkflowEngine`;
* `IProcessLiveness` (`src/PrintFlow.Workflow/Ports/`) and `SystemProcessLiveness`
  (`src/PrintFlow.Infrastructure/Diagnostics/`);
* two `IWorkspace` methods — `ListWorkingFiles` and `QuarantineWorkingFile` — plus the
  `WorkingFileEntry` domain record they speak in;
* registration of both new services in the composition root.

Nothing in the already-passing workflow, persistence, workspace, fake-adapter or
`IEnvironmentGate` behaviour was redesigned.

---

## 2. `Running` → `Interrupted`

Recovery queries `FindRunningAttemptsAsync`. For every persisted attempt still `Running` whose
owning execution is no longer active (§3 below defines that), the attempt row becomes
`Interrupted` and its step is moved to `Interrupted`.

The step transition goes through the engine's existing `WorkflowCommand.System.AttemptInterrupted`
rather than a direct state write, so recovery cannot reach a state the workflow rules forbid.
`StartupRecoveryService` lives in `PrintFlow.Workflow` precisely because that command's
constructor is internal to that assembly — a WPF view model still cannot synthesise one.

Preserved and produced:

```text
Attempt history          preserved (rows are upserted by id, never replaced)
StartedAtUtc             preserved
EndedAtUtc               set to the recovery instant
OutputRevisionId         null
new Revision             none
new PrintOutput          none
adapter call             not resumed
old Working directory    not reused
```

Retry after recovery therefore takes the ordinary path: `Interrupted → Retry → Waiting →
StartStep`, producing a new `AttemptId` and a new `Working\<attemptId>\` while the interrupted
attempt stays audit-visible.

**The crash invariant.** Recovery never reads a leftover file to decide whether the work "really"
finished. A partially written file in the old working directory is evidence of nothing; only the
ordinary attempt pipeline — adapter output, existence, stable size, streaming read, hash,
metadata, validation — may create a Revision. The database `CHECK` that forbids an output
Revision on a non-`SUCCEEDED` attempt backs this up structurally, and `ProcessingAttempt.Interrupt`
has no parameter that could set one.

One deviation worth recording: the attempt row is corrected **unconditionally**, while the step
moves only if the engine accepts. If persisted step state is inconsistent enough that the engine
refuses the transition, the attempt is still recorded as `Interrupted` — it is not running, and
saying so is simply true — but the step is left exactly as persisted and the refusal is reported
as `StepNotTransitioned`. Recovery corrects records; it never forces a state past the rules.

---

## 3. Stale-lock behaviour

`IProcessLiveness.Check(processId, machineName)` answers `Dead`, `Alive` or `Unknown`.
`SystemProcessLiveness` is the only place in the solution that calls `Process.GetProcessById`.

| Case | Answer | Recovery |
| --- | --- | --- |
| No process carries the id | `Dead` | release the lock |
| Id names a process with a different name (recycled id) | `Dead` | release the lock |
| Id names a live PrintFlow process | `Alive` | leave the lock alone |
| Id is this very process (recycled onto us at startup) | `Dead` | release the lock |
| Claim recorded on another machine, or with no machine name | `Unknown` | leave the lock alone |
| Id cannot be inspected (`Win32Exception`) | `Unknown` | leave the lock alone |

The bias is deliberately asymmetric. Wrongly calling a live owner dead would let two processes
drive Meitu at once; wrongly calling a dead owner alive only leaves a lock held until an operator
intervenes. `Unknown` is therefore treated exactly like `Alive`.

That verdict also governs attempts, not just the lock: when the lock's owner is `Alive` or
`Unknown`, that session's `Running` attempts are left completely untouched, because a live process
may still be driving them. Only a confirmed-dead owner has its work recovered.

**One engine effect is deliberately not applied.** `AttemptInterrupted` emits
`ReleaseAutomationLock`, and recovery ignores it. The engine reasons about a single session and
cannot know who holds the singleton lock; obeying that effect would let a crashed session release
a lock a genuinely live process is holding. Ownership is settled once, up front, by the liveness
check, and the lock release rides in the same transaction as the owning session's interrupt when
they coincide.

The recovery service is documented as **startup-only**: the "id equals our own id ⇒ recycled"
rule is sound only before this process has claimed anything.

---

## 4. Orphan quarantine

Part 2's `Quarantine` primitive is reused; nothing is hard-deleted. Wiring is conservative on
three axes.

**What can be seen.** The new `IWorkspace.ListWorkingFiles` returns files under
`Sessions\<session>\Working\` and nothing else, each tagged with its attempt folder. Recovery
cannot enumerate `Source\`, `Approved\`, `Rejected\`, `Logs\`, `Baseline\` or `TestData\` at all,
so §8's protection is a property of the seam rather than a rule recovery must remember.
`QuarantineWorkingFile` refuses any reference that is not a `Working` one, closing the other
direction.

**What qualifies.** All three must hold:

1. the file sits in a `Working\<attemptId>\` folder naming an attempt this session owns;
2. that attempt ended `Failed`, `Interrupted` or `Cancelled`;
3. no `Revision` and no `PrintOutput` of the session refers to its path.

Condition 3 is the one that matters most: for an adapter-backed step the Revision's file *is* its
working copy, so a succeeded step's result lives under `Working\` and must never be swept. It is
checked per file, not per attempt.

Anything failing a condition is left in place. An unrecognised folder name yields an
`UnattributedFileReported` entry — reported, never guessed at.

**Which sessions.** Only sessions this pass actually recovered a `Running` attempt for.
A startup that found no crash has no business moving files, and sweeping the whole workspace on
every launch would be both slower and riskier than the problem warrants.

---

## 5. Ordering and idempotence

```text
1  read the automation lock and every persisted Running attempt
2  establish whether the lock owner is genuinely gone
3  per session, one transaction: attempts -> Interrupted, steps -> Interrupted,
   and (for the lock-holding session only) the lock release
4  release a confirmed-stale lock held by a session with no running attempt,
   in its own transaction
5  quarantine orphaned working files, after every commit has landed
```

File moves are last and are never presented as part of a transaction — SQLite cannot roll a file
move back. Crashing between step 3 and step 5 leaves the database correct and some leftovers
un-quarantined, which is the harmless direction; the reverse order would let a crash lose files
whose records still pointed at them. This preserves the existing file/database ordering principle
from Part 2.

Idempotence follows from the shape rather than from a flag. A second pass finds no `Running`
attempts, so it recovers no session, so it scans no working directory, and the lock it would have
released is already `NULL`. `Running_recovery_twice_changes_nothing_the_second_time` asserts it
directly: the second report is a no-op, the interrupted attempt's `EndedAtUtc` is not re-stamped
(the clock is advanced 17 minutes between passes so a rewrite would be visible), the step rows
compare equal, and the quarantine directory listing is unchanged.

---

## 6. Logging and audit

`RecoverAsync` returns a `StartupRecoveryReport` of `StartupRecoveryEntry` records —
`Action`, `AtUtc`, `SessionId`, `AttemptId`, `FailureCode`, `Detail`. Entries carry identifiers,
workspace-relative references and stable codes; never image bytes, never baseline contents, never
the customer's original source path.

It is returned rather than written from inside the service because `PrintFlow.Workflow` references
`PrintFlow.Domain` and nothing else, and must not acquire a logging package. Deciding where these
entries are written belongs to the composition root. No diagnostics system was built.

---

## 7. Tests

18 new tests; nothing existing was modified to accommodate them.

`StartupRecoveryTests` (8) — real temp SQLite, real temp workspace, real fake-adapter
infrastructure:

| Test | Proves |
| --- | --- |
| Crashed attempt recovers to `Interrupted` | attempt + step `Interrupted`, `OutputRevisionId` null, no Revision, no PrintOutput, start time preserved, lock released |
| Retry after recovery | new `AttemptId`, new `Working\<attemptId>\`, old attempt still audit-visible |
| Partial file left by a crash | only the crashed attempt's own leftovers quarantined; the *succeeded* step's Revision file, the InputSnapshot, `Approved\`, `Baseline\` and `TestData\` all untouched |
| Dead owner | lock released, and the liveness question was asked about the owner the lock actually names |
| Alive / Unknown owner (theory ×2) | lock not stolen, and the running attempt not touched either |
| Recovery twice | no duplicate interruption, no duplicate quarantine, no lock mutation |
| Clean shutdown | no-op; no liveness question asked, no `Quarantine\` created |

Process death is simulated the same way throughout: the fake adapter is scripted to hang, the
command driving it is started and abandoned, and its token is never signalled. The `Running`
attempt row has already been committed by then, and the abandoned call never writes again — which
is exactly what the database sees when the application is killed mid-attempt, with none of the
flakiness of racing a real process.

`ProcessLivenessTests` (8) — the real `SystemProcessLiveness`, in the cases decidable without
racing a process: other machine, missing machine name (×3), unusable process id (×2), an id
nothing runs under, and this process's own id. The `Alive` answer is deliberately not asserted
against a real spawned process; the fail-closed half is what matters and is what is testable.
The `Alive` **decision** is covered at the recovery level by `FakeProcessLiveness`.

`WorkspaceTests` (2 added) — `ListWorkingFiles` attributes files to their attempt folder, reports
a stray file with an empty folder name, and cannot see `Source\` or `Approved\`;
`QuarantineWorkingFile` refuses a non-`Working` reference without moving anything.

Final gates:

```text
dotnet restore --locked-mode   succeeded
dotnet build                   succeeded, 0 warnings, 0 errors
dotnet test                    Passed! Failed: 0, Passed: 5439, Skipped: 0, Total: 5439
```

5,421 + 18 = 5,439. The suite was run three times with identical results.

---

## 8. Defects found and fixed

None. No pre-existing defect was exposed by these paths: the `AttemptInterrupted` command,
`RecordAttemptInterrupted` effect, `Interrupted` step-state row in the transition table,
`FindRunningAttemptsAsync`, the `ProcessingAttempt` `CHECK` constraint and the `Quarantine`
primitive were all already in place from Parts 1–3A and all behaved as their documentation said.

The one design judgement worth flagging rather than a defect: the engine's `ReleaseAutomationLock`
effect is correct for the live session path and wrong for recovery, for the reason given in §3. It
was left untouched and recovery declines to apply it, rather than changing an effect that six
other call sites depend on.

---

## 9. Git state

Two commits on `master`, ahead of `origin/master` at report time:

```text
1d86d56  Startup crash recovery: interrupted attempts, stale lock, orphan quarantine
<this>   Report: Epic 11100 Part 3B startup recovery
```

The implementation commit adds 13 files (5 modified, 8 new), 1,267 insertions, 0 deletions.
No runtime database, test temp workspace, production file, real preset or sign-off, TIFF evidence
or screenshot is staged or tracked. No package was added, so no lock file changed. No force push.

---

## 10. Remaining Part 3C work

Recovery is composed in `ServiceRegistration` but **deliberately not invoked**. It must run once,
after migrations and before this process claims the automation lock, and it is only safe behind
the single-instance guard — a second instance running it would recover attempts the first is still
driving. Both belong to the Part 3C startup sequence.

Part 3C therefore owns:

1. the named-mutex single-instance guard and its operator-facing behaviour;
2. calling `IStartupRecoveryService.RecoverAsync` from `App.OnStartup` inside that guard, and
   deciding where the returned report is written;
3. surfacing an interrupted step to the operator (Home / Session / Review UI);
4. the remaining Epic 11100 UI: dimensions, W1, fake-scenario surface;
5. the Epic 11100 release gate.

Still out of scope beyond Part 3C: real Meitu, real Photoshop, real trimming, real environment
validation, installer and deployment.

---

`PART 3B PASS — READY FOR WPF FINAL INTEGRATION`
