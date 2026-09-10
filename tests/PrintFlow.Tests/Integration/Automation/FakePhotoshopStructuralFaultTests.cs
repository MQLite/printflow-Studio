using System.Buffers.Binary;
using System.IO;
using System.Security.Cryptography;
using PrintFlow.Domain.Files;
using PrintFlow.Domain.Ids;
using PrintFlow.Domain.Outputs;
using PrintFlow.Domain.Results;
using PrintFlow.Infrastructure.Adapters.Fake;
using PrintFlow.Infrastructure.Adapters.Photoshop;
using PrintFlow.Tests.Fixtures;
using PrintFlow.Workflow.Ports;
using Shouldly;

namespace PrintFlow.Tests.Integration.Automation;

/// <summary>
/// The Fake Photoshop adapter's structural output vocabulary, observed through the same
/// application-facing port Production implements (SCRUM-11097).
/// </summary>
/// <remarks>
/// Every case here drives <see cref="IPhotoshopOutputProcessor.GenerateAsync"/> and reads the
/// ordinary <see cref="OperationResult{T}"/> that comes back. Nothing reaches into the fake for a
/// scripted verdict, and there is deliberately no test-only validator, session service or workflow
/// command anywhere in the file: if the assertions below could be satisfied by a double that
/// merely reported the fault it was told to report, they would be evidence about the double
/// rather than about PrintFlow.
/// <para>
/// No click order is asserted, here or anywhere else about the fake. SCRUM-11097 rules it out
/// explicitly, and the fake drives no application to have clicked anything in. What is asserted
/// is what the adapter produced and what the Product concluded about it.
/// </para>
/// <para>
/// No Photoshop process, COM server or UI automation is involved, and none of it depends on the
/// verified workstation: every case is bytes written to a temporary directory and read back.
/// </para>
/// </remarks>
public sealed class FakePhotoshopStructuralFaultTests : IDisposable
{
    /// <summary>
    /// The projected geometry every case is built from: 240 x 360 px at the fixed 300 PPI,
    /// which resolves to 20.32 x 30.48 mm.
    /// </summary>
    /// <remarks>
    /// Large enough for a one-pixel deviation to be a genuinely small proportional error rather
    /// than an obviously broken file, and small enough that nine of these files cost a few
    /// megabytes of temporary disk.
    /// </remarks>
    private const int ProjectedWidth = 240;
    private const int ProjectedHeight = 360;

    private readonly string _directory = Path.Combine(
        Path.GetTempPath(), "PrintFlow-FakePhotoshopFault-" + Guid.NewGuid().ToString("N"));

    private readonly ProductionTiffInspector _inspector = new();
    private readonly StubPhotoshopWorkspace _workspace;
    private readonly FakePhotoshopOutputProcessor _adapter;

    public FakePhotoshopStructuralFaultTests()
    {
        Directory.CreateDirectory(_directory);
        _workspace = new StubPhotoshopWorkspace(_directory);
        _adapter = new FakePhotoshopOutputProcessor(_workspace);
    }

    // -----------------------------------------------------------------------------------
    // The accepted output
    // -----------------------------------------------------------------------------------

    /// <summary>
    /// The valid case writes a file the real inspector accepts, at exactly the geometry the
    /// preparation projected.
    /// </summary>
    /// <remarks>
    /// The control for every refusal below. Without it, a test proving that eight faulty files are
    /// rejected would be equally satisfied by an adapter that rejected everything.
    /// </remarks>
    [Fact]
    public async Task A_valid_fake_TIFF_is_accepted_by_the_real_inspector_at_the_projected_geometry()
    {
        _adapter.SetTiffOutput(FakePhotoshopTiffOutput.ValidTiff);

        OperationResult<AdapterOutput> result = await GenerateAsync();

        result.IsSuccess.ShouldBeTrue(result.IsFailure ? result.Failure.ToString() : string.Empty);

        OperationResult<ProductionTiffFacts> facts = _inspector.Inspect(OutputPath);
        facts.IsSuccess.ShouldBeTrue(facts.IsFailure ? facts.Failure.ToString() : string.Empty);
        facts.Value.PixelWidth.ShouldBe(ProjectedWidth);
        facts.Value.PixelHeight.ShouldBe(ProjectedHeight);
        facts.Value.XResolutionDpi.ShouldBe(300);
        facts.Value.YResolutionDpi.ShouldBe(300);
        facts.Value.ExtraChannelNames.ShouldBe(["W1"]);
        facts.Value.W1NonWhiteSampleCount.ShouldBe((long)ProjectedWidth * ProjectedHeight);

        // Still says what it is. A fake note that read like a production one would be the single
        // most misleading thing this adapter could write, because the note is what a Revision's
        // audit line is built from.
        string notes = result.Value.AdapterNotes.ShouldNotBeNull();
        notes.ShouldContain("fake");
        notes.ShouldContain("no Photoshop ran");
    }

