# SCRUM-11122 — Explicit Diagnostic Package Export Completion

Date: 9 September 2026
Repository: `D:\Repositories\printflow-Studio`
Canonical branch: `master`
Starting HEAD: `57e3282574a35e4318299b78baa2e58efecc154d`
Result: **SCRUM-11122 FULL**; parent **SCRUM-11115 remains PARTIAL** for SCRUM-11123 only.

## 1. Exact original Jira authority

The source of truth was reread from
`C:\Users\admin\Downloads\printflow_studio_mvp_jira_epics_tasks.csv` before Product edits and
again before the final reassessment.

### SCRUM-11122 — Work Item 11607

> Allow the operator to manually export a diagnostic package only after showing exactly which
> screenshots, logs, local paths and metadata will be included and receiving explicit
> confirmation. No diagnostic content or customer imagery may upload automatically; export is a
> deliberate local action suitable for later support sharing.

### Parent SCRUM-11115 — Work Item 11600

> Complete the production-facing PrintFlow Studio experience with the confirmed Home/Drop,
> workflow, review, dimensions, TIFF review, Recent Processing, Settings/Environment Check and
> Error Details surfaces; simplified Chinese default and English switchable without restart;
> local-only logs and screenshots; explicit diagnostic-package export; and a repeatable versioned
> offline installer with no automatic updates. Operator-facing UI must use practical production
> terminology and hide internal implementation terms such as Revision, Session and Adapter while
> preserving stable internal English states and error codes.

### Remaining child SCRUM-11123 — Work Item 11608

> Create a repeatable versioned offline installer for the validated workstation with no automatic
> application update mechanism. Document installation, configuration and rollback expectations.
> Any PrintFlow, Windows, Meitu or Photoshop upgrade must require rerunning the standard test set
> before production use rather than silently changing the supported environment.

No audit prose was used as a substitute for these rows.

## 2. Pre-change matrix

| Original AC clause | Current Product capability before this slice | Existing authority | Already satisfied? | Remaining gap before this slice |
|---|---|---|---|---|
| Operator manually exports a diagnostic package | No package plan, writer, picker or export command | Error Details was the narrow diagnostic entry point | No | Add one exact-attempt export path |
| Show exactly which screenshots, logs, local paths and metadata will be included | Error Details showed one failure's screenshot/path and structured facts, but no package preview | Exact `SessionId + AttemptId`, `AutomationLogEntry`, diagnostic locations and readiness report | Facts only | Create one typed plan and display every disposition |
| Receive explicit confirmation before export | No export confirmation existed | Existing WPF command/UIA conventions | No | Deliberate Save action after preview |
| No diagnostic content or customer imagery uploads automatically | Existing diagnostics stayed local and no uploader existed | SCRUM-11121 local evidence boundary | Yes | Preserve structurally and state it in both languages |
| Export is a deliberate local action | No export existed | Existing owned Windows file-dialog pattern | No | Owned Save dialog and operator-selected local ZIP |
| Suitable for later support sharing | Exact failure facts and local captures existed; retained rows/captures could expire | `ProcessingAttempt`, `AutomationLogEntry`, failure context, readiness | Partial | Validated manifest and available owned failure capture |
| Parent: explicit diagnostic-package export | No operator-reachable export | SCRUM-11115 | No | Complete this child without installer work |

## 3. Current source inventory and classification

The implementation was designed from the current source authorities for SCRUM-11120 Error Details,
SCRUM-11121 retention, `AutomationLogEntry`, `GdiWindowEvidenceSink`, Settings diagnostic locations,
the workspace, Revisions, reviews and PrintOutputs.

