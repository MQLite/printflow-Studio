# Epic 11300 Part C2B1 — Background Removal Workflow Decision and Revision Integration

**Verdict: 11300-C2B1 PASS — READY FOR BACKGROUND REMOVAL OPERATOR UI**

Baseline in: 7098 passed / 0 failed / 0 warnings / 0 errors / no vulnerable packages.
Baseline out: **7414 passed / 0 failed / 0 warnings / 0 errors / no vulnerable packages.**

Part C2A left `BackgroundRemovalDecision` defined but unreachable: the ordinary session route
passed `Unspecified` because no workflow caller could supply anything else. This slice adds that
caller. Nothing about the adapter contract, the 抠图 structural target, the Busy/completion
correlation, the CUTOUT export, the PNG/alpha validation, the exact-dimension or source-hash
checks, the Enhancement path or the immutable v1.5.0 preset was redesigned.

---

## 1. Reviewed-content authority model

The decision means:

> automatic selection is authorised for **this** reviewed content

and never:

> automatic selection is enabled for this session.

Keeping those two apart is the whole design. Every rule below exists to stop the first quietly
becoming the second.

The existing enum is reused unchanged — same name, same two members, same meaning. No second
enum was introduced, and no `bool UseAutomaticSelection` anywhere. `Unspecified` remains a
refusal state rather than a default.

It is now paired with the content it covers, as `PrintFlow.Domain.Sessions.BackgroundRemovalAuthority`:

```csharp
public sealed record BackgroundRemovalAuthority(
    BackgroundRemovalDecision Decision,
    RevisionId ReviewedRevisionId,
    Sha256 ReviewedSha256);
```

`BackgroundRemovalAuthority.For(...)` refuses to construct one from `Unspecified`. An authority
that authorises nothing is spelled `null`, and that is the only way to spell it — so no row can
ever exist that reads like a decision and grants nothing.

### Why the enum moved to `PrintFlow.Domain`

The type is now three things at once: pending session state, an immutable field of the
`ProcessingAttempt` that consumed it, and a field of `MeituRequest`. Only the third is an adapter
concern, and `PrintFlow.Domain` is the one assembly all three can see (`PrintFlow.Domain`
references nothing; `Workflow → Domain`; `Infrastructure → Workflow, Domain`). Leaving it in
`Workflow.Ports` would have made a product decision unusable by the domain records that must
record it.

The move is a relocation, not a redesign — the enum's declaration is character-for-character what
C2A shipped, and `MeituRequest.BackgroundRemovalDecision` keeps its name, position and type.
`AutomationBoundaryTests` now asserts the assembly, so a later drift back into an automation
assembly fails the build.

---

## 2. Revision and hash binding

`WorkflowSnapshot` gained a single predicate that everything else asks:

```csharp
public BackgroundRemovalAuthority? UsableBackgroundRemovalAuthority { get; }
```

It returns the authority only when it `Authorises(upstream.Id, upstream.Sha256)` for
`UpstreamResultOf(StepKind.BackgroundRemoval)` — the artefact Background Removal will actually
consume. Both halves must match:

- the **RevisionId** answers *which artefact*;
- the **SHA-256** answers *which bytes*.

An id alone would still match after the file underneath it was replaced. That is the case §24
requires to be refused, so the hash is not optional decoration.

The hash comes from `SessionStep.CurrentRevisionSha256` — the same recorded hash `Approve` binds
to — via a new `WorkflowSnapshot.UpstreamResultOf(StepKind)`. **No new file-hashing path was
introduced.** Nothing in the decision route opens a file.

The engine, the decision command and the read model all call that one property, which is why an
offered control and an accepted command cannot disagree about what "still valid" means.

---

## 3. The decision command

```csharp
public sealed record SetBackgroundRemovalDecision(
    BackgroundRemovalDecision Decision,
    RevisionId ReviewedRevisionId,
    Sha256 DisplayedHash) : WorkflowCommand;
```

Session-scoped in `TransitionTable`, alongside `SetTrimParameters` and for the same reason: it
names no step and changes no step state. `CommandKind.SetBackgroundRemovalDecision` was added to
the closed set, so the exhaustiveness matrix covers it automatically.

Eligibility, in the order the engine checks it:

