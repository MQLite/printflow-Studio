# PrintFlow Studio MVP Design Document

| Item | Value |
| --- | --- |
| Document version | 1.0 |
| Design status | Confirmed |
| Confirmation date | 2026-08-17 |
| Platform | Fixed Windows workstation |
| MVP scope | Single operator, single image, local desktop workflow assistant |

## 1. Purpose

This document is the implementation and acceptance baseline for the PrintFlow Studio MVP. It defines the product scope, fixed workflows, state and file rules, user interface, automation safety constraints, module interfaces, local data design, testing strategy, and acceptance criteria.

If implementation reveals that Meitu, Photoshop, Maintop RIP, or the fixed production workstation does not match an assumption in this document, the design or production preset must be updated first. The application must never continue production through silent error recovery.

## 2. Product Definition

PrintFlow Studio MVP is a Windows desktop processing assistant for one operator working on one image at a time. It controls the existing desktop interfaces of Meitu and Photoshop to reduce repetitive work across enhancement, background removal, trimming, print sizing, and DTF TIFF generation.

### 2.1 Primary Goals

1. Reduce active operator time by at least 30%.
2. Provide clear and consistent processing flows for design assets, customer-supplied finished designs, and shop-created finished designs.
3. Require human review after every significant transformation and never silently produce an incorrect production file.
4. Hide fragile third-party desktop automation behind replaceable, testable modules.
5. Stop safely and support recovery or manual takeover after crashes, unknown dialogs, or application UI changes.

### 2.2 Explicit Exclusions

The MVP does not include:

- Job, order, or customer management;
- TeeNova integration;
- multi-image batch processing;
- multi-Artwork or task-level coordination;
- multiple users, authentication, roles, or permissions;
- a user-configurable general workflow engine;
- DTF layout, automatic nesting, roll-length calculations, RIP control, or printer control;
- customer communication or production-status notifications;
- cloud upload of images, logs, screenshots, or usage data;
- general-purpose colour management or an image engine intended to replace Photoshop.

## 3. Core Principles

1. **One image at a time.** An active processing session has exactly one input image.
2. **Never overwrite user files.** The input is snapshotted, and third-party applications operate only on working copies.
3. **Attempts and revisions are separate.** A result becomes a Revision only after successful export, readability validation, and hashing.
4. **Significant transformations require review.** Enhancement, background removal, trimming, and final TIFF generation cannot silently advance.
5. **Automation continues only from recognised states.** An unknown UI or unverifiable output must stop the process.
6. **Manual takeover is a normal outcome.** It ends the current automated flow; the operator later drags the manually completed result back into PrintFlow.
7. **A fixed production environment takes priority over generality.** The MVP is supported only on a validated Windows workstation.

## 4. User and Operating Environment

### 4.1 User Model

- One operator;
- no authentication or permission system;
- the current operator also performs reviews;
- review records use the current Windows username, falling back to Operator when unavailable.

### 4.2 Fixed Workstation Contract

The production environment must record:

- Windows version;
- screen resolution and display scaling;
- Meitu version, UI language, and window layout;
- Photoshop version, UI language, and window layout;
- the validated Photoshop production procedure and preset;
- current Photoshop production colour settings;
- current Maintop production configuration;
- default output directory;
- whether external applications must be maximised or use fixed window dimensions.

PrintFlow supports Chinese and English, but its automation supports only the Meitu and Photoshop UI languages validated on the workstation.

## 5. Core Concepts

The MVP does not introduce Job, Asset, Artwork, or a general Output Variant aggregate. It uses only the following concepts.

### 5.1 ProcessingSession

One single-image processing flow. It contains the workflow type, current step, input snapshot, Revision chain, processing attempts, reviews, and outputs.

### 5.2 InputSnapshot

An application-managed copy of the initially imported file. Meitu, Photoshop, and subsequent PrintFlow processing never overwrite it.

### 5.3 Revision

A file successfully exported after a processing operation, confirmed readable, and hashed. A Revision records its direct source, operation, creation time, file path, hash, pixel dimensions, and review state.

### 5.4 ProcessingAttempt

One automated processing or manual-handoff attempt. An Attempt may succeed, fail, be interrupted, or be rejected. A failed Attempt does not automatically create a Revision.

### 5.5 PrintOutput

