# Local Prompt-image backend implementation

Scope: authorized local T03–T06, isolated `codex/prompt-work-images`, starting HEAD `70fa317`. Parent owns commits, Jira, final regression and fresh review. No real Edge/Work acceptance is claimed; T02 remains blocked and T06 clean-user registration/Hello/rollback is NOT RUN.

Routing: inherited offset 0. Normal/RequestedRoute Sol generation 6.0 High for state, persistence, files and protocol; T04 normally Medium remains in the tightly coupled High unit. ExecutionTarget `gpt-6.1-sol/high`, disclosed same-family generation-6 substitute after repeated 6.0 capacity failures: `REQUESTED_GENERATION_UNAVAILABLE`. ActualRoute **VERIFIED gpt-6.1-sol/high** from own exact session `01a10955-fd55-7190-9bc3-43eb625cf79e`, turn_context `2026-10-04T23:53:14.261Z`. Dispatch alone was not used as proof.

Ruling: commits, final whole-group regression and independent review remain parent-owned per authorized handoff; no per-task commits or full 13k run here. Cost if wrong: parent must perform those verification/integration steps before closing tasks.

Evidence will be recorded below. Raw logs/TRX use ignored `artifacts/test-results/prompt-work-images-local/`.

## T03 evidence (local acceptance verified)

All `dotnet test` commands use `tests/PrintFlow.Tests/PrintFlow.Tests.csproj -c Release --no-restore`, `--results-directory artifacts/test-results/prompt-work-images-local`, and `--logger trx;LogFileName=<file>.trx`.

| Evidence | Filter / command | Exit | Result |
| --- | --- | --- | --- |
| `t03-red` | `FullyQualifiedName~AiDesignTransitionsTests\|FullyQualifiedName~AiDesignRequestStoreTests` | 1 | 0 passed / 9 failed; unavailable transitions/store (two initial compile-only attempts fixed before this behavioral RED) |
| `t03-coordinator-red` | `FullyQualifiedName~AiDesignCoordinatorTests` | 1 | 0 passed / 6 failed; unavailable coordinator |
| `t03-recovery-red` | `FullyQualifiedName~Retry_revokes_old_candidate` | 1 | retained prior candidate wrongly authorized on retry |
| `t03-process-red` | `FullyQualifiedName~AiDesignProcessTests` | 1 | 2 passed / 1 failed; actual two-process CAS and submit-crash passed, unavailable installation lease failed |
| `t03-green` | combined four AI classes above | 0 | 19 passed / 0 failed / 0 skipped |
| `t03-final` | combined four AI classes + ScopeGuard/InstallerBoundary/BannedApiEnforcement | 1 | 52 passed / 1 failed; newly added atomic replacement stress exposed concurrent unlocked read failure |
| `probe-restore` | `dotnet restore tests/PrintFlow.AiDesignProbe/PrintFlow.AiDesignProbe.csproj` | 0 | initial new project lock generated; no runtime packages added |
| `t03-locked-restore` | `dotnet restore tests/PrintFlow.Tests/PrintFlow.Tests.csproj --locked-mode` | 1 | expected project-reference metadata mismatch before adding probe to existing lock |
| `t03-project-metadata-restore` | same restore without locked flag | 0 | only new Project dependency added to existing test lock; package versions unchanged |
| `t03-locked-restore-final` | same restore with `--locked-mode` | 0 | locked graph restored after metadata update |
| `t03-final-green` | four AI classes + DependencyRule/ScopeGuard/InstallerBoundary/BannedApiEnforcement | 0 | 62 passed = 20 AI behaviors + 42 existing boundary cases; 0 failed/skipped |
| `t03-review-red` | `FullyQualifiedName~Bridge_exceptions_after_queue\|FullyQualifiedName~Throwing_remote_stop\|FullyQualifiedName~Structural_corruption\|FullyQualifiedName~Submit_callback_requires` | 1 | 3 failed / 1 passed; bridge cancellation, stop throw and nested corrupt JSON reproduced; durable all-upload callback already passed |
| `t03-hung-stop-red` | `FullyQualifiedName~Hung_remote_stop` | 1 | 0 passed / 1 failed; unbounded remote stop reproduced |
| `t03-review-green` | four AI classes + DependencyRule/ScopeGuard/InstallerBoundary/BannedApiEnforcement | 0 | **67 passed = 25 AI behaviors + 42 existing boundary cases; 0 failed/skipped** |

