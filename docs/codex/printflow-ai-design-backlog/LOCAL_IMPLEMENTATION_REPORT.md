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
