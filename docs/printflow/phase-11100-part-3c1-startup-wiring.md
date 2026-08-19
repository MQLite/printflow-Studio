# Epic 11100 — Part 3C1: single-instance startup guard and recovery wiring

Scope: make application startup safe. No Home, Session, Review, PrintDimensions or W1 UI;
no real Meitu or Photoshop; no trimming; no environment-drift validation; no installer.

## 1. Single-instance implementation

`ISingleInstanceGuard` / `SingleInstanceGuard` (`src/PrintFlow.Infrastructure/Startup/`).

The claim is an exclusively opened lock file at
`%LOCALAPPDATA%\PrintFlow Studio\printflow-studio.instance.lock`
(`FileMode.OpenOrCreate`, `FileShare.None`). `TryAcquire()` returns
`Acquired` / `AlreadyRunning` / `Unavailable`; the third case is an environment fault
(denied path) and is deliberately never reported to the operator as "already running".

A file lock was chosen over the plan's suggested named mutex for two reasons:

* a mutex is owned by a *thread* and is re-entrant for it, so "another instance is running"
  could only be proved by racing two real processes — which §10 rules out. A process-scoped
  file lock makes the invariant directly testable in one process.
* a `Global\` mutex needs `SeCreateGlobalPrivilege`, which a standard operator account does
  not hold; a `Local\` mutex is scoped to a single desktop session. A file lock is
  machine-wide without needing either.

Staleness needs no reasoning: Windows closes the handle when the owner dies, so a leftover
lock file is never mistaken for a live claim. The bytes written into it (machine, pid, time)
are for a human and are never read back as evidence.

Lifetime: `ApplicationStartup` owns the guard; `App.OnExit` disposes `ApplicationStartup`.
There is no public force-release, and no ViewModel can reach the guard.

Known boundary: the lock path is per-user, because the guard is claimed *before*
configuration is read and therefore cannot be keyed on the workspace root. Two different
Windows accounts sharing one workspace are outside its reach and stay covered by the
persisted automation lock, which fails closed on `Alive` or `Unknown`.

## 2. Startup order

`ApplicationStartup.RunAsync` (`src/PrintFlow.App/Composition/ApplicationStartup.cs`):

1. acquire the single-instance guard
2. load `appsettings.json`
3. resolve and create the application-owned root directories
4. open SQLite, apply migrations
5. compose the service graph
6. verify signed preset integrity (recorded, non-blocking — unchanged Epic 11100 semantics)
7. `IStartupRecoveryService.RecoverAsync`, once
8. publish the result to `StartupStatusAccessor`
9. return the graph; `App.OnStartup` composes and shows the shell

Step 5 sits one place earlier than the plan's nominal order because recovery is resolved
*from* the graph; composition is registration only and touches nothing. The ordering rules
the plan requires all hold and are asserted by tests: guard before recovery, migrations
before recovery, recovery before the shell and therefore before any session interaction or
adapter-backed processing.

Every stage fails closed and returns **no container at all**, so a refused instance cannot
reach a repository, an adapter or a session even by accident.
`ServiceRegistration` is now registration-only; configuration loading and migration moved out
of it into the sequence.

## 3. Recovery invocation

Called exactly once, from `ApplicationStartup` only. The returned `StartupRecoveryReport` is
retained whole on `StartupStatus` (§13 read model: `IsPrimaryInstance`, `PresetVerified`,
`RecoveryExecuted`, `RecoveredAttemptCount`, `ReleasedStaleLockCount`, `QuarantinedFileCount`,
`RecoveryFailureCount`, `Failure`) and published through a small write-once
`StartupStatusAccessor` that the shell reads. `ShellViewModel.StartupSummary` shows one
localised line. No logging framework was introduced (§14).

`StartupRecoveryService` itself was not modified.

Failure handling distinguishes the two cases §7 asks for:

* *recovery completed with entries* — entries carrying `RecoveryFailed` are surfaced as
  `RecoveryFailureCount`; startup continues, because the pass ran and forced nothing;
* *recovery infrastructure failed* — a failed `OperationResult` disposes the container and
  refuses startup at `StartupStage.Recovery`.

## 4. Second-instance behaviour

Guard returns `AlreadyRunning` → `StartupStatus.SecondInstance()`, no configuration read, no
database opened, no container built, recovery call count 0. `App` shows
`Startup_AlreadyRunning` ("PrintFlow Studio is already running." / "PrintFlow Studio 已在运行。",
both in `Strings.resx`) and exits with code 2. Startup refused for any other reason exits 3.

## 5. Tests and manual smoke

14 new tests (5439 → 5453), all green.

`SingleInstanceGuardTests` — first instance acquires; second is refused while the first holds
it; acquire is idempotent for the owner; release happens only on disposal and a later instance
then acquires (a leftover lock file is not a claim).

`ApplicationStartupTests` — primary instance starts and composes a usable shell; recovery
called exactly once and not re-triggered by resolving the shell or `ISessionService`; the only
`.RecoverAsync(` call site in `src/` is `ApplicationStartup.cs`; second instance refused with
recovery count 0, no container and no database created; an unevaluable guard refuses rather
than claiming a second instance; migrations precede recovery (`user_version = 1` and
`ProcessingSession`/`ProcessingAttempt`/`AutomationLock` all present when recovery is called
against a brand-new database); a `user_version` ahead of this build fails closed at
`StartupStage.Database` before recovery; the startup status carries the recovery summary and
the shell shows it; a recovery infrastructure failure stops startup; `Adapters:Mode =
Production` still refuses to start.

Manual smoke, against `D:\PrintFlowStudio`:

* **A** — launched normally: one process, shell rendered, lock file held exclusively (even a
  read of it was refused, which is the guard doing its job).
* **B** — launched again while A was open: second process showed the "already running" message
  and exited with code 2; A was unaffected and stayed the only instance.
* **C** — synthetic persisted state seeded directly into the database (one `ACTIVE`
  `PREPARE_ASSET` session, `Enhancement` step `PROCESSING`, one `RUNNING` attempt, automation
  lock held by pid 999999 on this machine — no customer files anywhere). Launching the primary
  instance produced, before any interaction: attempt `RUNNING → INTERRUPTED`, step
  `PROCESSING → INTERRUPTED`, automation lock released, `OutputRevisionId` null and zero
  Revisions for the session. The shell showed
  「启动恢复：中断的处理 1 个，释放过期锁 1 个，隔离文件 0 个。」 A second launch changed
  nothing — recovery is idempotent. Synthetic rows were removed afterwards.

The seeding/verification helper was a throwaway file, deleted before commit; it is not in the
repository.

## 6. Defects found

None in Part 3B recovery. One observation worth recording: the first smoke seed put a
`RUNNING` Enhancement attempt in front of a still-`WAITING` Import, which is not a reachable
state. Recovery corrected the attempt row and released the lock but left the step exactly as
persisted, because the engine refused the transition — the documented "recovery corrects
records, never forces a state" behaviour, working as designed. The seed was made realistic and
the step then transitioned normally.

## 7. Remaining 3C work

Home screen with recent sessions, workflow selection, Session screen, Review screen,
PrintDimensions and W1 UI, fake-scenario UI, a real diagnostics/logging surface for the
retained recovery entries, and the Part 3C release gate.

## 8. Git state

Branch `master`, fast-forward on `origin/master`. Two commits: implementation, then this
report. No runtime database, lock file or log entered git — the workspace lives outside the
repository at `D:\PrintFlowStudio` and the guard's lock file under `%LOCALAPPDATA%`.

Gates: `dotnet restore --locked-mode`, `dotnet build` (0 warnings, 0 errors),
`dotnet test` (5453 passed, 0 failed).

---

`PART 3C1 PASS — READY FOR HOME AND WORKFLOW UI`
