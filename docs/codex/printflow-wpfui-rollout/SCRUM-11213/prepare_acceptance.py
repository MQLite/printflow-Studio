"""C05 preparation evidence only. Never launches the application or a desktop session.

freeze records the existing candidate without changing it; verify rechecks its identity.
The frozen record is deliberately not a workstation-entry manifest or permission to run it.
"""
from collections import Counter
from pathlib import Path
import argparse
import hashlib
import json
import re
import subprocess
import xml.etree.ElementTree as ET

DOCS = Path(__file__).resolve().parent
REPO = DOCS.parents[3]
SHARED = Path('C:/Users/admin/.codex/worktrees/scrum-11201-shared-theme/printflow-Studio')
CANDIDATE = Path('C:/Users/admin/.codex/worktrees/u09/printflow-Studio')
PRIOR = SHARED / 'output/ui/SCRUM-11212/candidates/u09-visual-20261028-r1'
RESX = 'src/PrintFlow.App/Resources/'


def sha(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def read(path):
    return json.loads(path.read_text(encoding='utf-8-sig'))


def save(path, data):
    if path.exists():
        raise FileExistsError(f'Preserve existing evidence: {path}')
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(json.dumps(data, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')


def git(root, *args):
    return subprocess.check_output(['git', *args], cwd=root)


def state(root):
    names = git(root, 'ls-files', '--cached', '--others', '--exclude-standard', '-z').decode().split('\0')
    # Existing evidence and build outputs are not part of this source/document protection set.
    names = [n for n in names if n and n.split('/')[0] not in ('output', 'artifacts')]
    return {'root': str(root), 'head': git(root, 'rev-parse', 'HEAD').decode().strip(),
            'branch': git(root, 'branch', '--show-current').decode().strip(),
            'index': hashlib.sha256(git(root, 'diff', '--cached', '--binary')).hexdigest(),
            'files': {n: sha(root / n) for n in sorted(names) if (root / n).is_file()}}


def resources(path):
    rows = ET.parse(path).getroot().findall('data')
    values = {x.attrib['name']: x.findtext('value') for x in rows}
    assert len(rows) == len(values), f'Duplicate keys: {path}'
    assert all(v and v.strip() for v in values.values()), f'Blank resource: {path}'
    return values


def freeze(out):
    save(out / 'preservation-before.json', {str(r): state(r) for r in (REPO, SHARED, CANDIDATE)})
    old = read(PRIOR / 'source-final.json')
    assert all(sha(SHARED / p) == h for p, h in old['files'].items()), 'Prior test source drift'
    assert all(sha(SHARED / p) == h for p, h in old['binaries'].items()), 'Prior test binary drift'
    manifest = CANDIDATE / 'artifacts/pf-opux-scrum11154-workstation-entry/manifests/u09-acceptance-20261028-a/candidate-manifest.json'
    prior = read(manifest)
    assert all(sha(Path(x['Path'])).lower() == x['Sha256'].lower() for x in prior['Files']), 'U09 candidate drift'
    source = {p: h for p, h in old['files'].items()}
    assert all(sha(CANDIDATE / p) == h for p, h in source.items()), 'Candidate differs from test source'
    bins = {p.relative_to(SHARED).as_posix(): sha(p)
            for p in (SHARED / 'tests/PrintFlow.Tests/bin/Debug').rglob('*') if p.is_file()}
    refs = [REPO / 'docs/codex/printflow-wpfui-rollout/PLAN.md',
            REPO / 'docs/printflow/operator-ux-acceptance-walkthrough.md',
            REPO / 'docs/printflow/operator-ux-novice-walkthrough.md',
            CANDIDATE / 'docs/codex/printflow-wpfui-rollout/SCRUM-11212/AGENT_ACCEPTANCE_20261028.md',
            CANDIDATE / 'output/ui/SCRUM-11212/acceptance/u09-workstation-20261028-r1/acceptance-summary.json']
    en, zh = (resources(CANDIDATE / (RESX + n)) for n in ('Strings.resx', 'Strings.zh-CN.resx'))
    assert en.keys() == zh.keys(), 'Resource key mismatch'
    placeholder = re.compile(r'(?<!\{)\{\d+(?:,-?\d+)?(?:\:[^{}]+)?\}(?!\})')
    mismatch = [k for k in en if Counter(placeholder.findall(en[k])) != Counter(placeholder.findall(zh[k]))]
    assert not mismatch, mismatch
    typed = (CANDIDATE / (RESX + 'Strings.cs')).read_text(encoding='utf-8-sig')
    accessors = set(re.findall(r'Get\(nameof\((\w+)\)\)', typed))
    assert accessors and accessors <= en.keys(), 'Unresolved typed accessor'
    baseline_path = SHARED / 'output/ui/SCRUM-11204/source-before/Strings.zh-CN.resx'
    baseline = resources(baseline_path)
    histories = {}
    for issue in ('11204', '11205', '11206'):
        path = SHARED / f'docs/codex/printflow-wpfui-rollout/SCRUM-{issue}/COPY_CHANGES.json'
        refs.append(path)
        for row in read(path)['rows']:
            histories.setdefault(row['key'], []).append({'task': 'SCRUM-' + issue, **row})
    changes = [{'key': k, 'before': baseline.get(k), 'current': zh.get(k),
                'history': histories.get(k, []),
                'laterThanLastCopyStage': bool(histories.get(k) and histories[k][-1]['after'] != zh.get(k))}
               for k in sorted(baseline.keys() | zh.keys()) if baseline.get(k) != zh.get(k)]
    rows = []
    for path in sorted((CANDIDATE / 'src/PrintFlow.App').rglob('*')):
        if path.suffix not in ('.xaml', '.cs') or {'bin', 'obj'} & set(path.parts):
            continue
        for line_no, line in enumerate(path.read_text(encoding='utf-8-sig').splitlines(), 1):
            if 'AutomationProperties.Name' in line or 'AutomationProperties.SetName' in line:
                rows.append({'path': path.relative_to(CANDIDATE).as_posix(), 'line': line_no, 'source': line.strip()})
    save(out / 'accessibility-source-index.json', rows)
    save(DOCS / 'COPY_COMPARISON.json', {'baseline': str(baseline_path), 'baselineSha256': sha(baseline_path),
         'candidate': str(CANDIDATE), 'rows': changes})
    def cell(value):
        return (value or '(absent)').replace('|', '\\|').replace('\r', '').replace('\n', '<br>')
    md = ['# C05 中文文案前后对照', '',
          '基线为 C02 改写前的工作字节；当前值取自冻结的 U09 候选。完整阶段历史见 COPY_COMPARISON.json。', '',
          '后续 UI/U09 改动与 C02–C04 阶段末值不同的项目单独标注；本表不是用户理解度结论。', '',
          '| 资源键 | 改写前 | 当前候选 | 阶段记录及后续变化 |', '|---|---|---|---|']
    for x in changes:
        stages = ', '.join(h['task'] for h in x['history']) or 'UI/U09 或其他后续改动'
        if x['laterThanLastCopyStage']:
            stages += '；后续已变化'
        md.append('| ' + ' | '.join(map(cell, (x['key'], x['before'], x['current'], stages))) + ' |')
    path = DOCS / 'COPY_COMPARISON.md'
    assert not path.exists()
    path.write_text('\n'.join(md) + '\n', encoding='utf-8')
    source_digest = hashlib.sha256(json.dumps(source, sort_keys=True).encode()).hexdigest()
    identity = {'date': '2026-10-29 Pacific/Auckland', 'scope': 'PREPARATION_ONLY',
                'sourceRoot': str(CANDIDATE), 'testRoot': str(SHARED),
                'sourceHead': prior['SourceHead'], 'sourceFiles': source,
                'sourceSetSha256': source_digest, 'testRuntimeFiles': bins,
                'u09CandidateManifest': str(manifest), 'u09CandidateManifestSha256': sha(manifest),
                'u09CandidateFilesVerified': len(prior['Files']),
                'references': {str(p): sha(p) for p in refs},
                'humanWalkthrough': 'NOT RUN', 'overallC05': 'PENDING_HUMAN_ACCEPTANCE',
                'workstationLaunchQualification': 'NOT CLAIMED; fresh run and qualification required'}
    save(DOCS / 'CANDIDATE.json', identity)
    save(out / 'resource-checks.json', {'status': 'PASS', 'englishKeys': len(en), 'chineseKeys': len(zh),
         'matchingFullPlaceholderTokens': True, 'typedAccessorsChecked': len(accessors),
         'baselineChangedKeys': len(changes), 'laterThanLastCopyStage': [x['key'] for x in changes if x['laterThanLastCopyStage']],
         'accessibleNameSourceSites': len(rows), 'hardcodedCopy': 'No product edits in C05; existing repository-wide absence not claimed',
         'limits': 'XML/source checks are not semantic compilation, screen reader or human comprehension evidence'})
    checks = out / 'checks'
    checks.mkdir()
    for name in ('run-safe-ui.ps1', 'safe-ui-exclusions.json'):
        data = (PRIOR / 'checks' / name).read_bytes()
        (checks / name).write_bytes(data)
    assert len(read(checks / 'safe-ui-exclusions.json')) == 11
    print(json.dumps({'sourceFiles': len(source), 'runtimeFiles': len(bins), 'resourceKeys': len(en),
                      'changedKeys': len(changes), 'candidateFiles': len(prior['Files'])}))


def verify(out):
    identity = read(DOCS / 'CANDIDATE.json')
    for p, h in identity['sourceFiles'].items():
        assert sha(CANDIDATE / p) == h and sha(SHARED / p) == h, p
    assert all(sha(SHARED / p) == h for p, h in identity['testRuntimeFiles'].items()), 'Runtime changed'
    assert all(sha(Path(p)) == h for p, h in identity['references'].items()), 'Reference changed'
    assert sha(Path(identity['u09CandidateManifest'])) == identity['u09CandidateManifestSha256']
    assert all(sha(Path(x['Path'])).lower() == x['Sha256'].lower()
               for x in read(Path(identity['u09CandidateManifest']))['Files'])
    protected = []
    for root, before in read(out / 'preservation-before.json').items():
        after = state(Path(root))
        assert all(after[k] == before[k] for k in ('head', 'branch', 'index')), root
        assert all(after['files'].get(p) == h for p, h in before['files'].items()), root
        added = sorted(after['files'].keys() - before['files'].keys())
        assert not added or root == str(REPO) and all(p.startswith('docs/codex/printflow-wpfui-rollout/SCRUM-11213/') for p in added), added
        protected.append({'root': root, 'existingFilesUnchanged': len(before['files']), 'newFiles': added})
    save(out / 'preservation-final.json', {'status': 'PASS', 'roots': protected,
         'sourceAndRuntimeUnchanged': True, 'oldU09EvidenceReadOnly': True,
         'scope': 'Git-visible source/docs/config; output/artifacts excluded, candidate manifest and test runtime separately checked'})
    print('PASS: frozen candidate, runtime, references and existing source/documents preserved')


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('mode', choices=('freeze', 'verify'))
    parser.add_argument('--output', required=True, type=Path)
    args = parser.parse_args()
    (freeze if args.mode == 'freeze' else verify)(args.output.resolve())
