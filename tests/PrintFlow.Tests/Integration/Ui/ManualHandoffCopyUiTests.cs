using System.IO;
using PrintFlow.App.Navigation;
using PrintFlow.App.ViewModels;
using PrintFlow.Domain.Files;
using PrintFlow.Domain.Sessions;
using PrintFlow.Infrastructure.Adapters.Fake;
using PrintFlow.Infrastructure.Delivery;
using PrintFlow.Tests.Fixtures;
using PrintFlow.Tests.Integration.Persistence;
using PrintFlow.Workflow.Commands;
using PrintFlow.Workflow.Ports;
using PrintFlow.Workflow.Services;

namespace PrintFlow.Tests.Integration.Ui;

[Collection(SqliteCollection.Name)]
public sealed class ManualHandoffCopyUiTests
{
    [Theory]
    [InlineData(StepKind.Enhancement, false)]
    [InlineData(StepKind.BackgroundRemoval, false)]
    [InlineData(StepKind.Enhancement, true)]
    [InlineData(StepKind.BackgroundRemoval, true)]
    public async Task Manual_handoff_offers_a_copy_of_the_current_preview(StepKind step, bool takeOver)
    {
        using SessionServiceHarness h = new();
        ISessionService workflow = h.CreateService();
        var id = await ManualResultImportTests.AtStep(h, workflow, step);
        if (!takeOver)
        {
            h.FakeMeitu.SetScenario(FakeAdapterScenario.Timeout);
            (await workflow.ExecuteAsync(id, new WorkflowCommand.StartStep(step), "qa", default)).IsFailure.ShouldBeTrue();
        }
        SessionView current = (await workflow.LoadAsync(id, default)).Value;
        var source = (await ManualResultImportTests.Load(h, id)).Revisions.Single(r => r.Id == current.CurrentArtefact!.RevisionId);
        byte[] expected = File.ReadAllBytes(h.FileWorkspace.ResolveAbsolute(source.File));
        string folder = FinalSaveFixtures.NewFolder("handoff-copy");
        try
        {
            string path = Path.Combine(folder, "manual.png");
            var picker = new Picker(path);
            var screen = new SessionViewModel(workflow, h.Previews, h.TiffReviews, new RecordingNavigation(),
                manualHandoffCopy: new ManualHandoffCopyService(h.Repository, h.FileWorkspace,
                    new WindowsDeliveryFileSystem(), FinalSaveFixtures.ProtectedRoots(h)), manualHandoffCopyPicker: picker);
            screen.Open(current);
            await screen.PreviewsLoaded;

            if (takeOver)
            {
                h.FakeMeitu.SetScenario(FakeAdapterScenario.WaitForStopAt(ExternalOperationPhase.Busy));
                Task running = screen.RunStepCommand.ExecuteAsync(null);
                await h.FakeMeitu.HangStarted;
                screen.BeginTakeOverCommand.Execute(null);
                screen.ConfirmTakeOverCommand.Execute(null);
                await running;
            }
            else
            {
                screen.CanHandOff.ShouldBeTrue();
                await screen.HandOffCommand.ExecuteAsync(null);
            }

            screen.IsHandedOff.ShouldBeTrue();
            File.ReadAllBytes(path).ShouldBe(expected);
            File.ReadAllBytes(h.FileWorkspace.ResolveAbsolute(source.File)).ShouldBe(expected);
            (await ManualResultImportTests.Load(h, id)).Reviews.ShouldBeEmpty();

            byte[] existing = [1, 2, 3];
            File.WriteAllBytes(path, existing);
            await screen.SaveManualHandoffCopyCommand.ExecuteAsync(null);
            File.ReadAllBytes(path).ShouldBe(existing);
            Directory.GetFiles(folder).ShouldBe([path]);

            picker.Destination = null;
            await screen.SaveManualHandoffCopyCommand.ExecuteAsync(null);
            screen.IsHandedOff.ShouldBeTrue();
            Directory.GetFiles(folder).ShouldBe([path]);
        }
        finally { FinalSaveFixtures.Remove(folder); }
    }

    private sealed class Picker(string path) : IManualHandoffCopyPicker
    {
        public string? Destination { get; set; } = path;
        public string? PickDestination(string suggestedFileName, ImageFormat format) => Destination;
    }
}