1. session is Active;
2. the workflow contains `BackgroundRemoval`;
3. the decision is an explicit authorisation — `Unspecified` is refused with `InvalidPayload`;
4. `BackgroundRemoval` is the **current** step;
5. it is **between attempts** (`Waiting`, `RetryRequired`, `Failed`, `Interrupted`);
6. an upstream result exists;
7. the supplied `ReviewedRevisionId` **is** that upstream result;
8. the supplied `DisplayedHash` **is** that result's current hash.

Accepting one starts nothing: no attempt, no working copy, no adapter call, no Revision. The only
effect emitted is `PersistBackgroundRemovalDecision`, and a pure-engine test asserts the effect
list has exactly one entry.

Rule 5 matters as much as rule 7. Recording an authority while a result already awaits review
would leave the session claiming something the producing attempt's own row does not describe —
the drift the attempt-level record exists to prevent.

No operator UI was added. Tests issue the command directly through `SessionService`.

---

## 4. No implicit default

`WorkflowEngine.StartStep` refuses `BackgroundRemoval` when
`state.UsableBackgroundRemovalAuthority is null`, and does so **before** the input-revision
lookup — therefore before the automation lock, before `RecordAttemptStarted`, before
`CreateWorkingCopy` and before `RunAdapter`.

```
Run BackgroundRemoval with no usable authority
  → PreconditionNotMet, message begins "PRODUCT DECISION REQUIRED: …"
  → no ProcessingAttempt
  → no adapter call
  → no working copy
  → no Revision
  → step stays Waiting
```

A rejected transition emits no effects at all, which is what makes "no fake processing attempt"
structural rather than a promise. A missing product decision is never represented as a failed
Meitu processing attempt.

Defence in depth: `SessionService.PerformStepWorkAsync` also refuses to build a
`RemoveBackground` request when the attempt carries no authority, so no code path can construct
an `Unspecified` background-removal request even if the engine guard were bypassed.

---

## 5. Persistence

Migration **0003_background_removal_decision.sql**, added through the existing `MigrationRunner`
authority. `NewestKnownVersion` remains the single version authority; no test uses a literal
schema version.

Three typed columns on each of two tables — nullable, no default, always moving together:

| Table | Columns | Answers |
| --- | --- | --- |
| `ProcessingSession` | `BackgroundRemovalDecision`, `BackgroundRemovalRevisionId`, `BackgroundRemovalReviewedSha` | what the **next** run would be allowed to do |
| `ProcessingAttempt` | the same three | what **this** cutout was authorised by |

Three columns rather than one, because the decision alone is not the record: the artefact it was
granted over is part of the value, not context around it.

`NULL` across all three means "no decision" — on a session, nobody has authorised anything; on an
attempt, it was not an authorised background removal at all. It never means "the default was
used". Rows written before 0003 therefore read back correctly as what they were.

`Mappers.ToBackgroundRemovalAuthority` refuses a partially-filled row rather than patching it up,
and goes back through `BackgroundRemovalAuthority.For`, so a stored `UNSPECIFIED` is refused on
read exactly as the command refuses it. The database `CHECK` is the first enforcement of that
invariant and the read is the last.

The session upsert **does** update these columns (the operator may authorise different content).
The attempt insert deliberately omits them from its `DO UPDATE` clause.

Nothing is stored only in a ViewModel; `src/PrintFlow.App` has zero changes in this slice.

---

## 6. Attempt audit snapshot

`ProcessingAttempt.BackgroundRemovalAuthority` is an `init` property with
`WithBackgroundRemovalAuthority(...)`, mirroring `TrimParameters` exactly.

`SessionService.RunProducingStepAsync` copies it into the running attempt with the **opening**
transaction — before the working copy and before Meitu is touched — reading
`UsableBackgroundRemovalAuthority`, the same predicate the engine validated. So what is
snapshotted is exactly what was checked, never a stale record that happened to be sitting on the
session.

Because the columns are outside the attempt's `DO UPDATE` clause, a later decision cannot rewrite
what an earlier attempt ran under. This is the same immutable-audit principle the Trim parameters
use, and it is enforced by the SQL rather than by caller discipline.

