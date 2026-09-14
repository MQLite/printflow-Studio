# PF-CHECK-MEITU-V2 — Two-case live compatibility check

## Outcome

**DIAGNOSTIC COMPLETE / FAILED — both selected cases stopped at guarded Meitu boundaries**

One new bound partial run was executed against `D:\PrintFlowStudio\TestData\v2` with the retained
pair `a4237f41-17d2-47f8-affd-8d829d98fd5f` and the explicitly authorized Meitu 7.8.8.2 exception.
The initial live readiness passed, but `NORMAL_JPG_PORTRAIT` failed before Save because the verified
editor did not match the signed loaded-document structure. The independent
`COMPLEX_BACKGROUND_FINE_HAIR` session then reached its recorded background-removal authorization,
but the operation-time environment gate refused before a background-removal attempt was created
because Meitu readiness had been invalidated.

This is compatibility evidence only. It is not version acceptance, artwork approval, Production
authorization or permission to remove `CandidateProblems`. The result retains the publication
prohibition for the executable override. The completed failed run must not be resumed or retried.

## Freeze, preflight and invocation

- Canonical checkout began clean on `master` at
  `4b44b73c7c8f47b1863fb1fcbbdd72d06b1bafbd`; no reset or portrait-contract change occurred.
- Pair receipt verification passed without a rebuild. Its source is
  `df162aae64dc8a18a7e671a27f11c20fea8419e2`, input digest is
  `B40780B6BEF7967F49CE03A71C2650A754D0FCD7ACF12F46B616EBDA26F60621`, and receipt SHA-256 is
  `BC4D3084E4CBB9440778E06940634A761B418AA9E009EFD7C5171D3682E317C4`.
- Static v2 preflight passed: all seven categories were present and every manifest and file hash
  matched. The selected set content digest is
  `21AF322BE9172B406DD1B12716C2CBC8977A6B057D63A39571A446ECE86232F0`.
- The supported wrapper exposes `Categories`, `BuildPairReceipt`, `CandidateInstallFolder` and
  `MeituExecutablePath` together. The retained harness contains the category-filter runner and the
  override implementation. The supplied filter was
  `NORMAL_JPG_PORTRAIT,COMPLEX_BACKGROUND_FINE_HAIR`; the harness preserved canonical order and
  created separate sessions.
- One immediate pre-run live-window confirmation found Meitu 7.8.8.2 running with its clean startup
  window plus the retained image-editor window, and Photoshop CC 2019 open. No novice notice was
  present. No notice, setting, document or editor control was clicked or changed.
- RunId: `pf-check-meitu-v2-20260914-163508`.
- InvocationId: `6aa9f43e-c227-4041-bffd-2e0a84eaf5be`.
- Wrapper interval: 14 September 2026, 16:35:16–16:35:44 NZST.
- Wrapper exit: `1`; paired vstest host exit: `0`; its one Fact completed and wrote a coherent
  `Failed` result. A passing test host is not a passing compatibility result.
- Binding records Meitu PID 16016, Photoshop PID 25696, Meitu SHA-256
  `9276B407F855A02F65B04FF0EBEAC1E778F6C8F24D25413A1C2D2853A3F96B0B`, pair receipt identity and
  the unchanged executable-override `CandidateProblems` publication prohibition.

Initial `readiness.json` is verified. All blocking checks passed, including the real shared
`ExternalApplicationAutomationLock`, `MeituSafeStartingState = KnownEditorEmpty`, Photoshop
KnownStartScreen/no document, colour settings and the Photoshop round trip. Probe
`1f41b07cf974434cb4a7d554a92f077a` reached `CleanupCompleted`; cleanup succeeded with no primary or
secondary failure.

## Case evidence

### NORMAL_JPG_PORTRAIT

- Independent session: `01a09e32-e603-7232-b0bf-fc89fabb9a3e`.
- Persisted progress: Import and OriginalConfirmation approved; Enhancement attempt
  `01a09e32-e7c5-7746-900c-ff7c1a7b8512` failed with `MeituUnknownState`.
- Actual managed pre-Enhancement input: **1200 × 1600** pixels, JPEG, SHA-256
  `F4CAD2A1EC7994E42E2A77CC6F30E29DE91D344821E4EE7AC01C7E712D9A4634`.
- Actual Enhancement output: **not produced; dimensions missing × missing**. There is no Enhance
  Revision, no output Revision ID and no copied case artefact. The expected output path was
  established, but Save was not invoked and that path does not exist. The attempt-owned Working
  folder retains only the byte-identical staged JPEG input.
