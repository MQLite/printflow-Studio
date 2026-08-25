# Epic 11300 Part C2B2 — Background Removal operator UI

Continues from **11300-C2B1 PASS — READY FOR BACKGROUND REMOVAL OPERATOR UI**.

Baseline at start: 7414 passed, 0 failed, 0 warnings, 0 errors, no vulnerable packages.
Result: **7450 passed**, 0 failed, 0 warnings, 0 errors, no vulnerable packages.

This slice gives the operator a way to say *"use Meitu's automatic selection for **this** image"*,
bound to the exact artefact on screen. It adds no new authority model, no second staleness rule
and no migration. Production remains disabled throughout.

---

## 1. Operator interaction

The Background Removal panel appears in the session screen's right-hand column, directly above
the trim controls, and only while the workflow layer says the decision would be accepted.

| State | What the operator sees |
| --- | --- |
| Undecided | Heading, one-line explanation, `Not authorised yet. Run Step becomes available once Automatic Selection is authorised for this image.`, and the button **Use Automatic Selection for this image** |
| Confirming | An inline confirmation stating that Meitu decides the subject on its own, that the result still needs checking, and that this covers the image shown above only — with **Confirm** / **Cancel** |
| Authorised | `Automatic Selection authorised for this image · Revision <short-id>`, plus `Ready to run background removal.` |
| Reviewing | The panel is gone; the review panel carries the audit line instead |

Deliberately **not** a checkbox. It is an action plus a confirmation, in the same shape the
`ReturnToStep` confirmation and the manual-crop surface already use, so nothing about it reads as
a long-lived session setting (§3, §4). The wording says *this image*, never *this session*.

zh-CN: **对此图片使用自动选择**.

Opening the confirmation records nothing. `BeginAutomaticSelection` and `CancelAutomaticSelection`
touch no service, so "cancel leaves no trace" is structural rather than promised.

## 2. Exact artefact binding

`ConfirmAutomaticSelectionAsync` reads `SessionView.CurrentArtefact` — the artefact whose
Revision, hash, format, pixels and DPI the metadata panel is displaying — and issues:

```
SetBackgroundRemovalDecision(
    UseAutomaticSelectionForReviewedContent,
    CurrentArtefact.RevisionId,
    CurrentArtefact.Sha256)
```

Nothing else can supply either half. There is no filename parsing, no cached field, no previous
selection, no adapter state, and no hashing anywhere in the shell — `BackgroundRemovalAuthorityBoundaryTests`
fails the build if `SHA256`, `HashAlgorithm`, `IncrementalHash`, `ComputeHash` or
`System.Security.Cryptography` appears anywhere in `PrintFlow.App`.

## 3. Stale-screen handling

The binding *is* the answer. A screen still showing Revision A sends A's identity, so if the
session has moved to B the engine's existing exact-Revision/exact-hash check refuses the command
outright. There is no retry against B: the operator has not seen B.

Afterwards the ordinary failure path re-reads the session, so the screen refreshes to B and shows
it unauthorised (§19). Covered by
`A_stale_screen_cannot_authorise_the_artefact_that_replaced_the_one_it_shows`.

One change was made below the shell for this. `SetBackgroundRemovalDecision` is now included in
`SessionService.EnsureIntegrityAsync`, alongside `Approve`, `Reject` and `StartStep`. Without it,
authorising content whose *bytes* had been mutated in place would succeed — the engine compares
the displayed hash against the Revision's own **recorded** hash, and a mutated file still matches
its own record; the mismatch would only surface later at `StartStep`, after an authority had been
written for content nobody reviewed. This is the same guard resolving the same Revision
`StartStep` resolves; no second hashing path was added, and the App computes nothing (§20).

## 4. Run gating

`SessionViewModel.CanRunBackgroundRemoval` reads `SessionView.CanRunBackgroundRemoval` and nothing
else. Readiness is never inferred from `BackgroundRemovalDecision != Unspecified`, because a
session can still hold raw authority over content that has been replaced.

The ordinary **Run Step** button was already gated by the same engine answer
(`AvailableCommands.Contains(StartStep)`), so it hides and reappears in step with the readiness
line without either restating a rule.

- Before authority: `CanRunBackgroundRemoval == false`, Run Step absent.
- After valid authority: both true.
- After the upstream changes: both false again.

## 5. Retry and upstream-change behaviour

Both follow C2B1's semantics; the UI adds no policy of its own.

- **Reject → Retry over unchanged reviewed content**: still authorised. The screen does not ask
  again, and no second decision is issued. A UI-only "always re-ask on retry" rule would have been
  a second staleness predicate that disagreed with the engine.
- **Upstream replaced** (`ReturnToStep` → re-run → approve): the session still holds the old
  authority in the database, and the screen shows none of it. `IsAutomaticSelectionAuthorised`
  reads `SessionView.BackgroundRemovalDecision`, which is already the *usable* authority, so the
  screen returns to the unauthorised state on its own. The operator must explicitly authorise the
  new content.

