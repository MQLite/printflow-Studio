# PF-ACCEPT-A2 — Handoff

**Latest continuation: Photoshop recovery and complete fresh round trip are verified after
Product repair. Replacement qualification FAILED at a separate Meitu Save-surface foreground
guard (2/7 categories passed). Ordinary A2 admission is CLOSED. A1's original PASS and
published record remain unchanged. See the final evidence in [RECOVERY.md](RECOVERY.md).**

The older sections below are historical observations and authorization, not a statement that
their named probes are still open. Prompt 19 and the current user request authorize scoped
repair/recovery and corrected-candidate qualification; no A3 is authorized.

## Executor and authority

Codex executed A2 only on `DESKTOP-0BG8884`. Started clean on `master` at
`c48387c657ff1439ece6f47428e110b05ffb5df6`, matching the supplied A1 closure. No reset.
Installed routing policy v2.3, explicit route_offset 0, actual model/effort UNVERIFIED;
MODEL_SWITCH_UNAVAILABLE for the active context. See PLAN.md for targets versus actual execution.
The operator explicitly confirmed the current exclusive A2 checkout/desktop window before the
write/live check. Claude Code was not used as an executor. No A3 was started or delegated.

## Exact retained identity

- SetRoot: `D:\PrintFlowStudio\TestData\v3`.
- RunId: `a1-v3-20260916-121334-903986e7`.
- Result: `D:\PrintFlowStudio\TestData\v3\runs\a1-v3-20260916-121334-903986e7\result.json`.
- Result SHA-256: `97E422FA53002725F8222A4C38075A8E6B43C7A169442DF3F1CBBC501DB9600A`.
- Original pair: `f0ac92e2-853e-4d2f-a6e4-c0af1a24e0e8`, source `a39846d8e0398e061e445fc79fe219b9bb195401`.
- Receipt: `D:\Repositories\printflow-Studio\artifacts\pf-accept-a1\build-pairs\f0ac92e2-853e-4d2f-a6e4-c0af1a24e0e8\build-pair.json`.
- Receipt SHA-256: `BEBEDFFC235F40AE66554F13D0BD0354C20DE75F41FA654341621DA836BFBDC4`.
- **Exact candidate folder:** `D:\Repositories\printflow-Studio\artifacts\pf-accept-a1\build-pairs\f0ac92e2-853e-4d2f-a6e4-c0af1a24e0e8\candidate`.
- Exact executable: that folder's `PrintFlow.App.exe`; sibling `harness` is retained but was not
  used for normal-App verification.
- Preset: `printflow-workstation-v1` 1.18.0; manifest SHA-256
  `8484F0AA18728FAC58B58ACD8E811C7058743B61271506647179CA0D0A6C8E0F`.
- Effective candidate configuration: workspace `D:\PrintFlowStudio`, Production adapters,
  `Baseline\workstation-v1\preset\printflow-workstation-v1.18.0.json`, expected hash above;
  no `appsettings.local.json`. Product version 0.1.0, Windows build 19045.

The supported VerifyOnly route revalidated four Product assemblies on each side plus all 182
retained harness files. Result/receipt/preset/readiness, manifests/inputs and actual outputs were
rehashed. Seven unique categories Passed, 59 held assertions, zero failed, three distinct real
Operator decisions across two non-synthetic reviews, CandidateProblems empty. The claim, database,
fine-hair case, A1 live log/command and both decision inputs match A1 closure hashes too. No A1
review, reaggregation or execution occurred. Older Pending/Failed reports remain historical.

## Publication and independent disk readback — PASSED

The user expressly authorized the named A1 readiness as the existing operator EnvironmentReadiness
attestation. Its file is the run's `readiness.json`, SHA-256
`7F746BBD507A4693B4499B126E6EDD861DBEB7538E448D7CD8487AB879742E52`, Verified true, all blocking
checks Passed, recorded at `2026-09-16T00:13:59.7992809+00:00`. It is historical A1 evidence;
it is **not** the fresh normal-App verification reported below.

No active record existed before publication, so no record required replacement/backup. The first
attempt under Windows PowerShell stopped at preset JSON parsing before writing (UTF-8 text was
misdecoded); absence of an active record was checked. No file or script was repaired. The same
successful-publication route under installed PowerShell 7 exited 0:

