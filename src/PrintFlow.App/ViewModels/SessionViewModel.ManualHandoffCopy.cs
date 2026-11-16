using System.Globalization;
using CommunityToolkit.Mvvm.Input;
using PrintFlow.App.Navigation;
using PrintFlow.App.Resources;
using PrintFlow.Domain.Files;
using PrintFlow.Domain.Sessions;
using PrintFlow.Workflow.Delivery;
using PrintFlow.Workflow.Services;

namespace PrintFlow.App.ViewModels;

public sealed partial class SessionViewModel
{
    private readonly ManualHandoffCopyService? _manualHandoffCopy;
    private readonly IManualHandoffCopyPicker? _manualHandoffCopyPicker;
    private StepKind? _manualHandoffCopyAfterRun;

    public string SaveManualHandoffCopyLabel => Strings.Resolve("Session_SaveManualHandoffCopy");
    public bool ShowsManualHandoffCopy => _manualHandoffCopy is not null && _manualHandoffCopyPicker is not null &&
        GuidedPage == GuidedSessionPage.Recovery && _session is
        {
            CurrentStep.Step: StepKind.BackgroundRemoval or StepKind.Enhancement,
            CurrentArtefact.Facts.Format: ImageFormat.Png or ImageFormat.Jpeg or ImageFormat.Tiff,
        };
    public bool CanSaveManualHandoffCopy => ShowsManualHandoffCopy && !IsBusy && CanInteractWithLivePage;

    [RelayCommand]
    private async Task SaveManualHandoffCopyAsync(CancellationToken cancellationToken)
    {
        if (!CanSaveManualHandoffCopy || _session is not { CurrentStep: { } step, CurrentArtefact: { } image } session)
            return;

        long generation = _openGeneration;
        bool IsCurrent() => generation == _openGeneration && ReferenceEquals(_session, session);
        string? path = _manualHandoffCopyPicker!.PickDestination(image.FileName, image.Facts.Format);
        if (string.IsNullOrWhiteSpace(path) || !IsCurrent() || !CanSaveManualHandoffCopy)
            return;

        _serviceOperationGeneration = generation;
        IsBusy = true;
        try
        {
            var result = await Task.Run(() => _manualHandoffCopy!.SaveAsync(session.Id, step.Step,
                image.RevisionId, path, cancellationToken)).ConfigureAwait(true);
            if (!IsCurrent()) return;
            Notice = result.Code switch
            {
                DeliveryCode.Delivered => string.Format(CultureInfo.CurrentCulture,
                    Strings.Resolve("Session_ManualHandoffCopySaved"), result.Value),
                DeliveryCode.Collision => Strings.Resolve("Session_ManualHandoffCopyCollision"),
                DeliveryCode.InvalidName => Strings.Resolve("Session_ManualHandoffCopyInvalidName"),
                DeliveryCode.UnsupportedDestination or DeliveryCode.ProtectedDestination =>
                    Strings.Resolve("Session_ManualHandoffCopyUnsupportedFolder"),
                DeliveryCode.NeedsReconciliation => Strings.Resolve("Session_ManualHandoffCopyUnconfirmed"),
                DeliveryCode.Cancelled => Notice,
                _ => Strings.Resolve("Session_ManualHandoffCopyFailed"),
            };
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        finally
        {
            if (generation == _openGeneration && _serviceOperationGeneration == generation)
            {
                _serviceOperationGeneration = null;
                IsBusy = false;
            }
        }
    }
}
