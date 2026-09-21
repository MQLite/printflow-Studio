# SCRUM-11134 — Maintop comparison against the accepted reference TIFF

Date: 21 September 2026

Dated amendment: 22 September 2026 — installed application identity and Operator display-scope clarification.

Bounded execution: **COMPLETE — available safe-preview comparison, integrity checks and isolated record review completed.**

SCRUM-11134: **PARTIAL — display/dimensions supported under Operator-attested unchanged settings;
safe preview has no independent CMYK/white view. Operator confirmed view closure; final integrity verified.**

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
Maintop remains exclusively Operator-driven. The executor does not capture, upload, reproduce or
commit restricted artwork. A later unsolicited Operator attachment in the conversation is described
with its evidence limits in section 11; it is not copied into the evidence directory or Git.
Pending and unobserved fields below are not inferred from successful composite display.

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

Exact original paths are in `operator-file-list.txt` and `measurements-before.json` in the ignored
evidence directory. Originals remain in place. On 22 September, after the current availability and
unmonitored-destination confirmation, three byte-identical copies were created; see section 10 and
`comparison-copies-20260922.json` for the active comparison paths and source/copy hash bindings.

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

The 22 September amendment resolves installed executable/path/version using local file metadata.
The Operator has now positively attested that the ordinary route is layout preview only and does
not automatically enqueue, send or print; exact menu captions were not supplied and are not invented.
The Operator also renewed the current saved-work/exclusive/no-competing-automation confirmation and
confirmed the exact copy destination is not a hot folder or output destination. These are Operator
attestations, not independent inspection of application/queue configuration. Copies are now verified.

The Operator subsequently confirmed settings remained unchanged between the three samples, CMYK
color is displayed, white ink cannot be viewed, and there is no independent separation view.
Specific profile/color/white identifiers have not been supplied or independently read. The current
fixed environment is therefore identified by its running binary and the Operator's ordinary DTF
configuration/unchanged-setting attestations, not by independently inspected settings. Manual
comparison instructions have been delivered; no GUI control or
application launch has been sent by the executor. See section 10 for the later dated checkpoint.

## 6. One comparison matrix

| ID | Executor measurement | Current Maintop import/render | Displayed image dimensions | CMYK interpretation | W1 interpretation | Operator usability | Warning / evidence level |
|---|---|---|---|---|---|---|---|
| REF | Hash/IFD PASS; accepted-reference identity verified; copy hash matches | Operator: displays correctly | **279.9926 × 378.7986 mm**, numeric crop 3; Operator confirms correct | Operator: CMYK color display; no independent separation view | NOT OBSERVED / PARTIAL — safe preview does not expose white ink | Correct layout display, per Operator; ink-specific readiness not established | Warnings not specifically reported; settings unchanged per Operator |
| G1 | Hash/IFD PASS; original approval/lineage verified; copy hash matches | Operator: displays correctly | **199.9826 × 300.0586 mm**, numeric crop 1; Operator confirms correct | Operator: CMYK color display; no independent separation view | NOT OBSERVED / PARTIAL — safe preview does not expose white ink | Correct layout display, per Operator; ink-specific readiness not established | Warnings not specifically reported; settings unchanged per Operator |
| G2 | Hash/IFD PASS; original approval/lineage verified; copy hash matches | Operator: displays correctly | **50.8 × 76.2 mm**, numeric crop 2; Operator confirms correct | Operator: CMYK color display; no independent separation view | NOT OBSERVED / PARTIAL — safe preview does not expose white ink | Correct layout display, per Operator; ink-specific readiness not established | Warnings not specifically reported; settings unchanged per Operator |

Numeric crops contain object-size controls but no filenames. Their REF/G1/G2 mapping is inferred
from the unique geometry and the object-bound Operator comparison, not independently read from
file labels. The displayed four-decimal REF/G1 values match truncation of the file-derived values
to four decimals; the application's internal rounding rule is not independently documented.
No arbitrary tolerance, resizing correction or pixel-equality requirement is introduced.

The comparison protocol required REF first, then G1, then G2, with the existing configuration
kept unchanged. The request named the loaded objects, import/warnings, image sizes/units, actual
safe CMYK/white-preview facilities and ink regions, and per-object usability. The evidence above
records only what was supplied. Composite rendering proves neither individual CMYK separations
nor W1 interpretation. The unavailable white-view claim stays NOT OBSERVED/PARTIAL without starting
a RIP or physical print. Different artwork is not a pixel-equality oracle, and no arbitrary
numerical tolerance is introduced.

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
perform the comparison, or verify the then-pending no-output route. Its preparation review cannot
supply any missing Operator observation or prove physical-print quality.

