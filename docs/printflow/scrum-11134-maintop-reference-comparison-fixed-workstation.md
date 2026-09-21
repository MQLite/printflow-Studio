# SCRUM-11134 — Maintop comparison against the accepted reference TIFF

Date: 21 September 2026

Dated amendment: 22 September 2026 — installed application identity and Operator display-scope clarification.

Status: **PARTIALLY COMPLETE — preparation verified; manual comparison awaits required Operator route details.**

Raw local evidence (ignored): `artifacts/scrum-11134/11134-codex-20260921-1719/`.
This report concerns the existing accepted reference and two existing approved outputs only.

## 1. Authority and execution boundary

Prompt 30 was explicitly submitted for execution. The retained Jira CSV row **11710**, matched
by the title `Validate PrintFlow TIFFs in the Current Maintop Environment`, supplies the original
SCRUM-11134 criterion:

> Import representative PrintFlow-generated TIFFs into the current validated Maintop environment
> and compare behaviour with the proven reference TIFF. Verify current-production CMYK and
> white-ink behaviour, dimensions and usability while explicitly limiting conclusions to this
> fixed environment. Do not automate Maintop or RIP control in the MVP.

The original coverage reaudit's SCRUM-11134 row and P3-4 identify the missing representative
comparison. Historical Epic 11000 reference acceptance and Epic 11400 final-gate section 12
(one different generated TIFF imported in Maintop v6.1) are retained historical evidence.
Neither establishes today's settings or substitutes for this comparison.

No new TIFF, Product approval, Product live gate, build, restore, qualification, publication,
live negative check, printer action, queue submission or SCRUM-11135 work is authorized here.
Maintop remains exclusively Operator-driven. Restricted artwork is never decoded for model
vision, uploaded or committed. All pending observations below are explicitly unperformed.

## 2. Receiver and routing

| Field | Record |
|---|---|
| Executor | Codex Desktop on the local Windows workstation, identified by the trusted execution context; no Claude process was launched |
| Mode | EXECUTE_HANDOFF |
| Native policy | `C:\Users\admin\.codex\workflows\development-routing.md`, v2.3, loaded through the native global AGENTS entry; the process has no CODEX_HOME override |
| Initial ExecutionTarget | gpt-6-astra / high, for acceptance interpretation and production boundary assessment |
| Post-preparation NormalRoute / RequestedRoute | gpt-5.6-sol / high, for bounded provenance interpretation and manual-comparison coordination |
| RouteOffset / AdjustmentResult | 0 (default) / UNCHANGED |
| ActualModel / ActualEffort | UNVERIFIED / UNVERIFIED — no exact current main-session execution metadata is exposed; configuration and requested targets are not execution proof |
| Main-session switching | Not attempted. The exposed controls do not provide an in-place current-turn model switch; no downgrade or actual model change is claimed. The post-preparation route above is a recommendation, not verified execution |
| Context | CONTINUE; one isolated read-only reviewer is required at the evidence review boundary |
| Checkout | Existing master; start HEAD `59c2284285251d80ce57206918ddd67649e0a0ee`; initially clean tracked worktree |
| Workspaces | Existing repository and fixed PrintFlow workspace; no branch/worktree/clone/reset/clean/rebase |

## 3. Producing authority, remeasured without a live gate

The qualification result's own `Binding.BuildOrigin` resolved the original receipt. The existing
`tools/regression/New-PrintFlowBuildPair.ps1 -VerifyOnly -ReceiptPath <resolved receipt>` returned
`Build pair verified: d915b1a6-4c06-4b16-9a20-5e1341d1ae9c`, exit 0.

| Authority | SHA-256 / identity, freshly verified |
|---|---|
| Qualification run | `a2-v3-20260917-154736-d915b1a6`, Passed |
| Result | `4083F1FB7F1BDA4D9B4F6A0EE7C9F95B8A4CF891087956F845D0B2CDDC5FDA1A` |
| Original receipt | `52E6DBC5E894EA95CB859CF9CF0BB0D6D42533B17AE26D90DAC0EB41E8DE8FBC` |
| Active record | `E6A7D7EAA9C370AFACF0769B927B4D5AD735ADAC8203B614F3EE9320D975F3B9` |
| Preset | `printflow-workstation-v1` / `1.18.0`; `8484F0AA18728FAC58B58ACD8E811C7058743B61271506647179CA0D0A6C8E0F` |
| Candidate fingerprint | `13A73BEFAE17B18E61E8B975CF030E7156315E9AEBDE661E391550C52BEEDD5F`, recomputed using the Product canonicalization; all four candidate assemblies match the active record |

This is existing-output provenance, not a new current PrintFlow readiness or Maintop gate.

