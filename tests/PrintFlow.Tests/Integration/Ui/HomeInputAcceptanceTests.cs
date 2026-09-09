using System.IO;
using PrintFlow.App.Resources;
using PrintFlow.App.ViewModels;
using PrintFlow.Domain.Files;
using PrintFlow.Domain.Results;
using PrintFlow.Domain.Sessions;
using PrintFlow.Tests.Fixtures;
using PrintFlow.Workflow.Services;

namespace PrintFlow.Tests.Integration.Ui;

/// <summary>
/// What Home accepts, what it refuses, and whether it says so truthfully (SCRUM-11116;
/// Jira 11201, 11601; MVP design §9.2).
/// </summary>
/// <remarks>
/// The defect this closes was not a missing message — it was a missing <i>distinction</i>. The
/// import path accepted any bytes it was handed, so a text file started a session that failed
/// several steps later, and the one refusal sentence Home had said "could not be imported" to
/// every cause alike. Two files can fail for opposite reasons: a TIFF is a perfectly good image
/// this product has no production path for, and a damaged PNG is a file it would happily process
/// if it could be read. Calling the second one unsupported sends the operator off to convert a
/// file that is already the right format.
/// <para>
/// Deliberately short. One genuinely unsupported input, one positively identified but unaccepted
/// container, one accepted-but-unreadable file, the two prepared formats that must not regress,
/// the multiple-file rule, and the source guarantees. A case per format per failure would mostly
/// re-test <c>FormatSniffer</c>, which has its own suite.
/// </para>
/// </remarks>
[Collection(SqliteCollection.Name)]
public sealed class HomeInputAcceptanceTests
{
    /// <summary>
    /// A file that is not any container this product recognises is refused, by name (§11601).
    /// </summary>
    /// <remarks>
    /// The assertion about the <i>absence</i> of a session is the load-bearing half. Before this
    /// slice a text file was imported successfully, the operator was sent to Workflow Selection,
    /// and the truth arrived several steps later as an adapter failure.
    /// </remarks>
    [Fact]
    public async Task An_unrecognised_file_is_refused_with_its_own_name_and_the_accepted_formats()
    {
        using HomeScreenHarness harness = new();
        string source = harness.Inner.Workspace.CreateSourceFile(
            "customer-notes.txt", SyntheticImages.KnownVectorAbc());
        byte[] before = File.ReadAllBytes(source);

        await harness.Home.DropFilesCommand.ExecuteAsync(new[] { source });

        harness.Navigation.WorkflowSelectionFor.ShouldBeNull();
        harness.Home.Notice.ShouldNotBeNullOrWhiteSpace();
        harness.Home.Notice!.ShouldContain("customer-notes.txt");
        harness.Home.Notice.ShouldContain(nameof(FailureCode.SourceFormatUnsupported));

        // The accepted list is quoted from the Product authority, not from this screen.
        foreach (ImageFormat accepted in SupportedInputFormats.All)
        {
            harness.Home.Notice.ShouldContain(DisplayNames.ImageFormat(accepted));
        }

        // Nothing was produced from it, and the operator's own file is untouched.
        SessionAggregate? aggregate = await SoleSessionAsync(harness);
        aggregate.ShouldNotBeNull("a refused import is still recorded as a failed attempt");
        aggregate!.Revisions.ShouldBeEmpty();
        aggregate.Snapshot.ShouldBeNull();
        File.ReadAllBytes(source).ShouldBe(before);
    }