A TIFF produced from an approved Revision, target physical dimensions, and the fixed production preset. One Session may create several PrintOutputs at different sizes, one at a time.

## 6. Three Fixed Workflows

### 6.1 Workflow Types

| UI label | Internal name | Purpose |
| --- | --- | --- |
| Prepare Design Asset | PREPARE_ASSET | Prepare a photo, logo, or similar source as a PNG for design work |
| Prepare Customer Design | PREPARE_CUSTOMER_DESIGN | Process a customer-composed design and generate a production TIFF |
| Generate Print TIFF | GENERATE_PRINT_TIFF | Generate a production TIFF from a shop-created finished design |

The operator must choose a workflow after dragging in a file. The choice may be changed before the first processed result is created. After that point, the Session cannot switch workflows. To use another route, the operator ends the Session and drags in the appropriate file again.

### 6.2 Prepare Design Asset

1. Drag in one image and save an input snapshot.
2. Confirm the original image.
3. Enhance, performed by default but skippable.
4. Human review.
5. Remove the background, performed by default but skippable.
6. Human review.
7. Automatically trim the canvas.
8. Review or manually adjust the trim.
9. Export an approved transparent PNG.
10. Complete the Session.

This flow does not request print dimensions and does not generate a TIFF.

### 6.3 Prepare Customer Design

1. Drag in one image and save an input snapshot.
2. Confirm the original image.
3. Enhance, performed by default but skippable.
4. Human review.
5. Remove the background, performed by default but skippable.
6. Human review.
7. Automatically trim the canvas.
8. Review or manually adjust the trim.
9. Set print dimensions.
10. Run the validated Photoshop production procedure.
11. Validate CMYK mode, the white-ink channel, and the TIFF.
12. Final human review.
13. Complete the Session.

Enhancement always precedes background removal. Background removal remains skippable because a complete design may intentionally contain a background.

### 6.4 Generate Print TIFF

1. Drag in a shop-created finished design and save an input snapshot.
2. Review the design.
3. Set print dimensions.
4. Run the validated Photoshop production procedure.
5. Validate CMYK mode, the white-ink channel, and the TIFF.
6. Final human review.
7. Complete the Session or add another output size.

This flow does not use Meitu or automatic trimming by default. If the design is not ready, the operator ends the Session and imports it through another workflow.

### 6.5 Manual Processing Handoff

1. PrintFlow creates a working copy from the latest valid Revision.
2. PrintFlow opens Photoshop or the working-copy folder.
3. The current Session is marked HANDED_OFF and its automated flow ends.
4. The operator completes the work and drags the result into PrintFlow.
5. The operator chooses the correct workflow for the new file.

The MVP does not monitor Photoshop saves and does not automatically link a manual result to the previous Session.

## 7. State and Review Model

### 7.1 User-Facing States

| State | Meaning |
| --- | --- |
| Waiting | The step has not started |
| Processing | Automation is running |
| Review Required | A validated result awaits a human decision |
| Approved | The current result has passed review |
| Retry Required | The current result was rejected |
| Handed Off | Automation ended and work was transferred to the operator |
| Skipped | The step was unnecessary because the file already satisfied it |
| Failed | Automation did not produce a valid result |
| Interrupted | The application or computer stopped unexpectedly |
| Completed | All required outputs for the Session passed final review |

### 7.2 Transition Rules

- Successful automation with a validated output: Processing → Review Required.
- Approval: Review Required → Approved → next step.
- Rejection: Review Required → Retry Required.
- A retry always starts from a new working copy of the latest approved upstream Revision.
- Manual processing: Retry Required or Failed → Handed Off.
- The default skip reason is “File already satisfies this step”; skipping creates no Revision.
- Returning to an upstream step immediately invalidates downstream derived results.
- Only one Session may hold the global automation lock.

### 7.3 Review Records

Every review records:

- Session and step;
- Revision or PrintOutput hash;
- decision time;
- Windows username or Operator;
- approved or rejected decision;
- quick rejection reason;
- optional notes.

Quick reasons include insufficient result, edge error, missing content, colour issue, dimension issue, white-ink issue, and other.

## 8. Trimming and Print Dimensions

### 8.1 Automatic Trimming