Null on Enhancement, Trim, promotion and manual-crop attempts — the honest reading.

---

## 7. SessionService request construction

The hard-coded `BackgroundRemovalDecision.Unspecified` is gone from the background-removal path.
`PerformStepWorkAsync` now takes the `ProcessingAttempt` and builds the request from
`attempt.BackgroundRemovalAuthority`, so the decision that reaches the adapter and the decision
the audit history records are the same value **by construction** — not two reads of a setting
that could have moved in between.

Enhancement is unchanged: it is not a background removal, so it carries `Unspecified` exactly as
it always has. A test asserts this on the request the adapter actually received.

`AutomationBoundaryTests` asserts that no literal `UseAutomaticSelectionForReviewedContent`
appears in `SessionService.cs`, and that no Infrastructure source file constructs a
`MeituRequest`.

---

## 8. CUTOUT Revision lineage

An authorised run through the ordinary `SessionService` in Fake mode produces:

```
Attempt created first (Running, committed before any file work)
  → working copy of the reviewed upstream
  → fake Meitu RemoveBackground → a real, separate transparent PNG
  → normal FileInspector → SHA-256
  → AttemptSucceeded
  → Revision(OperationKind.RemoveBackground)
  → ReviewRequired
```

Verified on the workspace and the database, never on the request object:

- filename is exactly `{Name}_CUTOUT.png`, from the preset naming authority, inside the attempt's
  own working directory;
- the input working copy and the CUTOUT remain separate files, both present afterwards;
- `Facts.HasAlpha` is true;
- `SourceRevisionId` is the exact reviewed upstream Revision — read off the derivation edge, never
  inferred from filenames or step ordering;
- the producing attempt's `OutputRevisionId` is that Revision and its `InputRevisionId` is the
  reviewed upstream.

Infrastructure still creates no Revision and no database row.

---

## 9. Before/After

Unchanged and deliberately generic: `Before = Revision.SourceRevisionId`,
`After = the step's own result`. No background-removal-specific comparison pairing was added; a
test asserts the pair comes from the same lineage every other step uses.

---

## 10. Retry semantics

The distinction is the **upstream**, not the attempt count.

**Same unchanged upstream** → authority remains valid.
Reject the cutout → Retry → run again, with no second decision. Rejecting the *result* did not
change the reviewed *content*, so demanding re-authorisation would be asking the same question
about the same bytes. Two attempts result; both are recorded.

**Changed upstream** → authority is dead.
Reject → `ReturnToStep(Enhancement)` → Enhancement produces a new Revision → `StartStep` is
refused with `PreconditionNotMet` until a fresh decision is granted over the new content.

Both are tested explicitly.

---

## 11. ReturnToStep and the invalidation choice

**Choice made: retain the record as history; eligibility requires an exact current match.**

A stale authority is never hunted down and deleted. It stops being usable the moment the artefact
it names stops being the one Background Removal would consume, which
`UsableBackgroundRemovalAuthority` decides by comparison.

Why this and not explicit clearing:

- it is the smaller implementation — no clearing logic threaded through `ReturnToStep`, `Retry`,
  `Reject` and descendant invalidation, and therefore no path that could forget to clear;
- it gives the retry distinction in §10 for free, in both directions;
- the row that stays behind is honest history rather than a lingering permission.

Verified end to end: after `ReturnToStep(Enhancement)` and a re-run, the session row still holds
the old authority, `UsableBackgroundRemovalAuthority` is `null`, `StartStep` is refused, the
adapter call count does not move, and the read model reports `Unspecified` / `null` /
`CanRunBackgroundRemoval == false`. A fresh decision over the new content then works.

Old attempts, Revisions and audit history are retained under the existing invalidation rules;
nothing in this slice changes them.

### Integrity mutation

If the reviewed bytes are mutated after the decision was recorded, `EnsureIntegrityAsync` — which
runs before the command reaches the engine — refuses with `RevisionIntegrityMismatch`, marks the
Revision `FileMutated`, and creates no attempt and no adapter call. The authority record is left
exactly as granted: nothing silently re-binds it to the mutated bytes.

---

## 12. Restart and resume

