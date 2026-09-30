using PrintFlow.Domain.Results;
using PrintFlow.Infrastructure.Adapters.Fake;
using PrintFlow.Infrastructure.Imaging;
using PrintFlow.Workflow.Ports;

namespace PrintFlow.WorkstationEntry;

public sealed class ContainedTrim(OwnedPaths paths, IWorkspace workspace) : ITrimProcessor
{
    private readonly DeterministicAlphaTrimProcessor inner = new(workspace);
    public string ProcessorId => inner.ProcessorId;
    public async Task<OperationResult<TrimResult>> TrimAsync(TrimRequest request, CancellationToken cancellationToken)
    {
        paths.CheckWritable(workspace.ResolveAbsolute(request.ExpectedOutput), "workspace");
        using NativePathLease source = paths.Read(workspace.ResolveAbsolute(request.Input), "workspace");
        return await inner.TrimAsync(request, cancellationToken);
    }
}
public sealed class ContainedCrop(OwnedPaths paths, IWorkspace workspace) : IManualCropProcessor
{
    private readonly WicManualCropProcessor inner = new(workspace);
    public string ProcessorId => inner.ProcessorId;
    public async Task<OperationResult<ManualCropResult>> CropAsync(ManualCropRequest request, CancellationToken cancellationToken)
    {
        paths.CheckWritable(workspace.ResolveAbsolute(request.ExpectedOutput), "workspace");
        using NativePathLease source = paths.Read(workspace.ResolveAbsolute(request.Input), "workspace");
        return await inner.CropAsync(request, cancellationToken);
    }
}
public sealed class ContainedMeitu(OwnedPaths paths, IWorkspace workspace, FakeMeituProcessor inner) : IMeituProcessor
{
    public string AdapterId => inner.AdapterId;
    public AdapterExecutionMode Mode => AdapterExecutionMode.Fake;
    public async Task<OperationResult<AdapterOutput>> ProcessAsync(MeituRequest request, CancellationToken cancellationToken)
    {
        paths.CheckWritable(workspace.ResolveAbsolute(request.ExpectedOutput), "workspace");
        using NativePathLease source = paths.Read(workspace.ResolveAbsolute(request.Input), "workspace");
        return await inner.ProcessAsync(request, cancellationToken);
    }
}
public sealed class ContainedPhotoshop(OwnedPaths paths, IWorkspace workspace, FakePhotoshopOutputProcessor inner) : IPhotoshopOutputProcessor
{
    public string AdapterId => inner.AdapterId;
    public AdapterExecutionMode Mode => AdapterExecutionMode.Fake;
    public async Task<OperationResult<AdapterOutput>> GenerateAsync(PhotoshopRequest request, CancellationToken cancellationToken)
    {
        paths.CheckWritable(workspace.ResolveAbsolute(request.ExpectedOutput), "workspace");
        using NativePathLease source = paths.Read(workspace.ResolveAbsolute(request.ApprovedInput), "workspace");
        return await inner.GenerateAsync(request, cancellationToken);
    }
}
