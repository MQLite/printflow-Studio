# SCRUM-11158 browser capability observations

Date: 2026-10-05 (Pacific/Auckland). Scope: PF-AIDESIGN-v1, design revision 0.2.

Current status: the user subsequently authorized Chrome ("改用Chrome"). The final Chrome trial appendix below supersedes the historical browser-scope blocker, while preserving incomplete download and automatic-channel acceptance.

## Direct observations

- The supported computer/browser inventory returned one connected Chrome profile, the Codex in-app browser, and MCP Apps. It returned no connected Edge browser and no native application surfaces.
- Attempting `cua.createBrowserTab("edge", "https://chatgpt.com", {sessionName: "🧪 PrintFlow Work 验证"})` returned `Browser is not available: edge` before any page opened.
- A later inventory recheck still listed only Chrome (`id=3`), Codex in-app browser and MCP Apps. Selecting that connected provider for documentation explicitly identified **Name: Chrome / Type: extension / ID: 3**. No tab was opened or claimed by this metadata check; no evidence supports treating this provider as Edge.
- Rechecked once more after T04/T05 local completion during T06 development: `cua.listBrowsers()` again returned Chrome `id=3`/Person 1/type extension, in-app browser `id=2` and MCP Apps `id=1`; still no Edge. This read-only inventory did not open/claim a page or transmit files. The pending scope question has received no answer.
- Final post-regression inventory at 2026-10-05 01:47 UTC still returned exactly Chrome 3, in-app browser 2 and MCP Apps 1; no Edge. This inventory-only check opened no page and transmitted no files.
- A clarification is pending: connect the required Edge surface, explicitly authorize a Chrome acceptance-scope change, or defer real browser acceptance while local work proceeds where dependencies allow.

## Acceptance result

| Capability | Status | Evidence / reason |
| --- | --- | --- |
| Required real Edge configuration | BLOCKED | Edge is unavailable in the current supported control channel |
| Logged-in Work mode | NOT RUN | No required Edge page was accessible |
| Text-only Work image generation | NOT RUN | No submit occurred |
| Synthetic image plus requirements-file upload | NOT RUN | No files were uploaded |
| Explicit per-attachment upload completion | NOT RUN | No upload occurred |
| Result identity, download ID and completed local download | NOT RUN | No generation/download occurred |

These observations do not prove that Edge is uninstalled or that the website cannot support the feature. They establish an execution-access blocker in this session. Connected Chrome is not evidence of Edge acceptance. No account data, cookies, customer content, or screenshots were saved.

SCRUM-11158 remains incomplete. The automatic channel must stay disabled until the actual required page, upload and download contracts are verified. Preserve the existing Jira dependency on SCRUM-11158 for SCRUM-11163; do not substitute ordinary Chat or fixture success for Work acceptance.

## Read-only Chrome observation appendix — 2026-10-05

This bounded inspection does not change the required Edge acceptance scope. The human browser choice remains pending. Only relevant control attributes were inspected in the existing ChatGPT tab; a temporary new tab at `https://chatgpt.com/` was used to inspect the root composer without reading conversation messages.

- The authenticated-looking composer surface exposes `Add files and more` and a textbox labelled `Ask ChatGPT`. Authentication was inferred from the available signed-in interface; no account menu, credentials, cookies or account details were inspected.
- The fresh root page exposes a visible `Work` control. The accessibility representation calls it an unchecked checkbox (`Value: 0`); the DOM identifies a `button` with exact text `Work`, `type="button"`, `data-state="closed"`, and no `role` or `aria-checked` attribute. A DOM checkbox locator therefore timed out. The control was not activated. This difference is relevant when establishing the eventual selector contract; neither representation alone proves active Work mode.
- The existing composer exposes standard `input[type="file"]` elements labelled `Attach photos or videos` (`accept="image/*,video/*"`), `Attach photos` (`accept="image/*"`), and `Attach files` (no accept attribute). All three have `multiple`. These are observed input attributes, not proof of supported file formats, actual receipt, or successful uploads in Work mode.
- No button or link with a download-related accessible label was present in the inspected page. No generated result was requested or inspected, so this is not evidence that image download is unsupported.
- No prompt was entered or submitted, no file was uploaded or downloaded, no Work task was created, and no settings or extensions were changed. No screenshots, account details, unrelated chat content or page identifiers were saved in this report. No supported production selector/fixture was declared from this inspection.

The useful next step, after the pending browser decision, is to enter Work through the observed control in the authorized browser, then run the two planned synthetic trials and establish attachment completion, result identity, and unique browser download-ID evidence. T02 AC1–4 remain incomplete and the automatic channel remains unavailable.

Routing: policy 2.4; normal/requested/execution target Astra 6.0 High (`gpt-6-astra`, `high`); inherited route offset 0, adjustment unchanged. The native subagent's own rollout `turn_context` at `2026-10-05T03:18:19.839Z` verifies actual `gpt-6-astra` / `high`; this is runtime evidence, not merely an accepted spawn request. This was a bounded webpage-contract subagent context, with no code or Jira changes.

## Authorized Chrome synthetic trials — 2026-10-05, approximately 03:29–03:35 UTC

The user explicitly selected Chrome. Two dedicated new Chrome tabs on `https://chatgpt.com/` were used, without customer files, account-menu inspection, cookies, unpublished endpoints, settings changes or extension installation. Each trial was submitted exactly once. The original Work/upload/result/download requirements remain unchanged. The current trial facts below replace the earlier NOT RUN observations for those interactions only.

### Observed page contract

