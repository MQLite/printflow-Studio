using System.Globalization;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using PrintFlow.Domain.Files;
using PrintFlow.Domain.Ids;
using PrintFlow.Domain.Outputs;
using PrintFlow.Domain.Results;
using PrintFlow.Infrastructure.Adapters.Photoshop;
using PrintFlow.Infrastructure.Automation;
using PrintFlow.Infrastructure.Configuration;
using PrintFlow.Infrastructure.Workspace;
using PrintFlow.Tests.Fixtures;
using Xunit.Abstractions;

namespace PrintFlow.Tests.Smoke;

/// <summary>
/// SCRUM-11101-A Gate F. Proves that the Regenerate branch needs no new output logic: once the
/// supported existing W1 is removed from a managed copy, the document is in exactly the state the
/// accepted production path already demands, and that path then runs unchanged.
/// </summary>
/// <remarks>
/// This is the branch the architectural determination expected to be completable first, and the
/// reason is structural rather than optimistic. <c>GuardedPhotoshopDocumentPreparer</c> requires
/// RGB/8 with three component channels and refuses any spot channel outright;
/// <c>GuardedPhotoshopW1Executor</c> requires the same and additionally that no W1 exists. Removing
/// the one supported W1 produces precisely that state, so there is nothing left for Regenerate to
/// redesign — it is the accepted path with one deletion in front of it.
/// <para>
/// The removal itself is exercised here as a fixed test-owned program rather than as Product code.
/// SCRUM-11101-A is a feasibility gate and §17 requires ordinary Product behaviour to remain the
/// safe refusal, so nothing here is reachable from <c>SessionService</c>, no workflow command
/// exists and no operator can choose anything.
/// </para>
/// <para>
/// Opt in with <c>PRINTFLOW_REGENERATE_REUSE_PROBE=1</c>.
/// </para>
/// </remarks>
public sealed class ExistingWhiteInkRegenerateReuseProbe(ITestOutputHelper output)
{
    private const string EnableVariable = "PRINTFLOW_REGENERATE_REUSE_PROBE";

    [Fact]
    public async Task Removing_the_supported_W1_lets_the_accepted_production_path_run_unchanged()
    {
        if (Environment.GetEnvironmentVariable(EnableVariable) != "1")
        {
            return;
        }

        PrintFlowConfiguration configuration =
            PrintFlowConfiguration.LoadFromFile(RepositoryFile("appsettings.json"));
        configuration.Adapters.Mode.ShouldBe("Production");

        string token = DateTimeOffset.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture) +
                       "-" + Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();
        string root = Path.Combine(configuration.Workspace.Root, "QA", "Scrum11101F", token);
        FileWorkspace workspace = new(root);
        IPhotoshopTiffAutomation automation = PhotoshopAutomationComposition.CreateTiffAutomation(
            Path.Combine(configuration.Workspace.Root, configuration.Preset.Path),
            Sha256.Parse(configuration.Preset.ExpectedSha256), workspace,
            Path.Combine(root, "Evidence"), TimeProvider.System);
        string installDirectory = Path.GetDirectoryName(Path.GetFullPath(
            PresetExecutablePath(configuration)))!;

        byte[] synthetic = SpotChannelPsdFixtures.QuadrantW1Psd();
        WorkspaceFileRef carrier = WorkspaceFileRef.Create(
            $"Sessions/S_11101F/Working/A_{token}/PF_11101F_REGENERATE.psd", WorkspaceArea.Working);
        string carrierPath = workspace.ResolveAbsolute(carrier);
        Directory.CreateDirectory(Path.GetDirectoryName(carrierPath)!);
        File.WriteAllBytes(carrierPath, synthetic);
        Sha256 carrierSha = Sha256.FromBytes(SHA256.HashData(synthetic));

        output.WriteLine($"controlled workspace : {root}");
        output.WriteLine($"managed copy         : {carrier.RelativePath}");
        output.WriteLine($"managed copy SHA     : {carrierSha}");
        output.WriteLine(string.Empty);

