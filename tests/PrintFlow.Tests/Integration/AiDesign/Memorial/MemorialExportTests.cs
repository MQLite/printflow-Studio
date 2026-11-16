using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using PrintFlow.Domain.AiDesign.Authoring;
using PrintFlow.Domain.AiDesign.Memorial;
using PrintFlow.Domain.Files;
using PrintFlow.Domain.Results;
using PrintFlow.Infrastructure.AiDesign.Memorial;
using PrintFlow.Infrastructure.Sqlite;
using PrintFlow.Tests.Fixtures;
using PrintFlow.Workflow.AiDesign.Memorial;

namespace PrintFlow.Tests.Integration.AiDesign.Memorial;

[Collection(SqliteCollection.Name)]
public sealed class MemorialExportTests
{
    [Fact]
    public async Task Export_copies_exact_verified_bytes_without_overwrite()
    {
        using var fixture = await Scenario.CreateAsync();
        string destination = fixture.Destination("chosen design.png");

        var exported = await fixture.Exporter().ExportAsync(fixture.Render, destination, default);

        exported.IsSuccess.ShouldBeTrue(exported.IsFailure ? exported.Failure.TechnicalDetail : "");
        exported.Value.ShouldBe(destination);
        Hash(destination).ShouldBe(fixture.Render.Facts.Sha256);
        File.ReadAllBytes(destination).LongLength.ShouldBe(fixture.Render.Facts.ByteLength);
        (await fixture.Exporter().ExportAsync(fixture.Render, destination, default)).Failure.Code.ShouldBe(FailureCode.PreconditionNotMet);
        Hash(destination).ShouldBe(fixture.Render.Facts.Sha256);
        fixture.DestinationFiles().ShouldBe([destination]);
    }

