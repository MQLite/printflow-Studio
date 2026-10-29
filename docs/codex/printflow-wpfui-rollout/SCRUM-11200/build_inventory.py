"""Generate the reviewable page/state ledger from the approved 11174 matrix and current source."""
import csv
import hashlib
import json
from pathlib import Path
import re

HERE = Path(__file__).resolve().parent
ROOT = HERE.parents[3]
BASELINE = ROOT / 'output/ui/SCRUM-11200/baseline-20261022'
SAMPLE = Path('C:/Users/admin/.codex/worktrees/scrum-11173-guided-acceptance/printflow-Studio')
PROTO = BASELINE / 'prototype'
ALL = ['PrepareAsset', 'PrepareCustomerDesign', 'GeneratePrintTiff']
MEITU = ALL[:2]
TIFF = ALL[1:]
GUIDED = 'src/PrintFlow.App/ViewModels/SessionViewModel.Guided.cs'
SESSION = 'src/PrintFlow.App/ViewModels/SessionViewModel.cs'
SAVE = 'src/PrintFlow.App/ViewModels/SessionViewModel.FinalSave.cs'
HOME = 'src/PrintFlow.App/ViewModels/HomeViewModel.cs'
rows = []
candidate_hashes = json.loads((BASELINE / 'baseline.json').read_text(encoding='utf-8'))['trackedCandidateSha256']

# id: GuidedSessionPage, route applicability, migration task, copy task, source file, source anchor
groups = [
 ('home input-error', '-', ALL, '11207', '11204', HOME, 'HomeViewModel'),
 ('recent abandon', '-', ALL, '11211', '11206', HOME, 'HomeViewModel'),
 ('route', '-', ALL, '11207', '11204', 'src/PrintFlow.App/ViewModels/WorkflowSelectionViewModel.cs', 'WorkflowSelectionViewModel'),
 ('original', 'Source', MEITU, '11207', '11204', GUIDED, 'ShowsGuidedSourceDetails'),
 ('pdf-psd', 'SourcePreparation', ALL, '11207', '11204', GUIDED, 'SourcePreparation'),
 ('enhance-ready background-ready', 'Ready', MEITU, '11208', '11204', GUIDED, 'Ready'),
 ('enhance-running background-running', 'Processing', MEITU, '11208', '11204', GUIDED, 'GuidedProcessingState'),
 ('enhance-review review crop-review', 'Review', MEITU, '11208', '11204', GUIDED, 'IsReviewRequired'),
 ('crop', 'Crop', MEITU, '11208', '11204', GUIDED, 'IsCropping'),
 ('dimensions', 'Dimensions', TIFF, '11209', '11205', GUIDED, 'ShowsGuidedDimensions'),
 ('white-ink', 'WhiteInk', TIFF, '11209', '11205', GUIDED, 'ShowsGuidedWhiteInk'),
 ('preflight enlargement', 'Preflight', TIFF, '11209', '11205', GUIDED, 'ShowsGuidedPreflight'),
 ('tiff-running', 'Processing', TIFF, '11209', '11205', GUIDED, 'GuidedProcessingState'),
 ('tiff-review white-preview', 'Review', TIFF, '11209', '11205', 'src/PrintFlow.App/Views/TiffReviewSurface.cs', 'TiffReviewSurface'),
 ('png-final png-save png-saved', 'Save', ALL[:1], '11210', '11206', SAVE, 'HasFinalSave'),
 ('save multi-size saved', 'Save', TIFF, '11210', '11206', SAVE, 'HasFinalSave'),
 ('completed', 'Completed', ALL, '11210', '11206', GUIDED, 'Completed'),
 ('save-failed save-uncertain collision', 'Save', ALL, '11210', '11206', SAVE, 'FinalSaveKind'),
 ('correction-prepare correction-wait', 'Correction', MEITU, '11211', '11206', GUIDED, 'IsAskingColleague'),
 ('handed-off recovery', 'Recovery', ALL, '11211', '11206', GUIDED, 'IsHandedOff'),
 ('failure', 'Failure', ALL, '11211', '11206', GUIDED, 'IsStoppedStep'),
 ('stopping', 'Processing', ALL, '11211', '11206', GUIDED, 'StopRequested'),
 ('takeover', 'Processing', TIFF, '11211', '11206', GUIDED, 'TakeOverRequested'),
 ('return-confirm draft-leave', '-', ALL, '11211', '11206', GUIDED, 'GuidedInteractionIdentity'),
 ('readiness', '-', ALL, '11211', '11206', 'src/PrintFlow.App/ViewModels/EnvironmentReadinessViewModel.cs', 'EnvironmentReadinessViewModel'),
 ('settings', '-', ALL, '11211', '11206', 'src/PrintFlow.App/Views/SettingsView.xaml', 'Screen.Settings'),
 ('error-details', '-', ALL, '11211', '11206', 'src/PrintFlow.App/ViewModels/ErrorDetailsViewModel.cs', 'ErrorDetailsViewModel'),
 ('ai-planned', '-', ['ConditionalAi'], '11211', '11206', None, None),
]
mapping = {name: values for names, *values in groups for name in names.split()}
screens = json.loads((PROTO / 'prototype-manifest.json').read_text(encoding='utf-8-sig'))['screens']
screen_by_id = {s['id']: s for s in screens}