| Candidate | Classification | Package representation and rationale |
|---|---|---|
| `manifest.txt` | **INCLUDE** | One UTF-8 human-readable authority for the subject, failure, log identity, paths, environment, content dispositions and local-only statement |
| Exact `ProcessingAttempt` failure facts | **INCLUDE_AS_METADATA_ONLY** | Stable code, message key, technical detail, status, operation, adapter, attempt number, retry count and timestamps; these remain useful if retained diagnostic rows expire |
| Correlated `AutomationLogEntry` | **CONDITIONAL / INCLUDE_AS_METADATA_ONLY** | Exact row ID and timestamp when the Error Details correlation authority finds one; otherwise explicitly unavailable, never replaced by another row |
| Exact managed-input and expected-output paths | **INCLUDE_AS_METADATA_ONLY** | Local paths are named by the AC; no referenced bytes are thereby authorized |
| Exact screenshot path and package-time status | **INCLUDE_AS_METADATA_ONLY** | Explains available, expired, missing or policy-refused evidence truthfully |
| Owned failure screenshot | **CONDITIONAL / INCLUDE** | Included as `failure-screenshot.png` only when Error Details decoded it and the package inspector positively proves the direct owned capture; the preview warns what it may show |
| PrintFlow name/version and package time | **INCLUDE_AS_METADATA_ONLY** | Bounded application/package identity for support |
| Current production preset and passive environment summary | **INCLUDE_AS_METADATA_ONLY** | Existing readiness authority; only check key, status and blocking flag are copied, avoiding unrelated expected/current path details |
| SQLite diagnostic location and Evidence-root location | **INCLUDE_AS_METADATA_ONLY** | The same local support locations already shown in Settings; database contents are not included |
| Original customer source | **EXCLUDE** | The AC grants no permission to package the source image bytes |
| `InputSnapshot` | **EXCLUDE** | A managed path is metadata, not authority to copy image bytes |
| Working or historical Revision artwork | **EXCLUDE** | Workflow artefacts are not diagnostic package evidence |
| Approved PNG and review-bound bytes | **EXCLUDE** | Approval authority must not be weakened or silently shared |
| Production TIFF / `PrintOutput` | **EXCLUDE** | Production output is neither collected nor mutated |
| Manually imported artwork | **EXCLUDE** | Workflow import choice is not support-package consent |
| Recovery, quarantine and sidecar evidence | **EXCLUDE** | Separate recovery authority; not named by this exact-attempt package |
| Unknown, nested or unrelated Evidence files | **EXCLUDE** | Default deny; no directory recursion, wildcard, extension or proximity inference |
| SQLite database, WAL and SHM bytes | **EXCLUDE** | Only the location is included; database bytes would broaden customer/workflow data without Jira authority |

## 4. Package subject and one-plan authority

The package subject is exactly one terminal attempt selected by Error Details' existing
`SessionId + AttemptId` authority. `ISessionService.LoadErrorDetailsAsync` remains the only join
between the attempt and its structured log/capture. A historical attempt is never converted into
"latest failure" and never borrows a later attempt's evidence.

`DiagnosticPackagePlan` is the sole immutable package authority. It snapshots typed subject,
failure, application, storage and readiness facts and a closed list of roles with dispositions:

- `Included`
- `Unavailable`
- `ExcludedByPolicy`
- `ExcludedByOperator` (closed vocabulary retained for truthful future use; this slice has no
  operator-selectable customer content)

The same plan instance feeds both WPF preview and `IDiagnosticPackageWriter`. The writer receives
only that plan and an operator-selected destination. It receives no directory to enumerate, file
list, wildcard, session lookup or arbitrary add-file API. The only evidence-file role the plan can
admit is `FailureScreenshot`, with the fixed archive name `failure-screenshot.png`.

## 5. Manifest format and exact contents

The archive contains one BOM-free UTF-8 `manifest.txt`; there is no competing JSON or second
manifest authority. It records:

- package format, creation time and the local-only/no-upload invariant;
- PrintFlow application identity;
- processing name plus stable internal session and attempt references;
- workflow, step, attempt status, operation, adapter, timestamps and retry facts;
- stable failure code, message key and bounded technical detail;
- correlated structured-log availability, ID and timestamp;
- managed-input, expected-output and failure-screenshot paths with status;
- local diagnostic database and screenshot-folder locations;
- production preset identity and passive readiness check key/status/blocking facts; and
- every package role's included, unavailable or excluded disposition and exact archive entry name.

Values are newline-normalized and bounded. Paths are included exactly because the original AC
explicitly names local paths. No source path is recovered separately, no referenced production
bytes are copied, and readiness detail/expected/current strings that could reveal unrelated
profile or network paths are deliberately omitted.

## 6. Screenshot and customer-image consent decision

The original AC explicitly names screenshots among the content shown before confirmation. It does
not require a second per-file checkbox. Therefore an available, positively owned failure capture
appears in the immutable plan and preview and is created only after the operator deliberately
presses **Save package** on that preview. The preview says that it “contains what was visible in
the application window at failure time.” Package-level Save is the explicit confirmation.

If the capture is missing, expired or cannot be decoded, it is `Unavailable`. If the path is not an
exact direct owned capture or traverses a reparse point, it is `ExcludedByPolicy`. A retention race
between Error Details loading and plan creation is reflected in the package-time status. If an
included capture changes after preview, export fails and asks the operator to review again; it does
not substitute another file.

Customer source, InputSnapshot, Revision, approved PNG, production TIFF and manual artwork are not
checkboxes because this Jira row never authorizes their inclusion. Their exclusion is visible in
the preview and manifest.

## 7. Operator flow, local-only invariant and destination

The only entry point is:

