# PrintFlow Studio

Windows desktop processing assistant for a single operator working on one image at a time.
It drives the existing Meitu and Photoshop desktop interfaces to reduce repetitive work across
enhancement, background removal, trimming, print sizing, and DTF TIFF generation.

## Authority documents

| Document | Role |
| --- | --- |
| [PRINTFLOW_STUDIO_MVP_DESIGN_EN.md](PRINTFLOW_STUDIO_MVP_DESIGN_EN.md) | **Implementation authority** — Confirmed MVP Design v1.0 |
| [PRINTFLOW_STUDIO_MVP_DESIGN.md](PRINTFLOW_STUDIO_MVP_DESIGN.md) | Chinese reference translation (not an independent authority) |
| [docs/printflow/](docs/printflow/) | Epic plans and final reports |

## Epic map

| Epic | Scope | Status |
| --- | --- | --- |
| 11000 | Production Environment Baseline | Complete (signed preset `printflow-workstation-v1` 1.0.0) |
| 11100 | Core Desktop & Workflow Foundation | In progress |
| 11200 | Image Review, Comparison & Deterministic Trimming | Not started |
| 11300 | Meitu Automation Adapter | Not started |
| 11400 | Photoshop TIFF Output | Not started |
| 11500 | Automation Safety / Environment Check | Not started |

## Development baseline

- .NET 10 LTS, `net10.0-windows`; SDK pinned by [global.json](global.json).
- WPF desktop UI; local SQLite metadata; images on the file system.
- Build: `dotnet restore && dotnet build && dotnet test`.

## Privacy

All customer images, snapshots, screenshots, logs, and the database stay local. Nothing is
uploaded. Signed production evidence lives outside this repository under
`D:\PrintFlowStudio\Baseline` and is never copied into Git — see [.gitignore](.gitignore).

This repository is **local-only**: no remote is configured.
