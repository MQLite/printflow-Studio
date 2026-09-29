using PrintFlow.App.ViewModels;
using PrintFlow.Domain.Outputs;
using PrintFlow.Domain.Sessions;
using PrintFlow.Tests.Integration.Ui;
using PrintFlow.Workflow.Commands;
using PrintFlow.Workflow.Services;

namespace PrintFlow.Tests.Fixtures;

/// <summary>
/// Opens a session screen on the print-size step (SCRUM-11150) through the real session service,
/// the harness's GUID-owned workspace and database, synthetic images and the existing fakes.
/// Nothing here starts the application, shows a window or drives input.
/// </summary>
internal static class PrintSizeGuidanceSetup
{
    /// <summary>Representative sizing states for rendering: each mode, invalid input and enlargement.</summary>
    public static readonly string[] States = ["preset", "draft", "invalid", "enlargement"];

    public static async Task<SessionViewModel> AtDimensionsAsync(
        HomeScreenHarness harness, int width = 4000, int height = 2000, WorkflowType route = WorkflowType.GeneratePrintTiff,
        Func<SessionViewModel>? create = null)
    {
        string source = harness.Inner.Workspace.CreateSourceFile(
            $"size-{Guid.NewGuid():N}.png", SyntheticImages.Png(width, height, alpha: true));
        SessionView imported = (await harness.Sessions.ImportAsync(
            route, source, "print-size", "tester", CancellationToken.None)).Value;
        SessionView atDimensions = (await harness.Sessions.ExecuteAsync(
            imported.Id, new WorkflowCommand.ConfirmOriginal("finished design"), "tester", CancellationToken.None)).Value;
        atDimensions.CurrentStep!.Step.ShouldBe(StepKind.PrintDimensions);

        SessionViewModel screen = create?.Invoke() ?? harness.Session(new RecordingNavigation());
        screen.Open(atDimensions);
        await screen.PreviewsLoaded;
        return screen;
    }

    /// <summary>Opens one of <see cref="States"/>, settled and ready to render.</summary>
    public static async Task<SessionViewModel> OpenStateAsync(HomeScreenHarness harness, string state)
    {
        switch (state)
        {
            case "preset":
            {
                SessionViewModel screen = await AtDimensionsAsync(harness);
                await screen.UsePresetCommand.ExecuteAsync(
                    screen.SizePresets.Single(choice => choice.Preset == SizePreset.A3Landscape));
                await screen.PreviewsLoaded;
                return screen;
            }

            case "draft":
            {
                SessionViewModel screen = await AtDimensionsAsync(harness);
                screen.ChooseCustomSizeCommand.Execute(null);
                screen.SelectedTargetEdgeChoice = screen.TargetEdgeChoices.Single(choice => choice.Edge == TargetEdge.Width);
                screen.CustomMillimetresText = "250";
                await screen.PreflightLoaded;
                return screen;
            }

            case "invalid":
            {
                SessionViewModel screen = await AtDimensionsAsync(harness);
                screen.ChooseCustomSizeCommand.Execute(null);
                screen.SelectedTargetEdgeChoice = screen.TargetEdgeChoices.Single(choice => choice.Edge == TargetEdge.Width);
                screen.CustomMillimetresText = "abc";
                await screen.PreflightLoaded;
                return screen;
            }

            case "enlargement":
            {
                SessionViewModel screen = await AtDimensionsAsync(harness, 40, 20);
                screen.ChooseCustomSizeCommand.Execute(null);
                screen.SelectedTargetEdgeChoice = screen.TargetEdgeChoices.Single(choice => choice.Edge == TargetEdge.LongEdge);
                screen.CustomMillimetresText = "100";
                await screen.PreflightLoaded;
                await screen.ConfirmCustomSizeCommand.ExecuteAsync(null);
                screen.Notice.ShouldBeNull();
                await screen.PreviewsLoaded;
                return screen;
            }

            default:
                throw new ArgumentOutOfRangeException(nameof(state), state, null);
        }
    }
}
