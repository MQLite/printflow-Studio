"""Build C01 documentation from U01's frozen bytes; never edits product sources.

Python 3.12+. Run at any cwd. Generation requires the local U01 frozen baseline.
No UI, external adapter, database, restore, network, or application is started.
"""
from __future__ import annotations

import csv
from collections import defaultdict
import hashlib
import io
import json
from pathlib import Path
import re
import subprocess
import tarfile
import xml.etree.ElementTree as ET

HERE = Path(__file__).resolve().parent
ROOT = HERE.parents[3]
FROZEN = ROOT / "output/ui/SCRUM-11200/baseline-20261022"
PAGES = HERE.parent / "SCRUM-11200/PAGE_STATE_INVENTORY.json"
ZH = "src/PrintFlow.App/Resources/Strings.zh-CN.resx"
EN = "src/PrintFlow.App/Resources/Strings.resx"
PH = re.compile(r"(?<!\{)\{\d+(?:,-?\d+)?(?::[^{}]+)?\}(?!\})")
REF = re.compile(r"\bStrings\.([A-Za-z_]\w*)")
TOKEN = re.compile(r'//[^\n]*|/\*.*?\*/|\$*(?P<raw>"{3,}).*?(?P=raw)|(?:\$@|@\$|@)"(?:[^"]|"")*"|\$?"(?:\\.|[^"\\])*"|\'(?:\\.|[^\'\\])*\'', re.S)


def sha(data):
    return hashlib.sha256(data).hexdigest().upper()


def load(path):
    return json.loads(path.read_text(encoding="utf-8-sig"))


def write(name, value):
    (HERE / name).write_text(json.dumps(value, ensure_ascii=False, indent=2) + "\n", encoding="utf-8", newline="\n")


def placeholders(text):
    return PH.findall(text or "")


def sources():
    metadata = load(FROZEN / "baseline.json")
    # Verify the frozen files before using them, independent of mutable live worktrees.
    for item in load(FROZEN / "files.json"):
        assert sha((FROZEN / item["path"]).read_bytes()) == item["sha256"], item["path"]
    archive = subprocess.check_output(["git", "-C", str(ROOT), "archive", "--format=tar", metadata["baseCommit"], "src"])
    with tarfile.open(fileobj=io.BytesIO(archive)) as tf:
        data = {m.name: tf.extractfile(m).read() for m in tf.getmembers()
                if m.isfile() and Path(m.name).suffix in {".cs", ".xaml", ".resx"}}
    for p in metadata["overlayPaths"]:
        if p.startswith("src/") and Path(p).suffix in {".cs", ".xaml", ".resx"}:
            data[p] = (FROZEN / "overlay" / p).read_bytes()
    for p, expected in load(FROZEN / "checkout-bytes-manifest.json").items():
        if p in data:
            raw = (FROZEN / "checkout-bytes" / p).read_bytes()
            assert sha(raw) == expected
            data[p] = raw
    for p, raw in data.items():
        if p in metadata["trackedCandidateSha256"]:
            assert sha(raw) == metadata["trackedCandidateSha256"][p], p
    return metadata, data


def resx(raw):
    return {n.attrib["name"]: n.findtext("value") or "" for n in ET.fromstring(raw).findall("data")}


def family(key):
    if key.startswith(("FinalSave_", "Home_Recent", "Home_Abandon", "Home_Remove", "Environment", "Settings_", "ErrorDetails_", "DiagnosticPackage_", "Failure", "Startup_", "ReadinessRecovery_", "Session_Completion", "SessionState_", "Session_NextCompleted", "Session_StatusCompleted")) or key == "Session_Complete" or re.search(r"Stop|TakeOver|TakingOver|HandedOff|Correction|Reentry|Recovery|History", key):
        return "SCRUM-11206"
    if re.search(r"WhiteInk|Preflight|Print|Dimension|MaxBounds|Enlarg|TargetSize|TargetEdge|W1|Resize|Limiting|Preset|CurrentCustomTarget|SizeHeading|LabelMax|MaxLongEdge|MaxShortEdge|Recommended(?:LongEdge|ShortEdge|Maximum)", key):
        return "SCRUM-11205"
    return "SCRUM-11204"