- When transparency exists, the graphic bounds include every pixel with Alpha greater than 0.
- A small pixel safety margin is retained on every side; its initial value is determined with real test images.
- The operator may trim tightly, add a uniform margin, adjust each side independently, or cancel the trim.
- When no useful transparency exists, PrintFlow does not infer the boundary from white, black, or another background colour. It enters manual crop mode.
- PrintFlow performs automatic trimming internally as deterministic image processing rather than through Photoshop UI automation.
- The existing manual fallback is operator knowledge only: with the Crop tool inactive, Ctrl-click the layer thumbnail to select non-transparent pixels, then select Crop to derive a suitable crop box. Because this may occasionally fail and require manual adjustment, it is not the automated production path.

### 8.2 Order and Review

The fixed order is: background removal → trimming → trim review → physical dimensions → TIFF generation.

Trimming is an independent review step. It shows before-and-after views and supports adjustment or cancellation.

### 8.3 Print Dimensions

- Aspect ratio is locked by default.
- The operator enters width or height, and PrintFlow calculates the other value.
- Non-proportional stretching is not allowed during TIFF generation.
- Target dimensions describe the final transparent canvas, including the safety margin.
- Production resolution is fixed at 300 DPI.
- Resizing is shrink-only. If the trimmed image is already within the selected preset limits, PrintFlow does not enlarge it.
- A3 landscape fits proportionally within 360 mm width × 280 mm height.
- A3 portrait fits proportionally within 280 mm width × 400 mm height.
- A4 fits proportionally with a maximum long edge of 280 mm.
- A5 fits proportionally with a maximum long edge of 135 mm.
- PrintFlow computes the constrained dimensions before the fixed Photoshop steps; order-specific dimensions are not hard-coded into a reusable Photoshop Action.
- The UI displays millimetres, pixels, effective DPI, and detected non-transparent graphic bounds.
- Sufficient effective DPI permits processing; a minor shortfall warns; a significant shortfall blocks output.
- DPI thresholds are established through real print tests.
- The validated Photoshop production procedure uses a fixed, validated resampling method.

One approved design may create several differently sized TIFFs. Every size uses the same approved source Revision, and every output is generated and reviewed independently.

## 9. Inputs, Outputs, and Naming

### 9.1 Meitu Workflow Inputs

Supported inputs are the intersection of formats that Meitu can open and the PrintFlow preview module can decode reliably. Decode and preview validation must pass before Meitu automation starts.

The MVP does not add automatic EXIF-orientation or input-colour normalisation. Display and processing consistency must be verified with the fixed test set.

### 9.2 Generate Print TIFF Inputs

| Format | MVP rule |
| --- | --- |
| PNG | Supported |
| JPG/JPEG | Supported |
| PSD | Must include a Photoshop-compatible composite preview; Photoshop exports a flattened working copy |
| PDF | Single-page only; rasterised at production DPI after confirming page and target dimensions |
| Multi-page PDF | Unsupported; PrintFlow does not silently select the first page |

When importing PSD or PDF, PrintFlow detects colour mode, Alpha channels, and spot channels. If an existing white-ink spot channel is found, automation stops and asks the operator whether to retain it or regenerate white ink with the production preset.

### 9.3 Outputs

- Prepare Design Asset produces a transparent PNG.
- The two production workflows produce TIFF files.
- CMYK conversion, white-ink creation, and TIFF save options are controlled by the validated Photoshop production procedure and current production settings.
- The workflow module does not hard-code a Maintop-specific channel name, polarity, ink density, choke, or colour/white order.
- Production correctness depends on the validated Photoshop production procedure and Maintop environment on the fixed workstation.

### 9.4 Naming

After import, PrintFlow displays an editable Output Name. It defaults to the original filename but can replace corrupted or meaningless names. Editing it does not rename the user's source file.

Suggested output names:

- Name_HD.png;
- Name_CUTOUT.png;
- Name_280mm_CMYK_W.tif.

Windows-invalid characters are removed. If the target exists, PrintFlow adds _02, _03, and so on; it never overwrites silently.

## 10. File Workspace and Retention

The operator configures a default output root on first use. Each Session receives an independent directory with these logical areas:

- Working: temporary external-application copies;
- Approved: approved PNG and TIFF files;
- Rejected: derived results retained during review.

Retention and deletion rules:

