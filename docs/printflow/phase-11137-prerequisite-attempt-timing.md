# SCRUM-11137 prerequisite — ProcessingAttempt terminal timing correctness

Date: 2026-09-04 (Pacific/Auckland). Scope: when a `ProcessingAttempt` is recorded as having
ended. Nothing about what an attempt records, what it is allowed to produce, or what any adapter
does was changed.

Linked to **SCRUM-11137 — Measure Post-MVP Operator-Time Outcome** as a defect / prerequisite.
Epic 11600 is untouched accepted history.

---

## 1. Pre-fix reproduction

The first controlled real Production order left every persisted attempt claiming
`EndedAtUtc == StartedAtUtc`. Reproduced deterministically before any source change, with a
controlled clock advanced *inside* the adapter call:

```text
clock = 2026-08-19T09:00:00Z    attempt persisted Running
adapter works for 3 minutes     (FakeTimeProvider advanced by the adapter seam)
clock = 2026-08-19T09:03:00Z    attempt persisted Succeeded

read back from SQLite:
StartedAtUtc = 2026-08-19T09:00:00Z
EndedAtUtc   = 2026-08-19T09:00:00Z   <-- defect
```

```text
Shouldly.ShouldAssertException : attempt.EndedAtUtc
    should be  2026/8/19 9:03:00 +00:00
    but was    2026/8/19 9:00:00 +00:00
```

The reproduction is not incidental to the harness. Every existing double in the suite completes
inside a single clock tick, so a run through them cannot distinguish "the closing timestamp was
observed at the close" from "the opening timestamp was copied to the close" — both produce two
equal values. Advancing the clock during the adapter call is what makes the defect visible, and
that seam had not existed before.

Against the unfixed source, 8 of the 12 new tests fail. The 4 that pass are the paths that were
already correct and are now regression guards: Running, live lock owner, Interrupted recovery,
and the terminal commit that never lands.

## 2. Root cause

Not the domain model, not the schema, not the mapper, not materialisation, not the fixtures. All
five were already correct:

| Audited | Finding |
| --- | --- |
| `ProcessingAttempt` | `EndedAtUtc` is `DateTimeOffset?`, null while Running; `Succeed`/`Fail`/`Interrupt`/`Cancel` each take a terminal instant |
| SQLite schema | `EndedAtUtc TEXT NULL` since `0001_initial_schema.sql`; no `DurationMs` column anywhere |
| `Mappers.ToRow` / materialisation | writes and reads `EndedAtUtc` faithfully, including null |
| Attempt upsert | `ON CONFLICT DO UPDATE SET EndedAtUtc = excluded.EndedAtUtc` |
| `StartupRecoveryService` | already observes its own clock and writes the recovery instant |

The cause is in the workflow, in one place:

**`CommandContext.NowUtc` is a single clock reading taken when the command arrives, and a
producing step is not one transition but two.** The opening transaction stamps the attempt
Running from `context.NowUtc`; then the adapter runs — minutes, for real Photoshop — and the
closing transaction stamps the terminal state from *the same* `context.NowUtc`. The terminal
value was therefore the start value, by construction, for every attempt in the database.

`ImportAsync` had the identical shape: one context spanning the source copy and hash.

Nothing was adding an offset, losing a value, or overwriting a row. The closing transaction was
faithfully persisting a timestamp that had stopped being true before the work began.

## 3. Chosen timestamp contract

Unchanged from what the existing domain model already expressed — it was right, and the workflow
was not honouring it:

```text
StartedAtUtc = the instant the attempt is persisted as Running
EndedAtUtc   = the terminal transition's own clock observation
             = null while Running
Duration     = EndedAtUtc - StartedAtUtc, derived, never persisted
```

`EndedAtUtc >= StartedAtUtc`. Equality is legal and is not treated as a defect: a step that
genuinely completes inside one clock tick has two equal timestamps, and no test here asserts
strict inequality or manufactures one. The invariant asserted is that the terminal value is the
value the *closing* transition observed.

A Running attempt fabricates nothing: null reads as "not finished", never as "finished at the
start".

## 4. Exact files changed

| File | Change |
| --- | --- |
| `src/PrintFlow.Workflow/Commands/CommandContext.cs` | added `At(DateTimeOffset)`: the same command stamped at a later instant, identifiers carried across unchanged (+25 lines) |
| `src/PrintFlow.Workflow/Services/SessionService.cs` | one closing clock observation per two-transaction operation, in `RunProducingStepAsync` and `ImportAsync` (+27 / −13) |
| `tests/PrintFlow.Tests/Fixtures/SessionServiceHarness.cs` | `CreateServiceWithPhotoshop` accepts a repository, so a test can fail the closing commit (+7 / −2) |
| `tests/PrintFlow.Tests/Fixtures/ClockAdvancingPhotoshopProcessor.cs` | **new** — adapter that advances the controlled clock while it "works" |
| `tests/PrintFlow.Tests/Integration/Persistence/AttemptTimingTests.cs` | **new** — 12 tests |

