using PrintFlow.Domain.Attempts;
using PrintFlow.Domain.Automation;
using PrintFlow.Domain.Ids;
using PrintFlow.Domain.Revisions;
using PrintFlow.Domain.Results;
using PrintFlow.Domain.Sessions;
using PrintFlow.Workflow.Ports;

namespace PrintFlow.Workflow.Services;

/// <summary>
/// A pure, bounded classification of old diagnostics. It grants no filesystem authority: the
/// file adapter repeats containment, age and reparse checks immediately before deletion.
/// </summary>
public sealed record DiagnosticRetentionPlan(
    IReadOnlyList<AutomationLogId> Keep,
    IReadOnlyList<AutomationLogId> ExpireDatabaseDiagnostic,
    IReadOnlyDictionary<string, IReadOnlyList<AutomationLogId>> DeleteDiagnosticFile,
    IReadOnlyList<string> IgnoreUnknown)
{
    public static DiagnosticRetentionPlan Create(
        string evidenceRoot,
        DateTimeOffset cutoffUtc,
        IReadOnlyList<AutomationLogEntry> candidates,
        IReadOnlyList<DiagnosticFileReference> references,
        IReadOnlyDictionary<SessionId, SessionAggregate?> sessions,
        IReadOnlyList<string> authoritativePaths)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(evidenceRoot);
        ArgumentNullException.ThrowIfNull(candidates);
        ArgumentNullException.ThrowIfNull(references);
        ArgumentNullException.ThrowIfNull(sessions);
        ArgumentNullException.ThrowIfNull(authoritativePaths);

        string root = Path.GetFullPath(evidenceRoot);
        HashSet<string> authorities = Canonical(authoritativePaths);
        HashSet<AutomationLogId> keep = [];
        HashSet<AutomationLogId> expire = [];
        Dictionary<string, IReadOnlyList<AutomationLogId>> delete = new(StringComparer.OrdinalIgnoreCase);
        HashSet<string> ignored = new(StringComparer.OrdinalIgnoreCase);
        Dictionary<string, List<AutomationLogEntry>> byPath = new(StringComparer.OrdinalIgnoreCase);
        foreach (AutomationLogEntry entry in candidates.Where(entry =>
                     !string.IsNullOrWhiteSpace(entry.ScreenshotPath)))
        {
            if (!TryFull(entry.ScreenshotPath!, out string? path))
            {
                keep.Add(entry.Id);
                ignored.Add(entry.ScreenshotPath!);
                continue;
            }
            if (!byPath.TryGetValue(path, out List<AutomationLogEntry>? entries))
                byPath[path] = entries = [];
            entries.Add(entry);
        }

        Dictionary<string, List<DiagnosticFileReference>> referencesByPath =
            new(StringComparer.OrdinalIgnoreCase);
        foreach (DiagnosticFileReference reference in references.Where(reference =>
                     !string.IsNullOrWhiteSpace(reference.Path)))
        {
            if (!TryFull(reference.Path, out string? path)) continue;
            if (!referencesByPath.TryGetValue(path, out List<DiagnosticFileReference>? pathReferences))
                referencesByPath[path] = pathReferences = [];
            pathReferences.Add(reference);
        }

        foreach (AutomationLogEntry entry in candidates.Where(entry => string.IsNullOrWhiteSpace(entry.ScreenshotPath)))
        {
            if (IsProtected(entry.SessionId, FailureEvidence.AttemptIdOf(entry.Failure), entry.Step, sessions))
                keep.Add(entry.Id);
            else
                expire.Add(entry.Id);
        }

        foreach ((string path, List<AutomationLogEntry> pathCandidates) in byPath)
        {
            List<AutomationLogEntry> historical = pathCandidates
                .Where(entry => !IsProtected(
                    entry.SessionId, FailureEvidence.AttemptIdOf(entry.Failure), entry.Step, sessions))
                .ToList();
            foreach (AutomationLogEntry active in pathCandidates.Except(historical)) keep.Add(active.Id);
            if (historical.Count == 0) continue;

            if (!IsOwnedCapture(root, path))
            {
                ignored.Add(path);
                foreach (AutomationLogEntry entry in historical) keep.Add(entry.Id);
                continue;
            }

            if (authorities.Contains(path))
            {
                // The file belongs to another Product authority. Only the diagnostic record is
                // old; the bytes are outside this policy.
                foreach (AutomationLogEntry entry in historical) expire.Add(entry.Id);
                continue;
            }

            if (!referencesByPath.TryGetValue(path, out List<DiagnosticFileReference>? pathReferences))
            {
                // A candidate that vanished from the complete-reference read is not proof of
                // ownership. Preserve both the file and the row that may explain it.
                ignored.Add(path);
                foreach (AutomationLogEntry entry in historical) keep.Add(entry.Id);
                continue;
            }

            bool fileStillNeeded = pathReferences.Any(reference =>
                reference.AtUtc >= cutoffUtc ||
                IsProtected(reference.SessionId, reference.AttemptId, reference.Step, sessions));
            if (fileStillNeeded)
            {
                foreach (AutomationLogEntry entry in historical) expire.Add(entry.Id);
                continue;
            }

            delete[path] = historical.Select(entry => entry.Id).Distinct().ToList();
        }

        return new DiagnosticRetentionPlan(
            keep.ToList(),
            expire.ToList(),
            delete,
            ignored.ToList());
    }

    /// <summary>
    /// Positive ownership is a durable diagnostic reference plus the exact direct-child layout
    /// produced by <c>GdiWindowEvidenceSink</c>. Directory, age, or extension alone proves none
    /// of these facts.
    /// </summary>
    public static bool IsOwnedCapture(string evidenceRoot, string path)
    {
        string root = Path.GetFullPath(evidenceRoot);
        string candidate;
        try
        {
            candidate = Path.GetFullPath(path);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }

        if (!string.Equals(Path.GetDirectoryName(candidate), root, StringComparison.OrdinalIgnoreCase))
            return false;

        string name = Path.GetFileName(candidate);
        if (name.Length < 23 || !name.EndsWith(".png", StringComparison.OrdinalIgnoreCase) ||
            name[8] != 'T' || name[15] != 'Z' || name[16] != '_') return false;

        // yyyyMMddTHHmmssZ_reason_HEX.png — the two separators are unambiguous because the
        // capture writer replaces every non-alphanumeric reason character with '-'.
        if (!name.AsSpan(0, 8).ToString().All(char.IsAsciiDigit) ||
            !name.AsSpan(9, 6).ToString().All(char.IsAsciiDigit)) return false;
        int handleSeparator = name.LastIndexOf('_');
        if (handleSeparator <= 17 || handleSeparator >= name.Length - 5) return false;
        ReadOnlySpan<char> reason = name.AsSpan(17, handleSeparator - 17);
        ReadOnlySpan<char> handle = name.AsSpan(handleSeparator + 1, name.Length - handleSeparator - 5);
        return reason.Length is >= 1 and <= 48 &&
               reason.ToString().All(c => char.IsAsciiLetterOrDigit(c) || c == '-') &&
               handle.Length > 0 && handle.ToString().All(char.IsAsciiHexDigit);
    }

    private static bool IsProtected(
        SessionId? sessionId,
        AttemptId? attemptId,
        StepKind? step,
        IReadOnlyDictionary<SessionId, SessionAggregate?> sessions)
    {
        if (sessionId is not { } sid || !sessions.TryGetValue(sid, out SessionAggregate? aggregate) ||
            aggregate is null || aggregate.Session.State is not (SessionState.Active or SessionState.HandedOff))
            return false;

        SessionStep? currentStep = aggregate.Steps.FirstOrDefault(candidate =>
            candidate.Step == aggregate.Session.CurrentStep);
        if (currentStep is null) return false;

        HashSet<AttemptId> protectedAttempts = ProtectedAttempts(aggregate, currentStep);
        if (attemptId is { } id) return protectedAttempts.Contains(id);

        // A legacy row without exact attempt identity stays protected only when it names the
        // current unresolved step. Ambiguity narrows retention; it never broadens deletion.
        return step == currentStep.Step &&
               currentStep.State is StepState.Failed or StepState.RetryRequired or
                   StepState.Interrupted or StepState.Processing;
    }

    private static HashSet<AttemptId> ProtectedAttempts(SessionAggregate aggregate, SessionStep currentStep)
    {
        HashSet<AttemptId> protectedAttempts = aggregate.Attempts
            .Where(attempt => attempt.Status == AttemptStatus.Running)
            .Select(attempt => attempt.Id)
            .ToHashSet();

        ProcessingAttempt? current = ErrorDetailsSelection.Current(currentStep, aggregate.Attempts);
        if (current is not null) protectedAttempts.Add(current.Id);

        if (currentStep.State is not (StepState.Interrupted or StepState.Failed or
            StepState.RetryRequired or StepState.Processing)) return protectedAttempts;

        Dictionary<AttemptId, ProcessingAttempt> attempts = aggregate.Attempts
            .Where(attempt => attempt.Step == currentStep.Step)
            .ToDictionary(attempt => attempt.Id);
        ProcessingAttempt? cursor = attempts.Values
            .OrderByDescending(attempt => attempt.RetrySequence)
            .ThenByDescending(attempt => attempt.StartedAtUtc)
            .ThenByDescending(attempt => attempt.Id.ToString(), StringComparer.Ordinal)
            .FirstOrDefault();

        for (int remaining = attempts.Count; cursor is not null && remaining > 0; remaining--)
        {
            protectedAttempts.Add(cursor.Id);
            if (cursor.Status == AttemptStatus.Interrupted) break;
            if (cursor.Operation != OperationKind.ManualResultImport ||
                cursor.Status is not (AttemptStatus.Failed or AttemptStatus.Running) ||
                cursor.RetryOfAttemptId is not { } previous) break;
            attempts.TryGetValue(previous, out cursor);
        }

        return protectedAttempts;
    }

    private static HashSet<string> Canonical(IEnumerable<string> paths)
    {
        HashSet<string> result = new(StringComparer.OrdinalIgnoreCase);
        foreach (string path in paths)
        {
            try { result.Add(Full(path)); }
            catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException) { }
        }
        return result;
    }

    private static string Full(string path) => Path.GetFullPath(path);

    private static bool TryFull(string path, out string full)
    {
        try
        {
            full = Full(path);
            return true;
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            full = string.Empty;
            return false;
        }
    }
}