```powershell
pwsh -NoProfile -File tools\installer\Set-PrintFlowProductionRevalidation.ps1 `
  -InstallFolder 'D:\Repositories\printflow-Studio\artifacts\pf-accept-a1\build-pairs\f0ac92e2-853e-4d2f-a6e4-c0af1a24e0e8\candidate' `
  -WorkspaceRoot 'D:\PrintFlowStudio' -EnvironmentReadinessPassed `
  -StandardRegressionSetPath 'D:\PrintFlowStudio\TestData\v3\manifests' `
  -StandardRegressionSetResult 'D:\PrintFlowStudio\TestData\v3\runs\a1-v3-20260916-121334-903986e7\result.json'
```

`StandardRegressionSetPath` is the manifests folder, unlike the review wrapper's `SetRoot`.
No omitted-result/revocation route, fake reader or hand-edited passing record was used.

Persisted record: `D:\PrintFlowStudio\Revalidation\production-revalidation.json`, SHA-256
`78B0464C6A10F43DF8D9B2AEF669D0B429D03A02911513CBCDBA2C252030B0FF`;
attested by `DESKTOP-0BG8884\admin` at `2026-09-16T15:08:54.4314947+12:00`.
Independent JSON disk readback compared schema 2, product version, four candidate hashes/build
identities, preset id/version/hash, Windows build, Meitu/Photoshop hashes, readiness flag and
regression set id/status/run/invocation/binding version/content digest/evidence path/completion.
All matched the original evidence and actual candidate bytes. Record remains unchanged after the
live failure; publication approval and current live admissibility are separate facts.

## Ordinary Product result — BLOCKED

The exact candidate was launched normally at `2026-09-16T15:09:34+12:00`, PID **35440**, no
arguments. Process executable path was independently read from Win32_Process. Home showed
existing interrupted/recent work; none was opened, resumed, abandoned or processed.

Current callable Computer Use `node_repl` + installed `@oai/sky` read the normal WPF UI. Its click
failed with `coordinate input geometry is unavailable`; Photoshop screenshot capture separately
failed with `SetIsBorderRequired ... 0x80004002`. No alternate UI injection was used. The operator
opened **生产就绪状态**, then was asked to click **运行实时应用检查** once. The actual report
establishes the following results independently of the operator's confirmation.

| Item | Actual result |
|---|---|
| Before live checks | ProductionRevalidation Passed; overall not verified, seven live checks NotRun in this process |
| ProductionRevalidation after live attempt | Passed, original candidate recognized against the persisted record |
| ExternalApplicationAutomationLock | Passed, canonical lease acquired for the bounded check |
| Meitu launchability / safe starting state | Passed, attached PID 11484, KnownWelcome |
| Photoshop launchability / starting state | Passed, attached PID 1488, KnownStartScreen; no document open at the initial check |
| Photoshop colour settings | Passed: sRGB IEC61966-2.1; Coated FOGRA39 (ISO 12647-2:2004); Gray/Spot Dot Gain 15% |
| PhotoshopTestImageRoundTrip | **Failed**, PhotoshopUnknownState at CloseGuard |
| Final ordinary readiness/admission | **Not verified / CLOSED**, six of seven live checks Passed |
| Probe cleanup | **NotRun**; exact close and prior-state restoration not confirmed, backing file retained |
| Lease after completion | **Free**; read-only canonical SQLite query found null OwnerToken and all ownership fields null |

The displayed failure was: `No window owned by Photoshop matching the signed '另存为' signature
appeared within 20s. Nothing further was sent.` This is the Product's existing message; A2 added
no signing requirement. Diagnostic attempt `2026-09-16 03:12:03Z`, probe
`6eff41591db34c0b91177b34d398d103`. Recorded stages: ProbeCreation, ProbeCreated, OpenGuard,
OpenRequested, OpenConfirmed, IdentityCheck, IdentityConfirmed, CloseGuard. Last attempted
CloseGuard; last confirmed IdentityConfirmed; no CloseConfirmed, PriorStateRestored or cleanup.
No second failure was listed. No live retry, blind dialog handling or Product repair occurred.