`At` carries `NewAttemptId` and `NewReviewId` across deliberately: `NewAttemptId` is how the
closing command names the attempt the opening one started, so a re-allocated context would close
an attempt that never existed. Only the clock reading moves.

In `RunProducingStepAsync` the observation is taken once, after the work returns and before the
success / stop / failure branch, so all three closing paths are timed by one rule and the
attempt, its step and its session agree on when the attempt ended. `_timeProvider` was already
injected into the service; no new `DateTime.UtcNow` was introduced anywhere.

A truthful side effect worth stating: within a closing transaction the output `Revision`, the
`InputSnapshot` and the session's `UpdatedAtUtc` now also carry the closing instant rather than
the opening one. Those records describe work that finished at the close, so this corrects them by
the same argument.

## 5. Semantics, case by case

**Running** — `EndedAtUtc` null, whatever has elapsed. Nothing writes a terminal timestamp before
a terminal transition.

**Success** — written inside the existing success transaction alongside the Revision, the
`PrintOutput`, the step's move to `ReviewRequired` and the lock release. Not a later best-effort
update; there is no second write.

**Normal failure** — the failure-closing transaction's own instant, with the failure detail and
the lock release, in one transition.

**Unexpected exception** — 11600-A containment is unchanged: the same `FailureCode`, the same
message construction, the same `faultType` context, the same bounded detail. The containment
instant is the terminal attempt time. This task changed when, never what.

**Cancellation** — the closing transaction still runs on `CancellationToken.None`, so a cancelled
caller cannot prevent terminal timing from being persisted. 11600-A's distinction survives
intact: a cancelled caller token closes as `FailureCode.Cancelled`; an `OperationCanceledException`
raised while the caller token is *not* cancelled remains a fault (`AdapterUnavailable` with
`faultType`), not an operator cancellation. Both get truthful terminal timestamps.

**Interrupted recovery** — already correct, now covered by tests. `StartupRecoveryService` reads
its own clock once per recovery pass and writes that instant.

> For an Interrupted attempt, `EndedAtUtc` is the time recovery established and persisted the
> interruption. It does not claim to know the exact process-death time.

Nothing is derived from process metadata, filesystem timestamps, TIFF metadata or session rows.

**Live or unverifiable lock owner** — fail-closed, unchanged. The attempt stays Running, the lock
stays held, and no terminal timestamp is assigned. Tested.

**Persistence commit failure** — the closing clock observation is not a fact until the transaction
carrying it commits. When the terminal transaction fails, persistence keeps the pre-commit truth:
Running, `EndedAtUtc` null, no output Revision. Nothing repairs the row in memory afterwards. A
later dead-owner recovery transitions it to Interrupted and assigns its *own* instant — not the
terminal instant the crashed run had observed and failed to persist. Tested end to end.

**Retry** — each attempt owns its timing. Proved with deliberately different durations (2 min
then 11 min), so a second attempt whose timing had been derived from the first would fail rather
than pass by coincidence. Attempt 1's row is not rewritten; the attempt upsert's `DO UPDATE`
clause never touches `StartedAtUtc`, and the retry chain (`RetryOfAttemptId`) is intact.

## 6. Schema and migration decision

**No migration.** The schema already represents the lifecycle correctly: `EndedAtUtc TEXT NULL`,
present since `0001_initial_schema.sql` and carried through `0008`. No historical migration file
was touched and no new one was added. There is no persisted duration column, and none was added —
§14's three-mutable-truths problem does not arise.

## 7. Historical-data policy

Historical rows have lost their true terminal timestamps and are **not** backfilled. Nothing is
reconstructed from TIFF `DateTime`, file `LastWriteTime`, log lines, Revision timestamps or
session timestamps. Those may be useful supporting observations; none is an authoritative attempt
close time.

The documented limitation: because equality is legitimate for a genuinely fast attempt, an old
row written under the defect is **indistinguishable** from a fast attempt that honestly finished
in the same tick. Rows created before this change should therefore be treated as having unknown
duration rather than zero duration. SCRUM-11137 should measure attempts created after this change
and must not mix pre-fix rows into a duration baseline.

## 8. UI scope

Audited: no view, view model or XAML in `PrintFlow.App` displays attempt Started, Finished or
Duration. Nothing was added. The two consumers of these values in the workflow are ordering
predicates — `SessionView`'s newest-attempt lookup (by `RetrySequence`, then `StartedAtUtc`,
which was never wrong) and `ManualCropEligibility.LatestEndedAttempt` (by `RetrySequence` first,
`EndedAtUtc` only as a tiebreak). Correct data can only improve the tiebreak; neither changes
behaviour. No display correction was needed.

## 9. Tests added

12 deterministic tests in `AttemptTimingTests`, each reading the attempt back **out of SQLite**
rather than asserting on the in-memory record the service returned — the defect was invisible in
memory, and only the persisted row proves duration can be trusted afterwards.