## 4. Exact representative objects and file measurements

Exact local paths are in `operator-file-list.txt` and `measurements-before.json` in the ignored
evidence directory. The file list is preparation, not an instruction to import before the route
is verified. Originals remain in place. No comparison copies have been created.

| ID | Identity | Bytes | Measured pixels @ 300 PPI | Derived image dimensions, mm |
|---|---|---:|---|---|
| REF | v3 `FIX-REFERENCE-TIFF-001`, byte-identical to the accepted baseline artifact | 116,992,344 | 3307 × 4474 | 279.9926667 × 378.7986667 |
| G1 | 11133 output B; `01a0c22c-79c8-7bf5-94f5-9dc05afe5591`; requested WIDTH 200 mm; W1_2PX | 57,030,044 | 2362 × 3544 | 199.9826667 × 300.0586667 |
| G2 | 11131 output; `01a0c19b-7fb4-7c9a-a427-23f73fc3045b`; W1_1PX | 5,391,052 | 600 × 900 | 50.8 × 76.2 |

| ID | Actual SHA-256 |
|---|---|
| REF | `D1E69C4108D4C1D6119DB11DE036F56555CDE4A064F23AF541E24E1DAC5EA412` |
| G1 | `D522483F6143527279F9B5CB56897D19AC2B76D01D045AAFA6E4486A01C09569` |
| G2 | `1DD555E877A5B0C8F09A5D06D0EF1C4037070650F393818A33842875A2900A5D` |

The main executor's scoped preparation measurement read each file's first-IFD scalars as little-endian, uncompressed,
separated (`PhotometricInterpretation=5`), five interleaved 8-bit samples, 300 PPI and
`ExtraSamples=[0]`. No raster pixels were decoded. Accepted reference records and retained
successful Product validator notes establish the named spot W1 structure. **None of these
metadata facts establishes Maintop's actual white-ink interpretation.**

REF was resolved through the current preset's `tiffContract.referenceArtifact`, the actual v3
REFERENCE_PRODUCTION_TIFF manifest and the final 11007 signoff. Both the archived baseline bytes
and the v3 reference bytes match the accepted full hash. Signoffs 11004 and 11007 also match their
accepted hashes. The accepted evidence is the 18 August 2026 manual load/preview and Operator
acceptance of default W1 handling; no separate physical print was evidenced. The early planning
candidate was not substituted. The historical **600 × 900 mm is layout size**, not REF image size.

| Binding | G1 | G2 |
|---|---|---|
| Source Revision | `01a0c1f4-c6b4-7b6b-a502-6eadd5689089` | `01a0c196-b416-79b4-9987-af3c20c2cca4` |
| Source hash | `A20A722DB394B8CBBAE7975CC930DD456971E913E5844C21F34B27B9C4D377E2` | `8A7063D8F81FB72A1DB7F5633980660905E2A6C3D3971E4F94A138B5C8394879` |
| Original final review | `01a0c232-9b9c-75e8-ae90-51c2bc6e9984` | `01a0c1a4-971a-71b8-9af6-154bdf057c33` |
| Successful attempt | `01a0c22c-373e-7fc5-b0e8-6bf63451d539` | `01a0c19b-3849-76ac-be76-a106e06a5eaa` |

For both, current read-only database rows satisfy final review APPROVED, output APPROVED/valid,
`ReviewedSha256 = PrintOutput.Sha256 = output Revision.Sha256 = measured delivered bytes`, and a
SUCCEEDED attempt with `photoshop-tiff-validation-c1-v1` notes. Seven tables for each selected
session match the respective retained completion readback field for field. Source files and all
three 11133 approved TIFFs were rehashed. There was no Product action or repeated approval.

G1's transparent source margins and G2's complete opaque design supply the two intended coverage
cases. W1_1PX/W1_2PX are underbase-generation branches of the same spot channel W1. They do not
describe different spot-channel names. G1's integer-pixel rounding and G2's rounded `51mm`
filename must not be confused with stretching or the physical-dimension authority.

## 5. Current availability, environment and safe-route gate

One current saved-work/exclusive-availability question was asked for REF → G1 → G2, preserving
customer work/queues, excluding competing automation, and requiring no enqueue/send/print or
production-setting changes. The Operator answered **“确认”**. The exact question/answer is retained
with a recording timestamp in `operator-events.json`; it is reused while valid, not repeated per
file. The reply confirms availability and the agreed boundary; it does **not** establish the
behavior of an unidentified import route.

At the 17:21:11 +12:00 measurement, no ProcessingAttempt had a null EndedAtUtc, and the canonical
lease OwnerToken was null. These are momentary read-only observations, not a Maintop lock or
proof of future exclusivity. Current process-name/path searches did not identify Maintop;
that search alone does not prove the application is absent or establish its version.

