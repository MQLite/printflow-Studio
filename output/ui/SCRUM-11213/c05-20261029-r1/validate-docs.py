"""Structural QA for the C05 preparation packet; not a participant simulation."""
from collections import Counter
from pathlib import Path
import hashlib
import json
import re
import xml.etree.ElementTree as ET

ROOT = Path('D:/Repositories/printflow-Studio')
DOCS = ROOT / 'docs/codex/printflow-wpfui-rollout/SCRUM-11213'
OUT = Path(__file__).resolve().parent
ns = {'t': 'http://microsoft.com/schemas/VisualStudio/TeamTest/2010'}

for p in DOCS.glob('*.json'):
    json.loads(p.read_text(encoding='utf-8-sig'))
template = json.loads((DOCS / 'SESSION_TEMPLATE.json').read_text(encoding='utf-8'))
assert template['templateOnly'] and template['result'] == 'NOT RUN'
assert template['participantCode'] is None and not template['observations']
ids = re.findall(r'^\| (T\d{2}) /', (DOCS / 'SCENARIOS.md').read_text(encoding='utf-8'), re.M)
assert ids == template['plannedCoreScenarios'] == [f'T{i:02}' for i in range(1, 19)]
required_fields = ('currentStepUnderstanding', 'nextActionUnderstanding', 'resultUnderstanding',
                   'criticalMisjudgment', 'participantQuotation', 'helpRequestedAndGiven',
                   'wrongActionsAndSelfCorrection', 'attemptType', 'priorFailedAttemptReference')
assert all(k in template['observationFieldGuide'] for k in required_fields)

summary = json.loads((OUT / 'test-summary.json').read_text(encoding='utf-8'))
total = Counter()
for group in summary['groups']:
    if group.get('status') == 'NOT RUN':
        assert group['reason'] and group['freePhysicalMemoryKiB'] < 5 * 1024 * 1024
        continue
    trx = Path(group['trx'])
    assert hashlib.sha256(trx.read_bytes()).hexdigest() == group['sha256']
    tree = ET.parse(trx)
    rows = tree.findall('.//t:UnitTestResult', ns)
    expected = [line.strip() for line in (OUT / f"checks/{group['group']}-discovery.log").read_text(encoding='utf-8-sig').splitlines()
                if line.strip().startswith('PrintFlow.Tests.')]
    assert Counter(expected) == Counter(x.attrib['testName'] for x in rows)
    assert group['counts'] == dict(Counter(x.attrib['outcome'] for x in rows))
    total.update(group['counts'])
assert dict(total) == summary['counts'] == {'Passed': 127}
assert sum(g['discovered'] for g in summary['groups'] if g.get('status') == 'NOT RUN') == 67

missing = []
checked = 0
for path in DOCS.glob('*.md'):
    for target in re.findall(r'\]\(([^)]+)\)', path.read_text(encoding='utf-8')):
        if re.match(r'https?://', target):
            continue
        # REVIEW is created after the independent review returns.
        p = (path.parent / target.split('#')[0]).resolve()
        if p.name == 'REVIEW.md' and not p.exists():
            continue
        checked += 1
        if not p.exists():
            missing.append({'document': str(path), 'target': target})
assert not missing, missing
result = {'status': 'PASS', 'scenarioCount': len(ids), 'emptyTemplateVerified': True,
          'executedTestInstances': 127, 'deferredTestInstances': 67,
          'localLinksChecked': checked, 'missingLinks': missing,
          'scope': 'packet structure and existing evidence; no human observations created'}
path = OUT / 'document-qa.json'
assert not path.exists()
path.write_text(json.dumps(result, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')
print(json.dumps(result))
