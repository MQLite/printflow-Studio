using System.IO;
using System.Text.Json;
using PrintFlow.Domain.AiDesign;
using PrintFlow.Infrastructure.AiDesign;
using PrintFlow.Tests.Fixtures;
using PrintFlow.Tests.Unit.AiDesign;
using PrintFlow.Workflow.AiDesign;

namespace PrintFlow.Tests.Integration.AiDesign;

[Collection(SqliteCollection.Name)]
public sealed class LoopbackWorkResultImportTests
{
    [Fact]
    public async Task Resume_adopts_only_the_enrolled_original_and_preserves_bytes_on_repeat()
    {
        using var harness = new SessionServiceHarness();
        var store = new FileAiDesignRequestStore(harness.Workspace.Root);
        var draft = AiDesignTransitionsTests.Draft();
        var request = (await store.CreateAsync(draft.RequestId, new(draft.Prompt, []), default)).Value;
        var attemptId = Guid.NewGuid();
        var conversationId = Guid.NewGuid();
        request = (await store.ChangeAsync(request.Fence,
            new AiDesignEvent.Queued(attemptId, "test-token", Environment.ProcessId, 1, Guid.NewGuid()), default)).Value;
        request = (await store.ChangeAsync(request.Fence, new AiDesignEvent.UploadStarted("tab", "binding"), default)).Value;
        request = (await store.SubmitOnceAsync(request.Fence, (_, _) =>
            Task.FromResult(PrintFlow.Domain.Results.OperationResult.Ok<Guid?>(conversationId)), default)).Value;
        request = (await store.ChangeAsync(request.Fence, new AiDesignEvent.Failed("TrialResultHandoffUnavailable"), default)).Value;
        var version = Guid.NewGuid();
        const string sourceFileId = "test-original-png";
        var source = harness.WriteSourcePng();
        var bytes = await File.ReadAllBytesAsync(source);
        var facts = (await harness.FileInspector.InspectAsync(source, default)).Value;
        var configuration = Path.Combine(harness.Workspace.Root, "resume.local.json");
        await File.WriteAllTextAsync(configuration, JsonSerializer.Serialize(new
        {
            requestId = request.RequestId, attemptId, workConversationId = conversationId, pipeName = "PrintFlow-WorkTest",
            sourceFileId, sourceSha256 = facts.Sha256.Value,
            readBearer = "local-test-bearer", readTicket = "local-test-ticket", outputName = "Work return"
        }));
        ResultTransport WithSource(string fileId, Guid? boundConversation = null) => new(request.RequestId, attemptId, version, bytes, JsonSerializer.SerializeToUtf8Bytes(new
        {
            schemaVersion = 1, requestId = request.RequestId, attemptId, state = "Ready", sourceVerified = true,
            workConversationId = boundConversation,
            sourceFileId = fileId, objectId = Guid.NewGuid(), objectVersion = version, byteLength = bytes.Length,
            sha256 = facts.Sha256.Value, mediaType = "image/png", width = facts.PixelWidth, height = facts.PixelHeight
        }));
        var sessions = harness.CreateService();
        var wrong = WithSource("different-image");
        var rejected = await new LoopbackWorkResultImport(harness.Workspace.Root, harness.FileInspector, sessions, wrong)
            .ResumeAsync(configuration, default);
        rejected.IsFailure.ShouldBeTrue();
        wrong.BlobReads.ShouldBe(0);
        (await store.ReadAsync(request.RequestId, default)).Value.State.ShouldBe(AiDesignState.PromptOnly);

        var wrongConversation = WithSource(sourceFileId, Guid.NewGuid());
        (await new LoopbackWorkResultImport(harness.Workspace.Root, harness.FileInspector, sessions, wrongConversation)
            .ResumeAsync(configuration, default)).IsFailure.ShouldBeTrue();
        wrongConversation.BlobReads.ShouldBe(0);

        var matching = WithSource(sourceFileId);
        var importer = new LoopbackWorkResultImport(harness.Workspace.Root, harness.FileInspector, sessions, matching);
        var imported = await importer.ResumeAsync(configuration, default);
        imported.IsSuccess.ShouldBeTrue(imported.IsFailure ? imported.Failure.TechnicalDetail : null);
        var result = (await store.ReadAsync(request.RequestId, default)).Value;
        result.State.ShouldBe(AiDesignState.Imported);
        result.ImportedSessionId.ShouldBe(imported.Value.Id);
        var candidate = result.Candidate!;
        candidate.Facts.Sha256.ShouldBe(facts.Sha256);
        File.ReadAllBytes(Path.Combine(store.RequestsRoot, request.RequestId.ToString("D"), candidate.File.RelativePath)).ShouldBe(bytes);

        (await importer.ResumeAsync(configuration, default)).Value.Id.ShouldBe(imported.Value.Id);
        matching.BlobReads.ShouldBe(1);
        (await harness.Repository.LoadAsync(imported.Value.Id, default)).Value!.Revisions.ShouldHaveSingleItem()
            .Sha256.ShouldBe(facts.Sha256);
    }

