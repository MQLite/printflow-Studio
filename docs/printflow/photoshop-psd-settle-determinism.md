# Photoshop PSD settle-poll determinism

A defect closure, not a Jira feature. It explains and removes the load-sensitive race in the
bounded settle poll of `ProductionPhotoshopOutputProcessor.PreparePsdAsync` that repeatedly
contaminated otherwise valid full-suite gates.

## 1. What was actually observed

`PhotoshopPsdBoundaryTests.Production_psd_path_enforces_real_guards_and_independent_validation`
failed intermittently under full-suite parallelism and passed on isolated rerun, across three
separate slices:

| Slice | Full suite | Failing variant | Expected | Observed | Isolated rerun |
|---|---|---|---|---|---|
| SCRUM-11101 | 10,865 passed / 1 failed / 10,866 | `malformed` | `PsdPreparationFailed` | `OutputUnreadable` | 12 / 12 |
| SCRUM-11101 §D | 5 targeted matrix runs | `malformed` | `PsdPreparationFailed` | `OutputUnreadable` | 517 / 517 ×3 |
| SCRUM-11082 | 11,382 passed / 1 failed / 11,383 | `malformed` | `PsdPreparationFailed` | `OutputUnreadable` | 1 / 1, 105 ms |

The `success` variant was recorded losing the same race, which is why this was never a
`malformed`-specific problem. Both reports declined to fix it, preserved the evidence, and
declined to call the baseline green. That was the right call and this closure builds on it.

## 2. The settle contract as it stood

From `ProductionPhotoshopPsdPreparation.cs`, after the native export returned and the owned
document was closed:

```text
deadline = now + TiffSettleTimeout
do
    observe  := WicFileInspector.InspectAsync(output)      -- full read: length, format, SHA-256
    if observe succeeded and previous succeeded and SHA-256 equal
        -> ValidatePsdRaster -> AdapterOutput
    previous := observe succeeded ? observe : null
    await Task.Delay(PollInterval, clock)
while now < deadline
-> OutputUnreadable (file exists) / OutputMissing (it does not)
```

Stated precisely:

* **Observation contents** — a complete streamed read producing `ImageFormat`, byte length,
  SHA-256, and, for PNG/JPEG/TIFF, WIC pixel metadata. Not a cheap length probe.
* **Equality** — identical SHA-256 across two consecutive successful observations. A failed read
  resets `previous` to null.
* **Required observations** — two. Never named anywhere; implied by `previous is { } first`.
* **Interval** — `PhotoshopAutomationOptions.PollInterval`. Production 500 ms; the boundary
  fixture 2 ms.
* **Timeout** — `PhotoshopAutomationOptions.TiffSettleTimeout`, an absolute wall-clock budget
  measured from the moment the loop is entered. Production 60 s; the boundary fixture **40 ms**.
* **Timeout failure code** — `OutputUnreadable` when the file exists, `OutputMissing` otherwise.
* **Timing authority** — the injected `TimeProvider` throughout, for both `GetUtcNow` and
  `Task.Delay`. There was no second clock, and no `Thread.Sleep`, `DateTime.UtcNow` or
  `Stopwatch` anywhere on this path. That part of the design was already correct.

## 3. Root cause — Case B, triggered by Case A

The three candidate defects were tested against the code rather than assumed.

**Case B — Product settle algorithm race. This is the defect.** The deadline was evaluated at the
bottom of the loop, after the first observation and its interval. The budget therefore governed
not only how long a *changing* file was waited on but also **how many observations the stability
rule was allowed to make**. If the first observation plus one interval exceeded the whole budget,
the loop exited holding exactly one observation — half the evidence its own contract is stated
in — and reported a finished, stable raster as `OutputUnreadable`.

Nothing about that is specific to a variant, which is exactly why `success` and `malformed` both
lost it. It is also not specific to tests: the same structure in production would misreport any
output whose first read alone outlasted the budget.

**Case A — test policy. The trigger, not the defect.** The boundary fixture's 40 ms budget with a
2 ms interval is roughly 1,500× tighter than the TIFF path's 5 s, while its observation is the
most expensive one in the solution — a full file read, SHA-256, and a WIC decode whose first call
in a process pays for loading the imaging stack. Under full-suite parallelism, with the thread
pool saturated, the continuations behind `InspectAsync` and `Task.Delay` alone can outlast 40 ms.
The fixture made a latent algorithm defect reachable; it did not create it. Widening it would
have hidden the defect rather than closed it, which is why §13's instruction not to inflate
timeouts matters here.

