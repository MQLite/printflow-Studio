# Epic 11500 Part A — Production workstation verification foundation

Status: **11500-A PASS WITH NOTES — READY FOR ENVIRONMENT GATE INTEGRATION**

Scope delivered: a factual `IProductionWorkstationVerifier` that answers, with structured
reasons, whether this machine is the accepted production workstation. Production is **not**
enabled. `Adapters.Mode` is still `Fake`, `FoundationEnvironmentGate` is unchanged, and every
Production adapter still returns `EnvironmentNotVerified`.

---

## 1. v1.15.0 root of trust

`appsettings.json` names one preset and one digest, and that pair is the only thing this slice
trusts a priori:

| Configured | Value |
| --- | --- |
| Preset id | `printflow-workstation-v1` |
| Version | `1.15.0` |
| Path | `Baseline\workstation-v1\preset\printflow-workstation-v1.15.0.json` (under the workspace root) |
| Expected SHA-256 | `3392873ED0CA38BB410EA6725B6C4D0392F2514ECB10D9CF825B18D0DF785D16` |

The order in [PresetWorkstationRequirements.cs](../../src/PrintFlow.Infrastructure/Verification/PresetWorkstationRequirements.cs)
is the §4 order and is not negotiable:

1. resolve the configured manifest path;
2. open it read-only, no write share, and hash it in full;
3. refuse it unless the bytes hash to the configured digest — **only then parse anything**;
4. confirm the manifest's own `presetId`/`presetVersion` are the configured ones;
5. verify every `sourceManifestIntegrity` entry;
6. only then interpret workstation requirements.

A manifest whose hash is wrong is not a manifest with a suspect field — it is not evidence, and
nothing in it is read for meaning. A hash mismatch produces a result carrying exactly one check
(`PresetIntegrity`, failed) and no interpreted requirement at all. The expected hash is never
silently updated.

Evidence chain observed live: **27 of 27** entries present and hashing exactly, matching the
count v1.15.0 declares. The verifier reads the count from the manifest; nothing asserts "27" in
product code.

**No second workstation catalogue exists.** No path, digest, resolution, DPI, build number,
version string or culture tag appears as a literal in any verification source file. The one thing
written down is the manifest's JSON property spelling, reconciled in a single reader. A future
preset that changes an accepted value changes what the verifier demands, with no code change.

## 2. Verification result model

`WorkstationVerificationResult` ([source](../../src/PrintFlow.Infrastructure/Verification/WorkstationVerificationResult.cs)):

| Member | Purpose |
| --- | --- |
| `Verified` | Derived, never passed in — a result cannot claim verified while carrying a failed check |
| `Preset` | `ProductionPresetRef` (id, version, manifest digest), or `null` when integrity failed |
| `Checks` | Every check that ran, in evaluation order |
| `ObservedAt` | When the dynamic half was observed |
| `Failures` / `Advisories` / `ImmutableChecks` / `DynamicChecks` | Projections for the future gate |
| `Describe()` | One compact operator line plus one line per failure and advisory |

Each `WorkstationCheckResult` carries the typed check, its kind, its outcome, a `FailureCode`
when failed, a short `Expected`, a short `Observed`, and one operator-facing sentence.

Not a `bool`, and not a `Dictionary<string, object>`. The question the later gate has to answer
is not "may Production run" but "why not", and a caller that had to string-match a reason could
not be tested for having handled every one.

### Closed check vocabulary (§6)

`PresetIntegrity`, `EvidenceIntegrity`, `WorkspaceRoot`, `OperatingSystem`, `InteractiveSession`,
`DisplayConfiguration`, `UiCulture`, `ExternalApplicationUiLanguage`, `MeituExecutable`,
`PhotoshopExecutable`, `PhotoshopActionArtifact`, `FilesystemReadOnlyPolicyAdvisory`.

## 3. Immutable versus dynamic

| Immutable (signed baseline; evaluated once per process) | Dynamic (re-observed on every call) |
| --- | --- |
| `PresetIntegrity` | `WorkspaceRoot` |
| `EvidenceIntegrity` | `InteractiveSession` |
| `FilesystemReadOnlyPolicyAdvisory` | `DisplayConfiguration` |
| `OperatingSystem` | `UiCulture` |
| `MeituExecutable` | `ExternalApplicationUiLanguage` (advisory) |
| `PhotoshopExecutable` | |
| `PhotoshopActionArtifact` | |

