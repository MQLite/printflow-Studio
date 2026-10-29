"""Final packet check after the independent review's neutral-prompt correction."""
from pathlib import Path
import hashlib
import json
import re

ROOT = Path('D:/Repositories/printflow-Studio')
DOCS = ROOT / 'docs/codex/printflow-wpfui-rollout/SCRUM-11213'
OUT = Path(__file__).resolve().parent
report = OUT / 'document-qa.json'
assert not report.exists()
for path in DOCS.glob('*.json'):
    json.loads(path.read_text(encoding='utf-8-sig'))
rows = {}
for line in (DOCS / 'SCENARIOS.md').read_text(encoding='utf-8').splitlines():
    if re.match(r'^\| T\d{2} /', line):
        cells = [s.strip() for s in line.split('|')]
        rows[cells[1].split()[0]] = cells[2]
template = json.loads((DOCS / 'SESSION_TEMPLATE.json').read_text(encoding='utf-8'))
assert list(rows) == template['plannedCoreScenarios'] == [f'T{i:02}' for i in range(1, 19)]
assert rows['T11'] == rows['T12'] == '请查看这次保存后的情况，说说目前可以确定什么，再选择下一步。'
assert template['templateOnly'] and template['result'] == 'NOT RUN' and not template['observations']
targets = []
for path in DOCS.glob('*.md'):
    for target in re.findall(r'\]\(([^)]+)\)', path.read_text(encoding='utf-8')):
        if not re.match(r'https?://', target):
            p = (path.parent / target.split('#')[0]).resolve()
            assert p == report or p.exists(), (path.name, target)
            targets.append(p)
assert json.loads((OUT / 'preservation-final.json').read_text(encoding='utf-8'))['status'] == 'PASS'
data = {'status': 'PASS', 'scenarioCount': len(rows), 'emptyTemplateVerified': True,
        'reviewFinding': 'T11/T12 now use the identical neutral prompt; author checked after independent review',
        'localLinksChecked': len(targets), 'missingLinks': [], 'productRetest': 'Not repeated; product/test source and runtime unchanged'}
report.write_text(json.dumps(data, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')
assert all(p.exists() for p in targets)
manifest = {str(p): hashlib.sha256(p.read_bytes()).hexdigest() for p in DOCS.iterdir() if p.is_file()}
(OUT / 'packet-manifest.json').write_text(json.dumps(manifest, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')
print(json.dumps(data, ensure_ascii=False))