**Case C — failure classification ambiguity. Rejected.** The boundary matrix's expectations are
correct. A stable malformed raster *must* reach `ValidatePsdRaster` and fail as
`PsdPreparationFailed`; a settle timeout preempting that is the bug, not the contract. No
expected code was changed, and no assertion was loosened to accept either code.

## 4. Deterministic reproduction

`PhotoshopPsdSettleDeterminismTests` makes both inputs to the loop controllable without a single
sleep:

* `ProductionPhotoshopOutputProcessor.PsdOutputInspector` is a seam (default: the real
  `WicFileInspector`), so a test can state what each observation saw and what it cost.
* `VirtualClock` is a `TimeProvider` whose `GetUtcNow` advances only when a scripted observation
  says it cost something. Every case polls at `TimeSpan.Zero`, so no timer is ever armed and
  elapsed virtual time is exactly the sum of the scripted observation costs.
* The bytes on disk are real and the reader is the real `WicFileInspector`. A case that reaches
  `ValidatePsdRaster` reaches it over the same bytes production would read. Only the *timing* is
  written down instead of measured.

The reproduction is one line of timeline. A finished, valid 141-byte PNG, one observation costing
the whole 40 ms budget:

```text
#1 at 40 ms: 141 bytes, Png, 9C44EE27
OutputUnreadable: PSD preparation output did not settle into a readable PNG.
```

Against the unchanged algorithm (commit `e563246`, seam and tests added, fix not yet applied):

```text
Failed: 3, Passed: 13, Skipped: 0, Total: 16
  One_slow_observation_no_longer_spends_the_settle_budget_before_the_first_comparison
  A_stable_malformed_raster_settles_first_and_then_fails_psd_validation
  An_impossible_settle_policy_is_refused_before_photoshop_is_touched
```

The malformed case reproduced the exact full-suite signature — `OutputUnreadable` where
`PsdPreparationFailed` was expected — with no load, no parallelism and no rerunning.

## 5. The fix

Two changes, both in `Infrastructure/Adapters/Photoshop`.

**The budget no longer truncates the evidence.** The deadline may end the poll only once the
observations the rule is stated in have actually been taken:

```csharp
if (observations >= settle.RequiredEqualObservations && _clock.GetUtcNow() >= deadline) break;
```

The timeout keeps its meaning — a wall-clock budget for waiting out a file that is still
changing — and loses the meaning it never should have had. The guarantee costs at most one
further poll interval, bounded and independent of how badly the file behaves.

**`PsdOutputSettlePolicy` names the invariant the two numbers never had between them.**
Establishing stability needs `RequiredEqualObservations` observations separated by
`RequiredEqualObservations − 1` intervals, so a budget shorter than that
`MinimumObservationWindow` cannot be satisfied by any file, however well behaved. Such a
combination is now refused before Photoshop is touched, as a named configuration failure, instead
of surviving to the end of the poll disguised as an unreadable output.

The two rules are complementary and both are needed: the invariant bounds the *configuration*,
the loop bounds the *algorithm*. A budget can always be exhausted by one slow read, so validating
the numbers alone would not have fixed this.

`RequiredEqualObservations` stays at **2** — the accepted SCRUM-11099 contract. Raising it to the
Meitu rule's 3 would be a silent policy change, not a determinism fix.

## 6. Why this is not a weakened safety rule

Every distinction §7 requires is preserved, and each is now proved by a deterministic timeline
rather than by hoping:

