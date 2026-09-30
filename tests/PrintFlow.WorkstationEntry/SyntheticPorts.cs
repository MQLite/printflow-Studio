using PrintFlow.Domain.Results;
using PrintFlow.Workflow.Ports;

namespace PrintFlow.WorkstationEntry;

public sealed class SyntheticEnvironment : IEnvironmentGate, IEnvironmentDiagnostics
{
    public OperationResult<Unit> Verify(AdapterExecutionMode mode) => mode == AdapterExecutionMode.Fake
        ? OperationResult.Ok() : OperationResult.Fail<Unit>(FailureCode.EnvironmentNotVerified, "SYNTHETIC ENTRY: Production is always denied.");
    public EnvironmentReadinessReport Read() => new(false, "SYNTHETIC — NOT A WORKSTATION VERIFICATION", DateTimeOffset.UtcNow,
        [new("SyntheticEntry", EnvironmentCheckStatus.Blocked, true, "Environment_NotVerified", "SYNTHETIC: no workstation or external application was inspected.")]);
    public Task<EnvironmentReadinessReport> RunLiveChecksAsync(CancellationToken cancellationToken)
        => Task.FromResult(Read());
}

public sealed class RefusingPdf : IPdfPreparationProcessor
{
    public string AdapterId => "isolated-pdf-refused";
    public AdapterExecutionMode Mode => AdapterExecutionMode.Fake;
    public Task<OperationResult<AdapterOutput>> PrepareAsync(PdfPreparationRequest request, CancellationToken cancellationToken)
        => Task.FromResult(OperationResult.Fail<AdapterOutput>(FailureCode.PdfPreparationFailed, "This synthetic entry admits PNG/TIFF only."));
}

public sealed class ContainedRecycle(OwnedPaths paths) : IRecycleBin
{
    public OperationResult<Unit> SendToRecycleBin(string absolutePath)
    {
        try
        {
            paths.Require(absolutePath, "workspace");
            using (NativePathLease check = paths.Read(absolutePath, "workspace")) { }
            string target = paths.At("recycle", Guid.NewGuid().ToString("N") + "-" + Path.GetFileName(absolutePath));
            paths.Require(target, "recycle");
            File.Move(absolutePath, target, overwrite: false);
            return OperationResult.Ok();
        }
        catch (IOException ex) { return OperationResult.Fail<Unit>(FailureCode.WorkspaceError, ex.Message); }
    }
}
