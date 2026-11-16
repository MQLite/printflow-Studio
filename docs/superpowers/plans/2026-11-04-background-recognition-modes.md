# Background recognition modes implementation plan

> For agentic workers: use subagent-driven-development for the data contract task and inline integration for the UI and Meitu driver. Preserve the existing checkout and all pre-existing changes. Do not commit, publish, or migrate the production database during this task.

**Goal:** Allow Automatic, Portrait/Pets, Products, and Logos/Stamps recognition for background removal. The user explicitly selected recognition modes only; manual cutout/retouch tools are outside this change.

**Design:** Keep reviewed-content authorisation separate from the selected recogniser. Add a typed `BackgroundRemovalMode` (Automatic, PortraitPets, Products, LogosStamps) to the existing immutable authority, attempt, adapter request and read model. Automatic remains the legacy default. Additive SQLite columns preserve existing records and bind a mode to the same reviewed revision/hash. A mode picker is shown before authorisation; unconfirmed changes must not silently run the previously authorised mode. The Meitu driver must select and read back the requested recognition mode and must never fall back silently to automatic. Keep identity, Busy/completion, source-byte, canvas-size, transparency, cancellation and target guards.

**Alternatives:** Encoding the mode as new authorisation enum values would conflate consent with recogniser choice and require rebuilding constrained tables. Leaving the choice only in Meitu would make retries/restarts depend on retained UI state. Use a separately persisted typed mode instead.

## Task 1 — Contract, workflow and persistence

- Add `BackgroundRemovalMode` and optional `Mode = Automatic` to `BackgroundRemovalAuthority` and `SetBackgroundRemovalDecision`.
- Validate defined modes; preserve reviewed-revision/hash and immutable attempt rules.
- Add nullable mode columns through migration 0020; legacy null maps to Automatic. Session upsert updates pending mode; attempt upsert never rewrites its opening mode.
- Expose current and producing-attempt modes on `SessionView`; pass the recorded attempt mode through `MeituRequest.BackgroundRemovalMode`.
- Write and run meaningful round-trip, stale-content, invalid-mode, legacy-default and immutable-attempt tests before/after implementation.

## Task 2 — Mode picker and authorisation

- Use the existing WPF guided background-removal panel with localised options for the four modes.
- Preserve the chosen mode through confirmation and reload. A provisional choice differing from recorded authorisation disables run until confirmed.
- Display the actual recorded mode in current authorisation and result audit text.
- Cover selecting, confirming, changing, retrying and reloading modes in view-model tests.

## Task 3 — Meitu selection and validation

- Inspect the actual recognition-mode dropdown/control signatures on a synthetic Working image.
- Extend guarded Background Removal to accept a typed mode, select a specific recogniser, and positively read it back.
- Preserve the existing automatic path for legacy callers and retain positive processing/completion correlation.
- Test all modes, wrong/missing/ambiguous controls, mismatches, stop/cancellation, and no fallback.
- Run opt-in live synthetic checks, then relevant workflow/persistence/UI/Meitu/startup/architecture suites and rebuild the debug application.

## Review focus

- Existing automatic sessions and attempts continue to read correctly.
- Mode changes never transfer authority to a different image or rewrite a prior attempt.
- An on-screen provisional choice never runs a different persisted mode.
- A requested mode must match Meitu's actual selected mode.
- No manual tools, customer-image uploads, production data edits, installer changes or unrelated fixes.

## Progress

- Contract, workflow, SQLite persistence and immutable attempt audit implemented and reviewed.
- Four-mode picker, confirmation, provisional draft protection and result audit implemented and reviewed.
- Native mode selection, readback, entry-result undo and retry implemented. Expanded 7.9 preview completion requires an actual signed-shape Apply control with non-empty bounds.
- User clarified that an automatic result on entry must finish, then be undone before reapplication. Instant cached results require native Closed-to-selected and Undo/Redo history evidence; Busy is never fabricated.
- Verification complete: 1,361 targeted tests passed, with no failures. Independent review found no remaining actionable issues.
- Live synthetic sequence passed: Automatic → PortraitPets → Products → LogosStamps → Automatic. Each exported a same-size transparent PNG, preserved source bytes and returned to the signed empty editor.
- Live Enhancement → cutout passed: 480×360 → 1920×1440 → 1920×1440 transparent PNG, with source bytes preserved.
- Returning from cutout now waits read-only until the old result panel has retired before the final identity probe. The incorrectly targeted Open retry was removed.
- Isolated Debug build passed with zero warnings/errors. The existing debugger locks normal Debug PDB files; it was left running. Production database was not migrated or modified, and no commit was created.
- Local evidence: `artifacts/meitu-recognition-modes/verification.json`, targeted and live TRX files, and the five-mode transcript.