| Output state | Result | Test |
|---|---|---|
| stable and valid | success | `An_output_that_is_stable_immediately_settles_on_the_minimum_pair` |
| changes once, then stable | success on the first equal pair | `An_output_that_changes_once_settles_on_the_first_equal_pair` |
| stable exactly on the deadline | success — the pair is honoured | `An_output_that_becomes_stable_exactly_on_the_deadline_still_settles` |
| still changing at the deadline | `OutputUnreadable`, fails closed | `An_output_that_keeps_changing_fails_closed_as_unreadable` |
| never appears | `OutputMissing` | `An_output_that_never_appears_fails_as_missing` |
| stable but truncated | `PsdPreparationFailed` (decode refusal) | `A_stable_malformed_raster_settles_first_and_then_fails_psd_validation` |
| stable, decodable, wrong canvas | `PsdPreparationFailed` (structural refusal) | `A_stable_raster_of_the_wrong_size_settles_first_and_then_fails_psd_validation` |
| impossible policy | `PsdPreparationFailed`, before any external effect | `An_impossible_settle_policy_is_refused_before_photoshop_is_touched` |

Nothing unstable, missing or malformed was made to pass. A production file that truly never
settles still fails closed, still creates no Revision, and the automation-lock and attempt-failure
semantics are untouched. The two malformed rows are the point of the closure: settlement passes
first, and the refusal then comes from `ValidatePsdRaster` on its own terms — including for a
valid PNG of the wrong size, which proves the refusal is structural rather than a read that fell
over.

No sleep, no `Task.Delay` outside the product's own timing authority, no automatic retry, and no
assertion of the form `code == A || code == B` was added anywhere.

## 7. Product versus test timing impact

**No timing constant changed.** Production still polls at 500 ms inside a 60 s budget; the
boundary fixture still uses 2 ms and 40 ms. §13's prohibition is respected — the 40 ms budget was
deliberately *not* widened, because the invariant now certifies it as sufficient and leaving it
tight keeps the fixture honest.

The executed sequence is identical to before in every case except the one the defect describes:
where the deadline would have fired before two observations were taken, the loop now takes the
second. Production behaviour changes only for an output whose single read outlasts 60 s, and only
by adding at most one 500 ms interval. `PsdOutputSettlePolicy.Create` is a pure computation on
two `TimeSpan`s.

Because Product code changed — the algorithm, not its constants — a bounded live smoke was run
rather than claimed unnecessary. See §11.

## 8. Comparison with the Meitu and TIFF settle rules

Compared, deliberately not unified (§21).

| | PSD raster | Photoshop TIFF | Meitu output |
|---|---|---|---|
| Rule | 2 equal SHA-256 | 3 equal non-zero lengths + complete read | 3 equal non-zero lengths + readable |
| Observation cost | full read + SHA-256 + WIC decode | length + read pass | `FileInfo` + open attempt |
| Timing authority | injected `TimeProvider` | injected `TimeProvider` | injected `TimeProvider` |
| Deadline position | was after the delay; now after the minimum | after the rule, before the delay | after the rule, before the delay |
| Production budget / interval | 60 s / 500 ms | 60 s / 500 ms | 2 min / 250 ms |
| Test budget | **40 ms** | 5 s | generous |

All three share one timing authority and none uses a sleep, so §10's audit found no mismatch to
correct. The TIFF and Meitu loops carry the same structural shape — a deadline that could in
principle fire before their three observations — but neither is reachable in practice: their
observations are length probes costing microseconds against budgets three to five orders of
magnitude larger, and neither has ever been observed flaking. The PSD path was uniquely exposed
because it combined the most expensive observation in the solution with by far the tightest
budget. Changing them was left alone as cross-adapter refactoring for elegance, and is recorded
here as a known, currently unreachable structural similarity rather than tidied away.

## 9. Variant independence

The whole `PhotoshopPsdBoundaryTests` matrix was audited; `malformed` was not special-cased. Of
the twelve variants, `success`, `missing`, `malformed`, `wrong-size` and `w1` reach the settle
poll and are now load-independent by construction; the remaining seven return before it. The
fixture was left **byte-identical** — it is stronger evidence that an untouched accepted test now
passes deterministically than that a rewritten one does.

## 10. Verification

Every count below is an actual observed result.

**Deterministic timeline suite** — `PhotoshopPsdSettleDeterminismTests`

```text
before the fix (e563246):  Failed: 3, Passed: 13, Total: 16
after  the fix (114123b):  Failed: 0, Passed: 17, Total: 17
```

**Boundary class, five consecutive runs** — `PhotoshopPsdBoundaryTests`

```text
run 1..5: 12 passed / 12, 0 failed   (335, 366, 341, 338, 328 ms)
```

