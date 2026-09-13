using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using PrintFlow.Domain.Files;
using PrintFlow.Infrastructure.Automation;
using PrintFlow.Infrastructure.Configuration;
using PrintFlow.Infrastructure.Verification;
using PrintFlow.Infrastructure.Workspace;
using PrintFlow.Tests.Diagnostics;
using PrintFlow.Workflow.Ports;
using Xunit.Abstractions;

namespace PrintFlow.Tests.Smoke;

/// <summary>
/// The PF-AUDIT-R4 bounded readiness diagnosis, opt-in and inert by default.
/// </summary>
/// <remarks>
/// <b>Not a second readiness entry.</b>
/// <see cref="WorkstationVerificationSmoke.Run_the_explicit_live_application_verification"/>
/// remains the ordinary one and is unchanged. This exists only because that entry cannot reach
/// its live phase while <c>ProductionRevalidation</c> is failing, which on this workstation is
/// the single automatic check that fails — so the probe R4 must localise never runs and the
/// report says nothing about it.
/// <para>
/// <b>What it drives.</b> The same whole-live-check procedure: the automation lock, Meitu and
/// Photoshop launchability and safe starting states, Photoshop's colour settings and the
/// test-image round trip. Nothing is filtered to obtain a Photoshop-only answer, the accepted
/// preset and binaries are the same, and the shared workstation lease is the real one the
/// application uses. This does acquire the actual default automation lease, which is why the
/// opt-in exists and why it is set for one invocation rather than in a profile.
/// </para>
/// <para>
/// <b>What it cannot do.</b> It composes a verifier and a gate, held in local variables for the
/// life of this method. There is no service provider, no <c>SessionService</c>, no adapter and
/// no workflow, so nothing here can execute a standard-set case, produce an operator
/// attestation, or write a revalidation record. Its result is a
/// <see cref="ReadinessDiagnosticEnvelope"/>, which is deliberately not the schema any of those
/// consume.
/// </para>
/// </remarks>
public sealed class ReadinessDiagnosticSmoke(ITestOutputHelper output)
{
    [Fact]
    public async Task Run_the_bounded_readiness_diagnosis()
    {
        // The guard precedes every operational step: the composition delegate below is not
        // invoked, so a default `dotnet test` opens no store and touches no application.
        ReadinessDiagnosticEnvelope? envelope = await ReadinessDiagnostic.RunAsync(
            Environment.GetEnvironmentVariable(ReadinessDiagnostic.OptInVariable),
            Compose,
            TimeProvider.System,
            CancellationToken.None);

        if (envelope is null)
        {
            return;
        }

        output.WriteLine(JsonSerializer.Serialize(envelope, new JsonSerializerOptions
        {
            WriteIndented = true,
            Converters = { new JsonStringEnumConverter() },
        }));

        output.WriteLine(string.Empty);
        output.WriteLine($"diagnostic id            : {envelope.DiagnosticId}");
        output.WriteLine($"started (utc)            : {envelope.StartedAtUtc:u}");
        output.WriteLine($"revalidation omitted     : {envelope.RevalidationCheckOmitted}");
        output.WriteLine($"normal report verified   : {envelope.NormalReport.Verified}");
        output.WriteLine($"diagnostic verified      : {envelope.DiagnosticReport.Verified}");
        output.WriteLine($"production authorised    : {envelope.ProductionAuthorised}");
        output.WriteLine(envelope.Authority);

        output.WriteLine(string.Empty);
        foreach (EnvironmentCheckReport check in envelope.DiagnosticReport.Checks)
        {
            output.WriteLine($"[{check.Status,-8}] {check.Phase,-15} {check.CheckKey}");
            output.WriteLine($"             expected: {check.Expected ?? "-"}");
            output.WriteLine($"             current : {check.Current ?? "-"}");
            output.WriteLine($"             {check.Detail}");
        }

        ReadinessProbeDiagnostics? probe = envelope.DiagnosticReport.Lifecycle?.LatestProbe;
        output.WriteLine(string.Empty);
        if (probe is null)
        {
            // An absent probe means none was returned — which is not the same as one that
            // succeeded quietly, and is reported as the unknown it is.
            output.WriteLine("latest probe             : (none returned)");
        }
        else
        {
            output.WriteLine($"probe operation          : {probe.OperationId}");
            output.WriteLine($"probe path               : {probe.ManagedPath ?? "(none established)"}");
            output.WriteLine($"probe process            : {probe.Process.ProcessId} {probe.Process.ExecutablePath}");
            output.WriteLine($"stages                   : {string.Join(" -> ", probe.Stages)}");
            output.WriteLine($"last attempted stage     : {probe.LastAttemptedStage}");
            output.WriteLine($"last confirmed stage     : {probe.LastConfirmedStage}");
            output.WriteLine($"cleanup                  : {probe.CleanupOutcome} — {probe.CleanupReason}");
            output.WriteLine($"primary failure          : {Describe(probe.PrimaryFailure)}");
            foreach (ReadinessProbeFailure secondary in probe.SecondaryFailures)
            {
                output.WriteLine($"secondary failure        : {Describe(secondary)}");
            }
        }

        // Reported, never asserted. A workstation that fails this is a fact to localise, not a
        // test to turn red — and a passing assertion here would be the closest thing this file
        // could produce to a readiness verdict it has no authority to give.
    }

    private static string Describe(ReadinessProbeFailure? failure) =>
        failure is null
            ? "(none)"
            : $"{failure.Phase}/{failure.Code}: {failure.Detail} " +
              $"(input sent: {failure.InputSent ?? "unrecorded"}, " +
              $"confirm pressed: {failure.ConfirmPressed ?? "unrecorded"})";

    /// <summary>
    /// The application's own composition, plus the same one with the revalidation check omitted.
    /// </summary>
    /// <remarks>
    /// Both are built from the repository <c>appsettings.json</c>, the configured workspace, the
    /// real default automation lease store and the real machine readers, so the diagnosis
    /// observes the workstation the operator has rather than one assembled for it.
    /// </remarks>
    private static ReadinessDiagnosticVerifiers Compose()
    {
        string repositoryRoot = RepositoryRoot();
        PrintFlowConfiguration configuration = PrintFlowConfiguration.LoadFromFile(
            Path.Combine(repositoryRoot, "appsettings.json"));

        string workspaceRoot = Path.GetFullPath(configuration.Workspace.Root);
        string manifestPath = Path.Combine(workspaceRoot, configuration.Preset.Path);
        Sha256 expectedPresetHash = Sha256.Parse(configuration.Preset.ExpectedSha256);
        string evidenceDirectory = Path.Combine(workspaceRoot, "Evidence");

        FileWorkspace workspace = new(workspaceRoot);

        // The real shared authority, exactly as the application composes it. Constructing it is
        // inert; only an acquisition touches the store.
        SqliteWorkstationAutomationLeaseManager leases = new();

        return new ReadinessDiagnosticVerifiers(
            ProductionWorkstationVerifier.ForWorkstation(
                manifestPath,
                configuration.Preset.Id,
                configuration.Preset.Version,
                expectedPresetHash,
                workspaceRoot,
                workspace,
                leases,
                evidenceDirectory,
                TimeProvider.System),
            ProductionWorkstationVerifier.ForStandardRegressionRun(
                manifestPath,
                configuration.Preset.Id,
                configuration.Preset.Version,
                expectedPresetHash,
                workspaceRoot,
                workspace,
                leases,
                evidenceDirectory,
                TimeProvider.System));
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
