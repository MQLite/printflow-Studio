using System.Globalization;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using PrintFlow.Domain.Files;
using PrintFlow.Domain.Ids;
using PrintFlow.Domain.Results;
using PrintFlow.Infrastructure.Adapters.Photoshop;
using PrintFlow.Infrastructure.Automation;
using PrintFlow.Infrastructure.Configuration;
using PrintFlow.Infrastructure.Workspace;
using PrintFlow.Tests.Fixtures;
using Xunit.Abstractions;

namespace PrintFlow.Tests.Smoke;

/// <summary>
/// SCRUM-11101-A Gates B and C. Asks the two questions that decide whether a Retain branch is
/// buildable at all, and that no amount of source reading can answer: does an existing W1 spot
/// channel survive the accepted CMYK conversion, and does it survive the accepted 300-PPI resize
/// with its content still aligned to the visible canvas?
/// </summary>
/// <remarks>
/// <b>The signed W1 Action is never invoked here.</b> That is the point: the accepted
/// <c>cmyk-w1-action-runtime.json</c> transcripts show 转换模式 (Image &gt; Mode &gt; CMYK Color)
/// and the W1 creation as one indivisible Action, so a Retain job cannot run it without
/// regenerating the very channel the operator asked to keep. This probe exercises the two
/// underlying primitives on their own — <c>changeMode(ChangeMode.CMYK)</c> under the pinned
/// workstation colour settings, and <c>resizeImage(..., 300, ResampleMethod...)</c> — which is
/// exactly what a Retain path would have to rely on.
/// <para>
/// Nothing is saved. The document is opened through <see cref="IPhotoshopAutomationFoundation"/>
/// from a PrintFlow-managed <see cref="WorkspaceArea.Working"/> copy, mutated only in memory, and
/// closed again; both the managed copy and the synthetic source are hashed before and after.
/// </para>
/// <para>
/// Opt in with <c>PRINTFLOW_RETAIN_PRIMITIVE_PROBE=1</c>. Inert otherwise, like every other
/// workstation probe in this folder.
/// </para>
/// </remarks>
public sealed class ExistingWhiteInkRetainPrimitiveProbe(ITestOutputHelper output)
{
    private const string EnableVariable = "PRINTFLOW_RETAIN_PRIMITIVE_PROBE";

    /// <summary>The limiting width the 300-PPI stage resizes to, in millimetres.</summary>
    private const double TargetWidthMm = 50;

    /// <summary>Expected pixel width after the resize: 50 mm at 300 PPI.</summary>
    private const int ExpectedResizedWidth = 591;