## 6. Review audit display

During `ReviewRequired` the review panel shows one line:

> Background removal: Automatic Selection — authorised for reviewed Revision `<short-id>`

It comes from the **producing attempt**, not from the session's pending authority. Two read-model
fields were added, resolved exactly the way `CurrentTrimParameters` already is — through the
attempt whose `OutputRevisionId` is the Revision on screen:

- `SessionView.BackgroundRemovalAttemptDecision`
- `SessionView.BackgroundRemovalAttemptReviewedRevisionId`
- `SessionView.HasBackgroundRemovalAttemptAuthority` (computed)

No database entity is exposed — an enum and an id. No migration was needed: migration 0003
already persists both the session's pending authority and the attempt's.

This distinction is the point of §15, and it is observable: after an operator rejects a cutout,
returns upstream, and authorises different content, the session's pending authority names B while
the first attempt's row still names A. Each review describes the attempt that produced what is on
screen.

**Short-id form changed.** Revision ids are UUIDv7, whose leading digits are a millisecond
timestamp — two Revisions produced within about a minute share their leading eight characters
entirely, which is exactly the pair an operator is asked to distinguish after a re-run. The short
form now takes the **trailing** eight hex characters (the random part). This applies to the
existing metadata `Revision` line as well, so the operator can match the id in the authorisation
line against the one in the artefact panel.

## 7. Localisation and rendering

Ten new strings, en-US and zh-CN, in full parity:

`Session_BackgroundRemovalHeading`, `…Hint`, `…Authorise`, `…ConfirmQuestion`, `…Confirm`,
`…Cancel`, `…Authorised`, `…NotAuthorised`, `…Runnable`, `…AttemptAudit`.

Parity is enforced generically by the existing `LocalisationResourceTests` and specifically by a
new test naming these ten, so both files losing a string together is a failure rather than a
silent gap. A further test asserts the confirmation makes no quality claim — "guarantee",
"perfect", "always correct" and "no need to check" are all banned from its text.

Rendering: all four states — undecided (with the confirmation open), authorised, ReviewRequired
with the attempt audit, and stale-after-replacement — are measured and arranged at the existing
representative viewport (1000×700) with WPF's data-binding trace source escalated to error level.
Zero binding errors, and the screen asks for no more room than the window in either locale. No
golden-screenshot framework was introduced.

## 8. Fake-mode UI smoke

`The_full_fake_mode_journey_reaches_an_approved_transparent_cutout` walks the operator's real
path with no service call of its own:

```
Home imports PREPARE_ASSET
  → Confirm Original
  → Run Enhancement → Approve
  → Background Removal becomes current
  → operator sees the artefact the step will consume (ArtefactIsInput)
  → Run unavailable (CanRunBackgroundRemoval == false, Run Step hidden)
  → operator opens the confirmation and confirms
  → Run becomes available
  → Run
  → Fake RemoveBackground produces a real CUTOUT
  → ReviewRequired, Before/After both decoded, attempt authority shown
  → Approve
  → session advances to Trim
```

The cutout is inspected on disk with `WicMeituTransparencyInspector`:
`HasTransparentPixels` and `HasVisiblePixels` are both true. "A cutout was produced" means pixels,
not a database row.

Real `SessionService`, real SQLite, real workspace, real files, real WPF composition. Fake Meitu.

### Human visual smoke (§24)

**No human visual judgement occurred.** Nobody looked at the running application: wording,
placement, spacing, the enabled/disabled feel of the buttons and the zh-CN fit have not been
judged by a person. What was verified automatically is stated above — every binding resolves, the
strings are the translated ones rather than raw resource keys, and nothing outgrows the
representative window in either locale. That is the honest limit of what a build can say.

## 9. Tests

36 new tests. All twelve areas of §27 are covered.

`tests/PrintFlow.Tests/Integration/Ui/BackgroundRemovalUiTests.cs` (12)

| § | Test |
| --- | --- |
| 7, 8, 10 | `Arriving_at_background_removal_authorises_nothing_and_offers_no_run` |
| 4, 10 | `Cancelling_the_confirmation_records_no_authority` |
| 10 | `Running_the_step_without_authority_authorises_nothing_and_starts_nothing` |
| 1, 5, 8 | `Authorising_binds_to_the_displayed_revision_and_hash_and_makes_the_run_available` |
| 6, 19 | `A_stale_screen_cannot_authorise_the_artefact_that_replaced_the_one_it_shows` |
| 20 | `Authorising_content_that_changed_after_it_was_displayed_is_refused` |
| 13 | `Retrying_over_unchanged_reviewed_content_stays_authorised` |
| 12 | `Replacing_the_reviewed_content_returns_the_screen_to_the_unauthorised_state` |
| 21 | `A_restart_shows_a_valid_authority_and_drops_one_whose_content_changed` |
| 14 | `The_review_shows_the_authority_the_producing_attempt_ran_under` |
| 15 | `A_later_session_authority_does_not_rewrite_an_earlier_results_review` |
| 18 | `The_full_fake_mode_journey_reaches_an_approved_transparent_cutout` |