    [Fact]
    public async Task Protected_destination_and_symlink_are_refused()
    {
        using var fixture = await Scenario.CreateAsync();
        using var external = new TempWorkspace();
        string preset = Directory.CreateDirectory(Path.Combine(external.Root, "Signed Preset")).FullName;
        string baseline = Directory.CreateDirectory(Path.Combine(external.Root, "Signed Baseline")).FullName;
        string evidence = Directory.CreateDirectory(Path.Combine(external.Root, "Signed Evidence")).FullName;
        string protectedTarget = Path.Combine(preset, "new.png");
        string alias = fixture.Destination("junction");
        await LinkAsync("/J", alias, external.Root);
        try
        {
            var exporter = fixture.Exporter(protectedRoots: [preset, baseline, evidence]);
            foreach (string refused in new[] { Path.Combine(fixture.WorkspaceRoot, "new.png"), protectedTarget,
                         Path.Combine(baseline, "new.png"), Path.Combine(evidence, "new.png"), Path.Combine(alias, "new.png") })
            {
                (await exporter.ExportAsync(fixture.Render, refused, default)).Failure.Code.ShouldBe(
                    refused.StartsWith(alias + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
                        ? FailureCode.WorkspaceError : FailureCode.PreconditionNotMet);
                File.Exists(refused).ShouldBeFalse();
            }
        }
        finally { Directory.Delete(alias); }
    }

    [Fact]
    public async Task Existing_hard_link_and_its_original_keep_their_bytes()
    {
        using var fixture = await Scenario.CreateAsync();
        string original = Path.Combine(fixture.WorkspaceRoot, "signed-original.png");
        File.WriteAllBytes(original, [1, 2, 3]);
        string destination = fixture.Destination("linked.png");
        await LinkAsync("/H", destination, original);

        (await fixture.Exporter().ExportAsync(fixture.Render, destination, default)).Failure.Code.ShouldBe(FailureCode.PreconditionNotMet);

        File.ReadAllBytes(destination).ShouldBe(new byte[] { 1, 2, 3 });
        File.ReadAllBytes(original).ShouldBe(new byte[] { 1, 2, 3 });
        fixture.DestinationFiles().ShouldBe([destination]);
    }

    [Theory]
    [InlineData("unaccepted")]
    [InlineData("preview")]
    [InlineData("different-render")]
    [InlineData("forged-facts")]
    [InlineData("stale-draft")]
    public async Task Only_exact_accepted_current_design_material_is_eligible(string invalid)
    {
        using var fixture = await Scenario.CreateAsync(accept: invalid != "unaccepted",
            purpose: invalid == "preview" ? MemorialRenderPurpose.Preview : MemorialRenderPurpose.DesignMaterial);
        var expected = invalid switch
        {
            "different-render" => fixture.Render with { File = fixture.Render.File with { RenderId = Guid.NewGuid() } },
            "forged-facts" => fixture.Render with { Facts = fixture.Render.Facts with { ByteLength = 1 } },
            _ => fixture.Render
        };
        if (invalid == "stale-draft") await fixture.AdvanceDraftAsync();

        var result = await fixture.Exporter().ExportAsync(expected, fixture.Destination("chosen.png"), default);

        result.Failure.Code.ShouldBe(invalid == "stale-draft" ? FailureCode.RevisionIntegrityMismatch : FailureCode.PreconditionNotMet);
        fixture.DestinationFiles().ShouldBeEmpty();
    }

    [Theory]
    [InlineData("relative.png")]
    [InlineData("existing.png:stream.png")]
    [InlineData("NUL.png")]
    [InlineData("selected.png.")]
    [InlineData("selected.tif")]
    public async Task Destination_must_be_an_explicit_ordinary_absolute_png(string invalid)
    {
        using var fixture = await Scenario.CreateAsync();
        string sentinel = fixture.Destination("existing.png");
        File.WriteAllBytes(sentinel, [4, 5, 6]);
        string selected = invalid == "relative.png" ? invalid : fixture.Destination(invalid);

        (await fixture.Exporter().ExportAsync(fixture.Render, selected, default)).Failure.Code.ShouldBe(FailureCode.PreconditionNotMet);

        File.ReadAllBytes(sentinel).ShouldBe(new byte[] { 4, 5, 6 });
        fixture.DestinationFiles().ShouldBe([sentinel]);
    }

    [Theory]
    [InlineData("cancel")]
    [InlineData("read-error")]
    [InlineData("collision")]
    [InlineData("stale-draft")]
    [InlineData("review-revoked")]
    public async Task Copy_interruption_preserves_managed_result_and_removes_only_owned_partial(string interruption)
    {
        using var fixture = await Scenario.CreateAsync();
        using var cancellation = new CancellationTokenSource();
        string destination = fixture.Destination("chosen.png");
        string unrelated = fixture.Destination("unrelated.tmp");
        File.WriteAllBytes(unrelated, [8, 9]);
        var interrupted = new DuringCopyStore(fixture.Renders, async () =>
        {
            switch (interruption)
            {
                case "cancel": cancellation.Cancel(); break;
                case "read-error": throw new IOException("Synthetic read failure after a partial copy.");
                case "collision": File.WriteAllBytes(destination, [7]); break;
                case "stale-draft": await fixture.AdvanceDraftAsync(); break;
                case "review-revoked": await fixture.RevokeAcceptanceAsync(); break;
            }
        });

        var result = await fixture.Exporter(interrupted).ExportAsync(fixture.Render, destination, cancellation.Token);

        result.Failure.Code.ShouldBe(interruption switch
        {
            "cancel" => FailureCode.Cancelled,
            "read-error" => FailureCode.WorkspaceError,
            _ => FailureCode.PreconditionNotMet
        });
        File.ReadAllBytes(unrelated).ShouldBe(new byte[] { 8, 9 });
        if (interruption == "collision") File.ReadAllBytes(destination).ShouldBe(new byte[] { 7 });
        fixture.DestinationFiles().Order().ShouldBe((interruption == "collision" ? new[] { destination, unrelated } : [unrelated]).Order());
        var managed = await fixture.Renders.OpenVerifiedAsync(fixture.Render, default);
        managed.IsSuccess.ShouldBeTrue();
        await using Stream source = managed.Value;
        Sha256.FromBytes(await SHA256.HashDataAsync(source)).ShouldBe(fixture.Render.Facts.Sha256);
    }

    [Fact]
    public async Task Destination_ancestry_cannot_be_renamed_during_the_copy()
    {
        using var fixture = await Scenario.CreateAsync();
        string parent = Directory.CreateDirectory(fixture.Destination("parent")).FullName;
        string moved = fixture.Destination("renamed");
        string destination = Path.Combine(parent, "chosen.png");
        bool renameRefused = false;
        var interrupted = new DuringCopyStore(fixture.Renders, () =>
        {
            try { Directory.Move(parent, moved); }
            catch (IOException) { renameRefused = true; }
            catch (UnauthorizedAccessException) { renameRefused = true; }
            return Task.CompletedTask;
        });

        (await fixture.Exporter(interrupted).ExportAsync(fixture.Render, destination, default)).IsSuccess.ShouldBeTrue();

        renameRefused.ShouldBeTrue();
        Directory.Exists(moved).ShouldBeFalse();
        Hash(destination).ShouldBe(fixture.Render.Facts.Sha256);
    }

    [Fact]
    public async Task Replaced_managed_source_cannot_be_exported_with_old_acceptance()
    {
        using var fixture = await Scenario.CreateAsync();
        string managed = fixture.ManagedPngPath();
        byte[] replacement = SyntheticImages.Png(width: 7);
        File.SetAttributes(managed, FileAttributes.Normal);
        File.WriteAllBytes(managed, replacement);

        var result = await fixture.Exporter().ExportAsync(fixture.Render, fixture.Destination("chosen.png"), default);

        result.Failure.Code.ShouldBe(FailureCode.RevisionIntegrityMismatch);
        fixture.DestinationFiles().ShouldBeEmpty();
        File.ReadAllBytes(managed).ShouldBe(replacement);
    }

    [Fact]
    public async Task Review_and_export_do_not_create_production_approval()
    {
        using var fixture = await Scenario.CreateAsync(accept: false);
        var factory = new SqliteConnectionFactory(Path.Combine(fixture.WorkspaceRoot, "workspace.db"));
        await using var connection = factory.Open();
        MigrationRunner.Migrate(connection).IsSuccess.ShouldBeTrue();
        await fixture.AcceptAsync();

        string destination = fixture.Destination("design material.png");
        (await fixture.Exporter().ExportAsync(fixture.Render, destination, default)).IsSuccess.ShouldBeTrue();

        await using var query = connection.CreateCommand();
        query.CommandText = "SELECT (SELECT COUNT(*) FROM ReviewDecision) + " +
            "(SELECT COUNT(*) FROM ProcessingSession) + (SELECT COUNT(*) FROM ArtifactDelivery);";
        (await query.ExecuteScalarAsync()).ShouldBe(0L);
        Directory.Exists(Path.Combine(fixture.WorkspaceRoot, "Sessions")).ShouldBeFalse();
    }

    private static Sha256 Hash(string path) => Sha256.FromBytes(SHA256.HashData(File.ReadAllBytes(path)));

    private static async Task LinkAsync(string flag, string link, string target)
    {
        var start = new ProcessStartInfo("cmd.exe")
        {
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true
        };
        foreach (string argument in new[] { "/c", "mklink", flag, link, target }) start.ArgumentList.Add(argument);
        using var process = Process.Start(start)!;
        await process.WaitForExitAsync();
        process.ExitCode.ShouldBe(0, await process.StandardError.ReadToEndAsync());
    }

    private sealed class DuringCopyStore(IMemorialRenderStore real, Func<Task> duringCopy) : IMemorialRenderStore
    {
        public Task<OperationResult<MemorialRenderResult>> ReadAsync(MemorialRenderRef file, CancellationToken ct) => real.ReadAsync(file, ct);
        public Task<OperationResult<IReadOnlyList<MemorialRenderResult>>> ListAsync(Guid draftId, CancellationToken ct) => real.ListAsync(draftId, ct);

        public async Task<OperationResult<Stream>> OpenVerifiedAsync(MemorialRenderResult expected, CancellationToken ct)
        {
            var verified = await real.OpenVerifiedAsync(expected, ct);
            return verified.IsFailure ? verified.Failure : OperationResult.Ok<Stream>(new DuringCopyStream(verified.Value, duringCopy));
        }
    }

    private sealed class DuringCopyStream(Stream real, Func<Task> duringCopy) : Stream
    {
        private int _reads;
        public override bool CanRead => true;
        public override bool CanSeek => real.CanSeek;
        public override bool CanWrite => false;
        public override long Length => real.Length;
        public override long Position { get => real.Position; set => real.Position = value; }
        public override void Flush() => throw new NotSupportedException();
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => real.Seek(offset, origin);
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default)
        {
            if (++_reads == 2) await duringCopy();
            return await real.ReadAsync(buffer[..Math.Min(buffer.Length, 16)], ct);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) real.Dispose();
            base.Dispose(disposing);
        }
    }

    private sealed class Scenario(MemorialRenderFixture source, AiAuthoringDraft draft, MemorialRenderResult render) : IDisposable
    {
        private readonly TempWorkspace _output = new();
        private readonly FileMemorialReviewStore _reviews = new(source.Root);
        public string WorkspaceRoot => source.Root;
        public IMemorialRenderStore Renders => source.Renders;
        public MemorialRenderResult Render => render;

        public static async Task<Scenario> CreateAsync(bool accept = true, MemorialRenderPurpose purpose = MemorialRenderPurpose.DesignMaterial)
        {
            var source = new MemorialRenderFixture();
            Scenario? fixture = null;
            try
            {
                var draft = await source.CreateAsync();
                var render = await source.RenderAsync(draft, purpose);
                fixture = new(source, draft, render);
                if (accept && purpose == MemorialRenderPurpose.DesignMaterial) await fixture.AcceptAsync();
                return fixture;
            }
            catch
            {
                if (fixture is null) source.Dispose();
                else fixture.Dispose();
                throw;
            }
        }

        public async Task AcceptAsync()
        {
            var checklist = new MemorialReviewChecklist(source.Drafts, source.Renders, _reviews);
            long revision = 0;
            foreach (var item in Enum.GetValues<MemorialReviewItem>())
            {
                var checkedState = await checklist.SetCheckAsync(Render, revision, item, true, default);
                checkedState.IsSuccess.ShouldBeTrue(checkedState.IsFailure ? checkedState.Failure.TechnicalDetail : "");
                revision = checkedState.Value.Revision;
            }
            var accepted = await checklist.AcceptAsync(Render, revision, default);
            accepted.IsSuccess.ShouldBeTrue(accepted.IsFailure ? accepted.Failure.TechnicalDetail : "");
            accepted.Value.Accepted.ShouldBeTrue();
        }

        public string Destination(string name) => Path.Combine(_output.Root, name);
        public string[] DestinationFiles() => Directory.GetFiles(_output.Root);
        public string ManagedPngPath() => Directory.GetFiles(source.RenderPath(Render), "*.png").Single();
        public IMemorialMaterialExporter Exporter(IMemorialRenderStore? renders = null, string[]? protectedRoots = null) =>
            new FileMemorialMaterialExporter(renders ?? Renders, _reviews, source.Drafts, [WorkspaceRoot, .. (protectedRoots ?? [])]);

        public async Task AdvanceDraftAsync() => (await source.Drafts.SaveAsync(draft, draft.Revision, default)).IsSuccess.ShouldBeTrue();

        public async Task RevokeAcceptanceAsync()
        {
            var prior = (await _reviews.ReadAsync(Render.File, default)).Value;
            var checklist = new MemorialReviewChecklist(source.Drafts, source.Renders, _reviews);
            var changed = await checklist.SetCheckAsync(Render, prior.Revision, MemorialReviewItem.Person, false, default);
            changed.IsSuccess.ShouldBeTrue(changed.IsFailure ? changed.Failure.TechnicalDetail : "");
            changed.Value.Accepted.ShouldBeFalse();
        }

        public void Dispose()
        {
            _output.Dispose();
            source.Dispose();
        }
    }
}
