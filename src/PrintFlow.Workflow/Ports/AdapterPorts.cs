using PrintFlow.Domain.Files;
using PrintFlow.Domain.Outputs;
using PrintFlow.Domain.Results;

namespace PrintFlow.Workflow.Ports;

/// <summary>The Meitu operations PrintFlow drives.</summary>
public enum MeituOperation
{
    Enhance,
    RemoveBackground,
}

/// <summary>A validated file an adapter produced.</summary>
public sealed record AdapterOutput(WorkspaceFileRef ProducedFile, TimeSpan Elapsed, string? AdapterNotes);

/// <summary>What the Meitu adapter is asked to do.</summary>
public sealed record MeituRequest(
    WorkspaceFileRef Input,
    MeituOperation Operation,
    WorkspaceDirRef WorkingDirectory,
    WorkspaceFileRef ExpectedOutput);

/// <summary>What the Photoshop adapter is asked to do.</summary>
/// <remarks>
/// <see cref="Branch"/> is non-nullable on purpose: there is no way to ask for a production
/// TIFF without having made the white-underbase decision (MVP design §12).
/// </remarks>
public sealed record PhotoshopRequest(
    WorkspaceFileRef ApprovedInput,
    PrintDimensions Dimensions,
    ProductionPresetRef Preset,
    WhiteUnderbaseBranch Branch,
    string OutputFileName,
    WorkspaceDirRef WorkingDirectory,
    WorkspaceFileRef ExpectedOutput);

/// <summary>
/// Enhancement and background removal (MVP design §16.2).
/// </summary>
/// <remarks>
/// The seam deliberately exposes no window handle, coordinate, keystroke, flow name, dialog
/// title, timeout or retry policy. When Meitu changes version, or the automation technology
/// is replaced entirely, this signature does not move — which is the whole reason the seam
/// exists.
///
/// No implementation exists in Epic 11100 Part 1. Deterministic fakes arrive later in
/// Epic 11100; real screen automation is Epic 11300.
/// </remarks>
public interface IMeituProcessor
{
    /// <summary>Identifies the implementation; written to every attempt so a fake result is never mistaken for a production one.</summary>
    string AdapterId { get; }

    Task<OperationResult<AdapterOutput>> ProcessAsync(MeituRequest request, CancellationToken cancellationToken);
}

/// <summary>
/// Production TIFF generation (MVP design §16.2).
/// </summary>
/// <remarks>
/// No implementation exists in Epic 11100 Part 1. Real Photoshop automation is Epic 11400.
/// Nothing in this Epic launches, focuses, reads or scripts Photoshop.
/// </remarks>
public interface IPhotoshopOutputProcessor
{
    string AdapterId { get; }

    Task<OperationResult<AdapterOutput>> GenerateAsync(PhotoshopRequest request, CancellationToken cancellationToken);
}
