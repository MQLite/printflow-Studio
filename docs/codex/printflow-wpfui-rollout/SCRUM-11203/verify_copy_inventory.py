"""Verify C01 artifacts. --sources additionally checks against the U01 frozen corpus."""
import argparse
import contextlib
import csv
import io
import json
from pathlib import Path
import re
import sys
import tempfile

sys.dont_write_bytecode = True
import build_copy_inventory as build

HERE = Path(__file__).resolve().parent


def verify(check_sources=False):
    rows = build.load(HERE / "COPY_INVENTORY.json")["rows"]
    by_key = {r["key"]: r for r in rows}
    assert len(by_key) == len(rows) == 1012
    edits = build.load(HERE / "CANDIDATE_EDITS.json")
    for r in rows:
        assert sorted(build.placeholders(r["currentEn"])) == sorted(build.placeholders(r["currentZh"]))
        assert sorted(build.placeholders(r["proposedZh"])) == sorted(r["placeholders"]), r["key"]
        assert r["proposedZh"] == edits.get(r["key"], {}).get("proposed", r["currentZh"])
        assert r["meaningAndRisk"] and r["verification"] and r["accessibilityRequirement"]
        for unit in r["unitsAndTokens"]:
            assert unit in r["proposedZh"], (r["key"], unit)
    with (HERE / "COPY_INVENTORY.csv").open(encoding="utf-8-sig", newline="") as f:
        csv_rows = list(csv.DictReader(f))
    assert len(csv_rows) == len(rows)
    for a, b in zip(rows, csv_rows):
        for key, value in b.items():
            assert (json.loads(value) if isinstance(a[key], list) else value) == a[key], (a["key"], key)
    page_data = build.load(HERE / "PAGE_COPY_MATRIX.json")
    pages = page_data["rows"]
    assert len(pages) == len({r["id"] for r in pages}) == 140
    conditional = [r for r in pages if r["group"] == "AI-CONDITIONAL"]
    assert len(conditional) == 7 and all(not r["resourceKeys"] for r in conditional)
    assert all(r["resourceKeys"] for r in pages if r not in conditional)
    for p in pages:
        assert set(p["resourceKeys"]) <= by_key.keys()
        for key in p["resourceKeys"]:
            assert p["id"] in by_key[key]["pageStateIds"]
    for prefix, count in [("F-PNG-", 15), ("F-TIFF-", 14)]:
        assert sum(p["id"].startswith(prefix) for p in pages) == count
    by_page = {p["id"]: p for p in pages}
    for prefix in ["F-PNG-", "F-TIFF-"]:
        assert "FinalSave_HistoryUnavailable" in by_page[prefix+"HistoryUnknown"]["resourceKeys"]
        assert "FinalSave_SavedPreviously" not in by_page[prefix+"HistoryUnknown"]["resourceKeys"]
        assert "FinalSave_CheckAgain" in by_page[prefix+"Uncertain"]["resourceKeys"]
        assert "FinalSave_RetrySave" not in by_page[prefix+"Uncertain"]["resourceKeys"]
    # Review regressions protect real call-site semantics, not word-count preferences.
    reason_template = by_key["FinalSave_ApprovedNotSavedReason"]["proposedZh"]
    reason = by_key["FinalSave_Collision"]["currentZh"].format("existing.png", "suggested.png")
    checks = {
        "uncertainOperationNotSavedClaim": any(word in by_key["FinalSave_UncertainElsewhere"]["proposedZh"] for word in ["发起", "可能"]),
        "composedReasonPunctuation": "。。" not in reason_template.format(reason),
        "unknownHistoryNoPresupposedRecords": "其他保存记录" not in by_key["FinalSave_HistoryUnavailable"]["proposedZh"],
        "completeTaskRoutingAndMapping": by_key["Session_Complete"]["copyTask"] == "SCRUM-11206" and "Session_Complete" in by_page["P-completed"]["resourceKeys"],
        "dimensionsRoutingAndMapping": all(by_key[k]["copyTask"] == "SCRUM-11205" and k in by_page["S-size-custom"]["resourceKeys"] for k in ["Session_CurrentCustomTarget", "Session_SizeHeading"]),
        "millimetreUnitRegistration": all("mm" in r["unitsAndTokens"] for r in rows if "mm" in r["currentZh"]),
    }
    assert all(checks.values()), checks
    assert all(p in by_page for p in ["S-meitu-takeover", "S-meitu-takeover-requested", "S-source-preparation-takeover", "S-source-preparation-takeover-requested"])
    register = build.load(HERE / "SOURCE_REGISTER.json")
    manifest = build.load(HERE / "SOURCE_MANIFEST.json")
    source_files = {p["path"]: p for p in manifest["files"]}
    for section in ["resourceExpressions", "dynamicProjections", "stringLiterals", "xamlText", "accessibility"]:
        entries = register[section]
        assert entries and len({e["id"] for e in entries}) == len(entries)
        for entry in entries:
            assert entry["path"] in source_files
            assert 0 < entry["line"] <= source_files[entry["path"]]["lines"]
    assert any("Strings.Resolve(failure.MessageKey)" in r["expression"] for r in register["dynamicProjections"])
    assert any("SetName" in r["current"] and r["path"].endswith("SharedReviewSurface.cs") for r in register["accessibility"])
    assert any("FailureNotice_OperationFaulted" in r["resourceKeys"] for r in register["resourceExpressions"])
    if check_sources:
        _, raw = build.sources()
        for doc in manifest["referenceDocuments"]:
            assert build.sha((build.ROOT / doc["path"]).read_bytes()) == doc["sha256"], doc["path"]
        assert set(raw) == set(source_files)
        for path, data in raw.items():
            assert build.sha(data) == source_files[path]["sha256"], path
        for path, field in [(build.ZH, "currentZh"), (build.EN, "currentEn")]:
            expected = build.resx(raw[path])
            assert set(expected) == set(by_key)
            assert all(by_key[k][field] == value for k, value in expected.items())
        original = build.load(build.PAGES)["rows"]
        assert build.sha(build.PAGES.read_bytes()) == page_data["sourceSha256"]
        assert [p["id"] for p in pages] == [p["id"] for p in original]
        for before, after in zip(original, pages):
            for field in ["source", "condition", "routes", "guidedPage", "state", "copyTask"]:
                assert before[field] == after[field], (before["id"], field)
        texts = {p: b.decode("utf-8-sig").splitlines() for p, b in raw.items()}
        for r in rows:
            for ref in r["consumers"]:
                assert r["key"] in texts[ref["path"]][ref["line"]-1], (r["key"], ref)
        for field in ["resourceExpressions", "dynamicProjections"]:
            for entry in register[field]:
                assert entry["expression"] == texts[entry["path"]][entry["line"]-1].strip()
        # Regenerate away from the deliverables and compare exact bytes, including source indexes.
        original_here = build.HERE
        with tempfile.TemporaryDirectory(prefix="printflow-11203-verify-") as temp:
            rebuilt = Path(temp)
            (rebuilt / "CANDIDATE_EDITS.json").write_bytes((HERE / "CANDIDATE_EDITS.json").read_bytes())
            try:
                build.HERE = rebuilt
                with contextlib.redirect_stdout(io.StringIO()):
                    build.build()
            finally:
                build.HERE = original_here
            for output in rebuilt.iterdir():
                assert output.read_bytes() == (HERE / output.name).read_bytes(), output.name
    for p in HERE.glob("*.md"):
        text = p.read_text(encoding="utf-8")
        assert all(line == line.rstrip() for line in text.splitlines()), p.name
        for target in re.findall(r"\]\(([^)]+)\)", text):
            if not target.startswith(("https:", "http:", "#")):
                assert (p.parent / target).resolve().is_file(), (p.name, target)
    print(json.dumps({"status": "PASS", "resources": len(rows), "candidateRows": len(edits),
                      "rewrites": sum(r["decision"] == "CANDIDATE_REWRITE" for r in rows),
                      "pages": len(pages), "conditionalAi": len(conditional), "sourceFiles": len(source_files),
                      "sourceBytesAndAnchors": "PASS" if check_sources else "NOT RUN",
                      "deterministicRegeneration": "PASS" if check_sources else "NOT RUN",
                      "reviewRegressions": "PASS",
                      "keyPlaceholderUnitCsvParity": "PASS", "productTests": "NOT RUN"}))


if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    parser.add_argument("--sources", action="store_true")
    verify(parser.parse_args().sources)