The signed half is cached because those bytes cannot change under a running process without
invalidating the hash that was checked. The dynamic half is never cached: the operator can lock
the screen, attach a monitor or start a remote session between two production steps. A test
proves it — one verifier, a passing `Verify()`, a second monitor appears, and the next `Verify()`
fails on `DisplayConfiguration`.

## 4. Operating-system verification

Verifies exactly the four facts `workstation.operatingSystem` names: edition, version, build,
architecture. Patch level, installed updates, hostname, hardware and installed software are
unread — failing on one would be this code deciding what the shop accepted (§7).

Drift in what *is* named fails closed and states both facts. An OS upgrade is never auto-accepted.

Live: `Windows 10 Pro 10.0.19045 (build 19045, 64-bit)` — expected and observed identical.

## 5. Display and scaling verification

Read from `displayContract`: active display count, primary display, monitor bounds, work area,
system DPI and derived scale percent. Failure messages distinguish topology, scaling and geometry
so the operator reads "Display scaling differs from the verified workstation configuration"
rather than a rectangle dump.

The accepted `acceptanceRule` permits the virtual `OrayIddDriver` adapter to remain installed
while requiring the single-active-display topology, so the **adapter inventory is not read** and
the **active count is**.

**No coordinate automation is introduced.** No coordinate is computed, stored or sent anywhere in
this slice; the numbers exist only to be compared against the signed baseline. An architecture
test bans `SetCursorPos`, `mouse_event`, `SendInput` and `keybd_event` from the verification
namespace outright.

The preset writes the primary display as `DISPLAY1` and Win32 reports `\\.\DISPLAY1`; the two
spellings are matched on the trailing device name rather than by rewriting either vocabulary.

Live: `1 display, 1920x1080 at (0,0), work area 1920x1040, 96 DPI (100%)` — exact match.

## 6. Interactive-session verification

Facts read: `Environment.UserInteractive`, the process's session id (0 is the isolated services
session), `SESSIONNAME`, `GetSystemMetrics(SM_REMOTESESSION)`, and the name of the desktop
currently receiving input via `OpenInputDesktop` + `GetUserObjectInformation`.

Refused: a non-interactive station, session 0, a remote session while
`sessionContract.remoteAutomationAllowed` is false, an unreachable desktop, and a secure desktop
(`Winlogon` — a lock screen or elevation prompt). The last is called out separately because it is
the case that most looks like readiness and is not.

**Foreground ownership is untouched.** This asks whether a person is signed in at a usable
desktop, never which application holds focus. `IWorkstationFactReader` has no member that could
report a foreground window, so the verifier cannot consult one, cannot require Photoshop or Meitu
to hold focus, and cannot take focus itself — a test asserts the absence structurally. The Epic
11400 rule stands unchanged: another application owning the foreground at a guarded input yields
`PhotoshopTargetLost` with `inputSent=false`. Nothing here fights for the foreground.

Live: `session 1 'Console', desktop 'Default'` — a supported interactive local-console session.

## 7. Culture and external-application language

Three different languages are kept apart:

| | Fact | Live value | Verified? |
| --- | --- | --- | --- |
| Windows **user** UI language | `GetUserDefaultUILanguage()` | `zh-CN` | **Yes**, against `workstation.operatingSystem.uiCulture` |
| Windows **system** UI language | `GetSystemDefaultUILanguage()` | `en-US` | No — not part of the accepted baseline (§7) |
| PrintFlow's own UI locale | operator's selection | either | Irrelevant here |

`GetUserDefaultUILanguage()` and not `CultureInfo.InstalledUICulture`: on this workstation the two
genuinely differ (`zh-CN` versus `en-US`). The user language is what Meitu's and Photoshop's
Windows chrome follow, and therefore what the accepted external-app evidence was captured under.
Meitu's recorded `uiLanguage` is cross-checked against the workstation culture so a
self-contradicting preset cannot pass.

PrintFlow passing en-US and zh-CN visual QA says what PrintFlow renders, not what the applications
it drives render. The withdrawn Epic 11400 en-US limitation is not reintroduced in either
direction — PrintFlow's locale is neither required to be, nor required to differ from, `zh-CN`.

Photoshop's own interface language (`Simplified Chinese`) is reported as a **non-blocking
advisory**, not a static check. Reading it would require starting Photoshop, which §17 rules out.
This is not a contract gap: the accepted `photoshopContract.uiContract` recognises Photoshop by
window *class* precisely so recognition does not move when the UI language does, and the
operation-time guard confirms the surfaces it actually uses at the moment it uses them.

