# SCRUM-11133 — Multiple TIFF output sizes from one approved source Revision

Date: 21 September 2026

Work item: SCRUM-11133 / CSV Work Item 11709

Verdict: **PASS — three differently sized `PrintOutput`s (275 / 200 / 100 mm on the WIDTH edge)
exist for one approved source Revision, each with its own dimensions, Photoshop attempt, validation,
hash and hash-bound Operator approval; two were newly generated through the ordinary Add Another
Size route on the fixed workstation, and every approval left the other outputs and their decisions
unchanged — one positive, sequential run (the Product never allows several sizes pending at once)**

Review status: **INDEPENDENT READ-ONLY RECORD AND SOURCE REVIEW COMPLETED; INDEPENDENT RE-EXECUTION
NOT PERFORMED** (§9).

Raw local evidence (git-ignored, not committed): `artifacts/scrum-11133/11133-cc-20260921-0a94/`.

---

## 1. Acceptance authority

Read directly from the retained Jira export
`C:\Users\admin\Downloads\printflow_studio_mvp_jira_epics_tasks.csv`, row **11709**, title
`Verify Multiple TIFF Output Sizes from One Approved Design` (the established mapping continues
11132 → 11708, 11133 → 11709; confirmed by the title):

> Generate at least three differently sized PrintOutputs from one approved source Revision and
> verify that each output uses the same approved source, has independent dimensions, validation,
> hash and final review state, and that approving one size never approves or mutates another.

No discrepancy with the task's quotation. No Jira connection was made and no transition is claimed.
This is the SCRUM-11133 half of P3-4; the Maintop comparison (SCRUM-11134) was **not** started.

**Operator scope decision carried forward.** 「现在不做负向检查」 still applies: no rejection,
forced retry, takeover, interruption, Recycle Bin test, invalid import or failure injection was
performed or offered. Observing pending state and positive approval isolation are positive-path
checks.

## 2. Execution record

| Fact | Value |
|---|---|
| Executor | Claude Code, VS Code extension host (from the runtime) |
| Native policy | `C:\Users\admin\.claude\workflows\development-routing.md` adaptation **v1.1**, via the managed `PERSONAL_DEV_ROUTING` entry. Mode `EXECUTE_HANDOFF` |
| ExecutionTarget | Opus / High |
| ActualModel | Opus 5 (`claude-opus-5`), reported by the host |
| ActualEffort | **UNVERIFIED** — `effortLevel: high` is configured intent, not observed |
| RouteOffset | 0 — none supplied or inherited |
| Context | CONTINUE; no Sonnet delegation; no model switch attempted |
| HEAD at start | `7be345a`, working tree clean |
| Repository / workspace | `D:\Repositories\printflow-Studio` on `master`; `D:\PrintFlowStudio`. No branch, worktree, clone, reset, clean, amend or rebase |
| Input channel | Native UI Automation (`InvokePattern`, `ValuePattern`, `SelectionItemPattern`, `ExpandCollapsePattern`) on visible, enabled controls of the candidate's own window. No SendKeys, synthetic mouse, bootstrap, Fake mode, direct service call or database write. All SQLite reads `Mode=ReadOnly` through the candidate's own `Microsoft.Data.Sqlite` |

## 3. Retained authority, re-verified read-only

| Item | Value | Verified |
|---|---|---|
| Build pair | `d915b1a6-4c06-4b16-9a20-5e1341d1ae9c` | Resolved from the qualification result's `Binding.BuildOrigin`; `tools\regression\New-PrintFlowBuildPair.ps1 -VerifyOnly -ReceiptPath …\build-pair.json` → "Build pair verified", at start and again at handback |
| Receipt | `52E6DBC5…8FBC` | = `BuildOrigin.ReceiptSha256` |
| Qualification result | `a2-v3-20260917-154736-d915b1a6/result.json`, `4083F1FB…5FDA1A`, `Status = Passed` | yes |
| Active record | `E6A7D7EA…F3B9`; its four `productAssemblies` digests equal the candidate DLLs on disk | yes |
| Preset | `printflow-workstation-v1` 1.18.0, `8484F0AA…C8E0F` | yes |
| Candidate fingerprint | recomputed with `ProductBuildIdentity.Fingerprint`'s own canonicalisation (lower-cased names, ordinal-ignore-case order, `name:SHA\n`): `13A73BEFAE17B18E61E8B975CF030E7156315E9AEBDE661E391550C52BEEDD5F` | yes |
| Candidate exe / appsettings / harness | `06FB7C7A…6BCA` / `E5862F5D…CF0A` (`Adapters.Mode = Production`) / `9AAC5BEB…FAB9` | yes |

