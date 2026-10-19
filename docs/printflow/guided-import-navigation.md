# Guided Home and import navigation (SCRUM-11170)

Home, route choice and original confirmation are separate steps. Home starts one local image, surfaces continuing tasks before finished history, and keeps environment details secondary. Route choice uses three compact cards with purpose, result and dimension requirements. The original step uses the E2 session shell and shows the original filename plus applicable PDF/PSD information.

## Ownership

`NavigationService.Show` deactivates the outgoing `IEntryScreenLifetime`. Home and route ViewModels are transient. Do not reuse a retired Home to refresh or navigate. `HomeViewModel` cancels its import token on departure; both cancelled and retired completion paths must be inert. Import cancellation means cancelling the wait: an already committed service transaction is not undone, and its task remains discoverable through normal recents/recovery.

`WorkflowSelectionViewModel.Open` starts a new operation generation. Check that generation after every awaited rename/select and before cleanup so an old operation cannot navigate, replace notices or clear the new task's busy state. Existing service results remain authoritative; no UI rollback or implicit retry is permitted.

`EntryActionScope` connects Home/route views to the existing E2 `SessionActionButton` / `SessionTextBox` identity guards. Preserve neutral focus and retained/queued UIA, key-repeat, rebinding and unload protections when adding an entry action. Command eligibility is still owned by the existing ViewModel and workflow service.

## Layout and source facts

Home uses a shared `HomeTaskRow` template for continuing and historical rows. Keep recovery identities, automation names and original commands in that template. Finished history is a disclosure; tests that inspect historical rows must expand it first. Import feedback is inside the import card so failure stays visible at 1000x700 DIP.

Route cards deliberately omit the complete future-step list; they retain purpose, PNG/TIFF result and whether print dimensions apply. At the minimum viewport all three select buttons must fit inside their ScrollViewer viewport, not merely within the root control's rectangle. The source summary is compact enough to preserve that contract.

`ShowsGuidedPdfSource` and `ShowsGuidedPsdSource` restrict source notices to the original step. PDF page/size and PSD channel caveats continue to come from the existing source facts; never invent preparation success. Shared source text must also be truthful for the PNG asset route, so it cannot promise later TIFF or print-size steps. `ChangeRouteCommand` uses engine `SelectWorkflow` eligibility and E2 leave/draft decisions. After a derived result it is unavailable.

AI ownership remains with SCRUM-11156 / SCRUM-11166. This change supplies only localized unavailable information with no AI command or external transfer. Full SCRUM-11184 integration is BLOCKED_DEPENDENCY and must be accepted separately once the upstream capability exists.

## Verification

Use `output/ui/SCRUM-11170/checks/run-safe-ui.ps1`; it wraps the include expression and always applies the same 11 audited visible/opt-in exclusions. Do not set workstation opt-in flags or launch App, Show/ShowDialog, Meitu, Photoshop, desktop input or production data for these checks.

The GuidedImport tests cover cancellation/lifetime, stale providers and keys, route lock/return, source-phase content and unavailable AI. VisualEvidence renders real UserControls offscreen on STA using synthetic/temp files and fake adapters. It emits 21 Chinese PNGs and JSON geometry at 96 DPI, with 1000x700, 1280x720 and 1920x1080 DIP coverage for Home, route choice and each original step. PDF/PSD pending/review plus import pending/refusal are 1000x700. Real keyboard, DPI scaling, newcomer and production acceptance remain NOT RUN.