- Never overwrite or delete the user's source file.
- Do not automatically delete the current valid InputSnapshot.
- Do not automatically delete approved PNG or TIFF files.
- Move a PrintFlow-generated TIFF rejected at final review to the Windows Recycle Bin.
- Retain rejected Meitu-derived files for comparison until the Session ends, then clean them up.
- Clean safe-to-remove Meitu and Photoshop working copies from Working after completion.
- Returning upstream invalidates downstream files; a rejected TIFF follows the Recycle Bin rule.

Recent Processing shows the most recent 30 days or 100 Sessions, whichever limit is reached first. Older records disappear from the list without deleting Approved production files.

## 11. Automation Design

### 11.1 Automation Priority

1. Windows UI element;
2. stable keyboard shortcut;
3. verifiable visual recognition;
4. absolute coordinates as the last resort.

Power Automate Desktop may be an MVP Adapter implementation detail, but the workflow module must not depend on PAD flow names, buttons, coordinates, or internal steps.

### 11.2 Meitu Automation

The Meitu processing module:

- imports a fresh working copy;
- runs enhancement or background removal;
- waits for an observable completed state;
- exports to a predetermined working path;
- verifies existence, stable size, complete readability, and a reasonable format;
- returns a validated output or structured failure.

A visible result in Meitu is insufficient to create a Revision. Export, readability validation, and hashing must all succeed.

### 11.3 Photoshop Automation

The Photoshop output module:

- prepares a flattened working copy from PSD or single-page PDF when required;
- opens an approved input copy;
- applies target dimensions and 300 DPI through the validated Photoshop production procedure;
- uses confirmed current Photoshop colour settings for CMYK conversion;
- creates the white-ink channel through the validated Photoshop production procedure;
- exports TIFF using validated save options;
- reopens and validates the output;
- returns a PrintOutput or structured failure.

### 11.4 Safe Starting State

Before automation begins, PrintFlow confirms:

- Meitu has no unfinished edit;
- Photoshop has no unsaved document;
- no unknown modal dialog exists;
- the target application is on a validated welcome page or empty workspace;
- the current Session exclusively holds the global automation lock.

If the state cannot be confirmed, the operator must resolve it. PrintFlow does not close unknown documents automatically.

### 11.5 Observable Completion Criteria

A fixed wait cannot be the sole continuation condition. Automation combines these checks:

1. The target application shows the expected completed state.
2. The output file exists.
3. Its size has stopped changing.
4. The file can be read completely.
5. Its format, dimensions, and required metadata are reasonable.

### 11.6 Stop and Manual Takeover

- While Meitu is computing, Stop means stop subsequent automatic steps after computation finishes.
- The operator may import or discard the completed result.
- Force-terminating Meitu is a last resort and requires a warning.
- Manual takeover never resumes in the middle of a mouse-click sequence.
- Unknown screens, unknown dialogs, export failure, rejected review, or environment-check failure offer manual takeover.

## 12. Photoshop, CMYK, and White-Ink TIFF

The MVP does not implement a general white-separation algorithm. The validated Photoshop production procedure creates white ink, using currently confirmed Photoshop production colour settings for CMYK. The shop does not currently use a Photoshop Action. The required explicit UI steps must first be captured and validated; newly authored Actions may then be adopted as optional implementation details if they are faster, handle variable inputs safely, and pass the same output validation.

The confirmed white-underbase procedure loads the current layer's non-transparent pixels, applies a content-dependent inward contraction, and creates a `W1` spot channel at 100% solidity/density. Fine-detail graphics—especially fine white details—use 0 px contraction; ordinary graphics use 1 px; a large solid rectangle or similar content uses 2 px. Classification applies to the complete final design being processed, not to whether the current file originated as a source asset. For the validation fixture, the cut-out person-and-chair treated hypothetically as a complete design uses 1 px, while the corresponding uncut full rectangular design uses 2 px. This is an explicit operator/review decision, not an inferred universal image rule. If Actions are used, the MVP selects among separately validated `W1_0px`, `W1_1px`, and `W1_2px` variants rather than running one unconditional contraction.

This strategy applies only to the validated workstation. It does not claim that every Maintop version, driver, printer, or DTF workflow uses the same channel contract.

A final TIFF must pass at least these checks:

- the file exists and its size is stable;
- it can be reopened;
- pixel dimensions match target millimetres at 300 DPI;
- aspect ratio did not change unexpectedly;
- colour mode is the expected CMYK;
- the expected white-ink channel exists;
- bit depth and compression match the validated production preset;
- the white-ink channel is not obviously empty or an abnormal full-canvas fill;
- an output hash has been calculated;
- final human approval is bound to that hash.

