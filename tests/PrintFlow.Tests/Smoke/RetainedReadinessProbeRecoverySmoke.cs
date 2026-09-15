using System.IO;
using System.Security.Cryptography;
using PrintFlow.Domain.Files;
using PrintFlow.Domain.Results;
using PrintFlow.Infrastructure.Adapters.Photoshop;
using PrintFlow.Infrastructure.Automation;
using PrintFlow.Infrastructure.Configuration;
using PrintFlow.Infrastructure.Workspace;
using PrintFlow.Workflow.Ports;
using Xunit.Abstractions;

namespace PrintFlow.Tests.Smoke;

using Unit = PrintFlow.Domain.Results.Unit;

/// <summary>
/// Opt-in recovery of exactly one PrintFlow-owned readiness probe that a refused round trip
/// retained, and inert by default.
/// </summary>
/// <remarks>
/// <b>Why it exists.</b> When the test-image round trip is refused after the open, the verifier
/// correctly keeps the backing file and leaves the probe loaded, because close ownership was
/// never confirmed. Nothing in the application may then close it on its own initiative, and a
/// manual close is exactly the unguarded unwind the probe's ownership rules exist to avoid.
/// <para>
/// <b>What it drives.</b> Only the existing guarded primitives, under the real shared workstation
/// lease. The accepted Photoshop must already be running and observable before anything that can
/// launch it is composed; a Photoshop that nevertheless comes back launched rather than attached
/// is reported as a refusal and nothing is closed. The probe is closed through
/// <see cref="GuardedPhotoshopUiDriver.CloseExactDocumentAsync(PhotoshopTarget, string, CancellationToken)"/>,
/// which re-proves by absolute path that the active document is the probe before Ctrl+W, and the
/// backing file is deleted only once Photoshop no longer reports any document with the probe's
/// path or name and the file still hashes to the canonical probe image.
/// </para>
/// <para>
/// <b>What it will not do.</b> It names no document other than the one managed probe path derived
/// from the operation id, closes nothing unless — observed under the lease — the probe is
/// Photoshop's only, active, unmodified document, dismisses no dialog, and writes no readiness,
/// regression or revalidation record.
/// </para>
/// </remarks>
public sealed class RetainedReadinessProbeRecoverySmoke(ITestOutputHelper output)
{
    /// <summary>The opt-in: the retained probe's exact 32-character operation id.</summary>
    internal const string OptInVariable = "PRINTFLOW_RECOVER_READINESS_PROBE";

    /// <summary>SHA-256 of the one synthetic image the verifier ever writes as a probe.</summary>
    private const string CanonicalProbeSha256 =
        "431CED6916A2A21A156E38701AFE55BBD7F88969FBBFC56D7FE099D47F265460";

