# PF-ACCEPT-A2 — Handoff

**A2 PARTIALLY COMPLETE / BLOCKED — real revalidation published and accepted by the normal
candidate; fresh normal-App live verification failed, so final production admission is CLOSED.**

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
