using System.Diagnostics;
using System.IO;
using System.Text.Json;
using PrintFlow.Domain.AiDesign;
using PrintFlow.Domain.Results;
using PrintFlow.Infrastructure.AiDesign.Authoring;
using PrintFlow.Workflow.Services;
using PrintFlow.Workflow.AiDesign;

namespace PrintFlow.Infrastructure.AiDesign;

public sealed partial class LoopbackWorkResultImport
{
    public const string ConfigurationVariable = "PRINTFLOW_WORK_RESULT_CONFIG";
    internal const string TicketFileName = "work-result-ticket.local.json";
    private const string FinalPdfMediaType = "application/pdf";

    internal async Task<OperationResult<string>> EnrollAsync(string configPath, AiDesignRequest request, CancellationToken ct)
    {
        try
        {
            var receiver = await LoadReceiverAsync(configPath, ct);
            var enroll = new WorkResultPipeRequest("enroll", receiver.ControlBearer, request.RequestId, request.AttemptId,
                ReferenceSha256: request.Attachments.Select(attachment => attachment.Sha256.Value)
                    .Concat(request.WorkFollowUp is { } prior ? [prior.ResultSha256.Value] : [])
                    .Distinct().ToArray(),
                ReturnMediaType: FinalPdfMediaType);
            var tickets = await ReadPipeAsync<Enrollment>(receiver.PipeName, enroll, ct);
            if (tickets.RequestId != request.RequestId || tickets.AttemptId != request.AttemptId ||
                tickets.ReturnMediaType != FinalPdfMediaType ||
                !WorkResultPrompt.IsTicket(tickets.UploadTicket) || !WorkResultPrompt.IsTicket(tickets.ReadTicket))
                throw new IOException("Work result enrollment mismatch.");
            using var staged = AiDesignOwnedStagingFiles.CreateRequest(workspaceRoot, request.RequestId,
                AiDesignOwnedStagingFiles.RequestPurpose.WorkResultTicket);
            await JsonSerializer.SerializeAsync(staged.Output,
                new ReadTicket(request.RequestId, request.AttemptId, receiver.PipeName, tickets.ReadTicket, tickets.ReturnMediaType),
                FileAiDesignRequestStore.JsonOptions, ct);
            await staged.PublishAsync(ct);
            return OperationResult.Ok(tickets.UploadTicket);
        }
        catch (Exception ex) when (IsReceiverFailure(ex) || ex is OperationCanceledException)
        {
            return OperationResult.Fail<string>(FailureCode.AdapterUnavailable, "WorkReceiverUnavailable");
        }
    }

    internal async Task<OperationResult<SessionView>> ResumeEnrolledAsync(string configPath, AiDesignRequest request, CancellationToken ct)
    {
        if (!AiDesignTransitions.CanResumeWorkResult(request) || request.WorkConversationId is not { } conversation)
            return Fail("The local Work request has no active result handoff.");
        try
        {
            string path = Path.Combine(_requests.RequestsRoot, request.RequestId.ToString("D"), TicketFileName);
            var ticket = await ReadLocalAsync<ReadTicket>(path, ct);
            var receiver = await LoadReceiverAsync(configPath, ct);
            if (ticket.RequestId != request.RequestId || ticket.AttemptId != request.AttemptId ||
                ticket.PipeName != receiver.PipeName || !WorkResultPrompt.IsTicket(ticket.Ticket) ||
                ticket.ReturnMediaType is not (null or FinalPdfMediaType))
            {
                progress?.Report(WorkResultObservation.From(request, WorkResultStage.NeedsAttention, WorkResultProblem.ValidationFailed));
                return Fail("The local Work result ticket is stale.");
            }

            string outputName = ticket.ReturnMediaType == FinalPdfMediaType ? "Work final PDF" : "Work image";
            return await ImportAsync(new Options(request.RequestId, request.AttemptId, conversation, receiver.PipeName,
                "", "", receiver.ReadBearer, ticket.Ticket, outputName, false, ticket.ReturnMediaType), automatic: true, ct);
        }
        catch (Exception ex) when (IsReceiverFailure(ex) || ex is OperationCanceledException)
        {
            if (!ct.IsCancellationRequested)
                progress?.Report(WorkResultObservation.From(request, WorkResultStage.NeedsAttention, WorkResultProblem.ReceiverUnavailable));
            return Fail("The local Work result ticket or receiver is unavailable.");
        }
    }

