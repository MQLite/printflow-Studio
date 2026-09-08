# SCRUM-11110 operating environment check completion

Status: **PASS — SCRUM-11110 OPERATING ENVIRONMENT CHECK VERIFIED** (2026-09-08).

## Requirement authority and pre-change gap

Source: `C:\Users\admin\Downloads\printflow_studio_mvp_jira_epics_tasks.csv`, exact
Summary match. The source row is Work Item **11503**, parent **11500**, priority **High**, estimate
**5**. Its exact Description / acceptance criterion is:

> Implement Check Operating Environment to validate, without automatic repair, that Meitu and
> Photoshop are installed and launchable, resolution and scaling match the preset, the output root
> is writable, the Photoshop production Action exists, colour settings are confirmed, no unsaved
> document or unknown dialog blocks automation, and a test image can be opened and closed. Show exact
> failed checks rather than a generic pass/fail.

Before this change, the Production Readiness page and `ProductionWorkstationVerifier` already
reported twelve individually named preset, evidence, OS, executable, Action, workspace, session,
display, culture and application-language checks. The gate already denied Production on any
blocking static/dynamic mismatch. Four material parts were absent: actual Meitu/Photoshop
launchability, live Photoshop colour settings, the controlled image open/close, and the
unsaved-document/unknown-dialog result on this page. Those were the historical reasons for
`PARTIAL`.

## One authority and two explicit phases

`ProductionWorkstationVerifier` remains the single authority used by both
`VerifiedEnvironmentGate` and `IEnvironmentDiagnostics`. No parallel verifier, UI rule engine or
stored permission was introduced.

| Phase | Side effects | Checks |
| --- | --- | --- |
| Automatic / passive | Reads and hashes only; never launches an application and never acquires the automation lock | Preset and 28-file evidence integrity; read-only attribute advisory; OS; Meitu and Photoshop executable path/version/hash; canonical Action path/size/hash; accepted workspace identity/presence/filesystem/addressability; local interactive session; display geometry, DPI and scale; Windows UI culture; recorded application languages |
| Live application | May launch or attach through the accepted foundations; reads current state and Photoshop facts; no setting or operator document is changed | Shared automation lock; Meitu launchability and safe starting state; Photoshop launchability and safe starting state; active colour settings |
| Controlled smoke | Creates and removes one contained PrintFlow-owned image | Photoshop exact-image open, positive identity, close without save, return to prior recognised state |

Passive navigation and **Refresh** do not launch anything. Before an explicit successful live run,
all seven live rows are `Blocked` / **Not run** and Production is denied. **Run live application
checks** is an asynchronous, separately labelled command; duplicate Refresh/live invocation is
disabled while it is running. Results are intentionally process-ephemeral. A subsequent passive
read re-observes the same certified process identities, current safe state, current colour spaces
and lock availability. If either application restarts or drifts, certification is lost and the
operator must explicitly run the live checks again.

`WorkstationCheckOutcome` now has the closed `Passed`, `Failed`, `Advisory`, `Blocked` vocabulary;
`WorkstationCheckKind` identifies `Static`, `Dynamic`, `Live`, `Smoke` and `Advisory`. The new stable
check codes are:

- `ExternalApplicationAutomationLock`
- `MeituLaunchability`
- `MeituSafeStartingState`
- `PhotoshopLaunchability`
- `PhotoshopSafeStartingState`
- `PhotoshopColourSettings`
- `PhotoshopTestImageRoundTrip`

Every row carries phase, classification, expected, current, bounded operator-readable detail and a
stable `Environment.Check.{code}` AutomationId. The new commands use
`Environment.RunLiveChecks`, `Environment.Refresh` and `Environment.BackToHome`. English and zh-CN
resources cover the phase headings, running state, `Blocked` result and every new check. These are
ordinary WPF buttons, so keyboard focus and Invoke behavior use normal controls with no focus trap
or coordinate interaction.

## Launch, process identity and safe state

Static trust is always evaluated first. A missing, unreadable or hash-mismatched preset,
executable or Action prevents the live verifier from being called; the remaining live rows are
`Blocked`, not misleading failures. No unverified binary is executed.

The explicit phase reuses `IMeituAutomationFoundation` and `IPhotoshopAutomationFoundation`, their
accepted launch policies and their existing positively recognised state classifiers. It records
`Launched` versus `Attached` and the certified process ID. An already-running accepted process is
reused rather than duplicated. Whether launched or attached, both applications are conservatively
left running; the verifier never terminates an operator process.