Fresh independent T03 scope review `/root/backend_review` PASS after both P2 findings were fixed with RED/GREEN evidence. Parent owns final integration/Jira status. No full-group regression or browser result is inferred from these focused results.

| Original T03 AC | Local evidence / result |
| --- | --- |
| AC1 all required uploads before Submitted | `Requires_every_upload_before_submit`; real-store `Submit_callback_requires_all_durable_upload_receipts_and_runs_once`; no callback for zero/partial receipt sets, exactly one after all durable receipts — PASS |
| AC2 two processes / all fence fields | `Two_process_CAS_preserves_one_winner`, `Concurrent_CAS_has_one_winner_and_rejects_every_stale_identity`; request/attempt/version/token rejection, one committed update — PASS |
| AC3 stop/import unique order, late reports | real-store stop/import race, revoked token, domain cancelled-stage rejection; bounded/throwing remote-stop tests; no default importer success — PASS locally |
| AC4 crash inputs/candidate/session preservation | click-reservation crash, repeated replacement writer crash/read stress, interrupted recovery, ImportFailed same candidate/session, explicit retry candidate revocation/history — PASS locally; future DB association is T09, not claimed |
| AC5 one installation request / complete handoff | process-held installation lease plus different Workspace Roots coordinator rejection; unavailable bridge/manual fallback with no token exposure; bridge failure cleanup — PASS locally |

Ruling: add `WorkspaceIdentity` and `EditDraftAsync` to store port so the workflow checks roots and can snapshot selected attachments while holding the request lock. This is a bounded extension of the frozen CAS contract, avoiding an Infrastructure dependency in Workflow. Cost if wrong: later adapters must implement two additional port members.

Ruling: `UploadStarted` also binds the tab/conversation for an attachment-free Queued request without changing its state; this supplies the missing identity prerequisite for `SubmitOnceAsync` without pre-reserving a click. Cost if wrong: a later adapter must use a dedicated conversation-binding event instead.

Ruling: use an explicit `AiDesignRequestView` in `AiDesignView`, preserving Request projection semantics without exposing live token/fence. Cost if wrong: future WPF consumers must use the view fields rather than full authority.

Candidate retry regression clears active candidate authority, retains its file metadata in `RetainedCandidates`, and keeps Importing failures in ImportFailed regardless of a caller's optional flag. Recovery does not implement the future SQLite association or fake receiver/importer success.

## T04 / T05 local evidence

| Evidence | Filter | Exit | Result |
| --- | --- | --- | --- |
| `t04-red` | `FullyQualifiedName~PromptTemplateTests` | 1 | 0 passed / 7 failed; unavailable template service |
| `t04-green` | same | 0 | 7 passed / 0 failed |
| `t05-red` | `FullyQualifiedName~AiDesignAttachmentTests` | 1 | 19 failed / 5 rejection assertions passed against unavailable snapshots; 24 total |
| `t05-green` | same | 0 | 24 passed / 0 failed |
| `t05-integration-red` | `FullyQualifiedName~Coordinator_snapshots_explicit_selection` | 1 | 0 passed / 1 failed; coordinator had no snapshot integration |
| `t04-t05-green` | `FullyQualifiedName~PromptTemplateTests\|FullyQualifiedName~AiDesignAttachmentTests\|FullyQualifiedName~AiDesignCoordinatorTests` | 0 | **41 passed / 0 failed / 0 skipped**: 7 templates, 25 attachments/integration, 9 coordinator behaviors |

| Original AC | Local evidence / status |
| --- | --- |
| T04 AC1 | all three required fields; Chinese/newlines/quotes/dollar literals preserved and frozen — PASS |
| T04 AC2 | same original filename, distinct stable upload names and purposes, no filesystem paths in generated attachment text — PASS |
| T04 AC3 | missing structured attachment ID rejects Validate and copy, no-attachment templates valid — PASS |
| T04 AC4 | exact edited final text persists and running DraftSaved rejected — PASS |
| T04 AC5 | complete generation/handoff separate text with manual necessary-file list; unconfigured bridge local fallback remains — PASS locally; UI is T10 |
| T05 AC1 | 8/9 count, 20/40 MiB exact and +1, 80 MiB total and +1, eleven supported extensions, empty/unsupported rejection — PASS |
| T05 AC2 | original bytes/hash unchanged; published readonly snapshot seals exact ID/name/hash, tamper rejected; upload order never reused after deleting all prior files — PASS |
| T05 AC3 | saved draft removal must fix structured references; coordinator rejects running attachment changes and tampered retry — PASS |
| T05 AC4 | exact owned path/identity seal; cross-request, absolute/traversal and real Windows junction rejection — PASS |
| T05 AC5 | temporary synthetic files only; raw results ignored; no runtime images/tokens in source — PASS locally |

