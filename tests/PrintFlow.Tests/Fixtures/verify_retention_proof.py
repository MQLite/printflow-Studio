"""Read-only, independent disk + SQLite verification of the synthetic SCRUM-11114 proof."""
import hashlib
import json
import pathlib
import sqlite3
import sys


def digest(path):
    with open(path, "rb") as stream:
        return hashlib.file_digest(stream, "sha256").hexdigest().upper()


manifest = json.loads(pathlib.Path(sys.argv[1]).read_text(encoding="utf-8-sig"))
root = pathlib.Path(manifest["WorkspaceRoot"]).resolve(strict=True)
database = pathlib.Path(manifest["Database"]).resolve(strict=True)
connection = sqlite3.connect(database.as_uri() + "?mode=ro", uri=True)
connection.row_factory = sqlite3.Row
assert connection.execute("PRAGMA integrity_check").fetchone()[0] == "ok"
assert not connection.execute("PRAGMA foreign_key_check").fetchall()
session = connection.execute("SELECT * FROM ProcessingSession WHERE Id = ?", (manifest["SessionId"],)).fetchone()
assert session["State"] == "COMPLETED" and session["CompletedAtUtc"]
session_root = (root / session["WorkspacePath"]).resolve(strict=True)
session_root.relative_to(root)
revisions = connection.execute("SELECT * FROM Revision WHERE SessionId = ?", (session["Id"],)).fetchall()
outputs = connection.execute("SELECT * FROM PrintOutput WHERE SessionId = ?", (session["Id"],)).fetchall()
before = {r["Id"]: r for r in manifest["IdentityBefore"]}
verified = []


def verify(row, kind):
    path = (root / row["RelativePath"]).resolve(strict=True)
    path.relative_to(session_root)
    assert path.is_file()
    assert digest(path) == row["Sha256"].upper(), path
    assert path.stat().st_size == row["ByteLength"], path
    verified.append({"Kind": kind, "Id": row["Id"], "Path": row["RelativePath"], "Sha256": row["Sha256"]})


for revision in revisions:
    original = before[revision["Id"]]
    for column in ("Sha256", "ByteLength", "PixelWidth", "PixelHeight"):
        assert revision[column] == original[column], (revision["Id"], column)
    assert revision["RetentionReleasedAtUtc"] is None  # This proof has no rejected results.
    verify(revision, "Revision")
for output in outputs:
    assert output["ReviewState"] == "APPROVED" and output["RecycledAtUtc"] is None
    verify(output, "PrintOutput")
    assert connection.execute(
        "SELECT COUNT(*) FROM ReviewDecision WHERE SubjectId = ? AND Decision = 'APPROVED' AND ReviewedSha256 = ?",
        (output["Id"], output["Sha256"]),
    ).fetchone()[0] == 1
snapshot = connection.execute("SELECT * FROM InputSnapshot WHERE SessionId = ?", (session["Id"],)).fetchone()
assert snapshot["RootRevisionId"] in before
assert digest(snapshot["OriginalSourcePath"]) == manifest["SourceSha256"]
assert digest(manifest["Evidence"]) == manifest["EvidenceSha256"]
failures = connection.execute("SELECT FailureDetailJson FROM ProcessingAttempt WHERE SessionId = ? AND ResultStatus = 'FAILED'",
                              (session["Id"],)).fetchall()
assert any(manifest["Evidence"] in json.loads(row[0])["Context"].values() for row in failures)
for relative in manifest["RemovedWorkingFiles"]:
    candidate = (root / relative).resolve()
    candidate.relative_to(session_root)
    assert not candidate.exists(), candidate
assert connection.execute("SELECT SessionId FROM AutomationLock WHERE Id = 1").fetchone()[0] is None
assert manifest["RestartVerified"]
connection.close()
print(json.dumps({"Result": "PASS", "SessionId": session["Id"], "VerifiedFiles": verified,
                  "RemovedWorkingFileCount": len(manifest["RemovedWorkingFiles"]),
                  "Source": "unchanged", "InputSnapshot": "retained", "FailedEvidence": "retained",
                  "DatabaseIntegrity": "ok", "AutomationLock": "free", "Restart": "verified"}, indent=2))