    [Fact]
    public async Task Recover_one_exact_retained_readiness_probe()
    {
        string? operationId = Environment.GetEnvironmentVariable(OptInVariable);
        if (string.IsNullOrEmpty(operationId))
        {
            return;
        }

        if (operationId.Length != 32 || !operationId.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f'))
        {
            throw new InvalidOperationException(
                $"{OptInVariable} must be exactly one 32-character lowercase hexadecimal operation id.");
        }

        string repositoryRoot = RepositoryRoot();
        PrintFlowConfiguration configuration = PrintFlowConfiguration.LoadFromFile(
            Path.Combine(repositoryRoot, "appsettings.json"));
        string workspaceRoot = Path.GetFullPath(configuration.Workspace.Root);
        string manifestPath = Path.Combine(workspaceRoot, configuration.Preset.Path);
        Sha256 expectedPresetHash = Sha256.Parse(configuration.Preset.ExpectedSha256);
        string evidenceDirectory = Path.Combine(workspaceRoot, "Evidence");
        FileWorkspace workspace = new(workspaceRoot);

        WorkspaceFileRef probe = WorkspaceFileRef.Create(
            $"EnvironmentVerification/{operationId}/Working/PF_ENV_PROBE_{operationId}.png",
            WorkspaceArea.Working);
        string absolute = Path.GetFullPath(workspace.ResolveAbsolute(probe));
        string expected = Path.Combine(
            workspaceRoot, "EnvironmentVerification", operationId, "Working", $"PF_ENV_PROBE_{operationId}.png");
        if (!string.Equals(absolute, expected, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"The probe resolved to '{absolute}', not '{expected}'.");
        }

        string probeName = Path.GetFileName(absolute);
        RefuseReparseAncestry(Path.GetDirectoryName(absolute)!, workspaceRoot);
        output.WriteLine($"probe operation : {operationId}");
        output.WriteLine($"probe path      : {absolute}");

        if (!File.Exists(absolute))
        {
            throw new InvalidOperationException("The probe backing file does not exist; there is nothing exact to recover.");
        }

        RequireCanonical(absolute);
        output.WriteLine("backing file    : canonical probe image");

        PresetPhotoshopBaselineProvider baselines = new(manifestPath, expectedPresetHash);
        OperationResult<PhotoshopBaseline> baseline = baselines.GetVerifiedBaseline();
        if (baseline.IsFailure)
        {
            throw new InvalidOperationException($"Photoshop baseline refused: {baseline.Failure.TechnicalDetail}");
        }

        // Read before the lease and before anything is composed that could launch Photoshop: the
        // ROT read only attaches to an accepted Photoshop that is already running.
        RotPhotoshopRuntimeFactReader facts = new();
        OperationResult<PhotoshopRuntimeFacts> running = facts.Read(baseline.Value.ExecutablePath);
        if (running.IsFailure)
        {
            throw new InvalidOperationException(
                $"The running accepted Photoshop could not be observed, so nothing was attached: {running.Failure.TechnicalDetail}");
        }

        SqliteWorkstationAutomationLeaseManager leases = new();
        OperationResult<IWorkstationAutomationLease> lease =
            await leases.TryAcquireAsync(enclosingLease: null, CancellationToken.None);
        if (lease.IsFailure)
        {
            throw new InvalidOperationException($"The workstation lease was not acquired: {lease.Failure.TechnicalDetail}");
        }

        output.WriteLine($"lease           : acquired {lease.Value.ResourceId}");
        try
        {
            // The decision is made only from what Photoshop reports while this lease is held.
            OperationResult<PhotoshopRuntimeFacts> before = facts.Read(baseline.Value.ExecutablePath);
            if (before.IsFailure)
            {
                throw new InvalidOperationException(
                    $"Photoshop could not be observed under the lease, so nothing was attached: {before.Failure.TechnicalDetail}");
            }

            Describe("documents before", before.Value);
            PhotoshopRuntimeDocument[] held = Holding(before.Value, absolute, probeName);

            if (held.Length > 0)
            {
                if (held.Length != 1 ||
                    before.Value.Documents.Length != 1 ||
                    !string.Equals(held[0].FullPath, absolute, StringComparison.OrdinalIgnoreCase) ||
                    !held[0].IsActive ||
                    !held[0].IsSaved)
                {
                    throw new InvalidOperationException(
                        "Photoshop holds the probe, but not as its only, active, unmodified document at the exact " +
                        "managed path. Nothing was closed.");
                }

                PhotoshopAutomationOptions options = new();
                Win32ExternalAppWindowLocator locator = new();
                GuardedPhotoshopUiDriver driver = new(
                    locator,
                    new Win32VerifiedControlSink(),
                    new Win32ScopedInputSink(locator),
                    new GdiWindowEvidenceSink(evidenceDirectory, TimeProvider.System),
                    baselines,
                    options,
                    TimeProvider.System);
                ProductionPhotoshopOutputProcessor foundation = new(
                    baselines, locator, driver, workspace, options, TimeProvider.System);

                OperationResult<PhotoshopReadiness> ready = await foundation.EnsureReadyAsync(CancellationToken.None);
                if (ready.IsFailure || ready.Value.WasLaunched)
                {
                    throw new InvalidOperationException(ready.IsFailure
                        ? $"Photoshop did not attach: {ready.Failure.TechnicalDetail}"
                        : "Photoshop was launched rather than attached; nothing was closed.");
                }

                output.WriteLine($"attached        : process {ready.Value.Target.Process.ProcessId}, state {ready.Value.State.State}");

                OperationResult<PhotoshopTarget> closed = await driver.CloseExactDocumentAsync(
                    ready.Value.Target, absolute, CancellationToken.None);
                if (closed.IsFailure)
                {
                    throw new InvalidOperationException(
                        $"The exact close was refused ({closed.Failure.Code}): {closed.Failure.TechnicalDetail}");
                }

                output.WriteLine("exact close     : confirmed");
            }
            else
            {
                output.WriteLine("exact close     : not needed; Photoshop reports no document with the probe's path or name");
            }

            OperationResult<PhotoshopRuntimeFacts> after = facts.Read(baseline.Value.ExecutablePath);
            if (after.IsFailure)
            {
                throw new InvalidOperationException(
                    $"Photoshop could not be re-observed, so the backing file was retained: {after.Failure.TechnicalDetail}");
            }

            Describe("documents after", after.Value);
            if (Holding(after.Value, absolute, probeName).Length > 0)
            {
                throw new InvalidOperationException(
                    "Photoshop still reports a document with the probe's path or name, so the backing file was retained.");
            }

            RequireCanonical(absolute);
            File.Delete(absolute);
            string working = Path.GetDirectoryName(absolute)!;
            string token = Path.GetDirectoryName(working)!;
            if (!Directory.EnumerateFileSystemEntries(working).Any())
            {
                Directory.Delete(working, recursive: false);
            }

            if (!Directory.EnumerateFileSystemEntries(token).Any())
            {
                Directory.Delete(token, recursive: false);
            }

            output.WriteLine($"backing file    : deleted; token directory present = {Directory.Exists(token)}");
        }
        finally
        {
            OperationResult<Unit> released = await lease.Value.ReleaseAsync(CancellationToken.None);
            output.WriteLine(released.IsSuccess
                ? "lease           : released"
                : $"lease           : RELEASE FAILED {released.Failure.TechnicalDetail}");
        }
    }

    /// <summary>
    /// Every document that could be the probe: its exact path, or its unique name under any path
    /// spelling — including none — Photoshop happens to report.
    /// </summary>
    private static PhotoshopRuntimeDocument[] Holding(PhotoshopRuntimeFacts facts, string absolute, string probeName) =>
        [.. facts.Documents.Where(d =>
            string.Equals(d.FullPath, absolute, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(d.Name, probeName, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(Path.GetFileName(d.FullPath ?? string.Empty), probeName, StringComparison.OrdinalIgnoreCase))];

    private void Describe(string label, PhotoshopRuntimeFacts facts)
    {
        output.WriteLine($"{label,-16}: {facts.Documents.Length}");
        foreach (PhotoshopRuntimeDocument document in facts.Documents)
        {
            output.WriteLine($"                  {document.FullPath ?? document.Name} active={document.IsActive} saved={document.IsSaved}");
        }
    }

    private static void RequireCanonical(string absolute)
    {
        byte[] observed;
        using (FileStream stream = new(absolute, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            observed = SHA256.HashData(stream);
        }

        if (!string.Equals(Convert.ToHexString(observed), CanonicalProbeSha256, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("The backing file is not the canonical probe image, so it was retained.");
        }
    }

    private static void RefuseReparseAncestry(string path, string root)
    {
        string fullRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar);
        for (string? current = path; current is not null; current = Path.GetDirectoryName(current))
        {
            if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
            {
                throw new IOException($"Recovery refuses reparse traversal at '{current}'.");
            }

            if (string.Equals(current.TrimEnd(Path.DirectorySeparatorChar), fullRoot, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }
        }

        throw new InvalidOperationException("The probe directory is not beneath the configured workspace.");
    }

    private static string RepositoryRoot()
    {
        DirectoryInfo? current = new(AppContext.BaseDirectory);
        while (current is not null && !File.Exists(Path.Combine(current.FullName, "PrintFlowStudio.sln")))
        {
            current = current.Parent;
        }

        return current?.FullName
            ?? throw new InvalidOperationException("Could not locate the repository root.");
    }
}
