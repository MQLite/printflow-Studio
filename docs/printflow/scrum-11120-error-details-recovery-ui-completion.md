# SCRUM-11120 — Structured Error Details and Recovery UI completion

Date: 8 September 2026

Repository: `D:\Repositories\printflow-Studio`

Branch: `master`

Starting HEAD: `e4cfb05a2e0971d86f87dbae4d06c2608398e763`

Implementation commit: `5c2afcdfc7909d55115fdf48ae4ee461c7166ea8`

## Exact Jira acceptance criterion

The authoritative CSV was reread at
`C:\Users\admin\Downloads\printflow_studio_mvp_jira_epics_tasks.csv`, Work Item 11605,
before Product edits. Its exact Description / acceptance criterion is:

> Provide an Error Details page showing workflow and step, structured error code, bilingual description, captured screenshot, input path, expected output path, retry information and available recovery actions such as retry or manual processing. Do not expose a misleading Continue action when the underlying state is unrecognised or output has not validated.

The exact rows for parent SCRUM-11115 (11600), SCRUM-11091 (11306), and SCRUM-11121
(11606) were also read before implementation and reread for the final reassessment.

## Pre-change matrix

| Original SCRUM-11120 clause | Current data/source | Already available? | Operator surface available? | Remaining gap |
|---|---|---:|---:|---|
| Error Details page | Existing transient view-model navigation and shell data templates | Navigation existed | No | Add one destination reached from the current failure surface; Back returns to refreshed processing context |
| Workflow and step | Session aggregate and exact `ProcessingAttempt.Step` | Yes | Only on processing screen | Project them for the selected attempt |
| Structured error code | `ProcessingAttempt.Failure` / `FailureDetailJson` | Yes for structured failures; interrupted attempts deliberately have no invented failure | Current failure line only | Show stable English code and truthful absence for interruption |
| Bilingual description | `OperationFailure.MessageKey`, `DisplayNames.Failure`, persisted/current operator culture | Yes | Current failure line only | Localise the page and description in en-US/zh-CN |
| Captured screenshot | Attempt failure context `evidencePath`; queryable `AutomationLogEntry.ScreenshotPath` | Usually; AutomationLog is diagnostic history | No | Correlate enrichment by persisted attempt identity, reuse WIC preview, keep missing files non-fatal |
| Input path | `InputRevisionId` → aggregate `Revision.File` → `IWorkspace.ResolveAbsolute` | Yes when an input Revision exists | No | Show the managed input path; never mislabel the original customer source |
| Expected output path | Exact runtime request destination; not generally frozen and unsafe to reconstruct from mutable output name/preset | Partial | No | Persist the exact path in closed failure context only after it exists; otherwise label it not established |
| Retry information | `RetrySequence`, `RetryOfAttemptId` | Yes | No | Show attempt number and preceding retry sequence without exposing internal IDs |
| Recovery actions | Engine `AvailableCommands`; existing Retry, HandOff, Re-enter Automation and manual importer | Yes | Current failure controls | Typed service returns actions for the exact still-current failure and revalidates before dispatch |
| No misleading Continue | Existing command-specific controls and state machine | Yes | Current failure surface already avoided generic Continue | Add no generic Continue; historical failures expose no stale actions |

## Implemented diagnostic authority

`ISessionService.LoadErrorDetailsAsync(SessionId, AttemptId, ...)` is the one typed boundary
that answers which error the operator opened, which attempt produced it, what evidence exists,
and what recovery actions are currently legal. Its `ErrorDetailsView` contains the exact immutable
attempt identity, workflow/step, terminal attempt state, stable code, message key, separate
technical detail, path/evidence statuses, screenshot preview, retry information, currentness, and
a closed recovery-action list.

The App binds that projection. It does not query or join `ProcessingAttempt`,
`AutomationLogEntry`, `Revision`, or `SessionStep`, and it does not reconstruct business meaning.
`SessionView.CurrentFailureAttemptId` supplies the normal failure-surface entry point only when the
current failed/retry-required/interrupted step's newest terminal attempt is the subject.

## Attempt authority and AutomationLog enrichment

The attempt/state machine remains authoritative for current workflow state, retry/manual-processing
legality, currentness, retry sequence, and input/output relationships. `AutomationLogEntry` remains
diagnostic history.

`FailureEvidence.AttemptIdKey` adds the smallest persisted relationship inside the existing closed
failure context, so no migration was required. An AutomationLog row may enrich Error Details only
when session, step, code, message key, and persisted attempt ID all match, and only when exactly one
row matches. There is no recency/timestamp guess. The exact attempt's own failure remains usable
when no row matches.

Crash-recovered `Interrupted` attempts still have no fabricated `OperationFailure`, stable code, or
AutomationLog row. Error Details shows truthful absence and the recovery actions actually permitted
by the engine.

## Path derivation and persistence decision