def source_ref(path, anchor):
    if path is None:
        return {'path': None, 'anchor': None, 'line': None, 'sha256': None, 'status': 'CONDITIONAL_NOT_IMPLEMENTED'}
    p = SAMPLE / path
    text = p.read_text(encoding='utf-8-sig')
    assert anchor in text, (path, anchor)
    digest = hashlib.sha256(p.read_bytes()).hexdigest().upper()
    assert candidate_hashes[path] == digest, 'Source drift from frozen candidate: ' + path
    return {'path': path, 'anchor': anchor, 'line': text[:text.index(anchor)].count('\n') + 1,
            'sha256': digest, 'status': 'SOURCE_CHECKED'}

def add(identifier, title, proto_id, state, condition, page=None, routes=None, source=None, anchor=None, guard=None):
    default_page, default_routes, owner, copy, default_source, default_anchor = mapping[proto_id]
    routes = routes or default_routes
    conditional = routes == ['ConditionalAi']
    fmt = ([] if conditional else (['PNG'] if ALL[0] in routes else []) + (['TIFF'] if any(r in TIFF for r in routes) else []))
    screen = screen_by_id[proto_id]
    sample_images = []
    if proto_id in ('home', 'png-save'):
        prefix = 'home' if proto_id == 'home' else 'save-png'
        sample_images = ['output/ui/SCRUM-11200/baseline-20261022/historical-sample/screenshots/' + prefix + '-' + size + '.png' for size in ['1000x700', '1280x720', '1920x1080']]
    rows.append({'id': identifier, 'page': title, 'guidedPage': page or default_page,
        'state': state, 'condition': condition, 'routes': routes, 'outputs': fmt,
        'migrationTask': 'SCRUM-' + owner, 'copyTask': 'SCRUM-' + copy,
        'inventoryTask': 'SCRUM-11200', 'copyInventoryTask': 'SCRUM-11203',
        'validationTask': 'SCRUM-11212', 'priority': 'Medium' if conditional or proto_id in ['recent', 'settings', 'error-details'] else 'High',
        'implementation': 'CONDITIONAL_NOT_IMPLEMENTED' if conditional else 'EXISTING_SURFACE; WPF_UI_ROLLOUT_PENDING',
        'prototype': 'output/ui/SCRUM-11200/baseline-20261022/prototype/' + screen['file'],
        'prototypeId': proto_id, 'sample': sample_images,
        'sampleStatus': 'HISTORICAL_96_DPI_OFFSCREEN_REFERENCE_ONLY' if sample_images else 'NOT RUN',
        'currentStateScreenshot': 'NOT RUN',
        'source': source_ref(source or default_source, anchor or default_anchor),
        'visibleIntent': screen['intent'], 'primaryActionReference': screen['primary'],
        'hiddenOrDisabled': screen.get('hidden', '按冻结design-spec.html第3节状态显隐矩阵；关键阻塞/停止/接管持续可达。'),
        'guard': guard or '沿用真实命令资格；当前任务/步骤/结果身份；不自动审核或触发下一步。',
        'automatedTestThisTask': 'NOT RUN', 'visualTestThisTask': 'NOT RUN',
        'realWindowDpiKeyboardScreenReader': 'NOT RUN',
        'evidence': '源码锚点已核对；原型为设计参考；本项未运行产品或视觉验收。'})

