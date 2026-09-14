# PF-DIAG-MEITU-INPUT — Click Tool vs Product Input Capability

DIAGNOSIS PARTIAL — current same-target native/UIA measurements and dismissal proof unavailable.

14 September 2026, DESKTOP-0BG8884. Canonical master started at
`c51fe622a5fd0a4392e9fb4de7677906b4c5b9b4`; tracked files clean, operator-owned
`printflow-remediation-prompts/` untracked and preserved. This is a current LOCAL source review,
not a replay of the older remote audit. No Product code was changed.

## Plan and authority

Read the latest A1 report, PLAN/HANDOFF, attached diagnostic prompt, JPG-to-PNG remediation,
R2 lease implementation/helper, and R3/R4 handoff conventions. Compare retained observations
with current providers and their callers; choose an action only after current target verification;
verify dismissal by fresh readable state under the same lease; otherwise record missing proof.

The user was asked once for current exclusive-window confirmation. They replied that they cannot
make the notice appear on purpose and selected operator help to dismiss it when it appears.
This is not a current target-presence or exclusive-window confirmation. Do not recreate onboarding,
infer TARGET_NOT_PRESENT from that answer, or automatically watch for/click the notice.

The current tool schema explicitly says native computer APIs are disabled. The Computer Use skill
requires `node_repl` and `@oai/sky`; no callable `node_repl` is available in this execution.
No fresh Computer Use probe or alternate desktop channel was attempted around that restriction.
This is a current execution-channel limitation, not evidence that PrintFlow's provider is defective.
No tool security rejection occurred; no permission settings were changed.

## Evidence and capability comparison

Fresh inspection record, source hashes and extracted historical notice lines:
`artifacts/pf-diag-meitu-input/20260914-source-comparison-151727/comparison-evidence.json`.
It records inspection time separately from original observation time. It is not a live UI capture.
Original before/after-error JSON remains at `artifacts/pf-accept-a1/meitu-notice-*.json`.

Historical before observation: `2026-09-14T02:49:52.708Z`, tool window id 463094,
title `美图秀秀-图片编辑`, application path
`C:\Users\admin\AppData\Local\MeituApp\XiuXiu\7.8.8.2\XiuXiu.exe`.
The A1 report records PID 21884 and executable SHA-256
`9276B407F855A02F65B04FF0EBEAC1E778F6C8F24D25413A1C2D2853A3F96B0B`.
These are historical identities, not revalidated current process facts.

| Layer | Equivalent-target evidence and current source capability | Limits / admission |
|---|---|---|
| Computer Use mapping | Historical tree maps the editor and nested notice; screenshots array is empty. A1 records capture `SetIsBorderRequired / 0x80004002` and click `coordinate input geometry is unavailable`. | No current probe; retained JSON does not expose rectangle/point/pattern measurements. Capture error and click geometry error are not causally linked by available evidence. Tool id is not promoted to a verified native HWND. |
| Native host | `Win32ExternalAppWindowLocator` attributes enumerated HWNDs to PID, reads GetWindowRect, class/title, visible/minimized/enabled; Refresh rereads the handle. Process lookup checks exact executable path and start time. | Current HWND/owner/client rectangle, visibility, foreground, DPI/session/integrity and executable hash were not measured. FindTopLevelWindows filters invisible/empty bounds; FindOwnedDialogs checks nonzero owner, not exact equality to the supplied main host. A virtual Qt child need not have an HWND. |
| UIA identity | Historical tree explicitly marks `MainWindow.NoviceGuideWidget.MessageGuideWidget` as an **ID**, beneath `MainWindow.NoviceGuideWidget`; titleLabel Name `AI助手来啦`, messageLabel Name `选中底图，在图片编辑里也能随时和AI对话改图啦，快试试吧~`, button Name `我知道了`, full ID ending `.MessageGuideWidget.okButton`. | These are reported IDs and tree relationships, not ClassName values. Button 623 is a historical index only. Native FromHandle ancestry, bounds, enabled/offscreen, clickable point and supported patterns remain unmeasured. |
| Product lookup/Describe | `UiaElementProvider.RootOf` uses FromHandle. Find uses FindFirst; FindAll exposes ambiguity. Queries use exact Name/AutomationId properties. Describe reads class, type, PID, bounds, enabled/offscreen and a closed pattern vocabulary. | No live Product FindAll/Describe result exists here. GetParent uses ControlView; retained Computer Use tree is not proof of identical Product ancestry. Unsupported pattern kinds are omitted by Describe. No client-side provider registration was found in the inspected automation directory. |
| Product semantic Invoke | Provider calls InvokePattern if exposed, otherwise SelectionItem.Select; neither route depends on Computer Use screenshot geometry or a clickable point. | No notice-specific Invoke availability or effectiveness proven. SelectionItem is not a generic notice-button substitute. Provider return alone is insufficient; primitive does not supply the caller's full target/foreground/lease policy. |
| Product pointer route | ClickAtLiveClickablePoint checks enabled/onscreen/nonempty bounds, process instance and roots, exact foreground, a current point inside bounds, exact FromPoint hit and virtual desktop bounds. It repeats process/foreground/hit validation before SendInput. | Independent of Computer Use capture geometry, but not independent of native/UIA geometry. No measured point here; no call made. Existing caller is the signed PNG-format popup route, not this notice. |
| Native button sink | Win32VerifiedControlSink locates native descendants by control id/class with unique-match checks; Press revalidates handle ownership/class/visible/enabled then sends BM_CLICK. | No compatible notice child HWND/control id/class demonstrated. Do not send BM_CLICK to the Qt host. This sink's guard is not a notice-recognition contract. |
| Product capture | GdiWindowEvidenceSink uses bounded host dimensions, PrintWindow then window-DC BitBlt fallback, WIC PNG encoding. | Different backend from Windows.Graphics.Capture; not run. A successful capture API return would still require visual inspection for blank/unusable pixels. Host rectangle cannot stand in for notice-button bounds. |
| Product caller/state | FindSignedControl → FindAll/Describe → MeituCardTargetRule.SelectSignedControl checks unique full signature, PID, pattern, enabled/onscreen. Its RejectControl does not require bounds. Card/enhancement owner rules do require marker containment. Export pointer caller requires signed popup/item shapes, bounds and exact Save foreground. Classifier uses baseline-backed positive markers and falls to Unknown. | Source search found no AI-assistant/NoviceGuide/MessageGuide/我知道了 handler. Primitive capability is not integrated notice support. No live classifier result was obtained and no specific Product guard refusal was executed on this notice. |

