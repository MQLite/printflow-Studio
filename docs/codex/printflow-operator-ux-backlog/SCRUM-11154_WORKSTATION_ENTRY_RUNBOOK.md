# SCRUM-11154 workstation entry runbook

**2026-10-01 findings remediation (F-V7), supersedes the "do not create a new folder" note below:** a folder created inside a folder/save dialog is now refused as no selection with a Chinese SYNTHETIC notice; the host keeps running and nothing is adopted. Choose the prepared `delivery` or `diagnostics\export` folder itself. Integrity faults (reparse/alias paths inside a role, tampered markers, ownership loss) still refuse the run. **Test-only format change:** `state/prepared.json` now carries `ScenarioLedgerSha256`, the SHA-256 of the exact `evidence/scenario-ledger.json` bytes the preparation wrote; Interactive validates those bytes before admitting any fixture. Every root prepared before this change (including both visible roots and `v-20260930T224610Z`) is refused for Interactive and is never upgraded; prepare a fresh root. **Exit codes:** 0 ok, 2 refused, 3 partial, 4 nonquiescent, **5 host fault**. For the tested injected dispatcher fault, `evidence/host-fault-*.json` and quiescence record the fault, the run was cancelled and torn down, and no prepared record was written. A startup fault before the protected try may exit 5 without these records; a fault after preparation may leave a prepared record; exit 4 can supersede a fault if teardown does not settle. See the findings review for those residuals. Only a PrepareAndSmoke **exit code 0** qualifies a root for a later Interactive session; the presence of `prepared.json` alone does not. `-InjectHostFault` exists only to prove exit 5 on its own disposable PrepareAndSmoke root; such a root is never used for Interactive.

**2026-10-01 update:** Interactive was run twice under fresh owner acknowledgments; see [SCRUM-11154_WORKSTATION_OBSERVATIONS.md](SCRUM-11154_WORKSTATION_OBSERVATIONS.md). Since code `20747aa`, Interactive admits the fixtures recorded in its own scenario ledger and returns an out-of-role or unadmitted native selection as no selection. In folder/save dialogs, do not create a new folder: that selection still ends the host. The command form below is unchanged; prepare a fresh root for every visible session.

**Status at implementation: noninteractive preparation, runtime boundaries and owned restart verified; final independent conformance review PASS. Interactive was then NOT RUN.** The approved design/review remain unchanged. This is a test-only synthetic entry, with explicit isolated resources and Fake processors. It is not production or operator acceptance.

## Permitted build and static checks

From the repository root in PowerShell:

```powershell
dotnet build tests/PrintFlow.Tests/PrintFlow.Tests.csproj --artifacts-path artifacts/pf-opux-scrum11154-workstation-entry/build --nologo
```

Only after exit code zero, run static refusals (no provider/DB/schema/WPF graph):

```powershell
dotnet test tests/PrintFlow.Tests/PrintFlow.Tests.csproj --no-build --artifacts-path artifacts/pf-opux-scrum11154-workstation-entry/build --filter 'FullyQualifiedName~PrintFlow.Tests.Unit.WorkstationEntry&WorkstationEntryPhase!=Runtime&WorkstationEntryPhase!=Prerequisite' --logger 'trx;LogFileName=static-refusals.trx' --results-directory artifacts/pf-opux-scrum11154-workstation-entry --nologo
```

The reviewer separately authorized the bounded prerequisite class. It creates fresh roots below the task's approved `runs` directory, uses a real instance guard and tiny SQLite fixture table, and retains evidence. Its integrated reparse test uses a successful empty control and verifies that marker-protected owned directories refuse the same mutation:

```powershell
dotnet test tests/PrintFlow.Tests/PrintFlow.Tests.csproj --no-build --artifacts-path artifacts/pf-opux-scrum11154-workstation-entry/build --filter 'FullyQualifiedName~PrintFlow.Tests.Unit.WorkstationEntry.EntryOwnershipPrerequisiteTests' --logger 'trx;LogFileName=ownership-prerequisite.trx' --results-directory artifacts/pf-opux-scrum11154-workstation-entry --nologo
```