| Test | Proves |
| --- | --- |
| `A_running_attempt_records_its_start_and_claims_no_end` | Running: start recorded, end null |
| `A_successful_attempt_ends_at_the_closing_observation_not_the_opening_one` | success timing; also the §22 readback |
| `A_failed_attempt_ends_at_the_instant_the_failure_closed_it` | adapter failure timing, lock free |
| `A_contained_exception_ends_the_attempt_at_the_containment_instant` | containment timing, `FailureCode` and `faultType` unchanged, lock free |
| `A_cancelled_caller_still_gets_a_truthful_terminal_timestamp` | caller cancellation closes at the real closing instant |
| `An_unrequested_OperationCanceledException_is_a_fault_with_a_truthful_end` | fault, not operator cancellation, truthful end |
| `An_interrupted_attempt_ends_at_the_recovery_instant` | recovery instant, stale lock released |
| `A_live_owner_leaves_the_attempt_running_with_no_terminal_timestamp` | fail-closed: no fabricated end, lock retained |
| `A_terminal_commit_that_fails_leaves_no_terminal_timestamp_behind` | failed commit cannot fabricate terminal state or timing; later recovery assigns its own |
| `A_retry_and_the_attempt_it_retries_keep_independent_timestamps` | independent timing, first row not rewritten |
| `The_real_order_shape_records_truthful_timing_that_approval_does_not_disturb` | §21 real-order shape; approval does not touch terminal timestamps |
| `Durations_are_derivable_for_every_terminal_attempt_of_a_session` | §18 compatibility |

The cancellation test advances the clock a second time — after the adapter is waiting, before the
token is cancelled — so the expected value is one no other reading in the operation could have
produced. The commit-failure test picks the failing commit relative to commits already made
rather than by a fixed count.

The §21 regression uses synthetic artwork only (a generated bordered PNG through the Fake
adapter). No real customer artwork is referenced anywhere.

## 10. Results

| Gate | Result |
| --- | --- |
| Pre-fix reproduction | 8 of 12 fail against unfixed source; 4 pass as regression guards |
| Targeted timing tests | **12 passed / 0 failed / 0 skipped** |
| Persistence + recovery integration (`Integration.Persistence`) | **325 passed / 0 failed / 0 skipped** |
| Build (Debug) | succeeded, 0 warnings, 0 errors |
| Build (Release, clean) | succeeded, 0 warnings, 0 errors |
| **Complete suite, final source** | **10,158 passed / 0 failed / 0 skipped** (2 m 20 s) |

The accepted baseline was 10,146; 10,146 + 12 = 10,158. No pre-existing test changed behaviour,
which is expected: existing doubles never advance the clock during work, so for them the closing
observation equals the opening one and every existing timestamp assertion still holds.

## 11. Dependency and security status

No dependency added, removed or upgraded. No xUnit migration. `Directory.Packages.props`
untouched.

```text
dotnet list package --vulnerable --include-transitive
  PrintFlow.Domain          no vulnerable packages
  PrintFlow.Workflow        no vulnerable packages
  PrintFlow.Infrastructure  no vulnerable packages
  PrintFlow.App             no vulnerable packages
  PrintFlow.Tests           no vulnerable packages
```

No migration was added, so no migration/upgrade check was required.

## 12. Controlled persistence readback

Synthetic Production-shaped operation, read back from SQLite after the fix (emitted by the
success test, so it is reproducible rather than transcribed):

```text
AttemptId      = 01a06936-6e49-7ec2-8f88-374cf03b3423
Status         = Succeeded
StartedAtUtc   = 2026-08-19T09:00:00.0000000+00:00
EndedAtUtc     = 2026-08-19T09:03:00.0000000+00:00
Duration       = 00:03:00
Adapter worked = 00:03:00 (clock advanced inside the adapter call)
```

The end value demonstrably came from the closing observation: the only clock movement in the run
happens inside the adapter call, after the opening transaction committed, and the persisted end
value carries that movement. The start value is unchanged at `09:00:00`, so the terminal value
cannot have been copied from it. No real customer order was re-run.

## 13. Product boundaries

Unchanged and verified untouched: `Adapters.Mode = Production` and workstation preset
`printflow-workstation-v1 1.16.0` (`appsettings.json` has no diff); environment verification;
Photoshop automation; Meitu automation; W1; TIFF structure; preview decoding; owned-document
cleanup; approval semantics; workflow state vocabulary; `FailureCode` vocabulary. No unrelated
defect was found or is being reported.

## 14. Git state

Branch `master`, clean before the task, no push. Epic 11600 commits were not amended, rebased or
rewritten. One new local commit:

```text
src/PrintFlow.Workflow/Commands/CommandContext.cs                     modified
src/PrintFlow.Workflow/Services/SessionService.cs                     modified
tests/PrintFlow.Tests/Fixtures/SessionServiceHarness.cs               modified
tests/PrintFlow.Tests/Fixtures/ClockAdvancingPhotoshopProcessor.cs    new
tests/PrintFlow.Tests/Integration/Persistence/AttemptTimingTests.cs   new
docs/printflow/phase-11137-prerequisite-attempt-timing.md             new
```

No AI-attribution trailer.

---

**PASS — PROCESSING ATTEMPT TIMING READY FOR SCRUM-11137**
