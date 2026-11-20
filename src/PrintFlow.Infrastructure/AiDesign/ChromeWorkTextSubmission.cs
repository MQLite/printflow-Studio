using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;
using System.Security.Cryptography;
using PrintFlow.Domain.AiDesign;
using PrintFlow.Domain.AiDesign.Authoring;
using PrintFlow.Domain.Results;
using PrintFlow.Infrastructure.AiDesign.Authoring;
using PrintFlow.Infrastructure.Workspace;
using PrintFlow.Workflow.AiDesign;
using PrintFlow.Workflow.AiDesign.Authoring;

namespace PrintFlow.Infrastructure.AiDesign;

public sealed class ChromeWorkTextSubmission(string workspaceRoot, LoopbackWorkResultImport? resultImport = null,
    IWorkResultProgressObserver? progress = null) : IAiDesignTextSubmission, IDisposable
{
    private readonly FileAiDesignRequestStore _store = new(workspaceRoot);
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly CancellationTokenSource _lifetime = new();

    public async Task<OperationResult<AiDesignWorkSubmission>> SubmitAsync(AiAuthoringDraft draft, CancellationToken cancellationToken)
    {
        Guid id = Guid.NewGuid();
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetime.Token);
        try
        {
            // Keep the coordinator and its synchronous lease cleanup off the WPF dispatcher.
            return await Task.Run(async () =>
            {
                await _gate.WaitAsync(linked.Token);
                try
                {
                    using var draftLease = _store.TryLockWorkDraft(draft.Id);
                    if (draftLease is null) return Unavailable("WorkRequestActive");
                    var authority = await _store.ListSnapshotsAsync(linked.Token);
                    if (authority.IsFailure) return OperationResult.Fail<AiDesignWorkSubmission>(authority.Failure);
                    if (authority.Value.Any(request => request.WorkDraft?.DraftId == draft.Id && BlocksNewSubmission(request)))
                        return Unavailable("WorkRequestActive");

                    var prompt = ExportForWork(draft, id);
                    if (prompt.Issues.Count != 0) return Unavailable("WorkPromptAttachmentRequired");
                    if (string.IsNullOrWhiteSpace(prompt.Text) || prompt.Text.Length > 100000)
                        return Unavailable("WorkPromptRejected");
                    return await SubmitCoreAsync(new(draft.Id, draft.TemplateSnapshot.Name), id, prompt, linked.Token);
                }
                finally
                {
                    _gate.Release();
                    progress?.RequestChanged(id);
                }
            }, linked.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (linked.IsCancellationRequested)
        {
            return Unavailable("WorkSubmitUnconfirmed");
        }
    }

    private bool BlocksNewSubmission(AiDesignRequest request) =>
        (AiDesignTransitions.IsRunning(request.State) && LoopbackWorkResultImport.IsOwnerAlive(request)) ||
        (request.State == AiDesignState.Submitted && request.WorkConversationId is not null && _store.HasWorkResultTicket(request.RequestId)) ||
        (AiDesignTransitions.CanResumeWorkResult(request) &&
            (request.Failure?.Code == "WorkResultPending" || _store.HasWorkResultTicket(request.RequestId)));

    public async Task<OperationResult<AiDesignWorkSubmission>> SubmitFollowUpAsync(Guid sourceRequestId, string prompt,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(prompt) || prompt.Length > 100000) return Unavailable("WorkPromptRejected");
        string? configuration = Environment.GetEnvironmentVariable(LoopbackWorkResultImport.ConfigurationVariable);
        if (resultImport is null || string.IsNullOrWhiteSpace(configuration))
            return Unavailable("WorkReceiverUnavailable");

        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetime.Token);
        Guid id = Guid.NewGuid();
        try
        {
            return await Task.Run(async () =>
            {
                await _gate.WaitAsync(linked.Token);
                try
                {
                    var source = await _store.ReadAsync(sourceRequestId, linked.Token);
                    if (source.IsFailure || source.Value.State != AiDesignState.Imported ||
                        source.Value.ImportedSessionId is null || source.Value.Candidate is null ||
                        source.Value.WorkDraft is not { } draft ||
                        source.Value.WorkConversationId is not { } conversation || conversation == Guid.Empty)
                        return Unavailable("WorkFollowUpUnavailable");
                    using var draftLease = _store.TryLockWorkDraft(draft.DraftId);
                    if (draftLease is null) return Unavailable("WorkRequestActive");
                    var listed = await _store.ListSnapshotsAsync(linked.Token);
                    if (listed.IsFailure) return OperationResult.Fail<AiDesignWorkSubmission>(listed.Failure);
                    if (listed.Value.Any(request => (request.WorkDraft?.DraftId == draft.DraftId ||
                        request.WorkConversationId == conversation || request.WorkFollowUp?.ConversationId == conversation)
                        && BlocksNewSubmission(request)))
                        return Unavailable("WorkRequestActive");

                    return await SubmitCoreAsync(draft, id, new(prompt, [], []), linked.Token, sourceRequestId);
                }
                finally
                {
                    _gate.Release();
                    progress?.RequestChanged(id);
                }
            }, linked.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (linked.IsCancellationRequested)
        {
            return Unavailable("WorkSubmitUnconfirmed");
        }
    }

    internal static AiPromptExport ExportForWork(AiAuthoringDraft draft, Guid requestId) =>
        AiAuthoringModule.Export(draft, asset =>
            AiDesignOwnedStagingFiles.AttachmentName(asset.Order, Path.GetExtension(asset.UploadName), requestId));

    private async Task<OperationResult<AiDesignWorkSubmission>> SubmitCoreAsync(WorkDraftReference draft, Guid id, AiPromptExport prompt,
        CancellationToken ct, Guid? sourceRequestId = null)
    {
        var loaded = await AiDesignBridgeConfiguration.LoadAsync(AiDesignBridgeConfiguration.ConfigurationPath, ct);
        if (loaded.IsFailure)
            return Unavailable("BridgeNotConfigured");

        var configuration = loaded.Value;
        var identity = configuration.Validate(configuration.ExtensionOrigin);
        if (identity.IsFailure || identity.Value != _store.WorkspaceIdentity)
            return Unavailable("BridgeRootMismatch");
        if (configuration.VerifiedWorkContractHash != AiDesignBridgeConfiguration.TextSubmissionTrialContract ||
            configuration.ChromeProfileDirectory is null)
            return Unavailable("WorkContractUnverified");
        string launchNonce = Convert.ToHexString(RandomNumberGenerator.GetBytes(16));
        string? resultConfig = Environment.GetEnvironmentVariable(LoopbackWorkResultImport.ConfigurationVariable);
        bool returnEnabled = !string.IsNullOrWhiteSpace(resultConfig) && resultImport is not null;
        if (sourceRequestId is not null && !returnEnabled)
            return Unavailable("WorkReceiverUnavailable");
        string? preparationFailure = null;
        var channel = new FileAiDesignChannel(configuration, expectedLaunchNonce: launchNonce,
            prepareResult: returnEnabled ? async (request, token) =>
            {
                var prepared = await resultImport!.EnrollAsync(resultConfig!, request, token);
                if (prepared.IsFailure) preparationFailure = prepared.Failure.TechnicalDetail;
                return prepared;
            } : null);
        if (!await IsReadyAsync(channel, ct))
        {
            string? chrome = LocateChrome();
            if (chrome is null)
                return Unavailable("ChromeNotFound");
            var launch = CreateLaunchInfo(configuration, chrome, launchNonce);
            if (launch.IsFailure)
                return OperationResult.Fail<AiDesignWorkSubmission>(launch.Failure);

            try
            {
                using var started = Process.Start(launch.Value);
                if (started is null) return Unavailable("ChromeLaunchFailed");
            }
            catch (Exception ex) when (ex is InvalidOperationException or Win32Exception)
            {
                return Unavailable("ChromeLaunchFailed");
            }

            var elapsed = Stopwatch.StartNew();
            while (!await IsReadyAsync(channel, ct))
            {
                if (elapsed.Elapsed >= TimeSpan.FromSeconds(16))
                    return Unavailable("BridgeHandshakeTimedOut");
                await Task.Delay(200, ct);
            }
        }

        if (sourceRequestId is not null)
        {
            var readiness = await channel.CheckAsync(ct);
            if (readiness.IsFailure || !readiness.Value.Available || !readiness.Value.SupportsWorkFollowUp)
                return Unavailable("WorkFollowUpBridgeUnavailable");
        }

        using var coordinator = new AiDesignCoordinator(_store, channel,
            new AiDesignBrowserLease(AiDesignBridgeConfiguration.InstallationDirectory), attachments: new FileAiDesignAttachments(workspaceRoot));
        var saved = sourceRequestId is { } sourceId
            ? await _store.CreateWorkFollowUpAsync(sourceId, id, prompt.Text, ct)
            : await new FileAiAuthoringAssetFork(workspaceRoot).CopyAndPublishAsync(draft.DraftId, id, prompt.RequiredAttachments,
                assets => _store.CreateAsync(id, new(new AiDesignPrompt(PromptTemplateService.WorkbenchTemplate, 1,
                    new Dictionary<string, string>(), prompt.Text, assets.Select(asset => asset.Id).ToArray(), ""), assets, draft), ct), ct,
                useWorkNames: true);
        if (saved.IsFailure)
            return OperationResult.Fail<AiDesignWorkSubmission>(saved.Failure);

        var startedRequest = await coordinator.StartAsync(id, ct);
        if (startedRequest.IsFailure)
            return OperationResult.Fail<AiDesignWorkSubmission>(startedRequest.Failure);

        var queued = startedRequest.Value.Request;
        if (queued.AttemptId != Guid.Empty && AiDesignTransitions.IsRunning(queued.State))
            progress?.Report(new(queued.RequestId, queued.AttemptId, queued.StateVersion, WorkResultStage.Submitting));

        var waiting = Stopwatch.StartNew();
        var submitTimeout = prompt.RequiredAttachments.Count == 0 ? TimeSpan.FromSeconds(30) : TimeSpan.FromMinutes(5);
        while (waiting.Elapsed < submitTimeout)
        {
            var current = await _store.ReadAsync(id, ct);
            if (current.IsFailure)
                return OperationResult.Fail<AiDesignWorkSubmission>(current.Failure);
            if (current.Value.State == AiDesignState.Submitted && current.Value.WorkConversationId is { } conversation)
            {
                // The page handoff is finished; release its lease while the local receiver waits.
                var ended = await _store.ChangeAsync(current.Value.Fence,
                    new AiDesignEvent.Failed(returnEnabled ? "WorkResultPending" : "TrialResultHandoffUnavailable"), ct);
                if (ended.IsFailure) return OperationResult.Fail<AiDesignWorkSubmission>(ended.Failure);
                var result = returnEnabled ? resultImport!.ResumeEnrolledAsync(resultConfig!, ended.Value, _lifetime.Token) : null;
                return OperationResult.Ok(new AiDesignWorkSubmission(id, conversation, result));
            }
            if (!AiDesignTransitions.IsRunning(current.Value.State))
                return Unavailable(preparationFailure ?? current.Value.Failure?.Code ?? "WorkSubmitUnconfirmed");
            await Task.Delay(200, ct);
        }

        await coordinator.StopAsync(id, CancellationToken.None);
        return Unavailable("WorkSubmitUnconfirmed");
    }

    public async Task<OperationResult<Unit>> StopReceivingAsync(Guid requestId, CancellationToken ct)
    {
        var current = await _store.ReadAsync(requestId, ct).ConfigureAwait(false);
        if (current.IsFailure) return OperationResult.Fail<Unit>(current.Failure);
        if (!AiDesignTransitions.CanResumeWorkResult(current.Value) ||
            current.Value.State != AiDesignState.AwaitingWorkFollowUp &&
            current.Value.Failure?.Code is not ("WorkResultPending" or "Interrupted"))
            return OperationResult.Fail<Unit>(FailureCode.PreconditionNotMet, "This Work result is no longer pending.");
        var stopped = await _store.ChangeAsync(current.Value.Fence, new AiDesignEvent.StopAccepted(), ct).ConfigureAwait(false);
        if (stopped.IsFailure) return OperationResult.Fail<Unit>(stopped.Failure);
        progress?.RequestChanged(requestId);

        if (resultImport is not null &&
            Environment.GetEnvironmentVariable(LoopbackWorkResultImport.ConfigurationVariable) is { } resultConfig)
            await resultImport.RevokeAsync(resultConfig, stopped.Value, ct).ConfigureAwait(false);
        return OperationResult.Ok();
    }

    private async Task<bool> IsReadyAsync(FileAiDesignChannel channel, CancellationToken ct)
    {
        var result = await channel.CheckAsync(ct);
        return result.IsSuccess && result.Value.Available && result.Value.WorkspaceIdentity == _store.WorkspaceIdentity;
    }

    internal static OperationResult<ProcessStartInfo> CreateLaunchInfo(AiDesignBridgeConfiguration configuration, string chromeExecutable, string launchNonce)
    {
        string? profile = configuration.ChromeProfileDirectory;
        if (configuration.VerifiedWorkContractHash != AiDesignBridgeConfiguration.TextSubmissionTrialContract ||
            profile is null || !Regex.IsMatch(profile, @"\A(?:Default|Profile [1-9][0-9]{0,2})\z", RegexOptions.CultureInvariant) ||
            !Regex.IsMatch(launchNonce, @"\A[A-F0-9]{32}\z", RegexOptions.CultureInvariant) ||
            configuration.Validate(configuration.ExtensionOrigin).IsFailure ||
            !Path.IsPathFullyQualified(chromeExecutable) || !File.Exists(chromeExecutable) ||
            !Path.GetFileName(chromeExecutable).Equals("chrome.exe", StringComparison.OrdinalIgnoreCase))
            return OperationResult.Fail<ProcessStartInfo>(FailureCode.AdapterUnavailable, "WorkTrialConfigurationInvalid");

        try { ReparsePointGuard.RefuseAncestry(chromeExecutable); }
        catch (IOException) { return OperationResult.Fail<ProcessStartInfo>(FailureCode.AdapterUnavailable, "ChromeLaunchFailed"); }

        var launch = new ProcessStartInfo(chromeExecutable) { UseShellExecute = false };
        launch.ArgumentList.Add("--profile-directory=" + profile);
        launch.ArgumentList.Add("--new-window");
        launch.ArgumentList.Add(configuration.ExtensionOrigin + "connect.html#" + launchNonce);
        return OperationResult.Ok(launch);
    }

    internal static string? LocateChrome() => new[]
    {
        Environment.SpecialFolder.ProgramFiles, Environment.SpecialFolder.ProgramFilesX86, Environment.SpecialFolder.LocalApplicationData
    }.Select(folder => Environment.GetFolderPath(folder))
        .Where(root => !string.IsNullOrWhiteSpace(root))
        .Select(root => Path.Combine(root, "Google", "Chrome", "Application", "chrome.exe"))
        .FirstOrDefault(File.Exists);

    private static OperationResult<AiDesignWorkSubmission> Unavailable(string code) => OperationResult.Fail<AiDesignWorkSubmission>(FailureCode.AdapterUnavailable, code);

    public void Dispose() => _lifetime.Cancel();
}
