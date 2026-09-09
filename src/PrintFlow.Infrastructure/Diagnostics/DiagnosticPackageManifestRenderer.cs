using System.Globalization;
using System.Text;
using PrintFlow.Workflow.Services;

namespace PrintFlow.Infrastructure.Diagnostics;

/// <summary>The one human-readable manifest authority for a diagnostic package.</summary>
internal static class DiagnosticPackageManifestRenderer
{
    private const int MaximumValueLength = 4096;

    public static string Render(DiagnosticPackagePlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        StringBuilder text = new();
        text.AppendLine("PrintFlow Studio diagnostic package");
        text.AppendLine("Package format: 1");
        text.AppendLine("Local-only: This package was saved locally. Nothing was uploaded automatically.");
        Field(text, "Created at UTC", plan.CreatedAtUtc.ToString("O", CultureInfo.InvariantCulture));

        text.AppendLine();
        text.AppendLine("[Application]");
        Field(text, "Name", plan.Application.Name);
        Field(text, "Version", plan.Application.Version);

        text.AppendLine();
        text.AppendLine("[Package subject]");
        Field(text, "Processing name", plan.Subject.ProcessingName);
        Field(text, "Session ID", plan.Subject.SessionId.ToString());
        Field(text, "Attempt ID", plan.Subject.AttemptId.ToString());
        Field(text, "Workflow", plan.Subject.Workflow.ToString());
        Field(text, "Step", plan.Subject.Step.ToString());
        Field(text, "Attempt status", plan.Subject.AttemptStatus.ToString());
        Field(text, "Operation", plan.Subject.Operation.ToString());
        Field(text, "Application adapter", plan.Subject.AdapterId);
        Field(text, "Started at UTC", plan.Subject.StartedAtUtc.ToString("O", CultureInfo.InvariantCulture));
        Field(text, "Ended at UTC", Time(plan.Subject.EndedAtUtc));
        Field(text, "Attempt number", plan.Subject.AttemptNumber.ToString(CultureInfo.InvariantCulture));
        Field(text, "Previous retries", plan.Subject.PreviousRetries.ToString(CultureInfo.InvariantCulture));
        Field(text, "Current failure", plan.Subject.IsCurrent ? "Yes" : "No (historical attempt)");

        text.AppendLine();
        text.AppendLine("[Failure]");
        Field(text, "Stable code", Value(plan.Failure.StableCode));
        Field(text, "Operator message key", Value(plan.Failure.MessageKey));
        Field(text, "Technical detail", Value(plan.Failure.TechnicalDetail));
        Field(text, "Structured automation log", plan.Failure.LogStatus.ToString());
        Field(text, "Automation log entry ID", plan.Failure.LogEntryId?.ToString() ?? "Unavailable");
        Field(text, "Automation log time UTC", Time(plan.Failure.LogAtUtc));

        text.AppendLine();
        text.AppendLine("[Local paths]");
        PathField(text, "Managed input", plan.Failure.ManagedInputPath, plan.Failure.InputPathStatus.ToString());
        PathField(text, "Expected output", plan.Failure.ExpectedOutputPath,
            plan.Failure.ExpectedOutputPathStatus.ToString());
        PathField(text, "Failure screenshot", plan.Failure.ScreenshotPath,
            plan.Failure.ScreenshotStatus.ToString());
        Field(text, "Local diagnostic database", plan.Storage.LocalLogLocation);
        Field(text, "Failure screenshot folder", plan.Storage.ScreenshotLocation);

        text.AppendLine();
        text.AppendLine("[Environment readiness]");
        Field(text, "Verified", plan.Environment.Verified ? "Yes" : "No");
        Field(text, "Preset", Value(plan.Environment.PresetIdentity));
        Field(text, "Observed at UTC", plan.Environment.ObservedAt.ToString("O", CultureInfo.InvariantCulture));
        foreach (DiagnosticPackageEnvironmentCheck check in plan.Environment.Checks)
        {
            Field(text, $"Check {check.CheckKey}",
                $"{check.Status}; blocking={check.IsBlocking}");
        }

        text.AppendLine();
        text.AppendLine("[Contents]");
        foreach (DiagnosticPackageItem item in plan.Items)
        {
            string entry = item.ArchiveEntryName is null ? string.Empty : $"; entry={item.ArchiveEntryName}";
            Field(text, item.Role.ToString(), $"{item.Disposition}; {item.Content}{entry}");
        }

        return text.ToString();
    }

    private static void PathField(StringBuilder text, string name, string? path, string status) =>
        Field(text, name, $"{status}; {Value(path)}");

    private static string Time(DateTimeOffset? value) =>
        value?.ToString("O", CultureInfo.InvariantCulture) ?? "Unavailable";

    private static string Value(string? value) => string.IsNullOrWhiteSpace(value) ? "Unavailable" : value;

    private static void Field(StringBuilder text, string name, string value)
    {
        string normal = value.Replace('\r', ' ').Replace('\n', ' ');
        if (normal.Length > MaximumValueLength)
            normal = string.Concat(normal.AsSpan(0, MaximumValueLength - 1), "…");
        text.Append(name).Append(": ").AppendLine(normal);
    }
}