`Error Details → Export diagnostic package → review actual plan → Save package → owned Windows Save dialog → local ZIP`

Opening Error Details or the preview creates no archive and opens no dialog. The preview displays
subject, included items, exact local paths, unavailable evidence, privacy exclusions, destination
guidance and the explicit statement:

> The package is saved locally. Nothing is uploaded automatically.

The equivalent zh-CN statement is:

> 诊断包仅保存到本机，不会自动上传任何内容。

The operator chooses an absolute `.zip` destination. The suggested name is
`PrintFlow-Diagnostics-<UTC timestamp>-<attempt prefix>.zip`. The writer never targets the customer
output directory, retention root or install directory automatically. An existing target is never
overwritten; a collision-safe numbered name is chosen and the actual saved path is reported.

There is no HTTP client, upload, telemetry export, cloud SDK, email or support-submission path. The
feature ends at an operator-owned local file.

## 8. Staging, publication and validation

The writer:

1. validates the closed plan shape and fully qualified ZIP destination;
2. refuses reparse ancestry around its owned global temporary staging root;
3. builds a randomly named staging archive using only `manifest.txt` and the single exact planned
   failure screenshot, when included;
4. reopens the staging archive, compares the complete entry set to the plan and decompresses every
   entry;
5. verified-copies to a randomly named temporary sibling of the selected destination and repeats
   validation;
6. moves the sibling to a collision-free final name without overwrite; and
7. opens the final operator-owned ZIP through a fresh reader, checks the exact entry set, manifest
   presence/non-empty content, evidence length and full decompression before reporting success.

Owned staging files are cleaned on success, failure or cancellation. If final validation fails,
the just-published invalid file is removed. Success is never inferred merely from archive disposal.

The screenshot inspector rechecks canonical direct-child ownership, the exact capture naming
contract, ancestry, regular-file attributes, length and last-write time immediately before open.
It then holds a non-write/non-delete-sharing handle while copying. There is no recursive directory
enumeration and no wildcard copying.

## 9. Retention, cancel and failure behavior

SCRUM-11121 remains the authority for internal rows and failure captures. Building or exporting a
plan mutates no retention fact and grants no immortality to its source evidence. Retention may win
before the verified screenshot open; the export then fails truthfully rather than guessing. Once
published to the operator-selected destination, the ZIP is outside the internal diagnostic store,
operator-owned and not subject to a second PrintFlow cleanup policy.

Back from preview and Save-dialog cancellation create no final file and do not change session,
attempt, Revision, review, output, log or screenshot state. Writer failure leaves no partial final
ZIP, does not mutate diagnostic evidence, and is presented through a bounded localized error that
does not expose exception detail. Workflow recovery actions remain unchanged and unavailable on
historical attempts exactly as before.

## 10. Localisation and accessibility

All new operator text uses the existing `ILocalisationService`, `OperatorCulture`, `Strings.resx`
and `Strings.zh-CN.resx` authorities. Stable error codes, AutomationIds and archive field names
remain English. No localized string is an identifier.

The preview uses ordinary focusable WPF `TextBox` and `Button` controls. A keyboard-only operator
can review the stable failure reference and exact paths, invoke Save and return Back. UI Automation
exposes stable IDs including `Screen.DiagnosticPackage`, `DiagnosticPackage.LocalOnlyNotice`,
`DiagnosticPackage.FailureReference`, `DiagnosticPackage.LocalPaths`,
`DiagnosticPackage.Save` and `DiagnosticPackage.Back`. The rendered tests use Value and Invoke
providers plus actual focus traversal; no coordinates are used.

## 11. Verification evidence

### Before Product edits

- Existing relevant Error Details, retention, localisation and composition baseline: **46 passed,
  0 failed, 0 skipped**.
- Accepted repository baseline: build **0 warnings / 0 errors** and full suite **11,632 passed**.

### Focused and affected tests

- Package planning/writing, architecture and Error Details UI after final hardening: **16/16**.
- Expanded affected-boundary set covering package, Error Details, retention, Settings/localisation,
  production composition, environment gate and architecture rules: **168/168**.
- Exact deletion-boundary regression plus package architecture: **9/9**.
- Final Release build: **0 warnings / 0 errors**.

New tests cover:

- exact session/attempt binding and no latest-failure substitution;
- available, expired and missing structured log/screenshot evidence;
- immutable default-deny plan and its closed evidence-file role;
- physical source, unknown and nested Evidence files excluded and unchanged;
- no Revision/approved-output/production-output/database byte inclusion;
- exact archive entry set, manifest facts and screenshot bytes;
- collision without overwrite;
- changed evidence failure without a partial archive;
- preview-before-dialog, explicit Save, Back and dialog cancellation;
- real SQLite, real `SessionService`, real composed application graph, synthetic owned diagnostic
  image, real writer, real WPF preview and fresh ZIP reader;
