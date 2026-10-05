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