## 8. Workspace-root verification

Checks, in order: the configured root is the root `storageAndNamingContract.defaultOutputRoot`
requires; the path is not a file; it exists as a directory; it is not a reparse point (junction,
symlink or `subst`, which would make the accepted path and the storage it reaches different
claims); and the volume's file system is the accepted one.

Nothing inside is enumerated, read, moved or deleted. No cleanup runs and `CleanupWorking` is not
reachable from here. No customer content is scanned.

Live: `D:\PrintFlowStudio` present, a real directory, NTFS, not redirected.

## 9. Meitu verification

Path, SHA-256, and any version fact the preset records — read from the binary, with the
application never started. Managed subroots and the running Meitu process are untouched.

A changed binary fails with `MeituNotInstalled`. Evidence is never silently updated.

Live: `C:\Users\admin\AppData\Local\MeituApp\XiuXiu\7.8.7.5\XiuXiu.exe` → `D65C6D82…B037B1`, exact.

## 10. Photoshop verification

Path, `productVersion`, `fileVersion` and SHA-256, each demanded **only when the accepted preset
records it**. v1.15.0 records `productVersion` `20.0` and `fileVersion`
`20.0 (20200706.r.120 2020/07/06: 1208496)`, and the accepted binary reports exactly those.

The historical `20.0.10` shorthand is **not** demanded anywhere — the accepted binary does not
report it and it is not part of the preset authority. The authority is the exact hash plus the
recorded product/build facts, and a test proves the verifier demands no version the preset does
not carry.

A changed binary fails with `PhotoshopNotInstalled`.

Live: `D:\Adobe Photoshop CC 2019\Photoshop.exe` → `81EE8930…6A80C5`, exact.

## 11. Canonical Action-artefact verification

`photoshopActionContract` → set name `PrintFlow DTF`, artefact path, `artifactBytes` 1636, and
`artifactSha256`. Size is checked before the digest so an operator reading a failure learns
whether the file was replaced or edited.

The Action set is **not** loaded or reloaded. This verifies the canonical artefact on disk only;
runtime Action-palette integrity remains the operation-specific Epic 11400 Photoshop guard.

Live: `A04203ED…CD83EE`, 1636 bytes, exact.

**Observation (not a failure).** The preset spells the artefact `PrintFlow-DTF-v1.atn` while the
file on disk is `Printflow-DTF-v1.atn`. NTFS is case-insensitive, the path resolves, and the bytes
hash exactly, so the accepted contract is satisfied. Recorded here because a case-sensitive
filesystem would not resolve it; that is a preset-hygiene item for a future version, not a Part A
change (§27 — no preset is minted for verifier code).

## 12. Read-only attribute — advisory decision

**Decided: advisory, non-blocking.** v1.15.0's own `immutability.filesystemPolicy` raises and
settles it in one sentence: "Manifest and accepted evidence are marked read-only after hashing.
**SHA-256 remains authoritative.**" v1.15.0 makes the attribute a policy statement, never a
required verification condition, so it cannot close Production.

`FilesystemReadOnlyPolicyAdvisory` reports the count either way. **No attribute is read or
written by this slice** — a test asserts read-only evidence is still read-only after a
verification pass.

Live: **16 of 27** integrity-referenced files no longer carry the attribute — the same drift Epic
11400 Final QA found. Every one of those 27 digests matched, so verification is unaffected.

## 13. Failure reporting

Each failed check reports the typed check, the failure code, a short expected fact, a short
observed fact, and one concise operator sentence. Examples the implementation actually emits:

- *The Photoshop executable does not match the accepted workstation baseline. A changed binary requires preset revalidation; it is never absorbed silently.*
- *Display scaling differs from the verified workstation configuration.*
- *No supported interactive desktop session is available; this process is running in a service or otherwise non-interactive session.*
- *A secure desktop — a lock screen or an elevation prompt — currently owns input. That is not ordinary desktop readiness and is never treated as such.*

No evidence JSON is emitted; a test caps explanation length and requires both facts present. No
secret, customer file name, or arbitrary machine inventory appears on the result.

## 14. Architecture boundaries

Enforced by [WorkstationVerificationBoundaryTests.cs](../../tests/PrintFlow.Tests/Architecture/WorkstationVerificationBoundaryTests.cs):