for screen in screens:
    key = screen['id']
    state = 'normal'
    if key in ['enhance-running', 'background-running', 'tiff-running']: state = 'loading'
    if key in ['enhance-review', 'review', 'crop-review', 'tiff-review']: state = 'review'
    if key in ['input-error', 'failure', 'save-failed', 'collision']: state = 'error'
    if key in ['return-confirm', 'draft-leave', 'abandon', 'takeover', 'enlargement']: state = 'confirmation'
    if key in ['recovery', 'handed-off', 'correction-wait', 'stopping']: state = 'recovery'
    if key == 'ai-planned': state = 'conditional'
    add('P-' + key, screen['title'], key, state, screen['intent'])

# State-specific rows supplement the original 45 screens rather than inventing every Cartesian combination.
extras = [
 ('home-empty', '首页无最近任务', 'home', 'empty', '无可继续任务；仍显示真实环境状态和单图导入。', None),
 ('home-importing', '首页正在导入', 'home', 'loading', '导入未完成；禁止重复提交，取消沿用原命令。', None),
 ('home-disabled', '首页导入/继续被禁用', 'home', 'disabled', '忙碌或资格不足；显示原因，不能由样式重新启用。', None),
 ('recent-empty', '最近任务空列表', 'recent', 'empty', '没有历史记录；不虚构预览或可恢复任务。', None),
 ('recent-history', '最近任务有保存历史', 'recent', 'history', '有记录不等于文件当前存在；打开/继续仍核对真实资格。', None),
 ('recent-pending', '最近任务待处理/恢复', 'recent', 'recovery', '区分待审核、失败、中断及已放弃；不能自动重启尝试。', None),
 ('route-unselected', '路线尚未选择', 'route', 'disabled', '没有选择时确认不可执行；保留原图上下文。', None),
 ('route-asset', '准备设计素材路线', 'route', 'normal', 'PNG；不显示尺寸、白墨、TIFF及多印刷尺寸。', ALL[:1]),
 ('route-customer', '处理客户设计路线', 'route', 'normal', '增强/去背景/裁切后，尺寸、白墨、TIFF审核保存。', [ALL[1]]),
 ('route-tiff', '生成打印TIFF路线', 'route', 'normal', '设计原图必须审核；无增强/去背景/默认裁切。', [ALL[2]]),
 ('pdf-pending', 'PDF单页准备待开始', 'pdf-psd', 'normal', '单页资格/第1页/300 PPI准备；原件不变。', None),
 ('pdf-running', 'PDF受管副本准备中', 'pdf-psd', 'loading', '仅展示真实准备阶段，未完成不得伪造预览。', None),
 ('psd-pending', 'PSD栅格副本待开始', 'pdf-psd', 'normal', '准备受管副本；原PSD及InputSnapshot不变，专色不沿用。', None),
 ('source-prepared-review', 'PDF/PSD准备后审核', 'pdf-psd', 'review', '准备成功后仍核对当前结果身份和原始尺寸。', None),
 ('source-failed', 'PDF/PSD准备失败', 'pdf-psd', 'error', '无有效准备结果不得进入审核或后续处理。', None),
 ('enhance-skip', '增强可跳过', 'enhance-ready', 'normal', '跳过处理不等于批准一个不存在的结果。', None),
 ('background-confirm', '自动去背景确认', 'background-ready', 'confirmation', '必须显式授权当前选择；取消不改变业务状态。', None),
 ('background-skip', '去背景可跳过', 'background-ready', 'normal', '保留设计背景时使用现有跳过资格。', None),
 ('review-disabled', '审核资格/身份缺失', 'review', 'disabled', '禁止批准；精确TargetIdentity、手势保护和AutomationId保留。', None),
 ('review-history', '只读历史审核', 'review', 'history', '仅查看旧图；不能批准当前结果或静默恢复旧审批。', None),
 ('review-rejected', '审核拒绝后重试', 'review', 'error', '当前结果被拒绝后须新尝试；旧审批不能作用于新结果。', None),
 ('crop-invalid', '裁切输入无效', 'crop', 'disabled', '无有效矩形/调整草稿或忙碌时不能使用裁切。', None),
 ('crop-cancel', '取消裁切草稿', 'crop', 'normal', '取消本页草稿与放弃任务分开；不产生审核批准。', None),
 ('trim-ready', '自动裁切参数与留边', 'crop-review', 'normal', 'Trim待开始时只呈现参数；生成后进入结果审核。', None),
 ('crop-review-return', '使用裁切后返回审核', 'crop-review', 'review', '使用裁切只更新候选，仍须必要审核。', None),
 ('size-invalid', '印刷尺寸输入无效', 'dimensions', 'disabled', '单位/数值/比例或边界校验失败；不得接受风险。', None),
 ('size-custom', '自定义印刷尺寸', 'dimensions', 'normal', '常用尺寸与自定义输入及草稿离开确认沿用现有资格。', None),
 ('white-none', '白墨尚未选择', 'white-ink', 'disabled', '无默认勾选；未选分支和理由不能继续。', None),
 ('white-0', '白墨内缩0像素及理由', 'white-ink', 'confirmation', '显式选择0像素分支与分类理由；原专色不沿用。', None),
 ('white-1', '白墨内缩1像素及理由', 'white-ink', 'confirmation', '显式选择1像素分支与分类理由。', None),
 ('white-2', '白墨内缩2像素及理由', 'white-ink', 'confirmation', '显式选择2像素分支与分类理由。', None),
 ('preflight-blocked', '生成前条件未齐', 'preflight', 'disabled', '环境、尺寸、白墨或审核缺失时保持阻塞。', None),
 ('tiff-failed', 'TIFF生成或校验失败', 'tiff-running', 'error', '300 PPI/CMYK/W1校验失败不产生有效最终结果。', None),
 ('tiff-review-history', '旧尺寸TIFF审核历史', 'tiff-review', 'history', '每尺寸独立身份，不能沿用另一尺寸审批。', None),
 ('stop-requested', '停止请求已发出', 'stopping', 'recovery', 'StopRequested不是已停止；持续显示真实阶段。', None),
 ('stop-finished', '尝试停止后待重试', 'failure', 'recovery', '停止完成后按真实业务资格重试，不自动重启。', None),
 ('takeover-requested', 'Photoshop接管请求处理中', 'takeover', 'loading', 'TakeOverRequested不是HandedOff；不强制关闭Photoshop。', None),
 ('correction-copy', '同事修正副本准备中', 'correction-prepare', 'loading', '保留原件和人工交接副本；不得自动发送或监控保存。', None),
 ('correction-failed', '同事修正准备失败', 'correction-prepare', 'error', '显示失败原因和合法重试；不伪称副本已交付。', None),
 ('correction-import-review', '修正回导后待审核', 'correction-wait', 'review', '显式选择回导且通过校验后仍须审核；不回导手工TIFF。', None),
 ('reentry', '自动化重新进入', 'recovery', 'confirmation', '恢复后须显式重入启动，不能自动继续外部应用操作。', None),
 ('completed-abandoned', '任务已放弃', 'completed', 'history', 'Completed投影也承载Abandoned；须区分已完成与已放弃。', None),
 ('readiness-unknown', '环境尚未检查', 'readiness', 'empty', '不能显示为已就绪。', None),
 ('readiness-running', '环境检查进行中', 'readiness', 'loading', '当前真实阶段与取消/返回资格可达。', None),
 ('readiness-blocked', '环境阻塞', 'readiness', 'error', '阻塞原因/下一步常驻；历史故障不当成当前检查。', None),
 ('readiness-ready', '当前环境已就绪', 'readiness', 'normal', '只依据真实检查证据显示，不能由界面自动授权。', None),
 ('settings-production', '生产设置只读', 'settings', 'disabled', '生产路径和配置按原权限只读。', None),
 ('error-empty', '无错误详情', 'error-details', 'empty', '没有可用错误时不伪造技术详情。', None),
 ('error-expanded', '错误技术详情展开', 'error-details', 'error', '原因/下一步保留首层，日志代码按需展开。', None),
 ('dialog-input-file', '选择输入文件对话框', 'input-error', 'confirmation', '单文件选择/过滤/取消；取消不创建新任务。', None),
 ('dialog-output-folder', '选择保存目录对话框', 'save', 'confirmation', '取消不写入结果、不改变已有保存记录。', ALL),
 ('dialog-correction-file', '选择修正回导文件对话框', 'correction-wait', 'confirmation', '只接受合法图像，不接收手工TIFF。', None),
 ('dialog-diagnostic-save', '诊断包保存对话框', 'error-details', 'confirmation', '显式目标/取消/失败；不上传或发送诊断包。', None),
 ('dialog-automatic-selection', '自动选择授权对话框', 'background-ready', 'confirmation', '当前选择授权绑定任务；中性焦点，取消无副作用。', None),
 ('dialog-trim-leave', '离开裁切草稿确认', 'draft-leave', 'confirmation', '保留/放弃草稿只操作当前草稿，不批准图像。', None),
]
for identifier, title, proto_id, state, condition, routes in extras:
    source = anchor = None
    if identifier.startswith('dialog-'):
        choices = {'dialog-input-file': ('OpenFileDialogPicker.cs', 'OpenFileDialog'),
                   'dialog-output-folder': ('OpenFolderDialogPicker.cs', 'OpenFolderDialog'),
                   'dialog-diagnostic-save': ('SaveDiagnosticPackageDialog.cs', 'SaveFileDialog')}
        if identifier in choices:
            name, anchor = choices[identifier]
            source = 'src/PrintFlow.App/Navigation/' + name
    page = 'Ready' if identifier == 'trim-ready' else 'Review' if identifier == 'source-prepared-review' else None
    if identifier == 'trim-ready':
        source, anchor = GUIDED, 'ShowsGuidedTrimParameters'
    add('S-' + identifier, title, proto_id, state, condition, page=page, routes=routes, source=source, anchor=anchor)