def safety(key, text):
    rules = []
    for pattern, rule in [
        (r"Review|Approv|Reject|Guidance|审核|检查", "S01-REVIEW"),
        (r"Save|Delivery|保存|交付", "S02-SAVE"),
        (r"Complet|完成", "S03-COMPLETE"),
        (r"History|Previously|历史|曾保存|记录", "S04-HISTORY"),
        (r"Stop|Cancel|TakeOver|TakingOver|停止|接管", "S05-STOP"),
        (r"Retry|CheckAgain|CheckSaved|Uncertain|SaveAnother|重试|核实|另存", "S06-RECOVERY"),
        (r"Abandon|Cancel|取消|放弃", "S07-CANCEL"),
        (r"WhiteInk|白墨|W1|CMYK|PPI|毫米|像素", "S08-PRINT"),
        (r"Correction|同事|修正|交接", "S09-COLLEAGUE"),
        (r"Source|Original|Snapshot|原件|原始文件", "S10-SOURCE"),
        (r"Environment|Readiness|环境|就绪", "S11-ENVIRONMENT"),
    ]:
        if re.search(pattern, key + " " + text, re.I):
            rules.append(rule)
    return rules or ["S00-CONTEXT"]


def select_keys(page, keys):
    """Editorial associations, not assertions that every key is visible in a state."""
    ident = page["id"]
    if page["implementation"] == "CONDITIONAL_NOT_IMPLEMENTED":
        return [], "AI-CONDITIONAL"
    if ident.startswith("F-"):
        kind = ident.rsplit("-", 1)[-1]
        names = {
            "None": ["FinalSave_Ineligible"], "HistoryUnknown": ["FinalSave_HistoryLoading", "FinalSave_HistoryUnavailable", "FinalSave_HistoryNotRefreshed"],
            "Pending": ["FinalSave_AwaitingConfirmation", "FinalSave_ConfirmAndSave", "FinalSave_ApproveOnlyHint"],
            "PngPreparation": ["FinalSave_PngPreparationPending"], "Ineligible": ["FinalSave_Ineligible"],
            "ApprovedNotSaved": ["FinalSave_ApprovedNotSaved", "FinalSave_SaveApproved"],
            "Failed": ["FinalSave_ApprovedNotSavedReason", "FinalSave_RetrySave"],
            "Collision": ["FinalSave_Collision", "FinalSave_CollisionNoSuggestion", "FinalSave_UseSuggestion"],
            "Uncertain": ["FinalSave_Uncertain", "FinalSave_CheckAgain", "FinalSave_UncertainElsewhere"],
            "Saved": ["FinalSave_Saved", "FinalSave_CheckSaved", "FinalSave_OpenFolder"],
            "SavedPreviously": ["FinalSave_SavedPreviously", "FinalSave_CheckSaved"],
            "Missing": ["FinalSave_Missing", "FinalSave_SaveAnotherCopy"],
            "Changed": ["FinalSave_Changed", "FinalSave_SaveAnotherCopy"],
            "Unavailable": ["FinalSave_Unavailable", "FinalSave_CheckSaved"],
            "Working": ["FinalSave_StageChecking", "FinalSave_StageApproving", "FinalSave_StageSaving", "FinalSave_StageFinishing", "FinalSave_CancelSave"],
        }[kind]
        assert set(names) <= keys, (ident, set(names) - keys)
        return names, "FINAL-SAVE-" + kind
    p = str(page["source"]["path"] or "")
    low = ident.lower()
    if "settings" in low:
        pattern, group = r"^Settings_|^Nav_", "SETTINGS"
    elif "readiness" in low:
        pattern, group = r"^Environment|^ReadinessRecovery_", "ENVIRONMENT"
    elif "error" in low and "input" not in low:
        pattern, group = r"^ErrorDetails_|^FailureNotice_|^DiagnosticPackage_", "ERROR-DETAILS"
    elif "diagnostic" in low:
        pattern, group = r"^DiagnosticPackage_", "DIAGNOSTIC-DIALOG"
    elif "recent" in low:
        pattern, group = r"^Home_Recent|^Session_Status|^SessionState_|^StepState_", "RECENT"
    elif "abandon" in low and "completed" not in low:
        pattern, group = r"^Home_Abandon|^Session_CompletionAbandoned|^SessionState_Abandoned", "ABANDON"
    elif "home" in low or "input" in low:
        pattern, group = r"^Home_|^Startup_", "HOME-IMPORT"
    elif "route" in low:
        pattern, group = r"^Workflow|^Session_GuidedChangeRoute", "ROUTE"
    elif "stop" in low or "takeover" in low or "takeover" in p.lower():
        pattern, group = r"Stop|TakeOver|TakingOver|HandedOff|ReturnGuidance|^Failure_Cancelled", "STOP-TAKEOVER"
    elif "correction" in low:
        pattern, group = r"Correction|Colleague|ManualReturn|HandedOff", "COLLEAGUE"
    elif "recovery" in low or "reentry" in low or "handed-off" in low:
        pattern, group = r"HandedOff|Reentry|Recovery|Return|TakeOver", "RECOVERY"
    elif "completed" in low:
        pattern, group = r"^Session_Complete$|^Session_Completion|^Session_StatusCompleted|^Session_NextCompleted|^FinalSave_Record", "COMPLETION"
    elif page["guidedPage"] == "Save" or "save" in low or "collision" in low or "saved" in low:
        pattern, group = r"^FinalSave_|^Session_Completion", "SAVE"
    elif page["guidedPage"] == "Dimensions":
        pattern, group = r"Dimension|MaxBounds|TargetSize|TargetEdge|Print|Enlarg|CurrentCustomTarget|SizeHeading|LabelMax|MaxLongEdge|MaxShortEdge|Recommended(?:LongEdge|ShortEdge|Maximum)|^Preset_|^ResizeDirection_|^LimitingEdge_", "DIMENSIONS"
    elif page["guidedPage"] == "WhiteInk":
        pattern, group = r"WhiteInk|^W1_", "WHITE-INK"
    elif page["guidedPage"] == "Preflight":
        pattern, group = r"Preflight|GuidedPrint|Enlarg|WhiteInkSource", "PREFLIGHT"
    elif "pdf" in low or "psd" in low or "source" in low or "original" in low:
        pattern, group = r"Original|Source|Pdf|Psd|Preparation|GuidanceApprove", "SOURCE"
    elif page["guidedPage"] == "Review" or "review" in low or "preview" in low:
        pattern, group = r"Guidance|Review|Reject|Approv|Compare|History|Zoom|Preview|WhiteInk", "REVIEW"
    elif "crop" in low or "trim" in low:
        pattern, group = r"Crop|Trim|Margin|Edge|GuidanceTrim", "CROP"
    elif "confirm" in low or "draft-leave" in low or "dialog" in low:
        pattern, group = r"Confirm|Cancel|Return|TrimAdjust|BackgroundRemoval", "CONFIRMATION"
    elif "failure" in low:
        pattern, group = r"^Failure|Retry|Stopped|ErrorDetails", "FAILURE"
    else:
        pattern, group = r"GuidedPhase|GuidedProcessing|GuidedStopping|Run|Skip|BackgroundRemoval|Enhancement|^Step_|^Session_Status", "PROCESSING"
    chosen = sorted(k for k in keys if re.search(pattern, k))
    assert chosen, ident
    return chosen, group