        var opened = await automation.OpenManagedWorkingFileAsync(carrier, CancellationToken.None);
        opened.IsSuccess.ShouldBeTrue(opened.IsFailure ? opened.Failure.ToString() : "");

        RemovalOutcome removal = RunRemoval(
            opened.Value.Identity.ObservedFullPath, installDirectory);
        output.WriteLine($"removal             : {(removal.Succeeded ? "PERFORMED" : "REFUSED")} — {removal.Detail}");
        output.WriteLine($"  before            : {removal.Before}");
        output.WriteLine($"  after             : {removal.After}");
        removal.Succeeded.ShouldBeTrue(removal.Detail);

        // The post-condition is the whole point: this is verbatim what the accepted preparer and
        // W1 executor already require of a document before they will touch it.
        removal.After.ShouldBe("RGB/8 | components=3 | spots=0 | w1=no | 400x300");
        output.WriteLine(string.Empty);

        // ---------------------------------------------------------------------------------
        // The accepted production path, driven exactly as PhotoshopTiffWorkstationSmoke drives
        // it. Nothing below this line knows a W1 was ever removed.
        // ---------------------------------------------------------------------------------
        PrintDimensions dimensions = PrintDimensions.FromMillimetres(50, 40, SizePreset.Custom);
        FitWithinBoundsPreparation preparation = new(PrintPreparationPlan.For(
            new RevisionId(Guid.CreateVersion7()), carrierSha,
            SpotChannelPsdFixtures.Width, SpotChannelPsdFixtures.Height, dimensions));

        var prepared = await automation.PrepareDocumentAsync(
            opened.Value, preparation, CancellationToken.None);
        if (prepared.IsFailure) output.WriteLine($"prepare failure     : {prepared.Failure}");
        prepared.IsSuccess.ShouldBeTrue(prepared.IsFailure ? prepared.Failure.ToString() : "");
        output.WriteLine($"B1A.3 preparation   : {prepared.Value.Actual.PixelWidth}x" +
                         $"{prepared.Value.Actual.PixelHeight} @ {prepared.Value.Actual.ResolutionPpi} PPI, " +
                         $"{prepared.Value.Actual.ColourMode}");

        var w1 = await automation.ExecuteW1Async(
            opened.Value, prepared.Value, WhiteUnderbaseBranch.W1_1px, CancellationToken.None);
        if (w1.IsFailure) output.WriteLine($"W1 failure          : {w1.Failure}");
        w1.IsSuccess.ShouldBeTrue(w1.IsFailure ? w1.Failure.ToString() : "");
        var generated = w1.Value.Provenance.ShouldBeOfType<PhotoshopWhiteInkProvenance.Generated>();
        output.WriteLine($"signed W1 Action    : {generated.ActionSetName} / {generated.ActionName}");
        output.WriteLine($"generated W1        : {w1.Value.W1.Name}/{w1.Value.W1.Type}, " +
                         $"nonWhite={w1.Value.W1.NonWhitePixelCount}");

        WorkspaceFileRef tiff = WorkspaceFileRef.Create(
            carrier.RelativePath[..^carrier.FileName.Length] + $"PRINT_50mm_CMYK_W_{token}.tif",
            WorkspaceArea.Working);
        var saved = await automation.SaveProductionTiffAsync(
            opened.Value, w1.Value, tiff, CancellationToken.None);
        if (saved.IsFailure) output.WriteLine($"TIFF failure        : {saved.Failure}");
        saved.IsSuccess.ShouldBeTrue(saved.IsFailure ? saved.Failure.ToString() : "");

        ProductionTiffFacts facts = saved.Value.Facts;
        output.WriteLine($"TIFF                : {facts.PixelWidth}x{facts.PixelHeight} @ " +
                         $"{facts.XResolutionDpi:R} DPI, samples={facts.SamplesPerPixel}, " +
                         $"names={string.Join(',', facts.ExtraChannelNames)}, " +
                         $"spot={facts.W1IsPhotoshopSpotChannel}, nonWhite={facts.W1NonWhiteSampleCount}");