add('S-source-tiff-review', '生成打印TIFF原图设计审核', 'original', 'review',
    'GeneratePrintTiff的OriginalConfirmation.RequiresReview=true；原图设计审核必须显式通过。',
    page='Review', routes=[ALL[2]], source='src/PrintFlow.Workflow/Definitions/WorkflowCatalog.cs', anchor='GeneratePrintTiff')
add('S-meitu-takeover', '美图接管确认', 'takeover', 'confirmation',
    '素材/客户设计路线运行美图外部自动化时可按CanTakeOverAutomation确认接管；原型为Photoshop视觉参考。',
    routes=MEITU, source='src/PrintFlow.Workflow/Services/AutomationRuntime.cs', anchor='CanTakeOverAutomation')
add('S-meitu-takeover-requested', '美图接管请求处理中', 'takeover', 'loading',
    'TakeOverRequested不是HandedOff；保留外部应用状态，不能强制结束美图或自动重启。',
    routes=MEITU, source='src/PrintFlow.Workflow/Services/AutomationRuntime.cs', anchor='CanTakeOverAutomation')
add('S-source-preparation-takeover', 'PSD/PDF源准备接管确认', 'takeover', 'confirmation',
    '三路线的PSD/PDF原图准备也可运行外部适配器；仅实际Running且DrivesExternalApplication时沿用现有接管资格。',
    routes=ALL, source='src/PrintFlow.Workflow/Services/SessionService.cs', anchor='bool drivesExternalApplication')