Final gate evidence is the ordinary EnvironmentReadinessViewModel report from the production
`VerifiedEnvironmentGate` registered as the same `IEnvironmentDiagnostics` singleton. It is not
a harness identity check, injected result or customer-operation test. Each later production
request still re-evaluates current conditions; publication is not perpetual admission.

Two nonblocking advisories remain: 16/30 accepted evidence files lack read-only attributes while
all hashes match; external-app UI-language information. Neither explains away the live failure.

## Current application and probe state — preserve for follow-up

At final observation PrintFlow PID 35440 remains on the failed Production readiness report.
Photoshop PID 1488 remains open with window title
`PF_ENV_PROBE_6eff41591db34c0b91177b34d398d103.png @ 100% (图层 1, 灰色/8)`.
Meitu PID 11484 remains open, title `美图秀秀`; the live check identified KnownWelcome.
Do not claim Photoshop was restored to document-free state.

Retained probe:
`D:\PrintFlowStudio\EnvironmentVerification\6eff41591db34c0b91177b34d398d103\Working\PF_ENV_PROBE_6eff41591db34c0b91177b34d398d103.png`,
68 bytes, SHA-256 `431CED6916A2A21A156E38701AFE55BBD7F88969FBBFC56D7FE099D47F265460`.
It was not manually deleted or closed. All seven historical retained probe files rehashed
unchanged. Canonical lease store:
`C:\Users\admin\AppData\Local\PrintFlow Studio\workstation-automation-v1.db`, resource
`printflow-studio.external-automation.v1`; no synthetic lease manager or outer lease was used.

## Evidence, reassessment and next boundary

Raw local evidence: `artifacts/pf-accept-a2/` — prepublication-verification.json, both publication
attempt logs/outcomes, published-record.json, record-readback.json, candidate-process.json,
normal-app-prelive.txt, normal-app-live-result.txt, lease-before/after.json, retained-probe.json,
probe-files-before.json, final-processes.json, final-preservation.json and A1 closure hash checks.
These are ignored local evidence, not committed customer data. A1 operator bundle and historical
records remain in place. See [clause reassessment](ASSESSMENT.md).

**Next A3 executor: Claude Code, only after separate authorization and resolution of the A2 live
gate blocker. Do not start A3 from this handoff.** Its candidate is the exact folder above, actual
gate is CLOSED and current Photoshop state contains the retained probe. A separately scoped
follow-up must resolve the real CloseGuard failure and safely handle that exact probe before a
fresh normal-App verification can establish admission. That repair is not authorized by this A2
publication task. No installation, deployment, push, full suite, standard-set rerun, new pair,
customer job, online Jira transition or final whole-project release was performed.

## Latest continuation — current-state reconciliation pending normal check

The earlier final probe-open statement describes the earlier observation only. The current
operator reports Photoshop empty and confirmed exclusive availability/saved work for this
execution. After restoring the same Photoshop process's minimized window through permitted
Computer Use, its refreshed title was `Adobe Photoshop CC 2019`, with no document surface in
the accessibility tree. The initial minimized title named `Choppers.tif`; it is insufficient
evidence of current documents. A complete current document read has NOT yet been obtained.
Neither probe presence nor AlreadyAbsent is asserted from a title or retained file.

The exact original candidate is now open normally on Home. Permitted `sky.click` failed with
`coordinate input geometry is unavailable`; no alternate injection channel was used. The
operator has been asked to open **生产就绪状态**, click **运行实时应用检查** once, and report
completion. Resume this same task by reading its actual normal report. No new check result,
automatic historical close, recovery success or A2 PASS is claimed. Do not repeat publication.

All 29 primary hashes, four candidate assemblies and 182 harness files match. Targeted tests
passed 88/88. The canonical lease was free at 2026-09-16T04:44:15.953Z; this is a timestamped
observation, not a perpetual claim. Product bytes remain unchanged. See [RECOVERY.md](RECOVERY.md).
No A3 is authorized.

## Final corrected-candidate continuation — 16 September, 18:23 local

User prompt 19 superseded the old repair ban. Exclusive desktop/saved-work confirmation was
reused within this execution. Root was the sole desktop executor. Bounded backend/UI workers
and the same sole read-only reviewer completed the offline changes; actual route metadata is
UNVERIFIED, offset0 unchanged. No claim of a parent model switch.