- The v2 named assertion is `enhancedOutputIsNotSmallerThanSource`. It was **not reached and not
  serialized** because the processor failed before returning an Enhancement Revision whose decoded
  dimensions could be evaluated. The case result contains only `caseCompleted = false`; it would be
  false evidence to report the size assertion as Passed or Failed for this run.
- Failure evidence:
  `D:\PrintFlowStudio\Evidence\20260914T043542Z_open-unconfirmed_131090.png`, SHA-256
  `495E517554B2F31A23C727C662C52BC5386C75266A3285727E91E8D25FDFF207`.

### COMPLEX_BACKGROUND_FINE_HAIR

- Independent session: `01a09e33-1475-7eb1-ab2f-57bd36e5f59f`.
- Persisted progress, despite the exception result's intentionally empty `StepsExecuted`: Import
  and OriginalConfirmation approved; Enhancement skipped; the background-removal authority was
  recorded against imported Revision `01a09e33-14a4-7b1b-a522-fe064523b101` and source SHA-256
  `5A705FE390AF87D1D48A0554D4908C425D4703A8807CA78EC73AC0E55E3C8D8E`.
- **Last evidenced stage:** background-removal decision/authority recorded. The BackgroundRemoval
  step remained `WAITING` with `AttemptCount = 0`.
- Actual failure: the operation-time Production gate rechecked at 04:35:43Z and reported 15/17
  blocking checks passed. `MeituLaunchability` failed because Meitu was no longer on a positively
  recognised screen, and `MeituSafeStartingState` was blocked because the certified process was no
  longer ready.
- Actual cleanup/output facts: no BackgroundRemoval attempt row, adapter invocation, Working
  attempt folder, output Revision, cutout file or case artefact exists. Therefore no cutout
  processor cleanup ran; there was no attempt-owned cutout output to clean. The managed source copy
  remains as session evidence.

The canonical shared lease row was passively checked after the wrapper returned and is free:
`OwnerToken`, process identity and acquisition time are all null. Meitu and Photoshop processes
remain running; no process-tree or application cleanup was attempted. Read-only SQLite evidence
inspection created an empty `regression-run.db-wal` and its standard `regression-run.db-shm`
sidecar; no evidence row was edited or deleted.

## Result integrity and boundaries

- Result SHA-256: `355F32EAF77D7F8614DB13FC72B2B4043F30D4D5D33C1084BE53A6FA784E8126`.
- Portrait case SHA-256: `7EA3F6D02BC7AD23A8DBFBBDBEEB943FD3D4D087163F57EB6445B4D466C0D075`.
- Fine-hair case SHA-256: `2F0D7CF5601636CF2D28C9D40E82B7F4E5B59E4077C85ECB459ADDAACE2398C5`.
- Initial readiness SHA-256: `371E07ADCD5576944A867CC096F47E04626501F49858038164CDF0EB880D11A9`.
- Live log:
  `artifacts/pf-check-meitu-v2/pf-check-meitu-v2-20260914-163508-live-run.log`, SHA-256
  `1D11BD1348735E0200AE0DF9389FC708C3FB9819A71A1ED13D817C2CC39107AB`.
- The five unselected categories were not run. No full seven-category run, full-suite rerun,
  Production revalidation, normal-App E2E, Jira change, preset/asset/assertion/permission/product
  edit, install, deploy, publish or push occurred. Historical runs, pairs and the operator prompt
  bundle remain preserved.

## One next action

**Operator: return Meitu 7.8.8.2 to its positively recognised clean start page.** The failed
portrait evidence shows the editor remained in an unrecognised loaded-document state and the next
case's gate explicitly requires this correction. Any later compatibility attempt must be a newly
authorized fresh run with a new RunId; this completed run cannot resume.

## Routing and continuation

Policy v2.3, `EXECUTE_HANDOFF`, `CONTINUE`, explicit RouteOffset `-1`. NormalRoute for bounded live
external-application/recovery evidence was `gpt-5.6-sol/high`; the offset requested
`gpt-5.6-sol/medium`, but the guarded live-operation and cleanup-invalidation risk floor retained
the High target: `ROUTE_OFFSET_CLAMPED_BY_RISK`. No real in-thread model switch or execution
metadata was available: `MODEL_SWITCH_UNAVAILABLE`, ActualRoute `UNVERIFIED`. Self-review only.