**Under parallel load** (§20) — four concurrent `dotnet test` processes on an 8-core machine, each
running the Photoshop/Meitu/PSD/Trim/Workflow matrix (10,129 tests) three times, while the two PSD
classes were run ten times in the foreground. Eight test hosts were confirmed live during the
runs.

```text
foreground, runs 1..10:  29 passed / 29, 0 failed
background load, 12 runs of 10,129:  0 failed
```

That is the exact condition — a saturated machine running the full matrix in parallel — under
which the failure was historically recorded.

**Targeted PSD gate** (§27) — PSD inspection, PSD preparation workflow, PSD input preparation,
visual-only PSD contract, PSD boundary, settle determinism, file inspection/validation, TIFF save
settle, workflow output, Photoshop architecture boundaries, existing-white-ink and spot-channel:

```text
151 passed / 151, 0 failed, 0 skipped   (9 s)
```

**Clean build** — `dotnet clean` then `dotnet build PrintFlowStudio.sln`:

```text
0 warnings, 0 errors
```

**One complete suite against final source** (§29):

```text
11,400 passed, 0 failed, 0 skipped, 11,400 total, 3 m 43 s
```

The accepted population before this closure was 11,383; the 17 added cases are this document's
deterministic timeline suite. **This is the first all-green complete suite in the history this
defect has contaminated** — the failure it used to contribute is gone, and no other test failed.

```powershell
$dotnet = "$env:LOCALAPPDATA\Microsoft\dotnet\dotnet.exe"
& $dotnet clean PrintFlowStudio.sln
& $dotnet build PrintFlowStudio.sln
& $dotnet test PrintFlowStudio.sln --no-build --logger 'trx;LogFileName=full-final.trx' --results-directory evidence/psd-settle
```

## 11. Live PSD smoke

Run because Product code changed, even though no timing constant did. Bounded to §32's minimum on
the accepted workstation, against Adobe Photoshop CC 2019 (20.0.10) at
`D:\Adobe Photoshop CC 2019\Photoshop.exe`, with the verified environment gate ALLOWED.

| Variant | Result | Duration |
|---|---|---|
| supported RGB PSD | preparation SUCCEEDED, raster 4×3 RGB PNG | 21.98 s |
| supported W1/spot visual-only PSD | preparation SUCCEEDED, source W1 recorded as diagnostic, managed raster ordinary RGB PNG | 16.38 s |

Both proved the §32 checklist: preparation completed, the output validated independently, the
source bytes were unchanged, the automation lock was free, the exact document was closed, and
Photoshop returned to the same `KnownStartScreen` it started from. Neither run showed artificial
delay — the settle poll ran at production timings inside runs of well under half a minute.

The first RGB attempt failed with `PhotoshopUnknownState`: on a cold Photoshop launch the signed
`另存为` identity surface did not appear within its 20 s budget. That phase runs before the native
export and is not on any path this closure changed; the immediate retry against a warm Photoshop
passed with the same binary and the same build. It is recorded here as an observed live UI
cold-start timing limitation, not as a result of this work, and not tidied away.

## 12. Scope, coverage and Git

Changed: `Adapters/Photoshop/ProductionPhotoshopPsdPreparation.cs` (settle loop, policy check,
inspector seam), the new `Adapters/Photoshop/PsdOutputSettlePolicy.cs`, and the new
`PhotoshopPsdSettleDeterminismTests`. No workflow state, migration, WPF surface, PSD business
input contract, PDF path or W1 production semantics was touched, and no dependency changed — the
deterministic clock is a plain `TimeProvider` subclass in the test, needing no timing package.

This is a defect closure, so functional coverage is unchanged: **SCRUM-11082 = FULL**,
**SCRUM-11083 = FULL**, **SCRUM-11126 = FULL**. What it restores is trustworthy QA determinism,
not scope.

Work stayed on `master` in `D:\Repositories\printflow-Studio` — no branch, worktree, alternate
clone, amend, rebase or push. Accepted history is untouched: this closure explains the race the
SCRUM-11101 and SCRUM-11082 reports recorded rather than rewriting them. Two new local commits:
`e563246` (deterministic reproduction against the unfixed algorithm, deliberately red) and
`114123b` (the fix), plus this document. Unrelated files are unchanged.
