# SCRUM-11153 independent review

One fresh read-only reviewer was assigned the original AC, bounded brief, current diff, tests and synthetic screenshots. Requested route: Astra High; actual runtime model/effort UNVERIFIED. The reviewer made no edits or Jira writes and did not run tests.

## First pass

Three P2 findings:

1. Recent's default SQLite transaction could unnecessarily lock a writer; use a deferred read transaction.
2. A TIFF delivery could still count after its upstream source was invalidated; validate lineage, TIFF twin/producing attempt and latest approval.
3. Existing rows retained prior-language text after a language change; reword the current snapshot without running work.

All three were fixed in scope. Behavior tests for invalidated source and existing-row language change showed RED before each fix and PASS afterwards. An unavailable-history test confirms unknown is distinct from zero.

## Final pass

The same reviewer rechecked the three fixes, current diff and test source and reported no remaining actionable code findings. It inspected ten mixed/expanded and correction/recovery PNGs in both languages and both viewports: visible status, history and controls did not overlap; expanded 1000×700 retained a usable scrolling region. It then inspected two added 1000×700 long-name captures and confirmed the full filename, metadata, status and Resume/Abandon controls are visible in English and Chinese.

Review limit: stopped, abandoned and processing status had text assertions but were not separately pictured. The reviewer did not rerun coordinator-reported test commands. Real input, non-96-DPI and human copy acceptance remain open. This is technical review of the bounded code and evidence, not product acceptance.