The original `6eff...` probe was proved absent before the operator's repeated check; its cause
of disappearance remains unknown. Original candidate repeated the SaveAs CloseGuard failure
with `60f...`. Product now recovers a census-observed canonical owned probe under the actual
shared lease and uses getter-only exact identity for runtime-backed open and close, avoiding
the unnecessary SaveAs modal transitions. Wrong/unreadable/dirty/shared recovery targets,
unknown modals, process changes and final exact-title mismatches still refuse input.

Core repair commit `1b948b7`, pair `fa0c9a3e-987d-4b32-befb-71ed9281ddeb` (full identity in
RECOVERY.md), passed final paired full suite **11,939/11,939**. Live diagnostic recovered b87a,
completed fresh d93d probe through cleanup, and passed all live checks. Its normal report and
ProductionAuthorised remained false. New v3 run `a2-v3-20260916-181700-fa0c9a3e` used this exact
pair, passed its own fresh readiness round trip with7f87, but FAILED at Meitu portrait identity:
the Save surface did not equal the foreground editor window, so no dialog control was used.
Subsequent external cases were blocked. Transparent PNG/reference TIFF passed. No new artwork
reviews are ready and no old decisions were transferred. The failed result remains Failed.

The ordinary candidate's **安全恢复并重新检查** action was executed at18:23. Its automatic
ProductionRevalidation check refused the unqualified bytes, so the seven live items were not
run. Their generic failure descriptions were found misleading. The final display-only fix
passed94/94 screen/localization tests and now truthfully describes them as unexecuted; this
does not open the gate or authorize another v3 run.

Current confirmed desktop: Photoshop PID1488 has a complete zero-document census at
06:19:15.8298154Z; Meitu PID5980 editor holds the visible **保存图片** panel from the failed
qualification, observed by native accessibility. No save/discard/dismiss input was sent to it.
Canonical lease all owner fields null at06:20:15.734Z. Original pair inventories and29 primary
hashes match after the run; accepted preset1.18.0 and original production record unchanged.

Next genuine boundary: scoped Meitu Save-surface foreground diagnosis/recovery, then a newly
authorized complete qualification run after a stable correction if needed. Prompt19 section7
authorized one new complete v3 run, which has been used; do not silently repeat it, publish the
failed replacement or present it as Pending artwork review. No A3/install/deploy/push/Jira or
customer work. Original A1 remains Passed for its original candidate only.

### Latest retained UI candidate and actual ordinary gate

Final source `f27fce130d63d9db697348d4bb52eb77a6826a17`, pair
`0298e108-5572-47ba-a3af-9bb5592cf6ff`, receipt SHA-256
`7AB33AF3B5E3203C5BCBFAC12FDD890F2D61C5454845E40838602F9FA5691053`.
Candidate: `artifacts/pf-accept-a2/build-pairs/0298e108-5572-47ba-a3af-9bb5592cf6ff/candidate`.
The complete pair inventory verifies. Relative to fa0c, bound source differences are only
two App resource files, the readiness row projection and its screen tests. Core runtime source
is unchanged; fa0c's full-suite/live proof and failed qualification keep their original identity.
No complete regression run or authorizing record exists for0298, and none is implied.

The actual0298 executable was launched normally, PID37024. At18:30 the ordinary primary action
was executed and returned **ProductionRevalidation Failed / live checks NotRun / admission
CLOSED**. All seven live rows now say, "前置检查未通过，本项未运行。请先处理未通过的检查项，再重新检查。"
Technical details remain collapsed and available. No duplicate probe input occurred because
the automatic gate refused before live execution. App is left on this normal readiness page.
Final lease read at06:30:57.914Z shows all owner fields null. Evidence:
`recovery-final-ui-identity.json`, `recovery-final-ui-normal-recheck.txt`,
`recovery-final-ui-processes.json`, `recovery-final-ui-lease.json`.

Ordinary operators have a plain recovery/recheck action; actual owned-probe recovery was proved
through the same Product route in fa0c. **They cannot yet complete production admission through
the ordinary app**, because replacement qualification has not passed and Meitu's Save panel
remains unresolved. Do not describe this as released/integrated into the original accepted
candidate. The one new complete v3 authorization has been consumed; request a new bounded
qualification authorization before another such execution. No A3.