def build():
    metadata, raw = sources()
    texts = {p: b.decode("utf-8-sig") for p, b in raw.items()}
    zh, en = resx(raw[ZH]), resx(raw[EN])
    assert set(zh) == set(en)
    pages = load(PAGES)["rows"]
    edits = load(HERE / "CANDIDATE_EDITS.json")
    assert set(edits) <= set(zh)
    refs = {key: [] for key in zh}
    expressions, literals, accessibility, xaml_copy, projections = [], [], [], [], []
    files = []
    for p in sorted(texts):
        text = texts[p]
        files.append({"path": p, "sha256": sha(raw[p]), "lines": len(text.splitlines())})
        if p.endswith(".resx"):
            continue
        lines = text.splitlines()
        if p.endswith(".cs"):
            # Mask comments and literal contents so a resource name in a comment is not a use.
            masked = list(text)
            for token in TOKEN.finditer(text):
                if token[0].startswith('$'):
                    # Resource reads inside interpolations are real dynamic uses.
                    continue
                for i in range(token.start(), token.end()):
                    if masked[i] != "\n":
                        masked[i] = " "
            code = "".join(masked)
        else:
            code = re.sub(r"<!--.*?-->", lambda m: "\n" * m[0].count("\n"), text, flags=re.S)
        literal_keys = defaultdict(set)
        if p.endswith(".cs"):
            for token in TOKEN.finditer(text):
                if token[0].startswith('"') and token[0][1:-1] in zh:
                    literal_keys[text.count("\n", 0, token.start())+1].add(token[0][1:-1])
        for line_no, line in enumerate(code.splitlines(), 1):
            names = sorted((set(REF.findall(line)) | literal_keys[line_no]) & zh.keys())
            for key in names:
                refs[key].append({"path": p, "line": line_no})
            if names:
                expressions.append({"id": f"D{len(expressions)+1:05}", "path": p, "line": line_no,
                                    "expression": lines[line_no-1].strip(), "resourceKeys": names,
                                    "contract": "参数来自当前投影；保留格式、单位、结果身份和失败分支；不能据资源名认定当前可见。"})
            if p.startswith("src/PrintFlow.App/") and re.search(r"DisplayNames\.|Strings\.(?:Get|Resolve)\(|GetString\(|MessageKey|TechnicalDetail|\.Message\b|\.ToString\(|string\.Format\(|Format\(|GetNameCore|Notice\s*=>", line):
                projections.append({"id": f"B{len(projections)+1:04}", "path": p, "line": line_no,
                                    "expression": lines[line_no-1].strip(),
                                    "contract": "动态Get/枚举映射、格式化、系统/异常/用户数据透传的保守复核点；保留代码值与单位，具体运行时值不是固定文案。"})
        if p.endswith(".cs"):
            for token in TOKEN.finditer(text):
                value = token[0]
                if value.startswith(("//", "/*", "'")):
                    continue
                line_no = text.count("\n", 0, token.start()) + 1
                context = lines[line_no-1].strip()
                chinese = bool(re.search(r"[\u4e00-\u9fff]", value))
                layer = "APP" if p.startswith("src/PrintFlow.App/") else "SERVICE-DETAIL"
                literals.append({"id": f"L{len(literals)+1:05}", "path": p, "line": line_no,
                                 "currentExpression": value, "proposedExpression": value,
                                 "context": context, "layer": layer,
                                 "disposition": "REVIEW_HARDCODED_ZH" if chinese else "RETAIN_LITERAL_PENDING_EXPOSURE_CLASSIFICATION",
                                 "meaning": "硬编码中文；下一文案任务核对可见性并提取资源" if chinese else "保守登记字符串字面量；可能是标识、路径、格式、诊断或插值，不等于用户文案",
                                 "verification": "追踪 Content/Text/Notice/异常详情消费者；标识、路径、代码不翻译；用户输入与系统异常不当作固定模板。"})
        if p.endswith(".xaml"):
            for line_no, line in enumerate(code.splitlines(), 1):
                for name, value in re.findall(r'([\w.:]+)="([^"]*)"', line):
                    if name in {"AutomationProperties.Name", "AutomationProperties.HelpText", "AutomationProperties.AutomationId", "AutomationProperties.ItemStatus", "AutomationProperties.LabeledBy"}:
                        accessibility.append({"id": f"A{len(accessibility)+1:04}", "path": p, "line": line_no,
                                              "property": name, "current": value, "proposed": value,
                                              "contract": "AutomationId不改；Name/HelpText须绑定同一对象动作，不用名称创造命令资格；实际读屏NOT RUN。"})
                    if name in {"Text", "Content", "Header", "Title", "ToolTip"}:
                        xaml_copy.append({"id": f"X{len(xaml_copy)+1:04}", "path": p, "line": line_no,
                                          "property": name, "current": value, "proposed": value,
                                          "disposition": "BINDING_OR_RESOURCE" if value.startswith("{") else "LITERAL_REVIEW",
                                          "contract": "绑定需沿ViewModel追踪；静态文本/单位/符号保留，下一对应文案任务判定改写。"})
        # Code-created controls and automation peers are outside XAML.
        if p.startswith("src/PrintFlow.App/") and p.endswith(".cs"):
            for line_no, line in enumerate(code.splitlines(), 1):
                if re.search(r"SetName\(|SetHelpText\(|SetAutomationId\(|GetNameCore\(|GetHelpTextCore\(", line):
                    accessibility.append({"id": f"A{len(accessibility)+1:04}", "path": p, "line": line_no,
                                          "property": "CODE_AUTOMATION", "current": lines[line_no-1].strip(),
                                          "proposed": lines[line_no-1].strip(),
                                          "contract": "追踪参数/peer；保持动作、当前结果身份和可见标签一致；不可按普通按钮替换审核控件。"})
    page_rows = []
    reverse = {k: [] for k in zh}
    for page in pages:
        names, group = select_keys(page, set(zh))
        for key in names:
            reverse[key].append(page["id"])
        page_rows.append({"id": page["id"], "page": page["page"], "state": page["state"], "guidedPage": page["guidedPage"],
                          "routes": page["routes"], "condition": page["condition"], "source": page["source"],
                          "copyTask": page["copyTask"], "group": group, "resourceKeys": names,
                          "mapping": "EDITORIAL_REVIEW_SET; 不是运行时同时显示集合" if names else "CONDITIONAL_NOT_IMPLEMENTED; 无现文案/无新入口",
                          "meaning": page["guard"], "primaryActionReference": page["primaryActionReference"],
                          "validation": "按条件检查当前状态与动作；动态/辅助来源见SOURCE_REGISTER；产品/视觉/真人NOT RUN。"})
    rows = []
    typed = texts["src/PrintFlow.App/Resources/Strings.cs"]
    for key in sorted(zh):
        edit = edits.get(key)
        proposed = edit["proposed"] if edit else zh[key]
        assert sorted(placeholders(en[key])) == sorted(placeholders(zh[key])), key
        assert sorted(placeholders(proposed)) == sorted(placeholders(zh[key])), key
        rows.append({"id": "R-" + key, "key": key, "zhSource": ZH, "enSource": EN,
                     "currentZh": zh[key], "currentEn": en[key], "proposedZh": proposed,
                     "decision": ("CANDIDATE_REWRITE" if proposed != zh[key] else "CANDIDATE_RETAIN") if edit else "RETAIN_CURRENT_FOR_C01",
                     "reason": edit["reason"] if edit else "本阶段保留现值作为基准；不因字数目标机械缩短，后续按页面条件复核。",
                     "copyTask": family(key), "pageStateIds": reverse[key],
                     "meaningAndRisk": safety(key, zh[key]), "placeholders": placeholders(zh[key]),
                     "unitsAndTokens": sorted(set(re.findall(r"(?<![A-Za-z])(?:mm|cm|px|PPI|DPI)(?![A-Za-z])|300|CMYK|W1|TIFF|PNG|PSD|PDF|NTFS|毫米|像素|%|×", zh[key]))),
                     "typedAccessor": bool(re.search(r"\b" + re.escape(key) + r"\s*=>", typed)),
                     "consumers": refs[key],
                     "accessibilityRequirement": "与同一目标的可见文字/Name/HelpText一起核对；使用SOURCE_REGISTER的A记录追踪绑定；AutomationId保持。",
                     "verification": "zh/en键与完整格式占位符相等；技术合同逐项保留；按页面条件核对动作、禁用原因和辅助名称；实际产品验证留给对应实施任务。"})
    write("COPY_INVENTORY.json", {"schemaVersion": 1, "task": "SCRUM-11203", "status": "CANDIDATES_ONLY_NOT_APPLIED", "rows": rows})
    with (HERE / "COPY_INVENTORY.csv").open("w", encoding="utf-8-sig", newline="") as out:
        fields = ["key", "currentZh", "proposedZh", "currentEn", "decision", "copyTask", "reason", "placeholders", "unitsAndTokens", "meaningAndRisk", "pageStateIds", "consumers", "accessibilityRequirement", "verification"]
        writer = csv.DictWriter(out, fieldnames=fields, extrasaction="ignore", lineterminator="\n")
        writer.writeheader()
        for row in rows:
            writer.writerow({k: json.dumps(v, ensure_ascii=False) if isinstance(v, (list, dict)) else v for k, v in row.items() if k in fields})
    write("PAGE_COPY_MATRIX.json", {"sourceSha256": sha(PAGES.read_bytes()), "rows": page_rows})
    write("SOURCE_REGISTER.json", {"scope": "src下全部cs/xaml/resx文件；字面量是保守扫描，不等于全部已证实可见文案。外部系统/用户数据为开放内容。", "resourceExpressions": expressions, "dynamicProjections": projections, "stringLiterals": literals, "xamlText": xaml_copy, "accessibility": accessibility})
    reference_paths = ["docs/codex/printflow-wpfui-rollout/PLAN.md", "docs/codex/printflow-operator-ux-backlog/PLAN.md", "docs/codex/printflow-operator-ux-backlog/SCRUM-11153_COPY_REVIEW.md", "docs/codex/printflow-operator-ux-backlog/SCRUM-11154_COPY_REVIEW.md"]
    write("SOURCE_MANIFEST.json", {"baseCommit": metadata["baseCommit"], "frozenBaseline": str(FROZEN.relative_to(ROOT)).replace("\\", "/"), "baselineSha256": sha((FROZEN / "baseline.json").read_bytes()), "pageInventorySha256": sha(PAGES.read_bytes()), "referenceDocuments": [{"path": p, "sha256": sha((ROOT / p).read_bytes())} for p in reference_paths], "files": files})
    def esc(value):
        return str(value).replace("|", "\\|").replace("\n", "<br>")
    table = ["# SCRUM-11203 文案候选对照", "", "完整1012键及来源见COPY_INVENTORY.json/CSV。下表列43项重点候选，含41项改写和2项显式保留；均未应用。", "", "| 资源键 | 当前 | 候选 | 保留的含义 / 理由 | 后续任务 |", "| --- | --- | --- | --- | --- |"]
    for row in rows:
        if row["decision"].startswith("CANDIDATE_"):
            table.append("| " + " | ".join(esc(row[k]) for k in ["key", "currentZh", "proposedZh", "reason", "copyTask"]) + " |")
    (HERE / "CANDIDATE_REVIEW.md").write_text("\n".join(table) + "\n", encoding="utf-8", newline="\n")
    matrix = ["# SCRUM-11203 页面与文案复核集合", "", "复用U01的140行；资源集合为编辑复核范围，不能据此推断可见、资格或页面已实现。精确键列表见PAGE_COPY_MATRIX.json。", "", "| ID | 页面 / 状态 | 文案组 | 资源数 | 后续任务 |", "| --- | --- | --- | --- | --- |"]
    for row in page_rows:
        matrix.append("| " + " | ".join(esc(v) for v in [row["id"], row["page"] + " / " + row["state"], row["group"], len(row["resourceKeys"]), row["copyTask"]]) + " |")
    (HERE / "PAGE_COPY_MATRIX.md").write_text("\n".join(matrix) + "\n", encoding="utf-8", newline="\n")
    print(json.dumps({"resources": len(rows), "candidateRows": len(edits), "rewrites": sum(r["decision"] == "CANDIDATE_REWRITE" for r in rows), "pages": len(page_rows), "sourceFiles": len(files), "resourceExpressions": len(expressions), "dynamicProjections": len(projections), "literals": len(literals), "xamlText": len(xaml_copy), "accessibility": len(accessibility)}, ensure_ascii=False))


if __name__ == "__main__":
    build()