        facts.SamplesPerPixel.ShouldBe((ushort)5);
        facts.ExtraSamples.ShouldBe([(ushort)0]);
        facts.ExtraChannelNames.ShouldBe(["W1"]);
        facts.W1IsPhotoshopSpotChannel.ShouldBeTrue();
        facts.XResolutionDpi.ShouldBe(300, 0.0001);
        saved.Value.SaveSettings.ShouldBe(PhotoshopProductionTiffSaveSettings.Accepted);

        // The generated underbase is derived from the opaque composite, so it covers essentially
        // the whole canvas. That is what separates this branch from Retain, whose synthetic
        // carrier deliberately leaves one quadrant with no ink at all.
        double coverage = (double)facts.W1NonWhiteSampleCount / ((long)facts.PixelWidth * facts.PixelHeight);
        output.WriteLine($"W1 coverage         : {coverage:P2} (generated underbase, not the retained ~75%)");
        coverage.ShouldBeGreaterThan(0.95,
            "a regenerated underbase should cover the opaque canvas, not the retained quadrant pattern");

        File.ReadAllBytes(carrierPath).ShouldBe(synthetic, "the managed copy was modified on disk");
        Directory.EnumerateFiles(Path.GetDirectoryName(carrierPath)!).Order().ShouldBe(
            new[] { carrierPath, workspace.ResolveAbsolute(tiff) }.Order());

        var closed = await automation.CloseExactDocumentAsync(
            opened.Value, carrier, CancellationToken.None);
        output.WriteLine(closed.IsSuccess
            ? "document closed     : cleanly, through the accepted seam"
            : $"document close      : REFUSED — {closed.Failure.Code}: {closed.Failure.TechnicalDetail}");