    // -----------------------------------------------------------------------------------
    // The structural faults
    // -----------------------------------------------------------------------------------

    /// <summary>
    /// Every named structural fault produces a real file that the Product then refuses, with the
    /// refusal naming the fact that is actually wrong.
    /// </summary>
    /// <remarks>
    /// The detail fragment is asserted so a case cannot pass on the strength of the wrong
    /// refusal: a wrong-colour-mode file rejected for its dimensions would mean the fake wrote
    /// something other than what it claims, and a matrix built on that would be fiction.
    /// </remarks>
    /// <remarks>
    /// The two geometry faults are absent on purpose: they have their own theory below, which
    /// makes every assertion this one does and adds the per-axis evidence. Repeating them here
    /// would be a fourth and fifth run of the same scenario.
    /// </remarks>
    [Theory]
    [InlineData(FakePhotoshopTiffOutput.MissingWhiteChannel, "five 8-bit samples")]
    [InlineData(FakePhotoshopTiffOutput.EmptyWhiteChannel, "wholly empty")]
    [InlineData(FakePhotoshopTiffOutput.WrongColourMode, "separated CMYK")]
    [InlineData(FakePhotoshopTiffOutput.IncorrectDpi, "300 pixels per inch")]
    [InlineData(FakePhotoshopTiffOutput.IncorrectMetadata, "compression is not None")]
    [InlineData(FakePhotoshopTiffOutput.IncorrectChannelMetadata, "exactly one W1 channel")]
    public async Task A_structural_fault_produces_a_real_file_that_the_Product_refuses(
        FakePhotoshopTiffOutput output, string expectedDetail)
    {
        _adapter.SetTiffOutput(output);

        OperationResult<AdapterOutput> result = await GenerateAsync();

        result.IsFailure.ShouldBeTrue($"{output} was accepted.");
        result.Failure.Code.ShouldBe(FailureCode.OutputValidationFailed);
        result.Failure.TechnicalDetail.ShouldContain(expectedDetail, Case.Insensitive);

        // The file genuinely exists and genuinely has substance. A refusal reached by writing
        // nothing would be the missing-output case wearing a structural label.
        File.Exists(OutputPath).ShouldBeTrue();
        new FileInfo(OutputPath).Length.ShouldBeGreaterThan(1024);
    }

    /// <summary>
    /// Both axes are compared. A width-only comparison would pass every test above except this
    /// one.
    /// </summary>
    /// <remarks>
    /// Stated as its own two-case assertion rather than trusted to the theory, because the theory
    /// shares one expected fragment for both axes and would still pass if the height case were
    /// rejected for some other reason entirely.
    /// </remarks>
    [Theory]
    [InlineData(FakePhotoshopTiffOutput.WrongPixelWidth, ProjectedWidth + 1, ProjectedHeight, "20.4047x30.48 mm")]
    [InlineData(FakePhotoshopTiffOutput.WrongPixelHeight, ProjectedWidth, ProjectedHeight + 1, "20.32x30.5647 mm")]
    public async Task A_saved_TIFF_that_disagrees_on_either_axis_is_refused_against_the_preparation(
        FakePhotoshopTiffOutput output, int savedWidth, int savedHeight, string actualMillimetres)
    {
        _adapter.SetTiffOutput(output);

        OperationResult<AdapterOutput> result = await GenerateAsync();

        result.IsFailure.ShouldBeTrue();
        result.Failure.Code.ShouldBe(FailureCode.OutputValidationFailed);
        result.Failure.TechnicalDetail.ShouldContain("projected dimensions", Case.Insensitive);

        File.Exists(OutputPath).ShouldBeTrue();
        new FileInfo(OutputPath).Length.ShouldBeGreaterThan(1024);

        result.Failure.Context["expectedPixels"].ShouldBe($"{ProjectedWidth}x{ProjectedHeight}");
        result.Failure.Context["actualPixels"].ShouldBe($"{savedWidth}x{savedHeight}");
        result.Failure.Context["revisionCreated"].ShouldBe("false");

        // The physical clause, per axis: the same disagreement stated in millimetres.
        result.Failure.Context["expectedMillimetres"].ShouldBe("20.32x30.48 mm");
        result.Failure.Context["actualMillimetres"].ShouldBe(actualMillimetres);
    }