Sources are the named files under `src/PrintFlow.Infrastructure/Automation/` and
`src/PrintFlow.Infrastructure/Adapters/Meitu/`. The prior JPG-to-PNG report demonstrates why
pattern returns cannot establish effects: Invoke/Selection returned normally on that **different**
Qt popup without changing its value. Its later pointer success is not evidence of notice dismissal.

## Exact action and lease outcome

Current target verified: **NO** → route selected: **none** → dispatch attempted/sent: **NO/NO** →
input return/exception: **N/A** → reacquired postcondition: **NOT OBSERVED**.
No automated or operator dismissal was observed in this execution. Historical after-error tree
still contains the notice; it does not prove present-day state. No fixed coordinates, keyboard
sequences, provider guard bypass, native messages, app activation or business operation occurred.

Lease acquisition/observation/release: **NOT EXECUTED**. No lease was held by this task;
current shared availability is unknown, not Free. The real authority remains
`SqliteWorkstationAutomationLeaseManager`, resource `printflow-studio.external-automation.v1`,
`%LOCALAPPDATA%\PrintFlow Studio\workstation-automation-v1.db`.
The opt-in WorkstationAutomationLeaseScope acquires through TryAcquireAsync(null) and checks
ReleaseAsync during unwind. Any future authorized agent input must keep that actual ownership
through dispatch completion and post-observation; Busy/Unknown refuses action. No database rows
were read or edited here. Photoshop readiness was not requested or inspected.

## Classification and smallest next implementation

**INCONCLUSIVE for the live failing layer; high confidence in the source-level separation.**
Computer Use's historical failure does not establish that Product Invoke or native pointer input
fails. Conversely, callable source does not prove either works on this target. TOOL-ONLY,
TARGET/PROVIDER and a live PRODUCT-PATH refusal are not established. Absence of an integrated
known-notice rule is a source finding, not proof of the historical cutout failure's cause.

Respect the user's selected fallback: when this notice naturally appears, pause and request
operator assistance. The smallest next implementation candidate is a narrow operator-help message
at the existing unknown-state stop, supported by a freshly verified exact notice signature; retain
the stop and all readiness guards. Do not implement a generic text click or automatically resume
the completed A1 run. No Product implementation is warranted solely from the old tree.

Prerequisite before implementing notice-specific detection: a permitted current FromHandle-scoped
capture proving exact title/body/button ancestry and uniqueness. Verification should exercise the
rule with exact/wrong/ambiguous notices and show no input, then on a natural live appearance read
the notice before and after the operator dismisses it. A readable tree must show absent/hidden
notice, the same underlying editor and observable document identity, no new dialog, and the actual
classifier result. If automation is reconsidered, separately prove one unchanged provider route
under the real lease; log method return separately from that postcondition. This is a proposed
verification procedure, not an implemented executable helper. Follow-up NOT IMPLEMENTED.

## Validation, routing and preservation

Docs/evidence only: no diagnostic code or assembly created; no build required; tests and live
tests NOT RUN. Self-review only; no independent reviewer or child agents. `git diff --check`
is the scoped document validation. Commands used: Get-Content, rg/rg --files, Get-FileHash,
ConvertFrom-Json/ConvertTo-Json, git status/rev-parse/check-ignore and scoped documentation edits.

Installed policy `C:\Users\admin\.codex\workflows\development-routing.md` v2.3
(CODEX_HOME unset; installed conventional home used). EXECUTE_HANDOFF, CONTINUE.
NormalRoute gpt-5.6-sol/medium for bounded source diagnosis; explicit RouteOffset -1;
RequestedRoute/ExecutionTarget gpt-5.6-sol/low; adjustment APPLIED to planning only.
ActualRoute UNVERIFIED; MODEL_SWITCH_UNAVAILABLE for this context, no alternate execution claimed.
Re-evaluation after comparison retains source/docs-only scope; no UI implementation was performed.

Only this report and the diagnostic HANDOFF are intended for a local commit. Operator prompt
bundle, old A1 results/claims/outputs, both build pairs, accepted presets/assets, portrait assertion
and version-exception publication ban remain untouched. No branch/worktree/clone, reset, amend,
rebase, push/install/deploy, normal-App E2E, Operator artwork review or Jira update.

Product integration NOT CHANGED. Meitu 7.8.8.2 production acceptance NOT EARNED.
Standard set NOT RERUN. Real production revalidation NOT WRITTEN.