    [Fact]
    public async Task Existing_W1_survives_the_accepted_CMYK_change_and_300ppi_sizing()
    {
        if (Environment.GetEnvironmentVariable(EnableVariable) != "1")
        {
            return;
        }

        PrintFlowConfiguration configuration =
            PrintFlowConfiguration.LoadFromFile(RepositoryFile("appsettings.json"));
        configuration.Adapters.Mode.ShouldBe("Production");

        string workspaceRoot = Path.GetFullPath(configuration.Workspace.Root);
        FileWorkspace workspace = new(workspaceRoot);
        IPhotoshopAutomationFoundation foundation = PhotoshopAutomationComposition.CreateFoundation(
            Path.Combine(workspaceRoot, configuration.Preset.Path),
            Sha256.Parse(configuration.Preset.ExpectedSha256),
            workspace,
            Path.Combine(workspaceRoot, "Evidence"),
            TimeProvider.System);

        // A real managed session, so the file Photoshop is handed is a genuine Working copy and
        // the immutable Source snapshot exists to be compared against afterwards.
        SessionId session = SessionId.From(Guid.CreateVersion7());
        var sessionDir = workspace.CreateSession(session, DateTimeOffset.UtcNow);
        sessionDir.IsSuccess.ShouldBeTrue();

        byte[] synthetic = SpotChannelPsdFixtures.QuadrantW1Psd();
        string staged = Path.Combine(Path.GetTempPath(), $"PF_11101A_{Guid.NewGuid():N}.psd");
        File.WriteAllBytes(staged, synthetic);
        try
        {
            var imported = await workspace.ImportSourceAsync(
                sessionDir.Value, staged, CancellationToken.None);
            imported.IsSuccess.ShouldBeTrue(imported.IsFailure ? imported.Failure.ToString() : "");

            var working = await workspace.CreateWorkingCopyAsync(
                sessionDir.Value, AttemptId.From(Guid.CreateVersion7()), imported.Value, CancellationToken.None);
            working.IsSuccess.ShouldBeTrue(working.IsFailure ? working.Failure.ToString() : "");

            string sourceAbsolute = workspace.ResolveAbsolute(imported.Value);
            string workingAbsolute = workspace.ResolveAbsolute(working.Value);
            byte[] workingBefore = SHA256.HashData(File.ReadAllBytes(workingAbsolute));

            output.WriteLine($"synthetic PSD    : {synthetic.Length} bytes, " +
                             $"{SpotChannelPsdFixtures.Width}x{SpotChannelPsdFixtures.Height}, RGB/8 + W1");
            output.WriteLine($"managed Source   : {imported.Value.RelativePath}");
            output.WriteLine($"managed Working  : {working.Value.RelativePath}");
            output.WriteLine(string.Empty);

            var ready = await foundation.EnsureReadyAsync(CancellationToken.None);
            ready.IsSuccess.ShouldBeTrue(ready.IsFailure ? ready.Failure.ToString() : "");
            output.WriteLine($"Photoshop before : {ready.Value.State.State}");

            var opened = await foundation.OpenManagedWorkingFileAsync(working.Value, CancellationToken.None);
            opened.IsSuccess.ShouldBeTrue(opened.IsFailure ? opened.Failure.ToString() : "");
            output.WriteLine($"identity proved  : {opened.Value.Identity.ObservedFullPath}");
            output.WriteLine(string.Empty);

            string response;
            try
            {
                response = RunProbeProgram(
                    ProbeProgram(opened.Value.Identity.ObservedFullPath),
                    Path.GetDirectoryName(Path.GetFullPath(
                        PresetExecutablePath(configuration, workspaceRoot)))!);
            }
            finally
            {
                var closed = await foundation.CloseExactDocumentAsync(
                    opened.Value, working.Value, CancellationToken.None);
                output.WriteLine(closed.IsSuccess
                    ? "document closed  : cleanly, through the accepted seam, nothing saved"
                    : $"document close   : REFUSED — {closed.Failure.Code}: {closed.Failure.TechnicalDetail}");
            }

            output.WriteLine(string.Empty);
            output.WriteLine("=== raw probe response ===");
            output.WriteLine(response);
            output.WriteLine(string.Empty);

            ProbeStage[] stages = Decode(response);
            stages.Length.ShouldBe(3, "the probe must report the opened, CMYK and resized stages");
            ProbeStage opened0 = stages[0], cmyk = stages[1], resized = stages[2];

            foreach (ProbeStage stage in stages)
            {
                output.WriteLine($"--- {stage.Name} ---");
                output.WriteLine($"  mode/bits    : {stage.Mode} / {stage.Bits}");
                output.WriteLine($"  profile      : {stage.ColourProfile}");
                output.WriteLine($"  pixels/res   : {stage.PixelWidth}x{stage.PixelHeight} @ {stage.Resolution} PPI");
                output.WriteLine($"  channels     : {string.Join(", ", stage.Channels)}");
                output.WriteLine($"  W1 name/kind : {stage.W1Name} / {stage.W1Kind}");
                output.WriteLine($"  W1 ink px    : {stage.W1InkPixels} of {stage.TotalPixels} " +
                                 $"({100.0 * stage.W1InkPixels / stage.TotalPixels:F2}%)");
                output.WriteLine($"  W1 quadrants : TL {stage.W1Quadrants[0]:F1}  TR {stage.W1Quadrants[1]:F1}  " +
                                 $"BL {stage.W1Quadrants[2]:F1}  BR {stage.W1Quadrants[3]:F1}");
                output.WriteLine($"  canvas quads : TL {stage.CanvasQuadrants[0]:F1}  TR {stage.CanvasQuadrants[1]:F1}  " +
                                 $"BL {stage.CanvasQuadrants[2]:F1}  BR {stage.CanvasQuadrants[3]:F1}");
                output.WriteLine(string.Empty);
            }

            // ---- The opened document is the synthetic contract ------------------------------
            opened0.Mode.ShouldBe("DocumentMode.RGB");
            opened0.Bits.ShouldBe("BitsPerChannelType.EIGHT");
            opened0.PixelWidth.ShouldBe(SpotChannelPsdFixtures.Width);
            opened0.PixelHeight.ShouldBe(SpotChannelPsdFixtures.Height);
            opened0.Channels.Length.ShouldBe(4);
            opened0.W1Kind.ShouldBe("ChannelType.SPOTCOLOR");
            opened0.W1InkPixels.ShouldBeGreaterThan(0);
            AssertQuadrantPattern(opened0, "opened");

            // ---- GATE B: the CMYK primitive preserves the existing W1 -----------------------
            cmyk.Mode.ShouldBe("DocumentMode.CMYK");
            cmyk.Bits.ShouldBe("BitsPerChannelType.EIGHT");
            cmyk.W1Name.ShouldBe("W1");
            cmyk.W1Kind.ShouldBe("ChannelType.SPOTCOLOR");
            cmyk.Channels.Length.ShouldBe(5, "four CMYK components plus the retained W1");
            cmyk.PixelWidth.ShouldBe(opened0.PixelWidth, "the mode change must not touch the canvas");
            cmyk.PixelHeight.ShouldBe(opened0.PixelHeight);
            cmyk.W1InkPixels.ShouldBe(opened0.W1InkPixels,
                "a colour-mode change must not alter one pixel of the retained spot channel");
            AssertQuadrantPattern(cmyk, "after changeMode(CMYK)");

            // The CMYK contract, not merely "it says CMYK". The preset pins the destination
            // working space, and Image > Mode > CMYK Color converts into exactly that space; a
            // different profile here would mean this primitive is not the accepted transform.
            using (JsonDocument preset = JsonDocument.Parse(File.ReadAllBytes(
                Path.Combine(workspaceRoot, configuration.Preset.Path))))
            {
                JsonElement colour = preset.RootElement
                    .GetProperty("photoshopContract").GetProperty("colourSettings");
                colour.GetProperty("convertToProfileCommandUsed").GetBoolean().ShouldBeFalse();
                colour.GetProperty("conversionCommand").GetString().ShouldBe("图像 > 模式 > CMYK 颜色");
                string expectedSpace = colour.GetProperty("cmykWorkingSpace").GetString()!;
                output.WriteLine($"preset CMYK space: {expectedSpace}");
                cmyk.ColourProfile.ShouldBe(expectedSpace,
                    "changeMode(CMYK) did not convert into the preset's pinned CMYK working space");
            }

            // ---- GATE C: the 300-PPI resize preserves and transforms it ---------------------
            resized.Mode.ShouldBe("DocumentMode.CMYK");
            resized.W1Name.ShouldBe("W1");
            resized.W1Kind.ShouldBe("ChannelType.SPOTCOLOR");
            resized.Channels.Length.ShouldBe(5);
            resized.Resolution.ShouldBe(300, 0.0001);
            resized.PixelWidth.ShouldBe(ExpectedResizedWidth);
            resized.W1InkPixels.ShouldBeGreaterThan(0, "the retained W1 must not be resampled away");

            // Coverage is a proportion, so it survives resampling; an accidental regeneration
            // would cover the whole canvas and a loss would cover none of it.
            double before = (double)opened0.W1InkPixels / opened0.TotalPixels;
            double after = (double)resized.W1InkPixels / resized.TotalPixels;
            output.WriteLine($"W1 coverage      : {before:P2} -> {after:P2}");
            Math.Abs(after - before).ShouldBeLessThan(0.02,
                "the retained W1's share of the canvas changed, which regeneration or loss would do");

            // The spatial signature is what proves it is the *same* channel content, correctly
            // resampled, and still registered to the visible artwork.
            AssertQuadrantPattern(resized, "after the 300-PPI resize");

            // ---- Nothing was written -------------------------------------------------------
            File.ReadAllBytes(sourceAbsolute).ShouldBe(synthetic, "the immutable Source snapshot changed");
            SHA256.HashData(File.ReadAllBytes(workingAbsolute)).ShouldBe(workingBefore,
                "the managed Working copy was modified even though nothing was saved");
            output.WriteLine("Source and Working bytes unchanged: no save occurred.");

            var after0 = await foundation.EnsureReadyAsync(CancellationToken.None);
            after0.IsSuccess.ShouldBeTrue(after0.IsFailure ? after0.Failure.ToString() : "");
            output.WriteLine($"Photoshop after  : {after0.Value.State.State}");
        }
        finally
        {
            File.Delete(staged);
        }
    }