White-channel name, type, polarity, density, choke, and colour/white order belong to the validated Photoshop production procedure and preset, not to generic workflow assumptions.

## 13. User Interface

### 13.1 Overall Layout

- Left: fixed workflow steps and states.
- Centre: image preview, comparison, and cropping.
- Right: current-step controls, guidance, and metadata.
- Bottom: currently available actions such as Approve, Reject, Retry, Manual Processing, and Skip.

### 13.2 Pages

1. Home/Drop page: accepts one image and clearly rejects multiple files.
2. Workflow Selection: three large workflow buttons.
3. Original Confirmation: preview, original filename, editable output name, format, and pixel dimensions.
4. Shared Review: enhancement, background-removal, and trim review.
5. Print Dimensions: width, height, pixels, DPI, aspect ratio, and resolution risk.
6. TIFF Review: colour image, white-ink view, overlay, and production metadata.
7. Recent Processing: thumbnail, name, type, step, time, resume, and delete.
8. Settings/Environment Check: displays and validates the workstation contract.
9. Error Details: structured error, screenshot, paths, and recovery actions.

### 13.3 Review Interaction

The shared review module supports:

- side-by-side or slider comparison;
- synchronised zoom and pan;
- fit, 100%, and high magnification;
- checkerboard, white, and black backgrounds;
- Approve, Reject, and Manual Processing.

TIFF review additionally displays:

- CMYK colour preview;
- white-ink channel preview;
- colour and white-ink overlay;
- physical dimensions, pixel dimensions, effective DPI, colour-setting name, and output path.

### 13.4 Localisation

- Simplified Chinese is the first-run default.
- English can be selected in Settings without restarting.
- Operator-facing text uses production terms and hides technical words such as Revision, Session, and Adapter.
- Internal states and error codes retain stable English names.

## 14. Operating Environment Check

Check Operating Environment validates but does not automatically repair:

- whether Meitu and Photoshop are installed and launchable;
- whether resolution and display scaling match the preset;
- whether the output directory is writable;
- whether the validated Photoshop production procedure's required panels, settings, and starting state are present;
- whether the operator has confirmed Photoshop colour settings;
- whether unsaved documents or unknown dialogs exist;
- whether a test image can be opened and closed.

A failed check identifies the exact item. The operator may exit or take over manually, but high-risk screen automation cannot start in an unconfirmed environment.

## 15. Errors, Recovery, and Diagnostics

### 15.1 Error Records

Every automation failure records:

- Session, workflow, and step;
- time and current application;
- screen screenshot;
- input and expected output paths;
- structured error code and Chinese/English description;
- retry count and available recovery actions.

Logs and screenshots remain local for 30 days and are never uploaded automatically.

### 15.2 Crash Recovery

At startup, PrintFlow checks unfinished Attempts and the global automation lock. An unfinished Attempt becomes INTERRUPTED, after which the operator may:

- restart the step with a fresh working copy;
- inspect and import a manually saved result;
- abandon the Attempt.

Automation never resumes from the previous mouse position.

### 15.3 Diagnostic Package

Only the operator can export a diagnostic package manually. Before export, PrintFlow shows the included screenshots, logs, and paths and asks for confirmation.

## 16. Module Design

### 16.1 Technical Baseline

- A currently supported .NET LTS release;
- WPF desktop UI;
- local SQLite metadata database;
- images stored in the local file system rather than as large SQLite BLOBs;
- versioned offline installer with no automatic updates.

### 16.2 Deep Modules and Seams

#### Workflow Module

Defines the states, valid commands, invariants, downstream invalidation rules, and recovery rules for the three fixed workflows. Its small interface applies an operator command to the current Session and returns a new workflow snapshot plus required effects. The UI never modifies step states directly.

#### Meitu Processing Module

Its interface accepts only an input file, operation type, and working directory, then returns a validated result or structured failure. Import, waiting, export, screenshots, and UI recognition remain hidden in the implementation.

- Production Adapter: Meitu screen automation.
- Test Adapter: deterministic local fake.

#### Photoshop Output Module

Its interface accepts only an approved input, physical dimensions, production-preset identifier, and output name, then returns a validated PrintOutput or structured failure. Explicit Photoshop UI operations, saving, and reopening remain hidden. Trimming is not part of this module.