`tests/PrintFlow.Tests/Integration/Ui/ViewRenderingTests.cs` (+2) — the four-state render pass and
the zh-CN render pass.

`tests/PrintFlow.Tests/Architecture/LocalisationResourceTests.cs` (+6) — the ten named strings in
both languages, and the five wording claims the confirmation must not make.

`tests/PrintFlow.Tests/Architecture/BackgroundRemovalAuthorityBoundaryTests.cs` (16, new).

## 10. Architecture regression

| Requirement | How it is enforced |
| --- | --- |
| App has no Meitu UI automation dependency | `AutomationBoundaryTests` — no automation type nameable in `App\ViewModels`, `App\Views`, `App\Navigation` (unchanged, still green) |
| ViewModels contain no `System.IO` | `BannedApiEnforcementTests.Shell_view_models_contain_no_System_IO_usage` (unchanged) |
| No hash calculation in App | **new** — seven banned hashing identifiers scanned across all of `PrintFlow.App` |
| No second stale-authority predicate | **new** — `Authorises`, `UsableBackgroundRemovalAuthority`, `ReviewedRevisionId`, `ReviewedSha256` banned from `App\ViewModels`; the same plus `UseAutomaticSelectionForReviewedContent` banned from `App\Views` (XAML included) |
| Attempt audit is immutable attempt data | **new** structural test that the read model keeps the pending and producing authorities as four separate members, plus the behavioural §15 test |
| No Production composition change | `ServiceRegistration`, `ApplicationStartup` and `FoundationEnvironmentGate` are untouched; `ProductionAdapterGateTests` still green |

The banned-identifier scans skip comment lines on purpose: explaining *why* the screen does not
compare Revisions requires naming them, and a scan that failed on the explanation would push the
reasoning out of the code.

## 11. Remaining Production / 11300 scope

Out of scope here and unchanged:

- Production Meitu is **not** registered, launched or driven anywhere in this slice.
  `Adapters:Mode` stays Fake and `FoundationEnvironmentGate` is unmodified. C1 and C2A already
  proved the real adapter path separately.
- Driving a real Meitu through `SessionService` end to end from the operator UI.
- `StepKind.PhotoshopOutput` production implementation (Epic 11400).

## 12. Git state

Nothing was committed and nothing was pushed. No history was rewritten.

The working tree carries this slice's changes on top of the still-uncommitted C2B1 work it
continues from, which is the state this slice inherited.

**Modified by this slice**

```
src/PrintFlow.App/Resources/Strings.cs
src/PrintFlow.App/Resources/Strings.resx
src/PrintFlow.App/Resources/Strings.zh-CN.resx
src/PrintFlow.App/ViewModels/SessionViewModel.cs
src/PrintFlow.App/Views/SessionScreenView.xaml
src/PrintFlow.Workflow/Services/SessionService.cs
src/PrintFlow.Workflow/Services/SessionView.cs
tests/PrintFlow.Tests/Architecture/LocalisationResourceTests.cs
tests/PrintFlow.Tests/Fixtures/HomeScreenHarness.cs
tests/PrintFlow.Tests/Integration/Ui/ViewRenderingTests.cs
```

**Added by this slice**

```
docs/printflow/phase-11300-c2b2-background-removal-operator-ui.md
tests/PrintFlow.Tests/Architecture/BackgroundRemovalAuthorityBoundaryTests.cs
tests/PrintFlow.Tests/Integration/Ui/BackgroundRemovalUiTests.cs
```

No runtime database, generated CUTOUT, screenshot, synthetic image or smoke file is present in
the working tree.

## 13. Gates

```
dotnet restore --locked-mode            ok
dotnet build                            0 Warning(s), 0 Error(s)
dotnet test                             Failed: 0, Passed: 7450, Skipped: 0
dotnet list package --vulnerable        no vulnerable packages (all five projects)
```

---

**11300-C2B2 PASS WITH NOTES — BACKGROUND REMOVAL WORKFLOW COMPLETE**

Notes:

1. No human visual judgement occurred (§24). Nobody looked at the running application.
2. `SetBackgroundRemovalDecision` was added to the existing `EnsureIntegrityAsync` switch so that
   authorising mutated content is refused with `RevisionIntegrityMismatch` as §20 requires. This
   is a Workflow-layer change, small and reusing the existing guard, but it is outside the shell.
3. The Revision short-id form changed from the leading eight hex characters to the trailing eight,
   because UUIDv7 leading digits are a timestamp and collided between the very Revisions the
   operator is asked to distinguish. This also affects the pre-existing artefact metadata line.