Meitu must reach its recognised safe state before any Photoshop step runs. Photoshop must reach a
recognised state and the runtime fact reader must be able to account for every open document.
Unknown UI/dialog state fails in the existing classifier. Any unsaved document fails
`PhotoshopSafeStartingState`; PrintFlow neither saves nor closes it, and colour/probe checks are
blocked. Saved pre-existing documents are recorded before the probe and must be present with the
same identity afterwards.

This readiness certificate does not replace the operation-time guards. The Meitu and Photoshop
processors still re-check their target immediately before each production attempt. Photoshop now
also reads the four active working spaces and unsaved-document facts at that boundary, preventing
`readiness PASS → operator changes state → automation runs anyway`.

## Photoshop colour-settings observation

The verified preset's actual `photoshopContract.colourSettings` fields are parsed rather than
copied into code: RGB, CMYK, Gray and Spot working spaces, conversion command,
`convertToProfileCommandUsed`, and `visibleSettingsManifestSha256` are all required. The visible
settings record remains part of the preset's hash-verified evidence chain. Conversion-command and
Convert-to-Profile values are the immutable production-operation policy; the mutable application
facts compared live are Photoshop's four active working spaces.

`PhotoshopRuntimeFactReader` attaches only to the accepted Photoshop CC 2019 COM/ROT object,
cross-checks the application's reported installation directory, and executes one fixed,
caller-invariable, read-only Action Manager query. It returns the four current spaces plus bounded
facts for each open document (name, saved/unsaved status, path when available and active status).
It does not expose a general script surface.

`PhotoshopColourSettingsRule` compares the runtime values ordinally with the preset. A mismatch
reports both complete expected and current four-space descriptions and blocks the image probe. No
Photoshop setting, preset, Action or manifest is changed; there is no repair path.

## Synthetic image lifecycle

The smoke uses a deterministic 1 × 1 PNG whose bytes and SHA-256 are fixed in Product code. It is
created with `CreateNew` only under:

```text
D:\PrintFlowStudio\EnvironmentVerification\{random-token}\Working\PF_ENV_PROBE_{token}.png
```

The existing workspace resolver re-proves containment. The existing Photoshop foundation opens
that managed Working reference and returns its positive document identity. Close is permitted only
for that exact returned identity and path, using the accepted discard-without-save behavior. The
foundation is reinspected afterwards and the complete pre/post document list must match.

Cleanup first re-hashes the exact file, then deletes only that one known path and its now-empty
owned directories. It never recursively deletes unknown content. If exact close cannot be proved,
the result fails closed and the file is retained because it may still back an open document.
Cancellation retries cleanup only for the exact owned document with `CancellationToken.None`; it
never improvises recovery for another document. No Session, ProcessingAttempt or Revision is
created, and no customer or production artwork is used.

## Shared lock and terminal paths

Live verification uses the same singleton SQLite `AutomationLock` row as production processing.
Migration 0015 adds a closed purpose and an unguessable owner token. Environment acquisition is a
single compare-and-swap from the empty row. Session acquisition refuses an environment-owned row,
and environment release clears only the exact `ENVIRONMENT_VERIFICATION` token it acquired. It
cannot release a session's lock or another verification owner.

Release runs from `finally` on success, mismatch, timeout/foundation failure, unknown/unsaved
state, cancellation and handled exception. A release failure replaces the lock check with a
blocking failure and discards live evidence. Production work reports the live readiness check as
the holder when contention occurs.

## Automated evidence matrix

| Safety boundary | Deterministic evidence |
| --- | --- |
| Repeatability | Three live successes on one verifier; one open/close per run; no duplicate application launch, document, probe directory or held lock |
| Unsaved operator work | Probe never opens; no document close occurs; exact unsaved fact remains; later checks blocked |
| Colour drift | Executable/launch/state pass, expected and current CMYK differ, no repair call exists, probe blocked and gate denied |
| Close/identity failure | Exact close refusal is reported; still-open probe file is retained; lock released; readiness denied |
| Contention | No application method runs and another owner's lock is neither changed nor released |
| Meitu failure | Its exact launch failure is reported; safe-state and every later application check are blocked; lock released |
| Photoshop failure categories | Launch failure and probe-open failure remain distinct check results |
| Cancellation | Exact owned close is retried, probe is cleaned after confirmed close, cancellation is reported and lock is released |
| Static trust failure | Missing executable fails the static row; all live rows are blocked; live seam is never called; gate denied |
| Lock ownership | Real SQLite compare-and-swap permits one owner, exposes environment purpose, refuses a second owner and releases only the matching token |
| Page/gate agreement | Seven-row matrix proves each live safety failure is visible on diagnostics and denies Production through the same result |
| Existing boundaries | Architecture tests retain the single verifier/gate authority and deny public general scripting/process seams; existing state-classifier, foundation and per-attempt tests remain green |

