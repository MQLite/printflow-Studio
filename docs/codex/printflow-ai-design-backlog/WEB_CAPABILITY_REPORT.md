# SCRUM-11158 browser capability observations

Date: 2026-10-05 (Pacific/Auckland). Scope: PF-AIDESIGN-v1, design revision 0.2.

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
