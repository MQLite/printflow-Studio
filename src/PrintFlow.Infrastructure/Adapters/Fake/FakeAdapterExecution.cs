using System.IO;
using PrintFlow.Domain.Files;
using PrintFlow.Domain.Results;
using PrintFlow.Workflow.Ports;

namespace PrintFlow.Infrastructure.Adapters.Fake;

/// <summary>
/// The scripted-scenario behaviour shared by <see cref="FakeMeituProcessor"/> and
/// <see cref="FakePhotoshopOutputProcessor"/> (Epic 11100 Part 3A §3).
/// </summary>
/// <remarks>
/// Only <see cref="FakeAdapterScenarioKind.Succeed"/> differs meaningfully between the two
/// adapters (Meitu edits its working copy in place; Photoshop copies into a freshly reserved
/// path), so that one case is left to the caller via <paramref name="succeed"/> in
/// <see cref="RunAsync"/>; every scripted failure behaves identically for both.
/// </remarks>
internal static class FakeAdapterExecution
{
    public static async Task<OperationResult<AdapterOutput>> RunAsync(
        FakeAdapterScenario scenario,
        WorkspaceFileRef expectedOutput,
        IWorkspace workspace,
        TaskCompletionSource? hangStarted,
        Func<Task<OperationResult<AdapterOutput>>> succeed,
        IAutomationStopSignal stop,
        CancellationToken cancellationToken)
    {
        switch (scenario.Kind)
        {
            case FakeAdapterScenarioKind.Succeed:
                return await succeed().ConfigureAwait(false);

            case FakeAdapterScenarioKind.FailWith:
                return OperationResult.Fail<AdapterOutput>(
                    scenario.Code!.Value, $"Fake adapter scripted failure: {scenario.Code}.");

            case FakeAdapterScenarioKind.Timeout:
                return OperationResult.Fail<AdapterOutput>(
                    FailureCode.Timeout, "Fake adapter scripted timeout: no completion observed in time.");

            case FakeAdapterScenarioKind.ProduceMissingFile:
            {
                string missingAbsolute = workspace.ResolveAbsolute(expectedOutput);
                if (File.Exists(missingAbsolute))
                {
                    File.Delete(missingAbsolute);
                }

                return OperationResult.Ok(new AdapterOutput(expectedOutput, TimeSpan.Zero, "fake:missing-output"));
            }

            case FakeAdapterScenarioKind.ProduceUnreadableFile:
            {
                string unreadableAbsolute = workspace.ResolveAbsolute(expectedOutput);
                File.WriteAllBytes(unreadableAbsolute, []);
                return OperationResult.Ok(new AdapterOutput(expectedOutput, TimeSpan.Zero, "fake:unreadable-output"));
            }

            case FakeAdapterScenarioKind.HangUntilCancelled:
                hangStarted?.TrySetResult();
                try
                {
                    await Task.Delay(System.Threading.Timeout.Infinite, cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    // Expected: the test cancels the token to end the hang deterministically.
                }

                return OperationResult.Fail<AdapterOutput>(
                    FailureCode.Cancelled, "Fake adapter cancelled while hanging.");

            case FakeAdapterScenarioKind.ReportPhaseAndWaitForStop:
            {
                stop.ReportPhase(scenario.Phase);
                hangStarted?.TrySetResult();

                // Polled rather than awaited on a handle, because the signal is the same object
                // the UI thread writes to and adding a synchronisation primitive to it would put
                // scheduling policy into a workflow port. The wait ends on the caller's token as
                // well, so a test that forgets to stop still terminates.
                while (stop.RequestedMode is null && !cancellationToken.IsCancellationRequested)
                {
                    try
                    {
                        await Task.Delay(TimeSpan.FromMilliseconds(5), cancellationToken)
                            .ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                }

                // No file is written on any path out of here, so the workflow layer's "a stopped
                // attempt produces no Revision" rule is exercised against an adapter that
                // genuinely produced nothing rather than one that produced something ignored.
                return OperationResult.Fail<AdapterOutput>(OperationFailure.Create(
                    FailureCode.Cancelled,
                    stop.RequestedMode is { } mode
                        ? $"Fake adapter stopped by operator request '{mode}' at phase '{scenario.Phase}'. " +
                          "No file was produced. The fake drives no external application, so nothing " +
                          "was cancelled in one."
                        : "Fake adapter stopped without an operator request. No file was produced.",
                    isRetryable: true,
                    context: new Dictionary<string, string>
                    {
                        ["phase"] = scenario.Phase.ToString(),
                        ["meituCancelInvoked"] = "false",
                        ["forceTerminationInvoked"] = "false",
                    }));
            }

            default:
                throw new InvalidOperationException($"Unhandled fake adapter scenario '{scenario.Kind}'.");
        }
    }
}