Do not broaden either filter to all WorkstationEntry tests: runtime-tagged lease/diagnostic fixtures require a released graph and explicit verified run authority.

## Candidate and Validate

`tools/New-WorkstationEntryCandidate.ps1` takes explicit `RepositoryRoot`, `BuildDirectory`, new `ManifestDirectory`, `RunId`, and successful `BuildEvidence`. BuildDirectory is the isolated `build/bin/PrintFlow.WorkstationEntry/debug` output. It writes candidate/scenario/provenance metadata without constructing an App or creating R. Manifest generation detects source changes while hashing; final generation must wait for source edits to settle.

The launcher requires explicit `Mode`, `Root`, `CandidateManifest`, `ScenarioManifest`, and `HostAssembly`. Validate checks source/artifact identities and planned ownership without creating R, opening a DB, constructing a provider or using WPF. A valid manifest does not establish runtime isolation. Any source/binary change requires a fresh successful build and new manifests; old retained roots must never be adopted for a new candidate.

## Noninteractive execution

Set explicit values returned by the candidate generator, with R a new direct child of the approved runs directory. Use a short RunId to leave room for the real workspace's 240-character path guard. The source and output bytes must remain frozen through host/test exit.

```powershell
$entryHost = Join-Path $PWD 'artifacts/pf-opux-scrum11154-workstation-entry/build/bin/PrintFlow.WorkstationEntry/debug/PrintFlow.WorkstationEntry.dll'
# Supply these exact generated values; do not reuse historical run roots.
$entryRoot = '<new approved R>'
$entryCandidate = '<candidate-manifest.json>'
$entryScenario = '<scenario-manifest.json>'
& ./tools/Start-WorkstationEntry.ps1 -Mode Validate -Root $entryRoot -CandidateManifest $entryCandidate -ScenarioManifest $entryScenario -HostAssembly $entryHost
# Only after validation and the required independent release for this implementation:
& ./tools/Start-WorkstationEntry.ps1 -Mode PrepareAndSmoke -Root $entryRoot -CandidateManifest $entryCandidate -ScenarioManifest $entryScenario -HostAssembly $entryHost
```

Normal successful PrepareAndSmoke writes `state/prepared.json` only after scenario/navigation success, closed task registration, owned-task quiescence, WPF/provider disposal, exact app/lease pool cleanup and final held database identity checks. It binds candidate hash, scenario hash, run token and current owner token, uses CreateNew and flushes under ownership. It returns zero only after that work completes. Failure returns nonzero; nonquiescence in the dedicated host records incomplete evidence and self-exits code 4 rather than releasing ownership around live work. No external PASS file or acknowledgment overrides these checks. Historical graph-only builds intentionally exited 3 and created no prepared record.

Prepared describes this run's synthetic preparation, not a repeated seven-test suite or deliberate process interruption. Those are implementation verification evidence. Resume renews ownership and invalidates any earlier prepared owner token. Missing, replaced, reparse or hardlinked lock/DB/sidecar/marker data refuses before dependent operations; never repair such a retained root by recreating files.

## Separate runtime boundary and owned-restart verification

The seven runtime tests use one nonparallel collection. After the graph process exits, verify all six recorded app/lease database identities before running them. Set process-local `PRINTFLOW_ENTRY_RUN_ROOT`, `PRINTFLOW_ENTRY_RUN_TOKEN`, `PRINTFLOW_ENTRY_OWNER_TOKEN` from the current ownership record, exact `PRINTFLOW_ENTRY_CANDIDATE_HASH`, `PRINTFLOW_ENTRY_SCENARIO_HASH`, `PRINTFLOW_ENTRY_CANDIDATE_MANIFEST`, `PRINTFLOW_ENTRY_SCENARIO_MANIFEST`, and the SHA256 of the built tests DLL in `PRINTFLOW_ENTRY_TEST_ASSEMBLY_SHA256`. The fixture validates source/manifests and run authority before the exclusive claim. The claim and owner renewal precede reacquiring mutable DB/sidecar holds; these holds and identity checks complete before constructing the fixture graph or opening its databases for writes.

