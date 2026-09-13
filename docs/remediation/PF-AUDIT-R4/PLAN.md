# PF-AUDIT-R4 — Plan

Scope: R4 only — a truthful, bounded diagnosis of the Photoshop readiness probe. Not restoration
of Production, not Jira acceptance, not A1/A2/A3.

Canonical checkout `D:\Repositories\printflow-Studio`, `master`. No branch, worktree or clone.

## Inherited state, verified at startup

| Fact | Supplied | Verified here |
|---|---|---|
| HEAD | docs `fdd1d4c` | `fdd1d4cd2850fa52b4a7d125b46fa64b5b2e5dea` — matches |
| Tested source | `ea12201` | R3 closure commit changes only three documents |
| Full suite | 11,836 / 0 / 0 | Inherited, **not rerun**; no reset performed |
| Release build | 0 warnings / 0 errors | Inherited |
| SDK | 10.0.400 | `global.json` pins `10.0.400` `latestFeature`; per-user dotnet reports `10.0.400` |

Tracked tree clean at startup; the operator-owned untracked `printflow-remediation-prompts/`
bundle is preserved and excluded from every commit.

R3's Release output was still on disk. All five assemblies in
`tests/PrintFlow.Tests/bin/Release/net10.0-windows10.0.19041.0/` hash **exactly** to
`artifacts/pf-audit-r3/final-corrected/assemblies-after.json`, so the binaries used for the
prerequisite observations are demonstrably the R3-verified output built from `ea12201`.
This is diagnostic reproducibility only. It is **not** a claim to satisfy R1's unresolved
harness-to-published-candidate build-origin condition, which remains open before A1/A2.

## R4-A — establish an entry that can actually reach the probe

1. Re-verify the R3-documented entry against current source, rather than trusting the handoff.
2. Determine the **actual** automatic prerequisite state by read-only observation, not inference.
3. Branch:
   - **A** — prerequisites legitimately satisfied: reuse the ordinary entry unchanged.
   - **B** — `ProductionRevalidation` alone blocks: add the smallest opt-in, non-authorizing
     diagnostic wrapper permitted by the R4 prompt.
   - **C** — any other automatic safety/integrity prerequisite unmet: keep it blocking and report
     the precise failed prerequisite, without relabelling it as revalidation-only.
4. Prove the admission boundary synthetically, review it, and commit before any live use.

## R4-B — one bounded live procedure, then inspect the actual result

Gated on one explicit current-session operator confirmation that the desktop is available, that
Meitu/Photoshop hold no unrelated documents needing protection, and that use of the real R2 lease
and accepted binaries for this bounded procedure is authorized. Past availability is not current
availability, and confirmation is not inferred from an older conversation.

Without that confirmation: finish R4-A, report **R4-B NOT EXECUTED** with an exact live-window
handoff — not as a new Product failure. No background scheduling.

With it: run the whole-live-check procedure through the source-verified entry, with the opt-in set
process-locally, unrelated inherited live flags cleared and the caller's environment restored.
Retain a fresh TRX, detailed console log, host exit and the structured report. Then inspect the
**report** — `Verified`, blocking checks, `Lifecycle`/`LatestProbe`, R3's typed stages,
`PrimaryFailure`/`SecondaryFailures`, cleanup outcome and any retained probe path. Host exit 0 and
an xUnit Passed row are not the live verdict; a missing `LatestProbe` means no probe was returned,
not a silent success.

## Boundaries held throughout

- No Adobe preference repair, Generator disablement, plugin/GPU/colour-space change, Action or
  preset modification, executable upgrade, blind key retry or timeout increase.
- No replacement of the Photoshop Save As identity/close protocol with ROT or a new script; ROT
  stays the existing read-only runtime-fact reader.
- No standard-set execution, Operator review, production revalidation, normal-App E2E, installer
  execution, deploy or Jira status change.
- R2 Busy/Unknown/own-scope/internal-PDF semantics, R3 revision-safe evidence lifecycle, R1
  evidence/publication/run-claim semantics and the operator readiness attestation stay unchanged.
- Historical failures — R3's first 11,834/2/0 run and R2's default-store acquisitions — are
  preserved, not rewritten.
- Local logical commits only; no amend, rebase, push or AI-attribution trailer.
- Never manually finish a failed probe and attribute the result to automated success.

## Verification budget

Test-only orchestration: focused synthetic boundary tests plus the needed Release build. No
automatic full-suite rerun, because no shared Product composition or authorization behavior is
changed. A full suite becomes justified only if production code is materially changed.

One separate read-only reviewer reviews the admission boundary before live execution and the
causal/evidence claims afterwards. If unavailable, disclose SELF-REVIEW ONLY rather than
fabricating independence.

## Stop point

Stop after the R4 diagnosis and HANDOFF. A supported root-cause candidate is written up —
reproducer, competing explanations, affected boundary, smallest proposed correction, needed
permission, validation and rollback — and **not** implemented. R1's pre-A1/A2 build-origin
condition is carried forward unchanged; even healthy R4 probes do not make A1/A2 release-ready
while it is open.
