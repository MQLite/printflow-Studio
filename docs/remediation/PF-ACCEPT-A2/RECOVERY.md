# A2 recovery continuation — 16 September 2026

Status: owned-probe recovery verified live; further fresh-round-trip correction under diagnosis.
This report appends current evidence without replacing the historical failed attempt.

## Observed state and bounded diagnosis

Photoshop PID 1488 has creation time 2026-09-15 11:03:56 local and executable
`D:\Adobe Photoshop CC 2019\Photoshop.exe`, consistent with the earlier accepted process.
Computer Use initially returned a minimized window naming `Choppers.tif`; accessibility capture
refused because it was minimized. Supported activation, window refresh and accessibility read
then returned `Adobe Photoshop CC 2019` with no document surface. This corroborates the
operator's empty-screen observation but does not constitute a full document census. No Close,
Escape or Save As input was sent. No old probe was deleted. Closure cause remains unknown.

Source and original failure agree: the round trip reached CloseGuard; the close helper invoked
ProbeDocumentIdentityAsync, requesting SaveAsProbe again before CloseActiveDocument. The owned
Save As window did not appear within 20 seconds. The previous identity probe had returned only
after CancelDialogAsync/AwaitDialogClosedAsync reported its surface gone and host enabled.
The source does not establish why the subsequent request produced no matching window. There is
no evidence that a Close request occurred. The existing filename-field settling fix is intact.
No speculative change to guards, timeouts or input dispatch has been made.

## Verification and preserved identity

The original candidate was launched through supported Computer Use and read on its normal Home
screen; no interrupted or customer operation was selected. All 29 primary evidence hashes,
four candidate Product assemblies, and 182 retained harness files match their recorded values.
A1 remains Passed for the original pair. Preset 1.18.0 and the active published record are
unchanged. There was no writer invocation, replacement pair, A1 rerun, or Product source edit.

Targeted command (installed SDK, no live smoke opt-in):

```powershell
& 'C:\Users\admin\AppData\Local\Microsoft\dotnet\dotnet.exe' test tests/PrintFlow.Tests/PrintFlow.Tests.csproj --no-restore --filter "FullyQualifiedName~GuardedPhotoshopUiDriverTests|FullyQualifiedName~ProductionLiveWorkstationVerifierTests|FullyQualifiedName~EnvironmentReadinessScreenTests" --verbosity quiet
```

Result: 88 passed, 0 failed, 0 skipped. Initial unqualified `dotnet` resolved to the system SDK 8
and failed SDK selection before testing; the installed per-user SDK resolved this without install
or repository configuration changes. No full suite is justified by documentation-only changes.

Canonical lease read using a read-only SQLite connection at 2026-09-16T04:44:15.953Z found the
resource row with all ownership fields null. No lease was acquired, fabricated or evicted here.
The normal Product live action must acquire its real lease when it runs.

Local raw evidence: `artifacts/pf-accept-a2/recovery-window-observations.json`,
`recovery-processes.json`, `recovery-preservation.json`, `recovery-harness-preservation.json`,
`recovery-lease-observation.json`, and `recovery-normal-app-latest.txt`.

## Pending ordinary action and honest gate result

The permitted click on Home.ShowEnvironment failed with `coordinate input geometry is unavailable`.
The operator was asked to open **生产就绪状态**, click **运行实时应用检查** once and report completion.
This is a tooling limitation, not a proved Product defect. Do not substitute an injection script
or test host for the normal gate. Reuse the already received exclusive-window confirmation.

Current fresh gate result: NOT RUN in the newly launched normal candidate. Last completed normal
gate remains the historical failed round trip; no current readiness success is claimed. Probe
absence still needs a complete successful Product document read. Historical A1 publication is
preserved and does not itself grant current admission.

An ordinary recheck button exists and requires no technical operator instructions. Whether that
action completes this recovery is still unverified. No Product recovery correction or integrated
operator-friendly recovery capability is claimed. Continue after the narrow operator action;
if the failure persists, diagnose its specific current condition and apply the authorized repair
branch rather than repeating identical attempts. A3 remains out of scope.

## Operator check completed — 2026-09-16 05:01:58Z

