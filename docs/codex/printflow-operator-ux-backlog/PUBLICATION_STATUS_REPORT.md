# PF-OPUX-v1 — direct master publication and Jira status synchronization

2026-09-29 NZ. Task `PF-OPUX-v1-master-publication-v1`. **MASTER_PUSH_VERIFIED; Jira statuses synchronized and read back.** Machine-readable detail: [PUBLICATION_STATUS_AUDIT.json](PUBLICATION_STATUS_AUDIT.json).

## Git publication

The user explicitly authorized a direct push to `master` for this batch. No feature branch or pull request was created.

| Item | Value |
|---|---|
| Repository / remote | `D:/Repositories/printflow-Studio` → `https://github.com/MQLite/printflow-Studio.git` (public) |
| Local HEAD before | `eea60198c5095243f0694717a7479b00c3e1cd73` |
| `origin/master` before | `cb552eefad00682c2e2f868206d248715dfeb6d3`. The remote had not advanced; it was an ancestor of local master. |
| New commit 1 | `cb875b2896481abd7915f52fba833b939aec22f4`: feat: operator UX for PF-OPUX-v1 through SCRUM-11148 (103 paths) |
| New commit 2 | `4ed9b52a448339e4041e5bbaf937312fee51aced`: docs: PF-OPUX-v1 plans, designs, Jira exports and tooling through SCRUM-11148 (44 paths) |
| Push | `cb552ee..4ed9b52  master -> master`: fast-forward, no force |
| Final `origin/master` | `4ed9b52a448339e4041e5bbaf937312fee51aced`. Fetch, `ls-remote` and the GitHub API agree, and the published paths equal the manifest. |

The same fast-forward also published seven earlier local docs commits (`d6e5e59`…`eea6019`, the SCRUM-11131 to SCRUM-11134 records). They were scanned before the push.

**Manifest** (`artifacts/pf-opux-master-publication/publication-manifest.tsv`)

| Class | Count |
|---|---|
| INCLUDE_PRODUCT | 65 |
| INCLUDE_MIGRATION | 2 (0018, 0019) |
| INCLUDE_TEST | 36 |
| INCLUDE_PROJECT_DOC | 20 |
| INCLUDE_TOOL | 6 |
| INCLUDE_REQUIRED_JIRA_EXPORT | 18 |
| EXCLUDE_UNRELATED_USER_WORK | 2 |
| REQUIRES_OWNER_DECISION | 5 |

**Excluded, preserved locally**
- `docs/printflow/original-jira-functional-coverage-reaudit.md` and `docs/printflow/scrum-11134-maintop-reference-comparison-fixed-workstation.md`. These carry the user's own uncommitted waiver deltas, which PLAN.md and WAVE1_PLAN.md record as outside PF-OPUX.

**Owner decision needed**
- `SCRUM-11144/11145/11145_CLOSEOUT/11147/11148_JIRA_READBACK.json`, and the new `PUBLICATION_STATUS_JIRA_READBACK.json`, embed the Atlassian accountId. Two of the earlier files also embed the owner email.
- The repository is public, and its published history has neither value. They were therefore not committed.
- The CSVs, which have neither value, were committed. HANDOFF links to those snapshots do not resolve on GitHub.

The staged scan found no binaries, credentials, tokens, keys, emails, real accountIds or workstation user paths.

## Verification

- **Source.** The settled source matched all 50 hashes of the SCRUM-11148 final review candidate. The rebuilt assemblies are byte-identical to that review's assemblies.
- **Build.** `Run-PublicationVerification.ps1 -Run 01` did a clean `--no-incremental` build: 0 warnings, 0 errors.
- **Test groups.** All used the exact SCRUM-11148 final filters. All passed, with 0 failed and 0 skipped. The counts overlap. Synthetic data only.

| Group | Result |
|---|---|
| Architecture | 452/452 |
| Combined UI | 518/518 |
| Workflow/persistence | 11531/11531 |
| Backend/delivery | 124/124 |
| Jira export integrity, with the updated exporter | 18/18 |

- **Migrations.** 0001–0017 are unchanged. 0018 and 0019 match their accepted candidate hashes.

**Exclusions, unchanged from SCRUM-11148**
- UI classes that open real windows or drive UIA, and the `ApplicationStartup` classes: the workstation is shared with a live operator.
- `ProductionCompositionTests`: production DI is prohibited.
- `ManualResultImportTests.Interrupted_recovery_offers_restart_manual_import_and_abandonment`: a known baseline hang, still **NOT PASS**.