The 22 September amendment below resolves the installed executable/path/version using local file
metadata. **PENDING:** existing profile/color/white identifiers and consistency with the accepted
environment; exact ordinary document/import-preview route; positive
evidence that it cannot automatically enqueue, send or print; protection choice for originals
(demonstrably read-only direct import, or byte-identical copies in a positively unmonitored path).
The initial version question is superseded by the executor's read-only measurement. The exact
route and its no-output basis remain unanswered.
No import instruction has been released and no GUI control or application launch has been sent.

## 6. One comparison matrix

| ID | Executor measurement | Current Maintop import/render | Displayed image dimensions | CMYK interpretation | W1 interpretation | Operator usability | Warning / evidence level |
|---|---|---|---|---|---|---|---|
| REF | Hash/IFD PASS; accepted-reference identity verified | NOT OBSERVED | NOT OBSERVED | NOT OBSERVED | NOT OBSERVED | NOT OBSERVED | Awaiting verified route; historical acceptance is separate |
| G1 | Hash/IFD PASS; original approval/lineage verified | NOT OBSERVED | NOT OBSERVED | NOT OBSERVED | NOT OBSERVED | NOT OBSERVED | Awaiting verified route and object-bound observations |
| G2 | Hash/IFD PASS; original approval/lineage verified | NOT OBSERVED | NOT OBSERVED | NOT OBSERVED | NOT OBSERVED | NOT OBSERVED | Awaiting verified route and object-bound observations |

After the route is established, the Operator compares REF first, then G1, then G2 in the same
unchanged environment. Record the actual loaded object, import/warnings, image size with units,
available CMYK/separation facility, actual safe white-preview facility and intended ink regions,
and per-object usability judgement at the time of the answer. Composite rendering alone proves
neither individual CMYK separations nor W1 interpretation. If the safe UI exposes no white
mechanism, that clause stays NOT OBSERVED/PARTIAL without starting a RIP or physical print.
Different artwork is not a pixel-equality oracle. There is no invented numerical tolerance.

## 7. Review, handback and outcomes

**Independent read-only preparation review completed.** One native child agent,
`independent_11134_review`, was launched with `fork_turns=none`, the original criterion, Prompt 30,
this report and navigable primary evidence. It received no parent conversation history and had no
workstation-operator role. Requested route: `gpt-5.6-sol/high`, RouteOffset 0, native policy v2.3;
actual model/effort: **UNVERIFIED**, as reported by the reviewer. No second reviewer was used.

The reviewer independently measured SHA-256 and lengths of the archived reference, v3 reference
copy, G1 and G2, and checked reference/preset/signoff hashes. Its readings agreed with the main
executor's. It compared the acceptance, lineage and validation records against the original
criterion. It did **not** query live SQLite: seven-table equality and current-row claims were
reviewed through the main executor's saved readback and read-only measurement method. It did
not rerun VerifyOnly. It did **not** decode pixels, independently parse the TIFF IFDs, observe Maintop,
perform the comparison, or verify the pending no-output route. Its preparation review cannot
supply any missing Operator observation or prove physical-print quality.

| Finding | Severity | Disposition |
|---|---|---|
| `MODEL_SWITCH_UNAVAILABLE` overstated capability evidence when no switch had been attempted | Moderate | Accepted; removed the marker and distinguished intended route from unverified actual execution |
| “Independently decode” could misattribute the main executor's TIFF metadata measurement to the reviewer | Low | Accepted; explicitly credited the main executor's first-IFD measurement |
| Current environment/route and actual comparison results remain missing | Expected blocker | Preserved as pending; no import or acceptance invented |

The reviewer confirmed the final accepted reference identity, the two complementary sample
selections, their original reviewed-byte bindings, integer-pixel geometry and the separation of
metadata from Maintop behavior. No additional TIFF/sample branch or physical-print requirement
was found necessary. Its verdict is limited to a reviewable **preparation record**, not a passed
Maintop comparison. See `independent-review.md` for the transcribed findings and limits.

**Preparation checkpoint integrity:** the 17:29:07 +12:00 read-only remeasurement matches the
17:21:11 baseline for all selected files, sources, all three 11133 approvals/outputs, both sessions'
seven tables, retained authority and candidate fingerprint. No active attempt and no lease owner
were recorded at that instant. This is a preparation checkpoint, **not** post-comparison evidence.

Actual post-comparison handback remains **PENDING** because no comparison occurred. No app was
opened, focused, modified or closed by the executor; no temporary comparison view or input copy
was created. After any eventual observations, remeasure originals/copies and approval bindings.
Only this comparison's temporary views may be closed by the Operator without saving when safely
distinguishable from unrelated work. No new live gate is needed.