| Finding | Severity | Disposition |
|---|---|---|
| `MODEL_SWITCH_UNAVAILABLE` overstated capability evidence when no switch had been attempted | Moderate | Accepted; removed the marker and distinguished intended route from unverified actual execution |
| “Independently decode” could misattribute the main executor's TIFF metadata measurement to the reviewer | Low | Accepted; explicitly credited the main executor's first-IFD measurement |
| Current environment/route and actual comparison results were missing at the initial review | Expected blocker | Preserved at that checkpoint; later attestations/observations are recorded in sections 10–11; white/separation limits remain |

The reviewer confirmed the final accepted reference identity, the two complementary sample
selections, their original reviewed-byte bindings, integer-pixel geometry and the separation of
metadata from Maintop behavior. No additional TIFF/sample branch or physical-print requirement
was found necessary. Its verdict is limited to a reviewable **preparation record**, not a passed
Maintop comparison. See `independent-review.md` for the transcribed findings and limits.

**Preparation checkpoint integrity:** the 17:29:07 +12:00 read-only remeasurement matches the
17:21:11 baseline for all selected files, sources, all three 11133 approvals/outputs, both sessions'
seven tables, retained authority and candidate fingerprint. No active attempt and no lease owner
were recorded at that instant. This is a preparation checkpoint, **not** post-comparison evidence.

**Post-comparison integrity verified:** after the Operator display/dimension observations and
numeric crops, `measurements-after-comparison-20260922.json` confirms protected originals, source
files, all three 11133 outputs/approvals, both sessions' seven tables, retained authority and the
candidate fingerprint unchanged. `comparison-copy-integrity-after-20260922.json` separately
records each copy and its original still matching the pre-copy accepted hash. These are main
executor measurements; they do not establish view closure or unobserved Operator actions.

**Handback:** the Operator initially retained the view for later closure, then explicitly replied
**“已关闭”** (closed) to the existing comparison-view handback request. Closure is Operator-attested,
not independently observed; no claim is made that the whole application exited or that a particular
UI command was used. After that reply, `measurements-handback-closed-20260922.json` and
`comparison-copy-integrity-closed-20260922.json` again verified all protected files/records/authority
and the three copies unchanged. Copies remain local and retained. No force-close, cleanup or
monitoring was performed. No app was opened, focused, modified or closed by the executor, and no
TIFF processing or new Product live gate occurred.

| Outcome | Current status |
|---|---|
| Observed Maintop comparison | PARTIAL — all three correctly display per Operator; numerical object sizes align at shown precision; settings unchanged per Operator; no independent CMYK/white view available |
| This bounded task | COMPLETE — available safe-preview comparison, integrity checks and isolated evidence review completed. Operator confirmed comparison view closed; subsequent integrity verified |
| SCRUM-11134 clauses | PARTIAL — Operator display/dimension support; no separately established current CMYK/W1 interpretation |
| Whole-project release | NOT ASSESSED |
| Physical-print quality | NOT ASSESSED — no physical-print test requested or evidenced; Operator's printing statement is general experience only |

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
| `operator-safe-route-20260922.json` / `operator-current-window-20260922.json` | Actual positive route attestation and current availability/unmonitored-destination confirmation |
| `measurements-resume-20260922.json` / `comparison-copies-20260922.json` | Unchanged protected records at resumption; exact original/copy identities and verified SHA-256 bindings |
| `manual-comparison-request-20260922.json` / `operator-comparison-guide-20260922.txt` | Object-bound observation request and exact three-copy manual guide |
| `maintop-running-readback-20260922.json` | Later passive running-process identity and binary-version readback; no UI/object/settings/queue observation |
| `operator-observations-20260922.json` | Object-bound actual Operator replies, support/limits, attachment handling and recording time |
| `dimension-readings-20260922.json` / `dimension-crop-1.png` to `dimension-crop-3.png` | Operator-supplied numeric-only crops, unchanged local copies/hashes, exact readings and identity-mapping limits; no artwork |
| `measurements-after-comparison-20260922.json` / `comparison-copy-integrity-after-20260922.json` | Post-observation original/source/approval/authority/copy integrity; not evidence of UI closure |
| `measurements-handback-closed-20260922.json` / `comparison-copy-integrity-closed-20260922.json` | Final unchanged-file/record/copy checks after the later Operator close confirmation; main executor measurements |

Only this English report is included in the scoped local documentation commits. Raw evidence stays
ignored. Start HEAD is recorded in section 2; checkpoint commits are recorded in `git-final.json`
and `git-amendment-20260922.json`; final comparison end HEAD/local commit is recorded after commit
in `git-comparison-final-20260922.json` and the user handback (the report cannot contain its own hash).
The subsequent close-confirmation documentation commit is recorded in `git-closure-final-20260922.json`.
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

