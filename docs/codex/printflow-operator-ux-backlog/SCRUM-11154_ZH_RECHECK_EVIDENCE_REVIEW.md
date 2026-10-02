# SCRUM-11154 — Chinese targeted recheck: independent evidence-to-claim review

2026-10-02. One fresh read-only reviewer (Claude Code `personal-dev-reviewer`, Read/Glob/Grep only, separate context; model/effort requested Opus High, runtime UNVERIFIED). It received the original recheck prompt, the checklist, the scope decision, the two result documents and the raw local evidence paths, not the executor's working notes. It viewed all 30 window captures. It did not rerun anything and did not certify unobserved behaviour.

**Final verdict: SUPPORTED** (round 1 SUPPORTED WITH CORRECTIONS; round 2 SUPPORTED WITH CORRECTIONS; round 3 SUPPORTED after all corrections). The executor applied every correction to [SCRUM-11154_ZH_RECHECK_RESULTS.md](SCRUM-11154_ZH_RECHECK_RESULTS.md) and [SCRUM-11154_ZH_RECHECK_LEDGER.json](SCRUM-11154_ZH_RECHECK_LEDGER.json).

## Round 1 findings and dispositions

| # | Severity | Finding | Disposition |
|---|---|---|---|
| F1 | P1 | Consent account named only A3 and A12 as acted before renewal; the B1 export run and B5 re-run also followed absences before renewal | Six such actions now listed; per-row consent limits added |
| F2 | P2 | A3 limit covered all of A3 (Esc/Keep from 00:48:20Z) | Corrected |
| F3 | P2 | “Renewed four times before consequential steps” contradicted the limits | Reworded (see R1) |
| F4 | P2 | Window claimed 1000×1047 from the first capture; A8 was captured at 1000×700 | Corrected; AC3 says only A8 at 1000×700 |
| F5 | P2 | B9 PASS without scroll positions | B9 PARTIAL |
| F6 | P2 | N4 omitted the contradictory screen (当前文件 PNG 600×400 beside a “valid” TIFF row) and lacked retained byte evidence | Described; private byte/hash notes added |
| F7 | P2 | B1 PASS without its release-semantics limit | B1 PARTIAL; residual release NOT RUN |
| F8 | P3 | “808 source files” mixed manifest entries and build outputs | Corrected |
| F9 | P3 | A1 0% endpoint uncaptured; two capture names say 50%/resized while UIA read 75/71 at 1000×1047 | Marked owner-reported; private annotation file |
| F10 | P3 | B7 quote omitted 已失效; no earlier delivery existed | Corrected |
| F11 | P3 | B8 actions only displayed | Corrected |
| F12 | P3 | O1 vs dotted focus cue in the A2 capture | Labelled owner-reported with caveat |
| F13 | P3 | N1's Session part is source-only | Corrected |
| F14 | P3 | Display, full hashes, owner quotes and empty folder not retained | Recorded in private verification notes |
| F15 | P3 | Capture note said “not confirmed” for B3 although 0 mm was submitted | Annotated |
| F16 | P3 | Public text named the owner's other applications | Generalised |
| F17 | obs | A8 Tab and A10 完成 also followed absences; no threshold stated | Disclosed; judgement stated |

## Round 2 findings and dispositions

| # | Severity | Finding | Disposition |
|---|---|---|---|
| R1 | P2 | F3 fix understated renewals preceding A4/A6 and A12 delivery/A10 save | Reworded: all four renewals preceded later consequential steps; two arrived after the steps first asked for |
| R2 | P3 | Owner remark that 导出诊断包 opens no dialog missing | Observation O9, linked to diagnostic navigation; not an A12 failure |
| R3 | P3 | A10 full hash for the reviewed revision missing | Supplement records equal full hashes for the revision, Approved copy and delivery |
| R4 | P3 | Display bounds read after the run | Stated |

Round 3 confirmed R1–R4 and noted one optional evidence-list nit, also fixed.

## Confirmed by the reviewer

Candidate/run identity and binding (including `ScenarioLedgerSha256`), acknowledgment before launch, exit 0 and settled quiescence, absence of agent input, the PASS rows A2/A4/A6/A8–A12/B3, B4 FAIL, A5 BLOCKED, N1–N3 facts, English recorded as NOT RUN — NOT REQUIRED BY OWNER, F6 as known negative, and no copy-acceptance, novice-validation or Done claim.

## Limits

The reviewer could not read binary bytes or the chat transcript; magic bytes, full hashes, display bounds and verbatim owner answers rest on the executor's private notes, which agreed with every retained artefact it cross-checked. Raw logs, captures and notes stay out of Git.