    /// <summary>
    /// The wrong-dimension file is a real, reopenable, otherwise entirely accepted production
    /// TIFF: only its pixel grid disagrees with the preparation.
    /// </summary>
    /// <remarks>
    /// This is the assertion that makes the wrong-dimension case worth having. PrintFlow already
    /// refuses files that are not production TIFFs at all, and a "wrong dimensions" scenario that
    /// produced a broken file would have been caught by that older check while proving nothing
    /// about dimensions. So the file is proven here to pass every absolute check the inspector
    /// makes — it exists, it reopens, it is separated CMYK, it carries a non-empty W1 spot
    /// channel, it is exactly 300 PPI — and to be refused solely because its bytes disagree with
    /// the geometry the workflow validated.
    /// <para>
    /// It is also why the check cannot live only in the production adapter's pre-save read-back
    /// of the Photoshop document. That read-back proves what Photoshop believes it is holding; it
    /// cannot prove anything about what was written to disk afterwards.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task The_wrong_dimension_TIFF_is_otherwise_a_fully_accepted_production_file()
    {
        _adapter.SetTiffOutput(FakePhotoshopTiffOutput.WrongPixelWidth);

        OperationResult<AdapterOutput> result = await GenerateAsync();
        result.IsFailure.ShouldBeTrue();

        OperationResult<ProductionTiffFacts> facts = _inspector.Inspect(OutputPath);
        facts.IsSuccess.ShouldBeTrue(
            "the inspector's absolute contract must be satisfied, or the refusal above proves " +
            "nothing about dimensions: " +
            (facts.IsFailure ? facts.Failure.ToString() : string.Empty));

        facts.Value.PhotometricInterpretation.ShouldBe((ushort)5);
        facts.Value.SamplesPerPixel.ShouldBe((ushort)5);
        facts.Value.Compression.ShouldBe((ushort)1);
        facts.Value.ExtraChannelNames.ShouldBe(["W1"]);
        facts.Value.W1IsPhotoshopSpotChannel.ShouldBeTrue();
        facts.Value.W1NonWhiteSampleCount.ShouldBeGreaterThan(0);
        facts.Value.XResolutionDpi.ShouldBe(300);
        facts.Value.YResolutionDpi.ShouldBe(300);

        // The one disagreement, and it is with the preparation rather than with the contract.
        facts.Value.PixelHeight.ShouldBe(ProjectedHeight);
        facts.Value.PixelWidth.ShouldBe(ProjectedWidth + 1);
    }

    // -----------------------------------------------------------------------------------
    // Physical canvas
    // -----------------------------------------------------------------------------------

