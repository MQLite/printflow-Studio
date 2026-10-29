from pathlib import Path
from collections import Counter
import hashlib, json, subprocess

ROOT=Path(__file__).resolve().parents[5]
OUT=Path(__file__).resolve().parent
SOURCE=Path(r'C:/Users/admin/.codex/worktrees/scrum-11201-shared-theme/printflow-Studio')
PRIOR=SOURCE/'output/ui/SCRUM-11212/candidates/u09-visual-20261028-r1'
ARTIFACTS=ROOT/'artifacts/pf-opux-scrum11154-workstation-entry'
RUN=ARTIFACTS/'runs/a'

def read(p): return json.loads(p.read_text(encoding='utf-8-sig'))
def sha(p): return hashlib.sha256(p.read_bytes()).hexdigest()

assert ROOT==Path(subprocess.check_output(['git','rev-parse','--show-toplevel'],cwd=ROOT).decode().strip())
provenance=read(PRIOR/'workstation/candidate-provenance.json')
assert all(sha(SOURCE/r['Path']).upper()==r['Sha256'] for r in provenance['SourceFiles'])
assert all(sha(ROOT/r['Path']).upper()==r['Sha256'] for r in provenance['SourceFiles'])
manifest_path=ARTIFACTS/'manifests/u09-acceptance-20261028-a/candidate-manifest.json'
scenario_path=manifest_path.with_name('scenario-manifest.json')
manifest=read(manifest_path)
assert all(sha(Path(r['Path'])).upper()==r['Sha256'] for r in manifest['Files'])
prepared=read(RUN/'state/prepared.json'); ledger=read(RUN/'evidence/scenario-ledger.json'); graph=read(RUN/'evidence/graph-smoke.json')
assert prepared['CandidateHash']==sha(manifest_path).upper()
assert prepared['ScenarioHash']==sha(scenario_path).upper()
assert prepared['ScenarioLedgerSha256']==sha(RUN/'evidence/scenario-ledger.json').upper()
assert len(ledger['Scenarios'])==8 and all(r['Status']=='VERIFIED_SYNTHETIC_SERVICE_FACTS' for r in ledger['Scenarios'])
assert graph['WindowShown'] is False and graph['NativeDispatches']==0 and graph['ScenariosFailed'] is False
quiescence=[read(p) for p in (RUN/'evidence').glob('*quiescence*.json')]
assert quiescence and all(q['OwnedTasksSettled'] and q['PendingTasks']==0 and not q['WindowShown'] and q['HostFault'] is None for q in quiescence)
deliveries=[]
for scenario in ledger['Scenarios']:
    for fact in scenario['Facts']:
        if fact['Action']=='real isolated NTFS delivery':
            values=fact['Values']; p=Path(values['path']); assert p.is_relative_to(RUN/'delivery')
            assert values['code']=='Delivered' and sha(p).upper()==values['offerSha256']
            deliveries.append(dict(scenario=scenario['Id'],path=str(p),sha256=sha(p),delivered=True))
restart=read(OUT/'restart-qualified.json'); assert restart['exitCode']==0 and restart['facts']['Verified'] and restart['facts']['NewResults'] is False
recovery_path=ARTIFACTS/'runs/r/evidence/owned-child-recovery.json'
assert sha(recovery_path)==restart['recoverySha256']
screens=read(PRIOR/'screenshot-manifest.json'); images={r['path']:r['sha256'] for r in screens['captures']}
processing=read(OUT/'visual-processing-review.json'); completion=read(OUT/'visual-completion-review.json')
reviewed=processing['images']+completion['frames']
assert len(reviewed)==100 and Counter(r['path'] for r in reviewed)==Counter(images.keys())
assert all((r.get('actuallyViewed') or r.get('viewed')) and r['sha256']==images[r['path']]==sha(PRIOR/r['path']) for r in reviewed)
assert all(r['sourceManifestSha256']==sha(PRIOR/'source-final.json') for r in reviewed)
result={
    'date':'2026-10-28 Pacific/Auckland',
    'scope':'Noninteractive acceptance only, per current owner instructions',
    'sourceRoot':str(SOURCE),'executionRoot':str(ROOT),
    'sourceFilesByteIdentical':len(provenance['SourceFiles']),
    'originalSourceUnchanged':True,'newProductOrTestSourceChangesThisTurn':False,
    'candidateFilesVerified':len(manifest['Files']),
    'candidateHash':prepared['CandidateHash'],'scenarioHash':prepared['ScenarioHash'],
    'preparationExit':0,'scenarioLedgerSha256':prepared['ScenarioLedgerSha256'],
    'verifiedScenarioRows':8,'negativeCasesPreserved':['F1K KeepOriginalExtent promotion refused','F6 legacy maximum-box versus decoded-size mismatch'],
    'isolatedDeliveredFilesRehashed':deliveries,
    'ownedRestart':'VERIFIED, exact owned child only; no new result; not physical power loss',
    'screenshotsActuallyReviewed':100,'snapshotReviewOnly':True,
    'openVisualFindings':completion['materialIssues'],
    'knownFixtureObservations':completion['evidenceObservations'],
    'reviewReportsSha256':{name:sha(OUT/name) for name in ['visual-processing-review.json','visual-completion-review.json']},
    'desktopDecision':'OWNER_DEFERRED: 现在不方便，请先做不占桌面的检查',
    'noviceDecision':'NO_PARTICIPANT: 暂时无人参与，请先完成代理可执行的验收',
    'windowShown':False,'nativeDispatches':0,'systemScalingChanges':False,
    'realWindowDpiNativeDialogsInputReader':'NOT RUN — OWNER DEFERRED',
    'noviceAcceptance':'NOT RUN — NO PARTICIPANT',
    'all140StatesAccepted':False,'wholeU09':'PARTIAL — DESKTOP/HUMAN DEFERRED, VIS-COMP-01 OPEN',
    'headUnchanged':subprocess.check_output(['git','rev-parse','HEAD'],cwd=ROOT).decode().strip()==provenance['SourceHead'],
    'commitPushMergePublishJiraC05':'NOT RUN',
}
assert result['headUnchanged']
destination=OUT/'acceptance-summary.json'; assert not destination.exists()
destination.write_text(json.dumps(result,indent=2,ensure_ascii=False),encoding='utf8')
print(json.dumps({k:v for k,v in result.items() if k not in ['openVisualFindings','knownFixtureObservations','isolatedDeliveredFilesRehashed']},indent=2,ensure_ascii=False))