add('S-source-preparation-takeover-requested', 'PSD/PDF源准备接管请求处理中', 'takeover', 'loading',
    '源准备外部适配器接管请求未结束时不显示已接管，不强制关闭应用、不自动重启准备。',
    routes=ALL, source='src/PrintFlow.Workflow/Services/AutomationRuntime.cs', anchor='CanTakeOverAutomation')

# All FinalSaveKind values are inventoried for PNG and both TIFF routes; these are projections, not new pages.
save_conditions = {
 'None': '无当前可保存目标；不实例化错误作用域表单。',
 'HistoryUnknown': '保存历史未读、不可读或过期；连是否存在先前保存也不能确认。', 'Pending': '最终结果仍待审核，联合确认保存只留在原审核动作组。',
 'PngPreparation': '批准PNG准备尚未完成。', 'Ineligible': '当前目标无保存资格，说明缺失前提。',
 'ApprovedNotSaved': '已审核，尚未保存；不显示任务已完成。', 'Failed': '保存失败，按合法资格重试。',
 'Collision': '同名冲突不覆盖；显式选择不同名称/目录。', 'Uncertain': '先核实同一次保存，不写成重新保存。',
 'Saved': '当前保存已核实；任务是否完成独立显示。', 'SavedPreviously': '已有历史保存；不推断当前文件仍在。',
 'Missing': '保存记录对应文件缺失，按资格核实或另存。', 'Changed': '目标文件内容已变化，不能沿用旧核实。',
 'Unavailable': '目标路径不可用或不可读，显示原因。', 'Working': '保存/核实正在运行，阻止重复提交。'}