At this early 22 September checkpoint, the ordinary nonprinting route and positive
no-automatic-enqueue/send/print basis were still unprovided. No three-file comparison had yet
been reported, and the matrix then remained NOT OBSERVED. The 21 September availability reply
was retained without being silently redated. Later current confirmation and actual observations
supersede these pending items in sections 10–11; the early version amendment itself requested no live action.

At the time of this version amendment, section 7's review covered only 21 September preparation.
The same reviewer subsequently reviewed the later records as described below; no new reviewer or
acceptance replay was started. Whole-project release and physical-print quality remain NOT
ASSESSED; SCRUM-11135 is not started.

## 10. Later 22 September checkpoint — safe route and manual dispatch

This later checkpoint supersedes the pending route/copy-position items in section 9 without
backdating that earlier amendment. The actual answer to the safe-route question was:
**“仅排版预览，不自动入队发送或打印”** (layout preview only; no automatic enqueue, send or print).
The next combined question asked whether the saved-work/exclusive/no-competing-automation window
was still valid after the date change and whether the exact proposed copy directory was neither
a hot folder nor an output directory. The Operator answered **“两项均确认”** (both confirmed).
No repeated per-file availability or Product approval is requested.

The resumed read-only measurement found the protected files, both sessions' seven tables,
retained authority and fingerprint unchanged from the original preparation baseline. Only REF,
G1 and G2 were copied using non-overwriting `File.Copy`, with each original measured before and
after and each copy measured after copying. All three copy hashes equal their accepted originals.
No image conversion, new raster generation, resave, channel remapping or source rename occurred.

| ID | Local comparison copy | Expected image mm at 300 PPI |
|---|---|---|
| REF | `QA/SCRUM-11134/11134-codex-20260922/input-copies/REF.tif` | 279.9926667 × 378.7986667 |
| G1 | `QA/SCRUM-11134/11134-codex-20260922/input-copies/G1.tif` | 199.9826667 × 300.0586667 |
| G2 | `QA/SCRUM-11134/11134-codex-20260922/input-copies/G2.tif` | 50.8 × 76.2 |

Paths above are relative to the existing PrintFlow workspace; exact absolute paths, byte lengths
and full hashes are in the ignored copy manifest and the Operator guide. Copies inherit the
originals' local-only restrictions and are retained, not swept or sent to model vision.

The Operator received all three paths and expected sizes together, with instructions to view REF
first, then G1/G2, keep the existing layout-preview settings unchanged, and report whether they
remained unchanged. That observation was pending at dispatch and was later supplied in section 11.
The request named each object and
asks for import/warnings, complete artwork, actual displayed image dimensions and units,
available CMYK/white-preview mechanism and ink regions, per-file usability, current visible
configuration names, and unchanged settings. No screenshot of restricted artwork was requested.
Unavailable fields must be recorded as not shown; screen-display deviations are excluded under
the Operator's instruction. Format/actual dimension errors should stop the affected comparison.

The route and unmonitored-directory facts are positively Operator-attested. No independent
Maintop UI observation or menu-caption discovery is claimed. At dispatch, the matrix was left
NOT OBSERVED pending object-bound answers, and post-comparison integrity was still outstanding.
The later observations and integrity results are recorded in sections 6–7 and 11, without backdating
them into the dispatch record.

A later passive process readback at **10:02:25 +12:00** identified running `dtpw.exe`, PID **22100**, at the measured
`D:\MainTop\DTP\dtpw.exe`, ProductVersion **6.1.42**, FileVersion **6.1.42.2506**. This strengthens
installed-file identity to the actual running binary at that recorded instant. No window title,
image, loaded file, profile, queue or Maintop UI result was read, and none is inferred from the PID.

**Scoped review of this amendment:** the same isolated read-only reviewer resumed without receiving
parent history or becoming a workstation operator. It independently hashed/measured the three new
copies and read PE VersionInfo for the two installed executables; those measurements agree with the
record. It accepted the attribution of the route, availability, destination and display-scope facts
to Operator attestation. It found the potentially ambiguous unchanged-setting wording above
(Moderate), corrected to an instruction whose observation was then pending, and requested the timestamped
running-process note (Minor), now included. No additional acceptance replay was performed. The
reviewer did not observe any preview/settings/queue/UI result, query live SQLite, decode image/IFD
data, rerun VerifyOnly/tests/builds or change any state. Actual model/effort remains UNVERIFIED;
requested route remained Sol High, offset 0. Findings are appended in `independent-review.md`.

## 11. Subsequent Operator observations — 22 September 2026

The request explicitly named REF, G1 and G2, their verified copy paths and each sample's expected
geometry. The Operator answered **“都能正确显示”** (all display correctly), then **“尺寸也正确”**
(dimensions are also correct). These responses are collectively object-bound to the named three
samples and support successful layout display and the Operator's dimensional check. They are
not additional Product approvals and create no ReviewDecision.

