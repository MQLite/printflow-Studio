using System.IO;
using PrintFlow.Domain.Files;
using PrintFlow.Domain.Ids;
using PrintFlow.Domain.Results;
using PrintFlow.Domain.Sessions;
using PrintFlow.Infrastructure.Imaging;
using PrintFlow.Tests.Fixtures;

namespace PrintFlow.Tests.Integration.Files;

/// <summary>SCRUM-11092 / SCRUM-11112: untrusted external evidence and bounded managed copies.</summary>
[Collection(SqliteCollection.Name)]
public sealed class ManualResultFileTests
{
    [Fact]
    public async Task Same_attempt_destination_is_never_overwritten()
    {
        using SessionServiceHarness h = new();
        string path = h.WriteSourcePng("result.png");
        var facts = (await h.FileInspector.InspectAsync(path, default)).Value;
        var session = h.FileWorkspace.CreateSession(SessionId.From(Guid.NewGuid()), h.Clock.GetUtcNow()).Value;
        var attempt = AttemptId.From(Guid.NewGuid());
        var importer = new WicManualResultImporter(h.FileWorkspace, h.FileInspector);
        var first = await importer.ImportAsync(session, attempt, StepKind.Enhancement, facts, path, default);
        first.IsSuccess.ShouldBeTrue();
        byte[] original = File.ReadAllBytes(h.FileWorkspace.ResolveAbsolute(first.Value.File));
        File.WriteAllBytes(path, SyntheticImages.Png(7, 9));
        var second = await importer.ImportAsync(session, attempt, StepKind.Enhancement, facts, path, default);
        second.IsFailure.ShouldBeTrue();
        File.ReadAllBytes(h.FileWorkspace.ResolveAbsolute(first.Value.File)).ShouldBe(original);
    }

    [Theory]
    [InlineData("jpg", true)]
    [InlineData("jpeg", true)]
    [InlineData("tif", false)]
    [InlineData("psd", false)]
    [InlineData("pdf", false)]
    public async Task Enhancement_has_a_closed_raster_format_contract(string extension, bool accepted)
    {
        using SessionServiceHarness h = new();
        string source = h.WriteSourcePng();
        var facts = (await h.FileInspector.InspectAsync(source, default)).Value;
        string path = h.Workspace.CreateSourceFile("manual." + extension, SyntheticImages.Jpeg(13, 11));
        var session = h.FileWorkspace.CreateSession(SessionId.From(Guid.NewGuid()), h.Clock.GetUtcNow()).Value;
        var result = await new WicManualResultImporter(h.FileWorkspace, h.FileInspector).ImportAsync(
            session, AttemptId.From(Guid.NewGuid()), StepKind.Enhancement, facts, path, default);
        result.IsSuccess.ShouldBe(accepted);
        if (accepted) result.Value.Facts.PixelWidth.ShouldBe(13); // Enhancement need not match source dimensions/bytes.
    }

    [Fact]
    public async Task Oversized_file_is_refused_before_copy_and_cancel_is_structured()
    {
        using SessionServiceHarness h = new();
        string path = h.WriteSourcePng();
        var facts = (await h.FileInspector.InspectAsync(path, default)).Value;
        var session = h.FileWorkspace.CreateSession(SessionId.From(Guid.NewGuid()), h.Clock.GetUtcNow()).Value;
        using (var file = new FileStream(path, FileMode.Open, FileAccess.Write)) file.SetLength(WicManualResultImporter.MaxBytes + 1);
        var importer = new WicManualResultImporter(h.FileWorkspace, h.FileInspector);
        var result = await importer.ImportAsync(session, AttemptId.From(Guid.NewGuid()), StepKind.Enhancement, facts, path, default);
        result.IsFailure.ShouldBeTrue();
        h.FileWorkspace.ListWorkingFiles(session).Value.ShouldBeEmpty();
        File.WriteAllBytes(path, SyntheticImages.Png());
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        var stopped = await importer.ImportAsync(session, AttemptId.From(Guid.NewGuid()), StepKind.Enhancement, facts, path, cancelled.Token);
        stopped.Failure.Code.ShouldBe(FailureCode.Cancelled);
    }
}
