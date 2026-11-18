using PrintFlow.Domain.Attempts;
using PrintFlow.Domain.Files;
using PrintFlow.Domain.Results;
using PrintFlow.Domain.Revisions;
using PrintFlow.Domain.Sessions;
using PrintFlow.Domain.Settings;
using PrintFlow.Workflow.Commands;
using PrintFlow.Workflow.Engine;
using PrintFlow.Workflow.Ports;

namespace PrintFlow.Workflow.Services;

public sealed record ImageProcessingExecutionView(
    ImageProcessingMethod Method, string? AdapterId, string? AlgorithmVersion,
    bool DrivesExternalApplication, OperationFailure? Unavailable);

public sealed partial class SessionService
{
    private readonly IReadOnlyList<IImageProcessingBackend> _imageProcessingBackends;
    private readonly SemaphoreSlim _imageProcessingSlots = new(2);

    private sealed record ImageProcessingExecutionPlan(
        ImageProcessingExecutionView View, IImageProcessingBackend? Backend = null);

    private ImageProcessingExecutionPlan? PlanImageProcessing(WorkflowSnapshot state, StepKind step) =>
        state.ProcessingMethodFor(step) is { } method ? PlanImageProcessing(method) : null;

    private ImageProcessingExecutionPlan PlanImageProcessing(ImageProcessingMethod method)
    {
        if (method is ImageProcessingMethod.MeituEnhancement or ImageProcessingMethod.MeituBackgroundRemoval)
            return new(new(method, _meitu.AdapterId, null, true, null));
        if (method == ImageProcessingMethod.AlphaTrim)
            return new(new(method, _trim.ProcessorId, _trim.ProcessorId, false, null));
        if (method == ImageProcessingMethod.ManualCrop)
            return new(new(method, _manualCrop.ProcessorId, _manualCrop.ProcessorId, false, null));

        if (method is ImageProcessingMethod.OpenAiRestore or ImageProcessingMethod.OpenAiCutout or ImageProcessingMethod.OpenAiCombined)
            return new(new(method, null, null, false, OperationFailure.Create(FailureCode.PreconditionNotMet,
                "A cloud image operation requires explicit upload authority for this exact input and operation.",
                messageKey: "ImageProcessing_MethodUnavailable")));

        if (method is ImageProcessingMethod.LightSharpen or ImageProcessingMethod.AiUpscale or ImageProcessingMethod.BiRefNet)
        {
            foreach (IImageProcessingBackend backend in _imageProcessingBackends)
            {
                if (backend.GetCapability(method) is { } capability)
                    return new(new(method, capability.AdapterId, capability.AlgorithmVersion, false, capability.Unavailable), backend);
            }
        }

        return new(new(method, null, null, false, OperationFailure.Create(FailureCode.AdapterUnavailable,
            $"There is no automatic processor for the selected method {method}.",
            messageKey: "ImageProcessing_MethodUnavailable")));
    }

    public OperationFailure? GetImageProcessingUnavailable(ImageProcessingMethod method) => method switch
    {
        ImageProcessingMethod.MeituEnhancement => _meitu.GetConfigurationFailure(MeituOperation.Enhance),
        ImageProcessingMethod.MeituBackgroundRemoval => _meitu.GetConfigurationFailure(MeituOperation.RemoveBackground),
        ImageProcessingMethod.SkipEnhancement or ImageProcessingMethod.KeepBackground or ImageProcessingMethod.KeepCanvas => null,
        _ => PlanImageProcessing(method).View.Unavailable,
    };

    private ImageProcessingExecutionView? ImageProcessingViewOf(
        WorkflowSnapshot state, IReadOnlyList<ProcessingAttempt> attempts)
    {
        if (state.CurrentStep is not { } current)
            return null;
        var attempt = current.State switch
        {
            StepState.Processing => attempts.LastOrDefault(a => a.Step == current.Step && a.Status == AttemptStatus.Running),
            StepState.ReviewRequired => attempts.LastOrDefault(a => current.CurrentRevisionId is { } output &&
                a.Step == current.Step && a.OutputRevisionId == output),
            _ => null,
        };
        if (attempt is not null)
        {
            ImageProcessingMethod? actual = attempt.ImageProcessingDetails?.Method
                ?? ManualImageProcessingMethod(attempt.Step, attempt.Operation);
            if (actual is { } recorded)
                return new(recorded, attempt.AdapterId, attempt.ImageProcessingDetails?.AlgorithmVersion,
                    recorded is ImageProcessingMethod.MeituEnhancement or ImageProcessingMethod.MeituBackgroundRemoval, null);
        }
        if (state.ProcessingMethodFor(current.Step) is not { } method)
            return null;
        if (method == ImageProcessingMethod.KeepCanvas)
            return new(method, null, null, false, null);
        ImageProcessingExecutionView view = PlanImageProcessing(method).View;
        return method is ImageProcessingMethod.MeituEnhancement or ImageProcessingMethod.MeituBackgroundRemoval
            or ImageProcessingMethod.SkipEnhancement or ImageProcessingMethod.KeepBackground
            ? view with { Unavailable = GetImageProcessingUnavailable(method) }
            : view;
    }