- Active Work is directly observable: the exact `Work` DOM button has `aria-pressed="true"`; AX shows Work Value 1; heading is `What should we work on?`; the contenteditable textbox is labelled `Work with ChatGPT`. The Work button was activated for the first trial; the second fresh root retained active Work and was checked before submit.
- `Add files and more` opens an attachment menu containing button `Add photos & files Upload from computer`. The documented filechooser flow returned `isMultiple() = true` and accepted the two explicitly approved synthetic files. No fake path-only or prompt-embedded upload was used.
- Each file first had its own AX progress indicator (`Uploading reference-shapes.png`, `Uploading requirements.txt`). After completion these disappeared; the composer contained an enabled role-button `DIV` labelled `reference-shapes.png`, an enabled `BUTTON` labelled `requirements.txt`, and matching Remove buttons. The composer had zero `[role="progressbar"]` elements. The Send button was enabled. This is the observed visible completion transition, not an extracted server receipt or a proven attachment-ID/fence protocol; same-name/failed/late-upload cases remain untested.
- Submission uses the exact enabled `Send` button. On submit the root URL changed through a temporary local-chat identifier to a stable conversation URL. The UI showed Working, Thinking/Creating image, and a Stop button; completion showed `Worked for ...`, `Response complete`, generated-image controls and an output file. No fixed delay or arbitrary image was used to declare generation completion.
- A completed image uses button/image label `Generated image 1`, with separate Edit/Share controls. Its preview exposes `Image preview`, `Download`, and `Close viewer`. The response also exposes `Open preview of <generated-name>.png`, `Download file`, and an Outputs entry. The mixed trial's uploaded reference remained a distinct `User attachment`/source file. Generated-image labels are reusable UI labels, not globally unique IDs; conversation and output identity must remain part of the eventual product binding.

### Direct trial evidence

| Trial | Input and submit | Real result | Download result |
| --- | --- | --- | --- |
| PF-T02-TEXT-20261005 | No attachments; one request for one teal circle on white; active Work confirmed; Send clicked once | Dedicated conversation; Worked for 1m 2s; named `exec-<generated-output-id>.png` output; viewer title `Centered Teal Circle On White`; image natural size 1254×1254 | `Download file` click and viewer `Download` click each produced a 15s supported download-event timeout. Documented `downloadMedia` on the exact visible generated image also timed out after 20s. No path, bytes, hash or numeric download ID obtained. |
| PF-T02-MIXED-20261005 | `reference-shapes.png` (902 bytes) plus `requirements.txt` (368 bytes), from ignored synthetic-trial artifacts; both completion transitions observed before one Send click | Dedicated conversation; response reported reading both attachments; Worked for 1m 28s; named `exec-<generated-output-id>.png` output; viewer title `Red Square and Blue Circle`; image natural size 1254×1254 | Viewer Download clicked once; supported download event timed out after 15s. No path, bytes, hash or numeric download ID obtained. |

The input PNG was inspected locally and contains only a red square and blue circle on white. The TXT contains only synthetic image requirements. Exact trial conversation, tab and generated-output identifiers are retained only in ignored `artifacts/ai-design-chrome-trial/browser-trial-correlation.json`, not tracked fixtures. A cropped screenshot attempt of the generated image failed with `Page.captureScreenshot` timeout after 5s; therefore final generated pixels were not visually verified or saved. Natural dimensions and output existence are real DOM observations, not complete image decoding or visual-quality proof.

The supported Chrome control API documents `PlaywrightDownload.path()` but no numeric browser download-ID field. Capability discovery returned browser `viewport` and tab `pageAssets`; no extension-management/sideload or numeric download-ID capability was exposed. The failed download observations may be a controller limitation; they do not establish that the website or the future product extension cannot download. No browser download directory was scanned, and no latest-file heuristic was used. Loading the local unpacked extension and using its official downloads API remains a separate real trial; no profile or registry workaround was attempted here.

### Original T02 AC status and gate

| Original AC | Status | Evidence / remaining work |
| --- | --- | --- |
| AC1: webpage contract, sanitized fixture and browser/date | PARTIAL | Dated Chrome observations and partial sanitized fixture exist; Chrome is explicitly authorized. Complete download contract remains missing. |
| AC2: separate text-only/attachment outcomes and fallback | PASS for reporting | Both real Work generations and mixed upload transitions are recorded separately; completed download remains BLOCKED. Prompt plus manual upload/download remains the fallback. This reporting PASS is not automatic-generation acceptance. |
| AC3: all attachments complete before single submit; no arbitrary image/fixed delay success | PARTIAL | Mixed trial visibly transitioned both named uploads to ready chips before a single submit; explicit response completion plus generated output identified. Durable upload-ID binding, negative cases and unique result-to-download association remain unverified. |
| AC4: keep automation unavailable when Work/upload/download unverifiable | PASS for guard requirement | Work and synthetic upload/generation observed; completed local download and unique browser download ID unverified. `automationEnabled` remains false. |

SCRUM-11158 remains incomplete, and SCRUM-11163/T07 cannot pass its original gate yet. Next: load/validate the product extension in the authorized Chrome profile through a supported or human-assisted unpacked-extension flow; establish real Native Messaging Hello, then observe an official browser download ID and complete event bound to one of these generated results, obtain the exact local file, and verify its bytes/hash. Do not resubmit these completed generations. Both dedicated tabs are retained for that follow-up; no generated tasks were deleted.

Evidence artifacts: `extensions/printflow-work/fixtures/work-contract.json` and `work-observed-sanitized.html`. These describe partial observed semantics and explicitly deny production acceptance. They are not a simulated PASS or a complete selector implementation. No application tests/builds, code changes, Jira writes or commits were performed by this trial agent.