- Production Adapter: Photoshop screen automation.
- Test Adapter: deterministic local fake.

#### Trimming Module

Its interface accepts an input image, safety margins, and an optional manual crop rectangle. It returns a trimmed file, original bounds, and final bounds. Alpha detection, unit conversion, bounds validation, and file generation remain hidden.

#### File Workspace Module

Owns Session-directory creation, snapshots, working copies, derived-file registration, collision-free names, Recycle Bin operations, and safe cleanup. Other modules do not assemble or delete paths directly.

#### Review Module

Provides comparison, zoom, pan, background switching, rejection reasons, and review decisions. TIFF review is a deeper mode of the same module rather than a duplicate review implementation.

#### Persistence Module

Persists Session, Revision, Attempt, ReviewDecision, PrintOutput, AutomationLog, and Setting metadata in SQLite transactions. The UI does not execute SQL directly.

### 16.3 Dependency and Testing Seams

- Trimming and state transitions are deterministic in-process logic tested through module interfaces.
- SQLite and file-system behaviour use temporary databases and directories in integration tests.
- Meitu and Photoshop are uncontrolled external dependencies; their seams accept either production or test Adapters.
- Tests observe outcomes through the same interfaces used by production callers and do not assert internal click sequences.

## 17. Local Data Design

SQLite stores metadata only. Exact table and field names may change during implementation without changing the invariants.

### 17.1 ProcessingSession

- ID;
- workflow type;
- display/output name;
- current step and state;
- created, updated, and completed times;
- workspace path;
- automation-lock information;
- interruption and manual-handoff information.

### 17.2 Revision

- ID, Session ID, and direct source Revision ID;
- operation type;
- file path, format, size, and hash;
- pixel dimensions, DPI, colour mode, and transparency information;
- creation time;
- current validity and review state.

### 17.3 ProcessingAttempt

- ID, Session ID, and step;
- input Revision ID;
- operation and Adapter identifier;
- start and end times;
- success, failure, interruption, or rejection state;
- optional output Revision ID;
- structured failure and retry information.

### 17.4 ReviewDecision

- ID, Session ID, and step;
- Revision or PrintOutput ID;
- decision time and operator;
- approved or rejected;
- quick reason and optional notes;
- reviewed file hash.

### 17.5 PrintOutput

- ID, Session ID, and source Revision ID;
- target width and height in millimetres;
- pixel dimensions and 300 DPI;
- production-preset identifier;
- output path, file size, and hash;
- CMYK, white-ink, and TIFF validation results;
- review state;
- invalidation, Recycle Bin, and deletion records.

### 17.6 AutomationLog and Setting

AutomationLog stores structured errors and screenshot paths. Setting stores UI language, default output directory, production DPI, safety margin, fixed-environment details, colour-settings confirmation, and log retention.

## 18. Required Invariants

1. PrintFlow never overwrites or deletes a user's source file.
2. Every approval is bound to a specific file hash.
3. A changed file never inherits an earlier approval automatically.
4. A failed Attempt never creates a usable Revision.
5. Only fully exported, readable, hashed files may enter review.
6. Only an approved upstream Revision may produce a final TIFF.
7. Only one Session may control Meitu or Photoshop at any time.
8. Every retry starts from a clean working copy.
9. An invalidated downstream file cannot remain the current production result.
10. A rejected PrintFlow-generated TIFF is moved to the Recycle Bin.
11. Automation never guesses at clicks when the environment is unrecognised.
12. The UI never directly changes database state, deletes files, or controls external applications.

## 19. Privacy and Updates

- All customer images, snapshots, screenshots, logs, and the database remain local.
- The application never uploads customer data automatically.
- A diagnostic package requires explicit manual export and confirmation.
- The MVP uses a versioned offline installer.
- PrintFlow does not update automatically.
- Windows, Meitu, and Photoshop updates should be controlled on the production workstation.
- Every upgrade requires rerunning the standard test set.

## 20. Testing Strategy

### 20.1 Module and Integration Tests

- Test transitions, skipping, retry, invalidation, and recovery through the workflow module interface.
- Test Alpha bounds, safety margins, no-transparency cases, and manual cropping through the trimming module interface.
- Use temporary SQLite databases and directories to test transactions, snapshots, naming collisions, and safe cleanup.
- Use fake Adapters to simulate Meitu and Photoshop success, failure, timeout, unknown dialogs, and interruption.
- Assert observable interface results rather than internal click order.