The Production Readiness view-model test also proves the explicit asynchronous command replaces
the passive report with typed live detail and cannot be invoked twice while busy. Existing en-US,
zh-CN, rendering, AutomationId, navigation and gate regression suites were not weakened.

## Controlled live workstation proof

At **2026-09-08 12:55–12:56 NZST**, the opt-in
`WorkstationVerificationSmoke.Run_the_explicit_live_application_verification` used the real
production service graph, committed configuration, real preset/evidence, real foundations and an
isolated migrated SQLite database. It passed. Actual report identity:

```text
printflow-workstation-v1 1.16.0 (6396FB4EB87F)
observed 2026-09-08 00:56:16Z
```

| Check | Expected | Current/result |
| --- | --- | --- |
| Static blocking checks | 10/10 pass | 10/10 pass; preset and 28/28 evidence digests match; OS, binaries, Action, accepted workspace identity/addressability, session, display 1920×1080 / 96 DPI / 100%, and UI culture match |
| Automation lock | Acquired for bounded verification | Acquired, then independently observed free |
| Meitu launchability | Accepted process reaches recognised state | **Launched**, PID 14884 |
| Meitu safe state | Recognised state | `KnownWelcome` |
| Photoshop launchability | Accepted process reaches recognised state | **Attached** to existing PID 6560; no second instance launched |
| Photoshop safe state | Recognised; no unsaved document | `KnownStartScreen`; no document open |
| RGB | sRGB IEC61966-2.1 | sRGB IEC61966-2.1 |
| CMYK | Coated FOGRA39 (ISO 12647-2:2004) | Coated FOGRA39 (ISO 12647-2:2004) |
| Gray | Dot Gain 15% | Dot Gain 15% |
| Spot | Dot Gain 15% | Dot Gain 15% |
| Test image | Open, identify, close without saving, restore state | Passed; exact synthetic probe cleaned; Photoshop returned to prior state |

The two non-blocking automatic advisories were also reported exactly: 16 of 28
integrity-referenced files do not currently carry the Windows read-only attribute although every
accepted SHA-256 matched, and the preset records Meitu zh-CN / Photoshop Simplified Chinese. They
do not make the workstation unsafe under the accepted contract. No probe files remained. Both
applications were left running, consistent with the conservative ownership rule.

Current machine result: **Production Ready / gate ALLOWED** immediately after live certification.
This is current-state evidence, not a promise that later workstation drift will pass; passive
re-observation fails closed if the certified state changes.

## Build and tests

- Final clean build: **0 warnings, 0 errors**.
- Final targeted verifier/gate/readiness/composition/lock/architecture filter: **224 passed, 0
  failed, 0 skipped**. Result:
  `tests/PrintFlow.Tests/TestResults/scrum-11110-targeted-final.trx`.
- One legacy W1 evidence fixture initially omitted the newly required four colour-space fields.
  The first complete run therefore truthfully recorded **11,512 passed, 1 failed, 0 skipped**. The
  fail-closed Product rule was retained; the synthetic signed manifest was completed, and its
  isolated class passed **3/3**.
- Final complete suite against corrected final source: **11,513 passed, 0 failed, 0 skipped** in
  4m42s. Result:
  `tests/PrintFlow.Tests/TestResults/scrum-11110-full-suite-final-rerun.trx`.

The accepted baseline was 11,494; the final suite has 19 additional tests. The opt-in real
application smoke is deliberately inert in an ordinary suite and is reported separately above.

## Jira reassessment and Git discipline

**SCRUM-11110: PARTIAL → FULL.** Every material clause of the original AC now has Product,
deterministic test and safe live-workstation evidence. A future machine can legitimately report
Not ready without changing this capability classification.

**Parent Epic SCRUM-11107 / original Epic 11500 remains PARTIAL.** SCRUM-11112 recovery gaps are
outside this slice. SCRUM-11118 Settings/preset display is also unchanged; expected/current facts
needed for diagnosis are not editable configuration.

Work was performed only in `D:\Repositories\printflow-Studio` on canonical `master`. Product,
tests and these records are committed locally without amend, rebase, branch, worktree,
Co-Authored-By or AI attribution. Nothing was pushed. No external preset, Action, evidence,
application setting or customer file was modified.

**PASS — SCRUM-11110 OPERATING ENVIRONMENT CHECK VERIFIED**