    /// <summary>
    /// A TIFF is refused as a TIFF: the product has no input path for one (MVP design §9.2).
    /// </summary>
    /// <remarks>
    /// TIFF is an <i>output</i> of PrintFlow Studio. Jira 11405, 11406 and 11407 define input
    /// paths for PNG/JPEG, PSD and single-page PDF and for nothing else, so a TIFF import used to
    /// create a session that could only fail at the Photoshop step. Saying which container it
    /// actually is matters because the file dialog no longer offers <c>.tif</c> at all — an
    /// operator reaching this message dropped the file, and needs to know what PrintFlow saw.
    /// </remarks>
    [Fact]
    public async Task A_positively_identified_but_unaccepted_container_is_named_in_the_refusal()
    {
        using HomeScreenHarness harness = new();
        string source = harness.Inner.Workspace.CreateSourceFile("scan.tif", SyntheticImages.Tiff(8, 6));

        await harness.Home.DropFilesCommand.ExecuteAsync(new[] { source });

        harness.Navigation.WorkflowSelectionFor.ShouldBeNull();
        harness.Home.Notice.ShouldNotBeNullOrWhiteSpace();
        harness.Home.Notice!.ShouldContain(Strings.Format_Tiff);
        harness.Home.Notice.ShouldContain(nameof(FailureCode.SourceFormatUnsupported));

        // The dialog and the gate agree: the filter offers exactly the accepted formats.
        Strings.Home_ImportFilter.ShouldNotContain(".tif");
        Strings.Home_ImportFilter.ShouldContain(".png");
        Strings.Home_ImportFilter.ShouldContain(".psd");
        Strings.Home_ImportFilter.ShouldContain(".pdf");
    }

    /// <summary>
    /// A damaged PNG is reported as damaged, never as unsupported (§11601).
    /// </summary>
    /// <remarks>
    /// The exact requirement: "a corrupted PNG must not be described as an unsupported PNG". The
    /// two assertions that matter are the code — <see cref="FailureCode.SourceImageUnreadable"/>
    /// and not <see cref="FailureCode.SourceFormatUnsupported"/> — and the sentence, which names
    /// PNG as what the file <i>is</i> rather than as something to convert away from.
    /// </remarks>
    [Fact]
    public async Task A_supported_container_carrying_no_readable_image_is_not_called_unsupported()
    {
        using HomeScreenHarness harness = new();

        // A genuine PNG signature with nothing decodable behind it: the file the operator sees
        // is a .png, and it really is one, and there is no image in it.
        byte[] damaged = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, .. new byte[64]];
        string source = harness.Inner.Workspace.CreateSourceFile("damaged.png", damaged);

        await harness.Home.DropFilesCommand.ExecuteAsync(new[] { source });

        harness.Navigation.WorkflowSelectionFor.ShouldBeNull();
        harness.Home.Notice.ShouldNotBeNullOrWhiteSpace();
        harness.Home.Notice!.ShouldContain(nameof(FailureCode.SourceImageUnreadable));
        harness.Home.Notice.ShouldNotContain(nameof(FailureCode.SourceFormatUnsupported));
        harness.Home.Notice.ShouldContain("damaged.png");
        harness.Home.Notice.ShouldContain(Strings.Format_Png);