        var after = await automation.EnsureReadyAsync(CancellationToken.None);
        after.IsSuccess.ShouldBeTrue(after.IsFailure ? after.Failure.ToString() : "");
        output.WriteLine($"Photoshop after     : {after.Value.State.State}");
        output.WriteLine("accepted output logic changed: NONE");
    }

    /// <summary>
    /// The removal refuses a carrier whose ink cannot be positively classified, and removes
    /// nothing when it does.
    /// </summary>
    /// <remarks>
    /// Separated from the reuse test rather than sequenced inside it because each guarded open
    /// drives the real application to the foreground, and two of them in one run compete with the
    /// terminal for it. One open per test is what the other workstation smokes do, for the same
    /// reason.
    /// </remarks>
    [Fact]
    public async Task Ambiguous_spot_channels_are_refused_and_nothing_is_removed()
    {
        if (Environment.GetEnvironmentVariable(EnableVariable) != "1")
        {
            return;
        }

        PrintFlowConfiguration configuration =
            PrintFlowConfiguration.LoadFromFile(RepositoryFile("appsettings.json"));
        configuration.Adapters.Mode.ShouldBe("Production");

        string token = DateTimeOffset.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture) +
                       "-" + Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();
        string root = Path.Combine(configuration.Workspace.Root, "QA", "Scrum11101F", token);
        FileWorkspace workspace = new(root);
        IPhotoshopTiffAutomation automation = PhotoshopAutomationComposition.CreateTiffAutomation(
            Path.Combine(configuration.Workspace.Root, configuration.Preset.Path),
            Sha256.Parse(configuration.Preset.ExpectedSha256), workspace,
            Path.Combine(root, "Evidence"), TimeProvider.System);
        string installDirectory = Path.GetDirectoryName(Path.GetFullPath(
            PresetExecutablePath(configuration)))!;

        byte[] ambiguous = SpotChannelPsdFixtures.AmbiguousTwoSpotPsd();
        WorkspaceFileRef carrier = WorkspaceFileRef.Create(
            $"Sessions/S_11101F/Working/A_{token}_AMBIGUOUS/PF_11101F_AMBIGUOUS.psd", WorkspaceArea.Working);
        string path = workspace.ResolveAbsolute(carrier);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, ambiguous);

        var opened = await automation.OpenManagedWorkingFileAsync(carrier, CancellationToken.None);
        opened.IsSuccess.ShouldBeTrue(opened.IsFailure ? opened.Failure.ToString() : "");
        try
        {
            RemovalOutcome outcome = RunRemoval(opened.Value.Identity.ObservedFullPath, installDirectory);
            output.WriteLine($"ambiguous carrier   : {(outcome.Succeeded ? "PERFORMED" : "REFUSED")} — {outcome.Detail}");
            output.WriteLine($"  before            : {outcome.Before}");
            outcome.Succeeded.ShouldBeFalse("two spot channels must never be resolved by guessing");
            outcome.After.ShouldBe(outcome.Before, "a refusal must leave the document untouched");
        }
        finally
        {
            await automation.CloseExactDocumentAsync(opened.Value, carrier, CancellationToken.None);
            File.ReadAllBytes(path).ShouldBe(ambiguous);
        }

        output.WriteLine(string.Empty);
    }

    // -----------------------------------------------------------------------------------
    // The fixed removal program
    // -----------------------------------------------------------------------------------

    private sealed record RemovalOutcome(bool Succeeded, string Detail, string Before, string After);

    /// <summary>
    /// One fixed program that removes the single positively classified supported W1, or refuses.
    /// </summary>
    /// <remarks>
    /// The classification is the closed contract and nothing widens it: RGB/8, exactly three
    /// component channels, exactly one non-component channel, that channel a
    /// <c>SPOTCOLOR</c> named exactly <c>W1</c>, and its content non-empty. A W1 that is an alpha
    /// mask, a W1 alongside another ink, several W1 candidates, an empty W1 or any unknown spot all
    /// fall out as refusals, and a refusal removes nothing.
    /// </remarks>
    private static string RemovalProgram(string expectedPath)
    {
        string path = JsonSerializer.Serialize(Path.GetFullPath(expectedPath));
        return $$"""
            (function () {
                var before = '', after = '', removed = false;
                function enc(v) { return encodeURIComponent(String(v)); }
                function describe(doc) {
                    var components = 0, spots = 0, w1 = 'no';
                    for (var i = 0; i < doc.channels.length; i++) {
                        var kind = String(doc.channels[i].kind);
                        if (kind === 'ChannelType.COMPONENT') { components++; }
                        if (kind === 'ChannelType.SPOTCOLOR') { spots++; }
                        if (String(doc.channels[i].name) === 'W1') { w1 = 'yes'; }
                    }
                    return String(doc.mode).replace('DocumentMode.', '') + '/' +
                        (String(doc.bitsPerChannel) === 'BitsPerChannelType.EIGHT' ? '8' : '?') +
                        ' | components=' + components + ' | spots=' + spots +
                        ' | w1=' + w1 + ' | ' + Math.round(doc.width.as('px')) + 'x' +
                        Math.round(doc.height.as('px'));
                }
                function result(ok, detail) {
                    return ['PF-REMOVE-1', ok ? '1' : '0', enc(detail), enc(before), enc(after),
                        removed ? '1' : '0'].join('\n');
                }
                var dialogs = app.displayDialogs;
                try {
                    app.displayDialogs = DialogModes.NO;
                    if (app.documents.length < 1) { return result(false, 'No active document.'); }
                    var doc = app.activeDocument;
                    if (String(doc.fullName.fsName).toLowerCase() !== {{path}}.toLowerCase()) {
                        return result(false, 'The active document is not the exact managed copy.');
                    }
                    before = after = describe(doc);
                    if (String(doc.mode) !== 'DocumentMode.RGB' ||
                        String(doc.bitsPerChannel) !== 'BitsPerChannelType.EIGHT') {
                        return result(false, 'Only RGB/8 is a supported existing-W1 carrier.');
                    }
                    var components = [], extras = [];
                    for (var i = 0; i < doc.channels.length; i++) {
                        var channel = doc.channels[i];
                        if (String(channel.kind) === 'ChannelType.COMPONENT') { components.push(channel); }
                        else { extras.push(channel); }
                    }
                    if (components.length !== 3) {
                        return result(false, 'The document does not carry exactly three RGB components.');
                    }
                    if (extras.length !== 1) {
                        return result(false, 'Exactly one non-component channel is required; found ' +
                            extras.length + '. Ambiguous ink cannot be classified.');
                    }
                    var candidate = extras[0];
                    if (String(candidate.kind) !== 'ChannelType.SPOTCOLOR') {
                        return result(false, 'The extra channel is not a spot channel; a mask is not white ink.');
                    }
                    if (String(candidate.name) !== 'W1') {
                        return result(false, 'The spot channel is not named W1; ink identity is a preset concern.');
                    }
                    var histogram = candidate.histogram, ink = 0;
                    for (var h = 0; h < 255; h++) { ink += Number(histogram[h]); }
                    if (ink <= 0) {
                        return result(false, 'The existing W1 is empty and is not a retainable authority.');
                    }
                    candidate.remove();
                    removed = true;
                    after = describe(doc);
                    // The post-condition is asserted here as well as by the caller, so a removal
                    // that left the document in an unexpected shape cannot report success.
                    var stillWrong = false;
                    for (var c = 0; c < doc.channels.length; c++) {
                        if (String(doc.channels[c].kind) !== 'ChannelType.COMPONENT' ||
                            String(doc.channels[c].name) === 'W1') { stillWrong = true; }
                    }
                    if (stillWrong || doc.channels.length !== 3) {
                        return result(false, 'After removal the document is not RGB/8 with three components only.');
                    }
                    return result(true, 'Removed exactly one supported W1 spot channel.');
                } catch (error) {
                    try { after = describe(app.activeDocument); } catch (ignored) { }
                    return result(false, String(error));
                } finally {
                    app.displayDialogs = dialogs;
                }
            }())
            """;
    }

    private static RemovalOutcome RunRemoval(string documentPath, string expectedInstallDirectory)
    {
        object? application = null;
        try
        {
            Marshal.ThrowExceptionForHR(
                NativeMethods.CLSIDFromProgID("Photoshop.Application.130", out Guid classId));
            Marshal.ThrowExceptionForHR(NativeMethods.GetActiveObject(ref classId, 0, out application));
            object app = application ?? throw new InvalidOperationException("No running Photoshop object.");
            Type type = app.GetType();
            string reported = Convert.ToString(
                type.InvokeMember("Path", BindingFlags.GetProperty, null, app, null),
                CultureInfo.InvariantCulture) ?? string.Empty;
            Path.GetFullPath(reported.TrimEnd('\\', '/'))
                .ShouldBe(expectedInstallDirectory, StringCompareShould.IgnoreCase);
            string response = Convert.ToString(
                type.InvokeMember("DoJavaScript", BindingFlags.InvokeMethod, null, app,
                    [RemovalProgram(documentPath)]),
                CultureInfo.InvariantCulture) ?? string.Empty;

            string[] lines = response.Split('\n');
            lines[0].ShouldBe("PF-REMOVE-1");
            return new RemovalOutcome(
                lines[1] == "1",
                Uri.UnescapeDataString(lines[2]),
                Uri.UnescapeDataString(lines[3]),
                Uri.UnescapeDataString(lines[4]));
        }
        finally
        {
            if (application is not null && Marshal.IsComObject(application))
            {
                Marshal.FinalReleaseComObject(application);
            }
        }
    }

    private static string PresetExecutablePath(PrintFlowConfiguration configuration)
    {
        using JsonDocument manifest = JsonDocument.Parse(File.ReadAllBytes(
            Path.Combine(configuration.Workspace.Root, configuration.Preset.Path)));
        return manifest.RootElement.GetProperty("photoshopContract")
            .GetProperty("executablePath").GetString()!;
    }

    private static string RepositoryFile(string relativePath)
    {
        DirectoryInfo? current = new(AppContext.BaseDirectory);
        while (current is not null && !File.Exists(Path.Combine(current.FullName, "PrintFlowStudio.sln")))
        {
            current = current.Parent;
        }

        current.ShouldNotBeNull("the repository root must be locatable from the test output.");
        return Path.Combine(current.FullName, relativePath);
    }
}