    private static ImageProcessingMethod? ManualImageProcessingMethod(StepKind step, OperationKind operation) => operation switch
    {
        OperationKind.ManualImport when step == StepKind.Trim => ImageProcessingMethod.ManualCrop,
        OperationKind.ManualResultImport => ImageProcessingMethod.ManualResult,
        _ => null,
    };

    private async Task<OperationResult<StepWork>> RunImageBackendAsync(
        ImageProcessingExecutionPlan plan, ProducingWork work, ProcessingAttempt attempt,
        Revision source, WorkspaceFileRef workingCopy, Action<WorkspaceFileRef> establishExpectedOutput,
        CancellationToken cancellationToken)
    {
        string outputName = workingCopy.FileName.Equals("processed.png", StringComparison.OrdinalIgnoreCase)
            ? "processed-result.png" : "processed.png";
        WorkspaceFileRef output = SiblingOf(workingCopy, outputName);
        establishExpectedOutput(output);
        var request = new ImageProcessingRequest(plan.View.Method, work.Operation, plan.View.AlgorithmVersion!, source.Id, source.Sha256,
            workingCopy, output, attempt.BackgroundRemovalAuthority);
        var result = await plan.Backend!.ProcessAsync(request, cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        if (result.IsFailure)
            return OperationResult.Fail<StepWork>(result.Failure);
        if (result.Value.ProducedFile != output)
            return OperationResult.Fail<StepWork>(FailureCode.OutputValidationFailed,
                "Image processing returned a file other than the assigned output.");

        var verifiedInput = await VerifyImageProcessingInputAsync(source, workingCopy, cancellationToken).ConfigureAwait(false);
        if (verifiedInput.IsFailure)
            return OperationResult.Fail<StepWork>(verifiedInput.Failure);

        var inspected = await _fileInspector.InspectPngOutputAsync(
            _workspace.ResolveAbsolute(output), cancellationToken).ConfigureAwait(false);
        if (inspected.IsFailure || inspected.Value.Format != ImageFormat.Png || !inspected.Value.HasPixelDimensions)
            return OperationResult.Fail<StepWork>(FailureCode.OutputValidationFailed,
                "The assigned image processing output is not a readable PNG.");
        return OperationResult.Ok(new StepWork(output, inspected.Value, result.Value.AdapterNotes));
    }

    private async Task<OperationResult<Sha256>> VerifyImageProcessingInputAsync(
        Revision source, WorkspaceFileRef workingCopy, CancellationToken cancellationToken)
    {
        var copy = await InspectAsync(workingCopy, cancellationToken).ConfigureAwait(false);
        if (copy.IsFailure || copy.Value.Facts.Sha256 != source.Sha256)
            return OperationResult.Fail<Sha256>(FailureCode.RevisionIntegrityMismatch,
                "The working copy differs from the verified image processing input.");
        return await _integrityGuard.VerifyAsync(source, cancellationToken).ConfigureAwait(false);
    }

    private async Task<OperationResult<SessionView>> CloseConflictingImageAttemptAsync(
        ProducingWork work, ProcessingAttempt attempt, CommandContext context, OperationFailure conflict)
    {
        if (await ReloadRunningImageAttemptAsync(attempt).ConfigureAwait(false) is { } latest)
        {
            return await FailAttemptAsync(latest, latest.ToSnapshot(ConfiguredRecommendations()), context,
                work.Step, attempt, conflict, CancellationToken.None,
                imageProcessing: true, usesImageBackend: work.ImageProcessing?.Backend is not null,
                retryConflict: false).ConfigureAwait(false);
        }
        return OperationResult.Fail<SessionView>(conflict);
    }

    private async Task<SessionAggregate?> ReloadRunningImageAttemptAsync(ProcessingAttempt attempt)
    {
        var loaded = await _repository.LoadAsync(attempt.SessionId, CancellationToken.None).ConfigureAwait(false);
        return loaded.IsSuccess && loaded.Value is { } latest &&
            latest.Steps.Any(step => step.Step == attempt.Step && step.State == StepState.Processing) &&
            latest.Attempts.Any(recorded => recorded.Id == attempt.Id && recorded.Status == AttemptStatus.Running)
            ? latest : null;
    }
}