    /// <summary>
    /// The wrong-DPI file has an exactly correct pixel grid, and is still the wrong physical
    /// print — which is what makes "incorrect DPI" a different clause from "wrong pixel
    /// dimensions" rather than the same test under another name.
    /// </summary>
    /// <remarks>
    /// PrintFlow has no third, independent physical-size check, and this test is where that fact
    /// is recorded rather than left to be rediscovered. At the fixed 300 PPI the pixel grid and
    /// the resolution together <i>define</i> the canvas — one conversion,
    /// <see cref="PrintDimensions.MillimetresFromPixels"/> — so a file whose pixels match the
    /// preparation and whose resolution is exactly 300 PPI cannot have a wrong physical size, and
    /// a file failing either check already has one. Adding a separate physical assertion would not
    /// catch a further kind of defect; it would restate one of these two in millimetres and give a
    /// reader two numbers to reconcile.
    /// <para>
    /// The two clauses are answered by different authorities, and that is asserted here rather
    /// than assumed. A wrong grid is relative and is refused by
    /// <c>ProductionTiffPreparationMatch</c>, whose refusal carries the millimetres (see the
    /// per-axis theory above). A wrong resolution is absolute and is refused by
    /// <c>ProductionTiffInspector</c> before any preparation is consulted, so its refusal
    /// deliberately carries no millimetres — there is no expected canvas in scope at that point.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task An_exactly_correct_grid_at_the_wrong_resolution_is_still_the_wrong_print()
    {
        PrintDimensions.MillimetresFromPixels(ProjectedWidth).ShouldBe(20.32, tolerance: 0.0001);
        PrintDimensions.MillimetresFromPixels(ProjectedHeight).ShouldBe(30.48, tolerance: 0.0001);

        _adapter.SetTiffOutput(FakePhotoshopTiffOutput.IncorrectDpi);
        OperationResult<AdapterOutput> halfResolution = await GenerateAsync();

        halfResolution.IsFailure.ShouldBeTrue();
        halfResolution.Failure.Code.ShouldBe(FailureCode.OutputValidationFailed);
        halfResolution.Failure.TechnicalDetail.ShouldContain("300 pixels per inch", Case.Insensitive);

        // Refused by the absolute contract, not by the preparation comparison.
        halfResolution.Failure.Context.ShouldNotContainKey("expectedMillimetres");
        halfResolution.Failure.Context.ShouldNotContainKey("actualPixels");

        // And the grid really is right: half the resolution over the correct grid is twice the
        // intended canvas, which is the whole point of the case.
        ReadPixelGrid(OutputPath).ShouldBe((ProjectedWidth, ProjectedHeight));
    }

    // -----------------------------------------------------------------------------------
    // What must not change
    // -----------------------------------------------------------------------------------

    /// <summary>
    /// The default output class is untouched: a fake success still copies the approved input.
    /// </summary>
    /// <remarks>
    /// Dozens of workflow tests depend on a Fake success being a cheap byte-for-byte copy, and a
    /// new structural vocabulary that quietly changed the default would alter what all of them
    /// exercise while leaving them green.
    /// </remarks>
    [Fact]
    public async Task The_default_output_class_still_copies_the_approved_input()
    {
        OperationResult<AdapterOutput> result = await GenerateAsync();

        result.IsSuccess.ShouldBeTrue(result.IsFailure ? result.Failure.ToString() : string.Empty);
        Hash(OutputPath).ShouldBe(Hash(InputPath));
        _inspector.Inspect(OutputPath).IsFailure.ShouldBeTrue(
            "the default copy is a PNG named .tif and was never a production TIFF");
    }

    /// <summary>The same scripted output written twice is the same bytes.</summary>
    /// <remarks>
    /// SCRUM-11097 asks for a deterministic adapter, and a structural fault built from a
    /// timestamp, a GUID or a random sample would make a hash-bound review assertion flaky in a
    /// way that only shows up on someone else's machine.
    /// </remarks>
    [Fact]
    public async Task The_same_scripted_output_is_byte_identical_across_calls()
    {
        _adapter.SetTiffOutput(FakePhotoshopTiffOutput.ValidTiff);

        (await GenerateAsync()).IsSuccess.ShouldBeTrue();
        Sha256 first = Hash(OutputPath);

        (await GenerateAsync()).IsSuccess.ShouldBeTrue();
        Hash(OutputPath).ShouldBe(first);
    }

    /// <summary>
    /// A behavioural scenario that produces no file ignores the output class entirely.
    /// </summary>
    /// <remarks>
    /// The two halves of the vocabulary are independent, and this is the case that would expose
    /// them being accidentally coupled: an export failure has no output class, and a fake that
    /// wrote a TIFF anyway before failing would leave a file behind that no attempt claims.
    /// </remarks>
    [Fact]
    public async Task An_export_failure_writes_nothing_whatever_the_output_class_says()
    {
        _adapter.SetTiffOutput(FakePhotoshopTiffOutput.ValidTiff);
        _adapter.SetScenario(FakeAdapterScenario.FailWith(FailureCode.OutputValidationFailed));

        OperationResult<AdapterOutput> result = await GenerateAsync();

        result.IsFailure.ShouldBeTrue();
        File.Exists(OutputPath).ShouldBeFalse();
    }