The input path is resolved from the selected attempt's `InputRevisionId` to its managed `Revision`
file and then through `IWorkspace.ResolveAbsolute`. It is labelled "Managed input file" and is
never substituted with or mislabelled as the customer's original source path.

The exact expected path cannot safely be reconstructed after restart from the session's mutable
output name or current preset naming rules. The implementation therefore persists only the smallest
additional evidence in the existing failure context:

- `expectedOutputEstablished=false` when a failure closed before a destination existed;
- the exact absolute `expectedOutputPath` plus `expectedOutputEstablished=true` once the workflow
  constructed the real destination;
- no new fields for legacy/interrupted failures, which display "Not recorded" rather than making
  a claim about what once existed.

One centralized post-work boundary attaches an established destination to adapter failures,
post-output validation failures, exceptions, cancellation, and Stop. PDF/PSD contract-validation
branches and Approved PNG inspection are therefore covered as well as ordinary Meitu/Photoshop
failures. A stopped attempt preserves the adapter's screenshot and output evidence while retaining
the Stop operation's own stable cancellation code, message, and audit facts.

## Screenshot behaviour

`IDiagnosticImagePreviewDecoder` accepts an authorised persisted local diagnostic path.
`WicImagePreviewDecoder` implements it by reusing the same bounded 2048-pixel WIC decoding core as
normal artefact previews; no second decoding stack and no fake `Revision` were introduced. The
screenshot is read-only, never shell-opened or modified. If the path is missing, relative, or no
longer decodable, the page still opens, keeps the copyable stored path, and says "Evidence image
unavailable". If no screenshot was captured, it says so separately.

## Retry information and recovery legality

The operator sees `Attempt = RetrySequence + 1` and `Previous retries = RetrySequence`; internal IDs
remain out of the primary UI. The details service obtains candidate actions from the existing
workflow engine, retaining the established PDF retry exclusions. It maps Manual Processing to the
existing `HandOff` command and existing manual-result importer; no second importer or result path
exists.

`ResolveErrorRecoveryAsync` re-reads the selected details and then carries the exact `AttemptId` to
`ExecuteCoreAsync`, which checks it again on the aggregate the command actually uses. A newer retry
or failure between the display read and command read therefore cannot receive a stale page's action.
Retry returns the step to clean `Waiting` and does not run an external application, create a new
attempt, or create a success Revision. A historical failure remains readable but has no actions.
There is no generic Continue button anywhere on Error Details.

## Navigation

The existing navigation service gained one sixth destination, not a second navigation framework.
The route is `Session failure → Error details`, addressed by `SessionId + AttemptId`. Back reloads
the current processing projection and mutates no workflow state. The original AC did not require
Home or Recent Processing entry points, so none were added.

## Localisation and accessibility

Every new operator label, guidance string, status, and action is in the existing en-US and zh-CN
resources and follows the current persisted runtime language without restart. The structured
`FailureCode` remains stable English in both languages. The localised `MessageKey` description is
primary guidance; adapter technical detail is isolated in a separately labelled copyable block.

Stable automation IDs include `Screen.ErrorDetails`, `Session.ErrorDetails`,
`ErrorDetails.Description`, `.Code`, `.InputPath`, `.ExpectedOutputPath`, `.Screenshot`,
`.ScreenshotPath`, `.TechnicalDetail`, `.Retry`, `.ManualProcessing`, `.ReenterAutomation`, and
`.Back`. Paths, code, description, and technical detail use focusable read-only TextBoxes. Recovery
and Back are ordinary WPF Buttons exposing Invoke; actual focus traversal was exercised in a shown
WPF Window.

## Targeted tests

Eleven new representative tests were added:

- eight service/persistence cases for exact diagnostics, exact log correlation, no-log
  interruption, historical authority, missing screenshot, truthful pre-destination absence,
  post-destination exception evidence, Stop evidence preservation, and command-time exact-attempt
  revalidation (some tests cover more than one related clause);
- three rendered WPF cases for the normal failure entry, screenshot and technical-detail display,
  missing-image fallback, live en-US/zh-CN switching, truthful unavailable paths, actual keyboard
  focus order, UIA Retry, UIA Manual Processing, historical Back, and no Continue.

Existing persistence coverage was updated to require the original adapter context as a preserved
subset plus the newly authoritative attempt/output evidence. No test expectation was weakened to
hide a behavioural regression.

Validation results against final source:

- focused diagnostics plus the affected persistence assertion: **16 passed, 0 failed, 0 skipped**;
- expanded directly affected failure, Stop, PDF/PSD, Photoshop output, Session, recovery, manual
  processing, localisation, navigation/composition, and Architecture set: **987 passed, 0 failed,
  0 skipped**;
- final solution build: **0 warnings, 0 errors**;
- `git diff --check`: clean after removing Markdown hard-break whitespace from the plan.

## WPF/UIA proof

A real composed application graph used a synthetic PNG and deterministic `IMeituProcessor`:

