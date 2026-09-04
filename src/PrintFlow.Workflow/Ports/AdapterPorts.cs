using PrintFlow.Domain.Files;
using PrintFlow.Domain.Outputs;
using PrintFlow.Domain.Results;
using PrintFlow.Domain.Sessions;

namespace PrintFlow.Workflow.Ports;

/// <summary>The Meitu operations PrintFlow drives.</summary>
public enum MeituOperation
{
    Enhance,
    RemoveBackground,
}

/// <summary>A validated file an adapter produced.</summary>
public sealed record AdapterOutput(WorkspaceFileRef ProducedFile, TimeSpan Elapsed, string? AdapterNotes)
{
    public PsdInspection? PsdInspection { get; init; }
}

public sealed record PsdPreparationRequest(WorkspaceFileRef Input, WorkspaceFileRef ExpectedOutput)
{
    public IAutomationStopSignal Stop { get; init; } = InertAutomationStopSignal.Instance;
}

/// <summary>What the Meitu adapter is asked to do.</summary>
public sealed record MeituRequest(
    WorkspaceFileRef Input,
    MeituOperation Operation,
    BackgroundRemovalDecision BackgroundRemovalDecision,
    WorkspaceDirRef WorkingDirectory,
    WorkspaceFileRef ExpectedOutput)
{
    /// <summary>
    /// The channel through which the operator's Stop or Take Over reaches this run, and through
    /// which the run reports how far it has got (Epic 11300 Part D2A §4, §9).
    /// </summary>
    /// <remarks>
    /// An <c>init</c> property with a null-object default rather than a positional parameter,
    /// so every existing construction site keeps meaning what it meant: a request with no stop
    /// signal is an unattended one, and the adapter reads
    /// <see cref="InertAutomationStopSignal"/> unconditionally rather than null-checking.
    /// <para>
    /// Deliberately not the <c>CancellationToken</c> the call already carries. A cancelled token
    /// means "produce no more input", which is the wrong instruction for the single phase where
    /// stopping requires one further exactly-signed input; and a token cannot say whether the
    /// operator asked to cancel Meitu's work or to take Meitu over, which is the difference that
    /// decides whether that input is permitted at all (§9, §19, §27).
    /// </para>
    /// </remarks>
    public IAutomationStopSignal Stop { get; init; } = InertAutomationStopSignal.Instance;
}

/// <summary>What the Photoshop adapter is asked to do.</summary>
/// <remarks>
/// <see cref="Branch"/> is non-nullable on purpose: there is no way to ask for a production
/// TIFF without having made the white-underbase decision (MVP design §12).
/// <para>
/// <see cref="Preparation"/> is non-nullable for exactly the same reason, and it is the
/// <b>executable</b> geometry authority (Epic 11400 Part B1A.2A §17; Part B1A.2D §25). It says
/// which single edge Photoshop may be given, in millimetres, at the fixed 300 ppi, with a
/// neutral resampling policy the adapter maps to <c>ResampleMethod.NONE</c>,
/// <c>ResampleMethod.BICUBICSHARPER</c> or <c>ResampleMethod.PRESERVEDETAILS</c> — and it is
/// bound to the exact Revision and hash of <see cref="ApprovedInput"/>, so the plan and the
/// pixels cannot describe different files.
/// </para>
/// <para>
/// It is the closed <see cref="PhotoshopPreparation"/> union, carrying the <b>immutable attempt
/// snapshot</b> rather than a reference to anything the session may still change. So the adapter
/// receives a fully resolved operation and decides none of it: not whether an override was
/// allowed, not whether an enlargement was authorised, and not which preset recommendation
/// applies. An unauthorised enlargement is not a case for it to detect — the value cannot be
/// constructed (§25).
/// </para>
/// <para>
/// <see cref="Dimensions"/> is retained for display, naming and audit compatibility — the
/// output file name is built from its millimetres, and a <c>PrintOutput</c> records them. Its
/// <c>PixelWidth</c> and <c>PixelHeight</c> are the independent millimetre conversions the
/// pre-existing model carries, and they are <b>not</b> a Photoshop target pair: sending both
/// would be the non-proportional stretch the accepted contract prohibits. Production code takes
/// its geometry from <see cref="Preparation"/> and nowhere else.
/// </para>
/// </remarks>
public sealed record PhotoshopRequest(
    WorkspaceFileRef ApprovedInput,
    PrintDimensions Dimensions,
    PhotoshopPreparation Preparation,
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

    /// <summary>Whether this is a deterministic local double or real automation; consulted by <see cref="IEnvironmentGate"/>.</summary>
    AdapterExecutionMode Mode { get; }

    Task<OperationResult<AdapterOutput>> ProcessAsync(MeituRequest request, CancellationToken cancellationToken);
}

/// <summary>
/// Photoshop input preparation and production TIFF generation (MVP design §16.2).
/// </summary>
/// <remarks>
/// Both operations use the same execution mode and workstation trust boundary. Preparation
/// carries no production-sizing or white-ink decision and cannot create a production TIFF.
/// </remarks>
public interface IPhotoshopOutputProcessor
{
    string PsdPreparationAdapterId => AdapterId + "/psd-preparation-v1";

    Task<OperationResult<AdapterOutput>> PreparePsdAsync(
        PsdPreparationRequest request, CancellationToken cancellationToken) =>
        Task.FromResult(OperationResult.Fail<AdapterOutput>(FailureCode.PsdUnsupported,
            "PSD preparation is unavailable in this adapter. No raster was produced."));

    string AdapterId { get; }

    /// <summary>Whether this is a deterministic local double or real automation; consulted by <see cref="IEnvironmentGate"/>.</summary>
    AdapterExecutionMode Mode { get; }

    Task<OperationResult<AdapterOutput>> GenerateAsync(PhotoshopRequest request, CancellationToken cancellationToken);
}
