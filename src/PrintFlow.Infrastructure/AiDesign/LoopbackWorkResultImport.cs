using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using PrintFlow.Domain.AiDesign;
using PrintFlow.Domain.Files;
using PrintFlow.Domain.Results;
using PrintFlow.Infrastructure.AiDesign.Authoring;
using PrintFlow.Infrastructure.Imaging;
using PrintFlow.Infrastructure.Workspace;
using PrintFlow.Workflow.AiDesign;
using PrintFlow.Workflow.Ports;
using PrintFlow.Workflow.Services;

namespace PrintFlow.Infrastructure.AiDesign;

public sealed partial class LoopbackWorkResultImport(string workspaceRoot, IFileInspector inspector, ISessionService sessions,
    IWorkResultProgressObserver? progress = null)
    : IAiDesignWorkResultImport
{
    private const int MaxImageBytes = 40 * 1024 * 1024;
    private const int MaxMetadataBytes = 16 * 1024;
    private readonly FileAiDesignRequestStore _requests = new(workspaceRoot);
    private readonly IWorkResultPipe _pipe = new WorkResultPipe();

    internal LoopbackWorkResultImport(string workspaceRoot, IFileInspector inspector, ISessionService sessions,
        IWorkResultPipe pipe, IWorkResultProgressObserver? progress = null) : this(workspaceRoot, inspector, sessions, progress) => _pipe = pipe;

    public async Task<OperationResult<SessionView>> ResumeAsync(string configurationPath, CancellationToken cancellationToken)
    {
        try
        {
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            deadline.CancelAfter(TimeSpan.FromSeconds(5));
            var options = await ReadLocalAsync<Options>(configurationPath, deadline.Token);
            if (!options.Valid()) return Fail("Invalid Work result configuration.");
            return await ImportAsync(options, automatic: false, cancellationToken);
        }
        catch (Exception ex) when (IsReceiverFailure(ex) || ex is OperationCanceledException)
        {
            return Fail("The local Work result configuration is unavailable.");
        }
    }

    private async Task<OperationResult<SessionView>> ImportAsync(Options options, bool automatic, CancellationToken cancellationToken)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        bool awaitingFinalPdf = automatic && options.ExpectedMediaType == FinalPdfMediaType;
        if (!awaitingFinalPdf)
            deadline.CancelAfter(automatic ? TimeSpan.FromMinutes(12) : TimeSpan.FromMinutes(3));
        var ct = deadline.Token;
        AiDesignRequest? observed = null;
        WorkResultObservation? last = null;
        void Report(AiDesignRequest request, WorkResultStage stage, WorkResultProblem? problem = null)
        {
            var next = WorkResultObservation.From(request, stage, problem);
            if (next == last || cancellationToken.IsCancellationRequested) return;
            last = next;
            progress?.Report(next);
        }

        async Task ReportProblemAsync(WorkResultProblem problem)
        {
            if (observed is not { } source || progress is null || cancellationToken.IsCancellationRequested) return;
            var latest = await _requests.ReadAsync(source.RequestId, cancellationToken);
            if (latest.IsSuccess && FileAiDesignRequestStore.Matches(latest.Value, source.Fence))
                Report(source, WorkResultStage.NeedsAttention, problem);
        }

        try
        {
            var request = await _requests.ReadAsync(options.RequestId, ct);
            if (request.IsFailure) return OperationResult.Fail<SessionView>(request.Failure);
            var current = request.Value;
            if (current.AttemptId != options.AttemptId || current.WorkConversationId != options.WorkConversationId)
                return Fail("The local request does not match the Work result configuration.");
            if (current.State == AiDesignState.Imported && current.ImportedSessionId is { } session)
                return await sessions.LoadAsync(session, ct);
            if (!AiDesignTransitions.CanResumeWorkResult(current)) return Fail("The local Work request cannot resume result import.");

            observed = current;
            var status = new WorkResultPipeRequest("status", options.ReadBearer, options.RequestId, options.AttemptId, options.ReadTicket,
                WorkConversationId: automatic ? options.WorkConversationId : null);
            bool requiresConversation = automatic || File.Exists(Path.Combine(_requests.RequestsRoot,
                current.RequestId.ToString("D"), TicketFileName));
            Receipt receipt;
            do
            {
                receipt = await ReadPipeAsync<Receipt>(options.PipeName, status, ct);
                if (receipt.RequestId != options.RequestId || receipt.AttemptId != options.AttemptId || receipt.SchemaVersion != 1 ||
                    (receipt.WorkConversationId is { } bound && bound != current.WorkConversationId))
                {
                    await ReportProblemAsync(WorkResultProblem.ValidationFailed);
                    return Fail("The MCP receipt does not match the Work request.");
                }
                if (IsWaiting(receipt))
                {
                    if (progress is not null)
                    {
                        var latest = await _requests.ReadAsync(current.RequestId, ct);
                        if (latest.IsFailure || !FileAiDesignRequestStore.Matches(latest.Value, current.Fence))
                            return Fail("Work result import is no longer current.");
                        Report(current, receipt.State == "pending" ? WorkResultStage.AwaitingReturn : WorkResultStage.AwaitingVerification);
                    }
                    await Task.Delay(TimeSpan.FromSeconds(awaitingFinalPdf ? 2 : 1), ct);
                }
            } while (IsWaiting(receipt));

            bool IsWaiting(Receipt value) => value.State == "pending" ||
                (automatic && value.State == "received_unverified");
            ImageFormat format = receipt.MediaType switch
            {
                "image/png" when !awaitingFinalPdf => ImageFormat.Png,
                "image/jpeg" when !awaitingFinalPdf => ImageFormat.Jpeg,
                FinalPdfMediaType when awaitingFinalPdf => ImageFormat.Pdf,
                _ => ImageFormat.Unknown
            };
            bool validDimensions = format == ImageFormat.Pdf
                ? receipt.Width is null && receipt.Height is null
                : receipt.Width is > 0 && receipt.Height is > 0 && (long)receipt.Width.Value * receipt.Height.Value <= 64_000_000;
            if (receipt.State != "Ready" || !(receipt.SourceVerified || automatic && receipt.WorkReturnAccepted) ||
                string.IsNullOrWhiteSpace(receipt.SourceFileId) ||
                ((requiresConversation || receipt.WorkConversationId is not null) &&
                    receipt.WorkConversationId != current.WorkConversationId) ||
                (!automatic && !options.AllowReissuedFileId && receipt.SourceFileId != options.SourceFileId) ||
                receipt.ObjectId == Guid.Empty || receipt.ObjectVersion == Guid.Empty ||
                receipt.ByteLength is <= 0 or > MaxImageBytes || !Sha256.TryParse(receipt.Sha256, out var hash) ||
                (!automatic && (!Sha256.TryParse(options.SourceSha256, out var expectedHash) || hash != expectedHash)) ||
                current.Attachments.Any(attachment => attachment.Sha256 == hash) ||
                format == ImageFormat.Unknown || !validDimensions)
            {
                await ReportProblemAsync(WorkResultProblem.ValidationFailed);
                return Fail("The Work receipt does not match the expected result.");
            }

            var updated = await _requests.ImportReturnedWorkResultAsync(current.Fence, async token =>
            {
                Report(current, WorkResultStage.Validating);
                var bytes = await _pipe.ExchangeAsync(options.PipeName, status with
                {
                    Operation = "read", ObjectVersion = receipt.ObjectVersion
                }, receipt.ByteLength, token);
                if (bytes.Length != receipt.ByteLength || Sha256.FromBytes(SHA256.HashData(bytes)) != hash)
                    return OperationResult.Fail<AiDesignEvent.ReturnedWorkResultImported>(FailureCode.RevisionIntegrityMismatch,
                        "The MCP image hash does not match its receipt.");

                string name = AiDesignOwnedStagingFiles.ReturnedWorkResultName(receipt.ObjectVersion, format);
                string path = Path.Combine(_requests.RequestsRoot, current.RequestId.ToString("D"), name);
                if (!File.Exists(path))
                {
                    using var staged = AiDesignOwnedStagingFiles.CreateReturnedWorkResult(workspaceRoot, current.RequestId, receipt.ObjectVersion, format);
                    await staged.Output.WriteAsync(bytes, token);
                    await staged.MoveWithinBatchAsync(token);
                    staged.Release();
                }
                await using var held = VerifiedAiAttachmentReader.OpenRegular(path);
                var facts = await inspector.InspectAsync(path, token);
                if (facts.IsFailure) return OperationResult.Fail<AiDesignEvent.ReturnedWorkResultImported>(facts.Failure);
                if (facts.Value.Sha256 != hash || facts.Value.ByteLength != receipt.ByteLength ||
                    facts.Value.Format != format ||
                    facts.Value.PixelWidth != receipt.Width || facts.Value.PixelHeight != receipt.Height ||
                    (format == ImageFormat.Pdf && !await IsProcessablePdfAsync(path, token)) ||
                    await ReadPipeAsync<Receipt>(options.PipeName, status, token) != receipt)
                    return OperationResult.Fail<AiDesignEvent.ReturnedWorkResultImported>(FailureCode.RevisionIntegrityMismatch,
                        "The Work result or receipt is invalid or has changed.");

                var imported = await sessions.ImportVerifiedWorkResultAsync(current.RequestId, path, options.OutputName, hash, token);
                if (imported.IsFailure) return OperationResult.Fail<AiDesignEvent.ReturnedWorkResultImported>(imported.Failure);
                return OperationResult.Ok(new AiDesignEvent.ReturnedWorkResultImported(options.WorkConversationId,
                    new AiDesignCandidate(new(current.RequestId, name), facts.Value), imported.Value.Id));
            }, ct);
            if (updated.IsFailure)
            {
                await ReportProblemAsync(WorkResultProblem.ValidationFailed);
                return OperationResult.Fail<SessionView>(updated.Failure);
            }
            var loaded = await sessions.LoadAsync(updated.Value.ImportedSessionId!.Value, cancellationToken);
            if (loaded.IsSuccess) Report(updated.Value, WorkResultStage.Imported);
            else progress?.RequestChanged(current.RequestId);
            return loaded;
        }
        catch (OperationCanceledException) when (deadline.IsCancellationRequested)
        {
            await ReportProblemAsync(WorkResultProblem.TimedOut);
            return Fail("No verified Work image was imported within the wait period.");
        }
        catch (Exception ex) when (IsReceiverFailure(ex))
        {
            await ReportProblemAsync(WorkResultProblem.ReceiverUnavailable);
            return Fail("The local Work receiver or result file is unavailable.");
        }
    }

    private static async Task<bool> IsProcessablePdfAsync(string path, CancellationToken cancellationToken)
    {
        try
        {
            using var input = VerifiedAiAttachmentReader.OpenRegular(path);
            using var document = await new WindowsPdfDocumentAuthority().OpenAsync(input, cancellationToken);
            return !document.IsEncrypted && document.PageCount == 1 &&
                WindowsPdfPreparationProcessor.HasSupportedPageGeometry(document.InspectSinglePage());
        }
        catch (Exception ex) when (ex is not (OperationCanceledException or OutOfMemoryException or StackOverflowException))
        {
            return false;
        }
    }

    private static OperationResult<SessionView> Fail(string detail) =>
        OperationResult.Fail<SessionView>(FailureCode.AdapterUnavailable, detail);

    private sealed record Options(Guid RequestId, Guid AttemptId, Guid WorkConversationId, string PipeName,
        string SourceFileId, string SourceSha256, string ReadBearer, string ReadTicket, string OutputName, bool AllowReissuedFileId,
        string? ExpectedMediaType = null)
    {
        public bool Valid() => RequestId != Guid.Empty && AttemptId != Guid.Empty && WorkConversationId != Guid.Empty &&
            WorkResultPipe.IsName(PipeName) && SourceFileId is { Length: > 0 and <= 512 } && Sha256.TryParse(SourceSha256, out _) &&
            ReadBearer is { Length: >= 8 and <= 200 } && ReadTicket is { Length: >= 8 and <= 200 } &&
            !ReadBearer.Any(char.IsWhiteSpace) && !ReadTicket.Any(char.IsWhiteSpace) && !string.IsNullOrWhiteSpace(OutputName);
    }

    private sealed record Receipt(int SchemaVersion, Guid RequestId, Guid AttemptId, string State, string SourceFileId,
        Guid ObjectId, Guid ObjectVersion, int ByteLength, string Sha256, string MediaType, int? Width, int? Height, bool SourceVerified,
        Guid? WorkConversationId, bool WorkReturnAccepted);
}
