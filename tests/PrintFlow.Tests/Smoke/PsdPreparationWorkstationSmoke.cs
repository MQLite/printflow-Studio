using System.IO;
using Microsoft.Extensions.DependencyInjection;
using PrintFlow.App.Composition;
using PrintFlow.Domain.Files;
using PrintFlow.Domain.Revisions;
using PrintFlow.Domain.Sessions;
using PrintFlow.Infrastructure.Adapters.Photoshop;
using PrintFlow.Infrastructure.Configuration;
using PrintFlow.Infrastructure.Sqlite;
using PrintFlow.Tests.Fixtures;
using PrintFlow.Tests.Integration.Ui;
using PrintFlow.Workflow.Commands;
using PrintFlow.Workflow.Ports;
using PrintFlow.Workflow.Services;
using Xunit.Abstractions;

namespace PrintFlow.Tests.Smoke;

public sealed class PsdPreparationWorkstationSmoke(ITestOutputHelper output)
{
    [Theory]
    [InlineData("rgb")]
    [InlineData("transparent")]
    [InlineData("no-composite")]
    [InlineData("w1")]
    [InlineData("cmyk")]
    public async Task Accepted_production_prepares_synthetic_PSD_and_returns_clean(string variant)
    {
        if (Environment.GetEnvironmentVariable("PRINTFLOW_PSD_PREPARATION_SMOKE") != "1") return;
        if (Environment.GetEnvironmentVariable("PRINTFLOW_PSD_CASE") is { } selected && selected != variant) return;
        DirectoryInfo? repo = new(AppContext.BaseDirectory);
        while (repo is not null && !File.Exists(Path.Combine(repo.FullName, "PrintFlowStudio.sln"))) repo = repo.Parent;
        var configuration = PrintFlowConfiguration.LoadFromFile(Path.Combine(repo!.FullName, "appsettings.json"));
        configuration.Adapters.Mode.ShouldBe("Production");
        string qa = Path.Combine(configuration.Workspace.Root, "Evidence", "SCRUM-11099-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(qa);
        output.WriteLine("Evidence workspace: " + qa);
        var factory = new SqliteConnectionFactory(Path.Combine(qa, "psd-smoke.db"));
        using (var connection = factory.Open()) MigrationRunner.Migrate(connection).IsSuccess.ShouldBeTrue();
        using var provider = ServiceRegistration.BuildServiceProvider(configuration, configuration.Workspace.Root, factory);
        var processor = provider.GetRequiredService<IPhotoshopOutputProcessor>().ShouldBeOfType<ProductionPhotoshopOutputProcessor>();
        PhotoshopReadiness before = await EnsureReadyUnderLeaseAsync(provider, processor, verifyGate: true);
        output.WriteLine("Photoshop starting state: " + before.State.State);
        (before.State.State is PhotoshopStartingState.KnownStartScreen or PhotoshopStartingState.KnownEditorNoDocument)
            .ShouldBeTrue("The controlled smoke requires an accepted clean Photoshop starting state.");
        var service = provider.GetRequiredService<ISessionService>();
        var repository = provider.GetRequiredService<ISessionRepository>();
        byte[] bytes = PsdInputPreparationTests.RgbCompositePsd(variant == "transparent", variant == "w1");
        if (variant == "no-composite") bytes[50] = 0;
        if (variant == "cmyk") { bytes[13] = 4; bytes[25] = 4; bytes = [.. bytes, .. new byte[12]]; }
        string source = Path.Combine(qa, "PF_SCRUM11099_" + variant + ".psd");
        File.WriteAllBytes(source, bytes);
        var imported = await service.ImportAsync(WorkflowType.GeneratePrintTiff, source, null, "synthetic-qa", CancellationToken.None);
        imported.IsSuccess.ShouldBeTrue();
        output.WriteLine("Session: " + imported.Value.Id);
        var prepared = await service.ExecuteAsync(imported.Value.Id, new WorkflowCommand.StartStep(StepKind.OriginalConfirmation), "synthetic-qa", CancellationToken.None);
        output.WriteLine("Preparation: " + (prepared.IsSuccess ? "SUCCEEDED" : prepared.Failure.ToString()));
        (await repository.GetAutomationLockAsync(CancellationToken.None)).Value.IsHeld.ShouldBeFalse();
        output.WriteLine("Automation lock: free");
        File.ReadAllBytes(source).ShouldBe(bytes);
        if (variant == "no-composite")
        {
            prepared.IsFailure.ShouldBeTrue();
            prepared.Failure.Code.ShouldBe(PrintFlow.Domain.Results.FailureCode.PsdCompositeMissing);
            (await repository.LoadAsync(imported.Value.Id, CancellationToken.None)).Value!.Revisions.Count.ShouldBe(1);
            output.WriteLine("No-composite source refused before Photoshop open; no prepared Revision.");
            return;
        }
        if (variant == "cmyk")
        {
            prepared.IsFailure.ShouldBeTrue();
            prepared.Failure.Code.ShouldBe(PrintFlow.Domain.Results.FailureCode.PsdUnsupported);
            var refused = (await repository.LoadAsync(imported.Value.Id, CancellationToken.None)).Value!;
            refused.Revisions.Count.ShouldBe(1);
            var facts = refused.Attempts.Single(a => a.Operation == OperationKind.PreparePsd).PsdInspection!;
            output.WriteLine("Refused inspection: " + System.Text.Json.JsonSerializer.Serialize(facts));
            facts.OriginalMode.ShouldBe("CMYK");
            PhotoshopReadiness clean = await EnsureReadyUnderLeaseAsync(provider, processor, verifyGate: false);
            clean.State.State.ShouldBe(before.State.State);
            output.WriteLine("Unsupported PSD colour mode refused; no prepared Revision; Photoshop clean.");
            return;
        }
        prepared.IsSuccess.ShouldBeTrue(prepared.IsFailure ? prepared.Failure.ToString() : "");
        var state = (await repository.LoadAsync(imported.Value.Id, CancellationToken.None)).Value!;
        var root = state.Revisions.Single(r => r.IsRoot);
        var raster = state.Revisions.Single(r => r.Operation == OperationKind.PreparePsd);
        var attempt = state.Attempts.Single(a => a.Operation == OperationKind.PreparePsd);
        output.WriteLine($"Source: {root.Id} / {root.Sha256}");
        output.WriteLine($"Raster: {raster.Id} / {raster.Sha256} / {raster.Facts.PixelWidth}x{raster.Facts.PixelHeight}");
        output.WriteLine("Inspection: " + System.Text.Json.JsonSerializer.Serialize(attempt.PsdInspection));
        attempt.PsdInspection!.HasTransparency.ShouldBe(variant == "transparent");
        if (variant == "w1")
        {
            // A PSD is a visual design input. The source W1 is observed and recorded, prepares
            // like any other artwork, and becomes no part of the managed raster: production W1 is
            // generated by the signed Action immediately before the TIFF is written.
            attempt.PsdInspection!.HasW1.ShouldBeTrue();
            attempt.PsdInspection!.HasSpots.ShouldBeTrue();
            raster.Facts.Format.ShouldBe(ImageFormat.Png);
            raster.Facts.ColourMode.ShouldBe(ColourMode.Rgb);
            output.WriteLine("Source W1 observed as a diagnostic; managed raster is ordinary RGB PNG.");
        }
        output.WriteLine("Adapter: " + attempt.AdapterId + " / " + attempt.AdapterNotes);
        state.ToSnapshot().CurrentStep!.State.ShouldBe(StepState.ReviewRequired);
        PhotoshopReadiness after = await EnsureReadyUnderLeaseAsync(provider, processor, verifyGate: false);
        output.WriteLine("Photoshop final state: " + after.State.State);
        after.State.State.ShouldBe(before.State.State);
        output.WriteLine("Smoke ends at prepared-raster review. No TIFF production was run.");
    }

    private async Task<PhotoshopReadiness> EnsureReadyUnderLeaseAsync(
        ServiceProvider provider,
        ProductionPhotoshopOutputProcessor processor,
        bool verifyGate)
    {
        await using WorkstationAutomationLeaseScope admission =
            await WorkstationAutomationLeaseScope.AcquireAsync(
                provider.GetRequiredService<IWorkstationAutomationLeaseManager>());
        if (verifyGate)
        {
            var gate = ((IWorkstationScopedEnvironmentGate)provider.GetRequiredService<IEnvironmentGate>())
                .Verify(AdapterExecutionMode.Production, admission.Lease);
            output.WriteLine("VerifiedEnvironmentGate: " +
                             (gate.IsSuccess ? "ALLOWED" : gate.Failure.ToString()));
            gate.IsSuccess.ShouldBeTrue(gate.IsFailure ? gate.Failure.ToString() : "");
        }

        var ready = await processor.EnsureReadyAsync(CancellationToken.None);
        ready.IsSuccess.ShouldBeTrue(ready.IsFailure ? ready.Failure.ToString() : "");
        return ready.Value;
    }
}
