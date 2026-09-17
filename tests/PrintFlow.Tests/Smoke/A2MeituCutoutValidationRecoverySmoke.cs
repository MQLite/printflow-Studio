using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using PrintFlow.Domain.Files;
using PrintFlow.Infrastructure.Adapters.Meitu;
using PrintFlow.Infrastructure.Automation;
using PrintFlow.Infrastructure.Configuration;
using PrintFlow.Tests.Fixtures;
using Xunit.Abstractions;

namespace PrintFlow.Tests.Smoke;

/// <summary>
/// Opt-in, incident-bound recovery of the synthetic Background Removal document that the
/// production-seam smoke left loaded after Product output validation refused its export.
/// </summary>
/// <remarks>
/// It is inert unless every exact binding is supplied: the refused transcript and its hash, the
/// controlled synthetic source and refused cutout with their hashes, and the Meitu process
/// instance. It uses only Product's guarded dismissal of the signed save-result surface, a fresh
/// ordinary identity probe, and the signed close. It never saves, discards, re-exports or deletes.
/// </remarks>
public sealed class A2MeituCutoutValidationRecoverySmoke(ITestOutputHelper output)
{
    [Fact]
    public async Task Recover_exact_refused_synthetic_cutout_document_through_Product()
    {
        if (Environment.GetEnvironmentVariable("PRINTFLOW_A2_MEITU_RECOVER_CUTOUT_SYNTHETIC") != "1") return;

        string file = Path.GetFullPath(Required("PRINTFLOW_A2_MEITU_CUTOUT_SYNTHETIC_FILE"));
        string fileHash = Required("PRINTFLOW_A2_MEITU_CUTOUT_SYNTHETIC_SHA256");
        string refused = Path.GetFullPath(Required("PRINTFLOW_A2_MEITU_CUTOUT_REFUSED_OUTPUT"));
        string refusedHash = Required("PRINTFLOW_A2_MEITU_CUTOUT_REFUSED_SHA256");
        string transcriptPath = Required("PRINTFLOW_A2_MEITU_CUTOUT_TRANSCRIPT");
        string transcriptHash = Required("PRINTFLOW_A2_MEITU_CUTOUT_TRANSCRIPT_SHA256");

        string controlledRoot =
            Path.GetFullPath(Path.Combine(Path.GetTempPath(), "PrintFlowMeituCutoutSeam")) + Path.DirectorySeparatorChar;
        file.StartsWith(controlledRoot, StringComparison.OrdinalIgnoreCase).ShouldBeTrue();
        Path.GetFileName(file).StartsWith("PF_BACKGROUND_C2A_", StringComparison.Ordinal).ShouldBeTrue();
        refused.ShouldBe(Path.Combine(
            Path.GetDirectoryName(file)!, Path.GetFileNameWithoutExtension(file) + "_CUTOUT.png"));
        Hash(file).ShouldBe(fileHash);
        Hash(refused).ShouldBe(refusedHash);
        Hash(transcriptPath).ShouldBe(transcriptHash);

        string transcript = File.ReadAllText(transcriptPath);
        transcript.ShouldContain("/" + Path.GetFileName(file));
        transcript.ShouldContain("OutputValidationFailed");
        transcript.ShouldContain("Background Removal must preserve the observed canvas dimensions.");
        transcript.ShouldContain("No AdapterOutput and no Revision were produced.");
        transcript.ShouldContain("source SHA-256        : " + fileHash);

        DirectoryInfo? root = new(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "PrintFlowStudio.sln"))) root = root.Parent;
        root.ShouldNotBeNull();
        PrintFlowConfiguration configuration =
            PrintFlowConfiguration.LoadFromFile(Path.Combine(root.FullName, "appsettings.json"));
        PresetMeituBaselineProvider provider = new(
            Path.Combine(configuration.Workspace.Root, configuration.Preset.Path),
            Sha256.Parse(configuration.Preset.ExpectedSha256));
        var baseline = provider.GetVerifiedBaseline();
        baseline.IsSuccess.ShouldBeTrue();
        Hash(baseline.Value.ExecutablePath).ShouldBe(baseline.Value.ExecutableSha256.Value);

        await using WorkstationAutomationLeaseScope lease = await WorkstationAutomationLeaseScope.AcquireDefaultAsync();
        Win32ExternalAppWindowLocator locator = new();
        var processes = locator.FindProcessesByExecutable(baseline.Value.ExecutablePath);
        processes.IsSuccess.ShouldBeTrue();
        processes.Value.Count.ShouldBe(1);
        var process = processes.Value[0];
        process.StartedUtc.ShouldBe(DateTimeOffset.Parse(
            Required("PRINTFLOW_A2_MEITU_PROCESS_STARTED_UTC"), System.Globalization.CultureInfo.InvariantCulture));
        var windows = locator.FindTopLevelWindows(process);
        windows.IsSuccess.ShouldBeTrue();
        var editors = windows.Value.Where(w => w.Title == baseline.Value.DocumentIdentity!.Editor.WindowTitle).ToArray();
        editors.Length.ShouldBe(1);
        MeituTarget target = new(process, editors[0]);

        string evidence = Path.Combine(Path.GetTempPath(), "PrintFlowMeituRecovery", Guid.NewGuid().ToString("N"));
        GuardedMeituUiDriver driver = new(locator, new UiaElementProvider(), new Win32ScopedInputSink(locator),
            new GdiWindowEvidenceSink(evidence, TimeProvider.System), provider, new MeituAutomationOptions(),
            TimeProvider.System);
        output.WriteLine("ObservedAtUtc: " + DateTimeOffset.UtcNow.ToString("O"));
        output.WriteLine("ExactSyntheticFile: " + file + "; SHA256=" + fileHash);
        output.WriteLine("RefusedCutout: " + refused + "; SHA256=" + refusedHash);
        output.WriteLine("RefusedTranscript: " + transcriptPath + "; SHA256=" + transcriptHash);
        output.WriteLine("Process: " + JsonSerializer.Serialize(process));
        output.WriteLine($"Editor: {editors[0].Handle} '{editors[0].Title}' {editors[0].ClassName} " +
            $"enabled={editors[0].IsEnabled}");

        // The signed result surface is recognised by Product's own class and marker rule; an
        // unrecognised surface is left alone and the later identity probe refuses.
        var dismissed = await driver.DismissExportResultSurfaceAsync(target, CancellationToken.None);
        output.WriteLine("DismissSignedSaveResult: " +
            (dismissed.IsSuccess ? (dismissed.Value ? "dismissed" : "none-present") : dismissed.Failure.TechnicalDetail));
        dismissed.IsSuccess.ShouldBeTrue(dismissed.IsFailure ? dismissed.Failure.TechnicalDetail : string.Empty);
        dismissed.Value.ShouldBeTrue();

        var confirmed = await driver.ConfirmWorkingCopyIdentityAsync(target, Path.GetFileName(file), CancellationToken.None);
        output.WriteLine("IdentityBeforeClose: " +
            (confirmed.IsSuccess ? $"{confirmed.Value.State} ({confirmed.Value.Observation.ObservedDocumentIdentity})"
                                 : confirmed.Failure.TechnicalDetail));
        confirmed.IsSuccess.ShouldBeTrue(confirmed.IsFailure ? confirmed.Failure.TechnicalDetail : string.Empty);
        confirmed.Value.State.ShouldBe(MeituStartingState.KnownEditorWithExpectedWorkingCopy);

        var activated = await driver.ActivateAsync(target, CancellationToken.None);
        activated.IsSuccess.ShouldBeTrue(activated.IsFailure ? activated.Failure.TechnicalDetail : string.Empty);
        var closed = await driver.CloseDocumentAsync(activated.Value, CancellationToken.None);
        output.WriteLine("CloseExactSynthetic: " + (closed.IsSuccess ? "KnownEditorEmpty" : closed.Failure.TechnicalDetail));
        closed.IsSuccess.ShouldBeTrue(closed.IsFailure ? closed.Failure.TechnicalDetail : string.Empty);

        Hash(file).ShouldBe(fileHash);
        Hash(refused).ShouldBe(refusedHash);
        output.WriteLine("Synthetic source and refused cutout preserved unchanged; no Save, discard, export or file cleanup.");
    }

    private static string Required(string name) =>
        Environment.GetEnvironmentVariable(name) is { Length: > 0 } value
            ? value
            : throw new InvalidOperationException($"{name} is required; nothing was run.");

    private static string Hash(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
}