Capture the prepared record's binding immediately after host exit, before those tests. Runtime7 renews owner authority and leaves deliberate negative link fixtures; this consumes the test root for verification. Its historical prepared marker is evidence at the captured exit, not usable later Interactive authorization. Never rewrite that marker or remove negative fixtures to make the consumed root usable. Any separately authorized future visible work needs its own fresh candidate/root and successful preparation.

```powershell
dotnet test tests/PrintFlow.Tests/PrintFlow.Tests.csproj --no-build --artifacts-path artifacts/pf-opux-scrum11154-workstation-entry/build --filter 'FullyQualifiedName~PrintFlow.Tests.Unit.WorkstationEntry&WorkstationEntryPhase=Runtime' --logger 'trx;LogFileName=runtime-boundaries.trx' --results-directory artifacts/pf-opux-scrum11154-workstation-entry --nologo
```

The owned restart requires its own fresh R and generated scenario manifest. With explicit review release, add `-OwnedRestart` to PrepareAndSmoke. The supervisor starts only its dedicated hidden/nonshown child, verifies exact PID/start/control token/root identity/candidate/scenario/held boundary record, and interrupts that captured process only. It then starts a tracked recovery child against the same R and verifies the exact real interrupted attempt plus Recovery.Open→Session→Details→Session→Home. No process scan, name kill or tree kill is allowed. Missing authority never grants termination. Both children and redirected pipes remain owned until they exit. This is synthetic process-interruption evidence, not power loss.

## Evidence and future visible mode

Final completed pair: candidate `51123588D1C3F5790ED55119FD79106F223725C913D60981C5B0818D149EFD15`, graph `p-20260930T054646Z`, restart `c-20260930T054646Z`. `prepared-final-build-01.log` passed with zero warnings/errors; `prepared-final-static.trx` passed 46/46; prior `architecture-entry-static.trx` passed 514/514 (474 architecture plus then-current 40 entry tests); `runtime-boundaries-final.trx` passed 7/7. Both `prepared-20260930T054646Z.log` and `restart-final-20260930T054646Z.log` exited zero. Prepared binding, six database identities and recovery-to-Home were captured in separate readback JSON records. The graph test root was consumed by the runtime tests and is not reusable for Interactive. See the implementation record for full W01–W10 evidence and limitations. All evidence is below `artifacts/pf-opux-scrum11154-workstation-entry`; retain original failures and roots. Never inspect or control unrelated live processes.

Future Interactive is a distinct mode requiring matching prepared-run identity, inactive exact prior owner, verified resume ownership and a fresh `SafeDesktopConfirmed` acknowledgment. No acknowledgment is persisted. It was not invoked or authorized by this noninteractive verification. A successful prepared record does not authorize an agent to show windows or interact with the desktop.

**FUTURE ONLY — NOT RUN.** After separate owner authorization for visible work, build and prepare a separate fresh candidate/root, leave that prepared root unconsumed by runtime tests, and obtain a new manual acknowledgment that the desktop is safe immediately before invocation. Do not use any graph/restart roots or variables from the verification runs above. The following is a future command template only; its acknowledgment switch must never be added automatically or carried over from a previous session. Interactive performs its own verified resume claim, so the separate `-Resume` switch is unnecessary.

```powershell
$futureVisibleHost = '<absolute successfully built host assembly for the fresh candidate>'
$futureVisibleRoot = '<absolute separately authorized, freshly prepared, unconsumed root>'
$futureVisibleCandidate = '<absolute matching fresh candidate-manifest.json>'
$futureVisibleScenario = '<absolute matching fresh scenario-manifest.json>'
# FUTURE ONLY: run manually only after separate authorization and a fresh safe-desktop acknowledgment.
& ./tools/Start-WorkstationEntry.ps1 -Mode Interactive -HostAssembly $futureVisibleHost -Root $futureVisibleRoot -CandidateManifest $futureVisibleCandidate -ScenarioManifest $futureVisibleScenario -SafeDesktopConfirmed
```

Physical keyboard use, screenshots, whole-journey/wording acceptance, colleague work, live automation and print quality remain separate OPEN/NOT RUN work. No production resources, installed application, normal App startup or production composition are part of this entry.