for fmt, routes, proto_id in [('PNG', ALL[:1], 'png-save'), ('TIFF', TIFF, 'save')]:
    for kind, condition in save_conditions.items():
        state = 'loading' if kind == 'Working' else 'history' if kind in ['HistoryUnknown', 'SavedPreviously'] else 'error' if kind in ['Failed', 'Collision', 'Missing', 'Changed', 'Unavailable'] else 'disabled' if kind in ['None', 'Pending', 'PngPreparation', 'Ineligible'] else 'normal'
        # PNG preparation is not a TIFF state.
        if fmt == 'TIFF' and kind == 'PngPreparation':
            continue
        add('F-' + fmt + '-' + kind, fmt + '保存 · ' + kind, proto_id, state, condition,
            routes=routes, source=SAVE, anchor='FinalSaveKind', guard='HasFinalSave/GuidedPage/协调器资格不变；核实原DeliveryId；显式另存才新写入。')

for key, title in [('requirements', '需求与模板'), ('attachments', '附件及实际上传清单'), ('prompt', 'Prompt编辑与确认'), ('generation', '上传/生成进度'), ('candidate', '候选校验与确认'), ('import', '候选导入与手动交接')]:
    add('AI-' + key, '条件AI · ' + title, 'ai-planned', 'conditional',
        'SCRUM-11184/11166均To Do；当前候选无可集成AI界面。按原Epic实现后才接入；不阻塞三路线。')

payload = {'schemaVersion': 1, 'task': 'SCRUM-11200', 'timezone': 'Pacific/Auckland',
 'sourceMatrix': 'SCRUM-11174 / frozen prototype/design-spec.html sections 2,3,6,8 / flow-map.json',
 'scope': '既有页面与真实状态投影清单；映射待迁移，不声明已完成全站WPF UI。',
 'routes': {ALL[0]: {'output': 'PNG', 'excludes': ['Dimensions','WhiteInk','Preflight','TIFF','multi-print-size']},
            ALL[1]: {'output': 'TIFF', 'steps': 'source/enhance/background/trim/dimensions/whiteInk/preflight/TIFF/review/save'},
            ALL[2]: {'output': 'TIFF', 'excludes': ['enhancement','background-removal','default-trim']}},
 'rows': rows}
