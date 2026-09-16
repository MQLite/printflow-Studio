# A2 recovery continuation — 16 September 2026

Status: Photoshop recovery and fresh round trip verified; replacement qualification FAILED
at a separate Meitu Save-surface foreground guard. Ordinary A2 admission remains CLOSED.
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

## Final repair verification and qualification boundary

Final Product commit `1b948b7e273720ba24574b5206bcc3ce7afa7cf7` produced controlled pair
`fa0c9a3e-987d-4b32-befb-71ed9281ddeb`; receipt SHA-256
`8A2BED95894DAB5BFDB08FA525AC5D2FECCEB1A85601710303650B4CB569A102`.
Final clean paired build succeeded and its complete suite passed **11,939/11,939**, zero
failures/skips, 6m30s. Log `recovery-runtime-identity-final-suite.log`; TRX under
`final-runtime-identity-test-results`. The same reviewer confirmed the final exact-name rule.

At **06:16:56Z**, diagnostic `b8ba7c7298c840549793cd22a3cb41e9` recovered exact retained
`b87a8bf95a02444796b8ff3f53831f9c` through Product with the real shared lease. It recorded
ClosedExactProbe, same Photoshop process instance, complete post-close empty census and exact
file cleanup. Fresh probe `d93d40470f944d7ca17360694aac3045` then completed every required stage
through CloseConfirmed, PriorStateRestored and CleanupCompleted. All seven live checks passed.
The envelope retains **NormalReport.Verified=false, DiagnosticReport.Verified=true,
ProductionAuthorised=false**. This is repair proof, not an ordinary A2 PASS or a publishable
qualification result. Raw log and extracted envelope: `recovery-final-live-diagnostic.log` and
`recovery-final-live-envelope.json`.

The one authorized new complete v3 execution ran as
`a2-v3-20260916-181700-fa0c9a3e`, invocation `7f348016-68a7-4099-bf5a-2bb782a92427`, with the
exact final pair and candidate, no category filter, version override or unbound mode. Preflight
passed all seven categories/hashes. Its own fresh readiness probe
`7f87d8d887bd45cf95cc7c14b6a5a97e` also completed and cleaned successfully; bootstrap readiness
was Verified with no blocking failures at 06:17:51Z. This separately corroborates the Photoshop
repair from an empty start; the bootstrap is not normal Product authorization.

The full run is honestly **Failed, 2/7 categories passed**, not Pending artwork review.
Portrait failed at Meitu's existing Save identity guard: expected Save surface `0x1A30E82`,
actual foreground editor `0xB020BA`, both owned by accepted Meitu PID 5980. No dialog control
was used. `GuardedMeituUiDriver.VerifyIdentityDialog` requires exact foreground-window equality
before identity read or cancel; the same refusal therefore leaves the owned Save surface open.
Fine-hair then failed Meitu live readiness; remaining external cases were blocked by lost live
readiness. Transparent PNG and reference TIFF passed. No portrait output or manual decisions
were produced. CandidateProblems is empty and run-origin verification passes. Result SHA-256:
`1BCB9EBB2477201E2E06AC40A40029A40CB1F278395C6E1AC3521E971234272D`.

Fresh native accessibility at 06:19Z shows Meitu's editor with nested **保存图片** SaveMaskWidget,
its path/name/format fields and save/close controls. This establishes the actual visible surface,
not why Windows foreground differs or authority to relax the guard. No speculative Meitu fix,
unknown-modal input, save, discard, or second full v3 run was performed. Current evidence:
`recovery-qualification-meitu-current.txt`, `recovery-final-v3-qualification.log`, and the run's
seven case files. The next technical step is a scoped Meitu Save-surface/foreground diagnosis
and guarded recovery; a further complete qualification run requires expanded authorization
because prompt 19 section 7 authorized one new complete v3 execution. Do not reuse this Failed
result, substitute old A1 artwork decisions, or publish either replacement pair.

Final getter-only complete Photoshop census at **06:19:15.8298154Z**: PID1488, original start
time unchanged, **zero documents**. Lease observation at **06:20:15.734Z**: canonical resource
has all owner fields null. Both original and final pair inventories verify; all 29 preserved
primary hashes still match, including original A1 PASS/result, original publication and preset
1.18.0. Recovered60f/b87 and successful d93/7f probe files are absent by verified cleanup;
original6eff and the cancelled-open310af backing files remain as historical evidence. No disk
sweep occurred. Final evidence: `recovery-final-photoshop-census.log`, `recovery-final-lease.json`,
`recovery-final-preservation-and-binding.json`.

The ordinary new candidate's recovery button and concise Chinese UI were observed; English and
Chinese source renders/tests cover layout and localization. Its ordinary gate correctly refuses
unqualified replacement bytes. A nontechnical operator has the normal recovery/recheck action
implemented, with the same Product route verified by diagnostics, but cannot currently finish
admission in this unqualified build. Neither the old accepted candidate nor an installed copy
has been patched. No install/deploy/push/Jira change, publication repeat, customer job or A3.

The final ordinary recheck at18:23 exposed a display-only defect: live Blocked/NotRun rows
used failure prose despite the automatic revalidation refusal preventing those checks from
running. Their text now says the prerequisite has not passed and the check has not run, in
English/Chinese. Actual Failed reports retain their failure/recovery wording; report status,
blocking flags and production authority are unchanged. Two locale tests failed before the fix;
the screen/localization slice then passed94/94 with no skips. Evidence:
`ui-notrun-followup-tests.txt`. This last App presentation change does not justify repeating the
shared Product full suite or the one v3 execution; it requires a separate retained build identity.
The fa0c core repair/qualification evidence is not relabelled as evidence for those new bytes.