The initial text reply supplied no exact displayed width/height/units; these arrived later in the
numeric-only crops described below. Section 4's numerical dimensions remain file-derived
measurements, distinct from section 6's displayed values. Warning absence,
individual CMYK separations, W1 interpretation/regions, unchanged configuration and production
readiness are not inferred from "correctly displays". The Operator was asked only for the remaining
unchanged-setting and safe separation-view availability facts, plus whether the temporary comparison
view was safely closed without saving or retained for inspection. No repeat dimensional approval is
requested, and unavailable white/separation facilities must remain NOT OBSERVED.

The Operator voluntarily supplied a composite layout screenshot in the conversation alongside the
dimensional statement. It contains no numeric image-size readout, loaded-file labels or separate
ink-view controls; it cannot independently establish the file identities, dimensions or W1 behavior.
The executor did not request or capture that screenshot, did not copy it into local evidence or Git,
and did not re-open it with a tool, repost it or use it as quantitative/separation evidence. The
standing no-agent-upload/no-restricted-artwork-publication boundary remains in force.

The subsequent actual replies were **“不可以查看白墨，只显示CMYK色，但打印时会有白墨。设置保持不变，尺寸稍后发送截图”**
(white ink cannot be viewed; only CMYK color is shown; white ink appears when printing; settings
unchanged; numerical-size screenshot to follow), **“没有独立视图”** (no independent view), and
**“稍后关闭”** (will close later). These establish unchanged settings and the safe preview's
explicit limitation. Neither CMYK ink separations nor W1 interpretation/regions are exposed in
this route, so those claims remain NOT OBSERVED/PARTIAL; no RIP/output action is sought to fill them.
The statement about white ink when printing is the Operator's general production account, not a
new physical-print test or an observed result for these copies. Screen-display deviations remain
excluded as directed. The comparison view remains retained by the Operator at this checkpoint.

Recording times and verbatim answers are retained in `operator-observations-20260922.json`; actual
import instants and attachment capture time are unknown and are not reconstructed. After being
offered text or numeric-only crops, the Operator supplied three crops containing width/height
controls, units, position and zero rotation/skew controls, with no artwork/customer identifiers.
Those three numeric-only files alone were copied unchanged into ignored local evidence and hashed;
the earlier artwork-containing screenshot was not copied or reposted.

The readings are G1 **199.9826 × 300.0586 mm**, G2 **50.8 × 76.2 mm**, REF **279.9926 × 378.7986 mm**.
The crop-to-file mapping follows their unique geometry plus the Operator's named comparison; the
crops do not independently show loaded-file labels. REF/G1 differ from exact pixel/PPI-derived
geometry by 0.0000667 mm at each displayed edge, consistent with four-decimal truncation; the
display formatting implementation itself is not proven. G2 matches its exact decimal geometry.
This supports the dimensional comparison at the displayed precision, not a physical-print size
measurement. No screen-display difference is treated as a TIFF defect under the Operator's scope.

At the final record-review checkpoint, integrity was verified and view closure was still unconfirmed
under the Operator's express "later" decision; the subsequent close confirmation is recorded below
and in section 7. The same isolated reviewer completed the final affected-scope
review: it opened only the three numeric-only crops, confirmed the exact visible size readings,
and independently hashed those crop files against the retained manifest. This is independent
review of Operator-provided UI records, not observation of the live UI or proof of loaded filenames.
It did not access the earlier artwork screenshot, independently rehash originals/copies after
comparison, query live SQLite, inspect settings/queues, print, or mutate state. Post-comparison
unchanged-file/record/lease facts remain the main executor's saved measurements.

The final review accepted the matrix, attribution, inferred crop bindings, unavailable ink views,
retained-view handback and PARTIAL verdict. Three wording findings were accepted: describe the
protocol in past tense (Minor), replace "independently records" with "separately records" for the
main executor's second hash record (Low), and add the explicit physical-print-quality NOT ASSESSED
row (Low). Review-pending labels were then finalized. No further measurement replay was warranted
by these documentation-only corrections. Unavailable white/separation views remain the substantive
AC limitation, with current profile identifiers and direct UI filename bindings also unrecorded.

**Later closure amendment:** after the final record review and initial comparison-results commit,
the Operator supplied **“已关闭”**. The reply is appended with its actual recording time in the
observation record. Section 7 now reflects Operator-confirmed closure and the successful subsequent
main-executor integrity checks. This later closure statement/check was not independently observed
or remeasured by the reviewer; no new review or acceptance replay was warranted by this narrow
handback update. No historical observation or review finding has been backdated or replaced.