## Jira

**Workflow**
- Transitions, discovered live: To Do 11, In Progress 21, In Review 31, Done 41.
- Each issue was evaluated against its own current AC and Validation section. Every status change below was made after MASTER_PUSH_VERIFIED.
- Each issue received one comment with marker `PF-OPUX-v1-master-publication-v1`.

**No issue was moved to Done.** Every implemented Task still has issue-level human, native or owner acceptance open.

| Issue | Before → after (transition) | Comment | Reason / open acceptance | Commit |
|---|---|---|---|---|
| SCRUM-11139 Epic | To Do → In Progress (21) | 10173 | No child Done; the real beginner walkthrough has not run | 4ed9b52 |
| SCRUM-11140 | To Do → In Review (31) | 10174 | Reference is published; the delivery-term business decision needs product-owner review (AC3) | 4ed9b52 |
| SCRUM-11141 | To Do → In Progress (21) | 10175 | Script is prepared; the human-only walkthrough has NOT RUN | 4ed9b52 |
| SCRUM-11142 | To Do → In Review (31) | 10176 | AC6 workstation bilingual visual check | cb875b2 |
| SCRUM-11143 | To Do → In Review (31) | 10177 | Post-focus-fix native input; physical input; workstation visuals | cb875b2 |
| SCRUM-11144 | To Do → In Review (31) | 10178 | KeepOriginalExtent PNG approval contract (owner decision) and asset-route acceptance; physical disconnect | cb875b2 |
| SCRUM-11145 | To Do → In Review (31) | 10179 | Explorer selection (AC5), real picker, physical input, workstation layout (AC8) | cb875b2 |
| SCRUM-11146 | To Do → In Review (31) | 10180 | Human bilingual visual check | cb875b2 |
| SCRUM-11147 | To Do → In Review (31) | 10181 | Human synthetic-image check, physical drag and keys, layout | cb875b2 |
| SCRUM-11148 | To Do → In Review (31) | 10182 | Human check and real colleague round trip, picker/Explorer, physical input, visuals, novice usability | cb875b2 |

- SCRUM-11149 to SCRUM-11155 were read only and remain To Do.
- Comment 10174 was edited once, in place, to correct its own transition line.

**Final authenticated readback, 2026-09-28T22:29:11.826Z**
- One complete page: 17 issues, 26 Blocks, 51 string labels, 34 exact timestamps.
- All comments are complete, with exactly one marker on each of SCRUM-11139..11148.
- **Drift against the SCRUM-11148 snapshot:** only status, updated and the new comment changed. Relationship fields differ only in the embedded related-issue status. Descriptions, AC, labels, priority, type and created are unchanged, and prior comments are byte-identical.
- The exporter gained only a narrow task mapping, with Write_Action `STATUS_TRANSITION_AND_COMMENT` and `READ_ONLY`.
- **Exporter validation:** PASS; exactly 23 columns with a UTF-8 BOM.
- **Independent oracle:** raw → snapshot → CSV PASS, 0 failures.

## Artifacts

- Report: `docs/codex/printflow-operator-ux-backlog/PUBLICATION_STATUS_REPORT.md`
- Audit: `docs/codex/printflow-operator-ux-backlog/PUBLICATION_STATUS_AUDIT.json`
- Readback: `docs/codex/printflow-operator-ux-backlog/PUBLICATION_STATUS_JIRA_READBACK.json` (local only; see above)
- CSV: `docs/codex/printflow-operator-ux-backlog/PUBLICATION_STATUS_JIRA_FINAL.csv`
- Evidence: `artifacts/pf-opux-master-publication/`, including the preflight, manifest, logs/TRX, push verification, raw readback, fidelity result and drift results

These post-push outputs and the HANDOFF update are local and uncommitted.

## Not performed

- No deployment, production database migration, production application startup or customer-file operation.
- No force-push, history rewrite, branch-protection bypass, feature branch or PR.
- No SCRUM-11149 or later work, and no global routing policy update.

**Routing.** Claude adaptation v1.2 of policy v2.4, `route_offset: 0`. Opus High was requested; the host model is `claude-opus-5-5`, effort UNVERIFIED. No Sonnet unit, no subagent.
