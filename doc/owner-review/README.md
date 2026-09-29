# Owner-review packets

One self-contained folder, and one ZIP of it, per issue per run. The owner uploads the ZIP for the next decision instead of collecting files from several folders. Introduced by SCRUM-11152; every later PF-OPUX task produces one the same way.

This README is the only tracked file here. Packet folders, ZIPs and `LATEST.md` are local generated deliverables and are ignored by Git (see the `/doc/owner-review/` rules in `.gitignore`). The canonical documents under `docs/` are published exactly as before; a packet only copies them.

## Layout

```
doc/owner-review/
  README.md                          # this convention (tracked)
  LATEST.md                          # local pointer: latest completed packet, ZIP path, ZIP SHA-256
  SCRUM-<n>/
    <UTC-run-id>-<code-sha7>/        # real copies; immutable once finalised
      00_REVIEW_BRIEF.md             # Chinese owner summary: SHAs, Jira status, AC table, decisions, gaps, manual checks
      SCRUM-<n>_PLAN.md
      SCRUM-<n>_COPY_REVIEW.md       # when the task has owner-reviewed wording
      SCRUM-<n>_RESULTS.md
      SCRUM-<n>_INDEPENDENT_REVIEW.md
      HANDOFF.md                     # snapshot of the current top section and history
      SCRUM-<n>_JIRA_FINAL.csv
      SCRUM-<n>_JIRA_READBACK.redacted.json
      SCRUM-<n>_PUBLICATION_STATUS_AUDIT.json
      SCOPED.diff                    # product/test delta against the task's real baseline
      screenshots/                   # final synthetic PNGs + INDEX.md (state, locale, viewport, DPI, candidate)
      related-pending/               # still-open decisions from earlier issues, copied, originals untouched
      MANIFEST.json
    <UTC-run-id>-<code-sha7>.zip     # beside the folder, never inside it
```

`<UTC-run-id>` is `yyyyMMddTHHmmssZ` at packet build time; `<code-sha7>` is the task's published code commit.

## Rules

1. **Build last.** Only after code and docs are pushed and verified, the final Jira readback is taken and the local audit is settled. A partial run says so in `00_REVIEW_BRIEF.md` and `MANIFEST.json`; `LATEST.md` never points at an incomplete packet as though it were final.
2. **Copy by allowlist**, never a recursive copy of an evidence folder. Real file copies: no links, shortcuts or absolute paths the reader cannot open. Rewrite relative links inside packet copies when needed and record the rewrite; never edit the originals.
3. **Privacy.** No raw Jira/API responses, account IDs, e-mail/avatar metadata, credentials, runtime databases, customer files or workstation user paths. The readback copy is a documented, redacted derivative that keeps IDs/keys, full descriptions and AC, statuses, labels, links and timestamp strings.
4. **Provenance.** `MANIFEST.json` lists every payload with purpose, repository-relative source, source SHA-256, packet SHA-256, class (`copy`, `redacted`, `excerpt`, `generated`) and any transformation, plus candidate, publication and Jira snapshot metadata. A plain copy's hashes match; a transformed copy never claims byte identity. The manifest does not hash itself or the ZIP.
5. **Validate, then zip.** Parse JSON/CSV, check required files, links and hashes, and check the redaction kept business fields. Zip only the finished folder, reopen the ZIP, extract it to a temporary folder and compare every entry. Record the ZIP SHA-256 in `LATEST.md` and the final report. Never overwrite an earlier run.
6. **Never commit a packet.** Commit only this README and the narrow ignore rules.