    internal async Task RevokeAsync(string configPath, AiDesignRequest request, CancellationToken ct)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(TimeSpan.FromSeconds(3));
        try
        {
            var receiver = await LoadReceiverAsync(configPath, deadline.Token).ConfigureAwait(false);
            await _pipe.ExchangeAsync(receiver.PipeName,
                new("cancel", receiver.ControlBearer, request.RequestId, request.AttemptId), MaxMetadataBytes, deadline.Token)
                .ConfigureAwait(false);
        }
        catch (Exception ex) when (IsReceiverFailure(ex) || ex is OperationCanceledException)
        { /* The cancelled local fence remains authoritative. */ }
    }

    public Task ResumePendingAsync(CancellationToken ct) =>
        ResumePendingAsync(Environment.GetEnvironmentVariable(ConfigurationVariable), ct);

    internal async Task ResumePendingAsync(string? configPath, CancellationToken ct)
    {
        bool receiverConfigured = false;
        if (!string.IsNullOrWhiteSpace(configPath))
        {
            try { await LoadReceiverAsync(configPath, ct); receiverConfigured = true; }
            catch (Exception ex) when (IsReceiverFailure(ex) || ex is OperationCanceledException) { }
        }
        var requests = await _requests.ListAsync(ct);
        if (requests.IsFailure) return;
        // A crash can occur after Chrome records submission but before Studio releases the page lease.
        // Recover only attempts with a durable ticket and an independently recorded submission.
        var pending = requests.Value.Where(request => AiDesignTransitions.CanResumeWorkResult(request) ||
                (request.State == AiDesignState.Submitted && request.WorkConversationId is not null && PreviousOwnerExited(request)))
            .Select(request => (Request: request, HasTicket: _requests.HasWorkResultTicket(request.RequestId)))
            .Where(item => item.Request.Failure?.Code == "WorkResultPending" || item.HasTicket)
            .OrderByDescending(item => item.Request.UpdatedAtUtc).ToArray();
        var watch = pending.Where(item => item.HasTicket).Take(8).Select(item => item.Request.RequestId).ToHashSet();
        await Task.WhenAll(pending.Select(async item =>
        {
            var request = item.Request;
            if (request.State == AiDesignState.Submitted)
            {
                var recovered = await _requests.ChangeAsync(request.Fence, new AiDesignEvent.Interrupted(), ct);
                if (recovered.IsFailure) return;
                request = recovered.Value;
            }
            if (ct.IsCancellationRequested) return;
            if (receiverConfigured && watch.Contains(request.RequestId))
            {
                await ResumeEnrolledAsync(configPath!, request, ct);
                return;
            }
            progress?.Report(WorkResultObservation.From(request, WorkResultStage.NeedsAttention,
                receiverConfigured && item.HasTicket ? WorkResultProblem.RecoveryDeferred : WorkResultProblem.ReceiverUnavailable));
        }));
    }

    internal static bool IsOwnerAlive(AiDesignRequest request) => request.OwnerProcessId is not null && !PreviousOwnerExited(request);

    private static bool PreviousOwnerExited(AiDesignRequest request)
    {
        if (request.OwnerProcessId is not { } pid || request.OwnerProcessStartUtcTicks is not { } ticks) return false;
        try
        {
            using var owner = Process.GetProcessById(pid);
            return owner.HasExited || owner.StartTime.ToUniversalTime().Ticks != ticks;
        }
        catch (ArgumentException) { return true; }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception) { return false; }
    }

    private async Task<ReceiverOptions> LoadReceiverAsync(string path, CancellationToken ct)
    {
        var receiver = await ReadLocalAsync<ReceiverOptions>(path, ct);
        if (!WorkResultPipe.IsName(receiver.PipeName) || !ValidBearer(receiver.ReadBearer) || !ValidBearer(receiver.ControlBearer) ||
            receiver.ReadBearer == receiver.ControlBearer || string.IsNullOrEmpty(receiver.WorkspaceRoot) ||
            !Path.IsPathFullyQualified(receiver.WorkspaceRoot) ||
            !string.Equals(Path.TrimEndingDirectorySeparator(Path.GetFullPath(receiver.WorkspaceRoot)),
                Path.TrimEndingDirectorySeparator(_requests.WorkspaceRoot), StringComparison.OrdinalIgnoreCase))
            throw new IOException("Work receiver configuration does not match the workspace.");
        return receiver;
    }

    private static async Task<T> ReadLocalAsync<T>(string path, CancellationToken ct)
    {
        if (!Path.IsPathFullyQualified(path) || !path.EndsWith(".local.json", StringComparison.OrdinalIgnoreCase))
            throw new IOException("An absolute private Work configuration path is required.");
        await using var input = VerifiedAiAttachmentReader.OpenRegular(path);
        if (input.Length is <= 0 or > MaxMetadataBytes) throw new IOException("Invalid Work metadata size.");
        return await JsonSerializer.DeserializeAsync<T>(input, FileAiDesignRequestStore.JsonOptions, ct) ?? throw new JsonException();
    }

    private async Task<T> ReadPipeAsync<T>(string pipeName, WorkResultPipeRequest request, CancellationToken ct)
    {
        byte[] bytes = await _pipe.ExchangeAsync(pipeName, request, MaxMetadataBytes, ct);
        return JsonSerializer.Deserialize<T>(bytes, FileAiDesignRequestStore.JsonOptions) ?? throw new JsonException();
    }

    private static bool ValidBearer(string? bearer) =>
        bearer is { Length: >= 8 and <= 200 } && bearer.All(c => c is >= '!' and <= '~');

    private static bool IsReceiverFailure(Exception ex) => ex is IOException or TimeoutException or JsonException or
        UnauthorizedAccessException or ArgumentException or FormatException or System.ComponentModel.Win32Exception;

    private sealed record ReceiverOptions(string PipeName, string ControlBearer, string ReadBearer, string WorkspaceRoot);
    private sealed record Enrollment(Guid RequestId, Guid AttemptId, string UploadTicket, string ReadTicket, string? ReturnMediaType);
    private sealed record ReadTicket(Guid RequestId, Guid AttemptId, string PipeName, string Ticket, string? ReturnMediaType);
}