Ruling: the attachment module persists a monotonic `.attachment-sequence` under its own request-scoped lock, before publishing a snapshot. Deleted and failed selections cannot reuse an order even when the retained list is empty. Cost if wrong: a consumed order may leave harmless gaps, while later modules must retain this high-water file.

Ruling: immutable means an owned readonly copy plus an identity seal and mandatory same-handle length/hash verification, not a promise that a local user cannot edit filesystem bytes. The upload boundary rejects such edits. Cost if wrong: a damaged snapshot requires reselecting the original file.

## T06 local protocol and prepared trial tools

The local native host, persistent managed mailbox, MV3 protocol module and registration/rollback tools are implemented. Default configuration and the extension have **no verified Work page contract**, so automatic generation remains unavailable. `StopAsync` reports unconfirmed failure; a queue write or protocol Ack cannot claim actual page stop. T07 page upload/submit and T08 result/download operations remain explicitly unavailable. The configured download root is validated; no message can request arbitrary path reads, writes or command execution.

The native wire uses UTF-8 JSON and a native-order 32-bit length prefix, with a 1 MiB frame limit and raw attachment chunks no larger than 256 KiB. Host stdout contains frames only; production stderr uses static failure codes. This follows the official [Edge native messaging guide](https://learn.microsoft.com/en-us/microsoft-edge/extensions/developer-guide/native-messaging) and [Chromium native messaging contract](https://developer.chrome.com/docs/extensions/develop/concepts/native-messaging). Native caller origin, host/protocol version, workspace, connection, request, attempt, version and token fences are checked. Stream reads use the same verified owned snapshot handle.

Mailbox publication flushes a private temporary file before atomic publication, rejects same-attempt replay and permits explicit new-attempt replacement. Pump faults terminate the session and interrupt its owned active attempts. Request gates prevent version changes during one attachment; each trusted attachment boundary emits the current Ack before bytes. A duplicate exact-current-fence Ack is idempotent, while changed versions only bind at an empty attachment boundary. Browser-origin stale authority is never refreshed.

All C# tests below use `dotnet test tests/PrintFlow.Tests/PrintFlow.Tests.csproj -c Release --no-restore --filter '<filter>' --logger 'trx;LogFileName=<evidence>.trx' --results-directory artifacts/test-results/prompt-work-images-local`. Output is preserved in the matching `<evidence>.log`. Node commands and static checks also write that ignored directory. Only synthetic temporary files and synthetic authority are used.

| Evidence | Exact filter / command | Exit | Actual result |
| --- | --- | --- | --- |
| `t06-project-metadata-restore` | `dotnet restore tests/PrintFlow.Tests/PrintFlow.Tests.csproj` | 0 | new project-reference lock metadata; no runtime packages added |
| `t06-red` | `FullyQualifiedName~NativeMessageCodecTests\|FullyQualifiedName~NativeHostBoundaryTests` | 1 | 2 failed / 9 rejection assertions passed, 11 total |
| `t06-transfer-red` | `FullyQualifiedName~Full_attachment_frames` | 1 | 1 failed / 0 passed, unavailable transfer |
| `t06-green` | `FullyQualifiedName~NativeMessageCodecTests\|FullyQualifiedName~NativeHostBoundaryTests` | 0 | 12 passed / 0 failed |
| `t06-js-red` / `t06-js-green` | `node --test extensions/printflow-work/tests/protocol.test.mjs` | 1 / 0 | 3 failed + 1 rejection pass, then 4 passed |
| `t06-js-fence-red` / `t06-js-final-green` | same Node command | 1 / 0 | 1 failed + 4 passed, then 5 passed |
| `t06-lifecycle-red` | `FullyQualifiedName~AiDesignBridgeLifecycleTests` | 1 | 3 failed / 0 passed: retry mailbox, reader/write overlap, faulted pump with pending stdin |
| `t06-process-red` | `FullyQualifiedName~NativeHostProcessTransferTests` | 1 | 2 failed / 0 passed, missing probe implementation |
| `t06-version-red` | `FullyQualifiedName~Upload_progress_waits` | 1 | 1 failed / 0 passed, progress changed authority during chunk stream |
| `t06-review-green` | `FullyQualifiedName~NativeMessageCodecTests\|FullyQualifiedName~NativeHostBoundaryTests\|FullyQualifiedName~AiDesignBridgeLifecycleTests\|FullyQualifiedName~NativeHostProcessTransferTests` | 0 | 18 passed / 0 failed |
| `t06-ack-order-red` | `FullyQualifiedName~Upload_progress_waits` | 1 | 1 failed / 0 passed: already-current trusted fence skipped boundary Ack |
| `t06-protocol-final-green` | same four-class filter as `t06-review-green` | 0 | 20 passed / 0 failed, including two actual Program/Pump/Enqueue stdio scenarios |
| `t06-js-duplicate-ack-red` | `node --test extensions/printflow-work/tests/protocol.test.mjs` | 1 | 1 failed / 4 passed: delayed duplicate Ack rejected during stream |
| `t06-worker-red` | `node --test extensions/printflow-work/tests/bridge-client.test.mjs` | 1 | 2 failed / 0 passed, unavailable client |
| `t06-js-worker-final-green` | `node --test extensions/printflow-work/tests/protocol.test.mjs extensions/printflow-work/tests/bridge-client.test.mjs` | 0 | **7 passed / 0 failed / 0 skipped** |
| `t06-registration-red` / `t06-registration-green` | `FullyQualifiedName~NativeRegistrationToolsTests` | 1 / 0 | 1 failed + 1 rejection pass, then 2 passed; ValidateOnly performs no HKCU writes |
| `t06-registration-final-green` | same registration filter after ownership-preserving cleanup refinement | 0 | 2 passed / 0 failed / 0 skipped |
| `t06-tool-parse` | `System.Management.Automation.Language.Parser.ParseFile` for all `build/ai-design/*.ps1` | 0 | 3 files, 0 parse errors |
| `t03-t06-group-green` | group filter below | 1 | compile-only error in new test assertions, fixed before execution; not behavioral RED |
| `t03-t06-group-green-final` | group filter below | 0 | **157 passed / 0 failed / 0 skipped: 80 AI behaviors + 77 existing architecture/lease/import regressions**, 28 seconds |

Exact group filter:

```text
FullyQualifiedName~AiDesign|FullyQualifiedName~NativeMessageCodecTests|FullyQualifiedName~NativeHostBoundaryTests|FullyQualifiedName~NativeHostProcessTransferTests|FullyQualifiedName~NativeRegistrationToolsTests|FullyQualifiedName~ScopeGuardTests|FullyQualifiedName~InstallerBoundaryTests|FullyQualifiedName~BannedApiEnforcementTests|FullyQualifiedName~DependencyRuleTests|FullyQualifiedName~WorkstationAutomationLeaseTests|FullyQualifiedName~ImportCancellationShellBoundaryTests|FullyQualifiedName~ManualResultImportTests
```

`NativeHostProcessTransferTests` distinguishes two direct-session probe cases with hand-built Request frames from two product cases. Product cases run the actual `NativeHost.Program.RunAsync` stdin/stdout loop, session pump, `FileAiDesignChannel.EnqueueAsync` and Node `native-frame-receiver.mjs` using real child process standard pipes. The complete case reconstructs the exact File bytes/name/hash. The stop case changes durable authority after the first chunk, verifies no later frames and rejects the incomplete file. A test-only stdout backpressure wrapper provides that deterministic failure window. These are actual local protocol integration tests with synthetic configuration, **not actual Edge or Work acceptance**, and do not exercise production `Main` loading a real HKCU registration.

| Original T06 AC | Evidence / status |
| --- | --- |
| AC1 protocol errors, origin and old authority | codec C# cases + Node ordering/duplicates/missing/256 KiB/+1/hash/fence cases; live session rejects every stale authority field — PASS locally |
| AC2 complete file / stop interruption | direct session and actual Program/Pump/Enqueue child stdio paths to real Node File assembler; stop cuts further chunks and completion — PASS locally; actual browser transfer NOT RUN |
| AC3 unavailable/version/origin and Prompt-only | structured readiness failures; default/unverified Ready unavailable; MV3 client rejects Work operations even if peer claims available; existing coordinator preserves manual handoff — PASS locally |
| AC4 clean user registration → Hello → rollback | prepared Register/Unregister tools, ValidateOnly tests, minimal nativeMessaging permission; **NOT RUN / BLOCKED by unavailable Edge control**; no current HKCU or extension installation performed |
| AC5 dependency/install/stdout boundaries | latest corrective group includes all 476 architecture cases + existing lease/import/retention regressions; product stdout parsed entirely as official frames; static stderr codes; no new runtime package or installer change — PASS locally; full ordinary rerun pending |

Prepared tool usage is documented in `build/ai-design/README.md`. Registration binds the exact supplied extension origin and existing absolute executable/workspace/download roots, refuses existing bindings, and defaults the Work contract pin to null. Rollback verifies the exact owned record, hashes and registry target, then removes only its matching three registration files and specific HKCU key; it leaves app/request/attachment/download data intact. Idle connection/lease locks are required before changes. No services, startup entries, scheduled tasks, production MSI changes or extension publication are introduced.

Ruling: an empty WorkspaceIdentity is accepted only for the first native Hello bootstrap; Ready returns the configured identity, and every request is then pinned to it. A nonempty mismatched identity is rejected. Cost if wrong: initial extension configuration would need an explicit workspace identity input.

Local T06 implementation is ready for final independent scope review. Parent owns final locked restore/Release build and one complete ordinary regression after source/test activity is idle. Those future checks and real Edge AC4 are not inferred from this focused PASS; T06 whole-task Done and later task gates are not claimed.

## Corrective owned-staging boundary after ordinary regression failure

The parent's first whole-group ordinary Release run **failed** at `PhotoshopFinalReviewBoundaryTests.Production_disposal_and_classified_retention_have_only_their_named_Infrastructure_boundaries`: `FileAiDesignAttachments.cs` contained a direct deletion outside the named disposal boundaries. Parent stopped that run after this clear failure; its exit 1 / interrupted evidence is preserved in `artifacts/test-results/prompt-work-images-local/group-release-ordinary.log`, with **no final TRX or aggregate completion counters**. This was an AI feature integration gap, not a T01 baseline defect. The earlier 157-case focused PASS did not include this guard and is retained as limited historical evidence, not a whole-suite PASS.

The approved bounded architecture correction adds only `AiDesignOwnedStagingFiles.cs` to Infrastructure. No Domain/Workflow/schema/retention service, native declaration or production authority changed. Public creation uses validated roots, nonempty request IDs and closed purposes to derive random request/sequence/queue staging names and their fixed publication destinations. Connection staging is separately bound to the installation directory. Ownership objects have private constructors and are issued only after successful CreateNew and file-identity capture. Attachment IDs and snapshot/seal paths are internally derived; each batch tracks only the files it successfully created. A move retains ownership through batch completion, and commit/publication consumes it. Previous snapshots and removed committed draft attachments never become rollback authority.

An independent reviewer subsequently found a post-verification pathname swap race in the first implementation, despite its 627-case focused PASS. Its verified handle permitted delete sharing, so pathname-based mutations could affect a replacement. That result is retained as **pre-race-fix evidence**, not final review approval. The authorized refinement uses the existing `NativeMethods` declarations and `WindowsDeliveryNative` held operations without altering those files: pin parent ancestry top-down, open the exact captured file with synchronous DELETE/READ_ATTRIBUTES/WRITE_ATTRIBUTES and SHARE_READ, reject reparse/nonregular/shared/replaced identity, and retain that handle through attributes, disposition or relative rename completion. Readonly rollback clears only ReadOnly; zero remaining bits become FILE_ATTRIBUTE_NORMAL. Fixed authority replacement and attachment no-replace publication use pinned RootDirectory plus a derived simple leaf. Ownership/path changes occur immediately after successful synchronous publication, before any cancellation check.

Cleanup has no pathname fallback, enumeration or directory deletion. Changed/replaced/shared files are preserved, refusal cannot mask the original caller failure, and empty generated directories/crash leftovers remain. The architecture guard now includes NativeHost, retains all existing pathname bans, and explicitly governs native mutation/helper calls through exact source paths. The unused new File.Delete exemption was removed. The AI helper itself is forbidden from File.Delete/SetAttributes/Move/Replace or directory enumeration/deletion.

All corrective C# commands use the same Release/no-restore/TRX/results-directory form documented above.

| Evidence | Exact filter | Exit | Actual result |
| --- | --- | --- | --- |
| `ai-cleanup-architecture-red` | `FullyQualifiedName~Production_disposal_and_classified_retention_have_only_their_named_Infrastructure_boundaries` | 1 | 0 passed / 1 failed against original AI direct cleanup |
| `ai-cleanup-owned-red` | `FullyQualifiedName~AiDesignOwnedStagingFilesTests` | 1 | 0 passed / 12 failed against unavailable creation-issued boundary |
| `ai-cleanup-midbatch-red` | `FullyQualifiedName~Mid_batch_failure_or_cancel` | 1 | 0 passed / 2 failed: original rollback deleted unknown sibling in both failure/cancel windows |
| `ai-cleanup-owned-green` | same owned-staging filter | 0 | 12 passed / 0 failed |
| `ai-cleanup-migration-green` | migration filter below | 0 | 52 passed / 0 failed |
| `ai-cleanup-all-boundaries-green` | expanded filter below | 0 | 627 passed / 0 failed / 0 skipped — **before the independently identified held-identity race fix** |
| `ai-cleanup-swap-race-red` | `FullyQualifiedName~A_swap_after_verification` | 1 | 0 passed / 4 failed: cleanup, authority publication, attachment move, readonly mutation all allowed replacement after validation |
| `ai-cleanup-held-green` | migration filter below | 0 | 58 passed / 0 failed after held-handle correction |
| `ai-cleanup-readonly-zero-red` | `FullyQualifiedName~Readonly_only_staging` | 1 | 0 passed / 1 failed: native zero attribute bits left readonly unchanged and cleanup refused |
| `ai-cleanup-held-all-boundaries-green` | expanded filter below | 0 | **633 passed / 0 failed / 0 skipped**, 26 seconds: **476 architecture + 102 AI + 55 existing lease/import/retention** |

Migration filter:

```text
FullyQualifiedName~AiDesignOwnedStagingFilesTests|FullyQualifiedName~AiDesignAttachmentTests|FullyQualifiedName~AiDesignRequestStoreTests|FullyQualifiedName~AiDesignBridgeLifecycleTests|FullyQualifiedName~NativeHostProcessTransferTests|FullyQualifiedName~Production_disposal_and_classified_retention_have_only_their_named_Infrastructure_boundaries
```

Expanded filter:

```text
FullyQualifiedName~Architecture|FullyQualifiedName~AiDesign|FullyQualifiedName~NativeMessageCodecTests|FullyQualifiedName~NativeHostBoundaryTests|FullyQualifiedName~NativeHostProcessTransferTests|FullyQualifiedName~NativeRegistrationToolsTests|FullyQualifiedName~WorkstationAutomationLeaseTests|FullyQualifiedName~ImportCancellationShellBoundaryTests|FullyQualifiedName~ManualResultImportTests|FullyQualifiedName~RetentionWorkspaceTests|FullyQualifiedName~DiagnosticRetentionTests
```

The new behavioral cases prove CreateNew collision preservation, publication survival/old-temp disappearance, batch commit, removed draft snapshot retention, unknown sibling and original/old snapshot/Revision/Approved/Rejected/production TIFF byte/attribute preservation, pre-verification identity/reparse/shared-hardlink refusal, original failure preservation on refused cleanup, four deterministic post-verification swap attempts, pinned-parent replacement refusal, actual cleanup success and readonly-only cleanup. Test seams are internal deterministic IDs/after-prepared/after-verified hooks; they grant no production arbitrary-path registration or deletion capability.

This correction follows the parent's verified Astra generation 6.0 / High bounded design and refinement, with implementation continuing in this existing actual Sol generation 6.1 / High context. The existing requested generation 6.0 fallback disclosure remains unchanged. Final independent review and the parent's fresh locked build/full ordinary rerun remain required; this corrective focused PASS does not claim them or real Edge AC4.