        // And the file is still a PNG as far as the product is concerned — the refusal is about
        // its contents, which is exactly what the operator has to be told.
        SessionAggregate aggregate = (await SoleSessionAsync(harness))!;
        aggregate.Attempts.Single().Failure!.Code.ShouldBe(FailureCode.SourceImageUnreadable);
    }

    /// <summary>
    /// PSD and single-page PDF are accepted, exactly as their preparation slices established
    /// (Jira 11406, 11407).
    /// </summary>
    /// <remarks>
    /// The regression this guards is the obvious way to get the new gate wrong: a rule written
    /// from "what can the preview decoder draw" rather than from "what does this product have a
    /// production path for" would refuse both, because neither carries pixel metadata at import.
    /// </remarks>
    [Theory]
    [InlineData("design.psd", ImageFormat.Psd)]
    [InlineData("design.pdf", ImageFormat.Pdf)]
    public async Task A_prepared_format_is_imported_rather_than_refused_as_unsupported(
        string fileName, ImageFormat expected)
    {
        using HomeScreenHarness harness = new();
        byte[] bytes = expected == ImageFormat.Psd
            ? PsdInputPreparationTests.RgbCompositePsd()
            : PdfFixtures.Read("single");
        string source = harness.Inner.Workspace.CreateSourceFile(fileName, bytes);

        harness.FilePicker.Path = source;
        await harness.Home.ChooseFileCommand.ExecuteAsync(null);

        harness.Home.Notice.ShouldBeNull();
        SessionView imported = harness.Navigation.WorkflowSelectionFor.ShouldNotBeNull();

        SessionAggregate stored =
            (await harness.Inner.Repository.LoadAsync(imported.Id, CancellationToken.None)).Value!;
        stored.Revisions.Single().Facts.Format.ShouldBe(expected);

        // Source immutability and the InputSnapshot are exactly as they were.
        stored.Snapshot.ShouldNotBeNull();
        stored.Snapshot!.OriginalFileName.ShouldBe(fileName);
        File.ReadAllBytes(source).ShouldBe(bytes);
    }

    /// <summary>
    /// The exactly-one-image rule is unchanged: several files are still refused with a count,
    /// and the acceptance gate never sees them (§11601).
    /// </summary>
    [Fact]
    public async Task Several_dropped_files_are_still_refused_before_any_file_is_examined()
    {
        using HomeScreenHarness harness = new();
        string first = harness.WriteSourceFile("first.png");
        string second = harness.Inner.Workspace.CreateSourceFile("second.txt", SyntheticImages.KnownVectorAbc());

        await harness.Home.DropFilesCommand.ExecuteAsync(new[] { first, second });

        harness.Home.Notice.ShouldNotBeNullOrWhiteSpace();
        harness.Home.Notice!.ShouldContain("2");
        // Several files is a rule about the drop, not a verdict on any one file.
        harness.Home.Notice.ShouldNotContain(nameof(FailureCode.SourceFormatUnsupported));
        harness.Navigation.WorkflowSelectionFor.ShouldBeNull();
        (await harness.Sessions.ListRecentAsync(CancellationToken.None)).Value.ShouldBeEmpty();
    }

    /// <summary>
    /// An accepted file still produces exactly the source guarantees it always did (Jira 11201).
    /// </summary>
    /// <remarks>
    /// The gate sits after the managed copy is established, so it must not have changed what a
    /// successful import records: one root Revision bound to the copied bytes by hash, one
    /// InputSnapshot naming the operator's own file, and the operator's file untouched.
    /// </remarks>
    [Fact]
    public async Task An_accepted_file_still_records_its_snapshot_hash_and_untouched_source()
    {
        using HomeScreenHarness harness = new();
        string source = harness.WriteSourceFile("accepted.png");
        byte[] before = File.ReadAllBytes(source);

        harness.FilePicker.Path = source;
        await harness.Home.ChooseFileCommand.ExecuteAsync(null);

        SessionView imported = harness.Navigation.WorkflowSelectionFor.ShouldNotBeNull();
        SessionAggregate stored =
            (await harness.Inner.Repository.LoadAsync(imported.Id, CancellationToken.None)).Value!;

        stored.Snapshot.ShouldNotBeNull();
        stored.Snapshot!.OriginalSourcePath.ShouldBe(source);
        stored.Snapshot.OriginalFileName.ShouldBe("accepted.png");

        Domain.Revisions.Revision root = stored.Revisions.Single();
        root.IsRoot.ShouldBeTrue();
        root.Facts.Format.ShouldBe(ImageFormat.Png);
        stored.Snapshot.RootRevisionId.ShouldBe(root.Id);

        string managed = harness.Inner.FileWorkspace.ResolveAbsolute(root.File);
        File.ReadAllBytes(managed).ShouldBe(before);
        File.ReadAllBytes(source).ShouldBe(before, "the customer's own file is never written to");
    }

    private static async Task<SessionAggregate?> SoleSessionAsync(HomeScreenHarness harness)
    {
        OperationResult<IReadOnlyList<SessionListItem>> listed =
            await harness.Sessions.ListRecentAsync(CancellationToken.None);
        return listed.Value.Count == 0
            ? null
            : (await harness.Inner.Repository.LoadAsync(listed.Value.Single().Id, CancellationToken.None)).Value;
    }
}
