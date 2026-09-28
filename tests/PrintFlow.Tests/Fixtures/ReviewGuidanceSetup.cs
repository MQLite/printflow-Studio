using PrintFlow.App.Localisation;
using PrintFlow.App.Navigation;
using PrintFlow.App.ViewModels;
using PrintFlow.Domain.Ids;
using PrintFlow.Domain.Sessions;
using PrintFlow.Tests.Integration.Persistence;
using PrintFlow.Tests.Integration.Ui;
using PrintFlow.Workflow.Commands;
using PrintFlow.Workflow.Delivery;
using PrintFlow.Workflow.Services;

namespace PrintFlow.Tests.Fixtures;

/// <summary>
/// Opens a session screen at each of the four reviewed steps (SCRUM-11149) through the real session
/// service, the harness's GUID-owned workspace and database, and the existing fakes. The screen is
/// composed the way the application composes it where it matters: colleague correction on the
/// background-removal review, and the final-save section on the two final reviews.
/// </summary>
internal static class ReviewGuidanceSetup
{
    public static readonly string[] Steps = ["enhancement", "background", "trim", "tiff"];

    internal sealed record Opened(ISessionService Service, SessionViewModel Screen, SessionView Review)
    {
        /// <summary>The file picker the screen uses; a test sets its next answer.</summary>
        public ColleagueCorrectionUiTests.RecordingPicker Picker { get; init; } = null!;
    }

    public static async Task<Opened> OpenAtAsync(SessionServiceHarness h, string step, ILocalisationService? localisation = null)
    {
        (ISessionService service, SessionView review) = step switch
        {
            "enhancement" => await EnhancementAsync(h),
            "background" => await BackgroundAsync(h),
            "background-no-colleague" => await BackgroundWithoutCorrectionAsync(h),
            "trim" => await TrimAsync(h),
            "tiff" => await TiffAsync(h),
            _ => throw new ArgumentOutOfRangeException(nameof(step), step, null),
        };
        review.CurrentStep!.State.ShouldBe(StepState.ReviewRequired);

        ColleagueCorrectionUiTests.RecordingPicker picker = new();
        SessionViewModel screen = new(service, h.Previews, h.TiffReviews, new RecordingNavigation(),
            picker, localisation,
            new FinalSaveCoordinator(service, FinalSaveFixtures.Delivery(h), "tester"), new NoFolderPicker(),
            new RecordingDeliveredFileShell(), new ColleagueCorrectionUiTests.RecordingCorrectionShell());
        screen.Open(review);
        await screen.PreviewsLoaded;
        await screen.FinalSaveFactsLoaded;
        return new Opened(service, screen, review) { Picker = picker };
    }

    private static async Task<(ISessionService, SessionView)> EnhancementAsync(SessionServiceHarness h)
    {
        ISessionService service = h.CreateService();
        SessionId id = (await service.ImportAsync(WorkflowType.PrepareAsset, h.WriteSourcePng("enhance.png"), "enhance", "tester",
            CancellationToken.None)).Value.Id;
        await CorrectionFixtures.MustAsync(service.ExecuteAsync(id, new WorkflowCommand.ConfirmOriginal(), "tester", CancellationToken.None));
        SessionView review = await CorrectionFixtures.MustAsync(service.ExecuteAsync(
            id, new WorkflowCommand.StartStep(StepKind.Enhancement), "tester", CancellationToken.None));
        return (service, review);
    }

    private static async Task<(ISessionService, SessionView)> BackgroundAsync(SessionServiceHarness h)
    {
        SessionService service = CorrectionFixtures.Service(h);
        return (service, await CorrectionFixtures.AtBackgroundRemovalReviewAsync(h, service, opaqueSource: true));
    }

    /// <summary>A service composed without the correction package store: the Ask entry does not exist.</summary>
    private static async Task<(ISessionService, SessionView)> BackgroundWithoutCorrectionAsync(SessionServiceHarness h)
    {
        ISessionService service = h.CreateService();
        return (service, await CorrectionFixtures.AtBackgroundRemovalReviewAsync(h, service, opaqueSource: true));
    }

    private static async Task<(ISessionService, SessionView)> TrimAsync(SessionServiceHarness h)
    {
        ISessionService service = h.CreateService();
        (_, SessionView review) = await TrimAdjustmentStepTests.AtTrimReviewAsync(h, service);
        review.TrimAdjustment.ShouldNotBeNull();
        return (service, review);
    }

    private static async Task<(ISessionService, SessionView)> TiffAsync(SessionServiceHarness h)
    {
        ISessionService service = FinalSaveFixtures.TiffService(h);
        return (service, await FinalSaveFixtures.TiffAtFinalReviewAsync(h, service, outputName: "guidance"));
    }

    /// <summary>Never shows a dialog and never chooses a folder.</summary>
    private sealed class NoFolderPicker : IDeliveryFolderPicker
    {
        public string? PickFolder(string title, string? initialFolder) => null;
    }
}