(HERE / 'PAGE_STATE_INVENTORY.json').write_text(json.dumps(payload, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')

def validate():
    assert len({r['id'] for r in rows}) == len(rows)
    assert {s['id'] for s in screens} == {r['prototypeId'] for r in rows}
    guided_text = (SAMPLE / GUIDED).read_text(encoding='utf-8-sig')
    enum = re.search(r'enum GuidedSessionPage\s*\{([^}]+)', guided_text).group(1)
    pages = set(re.findall(r'\b[A-Z][A-Za-z]+\b', enum))
    assert pages <= {r['guidedPage'] for r in rows}, pages - {r['guidedPage'] for r in rows}
    actual_save = set(re.search(r'enum FinalSaveKind\s*\{([^}]+)', (SAMPLE / SAVE).read_text(encoding='utf-8-sig')).group(1).replace(' ', '').split(','))
    assert actual_save == set(save_conditions)
    by_id = {r['id']: r for r in rows}
    assert by_id['S-trim-ready']['guidedPage'] == 'Ready'
    assert by_id['S-source-prepared-review']['guidedPage'] == 'Review'
    assert ALL[0] in by_id['S-meitu-takeover']['routes']
    assert by_id['S-source-preparation-takeover']['routes'] == ALL
    assert by_id['S-source-preparation-takeover-requested']['routes'] == ALL
    assert by_id['F-PNG-HistoryUnknown']['condition'] == '保存历史未读、不可读或过期；连是否存在先前保存也不能确认。'
    known_tasks = {x['key'] for x in json.loads((HERE.parent / 'JIRA_CREATED.json').read_text(encoding='utf-8-sig'))['created']}
    for row in rows:
        assert row['migrationTask'] in known_tasks and row['copyTask'] in known_tasks
        assert (ROOT / row['prototype']).is_file()
        for p in row['sample']: assert (ROOT / p).is_file()
        assert row['priority'] and row['guard'] and row['condition'] and row['evidence']
        assert row['outputs'] == ((['PNG'] if ALL[0] in row['routes'] else []) + (['TIFF'] if any(r in TIFF for r in row['routes']) else []))
        if row['guidedPage'] in ['Dimensions','WhiteInk','Preflight']:
            assert ALL[0] not in row['routes'], 'Asset route must not contain print parameters'
        if 'ConditionalAi' in row['routes']:
            assert row['source']['path'] is None and row['implementation'] == 'CONDITIONAL_NOT_IMPLEMENTED'
    result = {'status': 'PASS', 'rows': len(rows), 'originalPrototypeScreens': len(screens),
              'guidedPages': sorted(pages), 'finalSaveKinds': sorted(actual_save),
              'conditionalAiRows': sum('ConditionalAi' in r['routes'] for r in rows),
              'sourceAnchorsAndHashes': 'checked', 'allOwnersAndReferences': 'checked',
              'states': sorted(set(r['state'] for r in rows)), 'productTests': 'NOT RUN'}
    (HERE / 'INVENTORY_VALIDATION.json').write_text(json.dumps(result, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')
    print(json.dumps(result, ensure_ascii=False))

validate()
fields = ['id','page','guidedPage','state','condition','routes','outputs','migrationTask','copyTask','priority','implementation','prototype','sampleStatus','automatedTestThisTask','visualTestThisTask','realWindowDpiKeyboardScreenReader']
with (HERE / 'PAGE_STATE_INVENTORY.csv').open('w', encoding='utf-8-sig', newline='') as f:
    writer = csv.DictWriter(f, fieldnames=fields)
    writer.writeheader()
    for row in rows: writer.writerow({k: ', '.join(row[k]) if isinstance(row[k], list) else row[k] for k in fields})
md = ['# 全页面与状态清单', '', '2026-10-22，Pacific/Auckland。SCRUM-11200；Native顺序执行。', '',
      '复用SCRUM-11174的45个设计页面，追加真实状态、保存分支与条件AI。以下任务是后续迁移归属；源码存在不表示WPF UI迁移完成。逐项条件、显隐意图、业务约束、源码行号/哈希、原型/历史样板引用和NOT RUN证据栏见同目录JSON。CSV用于筛选。', '',
      '首页和PNG保存页引用历史96 DPI离屏样板；其余原型均为设计参考。产品构建/测试、新截图、真实窗口/DPI/键鼠/读屏/真人走查，本次全部未执行。', '',
      '| 编号 | 页面/状态 | 路线/输出 | 迁移 / 文案 | 优先级 | 源码或条件 |', '| --- | --- | --- | --- | --- | --- |']
for r in rows:
    source = r['source']
    ref = source['path'] + ':' + str(source['line']) if source['path'] else '条件接入，尚未实现（11184/11166）'
    md.append('| ' + ' | '.join([r['id'], r['page'] + ' / ' + r['state'], ','.join(r['routes']) + ' / ' + ','.join(r['outputs']), r['migrationTask'] + ' / ' + r['copyTask'], r['priority'], ref]) + ' |')
(HERE / 'PAGE_STATE_INVENTORY.md').write_text('\n'.join(md) + '\n', encoding='utf-8')