### 20.2 Fixed-Workstation Test Set

The local standard test set includes at least:

- a normal JPG portrait;
- a complex background with fine hair;
- a transparent PNG;
- a complete customer design;
- a PSD with a compatible composite preview;
- a single-page PDF;
- a reference TIFF proven in real production.

The following three successful end-to-end paths must pass:

1. JPG photo → enhancement → background removal → trimming → approved PNG.
2. Customer design → Meitu → trimming → dimensions → validated Photoshop production procedure → approved TIFF.
3. PNG/JPG/PSD/single-page PDF → dimensions → validated Photoshop production procedure → approved TIFF.

Each path also tests:

- rejected review;
- automatic retry;
- manual takeover;
- restart recovery;
- unknown dialogs;
- output-validation failure;
- moving a rejected TIFF to the Recycle Bin;
- adding multiple TIFF sizes from one design.

## 21. MVP Acceptance Criteria

### 21.1 Functional Acceptance

- All three fixed workflows complete end to end.
- Enhancement always precedes background removal, and both may be skipped.
- Trimming can be calculated, reviewed, adjusted, or cancelled.
- Print dimensions use a locked aspect ratio and 300 DPI.
- The validated Photoshop production procedure creates a CMYK white-ink TIFF accepted by the current Maintop environment.
- Every significant step requires review before continuation.
- Failures, retries, manual takeover, and recovery never overwrite user files.
- Every approval is bound to a file hash.
- A rejected PrintFlow TIFF is moved to the Recycle Bin.
- Unknown UI states never trigger guessed clicks.

### 21.2 Outcome Acceptance

- Active operator time falls by at least 30%.
- Single-image end-to-end time does not exceed the current manual flow.
- Automation succeeds for at least 90% of the standard test set.
- No incorrect TIFF is silently produced or marked approved.

The system records:

- active operator time;
- automation wait time;
- total single-image processing time;
- manual-takeover rate;
- automation retry rate;
- rejection rate for each review step.

## 22. Inputs Required Before Implementation

Before automation implementation begins, collect:

1. Windows version, resolution, and display scaling for the fixed workstation.
2. Meitu version, UI language, and actual enhancement, background-removal, and export steps.
3. Photoshop version, UI language, and production colour settings.
4. The documented and validated explicit Photoshop production procedure and preset.
5. Default output directory and naming examples.
6. The standard local image test set.
7. Timing data from at least 20 real examples of the current manual workflow.
8. One reference TIFF already proven in actual Maintop production.

The reference TIFF confirms the actual result of the fixed environment; it does not establish a universal Maintop contract.

## 23. Recommended Implementation Order

1. **Environment baseline:** collect workstation details, actual Photoshop operating steps and settings, and test images.
2. **Workflow foundation:** implement fixed flows, Session persistence, SQLite, snapshots, and Recent Processing.
3. **Review and trimming:** implement comparison, backgrounds, Alpha trimming, manual adjustment, and review records.
4. **Meitu Adapter:** implement enhancement, background removal, export validation, failure screenshots, and manual takeover.
5. **Photoshop Adapter:** implement PSD/PDF preparation, sizing, the validated explicit Photoshop UI procedure, TIFF validation, and multiple output sizes.
6. **Recovery and environment checks:** implement the global lock, interrupted recovery, environment validation, and unknown-dialog protection.
7. **Localisation and diagnostics:** complete Chinese/English UI, retention, error details, and diagnostic-package export.
8. **Production acceptance:** run the standard test set, Maintop validation, and physical test prints; compare timing and takeover rates.

## 24. Accepted MVP Risks

1. **Screen automation is fragile.** Mitigated through a fixed environment, observable states, structured failures, and manual takeover.
2. **Current Photoshop settings and UI procedure can change.** Mitigated through a versioned production preset, environment confirmation, output validation, and real test prints.
3. **The TIFF/Maintop contract is not universal.** The MVP supports only the validated environment.
4. **Input orientation and colour normalisation are deferred.** Consistency must be observed in the fixed test set.
5. **Manual results are not linked automatically to old Sessions.** This is an intentional MVP simplification.
6. **There is no Job management.** The MVP validates processing automation, not customer or order workflow.