    /// <summary>
    /// Asserts that the four quadrant ink densities are still in their original spatial order and
    /// still distinguishable from one another.
    /// </summary>
    /// <remarks>
    /// Absolute values are compared with a wide tolerance because resampling legitimately softens
    /// them; what must hold exactly is the <i>ordering</i>, because that is what a flip, rotation
    /// or shift relative to the canvas would break, and what a regenerated underbase would flatten.
    /// </remarks>
    private void AssertQuadrantPattern(ProbeStage stage, string what)
    {
        byte[] expected = SpotChannelPsdFixtures.QuadrantInk;
        for (int quadrant = 0; quadrant < 4; quadrant++)
        {
            Math.Abs(stage.W1Quadrants[quadrant] - expected[quadrant]).ShouldBeLessThan(12,
                $"{what}: quadrant {quadrant} ink density moved from {expected[quadrant]} " +
                $"to {stage.W1Quadrants[quadrant]:F1}");
        }

        // Strictly increasing TL < TR < BL < BR, the asymmetry the fixture was built around.
        for (int quadrant = 1; quadrant < 4; quadrant++)
        {
            stage.W1Quadrants[quadrant].ShouldBeGreaterThan(stage.W1Quadrants[quadrant - 1],
                $"{what}: the quadrant ordering that proves spatial registration was lost");
        }

        output.WriteLine($"  quadrant check : {what} — pattern intact");
    }