`Home → import → Prepare Design Asset → Confirm Original → run Enhancement → deterministic failure`.

From the rendered Session screen, an in-process WPF UI Automation `IInvokeProvider` invoked
`Session.ErrorDetails`. The rendered destination was checked for the localised description, stable
code, managed input path, exact expected output, retry information, decoded screenshot and its
stored path, and separate technical detail. Another rendered `IInvokeProvider` invoked Retry.
Independent repository/SQLite readback showed `Waiting`, the original failed attempt unchanged,
no new attempt, no new Revision, no fabricated success, and one adapter call only. A second proof
invoked Manual Processing and read back `HandedOff` with the existing manual importer available.
No coordinates and no external application were used.

Local visual captures were inspected at
`evidence/scrum-11120/error-details-en-US.png` and
`evidence/scrum-11120/error-details-evidence-en-US.png`. The evidence directory remains ignored;
customer-style diagnostic files were not added to Git.

## Full-suite decision and result

A full suite was justified because the change affects shared failure writing, `OperationFailure`
value semantics, Session projection, navigation/composition, and recovery dispatch. The first run
reported **11,593 passed, 1 failed, 0 skipped**: an existing persistence test required exact context
dictionary equality even though the product now deliberately enriches that dictionary with attempt
and output evidence. At the same time, the independent review found genuine missing
post-destination/Stop/race cases. Those implementation gaps were repaired, focused regressions were
added, and the existing assertion was made stricter about both preserved original values and new
closed evidence.

Because these were genuine investigated findings, the allowed investigation rerun was performed.
The final complete suite passed **11,597 passed, 0 failed, 0 skipped** (accepted baseline 11,586;
exactly 11 new tests).

## Independent review and routing evidence

The plan, UI slice, and final acceptance review were dispatched to separate native agent contexts
with requested `gpt-6-astra` / `high` routing where the global policy called for Astra High. The
dispatch configuration is recorded, but those contexts could not independently expose their actual
runtime model/effort metadata. They are therefore recorded as **UNVERIFIED** here; this report does
not claim a verified model switch.

The fresh independent final reviewer originally found three P2 issues: command-time loss of the
exact-attempt guard, missing expected-output evidence on some post-destination exits, and Stop
discarding diagnostic evidence. All three were fixed. A separate remediation pass re-read the
changes and regression tests and reported all three resolved with no remaining finding in scope.

## Jira reassessments

### SCRUM-11120 — PARTIAL → FULL

Every material original clause is now operator-reachable and truthful: exact workflow/step,
stable code, bilingual guidance, screenshot or honest absence, authoritative managed input,
persisted/non-guessed expected output, retry information, current legal actions, historical
read-only evidence, and no misleading Continue. The independent review findings are resolved and
the final build, targeted sets, rendered UIA proof, and full suite are green.

### SCRUM-11091 — remains PARTIAL

Factual overlap improved: exact attempt relationships, managed input, established expected output,
queryable/displayed screenshot, structured code, bilingual description, and retry information are
now persisted and presented for Meitu failures. The remaining original clause is material: evidence
is not yet kept under an enforced configured retention policy because SCRUM-11121's cleanup engine
does not exist. Error Details does not make retention enforcement true.

### SCRUM-11121 — remains PARTIAL

Structured failure records and screenshots remain local, and there is no automatic cloud upload.
The retention preference is operator-visible, but the default 30-day cleanup is not executed,
active diagnostic references are not cleanup inputs, and stored diagnostic locations are not yet
shown as a retention surface. This task added no logger rollout, scheduler, deletion, or retention
engine.

### Parent SCRUM-11115 — remains PARTIAL

The named Error Details surface is now complete, but the parent is not. Fresh reassessment retains
these gaps: SCRUM-11117 still lacks the required Recent Processing thumbnail and delete-record
action; SCRUM-11121 lacks enforced retention and stored-location visibility; SCRUM-11122 has no
diagnostic-package export/contents preview/consent flow; and SCRUM-11123 has no repeatable versioned
offline installer or install/configure/rollback procedure. SCRUM-11116 also retains the audit's
format-specific unsupported-file feedback concern. Closing SCRUM-11120 does not close the Epic.

## Scope and Git state

No general log browser, search/filter/export UI, rolling text-log framework, retention cleanup,
diagnostic package, installer, signing/certificate infrastructure, or digital-signature test was
added. No migration was added.

The implementation is committed locally on `master` as
`5c2afcdfc7909d55115fdf48ae4ee461c7166ea8`. This completion report, the append-only audit delta,
and removal of trailing Markdown whitespace from the already committed plan form a second local
documentation commit. No branch, worktree, alternate clone, amend, rebase, Co-Authored-By line, AI
attribution, or push was used.

**PASS — SCRUM-11120 STRUCTURED ERROR DETAILS AND RECOVERY UI VERIFIED**