Nothing was built, paired, qualified, published or revoked. All values were unchanged at handback
(§8).

## 4. Source selection and reuse decision

The preferred seed was used. Its identities were resolved from the 11132 raw evidence
(`png-db-04-completed.json`) and re-read from the live database before any input
(`db-00-baseline.json`). Apart from its own `observedAtUtc`, the live readback was **identical**,
field for field, to the 11132 completion readback.

| Object | Identity | Pre-run facts |
|---|---|---|
| Session | `01a0c1f4-c5f4-7580-9365-ff914febfcaf`, `11132-PNG-20260921-FC99`, `GENERATE_PRINT_TIFF`, `COMPLETED` | — |
| Source Revision **R** | `01a0c1f4-c6b4-7b6b-a502-6eadd5689089`, `IMPORT`, PNG 3412 × 5120, `HasAlpha 1` | `IsValid 1`, `ReviewState APPROVED`; `OriginalConfirmation` decision `01a0c1f6-72cc-7d96-abce-0b914d2fe03f` APPROVED over `A20A722D…77E2`; session `Source\` copy and the v3 fixture both rehash to `A20A722DB394B8CBBAE7975CC930DD456971E913E5844C21F34B27B9C4D377E2` |
| Output **A** | `PrintOutput 01a0c1f8-050c-7143-a663-7a7adcd110fb` | `SourceRevisionId = R`; 275 mm WIDTH, 3248 × 4874, W1_2PX; valid; decision `01a0c1f9-8b44-761b-af6b-04768ac497b0` APPROVED over `F6D5C841…0CED2`; `Approved\…_275mm_CMYK_W.tif` rehashes to that value |

**Route.** The completed session was reachable through the ordinary Home row button 「查看」
(`Home.ResumeSession`, located inside the single row named `11132-PNG-20260921-FC99`). It only
loads persisted state (`HomeViewModel.ResumeAsync` → `ISessionService.LoadAsync`); the database was
confirmed unchanged after opening (`db-01-opened-details.json`). The Session screen then offered
`Session.AddAnotherSize` 「增加另一个尺寸」. `WorkflowEngine.AddAnotherSize` requires a `Completed`
session with a `PhotoshopOutput` step, resets only `PrintDimensions` and later, clears the plan,
sizing selection, enlargement authority and W1 branch, and emits
`BeginAdditionalOutput(UpstreamRevisionOf(PrintDimensions))` — the same approved Revision. No
re-import, no re-review of R and no fallback were needed.

**Target edge.** A's authoritative plan is `SizingMode CUSTOM_TARGET_EDGE`, `SizingTargetEdge WIDTH`,
`TargetPlanPhotoshopEdge WIDTH`, direction `SHRINK`. B and C used the same edge.

**W1 branch.** A's branch is `W1_2PX` (the Operator's 11132 "W2" was read as the 2-pixel W1 branch,
labelled 「2 像素 — 实色／整块矩形图案」). Because Add Another Size clears the branch, it was
selected again, explicitly, for each new output, and kept identical to A. The spot channel remains
`W1`; no other mask policy was introduced.

**Planned test parameters** (task-authorised, not historical user quotes): B = 200 mm and C = 100 mm
on WIDTH, constrained proportions, fixed 300 PPI. The 3412-pixel source makes both a shrink, so no
enlargement authority was required or created.

## 5. Desktop admission and gate

**Exclusive use.** One current confirmation was requested before any desktop input, naming the
reuse plan, B/C sizes, the two final decisions expected, the possible Generator dialog, the
no-negative-checks scope and the untouched CorelDRAW document. Answer 「同意独占，工作已保存」,
recorded at the time (`exclusive-use-confirmation.json`). CorelDRAW (PID 14324) held the Operator's
`Filomena Hansen.cdr` throughout and was never touched.

**Processes.** PrintFlow PID 1320 — the candidate's own `PrintFlow.App.exe`, command line with no
arguments, started 13:17:08 local — was positively identified and reused. Photoshop PID 8408 is the
accepted `D:\Adobe Photoshop CC 2019\Photoshop.exe`, visible title exactly `Adobe Photoshop CC 2019`,
`Modal Layer` and `Document Layer` not visible. Meitu PID 8368 was idle and never used.

**Gate — refresh versus new execution.** Opening 生产就绪状态 showed a passing status stamped 16:09,
which was a passive re-read. One ordinary 安全恢复并重新检查 was then run (pressed 16:10:42 local):
**本工作站已通过生产环境校验。** — **11** 必须满足 automatic rows 通过 (including 升级后重新验证),
**2** 仅供参考 advisories (已认可文件的只读标记; 美图秀秀与 Photoshop 界面语言), **7 / 7** live checks
通过. The Photoshop 测试图像 detail shows it was a **new execution**: probe
`72255a95271d43e2a7e1e0afcca28e1f`, attempt 04:10:42Z, last complete live verification 04:10:48Z,
stages ProbeCreation → … → CleanupCompleted, cleanup `Succeeded`. No Generator dialog appeared and
no Operator action was needed. The probe inventory stayed at nine directories.

**Operator assistance, all of it:** the exclusive-use grant and two final TIFF decisions.

## 6. The two new sizes

Both followed 查看 → 增加另一个尺寸 → 自定义尺寸 → 目标边 宽度 → 目标尺寸 → 确认自定义尺寸 → W1
2 像素 → 确认 W1 分支 → 执行步骤 → (Operator decision) → 批准此 TIFF → 完成, inside the **same
session**, which is the Product's real behaviour. Each Photoshop attempt `SUCCEEDED` first time, with
no `FailureCode`, and Photoshop was document-free afterwards.

### 6.1 Parent-session transitions (expected, not mutation of an old output)

Each Add Another Size moved the session `COMPLETED → ACTIVE`, reset `PrintDimensions` and
`PhotoshopOutput` to `WAITING`, and nulled the session-level sizing/plan/W1 columns. Those columns
describe the *current* size being planned; each output's own plan is retained on its
`ProcessingAttempt` and `PrintOutput` rows. `CompletedAtUtc` is overwritten by each later completion
(03:19:27Z → 04:21:54Z → 04:31:00Z); A's own completion instant is still evidenced by its attempt,
decision and file times. Likewise the shared `SessionStep` pointer for `PhotoshopOutput`
(`CurrentRevisionId` / `CurrentRevisionSha`) moved A → B → C and now names C. None of these shared
columns is part of A's or B's own `PrintOutput`, output `Revision`, attempt or decision rows, which
did not change (§7).

**Attempt-labelling fact.** B's attempt carries `RetrySequence 1`, `RetryOfAttemptId` = A's attempt;
C's carries `RetrySequence 2`, `RetryOfAttemptId` = B's. In source, `RetrySequence` is the step's
`AttemptCount` (not reset by Add Another Size) and `RetryOfAttemptId` the step's previous attempt
(`WorkflowEngine` → `RecordAttemptStarted(…, step.AttemptCount)`; `SessionService` retry-of lookup).
It is an attempt ordinal on the step. No attempt failed, no retry was requested and no UI showed
重试. The stored rows nevertheless read as a retry chain that did not happen; that is a Product
audit-model observation for the backlog, not a defect of this run, and nothing was changed.

### 6.2 Size matrix

| | **A** (reused) | **B** (new) | **C** (new) |
|---|---|---|---|
| PrintOutputId | `01a0c1f8-050c-7143-a663-7a7adcd110fb` | `01a0c22c-79c8-7bf5-94f5-9dc05afe5591` | `01a0c234-1d67-7a80-a075-aa28b22ab71c` |
| `PrintOutput.SourceRevisionId` / attempt `InputRevisionId` / `TargetPlanSourceRevisionId` | R / R / R | R / R / R | R / R / R |
| `TargetPlanSourceSha256` | `A20A722D…77E2` | `A20A722D…77E2` | `A20A722D…77E2` |
| Session | `01a0c1f4-c5f4-…febfcaf` (11132 run) | same session | same session |
| Photoshop attempt | `01a0c1f7-babb-763b-a8f3-319a7895204a`, 03:17:13 → 03:17:32Z | `01a0c22c-373e-7fc5-b0e8-6bf63451d539`, 04:14:32 → 04:14:49Z | `01a0c233-e03d-774e-9aa3-0c23fe2947c4`, 04:22:54 → 04:23:10Z |
| Mode / edge / mm | CUSTOM_TARGET_EDGE / WIDTH / 275 | CUSTOM_TARGET_EDGE / WIDTH / 200 | CUSTOM_TARGET_EDGE / WIDTH / 100 |
| Rational scale, direction, resampling | 812/853, SHRINK, BICUBIC_SHARPER | 1181/1706, SHRINK, BICUBIC_SHARPER | 1181/3412, SHRINK, BICUBIC_SHARPER |
| Projected px | 3248 × 4874 | 2362 × 3544 | 1181 × 1772 |
| Decoded px (executor IFD parse) | 3248 × 4874 (11132's `png-tiff-structure-pre-review.json`; not re-parsed here) | 2362 × 3544 (this task) | 1181 × 1772 (this task) |
| Physical | 275.0 × 412.67 mm | 200.0 × 300.06 mm | 100.0 × 150.03 mm |
| Resolution | 300 PPI | 300 PPI | 300 PPI |
| W1 branch | W1_2PX | W1_2PX | W1_2PX |
| Enlargement authority | none | none | none |
| Validation (attempt `AdapterNotes`) | `photoshop-tiff-validation-c1-v1`; W1 non-white 5,036,910 px | `photoshop-tiff-validation-c1-v1`; W1 non-white 2,655,972 px | `photoshop-tiff-validation-c1-v1`; W1 non-white 656,320 px |
| Delivered file | `Approved\11132-PNG-20260921-FC99_275mm_CMYK_W.tif` | `Approved\11132-PNG-20260921-FC99_200mm_CMYK_W.tif` | `Approved\11132-PNG-20260921-FC99_100mm_CMYK_W.tif` |
| Bytes | 106,445,076 | 57,030,044 | 14,989,336 |
| Delivered SHA-256 | `F6D5C841BA0EF5AFA0B413F0947CDDE27219CEC8A37B5C9218E202740FC0CED2` | `D522483F6143527279F9B5CB56897D19AC2B76D01D045AAFA6E4486A01C09569` | `B94AFF41B71F278B8BA1F39840561F8B2AAA0C1634DF791DC62C6A31BE531C60` |
| Final decision | `01a0c1f9-8b44-761b-af6b-04768ac497b0`, 03:19:11.940Z | `01a0c232-9b9c-75e8-ae90-51c2bc6e9984`, 04:21:31.676Z | `01a0c23b-0f6e-7fed-97aa-aace18d807dd`, 04:30:45.614Z |
| Decision `ReviewedSha256` | = delivered hash | = delivered hash | = delivered hash |
| Evidence origin | **reused** 11132 execution; timestamps original | new, this task | new, this task |

`projectedPixel = round-half-away-from-zero(mm × 1500 / 127)` gives 2362 and 1181 on the target
edge; the other edge is `round(5120 × scale)`: 3544.4 → 3544 and 1772.2 → 1772. Decoded dimensions
equal projections for all three; `_200mm` / `_100mm` in the names equal the requested values.

Every validation reported IBM PC / little-endian, compression none, 5 × 8-bit interleaved
separated, one Photoshop spot `W1`, alpha false, pyramid false, "backing unchanged A20A722DB394",
save as copy, settled over three observations, and "cleanup signed owned-document discard
completed; expected Working document gone". The adapter's limitation is carried forward: *W1
sample content is proven non-empty from the fifth sample; the inspector does not interpret the
ink's visual meaning.* The W1 figures above are counts (≈31.8 %, 31.7 %, 31.4 % of each canvas),
not spatial or print-quality claims.

**Executor structural re-measurement** (`b-`/`c-tiff-structure-pre-review.json`, before each
decision): `II`, `BitsPerSample 8,8,8,8,8`, `SamplesPerPixel 5`, `Compression 1`, `Photometric 5`,
`Planar 1`, `ExtraSamples 0`, `X/YResolution 3000000/10000 = 300`, `StripByteCounts` = width × height
× 5 (41,854,640 and 10,463,660), Photoshop image resources present, SHA-256 equal to
`PrintOutput.Sha256`. Pixels were never decoded or shown to the model.

## 7. Approval isolation — observed sequence

Snapshots are complete database readbacks of the session plus file rehashes, taken at each point.
The normal route allows the next size only after the current one completes, so **three outputs were
never pending at once**; this is a sequential observation, not a simultaneous-pending test.

| # | Observation (file) | A | B | C | `ReviewDecision` rows |
|---|---|---|---|---|---|
| 0 | Baseline (`db-00`) | APPROVED, valid, `F6D5…` | — | — | 2 (R original, A final) |
| 1 | After Add Another Size for B (`b-db-01`) | APPROVED, valid, unchanged | — | — | 2, unchanged |
| 2 | B plan confirmed (`b-db-02`) | unchanged | — | — | 2 |
| 3 | **B before approval** — latest snapshot before the decision, 04:15:04Z (`b-db-03`, `b-ui-05`) | APPROVED, valid; file `F6D5…` | **NOT_REVIEWED**, valid, in `Working\`; UI 「尚未审核 · 待审核（Working）」 | — | 2 — **none for B** |
| 4 | After B approved (`b-db-04`) | unchanged | APPROVED; file promoted to `Approved\` | — | 3: new `01a0c232…` over `D522…` |
| 5 | B completed (`b-db-05`) | unchanged | APPROVED, `D522…` | — | 3 |
| 6 | After Add Another Size for C (`c-db-01`) | unchanged | unchanged | — | 3 |
| 7 | **C before approval** — latest snapshot before the decision, 04:23:29Z (`c-db-03`, `c-ui-05`) | APPROVED, `F6D5…` | APPROVED, `D522…` | **NOT_REVIEWED**, valid, in `Working\` | 3 — **none for C** |
| 8 | After C approved and completed (`c-db-04`, `c-db-05`) | unchanged | unchanged | APPROVED, `B94A…` | 4: new `01a0c23b…` over `B94A…` |

So: approving B left A intact; C, created after B was already approved, did **not** inherit an
approval; approving C left A and B intact. The pre-approval snapshots are minutes before each
decision, not at its instant; what closes the gap is that each post-approval snapshot (`b-db-04`,
`c-db-04`) contains exactly one new decision, bound to the right subject and hash. The reverse
direction — approving A while B or C exist — is not exercised, because A was approved before either
existed. The two pre-existing decision rows were byte-for-byte
unchanged in every snapshot (same Ids, times, hashes). The source approval (`OriginalConfirmation`,
subject `REVISION`) never served as a final-output approval. Output `Revision` rows keep
`ReviewState NOT_REVIEWED` while their `PrintOutput` is `APPROVED`; that is the existing record model,
equally true of A since 11132, and is noted without a claim.

**Operator decisions, recorded before actuation.** For each of B and C the executor put the exact
PrintOutputId, file name, full SHA-256, byte length, source Revision, physical and pixel size,
300 PPI, shrink, W1 branch and the Product's validation line to the Operator. Answers
「批准 B」 and 「批准 C」 were written with their object binding **before** any review control was
pressed (`b-operator-decision.json`, `c-operator-decision.json`): `recordedAtLocal` precedes
`DecidedAtUtc` by 0.75 s (B, 04:21:30.926Z → 04:21:31.676Z) and 0.79 s (C, 04:30:44.824Z →
04:30:45.614Z). A guard then confirmed that the Product's review screen still showed that output's
hash prefix and pixel size before `Session.Approve` was invoked. At the time those files held only
the binding and the answer; the verbatim question text, the guard's console output and the press
timestamp were added afterwards as a dated `amendment` block (independent review MINOR-1/2). They
are executor transcriptions from this session, not contemporaneous files. **Who judged and who pressed:** the Operator judged; the executor
pressed. `ReviewDecision.Operator = "admin"` is the shared Windows account, so the record itself
cannot show that division; it is documentary.

**Hash chain.** For each output: `ReviewDecision.ReviewedSha256 = PrintOutput.Sha256 =` output
`Revision.Sha256` = delivered `Approved\` file hash. Every session-workspace file was hashed by path
after B's completion and after C's completion (console observations, not saved), and again after
the review as the file of record
(`handback-rehash-paths.json`: the `Approved\`, `Revisions\` and `Working\` copies of each output
and the `Source\` copy of R, by full path).
Lineage is output → R; no TIFF-to-PNG byte equality is claimed.

**Supporting contracts, inspected not rerun.** `AddAnotherSizeTests` (three methods: the second
size's approval leaves both outputs valid and approved; a rejected second size leaves the first
untouched; returning upstream invalidates both) and `MaximumBoundsPlanTests` describe the same
engine behaviour. The live evidence above covers every clause of the criterion directly, so no
isolated test was run: there was no remaining positive-path gap for them to close, and the
rejection contract is a deferred negative path. No build, restore, standard set or full suite.

## 8. Handback (16:32 local)

| Item | Observation |
|---|---|
| PrintFlow | PID 1320, candidate executable, back on Home |
| Photoshop | PID 8408, accepted install, single visible window `Adobe Photoshop CC 2019`, document-free, no dialog |
| Meitu | PID 8368, not used |
| Customer work | CorelDRAW `Filomena Hansen.cdr` never opened, closed, saved or modified |
| Session | `COMPLETED`, all four steps `APPROVED`, three valid APPROVED outputs, four decisions |
| Source R | session copy and fixture `A20A722D…77E2`, unchanged |
| Outputs | A `F6D5C841…0CED2`, B `D522483F…09569`, C `B94AFF41…31C60` |
| Protected outputs | A3 PNG `8D94D320…80C1`, 11131 TIFF `1DD555E8…0A5D`, 11132 JPEG `DCC5EF96…8D2A`, PDF `2A405A87…1AF9`, PSD `91F13D4C…857F` — unchanged, not opened |
| Inputs | all six v3 inputs unchanged |
| Retained authority | VerifyOnly passed; receipt, exe, appsettings, harness, result, active record, preset unchanged |
| Lease | owner fields all null. The store hash moved from `A1F00546…37E9` (pre, identical to the 11132 handback) to `D35494AB…377F`: the Product acquiring and releasing it during the gate and two runs. A free lease now is not back-inferred into in-run behaviour |
| Probes | nine historical directories; the gate's probe was cleaned by the Product |
| Working files | the Product left each attempt's TIFF copy in `Working\<attemptId>\` (safe completion retention, as for A) and removed the source working copies |

One fresh live gate ran at the start (§5). No gate ran at handback; this is a passive observation.

**Not performed:** build, restore, new pair, qualification, publication, revocation; Product,
test-source, schema, configuration, preset, Action or routing edits; full suite, standard set or
isolated tests; any live negative check; re-import or re-review of R; repeat of A's approval;
resize or copy of an earlier TIFF; SCRUM-11130/11131/11132 or A2/A3 work; Prompt 25; the takeover
label; SCRUM-11134 / Maintop.

**Evidence index** — `artifacts/scrum-11133/11133-cc-20260921-0a94/`:

```
observation-sidecar-pre.json        authority, processes, source/A/protected hashes, lease, probes (pre-run)
exclusive-use-confirmation.json     the grant, recorded at the time
db-00-baseline.json                 live readback before input (equal to 11132 completion readback)
db-01-opened-details.json           after opening 查看 (unchanged)
gate-0-before*.txt, gate-1-*.txt    passive status, pressed time, result screen, live-check detail text
b-*, c-*                            per-size evidence (below)
lease-pre.json / lease-handback.json
handback-rehash.json                authority, R, outputs, protected, inputs, lease, probes, processes
handback-rehash-paths.json          every session-workspace file by full path (after the review)
```

The B/C per-size files are `*-db-*.json` (readbacks), `*-ui-*.txt` (screens), `*-pressed.txt`
(add / run-step press times), `*-tiff-structure-pre-review.json` and `*-operator-decision.json`.

## 9. Independent review

| Fact | Value |
|---|---|
| Reviewer | **One** scoped `personal-dev-reviewer` subagent in a separate context, `Read` / `Glob` / `Grep` only |
| Supplied | the criterion's CSV location (it re-read line 75 itself), the task constraints, this report, the raw evidence directory, the 11132 seed evidence and source/test paths — not the executor's reasoning |
| Real limitation | It could not hash, decode, execute or observe anything, nor see the Operator conversation. Every hash equality it checked compares recorded strings; file hashes, IFD parses, process identity, the Operator's answers and who pressed which control remain **executor measurements or attestations** |
| Rounds | One. All findings were documentation-level; no affected-scope recheck was needed |

| # | Severity | Finding | Disposition |
|---|---|---|---|
| R1 | MINOR | The decision files kept the binding and answer but not the question put to the Operator, although §7 said the validation line was presented | **Accepted.** Verbatim question text added to both files as a dated `amendment`, disclosed in §7 as an after-the-fact transcription |
| R2 | MINOR | No artifact for the pre-approval guard or the approve-press time; the "before approval" snapshots are minutes old | **Accepted.** Guard output and press time added as a dated amendment; §7 gives the recorded-vs-decided intervals and calls the snapshots "latest before the decision" |
| R3 | MINOR | A's decoded pixels came from the 11132 parse, not this task | **Accepted.** §6.2 names the source |
| R4 | MINOR | Header and coverage verdict were stronger than the clause table's "PASS (sequential)" | **Accepted.** Qualifier carried into the header and §10; reverse direction noted as not exercised |
| R5 | MINOR | `handback-rehash.json` named no paths; `Revisions\` copies of B and C were never rehashed | **Accepted.** New dated per-path measurement `handback-rehash-paths.json`: all ten session files, each copy equal to its output's hash, source `A20A722D…77E2` |
| R6 | OBSERVATION | The shared `SessionStep` pointer moved A → B → C | **Accepted.** §6.1 now says so |
| R7 | OBSERVATION | `RetrySequence` / `RetryOfAttemptId` read correctly, but the stored rows assert a retry lineage that did not happen | **Accepted.** Recorded in §6.1 as a backlog observation; no change |
| R8 | OBSERVATION | Gate "new execution" supported by the probe time equalling the press time; the 16:09 passive stamp and the grant cannot be ordered within that minute from the files | Noted. The executor opened the screen after recording the grant; that order is executor-attested |

**Verified without defect by the reviewer:** same R for all three outputs in `PrintOutput`, attempt
input and plan source, with the same plan-source hash; every plan value, reproduced by its own
arithmetic, including physical heights; decoded B/C dimensions and strip sizes; three separate
validator notes; three decisions with correct subjects and `ReviewedSha256 = PrintOutput.Sha256 =`
output `Revision.Sha256`; B and C `NOT_REVIEWED` with no decision in their pre-approval snapshots
and in the UI; A's rows and the two earlier decisions identical from `db-00` to `c-db-05`; the Add
Another Size and 查看 behaviour against source; the three `AddAnotherSizeTests` methods, correctly
reported as inspected and not run; the reuse boundaries (no re-import, no new original review, A's
decision not repeated); and the honest statement of who judged and who pressed.

**Reviewer verdict, as delivered:** "No BLOCKER or MAJOR findings. … My verdict is PASS (sequential,
positive-path) with minor record corrections recommended."

## 10. Verdicts

| Item | Status | Reason |
|---|---|---|
| **Three-size run** | **PASS** | One reused (A) and two new (B, C) outputs; every output bound to R; distinct dimensions, attempts, validations, hashes and final decisions; no failure, no retry, no enlargement |
| **This bounded task** | **COMPLETE within its authorization** | Authority re-verified without building; one fresh gate; two ordinary Add Another Size runs; two real Operator decisions recorded before actuation; one independent read-only review, five MINOR findings accepted and corrected; handback established |
| **SCRUM-11133 coverage** | **FULL** — every clause supported at ordinary-Product level by one positive, sequential run (table below) | Not a universal proof: the isolation clause rests on this run's observations plus the engine's design, and no negative path was exercised, by the Operator's standing decision |
| **Whole-project release** | **not assessed here** | — |

| Clause | Evidence | Status |
|---|---|---|
| At least three differently sized PrintOutputs | A 275, B 200, C 100 mm | PASS |
| From one approved source Revision; each uses the same approved source | `SourceRevisionId`, attempt input and plan source all = R; R approved, valid and unchanged | PASS |
| Independent dimensions | Separate plans, projected = decoded pixels | PASS |
| Independent validation | Three attempts, each with its own validator result | PASS |
| Independent hash | Three distinct delivered hashes, each chained to its decision | PASS |
| Independent final review state | Three separate `PRINT_OUTPUT` decisions; B and C each observed NOT_REVIEWED with no decision before their own approval | PASS |
| Approving one size never approves or mutates another | Sequential observation §7; limit: never three pending simultaneously, which the Product's route does not allow | PASS (sequential) |

**Implication for SCRUM-11105** (not re-audited, online status unchanged): the reaudit marked it
PARTIAL only because this ≥3-size acceptance run had not been executed. That run now exists.

**Next planned item:** SCRUM-11134 — Maintop comparison — only after a later execution
instruction. It was not started.
