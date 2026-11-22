using PrintFlow.Domain.Files;
using PrintFlow.Domain.Ids;
using PrintFlow.Domain.Sessions;
using PrintFlow.Workflow.Engine;

namespace PrintFlow.Workflow.Services;

/// <summary>The image and exact workflow position that a Photoshop edit may update.</summary>
public sealed record PhotoshopBackgroundEditTarget(SessionStep Step, RevisionId SourceRevision, Sha256 SourceHash)
{
    internal static PhotoshopBackgroundEditTarget? From(WorkflowSnapshot snapshot)
    {
        if (snapshot.SessionState != SessionState.Active ||
            snapshot.CurrentStep is not { Step: StepKind.BackgroundRemoval } step)
            return null;

        (RevisionId Id, Sha256 Hash)? source = step.State switch
        {
            StepState.ReviewRequired when step.CurrentRevisionId is { } id && step.CurrentRevisionSha256 is { } hash =>
                (id, hash),
            StepState.Waiting or StepState.RetryRequired => snapshot.UpstreamResultOf(step.Step),
            _ => null,
        };
        if (source is not { } image)
            return null;

        // SQLite stores step timestamps to the millisecond. Use the same precision
        // for the immediate result of a transition and its subsequent reload.
        var persistedStep = step with
        {
            EnteredStateAtUtc = DateTimeOffset.FromUnixTimeMilliseconds(step.EnteredStateAtUtc.ToUnixTimeMilliseconds()),
        };
        return new(persistedStep, image.Id, image.Hash);
    }
}