    // -----------------------------------------------------------------------------------
    // The fixed probe program
    // -----------------------------------------------------------------------------------

    /// <summary>
    /// One fixed, test-owned ExtendScript program. It takes no caller-supplied source and names
    /// exactly one document, by absolute path, which it re-proves before touching anything.
    /// </summary>
    private static string ProbeProgram(string expectedPath)
    {
        string path = JsonSerializer.Serialize(Path.GetFullPath(expectedPath));
        string width = TargetWidthMm.ToString("R", CultureInfo.InvariantCulture);
        return $$"""
            (function () {
                var stages = [];
                function enc(v) { return encodeURIComponent(String(v)); }
                function w1Of(doc) {
                    var found = [];
                    for (var i = 0; i < doc.channels.length; i++) {
                        if (String(doc.channels[i].name) === 'W1') { found.push(doc.channels[i]); }
                    }
                    return found.length === 1 ? found[0] : null;
                }
                function mean(histogram) {
                    var total = 0, weighted = 0;
                    for (var i = 0; i < 256; i++) {
                        var count = Number(histogram[i]);
                        total += count; weighted += i * count;
                    }
                    return total === 0 ? -1 : weighted / total;
                }
                // Quadrant means are read from a cropped duplicate. The crop takes the middle
                // half of the quadrant so that resampled edges never enter the measurement.
                // Bounds are explicit pixel UnitValues: Photoshop would otherwise read bare
                // numbers in the operator's current ruler units and crop somewhere else entirely.
                function quadrantMeans(doc, useW1) {
                    var out = [];
                    for (var q = 0; q < 4; q++) {
                        var qx = q % 2, qy = q < 2 ? 0 : 1;
                        var qw = doc.width.as('px') / 2, qh = doc.height.as('px') / 2;
                        var copy = doc.duplicate('PF 11101A measure');
                        try {
                            copy.crop([UnitValue(qx * qw + qw / 4, 'px'), UnitValue(qy * qh + qh / 4, 'px'),
                                       UnitValue(qx * qw + qw * 3 / 4, 'px'), UnitValue(qy * qh + qh * 3 / 4, 'px')]);
                            var channel = useW1 ? w1Of(copy) : copy.componentChannels[0];
                            out.push(channel === null ? -1 : mean(channel.histogram));
                        } finally {
                            copy.close(SaveOptions.DONOTSAVECHANGES);
                        }
                    }
                    return out;
                }
                function observe(doc, name) {
                    var channels = [];
                    for (var i = 0; i < doc.channels.length; i++) {
                        channels.push(enc(doc.channels[i].name) + ',' + enc(String(doc.channels[i].kind)));
                    }
                    var w1 = w1Of(doc), ink = -1, w1Name = '', w1Kind = '';
                    if (w1 !== null) {
                        w1Name = String(w1.name); w1Kind = String(w1.kind);
                        var histogram = w1.histogram; ink = 0;
                        for (var h = 0; h < 255; h++) { ink += Number(histogram[h]); }
                    }
                    var profile = '';
                    try { profile = String(doc.colorProfileName); } catch (noProfile) { }
                    stages.push([
                        enc(name),
                        enc(profile),
                        Math.round(doc.width.as('px')),
                        Math.round(doc.height.as('px')),
                        Number(doc.resolution),
                        enc(String(doc.mode)),
                        enc(String(doc.bitsPerChannel)),
                        channels.join(';'),
                        enc(w1Name),
                        enc(w1Kind),
                        ink,
                        quadrantMeans(doc, true).join(','),
                        quadrantMeans(doc, false).join(',')
                    ].join('|'));
                }
                function result(ok, detail) {
                    return ['PF-RETAIN-1', ok ? '1' : '0', enc(detail)].concat(stages).join('\n');
                }
                var dialogs = app.displayDialogs;
                var rulers = app.preferences.rulerUnits;
                try {
                    app.displayDialogs = DialogModes.NO;
                    app.preferences.rulerUnits = Units.PIXELS;
                    if (app.documents.length < 1) { return result(false, 'No active document.'); }
                    var doc = app.activeDocument;
                    if (String(doc.fullName.fsName).toLowerCase() !== {{path}}.toLowerCase()) {
                        return result(false, 'The active document is not the exact managed Working copy.');
                    }
                    observe(doc, 'opened');
                    // The accepted colour transform, as pinned by the workstation preset's
                    // colourSettings: Image > Mode > CMYK Color, not Convert to Profile.
                    doc.changeMode(ChangeMode.CMYK);
                    observe(doc, 'changeMode(CMYK)');
                    // The accepted B1A.3 sizing call shape, width-limited at 300 PPI.
                    doc.resizeImage(UnitValue({{width}}, 'mm'), undefined, 300, ResampleMethod.BICUBICSHARPER);
                    observe(doc, 'resizeImage(300 PPI)');
                    return result(true, 'observed');
                } catch (error) {
                    return result(false, String(error));
                } finally {
                    app.displayDialogs = dialogs;
                    app.preferences.rulerUnits = rulers;
                }
            }())
            """;
    }