`ProcessingSession.BackgroundRemovalAuthority` round-trips through
`SessionAggregate.ToSnapshot()` the way the print size and the W1 branch do. A second
`ISessionService` instance sharing nothing but the database and the files on disk sees the
decision, reports it in the read model, and runs on it.

Then, replacing the content it named makes it unusable — both halves of §20 in one test.

---

## 13. Read model

Four additions to `SessionView`, the whole of the seam C2B2 will build on:

| Field | Meaning |
| --- | --- |
| `BackgroundRemovalDecision` | the **currently usable** decision, else `Unspecified` |
| `BackgroundRemovalDecisionRevisionId` | the Revision it was granted over, else `null` |
| `CanSetBackgroundRemovalDecision` | may the authorisation be recorded now |
| `CanRunBackgroundRemoval` | would Background Removal actually start if asked |

The first two report the *usable* authority, never the raw one — a stale record must not be
presented as readiness. There is no third state meaning "probably fine".

The last two are answered by the engine's own probe (`AvailableCommands`), not by re-deriving the
rules in the read model's words. `CanRunBackgroundRemoval` is "BackgroundRemoval is the current
step **and** `StartStep` is on offer", which is exactly "the real command would be accepted".
Workflow legality is not duplicated in WPF.

The artefact to bind a decision to is already on the read model: when Background Removal is
current with no result yet, `SessionView.CurrentArtefact` resolves to the upstream Revision it
would consume, carrying both `RevisionId` and `Sha256`. C2B2's control needs nothing further.

A focused consistency test walks undecided → decided → awaiting-review and asserts the read model
and the real command agree at every stage.

---

## 14. Failed adapter after authorisation

Once an authorised attempt genuinely starts, an adapter failure behaves normally: the attempt ends
`Failed`, `OutputRevisionId` is null, no Revision exists, the step is `Failed`, and retry semantics
apply. The authority is still recorded on the failed attempt, because it is still the truthful
answer to "what was this run authorised by".

The contrast with §4 is the point, and it is asserted in both directions: a missing product
decision produces **no attempt at all**; a processing failure produces a failed one. The two are
never reported as the same thing.

---

## 15. Tests

New: `BackgroundRemovalDecisionTests` (18 integration tests, real workspace/database/inspector,
fake Meitu) and `BackgroundRemovalAuthorityTests` (8 pure-engine tests). Plus a new migration test.
The remaining growth over baseline is the `StepState × CommandKind` matrix expanding for the new
`CommandKind`.

| §27 item | Covered by |
| --- | --- |
| 1. Unspecified → no Attempt / no adapter call | `An_unspecified_decision_creates_no_attempt_and_never_reaches_the_adapter`, `Starting_without_an_authority_is_rejected_with_no_effects` |
| 2. Authority A + current A → allowed | `An_authorised_run_produces_one_CUTOUT_Revision_awaiting_review` |
| 3. Authority A + current B → refused | `Returning_upstream_and_re_running_it_leaves_the_old_authority_unusable`, `An_authority_does_not_survive_its_upstream_being_replaced` |
| 4. Mutated A → refused | `Mutating_the_reviewed_bytes_stops_an_already_granted_authority_executing` |
| 5. Restart preserves valid A | `A_valid_decision_survives_a_restart_and_dies_with_its_content` |
| 6. Attempt snapshots immutably | `Each_attempt_keeps_the_authority_it_actually_ran_under`, `Only_the_background_removal_attempt_carries_an_authority` |
| 7. Fake success creates CUTOUT Revision | `An_authorised_run_produces_one_CUTOUT_Revision_awaiting_review`, `The_cutout_and_the_working_input_remain_separate_files` |
| 8. Correct SourceRevisionId | same, plus `Before_and_after_come_from_the_same_generic_lineage_every_step_uses` |
| 9. Reject/retry same upstream | `Reject_and_retry_over_byte_identical_reviewed_content_stays_authorised` |
| 10. Retry after changed upstream | `A_retry_after_the_upstream_changed_requires_a_new_decision` |
| 11. ReturnToStep invalidates usability | `Returning_upstream_and_re_running_it_leaves_the_old_authority_unusable` |
| 12. Before/After stays generic | `Before_and_after_come_from_the_same_generic_lineage_every_step_uses` |
| 13. Enhancement unchanged | `Enhancement_still_runs_undecided_and_carries_no_background_removal_decision` |

