# SCRUM-11147 trim adjustment design — independent review

Task `PF-OPUX-v1-SCRUM-11147-design-v1`, 2026-09-25 NZ. This file reviews [SCRUM-11147_TRIM_DESIGN.md](SCRUM-11147_TRIM_DESIGN.md). A technical design review is not owner approval and does not authorize implementation.

## Mechanism

- **Reviewer.** One fresh `personal-dev-reviewer` subagent context, which is read-only (Read, Glob, Grep; no edit, shell or delegation). Its frontmatter sets model `opus` and effort `high`.
- **What it received:**
  - the owner prompt file;
  - the design;
  - the verbatim Jira AC and non-goals;
  - a list of source files and members to verify.

  It did **not** receive the designer's exploration history or self-assessment.
- **What it did not do.** It read no Jira itself; the AC came from the designer's message, copied from the authenticated read. It built nothing, ran nothing, started nothing and did not verify WPF runtime behaviour.
- **Rechecks.** Two corrected scopes were rechecked in the **same** reviewer context. That is a continuation, not a second independent review.
- **Route.** RequestedRoute Opus/High. Reviewer ActualRoute UNVERIFIED: the host exposed no reviewer model/effort telemetry. Designer: host model `claude-opus-5-5`, effort UNVERIFIED. Policy: Claude adaptation v1.2 of policy v2.4, `route_offset: 0`, AdjustmentResult UNCHANGED, no Sonnet unit.

## Round 1 — verdict OPEN (no P0/P1; 2 × P2, 7 × P3)

The reviewer confirmed every §1 source-map claim it checked and the §5.2 arithmetic. It judged option C lawful and minimal: no fabricated review, one gate, no nesting, and the Failed/RetryRequired rows untouched. It found the exact-target binding sound.

| # | Sev | Finding (summary) | Disposition |
|---|---|---|---|
| F1 | P2 | Marking R1 `Superseded` in the opening commit records a false "replaced" fact when the crop fails, is interrupted or its closing commit fails. It contradicts the KeepOriginalExtent precedent, is unnecessary for safety, and needs an unlisted `BuildMetadataMutation` case. The `InvalidateDescendants(R1, UpstreamChanged)` is empty and misnamed. | **Fixed.** The transition emits only the three existing manual-crop effects. R1 is taken off the step pointer with no review and no invalidation. Eligibility refuses an R1 that has any descendant revision or PrintOutput (§7.2, §7.3, §8, §11–§14). |
| F2 | P2 | `CropOverlay` clips at the image. At fit zoom one axis has no letterbox, so handles and outline on the picture border are cut. The exact border is about 0.26 DIP wide. Handle placement is unspecified. The corner "per axis" rule is ambiguous. `Thumb` is not focusable by default. Arrow keys would scroll. | **Fixed.** Handles are anchored inside the kept rectangle and the outline is inset; the overlay and `CropSurfaceLayout` are unchanged. `Focusable="True"` is set explicitly and arrow keys are handled. A corner with any invalid axis is refused whole. Exact border routes: zoom, Ctrl+arrow snap (validated in source space) and Restore; overshoot is still refused. Copy updated, and a border render was added to the AC7 evidence (§5.1, §9.1–§9.3, §11). |
| F3 | P3 | After an integrity refusal (U invalidated), the editor stays open and keeps failing. | **Fixed.** Eligibility requires U and R1 valid and U not retention-released. A null adjustment view closes the editor with the source-unavailable line (§3, §6.4, §7.2, §7.3). |
| F4 | P3 | The "Unchanged" fallback row contradicts the handles and Restore added to the fallback, and margin semantics there were unspecified. | **Fixed.** The Failed/RetryRequired fallback is left truly unchanged (§6.2, §7.3). |
| F5 | P3 | "Skip where lawful" appears although Trim is not skippable. The uncertain row omits a failed closing commit. | **Fixed** (§7.3). |
| F6 | P3 | Focus after Cancel adjustment is undefined, because `FocusNewReview` does not run on the same target. | **Fixed.** Focus returns to "Adjust trim edges", or to `OperatorStatusPanel` (§8, §9.3). |
| F7 | P3 | Trim Reject is hash-only and KeepOriginalExtent is unbound; neither has a fresh-gesture guard. | **Recorded as a pre-existing residual** outside this design, for owner awareness (§8, §14). A mistaken Reject of R2 is recoverable. |
| F8 | P3 | A same-identity `Show()` keeps the draft but resets zoom. | **Fixed.** Zoom and fit are kept with the draft; scroll may reset (§6.4). |
| F9 | P3 | §10 overstated the KeepOriginalExtent refusal (an approved upstream passes). | **Fixed** (§10 wording). No dependency either way. |

## Round 2 — recheck of the corrected scope: OPEN (1 × new P2, 2 × new P3)

F1–F9 were all confirmed fixed. The reviewer also confirmed that leaving R1 valid, unreviewed and no longer current is safe: approval, PNG promotion and the resolver all bind to the step's current revision, and retention releases only Enhance and Background-removal revisions.

| # | Sev | Finding (summary) | Disposition |
|---|---|---|---|
| N1 | P2 | Introduced by the F2 fix. Handles sit up to 10 DIP inside the edge and the edge was set to the absolute pointer, so pressing and releasing without moving pulled the edge inward by the grab offset (about 19–39 source px in the §5.2 layout). | **Fixed.** The edge moves by the pointer's displacement since the press, per axis. Movement below the system drag threshold is not a drag, so a press and release leaves the bounds unchanged. §5.2 examples are rephrased as displacements; §9.1 and the §11 AC1 evidence are updated. |
| N2 | P3 | Inside-anchored handles overlap on selections under about 20 DIP. | **Fixed.** The press goes to the nearest handle centre, with corners winning ties. The screen suggests zooming in, and keyboard nudges still work. Added to the AC1 evidence. |
| N3 | P3 | Stale reference "§10.3" in §4. | **Fixed** (now §9.3). |

## Round 3 — recheck of N1–N3: **CLOSED**

N1–N3 were confirmed fixed, with no P0–P2 remaining. The reviewer checked the §5.2 arithmetic again (302.08 − 12.08 = 290.0 → 1132; 584.32 − 24.32 = 560.0 → 2125). It observed that an edge which passes through an invalid position resolves from the press point; that is consistent with the refusal rule and is not a defect. One run of the reviewer ended early on a host rate limit (HTTP 429) without producing a result. It was resumed once with the same request and completed.

**Outcome: technically ready for owner review.** Open items:
- Owner approval of the design.
- The pre-existing residual F7, recorded for owner awareness only.
- All runtime, visual, physical-input and novice evidence, which is NOT RUN.

## Limitations

- This is static design review only. No build, test, application, desktop, native input or visual check was performed, and all future evidence in the design is NOT RUN.
- The reviewer's model identity is UNVERIFIED.
- The recheck ran in the same reviewer context.