    [Theory]
    [InlineData("import")]
    [InlineData("automatic-return")]
    [InlineData("stop-before-restart")]
    [InlineData("stop-after-receipt")]
    [InlineData("no-receiver")]
    [InlineData("old-attempt")]
    [InlineData("wrong-request")]
    [InlineData("multiple-pages")]
    [InlineData("wrong-format")]
    public async Task Work_receipt_progress_precedes_persisted_import(string scenario)
    {
        using var harness = new SessionServiceHarness();
        var store = new FileAiDesignRequestStore(harness.Workspace.Root);
        var draft = AiDesignTransitionsTests.Draft();
        var request = (await store.CreateAsync(draft.RequestId, new(draft.Prompt, []), default)).Value;
        var attempt = Guid.NewGuid();
        var conversation = Guid.NewGuid();
        request = (await store.ChangeAsync(request.Fence,
            new AiDesignEvent.Queued(attempt, "test-token", Environment.ProcessId, 1, Guid.NewGuid()), default)).Value;

        var configPath = Path.Combine(harness.Workspace.Root, "receiver.local.json");
        await File.WriteAllTextAsync(configPath, JsonSerializer.Serialize(new
        {
            pipeName = "PrintFlow-WorkTest", controlBearer = "local-control-bearer", readBearer = "local-read-bearer",
            workspaceRoot = harness.Workspace.Root
        }));
        bool wrongFormat = scenario == "wrong-format";
        var source = wrongFormat ? harness.WriteSourcePng() : harness.Workspace.CreateSourceFile("final.pdf",
            PdfFixtures.Read(scenario == "multiple-pages" ? "two" : "single"));
        var facts = (await harness.FileInspector.InspectAsync(source, default)).Value;
        var version = Guid.NewGuid();
        var image = await File.ReadAllBytesAsync(source);
        byte[] Receipt(string state, bool ready = false) => JsonSerializer.SerializeToUtf8Bytes(new
        {
            schemaVersion = 1,
            requestId = ready && scenario == "wrong-request" ? Guid.NewGuid() : request.RequestId,
            attemptId = ready && scenario == "old-attempt" ? Guid.NewGuid() : attempt,
            state, sourceVerified = ready && scenario != "automatic-return",
            workReturnAccepted = ready && scenario == "automatic-return",
            workConversationId = ready ? conversation : (Guid?)null,
            sourceFileId = "platform-image", objectId = version, objectVersion = version,
            byteLength = image.Length, sha256 = facts.Sha256.Value, mediaType = wrongFormat ? "image/png" : "application/pdf",
            width = facts.PixelWidth, height = facts.PixelHeight
        });
        var transport = new HandoffTransport(request.RequestId, attempt, version, image, Receipt("pending"), Receipt("received_unverified"), Receipt("Ready", true));
        var sessions = harness.CreateService();
        var importer = new LoopbackWorkResultImport(harness.Workspace.Root, harness.FileInspector, sessions, transport);
        var enrolled = await importer.EnrollAsync(configPath, request, default);
        enrolled.Value.ShouldBe("local-upload-ticket");
        var prompt = WorkResultPrompt.Append(request, enrolled.Value);
        prompt.ShouldContain("save_final_pdf");
        prompt.ShouldNotContain("Generate exactly one image");
        prompt.ShouldContain("local-upload-ticket");
        prompt.ShouldNotContain("local-read-ticket");
        (await store.ReadAsync(request.RequestId, default)).Value.Prompt.Text.ShouldBe(request.Prompt.Text);

        request = (await store.ChangeAsync(request.Fence, new AiDesignEvent.UploadStarted("tab", "binding"), default)).Value;
        request = (await store.SubmitOnceAsync(request.Fence, (_, _) =>
            Task.FromResult(PrintFlow.Domain.Results.OperationResult.Ok<Guid?>(conversation)), default)).Value;
        request.State.ShouldBe(AiDesignState.Submitted);
        bool stopped = scenario is "stop-before-restart" or "stop-after-receipt";
        if (scenario == "stop-before-restart")
        {
            request = (await store.ChangeAsync(request.Fence, new AiDesignEvent.Failed("WorkResultPending"), default)).Value;
            using var submission = new ChromeWorkTextSubmission(harness.Workspace.Root);
            (await submission.StopReceivingAsync(request.RequestId, default)).IsSuccess.ShouldBeTrue();
        }

        // A different process creation time models a dead owner after Chrome confirmed submission.
        // The startup path uses the persisted read ticket; it never enrolls or submits again.
        var observations = new ProgressCapture(observation =>
        {
            if (observation.Stage is WorkResultStage.AwaitingReturn or WorkResultStage.AwaitingVerification)
            {
                transport.BlobReads.ShouldBe(0);
                (sessions.ListRecentAsync(default).GetAwaiter().GetResult()).Value.ShouldBeEmpty();
            }
        });
        if (scenario == "stop-after-receipt")
            transport.BeforeReady = async () =>
            {
                using var submission = new ChromeWorkTextSubmission(harness.Workspace.Root);
                (await submission.StopReceivingAsync(request.RequestId, default)).IsSuccess.ShouldBeTrue();
            };
        var resumed = new LoopbackWorkResultImport(harness.Workspace.Root, harness.FileInspector, sessions, transport, observations);
        await resumed.ResumePendingAsync(scenario == "no-receiver" ? null : configPath, default);
        transport.Enrollments.ShouldBe(1);
        bool imported = scenario is "import" or "automatic-return";
        transport.BlobReads.ShouldBe(imported || scenario == "multiple-pages" ? 1 : 0);
        var completed = (await store.ReadAsync(request.RequestId, default)).Value;
        completed.State.ShouldBe(stopped ? AiDesignState.Cancelled : imported ? AiDesignState.Imported : AiDesignState.Interrupted);
        completed.WorkConversationId.ShouldBe(conversation);
        if (!imported)
        {
            completed.Candidate.ShouldBeNull();
            observations.Events.ShouldNotContain(x => x.Stage == WorkResultStage.Imported);
            if (scenario != "multiple-pages") observations.Events.ShouldNotContain(x => x.Stage == WorkResultStage.Validating);
            if (scenario == "stop-before-restart") observations.Events.ShouldBeEmpty();
            if (scenario == "no-receiver") observations.Events.ShouldHaveSingleItem().Problem.ShouldBe(WorkResultProblem.ReceiverUnavailable);
            (await sessions.ListRecentAsync(default)).Value.ShouldBeEmpty();
        }
        else
        {
            completed.Candidate!.Facts.Sha256.ShouldBe(facts.Sha256);
            completed.Candidate.Facts.Format.ShouldBe(PrintFlow.Domain.Files.ImageFormat.Pdf);
            completed.Candidate.File.RelativePath.ShouldEndWith(".pdf");
            observations.Events.Select(x => x.Stage).ShouldBe(new[] { WorkResultStage.AwaitingReturn,
                WorkResultStage.AwaitingVerification, WorkResultStage.Validating, WorkResultStage.Imported });
            observations.Events[^1].ImportedSessionId.ShouldBe(completed.ImportedSessionId);
            observations.Events[^1].StateVersion.ShouldBe(completed.StateVersion);
            (await harness.Repository.LoadAsync(completed.ImportedSessionId!.Value, default)).Value!.Revisions.ShouldHaveSingleItem();
        }
    }