Additional: hash-mismatch and wrong-revision refusals on the command itself; `Unspecified` refused
rather than recorded; workflow-without-the-step refusals; read-model/command consistency;
adapter-failure-after-authorisation; adapter receives the recorded decision; byte-identical
content under a different Revision does not authorise; an authority cannot be built from the
refusal value; a decision cannot be recorded while a cutout awaits review.

The transition matrix was **not** widened beyond adding the new `CommandKind` to the closed set.

### Existing tests touched

Every change is the same shape — a step that now needs authorising gets authorised — never a
weakened assertion. `WorkflowScenario.CompleteStep` and a new
`SessionServiceHarness.AuthoriseBackgroundRemovalAsync` do it in one place each, built from
`SessionView.CurrentArtefact` so tests authorise exactly what an operator screen would.
`StartupRecoveryTests.CrashDuringAsync` needed it too: without an authority there is no running
attempt to crash during.

`AutomationBoundaryTests.Background_Removal_authority_is_typed_and_the_normal_workflow_leaves_it_unspecified`
was **replaced** rather than left passing. Its "the normal workflow leaves it unspecified"
assertion encoded the C2A state and would have kept passing while meaning the opposite of what
this slice implements. The replacement asserts the typed decision, its Domain assembly, that
`SessionService` builds the request from the recorded authority, and that no Infrastructure file
constructs a `MeituRequest`.

---

## 16. Migration verification

- **fresh database → latest**: `Empty_database_migrates_successfully` (asserts `NewestKnownVersion`);
- **previous schema → latest, rows retained**: new `A_pre_0003_database_upgrades_and_keeps_its_rows`
  replays 0001 and 0002 off the shipped assembly, seeds a session with a real trim margin, upgrades,
  and asserts the margin survived and the three new columns read NULL. The existing
  `A_pre_0002_database_upgrades_and_keeps_its_rows` now exercises 0001 → 0003 as well;
- **future schema fails closed**: `A_database_user_version_ahead_of_this_build_fails_closed`;
- **`NewestKnownVersion` is the single authority**: no literal schema version in any test.

---

## 17. Architecture

| Requirement | Status |
| --- | --- |
| Decision type outside Infrastructure UI automation | Now in `PrintFlow.Domain` — asserted by reflection |
| ViewModels contain no `System.IO` | Unchanged; `src/PrintFlow.App` has zero diff |
| App cannot call the Meitu UI driver | Unchanged |
| Infrastructure creates no Revision | Unchanged; new assertion that it builds no `MeituRequest` either |
| SessionService owns request construction | Asserted on source |
| Workflow/domain state owns decision legality | Engine reducer; `UsableBackgroundRemovalAuthority` is the single predicate |
| Audit fields immutable once copied | Enforced by the attempt upsert's `DO UPDATE` clause and tested |

---

## 18. Production mode

`Adapters:Mode = Production` was **not** enabled. Application Production remains blocked by the
existing `EnvironmentGate` / Epic 11500 policy. Every workflow behaviour in this slice was proved
in Fake mode, through the ordinary `SessionService`, with no fabricated Revisions and no bypass.
No production Session smoke was performed.

---

## 19. Remaining C2B2 scope

- the operator control that issues `SetBackgroundRemovalDecision` — the reviewed-content
  confirmation surface, bound to `CurrentArtefact.RevisionId` / `.Sha256`, gated on
  `CanSetBackgroundRemovalDecision`, with "Run Background Removal" gated on
  `CanRunBackgroundRemoval`;
- operator-facing strings and display names for the decision and its refusal;
- surfacing the producing attempt's recorded authority beside a cutout under review;
- enabling `Adapters:Mode = Production` and the real production Session smoke (Epic 11500 gate).

---

## 20. Git state

Branch `master`, 18 source/test files modified and 4 added, **not committed, not pushed**, no
amend and no history rewrite. Working tree contains no synthetic files, cutouts, runtime database,
screenshots, smoke transcripts or external baseline artifacts — the only additions are the
migration script, the domain type, and two test files.

---

**11300-C2B1 PASS — READY FOR BACKGROUND REMOVAL OPERATOR UI**
