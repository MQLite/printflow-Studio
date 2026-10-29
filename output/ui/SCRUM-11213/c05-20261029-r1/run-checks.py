"""Runs only existing, noninteractive C05 checks through the unchanged U09 wrapper."""
from collections import Counter
from pathlib import Path
import hashlib
import json
import os
import subprocess
import xml.etree.ElementTree as ET

OUT = Path(__file__).resolve().parent
ROOT = Path('C:/Users/admin/.codex/worktrees/scrum-11201-shared-theme/printflow-Studio')
PWSH = 'C:/Users/admin/.cache/codex-runtimes/codex-primary-runtime/dependencies/native/powershell/pwsh.exe'
NS = {'t': 'http://microsoft.com/schemas/VisualStudio/TeamTest/2010'}
env = {k: v for k, v in os.environ.items()
       if not (k.startswith(('PRINTFLOW', 'PF_OPUX')) or k.startswith('PF_') and ('CAPTURE' in k or k.endswith('_LAYOUT_DIR')))}
groups = [
    ('resource-and-review', 'FullyQualifiedName~LocalisationResourceTests|FullyQualifiedName~PlainChineseReviewCopyTests', False),
    ('accessibility', 'FullyQualifiedName~SessionAccessibilityTests|FullyQualifiedName~HomeRecentAccessibilityTests|FullyQualifiedName~WorkflowPurposeCardTests', False),
    ('print-copy', 'FullyQualifiedName~PlainChinesePrintCopyTests', True),
    ('save-recovery-copy', 'FullyQualifiedName~PlainChineseSaveRecoveryCopyTests', True),
]
results = []
for name, selection, large in groups:
    base = [PWSH, '-NoProfile', '-File', str(OUT / 'checks/run-safe-ui.ps1'), '-NoBuild', '-Include', selection]
    discovery = subprocess.run(base + ['-RunName', name + '-discovery', '-ListTests'], cwd=ROOT, env=env, capture_output=True)
    assert discovery.returncode == 0, discovery.stdout.decode(errors='replace')
    expected = [line.strip() for line in (OUT / f'checks/{name}-discovery.log').read_text(encoding='utf-8-sig').splitlines()
                if line.strip().startswith('PrintFlow.Tests.')]
    assert expected, f'Empty selection: {name}'
    assert not any('.Smoke.' in n for n in expected)
    exclusions = json.loads((OUT / 'checks/safe-ui-exclusions.json').read_text(encoding='utf-8-sig'))
    assert len(exclusions) == 11 and not any(x in n for x in exclusions for n in expected)
    if large:
        free = int(subprocess.check_output([PWSH, '-NoProfile', '-Command', '(Get-CimInstance Win32_OperatingSystem).FreePhysicalMemory']).decode().strip())
        if free < 5 * 1024 * 1024:
            results.append({'group': name, 'status': 'NOT RUN', 'reason': 'Existing U09 5 GiB free-memory render prerequisite not met',
                            'freePhysicalMemoryKiB': free, 'discovered': len(expected)})
            print(json.dumps(results[-1]), flush=True)
            continue
    run = subprocess.run(base + ['-RunName', name], cwd=ROOT, env=env, capture_output=True)
    trx = OUT / f'checks/{name}.trx'
    assert trx.is_file(), (name, run.returncode, run.stdout.decode(errors='replace'))
    tree = ET.parse(trx)
    rows = [{'name': x.attrib['testName'], 'outcome': x.attrib['outcome']}
            for x in tree.findall('.//t:UnitTestResult', NS)]
    errors = [x.findtext('t:Text', '', NS) for x in tree.findall('.//t:RunInfo', NS)
              if x.attrib.get('outcome', '').lower() in ('error', 'aborted')]
    counters = tree.find('.//t:Counters', NS).attrib
    exact = Counter(expected) == Counter(x['name'] for x in rows)
    complete = not errors and int(counters['total']) == int(counters['executed']) == len(rows)
    result = {'group': name, 'exitCode': run.returncode, 'discovered': len(expected), 'complete': complete,
              'exactInstances': exact, 'counts': dict(Counter(x['outcome'] for x in rows)), 'rows': rows,
              'trx': str(trx), 'sha256': hashlib.sha256(trx.read_bytes()).hexdigest(), 'runErrors': errors}
    (OUT / f'{name}-results.json').write_text(json.dumps(result, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')
    results.append({k: v for k, v in result.items() if k != 'rows'})
    print(json.dumps(results[-1], ensure_ascii=False), flush=True)
    assert complete and exact and run.returncode == 0 and all(x['outcome'] == 'Passed' for x in rows), name
summary = {'groups': results, 'counts': dict(sum((Counter(r.get('counts', {})) for r in results), Counter())),
           'excludedSmokeAndExact11': True, 'noBuild': True, 'humanAcceptance': 'NOT RUN',
           'realScreenReader': 'NOT RUN', 'physicalInput': 'NOT RUN', 'overallC05': 'PENDING_HUMAN_ACCEPTANCE'}
(OUT / 'test-summary.json').write_text(json.dumps(summary, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')