    private sealed class ProgressCapture(Action<WorkResultObservation> onReport) : IWorkResultProgressObserver
    {
        public List<WorkResultObservation> Events { get; } = [];
        public void Report(WorkResultObservation observation)
        {
            onReport(observation);
            Events.Add(observation);
        }
    }

    private sealed class HandoffTransport(Guid request, Guid attempt, Guid version, byte[] image, byte[] pending, byte[] unverified, byte[] ready)
        : IWorkResultPipe
    {
        private int statusReads;
        public Func<Task>? BeforeReady { get; set; }
        public int Enrollments { get; private set; }
        public int BlobReads { get; private set; }

        public Task<byte[]> ExchangeAsync(string name, WorkResultPipeRequest message, int limit, CancellationToken ct)
        {
            name.ShouldBe("PrintFlow-WorkTest");
            message.RequestId.ShouldBe(request);
            message.AttemptId.ShouldBe(attempt);
            if (message.Operation == "enroll")
            {
                Enrollments++;
                message.Bearer.ShouldBe("local-control-bearer");
                message.ReferenceSha256.ShouldBeEmpty();
                message.ReadTicket.ShouldBeNull();
                message.ReturnMediaType.ShouldBe("application/pdf");
                return Task.FromResult(JsonSerializer.SerializeToUtf8Bytes(new { requestId = request, attemptId = attempt,
                    readTicket = "local-read-ticket", uploadTicket = "local-upload-ticket", returnMediaType = message.ReturnMediaType }));
            }

            message.Bearer.ShouldBe("local-read-bearer");
            message.ReadTicket.ShouldBe("local-read-ticket");
            if (message.Operation == "status")
            {
                var count = statusReads++;
                if (count == 3 && BeforeReady is { } action) return ReadyAfterAsync(action);
                return Task.FromResult(count switch { 0 or 1 => pending, 2 => unverified, _ => ready });
            }
            message.Operation.ShouldBe("read");
            message.ObjectVersion.ShouldBe(version);
            statusReads.ShouldBeGreaterThan(1);
            BlobReads++;
            return Task.FromResult(image);
        }

        private async Task<byte[]> ReadyAfterAsync(Func<Task> action)
        {
            await action();
            return ready;
        }
    }

    private sealed class ResultTransport(Guid request, Guid attempt, Guid version, byte[] image, byte[] receipt) : IWorkResultPipe
    {
        public int BlobReads { get; private set; }

        public Task<byte[]> ExchangeAsync(string name, WorkResultPipeRequest message, int limit, CancellationToken ct)
        {
            name.ShouldBe("PrintFlow-WorkTest");
            message.Bearer.ShouldBe("local-test-bearer");
            message.ReadTicket.ShouldBe("local-test-ticket");
            message.RequestId.ShouldBe(request);
            message.AttemptId.ShouldBe(attempt);
            if (message.Operation == "status") return Task.FromResult(receipt);
            message.Operation.ShouldBe("read");
            message.ObjectVersion.ShouldBe(version);
            BlobReads++;
            return Task.FromResult(image);
        }
    }
}
