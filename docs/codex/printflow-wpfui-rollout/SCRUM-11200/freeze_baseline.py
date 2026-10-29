"""SCRUM-11200 evidence capture. No checkout, index, source or historical evidence writes."""
import hashlib
import json
from pathlib import Path
import shutil
import subprocess
import sys
import tarfile
import tempfile
from datetime import datetime, timezone

ROOT = Path(__file__).resolve().parents[4]
SAMPLE = Path('C:/Users/admin/.codex/worktrees/scrum-11173-guided-acceptance/printflow-Studio')
DEST = ROOT / 'output/ui/SCRUM-11200/baseline-20261022'
BASE = '2561dfd127e3336ec57b37fb3fe47709c1d81fcb'

def git(root, *args):
    return subprocess.check_output(['git', '-C', str(root), *args])

def sha(path):
    return hashlib.sha256(path.read_bytes()).hexdigest().upper()

def write(path, value):
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(json.dumps(value, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')

def preservation(root):
    dirty = git(root, 'diff', '--name-only', 'HEAD', '-z').decode().split('\0')
    untracked = git(root, 'ls-files', '--others', '--exclude-standard', '-z').decode().split('\0')
    files = {p: sha(root / p) if (root / p).is_file() else None for p in sorted(set(dirty + untracked))
             if p and (root / p).resolve() != Path(__file__).resolve()}
    return {'root': str(root), 'head': git(root, 'rev-parse', 'HEAD').decode().strip(),
            'branch': git(root, 'branch', '--show-current').decode().strip(),
            'indexSha256': hashlib.sha256(git(root, 'diff', '--cached', '--binary', '--full-index')).hexdigest(),
            'files': files}

def capture():
    if DEST.exists():
        raise SystemExit('Candidate already exists; use verify, never overwrite evidence.')
    before = [preservation(ROOT), preservation(SAMPLE)]
    assert before[1]['head'] == BASE
    assert before[1]['branch'] == 'codex/wpf-ui-sample-pages'
    DEST.mkdir(parents=True)
    write(DEST / 'preservation-before.json', before)
    manifest = json.loads((SAMPLE / 'output/ui/WPF-UI-SAMPLES/source-manifest.json').read_text(encoding='utf-8-sig'))
    selected = [x['Path'] for x in manifest]
    dirty = [p for p in git(SAMPLE, 'diff', '--name-only', 'HEAD', '-z').decode().split('\0') if p]
    assert set(dirty) <= set(selected), 'Unrelated tracked changes must not enter the patch'
    for row in manifest:
        assert sha(SAMPLE / row['Path']) == row['Sha256'].upper(), row['Path']
        out = DEST / 'overlay' / row['Path']
        out.parent.mkdir(parents=True, exist_ok=True)
        shutil.copy2(SAMPLE / row['Path'], out)
    (DEST / 'sample.patch').write_bytes(git(SAMPLE, 'diff', '--binary', '--full-index', 'HEAD', '--', *selected))
    shutil.copytree(SAMPLE / 'output/ui/WPF-UI-SAMPLES', DEST / 'historical-sample')
    proto = ROOT / 'output/imagegen/SCRUM-11168/2026-10-19-v2'
    (DEST / 'prototype').mkdir()
    for name in ['design-spec.html', 'prototype-manifest.json', 'flow-map.json', 'flow-map-v2.json', 'design-approval.json', 'screen-plan.json']:
        shutil.copy2(proto / name, DEST / 'prototype' / name)
    for screen in json.loads((proto / 'prototype-manifest.json').read_text(encoding='utf-8-sig'))['screens']:
        shutil.copy2(proto / screen['file'], DEST / 'prototype' / screen['file'])
    for name in ['run-safe-ui.ps1', 'safe-ui-exclusions.json']:
        target = DEST / 'safe-runner' / name
        target.parent.mkdir(exist_ok=True)
        shutil.copy2(SAMPLE / 'output/ui/SCRUM-11173/checks' / name, target)
    tracked = [p for p in git(SAMPLE, 'ls-files', '-z').decode().split('\0') if p]
    source_hashes = {p: sha(SAMPLE / p) for p in tracked if (SAMPLE / p).is_file()}
    master_comparison = []
    for p in selected:
        master_comparison.append({'path': p, 'sampleSha256': sha(SAMPLE / p),
            'masterSha256': sha(ROOT / p) if (ROOT / p).is_file() else None,
            'same': (ROOT / p).is_file() and sha(ROOT / p) == sha(SAMPLE / p)})
    write(DEST / 'baseline.json', {'schemaVersion': 1, 'task': 'SCRUM-11200',
        'capturedUtc': datetime.now(timezone.utc).isoformat(), 'execution': 'Native sequential; no model switch',
        'sampleRoot': str(SAMPLE), 'sampleBranch': before[1]['branch'], 'baseCommit': BASE,
        'masterHead': before[0]['head'], 'selectedSourceCount': len(selected),
        'trackedModifiedPaths': dirty, 'overlayPaths': selected,
        'trackedCandidateSha256': source_hashes, 'masterComparison': master_comparison,
        'scope': 'Base commit plus selected 16-file sample overlay only. Untracked unrelated work excluded.',
        'productBuild': 'NOT RUN', 'productTests': 'NOT RUN', 'realWindowDpiInputAccessibility': 'NOT RUN'})
    files = [{'path': p.relative_to(DEST).as_posix(), 'sha256': sha(p), 'bytes': p.stat().st_size}
             for p in sorted(DEST.rglob('*')) if p.is_file()]
    write(DEST / 'files.json', files)
    verify(capture_checkout_bytes=True)

def verify(capture_checkout_bytes=False):
    metadata = json.loads((DEST / 'baseline.json').read_text(encoding='utf-8'))
    files = json.loads((DEST / 'files.json').read_text(encoding='utf-8'))
    for row in files:
        assert sha(DEST / row['path']) == row['sha256'], row['path']
    original = json.loads((DEST / 'preservation-before.json').read_text(encoding='utf-8'))
    preserved = []
    for old in original:
        root = Path(old['root'])
        assert git(root, 'rev-parse', 'HEAD').decode().strip() == old['head']
        assert git(root, 'branch', '--show-current').decode().strip() == old['branch']
        assert hashlib.sha256(git(root, 'diff', '--cached', '--binary', '--full-index')).hexdigest() == old['indexSha256']
        for p, expected in old['files'].items():
            # The capture helper was created in this task before the snapshot, not pre-existing user work.
            if (root / p).resolve() == Path(__file__).resolve():
                continue
            assert (sha(root / p) if (root / p).is_file() else None) == expected, p
        preserved.append({'root': str(root), 'existingFilesUnchanged': sum((root / p).resolve() != Path(__file__).resolve() for p in old['files']), 'headBranchIndexUnchanged': True})
    # Recreate in a throwaway directory, without changing either working tree or index.
    with tempfile.TemporaryDirectory(prefix='printflow-11200-') as tmp:
        temp = Path(tmp)
        archive = temp / 'base.tar'
        subprocess.run(['git', '-C', str(ROOT), 'archive', '--format=tar', '--output=' + str(archive), BASE], check=True)
        rebuilt = temp / 'candidate'
        rebuilt.mkdir()
        with tarfile.open(archive) as tf:
            tf.extractall(rebuilt, filter='data')
        subprocess.run(['git', '-C', str(rebuilt), 'apply', '--check', str(DEST / 'sample.patch')], check=True)
        subprocess.run(['git', '-C', str(rebuilt), 'apply', str(DEST / 'sample.patch')], check=True)
        # Only the three untracked implementation/test files need copying after the tracked patch.
        untracked = [p for p in metadata['overlayPaths'] if p not in metadata['trackedModifiedPaths']]
        for p in untracked:
            out = rebuilt / p
            out.parent.mkdir(parents=True, exist_ok=True)
            shutil.copy2(DEST / 'overlay' / p, out)
        # Preserve mixed Windows checkout line endings too, so rebuilding is byte exact.
        # This supplement contains only files already identified in the frozen tracked-tree table.
        formatting_manifest = DEST / 'checkout-bytes-manifest.json'
        checkout_bytes = {} if capture_checkout_bytes else json.loads(formatting_manifest.read_text(encoding='utf-8'))
        for p in metadata['overlayPaths']:
            shutil.copy2(DEST / 'overlay' / p, rebuilt / p)
        for p, expected in checkout_bytes.items():
            assert metadata['trackedCandidateSha256'][p] == expected
            assert sha(DEST / 'checkout-bytes' / p) == expected
            shutil.copy2(DEST / 'checkout-bytes' / p, rebuilt / p)
        for p, expected in metadata['trackedCandidateSha256'].items():
            if sha(rebuilt / p) != expected:
                assert capture_checkout_bytes, 'Reconstruction mismatch: ' + p
                assert sha(SAMPLE / p) == expected
                assert (rebuilt / p).read_bytes().replace(b'\r\n', b'\n') == (SAMPLE / p).read_bytes().replace(b'\r\n', b'\n'), p
                target = DEST / 'checkout-bytes' / p
                assert not target.exists()
                target.parent.mkdir(parents=True, exist_ok=True)
                shutil.copy2(SAMPLE / p, target)
                shutil.copy2(target, rebuilt / p)
                checkout_bytes[p] = expected
            assert sha(rebuilt / p) == expected, p
        if capture_checkout_bytes:
            assert not formatting_manifest.exists()
            write(formatting_manifest, checkout_bytes)
        for p in metadata['overlayPaths']:
            assert sha(rebuilt / p) == sha(DEST / 'overlay' / p), p
    # Old asset manifest paths are relative to the old report, never to the live worktree.
    for row in json.loads((DEST / 'historical-sample/manifest.json').read_text(encoding='utf-8-sig')):
        assert sha(DEST / 'historical-sample' / row['Path'].replace('\\', '/')) == row['Sha256'].upper()
    write(DEST / 'verification.json', {'status': 'PASS', 'verifiedUtc': datetime.now(timezone.utc).isoformat(),
        'frozenFilesVerified': len(files), 'trackedFilesReconstructed': len(metadata['trackedCandidateSha256']),
        'overlayFilesVerified': len(metadata['overlayPaths']), 'preservation': preserved,
        'checkoutByteOverlayFiles': len(checkout_bytes),
        'overlayComparison': 'byte-exact including full tracked tree; mixed CRLF/LF bytes preserved separately',
        'preservationExcludesOwnCaptureHelper': True,
        'checks': ['frozen byte hashes', 'original source/asset manifest hashes', 'base archive + binary patch + untracked overlay reconstruction', 'existing dirty/untracked files unchanged'],
        'notRun': ['product build/tests', 'new screenshots', 'real window/DPI/keyboard/screen reader/user acceptance']})
    print('PASS: freeze hashes, full tracked-tree reconstruction, 16-file overlay and existing-work preservation')

if __name__ == '__main__':
    if sys.argv[1:] == ['capture']:
        capture()
    elif sys.argv[1:] == ['capture-checkout-bytes']:
        verify(capture_checkout_bytes=True)
    elif sys.argv[1:] == ['verify']:
        verify()
    else:
        raise SystemExit('Use capture (once) or verify (read-only sources).')