| Rule | How |
| --- | --- |
| Verifier and readers live in Infrastructure | Assembly + namespace assertion |
| Domain / Workflow / App name no verification type | Source scan |
| Domain / Workflow contain no registry, Win32 or shell environment code | Source scan for `Microsoft.Win32`, `RegistryKey`, hive roots, `DllImport`, `LibraryImport`, `Process.Start`, `Environment.OSVersion`, `GetSystemMetrics` |
| No shell/script surface anywhere in `src` | `powershell`, `PowerShell`, `cmd.exe`, `RunPowerShell`, `RunCommand`, `RunScript` |
| Process start confined to the one guarded Epic 11300 launch site | Banned in Domain/Workflow/App **and** in `Verification\` |
| Registry reached in exactly one file, through no caller-supplied path | Source scan + `IWorkstationFactReader` methods take zero parameters |
| Verification starts nothing | `.Start(` and `Launch` banned under `Verification\` |
| No process termination, no coordinate automation | `Kill`, `TerminateProcess`, `SetCursorPos`, `mouse_event`, `SendInput`, `keybd_event` banned under `Verification\` |
| No adapter can self-authorise Production | No file under `Adapters\` names the verifier |
| `FoundationEnvironmentGate` unchanged | One declared method, one parameterless constructor, zero fields |
| ViewModels contain no `System.IO` and no verification reference | Source scan |

The pre-existing rule that every P/Invoke lives in `Automation\NativeMethods.cs` is respected: the
new entry points were added there, in their own documented group.

## 15. Targeted tests

Per §1 the complete suite was **not** run. This slice changed no core Workflow state, no
persistence or migration, no adapter execution semantics and no shared cross-project
architecture, so the broader run is not owed here.

| Filter | Tests | Result |
| --- | --- | --- |
| `ProductionWorkstationVerifierTests` + `WorkstationVerificationBoundaryTests` | 67 | Pass |
| `Integration.Preset`, `DependencyRuleTests`, `BannedApiEnforcementTests`, `AutomationBoundaryTests`, `ScopeGuardTests`, `ForceTerminationPolicyBoundaryTests` | 156 | Pass |
| `EnvironmentGateTests`, `ProductionAdapterGateTests` | 4 | Pass |
| **Total targeted** | **227** | **Pass** |
| `WorkstationVerificationSmoke` (opt-in live observation) | 1 | Pass |

The §22 matrix is covered: exact manifest passes; hash mismatch fails before requirements are
trusted; evidence mismatch fails; missing evidence fails; expected workspace root passes; wrong
root fails; a root that is a file fails distinctly; accepted OS passes; unsupported build fails;
accepted display passes; scaling, topology and work-area mismatches each fail; interactive session
passes; five unsupported session/desktop shapes fail; Meitu exact passes and wrong-hash/absent
fail; Photoshop exact passes and wrong-hash fails; `.atn` exact passes and wrong-hash fails;
read-only absence is advisory; the verifier launches no external app; dynamic checks are
re-evaluated; the gate still refuses Production; `Adapters.Mode` is still `Fake`.

The synthetic fixture describes a machine that exists nowhere — "Windows Test Edition 99.0.1234",
1280x800 at 120 DPI, `fr-FR`. A verifier passing against it cannot be carrying a hidden copy of
the real workstation's values.

## 16. Live workstation result

Run read-only against the current accepted workstation. Production was not enabled; nothing was
launched, sent, written or repaired.

| Check | Kind | Outcome |
| --- | --- | --- |
| `PresetIntegrity` | Immutable | **Passed** — `printflow-workstation-v1 1.15.0 (3392873ED0CA)` |
| `EvidenceIntegrity` | Immutable | **Passed** — 27/27 entries verified |
| `FilesystemReadOnlyPolicyAdvisory` | Immutable | *Advisory* — 16/27 not marked read-only |
| `OperatingSystem` | Immutable | **Passed** — Windows 10 Pro 10.0.19045 (build 19045, 64-bit) |
| `MeituExecutable` | Immutable | **Passed** — `…\XiuXiu\7.8.7.5\XiuXiu.exe` (`D65C6D823232`) |
| `PhotoshopExecutable` | Immutable | **Passed** — `D:\Adobe Photoshop CC 2019\Photoshop.exe` (`81EE8930FC1E`) |
| `PhotoshopActionArtifact` | Immutable | **Passed** — `PrintFlow DTF`, 1636 bytes (`A04203EDEA62`) |
| `WorkspaceRoot` | Dynamic | **Passed** — `D:\PrintFlowStudio` |
| `InteractiveSession` | Dynamic | **Passed** — session 1 `Console`, desktop `Default` |
| `DisplayConfiguration` | Dynamic | **Passed** — 1 display, 1920x1080, work area 1920x1040, 96 DPI (100%) |
| `UiCulture` | Dynamic | **Passed** — user UI language `zh-CN` (system `en-US`) |
| `ExternalApplicationUiLanguage` | Dynamic | *Advisory* — Meitu `zh-CN`, Photoshop `Simplified Chinese` |

**Overall verification candidate: verified — 10/10 non-advisory checks passed**, observed
`2026-09-01 03:07:18Z`.

No drift was found and nothing was repaired. Confirmed after the run: the Photoshop and XiuXiu
processes carry the same PIDs and start times as before it — the verifier started nothing.

## 17. Remaining EnvironmentGate-integration scope

Not done in Part A, by design:

1. Replace or wrap `FoundationEnvironmentGate` with a gate that consults
   `IProductionWorkstationVerifier` and re-evaluates the dynamic checks per call.
2. Decide the gate's re-evaluation policy for dynamic checks (per `Verify`, or per production
   step) and the freshness window `ObservedAt` is measured against.
3. Register the verifier in the composition root and give it the configured workspace root and
   preset identity. **Nothing is registered today** — no DI entry, no consumer.
4. Map failed checks to operator-facing localised message keys, in both en-US and zh-CN.
5. Decide the operator surface (§21): a compact "Workstation verification — Verified / Not
   verified" read model. Part A adds no UI and no `Enable Production` control.
6. Decide whether the two advisories are surfaced to the operator or stay in the local log.
7. Only then, and separately, change `Adapters.Mode`.

Contract gaps found: **none blocking**. The only preset-hygiene note is the `.atn` filename case
difference in §11, which does not affect verification on NTFS. No preset or evidence file was
edited, and no new preset version was minted.

## 18. Git state

Branch `master`, 38 commits ahead of `origin/master`, no push, no amend, no history rewrite.
Preflight tree was clean with the A5 correction commits (`7f37986`, `85a9914`) present.

Added:

- `src/PrintFlow.Infrastructure/Verification/` — vocabulary, result model, fact seams,
  requirements model, preset requirements reader, verifier, Win32 fact reader, filesystem
  artefact reader
- `tests/PrintFlow.Tests/Fixtures/WorkstationVerificationFixture.cs`
- `tests/PrintFlow.Tests/Integration/Verification/ProductionWorkstationVerifierTests.cs`
- `tests/PrintFlow.Tests/Architecture/WorkstationVerificationBoundaryTests.cs`
- `tests/PrintFlow.Tests/Smoke/WorkstationVerificationSmoke.cs`

Modified:

- `src/PrintFlow.Infrastructure/Automation/NativeMethods.cs` — new verification entry points only

Unchanged: `appsettings.json` (`Adapters.Mode = Fake`, preset `1.15.0`),
`FoundationEnvironmentGate.cs`, the composition root, every adapter, all preset and evidence
files, and all package/project/lock files.

No dependency changed, so no vulnerability audit was run (§26). Nothing committed contains a
machine snapshot with personal data, a screenshot, the runtime database, a generated image or
TIFF, an external preset or evidence file, or a registry dump.

Build gate: `dotnet build` — **0 warnings, 0 errors**.

---

## Notes on the "with notes" verdict

1. **Read-only attribute drift.** 16 of 27 integrity-referenced evidence files no longer carry
   the read-only attribute. Reported as a non-blocking advisory per §15; every digest matched;
   no attribute was changed. It is worth a deliberate re-application outside this slice.
2. **Preset `.atn` filename case.** The preset spells `PrintFlow-DTF-v1.atn`; the file is
   `Printflow-DTF-v1.atn`. Resolves and hashes exactly on NTFS. A preset-hygiene item for a
   future version, deliberately not fixed here.
3. **Photoshop UI language is not statically verifiable.** Reported as an advisory rather than
   invented as a check, because reading it requires starting Photoshop (§17). Covered at
   operation time by the Epic 11400 guard.
4. **Preflight synthetic document.** A retained synthetic Photoshop document
   (`Feinando Sefo_A4.tif @ 16.7% (图层 1, W1/16)`) was open at preflight and was **not** closed —
   the task requires an operator to dismiss it manually with *Don't Save* and forbids automating
   the dialog. It has no bearing on this slice: static verification touches no window and starts
   no process, and the live run confirmed the Photoshop PID and start time were unchanged. The
   dialog remains outstanding for the operator.