    /// <summary>Attaches to the accepted running instance and runs the fixed program once.</summary>
    private static string RunProbeProgram(string program, string expectedInstallDirectory)
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
            return Convert.ToString(
                type.InvokeMember("DoJavaScript", BindingFlags.InvokeMethod, null, app, [program]),
                CultureInfo.InvariantCulture) ?? string.Empty;
        }
        finally
        {
            if (application is not null && Marshal.IsComObject(application))
            {
                Marshal.FinalReleaseComObject(application);
            }
        }
    }

    // -----------------------------------------------------------------------------------
    // Decoding
    // -----------------------------------------------------------------------------------

    private sealed record ProbeStage(
        string Name,
        string ColourProfile,
        int PixelWidth,
        int PixelHeight,
        double Resolution,
        string Mode,
        string Bits,
        string[] Channels,
        string W1Name,
        string W1Kind,
        long W1InkPixels,
        double[] W1Quadrants,
        double[] CanvasQuadrants)
    {
        internal long TotalPixels => (long)PixelWidth * PixelHeight;
    }

    private static ProbeStage[] Decode(string response)
    {
        string[] lines = response.Split('\n');
        lines.Length.ShouldBeGreaterThanOrEqualTo(3);
        lines[0].ShouldBe("PF-RETAIN-1");
        lines[1].ShouldBe("1", "the probe refused: " + Uri.UnescapeDataString(lines[2]));

        return [.. lines.Skip(3).Where(line => line.Length > 0).Select(line =>
        {
            string[] f = line.Split('|');
            f.Length.ShouldBe(13);
            double Number(string value) => double.Parse(value, CultureInfo.InvariantCulture);
            return new ProbeStage(
                Uri.UnescapeDataString(f[0]),
                Uri.UnescapeDataString(f[1]),
                int.Parse(f[2], CultureInfo.InvariantCulture),
                int.Parse(f[3], CultureInfo.InvariantCulture),
                Number(f[4]),
                Uri.UnescapeDataString(f[5]),
                Uri.UnescapeDataString(f[6]),
                [.. f[7].Split(';', StringSplitOptions.RemoveEmptyEntries)
                    .Select(pair => Uri.UnescapeDataString(pair.Replace(',', ' ')))],
                Uri.UnescapeDataString(f[8]),
                Uri.UnescapeDataString(f[9]),
                long.Parse(f[10], CultureInfo.InvariantCulture),
                [.. f[11].Split(',').Select(Number)],
                [.. f[12].Split(',').Select(Number)]);
        })];
    }

    private static string PresetExecutablePath(PrintFlowConfiguration configuration, string workspaceRoot)
    {
        using JsonDocument manifest = JsonDocument.Parse(
            File.ReadAllBytes(Path.Combine(workspaceRoot, configuration.Preset.Path)));
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