## Historical exact command record — NOT FOR REPLAY

These are the actual final candidate paths and invocations, recorded for reproducibility and review only. The roots already exist and retain verification fixtures; do not rerun these commands against them. Working directory was `D:\Repositories\printflow-Studio`.

```powershell
$testedHost = 'D:\Repositories\printflow-Studio\artifacts\pf-opux-scrum11154-workstation-entry\build\bin\PrintFlow.WorkstationEntry\debug\PrintFlow.WorkstationEntry.dll'
$testedGraphRoot = 'D:\Repositories\printflow-Studio\artifacts\pf-opux-scrum11154-workstation-entry\runs\p-20260930T054646Z'
$testedGraphCandidate = 'D:\Repositories\printflow-Studio\artifacts\pf-opux-scrum11154-workstation-entry\candidate-final-20260930T054646Z-graph\candidate-manifest.json'
$testedGraphScenario = 'D:\Repositories\printflow-Studio\artifacts\pf-opux-scrum11154-workstation-entry\candidate-final-20260930T054646Z-graph\scenario-manifest.json'
& .\tools\Start-WorkstationEntry.ps1 -Mode Validate -Root $testedGraphRoot -CandidateManifest $testedGraphCandidate -ScenarioManifest $testedGraphScenario -HostAssembly $testedHost
& .\tools\Start-WorkstationEntry.ps1 -Mode PrepareAndSmoke -Root $testedGraphRoot -CandidateManifest $testedGraphCandidate -ScenarioManifest $testedGraphScenario -HostAssembly $testedHost *> artifacts/pf-opux-scrum11154-workstation-entry/prepared-20260930T054646Z.log

$testedRestartRoot = 'D:\Repositories\printflow-Studio\artifacts\pf-opux-scrum11154-workstation-entry\runs\c-20260930T054646Z'
$testedRestartCandidate = 'D:\Repositories\printflow-Studio\artifacts\pf-opux-scrum11154-workstation-entry\candidate-final-20260930T054646Z-restart\candidate-manifest.json'
$testedRestartScenario = 'D:\Repositories\printflow-Studio\artifacts\pf-opux-scrum11154-workstation-entry\candidate-final-20260930T054646Z-restart\scenario-manifest.json'
& .\tools\Start-WorkstationEntry.ps1 -Mode Validate -Root $testedRestartRoot -CandidateManifest $testedRestartCandidate -ScenarioManifest $testedRestartScenario -HostAssembly $testedHost
& .\tools\Start-WorkstationEntry.ps1 -Mode PrepareAndSmoke -OwnedRestart -Root $testedRestartRoot -CandidateManifest $testedRestartCandidate -ScenarioManifest $testedRestartScenario -HostAssembly $testedHost *> artifacts/pf-opux-scrum11154-workstation-entry/restart-final-20260930T054646Z.log

# Executed after graph host exit and six-leaf identity verification, before the separate restart:
# Exact process-local environment bindings are retained in runtime-final-binding.json.
dotnet test tests/PrintFlow.Tests/PrintFlow.Tests.csproj --no-build --artifacts-path artifacts/pf-opux-scrum11154-workstation-entry/build --filter 'FullyQualifiedName~PrintFlow.Tests.Unit.WorkstationEntry&WorkstationEntryPhase=Runtime' --logger 'trx;LogFileName=runtime-boundaries-final.trx' --results-directory artifacts/pf-opux-scrum11154-workstation-entry --nologo *> artifacts/pf-opux-scrum11154-workstation-entry/runtime-boundaries-final.log
```

All four launcher invocations above exited zero. The runtime command passed 7/7 with no skips. The command listing groups launcher modes for readability; actual runtime order was Prepare → prepared binding/six-leaf readback → runtime7 → separate owned restart. Binding evidence beneath the task artifacts: `prepared-binding-20260930T054646Z.json`, `state-readback-20260930T054646Z.json`, `runtime-final-binding.json`, `prepared-consumed-20260930T054646Z.json`, and `restart-final-readback.json`. These records preserve the pre-fixture owner/marker, exact test DLL SHA256/MVID, intentional owner renewal, and final recovery/quiescence results.
