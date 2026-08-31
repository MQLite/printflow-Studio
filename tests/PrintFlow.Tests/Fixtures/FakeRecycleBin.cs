using System.IO;
using PrintFlow.Domain.Results;
using PrintFlow.Workflow.Ports;

namespace PrintFlow.Tests.Fixtures;

/// <summary>
/// A stand-in for the Windows Recycle Bin that records every call and can be made to fail
/// (Epic 11400 Part C2B §30, §31).
/// </summary>
/// <remarks>
/// Doubled rather than real for two reasons. A test suite that really recycled would fill the
/// developer's and the build agent's Recycle Bin with hundreds of temp files, and — more
/// importantly — "the Recycle Bin was invoked exactly once, with exactly this path" is the
/// property C2B has to prove, and only a recording double can state it. That the real
/// <see cref="PrintFlow.Infrastructure.Workspace.RecycleBin"/> genuinely recycles, and genuinely
/// has no hard-delete fallback, is proven separately and directly in <c>WorkspaceTests</c>.
/// <para>
/// The success path moves the file into a holding directory instead of deleting it, which is what
/// the real Recycle Bin does: the bytes leave the workspace but remain recoverable. A test can
/// therefore assert both that the TIFF is gone from <c>Working\</c> and that nothing hard-deleted
/// it.
/// </para>
/// </remarks>
internal sealed class FakeRecycleBin : IRecycleBin
{
    private readonly string _holdingDirectory;
    private readonly List<string> _recycled = [];

    public FakeRecycleBin(string holdingDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(holdingDirectory);
        _holdingDirectory = holdingDirectory;
    }

    /// <summary>When set, every call fails with this code and the file is left untouched.</summary>
    public FailureCode? FailsWith { get; set; }

    /// <summary>Every absolute path this bin was asked to recycle, in call order.</summary>
    public IReadOnlyList<string> Recycled => _recycled;

    /// <summary>Where a successfully recycled file was moved to, keyed by its original path.</summary>
    public Dictionary<string, string> Holding { get; } = new(StringComparer.OrdinalIgnoreCase);

    public OperationResult<PrintFlow.Domain.Results.Unit> SendToRecycleBin(string absolutePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(absolutePath);
        _recycled.Add(absolutePath);

        if (FailsWith is FailureCode code)
        {
            return OperationResult.Fail<PrintFlow.Domain.Results.Unit>(
                code, $"Simulated Recycle Bin failure for '{absolutePath}'. The file was not touched.");
        }

        if (!File.Exists(absolutePath))
        {
            return OperationResult.Fail<PrintFlow.Domain.Results.Unit>(
                FailureCode.OutputMissing, $"Nothing to recycle at '{absolutePath}'.");
        }

        Directory.CreateDirectory(_holdingDirectory);
        string held = Path.Combine(_holdingDirectory, $"{Guid.NewGuid():N}_{Path.GetFileName(absolutePath)}");
        File.Move(absolutePath, held);
        Holding[absolutePath] = held;

        return OperationResult.Ok();
    }
}
