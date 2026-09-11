# PF-AUDIT-R3 — Readiness lifecycle and truthful probe diagnostics

Scope: R3 only, canonical master starting at 519be2421e6dc6ec968caa5e550700231a3a57b1.
Tracked tree initially clean; operator-owned untracked prompt bundle preserved. SDK configuration
and installed executable both select 10.0.400. R2 final-source 11,819/0/0 is inherited evidence,
not rerun at startup. Historical R2 failures and two default-store acquisitions remain recorded.

Authority: installed routing policy 2.3, PLAN_EXECUTE, explicit route_offset 0. Current Windows
host; CODEX_HOME environment variable absent, installed policy read from C:/Users/admin/.codex/workflows/development-routing.md.
Planning NormalRoute/RequestedRoute Astra High (cross-module state/cleanup); implementation and
tests Sol High; review Sol High in one fresh read-only agent; docs Luna Low. Offset 0, UNCHANGED.
In-place MODEL_SWITCH_UNAVAILABLE; ActualRoute UNVERIFIED, safe current-runtime fallback.
CONTINUE for connected implementation; FRESH_REQUIRED for independent review. Post-plan routing
reassessment selects Sol High; no downgrade claimed without real runtime evidence.

Exact CSV mapping read: index 50/source 11503/SCRUM-11110 environment page, index 51/source
11504/SCRUM-11111 drift refusal; original descriptions require exact failures, no automatic repair,
confirmed settings/safe state and an open/close test; unconfirmed assumptions block automation.
Approved design sections 13.4, 14 and existing identity/close helpers constrain diagnostics.

| Finding | Current implementation | Status / change | Proof |
|---|---|---|---|
| Busy, Unknown, own-scope, internal PDF | R2 Reobserve/gate/bootstrap routes | Satisfied; preserve | Existing affected tests |
| Historical success vs current observation vs latest attempt | Only private evidence and generic ObservedAt | Add diagnostic lifecycle, no TTL or authority | Real gate/report lifecycle tests |
| Old passive failure may clear newly published evidence | Snapshot under lock, unconditional later clear | Conditional invalidation under existing lock | Barrier interleaving |
| Probe Unit/failure loses stages and ignored finally cleanup failures | RunProbeAsync + real foundation/driver guards | Typed bounded progress at request/confirmation boundaries; primary/secondary outcomes | Synthetic partial/success/unwind matrix |
| Report drops all probe detail | Workstation result -> gate -> EnvironmentReadinessReport -> readiness.json | Add shared optional metadata; historical absence unknown | Real orchestration and JSON round-trip |

Inspect/instrument helpers without replacing Save As identity/close protocol, changing input,
settings, timing or recovery actions. Retain uncertain backing files. No fabricated cleanup success.
Tests use synthetic workspaces/stores/resources and recording external seams only; clear inherited
PRINTFLOW/PF_R2/PF_R3 controls process-locally and preserve Windows PowerShell child module-path fix.
Reuse R2 full-tree isolation audit and review newly reachable tests before running.

Focused probe/readiness/projection tests first; affected gate/lease/composition/PDF/bootstrap next.
One fresh read-only review then settled-source Release build and complete Product suite because
shared readiness/helper behavior changes. Record host exits, TRX individual/counter outcomes,
SDK, source and assembly/input hashes; no source edits during full run, no unchanged-code retries.
Local logical commits only. Complete report and HANDOFF with source-verified R4 entry/prerequisites.
R1 build-origin condition remains open; no attestation or publication contract change.
Stop before R4/live acceptance; real external operations and production revalidation NOT EXECUTED.