The operator supplied the actual repeated-failure screenshot; supported Computer Use read and
retained the full ordinary report as `recovery-normal-app-repeat-failed.txt`. ProductionRevalidation
still passed for the frozen original candidate. The complete pre-probe Product runtime read
reported `KnownStartScreen; No document is open.` This positively establishes the original
`6eff41591db34c0b91177b34d398d103` probe was AlreadyAbsent before this attempt. Its historical
failure is retained; the cause/time of its disappearance and any automatic close remain unproven.

The new attempt `60f7def57ed14ae1bee7a9cc8cfbb45e` failed at the same CloseGuard identity-dialog
wait. No CloseRequested or CloseConfirmed occurred. Fresh window observation named this new
probe. A getter-only opt-in smoke using the existing Product runtime reader under the actual
shared lease read a complete census at 05:06:44.8272459Z: accepted Photoshop PID 1488, same
start time, exactly one document, exact new managed probe path, saved and active. No unrelated
document was reported. This reader produced no input, cleanup or authorizing readiness result.
The normal logger omitted successful-test output on the first read, so a second read with
detailed logging captured the facts; neither invocation mutated Photoshop. Raw output is
`recovery-readonly-census-detailed.log`; the lease was independently free at 05:07:32.888Z.

Repeated original-gate result: FAILED, admission CLOSED. This repeat is discriminating evidence
against treating the earlier failure as only stale display state. Candidate changes are now
being developed separately: normal Product recovery/identity boundary and compact readiness UI.
No frozen pair, original result, preset or publication has been patched.

Routing at the natural backend/UI boundary: offset 0 unchanged; requested native backend worker
`readiness_backend` with `gpt-5.6-sol/high` and UI worker `readiness_ui` with `gpt-6-astra/high`,
each given bounded fresh instructions and separate file ownership. These are implementation
workers, not independent reviewers. Runtime model/effort metadata remains UNVERIFIED. Root
retains sole desktop/executor ownership; workers run offline work only. Root owns the getter-only
diagnostic smoke, evidence, integration and final qualification. There is no claimed parent
model switch. One scoped independent read-only reviewer will follow stable changes.

## Product correction and independent review

The normal readiness command now reconciles only one census-observed, exact canonical probe in
the reserved workspace path; it never enumerates old probe files. It requires the real shared
lease, sole active saved document, canonical bytes, no reparse traversal, accepted process
continuity and known modal state. It re-proves the stricter recovery conditions at the final
runtime census. A missing reader, unknown state, changed file, other work or contradictory
current title refuses input. Close settlement and post-close observation remain under ownership,
including cancellation; only the exact confirmed-unheld canonical file may be removed.

The normal foundation close uses the already-supported getter-only runtime identity instead of
raising a second Save As dialog. It retains the final refreshed-window, process, foreground and
modal guards and existing no-save close behavior. Ordinary dirty-owned-document cleanup remains
distinct from recovery's stricter saved/sole condition. The demonstrated defect is the repeated
modal identity route failing before close; the underlying Windows/Photoshop timing reason for
the missing second dialog is not claimed as proven. No timeout inflation or broad guard removal.

An already-absent known probe records AlreadyAbsent, not a historical close. Missing files and
removed empty token directories are idempotent; unreadable state is never empty. Recovery is
separate from fresh probe success. Previous diagnostics, when available, retain their failure;
old probe timestamps are not relabelled by a new preflight refusal. Restart recovery records
current identity provenance rather than inventing a previous attempt.

The existing ordinary action is labelled **安全恢复并重新检查 / Safe recovery and recheck**.
Passive **刷新状态 / Refresh status** stays read-only. Processing and cancellation are visible;
overlapping actions and Back stay disabled until awaited work settles. Operator status comes
from typed recovery keys, and readiness only from the authoritative report. Technical detail,
full checks and restart help start collapsed. English/Chinese offscreen source renders were
inspected at 1000x700 with no clipping or binding error; these are not native live screenshots.

One isolated native reviewer, `a2_recovery_review` (requested gpt-6-astra/high, offset 0; actual
runtime metadata UNVERIFIED), read source and evidence without builds, tests or desktop actions.
It found two P1s: recovery's final census initially allowed a newly dirty/shared document, and
title corroboration initially used a pre-activation snapshot. Both were fixed with red-to-green
sequenced tests; the same reviewer re-read the final dispatch guard and reported both closed,
with no remaining blocker in its affected scope. This is source review, not live acceptance.