- en-US/zh-CN text, stable AutomationIds, UIA providers, keyboard traversal and no binding errors;
  and
- source-level no-recursion/no-network boundary plus the exact reviewed staging-cleanup deletion
  exemption.

### WPF/UIA proof

The real rendered end-to-end test produced and visually inspected:

- `artifacts/SCRUM-11122/wpf/diagnostic-package-en-US.png`
- `artifacts/SCRUM-11122/wpf/diagnostic-package-bottom-en-US.png`
- `artifacts/SCRUM-11122/wpf/diagnostic-package-zh-CN.png`

The render shows a narrow failure subject, local-only statement, included/unavailable/excluded
sections, all five path values, destination/no-overwrite guidance and reachable Save/Back actions.
Long paths wrap. Binding-error capture remained empty.

### Independent ZIP inspection

Both the backend integration test and the composed WPF/UIA test reopen the resulting ZIP through a
fresh `ZipArchive` reader rather than trusting the UI projection or writer result. They verify the
exact entries, non-empty manifest, correct session/attempt and stable-code facts, explicit policy
exclusions, screenshot bytes where included, absence of customer source, and unchanged workflow
and source evidence.

### Complete suite

One first complete run produced **11,644 passed / 1 failed / 0 skipped**. The sole failure was the
repository's intentional direct-deletion architecture allowlist: the new writer's exact owned
staging cleanup had not yet been named. The allowlist was extended by the single filename
`DiagnosticPackageArchiveWriter.cs`, with its authority documented; no general deletion rule was
weakened. The exact regression then passed 9/9.

The one post-fix complete rerun passed **11,645 / 11,645**, **0 failed, 0 skipped**, exactly the
accepted 11,632 baseline plus 13 new tests. Results are preserved under
`artifacts/SCRUM-11122/full-suite-final-rerun/full-suite-final-rerun.trx`.

A subsequent staged-review copy correction gave intentionally unrecorded structured logs their own
truthful bilingual preview sentence instead of grouping them with expired evidence. Because this
changed only one localized projection branch and resource key, the affected package/localisation
set and Release build were rerun; the complete suite was not repeated after this minor edit, as
required by the no-repeat rule.

## 12. Jira reassessment

### SCRUM-11122: NOT_IMPLEMENTED → FULL

The operator can now reach an exact-attempt diagnostic package from Error Details, see the actual
immutable plan's screenshots/log identity/local paths/metadata and all privacy exclusions, then
deliberately save it through an owned local Save dialog. The archive is default-deny, safely
staged, collision-safe, validated after final publication, local-only, never auto-uploaded and
truthful when retained evidence is unavailable. Every clause in Work Item 11607 is satisfied.

### Parent SCRUM-11115: remains PARTIAL

Current source plus accepted completion deltas establish the Home/Drop, workflow/review/dimensions,
TIFF review, Recent Processing, Settings/Environment Check, Error Details, runtime zh-CN/en-US,
local-only retained diagnostics, practical operator terminology and now explicit diagnostic
package export clauses. The exact parent row still requires SCRUM-11123's repeatable versioned
offline installer and documented install/configure/rollback procedure with no automatic updates.
No installer work exists in this slice, so the parent must not be marked FULL.

## 13. Routing, review and Git discipline

The requested Global Development Routing v2.2 workflow was used for planning and execution. The
requested Sol High starting point and conditional Astra High UI slice could not be verified or
switched because this runtime exposed no actual model/effort metadata or live model-switch
facility. Actual model/effort therefore remains **UNVERIFIED** and the conditional UI switch is
recorded as **MODEL_SWITCH_UNAVAILABLE**; no model switch is claimed.

A complete AC reread, source review, privacy-boundary review, rendered WPF inspection and two
independent fresh ZIP-reader inspections were performed. No genuinely fresh independent-agent
review mechanism was authorized/available in this execution context, so reviewer status is
**SELF-REVIEW ONLY / INDEPENDENT REVIEW NOT COMPLETED** rather than a fabricated approval.

Work stayed in the canonical `master` checkout. No branch, worktree, clone, alternate checkout,
amend, rebase, push, deploy, installer, signing, certificate, Jira mutation, external application,
Co-Authored-By line or AI attribution was added. The local commit containing this report is the
completion commit; its hash is reported in the final handoff. Nothing was pushed.

**PASS WITH NOTES — SCRUM-11122 DIAGNOSTIC PACKAGE EXPORT VERIFIED**
