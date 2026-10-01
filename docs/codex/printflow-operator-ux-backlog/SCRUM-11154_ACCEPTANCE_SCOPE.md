# SCRUM-11154 — acceptance scope: owner's English-test decision

Recorded 2026-10-01 for task `PF-OPUX-v1-SCRUM-11154-zh-findings-fix-v1`. This document records a later scope decision. It does not edit the original Jira description or acceptance criteria, and it does not change any earlier result.

## The owner's instruction

The owner, when authorizing the findings remediation, instructed verbatim:

> 不需要安排英文测试

Meaning, as applied here: do not arrange English testing — no English workstation session, English input journey, English screenshot set or English operator round.

## How it is applied

| Item | Status |
|---|---|
| English workstation / journey / physical input / screenshot checks for SCRUM-11154 | **NOT RUN — NOT REQUIRED BY OWNER** |
| Earlier recorded English results (e.g. "en NOT RUN" in the [observations](SCRUM-11154_WORKSTATION_OBSERVATIONS.md) and [ledger](SCRUM-11154_WORKSTATION_OBSERVATION_LEDGER.json)) | Unchanged history. Never converted to PASS. |
| Remaining operator/workstation verification | Chinese (zh-CN) only: [SCRUM-11154_ZH_RETEST_CHECKLIST.md](SCRUM-11154_ZH_RETEST_CHECKLIST.md) |
| English UI, translations and resource structure | Kept. Every new resource key exists in both `Strings.resx` and `Strings.zh-CN.resx`; existing structural localisation checks keep running. |
| Existing language-parameterized automated tests | May still run inside an otherwise necessary affected regression set. They are not an English acceptance task and add no English acceptance claim. |
| New captures in this batch | zh-CN off-screen only. |

## What the decision does not do

- It does not waive Chinese checks, defects, run isolation, exact-result protections, F6 (200×150 mm maximum box versus 6×5 px TIFF) or novice validation.
- It does not accept the existing English copy or close any other open item.
- Original AC1 still names both languages. The English half of AC1 is out of scope by the owner's instruction, not passed. Final acceptance of that criterion as worded remains the owner's decision in Jira.
- It does not approve any earlier Task or turn any earlier NOT RUN into PASS.