    /// <summary>The adapter still declares itself a fake.</summary>
    [Fact]
    public void The_structural_vocabulary_does_not_make_the_fake_claim_to_be_production()
    {
        _adapter.Mode.ShouldBe(AdapterExecutionMode.Fake);
        _adapter.AdapterId.ShouldBe("fake-photoshop-v1");
    }

    // -----------------------------------------------------------------------------------

    private Task<OperationResult<AdapterOutput>> GenerateAsync() =>
        _adapter.GenerateAsync(Request(), CancellationToken.None);

    /// <summary>
    /// A request built the way <c>SessionService</c> builds one, from the single domain
    /// preparation authority.
    /// </summary>
    /// <remarks>
    /// The source is 240 x 360 px, which at 300 PPI is well inside the 200 x 150 mm bounds, so
    /// the plan is a resolution-only one and the projected pixels are the source's own. That
    /// makes the expected geometry a stated fact of the fixture rather than the outcome of a fit
    /// calculation this file would then be quietly asserting.
    /// </remarks>
    private PhotoshopRequest Request()
    {
        PrintDimensions dimensions = PrintDimensions.FromMillimetres(200, 150, SizePreset.Custom);
        FitWithinBoundsPreparation preparation = new(PrintPreparationPlan.For(
            RevisionId.From(Guid.Parse("22222222-2222-2222-2222-222222222222")),
            Hash(InputPath),
            sourcePixelWidth: ProjectedWidth,
            sourcePixelHeight: ProjectedHeight,
            dimensions));

        preparation.ProjectedPixelWidth.ShouldBe(ProjectedWidth);
        preparation.ProjectedPixelHeight.ShouldBe(ProjectedHeight);

        return new PhotoshopRequest(
            Input,
            dimensions,
            preparation,
            new ProductionPresetRef("printflow-workstation-v1", "1.14.0", Sha256.Parse(new string('0', 64))),
            WhiteUnderbaseBranch.W1_1px,
            Output.FileName,
            WorkspaceDirRef.Create("Sessions/S1/Working/A_1"),
            Output);
    }

    private static WorkspaceFileRef Input =>
        WorkspaceFileRef.Create("Sessions/S1/Working/A_1/approved.png", WorkspaceArea.Working);

    private static WorkspaceFileRef Output =>
        WorkspaceFileRef.Create("Sessions/S1/Working/A_1/PRINT_200mm_CMYK_W.tif", WorkspaceArea.Working);

    private string InputPath => EnsureInput();

    private string OutputPath => _workspace.ResolveAbsolute(Output);

    /// <summary>The approved input the default output class copies, written once.</summary>
    private string EnsureInput()
    {
        string path = _workspace.ResolveAbsolute(Input);
        if (!File.Exists(path))
        {
            File.WriteAllBytes(path, SyntheticImages.Png(4, 4, alpha: true));
        }

        return path;
    }

    private static Sha256 Hash(string path)
    {
        using FileStream stream = File.OpenRead(path);
        return Sha256.FromBytes(SHA256.HashData(stream));
    }

    /// <summary>
    /// Reads ImageWidth and ImageLength straight out of the first IFD.
    /// </summary>
    /// <remarks>
    /// Deliberately not <c>ProductionTiffInspector</c>: the file this is used on is one the
    /// inspector refuses, and the assertion is precisely that its geometry is nevertheless
    /// correct. Nine lines of tag reading is the honest way to state that; asking the validator
    /// would return a failure and no facts.
    /// </remarks>
    private static (int Width, int Height) ReadPixelGrid(string path)
    {
        byte[] bytes = File.ReadAllBytes(path);
        ushort entries = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(8));
        int width = 0;
        int height = 0;

        for (int index = 0; index < entries; index++)
        {
            int entry = 10 + (index * 12);
            ushort tag = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(entry));
            int value = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(entry + 8)));
            if (tag == 256) width = value;
            if (tag == 257) height = value;
        }

        return (width, height);
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
        GC.SuppressFinalize(this);
    }
}