| Outcome | Current status |
|---|---|
| Observed Maintop comparison | NOT RUN — safe route/current environment pending |
| This bounded task | PARTIALLY COMPLETE — preparation verified, Operator-driven comparison pending |
| SCRUM-11134 clauses | PARTIAL — no new current Maintop import/dimension/CMYK/W1/usability evidence yet |
| Whole-project release | NOT ASSESSED |

Prior A2/A3/11130–11133 results remain unchanged. SCRUM-11135 is not started. Future 11136/11137
metrics must distinguish additional sizes from retries despite the retained RetrySequence fields;
no metric or schema work is performed here.

## 8. Evidence index and Git state

| Local artifact | Purpose |
|---|---|
| `measure.py` | Scoped read-only hash, IFD and SQLite measurement method; no UI or raster decoding |
| `measurements-before.json` | Before hashes, source/output/approval rows, historical comparisons, authority and momentary lease read |
| `measurements-checkpoint.json` | Fresh preparation-checkpoint readback; all protected file/record/authority comparisons unchanged |
| `verify-only.txt` | Original pair verification output |
| `operator-file-list.txt` | Exact three local file paths, expected dimensions and staged manual observation checklist |
| `operator-events.json` | Actual user questions/answers and their recording times, distinct from future pending observations |
| `process-observation.json` | Passive filtered process names/paths, without artwork, screenshots or window titles |
| `historical-reference-index.json` / `reference-save-evidence-integrity.json` | Local reference evidence hashes; referenced TIFF-save runtime evidence matches the current preset |
| `checkpoint.json` | Exact pending Operator information and the zero-action checkpoint |
| `independent-review.md` | Isolated review findings, independent hash measurements, record-review boundaries and dispositions |
| `maintop-installation-readback-20260922.json` / `maintop-version-observation-20260922.json` | Existing shortcut targets, installed PE versions, executable hashes and passive process readback |
| `operator-amendment-20260922.json` | Dated verbatim Operator statement and its bounded disposition |

Only this English report is included in the scoped local documentation commit. Raw evidence stays
ignored. Start HEAD is recorded in section 2; the end HEAD/local commit is recorded after commit
in ignored `git-final.json` and in the user handback (the report cannot contain its own commit hash).
No push, Jira write, release or publication.

## 9. Dated amendment — 22 September 2026

The Operator requested that the executor read the Maintop version locally, stated that the installed
version is suitable for DTF and current for that purpose, and directed the comparison to disregard
screen-display deviations, stating that these deviations are absent in physical output.

The executor read the existing desktop shortcuts without executing them, then read the target files'
PE version resources and hashes. No Maintop/Print Manager process was launched or controlled.

| Installed component | Actual executable | Product version | File version |
|---|---|---|---|
| MainTop DTP | `D:\MainTop\DTP\dtpw.exe` | `6.1.42` | `6.1.42.2506` |
| MainTop Print Manager | `D:\MainTop\MON\mt_mon.exe` | `6.0.18.f538b3eab6cbf0eca9924aaf178c639382540b86` | Same as product version |

Both target files exist. No running `dtpw.exe` or `mt_mon.exe` was returned by the passive process-name
readback at the recorded time. This establishes installed binary identity, not a running UI's edition,
active driver/profile, queue settings, or import behavior. The DTP major/minor version is consistent
with the historical v6.1 label; the baseline did not capture an executable hash, so no historical
binary-equality claim is possible or required. No online "latest release" claim was verified, and no
upgrade/version dispute or new qualification requirement is introduced.

**Operator scope clarification:** screen-display deviations are not an output defect criterion in this
comparison. Record any such reported deviation as display-only/Operator-accepted rather than using it
to fail the TIFF or request color/profile changes. Continue to distinguish actual image dimensions,
import success, available ink/separation interpretation and usability. The statement about printed
results remains the Operator's general account, not a newly observed physical print and not an
object-bound acceptance of REF/G1/G2. It does not establish W1 handling from TIFF metadata.

The exact ordinary nonprinting route and positive no-automatic-enqueue/send/print basis remain
unprovided. No three-file comparison was performed, and the matrix remains NOT OBSERVED. The single
21 September saved-work/exclusive-use confirmation is retained as given; it is not silently
redated into a 22 September live-use window. There is no new live-action request in this amendment.

The independent review in section 7 covered the 21 September preparation. This later executable
measurement and Operator statement have **not** been independently remeasured or reviewed. No new
reviewer or acceptance replay was started. Preparation/SCRUM-11134 remain PARTIAL; whole-project
release and physical-print quality remain NOT ASSESSED; SCRUM-11135 is not started.