Focused backend checks: 101 passed, covering lifecycle/recovery and foundation/guarded close.
UI/localization/diagnostics boundary slice: 133 passed; final wording/render subset: 5 passed.
The final clean build/full suite and live qualification are recorded below when completed.

Clean Release build passed with zero warnings/errors. The first full suite completed with
11,932 passed and one failure: its lexical Process.Start prohibition also matched two new
diagnostic StartedUtc getter expressions. No launch occurred. The diagnostic code now names the
immutable observed instance explicitly; the prohibition/assertion is unchanged. A first alias
still contained the banned substring and failed the focused check; the final name removed the
collision. The affected architecture/live-verifier slice then passed 67/67. Both failure logs
remain under artifacts/pf-accept-a2. Runtime recovery behavior was not changed by this correction.

The reviewed, focused-tested fix is committed before creating the isolated controlled pair.
Its clean paired harness build will supply the final complete suite, before any live diagnostic
or new standard-set run. This verifies the same retained build used for qualification; no
further conventional-output rebuild or tests are needed unless a real failure requires repair.

## Controlled pair and new discriminating live evidence

Stable correction commit `38d102985cd1e73eec589b12f7f3a0a72eb3a07a` produced controlled pair
`a05e8547-e10a-4154-9834-516035d29632`. Its receipt and complete harness/candidate inventories
verified. Final paired suite: **11,933 passed, zero failed/skipped**, 5m45s; raw log
`recovery-final-paired-suite.log`, TRX in `final-paired-test-results`. The normal candidate was
launched without arguments and its ordinary readiness page showed ProductionRevalidation
as the blocking check. The recovery button, plain-language status and collapsed details were
observed through supported native accessibility. It was exited normally before live diagnosis.

The first repaired diagnostic **did recover** exact probe `60f7def57ed14ae1bee7a9cc8cfbb45e`:
Product recorded ClosedExactProbe, same accepted process 1488/start time, sole saved active
canonical document, close plus complete empty census, then exact backing-file removal. This
is a current automatic close observation; it does not revise the original `6eff...` history.
However, fresh probe `310afeea7de9492a8f52185c8be701f2` failed Open-field exact readback. Product
did not press Open, cancelled its dialog and retained the backing file. Getter-only complete
census at **05:52:00.7358051Z** proved zero documents in the same process.

One evidence-supported recheck from that confirmed settled empty state passed Open and initial
identity but failed at CloseRequested: fresh probe `b87a8bf95a02444796b8ff3f53831f9c` remained
after the existing close timeout. Complete getter-only census at **05:54:04.1560696Z** confirmed
it remains the sole active saved document at its exact managed path. Native window observation
corroborates the title with no visible modal. Thus this is a real retained document, not merely
a stale title. Both retained fresh files have the canonical 68-byte probe hash. No blind repeat
Close or SaveAs input was sent. Diagnosis now compares recovery's successful runtime-only close
against the fresh route's remaining initial SaveAs identity/cancel transition. Both diagnostic
envelopes retain ProductionAuthorised=false; neither is a readiness pass. No v3 run or new
publication has occurred. Logs: `recovery-repaired-live-diagnostic.log`,
`recovery-settled-live-diagnostic.log`, `recovery-post-cancel-census.log`, and
`recovery-post-close-timeout-census.log`.

The follow-up correction is confined to the runtime-backed initial-open identity boundary.
It avoids the remaining SaveAs/cancel transition by using the existing complete getter-only
census, verifies unique same-process continuity before/after it, exact active path/name and
final modal/window state, and retains the legacy reader-null route. It does not claim the
underlying Windows timing mechanism is proven. Five new red failures established the missing
runtime-identity branch. The same independent reviewer found a coarse final title-prefix
comparison; a dedicated staged shared-prefix drift test went red, then green after applying
the existing exact-name signature rule and missing-signature refusal. Reviewer recheck found
no remaining blocker in this narrow scope. Related tests passed 107/107, architecture boundary
tests 34/34. Evidence summary: `runtime-open-identity-red-green.txt`. Source is frozen for a
new controlled pair/final suite before further live work. Pair a05 and its evidence stay intact.
