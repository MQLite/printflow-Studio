using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Time.Testing;
using PrintFlow.Domain.Files;
using PrintFlow.Infrastructure.Verification;

namespace PrintFlow.Tests.Fixtures;

/// <summary>
/// A complete synthetic workstation: a preset manifest, the evidence it vouches for, two
/// stand-in executables, a stand-in Action file and a workspace root — all under one temporary
/// directory (Epic 11500 Part A §22).
/// </summary>
/// <remarks>
/// <b>Every accepted value here is invented.</b> The synthetic workstation runs a "Windows Test
/// Edition 99.0.1234" on a 1280x800 display at 120 DPI in <c>fr-FR</c> — a machine that exists
/// nowhere. That is the point: a verifier that passes against this fixture cannot be carrying a
/// hidden copy of the real workstation's paths, digests, resolution or culture, because none of
/// them would match. The accepted production values are read from the signed preset at run time
/// and are written down in no source file, test fixture included (§3).
/// <para>
/// The signed production manifest is never copied into the repository, and no test here reads
/// it. The one place the real workstation is touched is the opt-in live observation smoke.
/// </para>
/// </remarks>
internal sealed class WorkstationVerificationFixture : IDisposable
{
    public const string PresetId = "test-workstation-v1";
    public const string PresetVersion = "0.0.1";

    public const string Edition = "Windows Test Edition";
    public const string OsVersion = "99.0.1234";
    public const string OsBuild = "1234";
    public const string Architecture = "64-bit";
    public const string UiCulture = "fr-FR";
    public const string SystemUiCulture = "en-GB";

    public const string AllowedSession = "LOCAL_CONSOLE_ONLY";

    public const int DisplayCount = 1;
    public const string PrimaryDisplay = "DISPLAY7";
    public const int DisplayWidth = 1280;
    public const int DisplayHeight = 800;
    public const int WorkAreaHeight = 760;
    public const int SystemDpi = 120;
    public const int ScalePercent = 125;

    public const string ActionSetName = "PrintFlow Test Set";
    public const string MeituProductVersion = "9.9.9.9";
    public const string PhotoshopProductVersion = "99.0";
    public const string PhotoshopFileVersion = "99.0 (19990101.r.1 1999/01/01: 1)";
    public const string MeituUiLanguage = UiCulture;
    public const string PhotoshopUiLanguage = "Test Language";

    private const int EvidenceFileCount = 3;

    private readonly string _root;

    public WorkstationVerificationFixture()
    {
        _root = Path.Combine(Path.GetTempPath(), "printflow-verify-" + Guid.NewGuid().ToString("N"));

        WorkspaceRoot = Path.Combine(_root, "workspace");
        Directory.CreateDirectory(WorkspaceRoot);

        string evidenceDirectory = Path.Combine(_root, "evidence");
        Directory.CreateDirectory(evidenceDirectory);
        EvidencePaths =
        [
            .. Enumerable.Range(1, EvidenceFileCount).Select(i =>
                WriteFile(Path.Combine(evidenceDirectory, $"evidence-{i}.json"), $$"""{"synthetic":{{i}}}"""))
        ];

        string appsDirectory = Path.Combine(_root, "apps");
        Directory.CreateDirectory(appsDirectory);
        MeituPath = WriteFile(Path.Combine(appsDirectory, "SyntheticMeitu.exe"), "synthetic meitu bytes");
        PhotoshopPath = WriteFile(Path.Combine(appsDirectory, "SyntheticPhotoshop.exe"), "synthetic photoshop bytes");

        string actionsDirectory = Path.Combine(_root, "actions");
        Directory.CreateDirectory(actionsDirectory);
        ActionPath = WriteFile(Path.Combine(actionsDirectory, "PrintFlow-Test-v1.atn"), "synthetic action bytes");

        ManifestPath = Path.Combine(_root, "preset", "synthetic-workstation-preset.json");
        Directory.CreateDirectory(Path.GetDirectoryName(ManifestPath)!);
        ManifestSha256 = WriteManifest();

        Facts = MatchingFacts();
        Artifacts = new FileSystemArtifactReader();
        Clock = new FakeTimeProvider(new DateTimeOffset(2026, 9, 1, 12, 0, 0, TimeSpan.Zero));

        // The synthetic workstation starts revalidated, so that every test written before
        // SCRUM-11123 still describes "this is the accepted workstation and nothing has changed"
        // and keeps asserting the thing it was written to assert. A test that wants the
        // fail-closed side calls RemoveRevalidationRecord or WriteRevalidationRecord.
        WriteRevalidationRecord(MatchingRevalidationRecord());
    }

    public string ManifestPath { get; }

    public Sha256 ManifestSha256 { get; private set; }

    public string WorkspaceRoot { get; }

    public string MeituPath { get; }

    public string PhotoshopPath { get; }

    public string ActionPath { get; }

    public IReadOnlyList<string> EvidencePaths { get; }

    /// <summary>Mutable so a test can present a different machine without owning one.</summary>
    public FakeWorkstationFactReader Facts { get; }

    /// <summary>The real reader, against the fixture's real temporary files.</summary>
    public IWorkstationArtifactReader Artifacts { get; set; }

    public FakeTimeProvider Clock { get; }

    /// <summary>The workstation exactly as the synthetic manifest accepts it.</summary>
    public static FakeWorkstationFactReader MatchingFacts() => new()
    {
        OperatingSystem = new OperatingSystemFacts(Edition, OsVersion, OsBuild, Architecture),
        Session = new InteractiveSessionFacts(true, 1, "Console", false, "Default"),
        Display = new DisplayFacts(
            DisplayCount,
            @"\\.\" + PrimaryDisplay,
            new DisplayRectangle(0, 0, DisplayWidth, DisplayHeight),
            new DisplayRectangle(0, 0, DisplayWidth, WorkAreaHeight),
            SystemDpi),
        Culture = new UiCultureFacts(UiCulture, SystemUiCulture),
    };

    /// <summary>Builds a verifier over this fixture's manifest, facts and files.</summary>
    public ProductionWorkstationVerifier CreateVerifier(string? workspaceRootOverride = null) =>
        new(ManifestPath,
            PresetId,
            PresetVersion,
            ManifestSha256,
            workspaceRootOverride ?? WorkspaceRoot,
            Facts,
            Artifacts,
            Clock);

    /// <summary>Rewrites a file so its bytes no longer hash to what the manifest recorded.</summary>
    public static void Corrupt(string path) => File.WriteAllText(path, "tampered", Encoding.UTF8);

    /// <summary>Deletes a file the manifest vouches for.</summary>
    public static void Remove(string path) => File.Delete(path);

    /// <summary>Clears the read-only attribute the immutability policy applies after hashing.</summary>
    public static void ClearReadOnlyAttribute(string path) =>
        new FileInfo(path).IsReadOnly = false;

    /// <summary>Applies the read-only attribute to every integrity-referenced file.</summary>
    public void MarkEvidenceReadOnly()
    {
        foreach (string path in EvidencePaths)
        {
            new FileInfo(path).IsReadOnly = true;
        }
    }

    /// <summary>Replaces the manifest's expected digest with one it does not hash to.</summary>
    public void ExpectWrongManifestHash() =>
        ManifestSha256 = Sha256.FromBytes(SHA256.HashData(Encoding.UTF8.GetBytes("not the manifest")));

    public void Dispose()
    {
        try
        {
            foreach (string file in Directory.EnumerateFiles(_root, "*", SearchOption.AllDirectories))
            {
                new FileInfo(file).IsReadOnly = false;
            }

            Directory.Delete(_root, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or DirectoryNotFoundException)
        {
            // A temp directory that outlives one test run is not worth failing that run over.
        }
    }

    private static string WriteFile(string path, string content)
    {
        File.WriteAllText(path, content, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        return path;
    }

    private Sha256 WriteManifest()
    {
        string json = JsonSerializer.Serialize(new
        {
            schema = "printflow.workstation-preset",
            presetId = PresetId,
            presetVersion = PresetVersion,
            workstation = new
            {
                operatingSystem = new
                {
                    edition = Edition,
                    version = OsVersion,
                    build = OsBuild,
                    architecture = Architecture,
                    uiCulture = UiCulture,
                },
                sessionContract = new
                {
                    allowedSession = AllowedSession,
                    remoteAutomationAllowed = false,
                    blockOnActiveRemoteSession = true,
                },
            },
            displayContract = new
            {
                activeDisplayCount = DisplayCount,
                primaryDisplay = PrimaryDisplay,
                monitorBounds = new { left = 0, top = 0, right = DisplayWidth, bottom = DisplayHeight },
                workArea = new { left = 0, top = 0, right = DisplayWidth, bottom = WorkAreaHeight },
                systemDpi = SystemDpi,
                scalePercent = ScalePercent,
            },
            storageAndNamingContract = new
            {
                defaultOutputRoot = WorkspaceRoot,
                volume = Path.GetPathRoot(WorkspaceRoot)?.TrimEnd('\\'),
                fileSystem = new DriveInfo(Path.GetPathRoot(WorkspaceRoot)!).DriveFormat,
            },
            meituContract = new
            {
                executablePath = MeituPath,
                executableSha256 = DigestOf(MeituPath),
                uiLanguage = MeituUiLanguage,
            },
            photoshopContract = new
            {
                executablePath = PhotoshopPath,
                executableSha256 = DigestOf(PhotoshopPath),
                uiLanguage = PhotoshopUiLanguage,
                colourSettings = new
                {
                    rgbWorkingSpace = "Synthetic RGB",
                    cmykWorkingSpace = "Synthetic CMYK",
                    grayWorkingSpace = "Synthetic Gray",
                    spotWorkingSpace = "Synthetic Spot",
                    conversionCommand = "image>mode>CMYK",
                    convertToProfileCommandUsed = false,
                    visibleSettingsManifestSha256 = DigestOf(EvidencePaths[0]),
                },
            },
            photoshopActionContract = new
            {
                setName = ActionSetName,
                artifactPath = ActionPath,
                artifactBytes = new FileInfo(ActionPath).Length,
                artifactSha256 = DigestOf(ActionPath),
            },
            sourceManifestIntegrity = EvidencePaths
                .Select(p => new { path = p, sha256 = DigestOf(p) })
                .ToArray(),
        });

        byte[] bytes = Encoding.UTF8.GetBytes(json);
        File.WriteAllBytes(ManifestPath, bytes);
        return Sha256.FromBytes(SHA256.HashData(bytes));
    }

    private static string DigestOf(string path) =>
        Sha256.FromBytes(SHA256.HashData(File.ReadAllBytes(path))).ToString();

    // ------------------------------------------------------------------------------------------
    // Production revalidation (SCRUM-11123 Part H)
    // ------------------------------------------------------------------------------------------

    /// <summary>The record that describes this fixture's workstation exactly as it stands.</summary>
    /// <remarks>
    /// Built from the fixture's own digests and the running assembly's version rather than from
    /// literals, for the same reason nothing else here is a literal: a record written by hand
    /// would drift from the manifest the moment either changed, and would then be testing that
    /// the check tolerates drift.
    /// </remarks>
    public ProductionRevalidationRecord MatchingRevalidationRecord() =>
        new(ProductionRevalidationRecord.CurrentSchemaVersion,
            ProductionRevalidationEvaluator.RunningProductVersion,
            PresetId,
            PresetVersion,
            ManifestSha256.ToString(),
            OsBuild,
            DigestOf(MeituPath),
            DigestOf(PhotoshopPath),
            EnvironmentReadinessPassed: true,
            new StandardRegressionSetOutcome(
                "synthetic-regression-set-v1",
                StandardRegressionSetStatus.Passed,
                "2026-09-01T09:00:00+12:00",
                Path.Combine(WorkspaceRoot, "Revalidation", "synthetic-run")),
            "SYNTHETIC\\operator",
            "2026-09-01T09:05:00+12:00");

    /// <summary>Writes a revalidation record into this fixture's workspace.</summary>
    public void WriteRevalidationRecord(ProductionRevalidationRecord record)
    {
        string path = ProductionRevalidationRecord.PathFor(WorkspaceRoot);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, record.ToJson(), Encoding.UTF8);
    }

    /// <summary>Writes arbitrary bytes where the record belongs, for the malformed-record case.</summary>
    public void WriteRevalidationRecordRaw(string content)
    {
        string path = ProductionRevalidationRecord.PathFor(WorkspaceRoot);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content, Encoding.UTF8);
    }

    /// <summary>Removes the record, which is the state a freshly upgraded installation is in.</summary>
    public void RemoveRevalidationRecord()
    {
        string path = ProductionRevalidationRecord.PathFor(WorkspaceRoot);
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }
}

/// <summary>
/// A workstation the test decides the shape of (Epic 11500 Part A §22).
/// </summary>
/// <remarks>
/// The failing halves of the verification matrix — a second monitor, 125% scaling, a remote
/// session, a lock screen, a different UI language — cannot be produced by reconfiguring the
/// machine the suite happens to run on. They are produced here instead, which is the reason
/// <see cref="IWorkstationFactReader"/> is an interface at all.
/// </remarks>
internal sealed class FakeWorkstationFactReader : IWorkstationFactReader
{
    public OperatingSystemFacts OperatingSystem { get; set; } =
        new("Windows Test Edition", "99.0.1234", "1234", "64-bit");

    public InteractiveSessionFacts Session { get; set; } = new(true, 1, "Console", false, "Default");

    public DisplayFacts Display { get; set; } =
        new(1, @"\\.\DISPLAY7", new DisplayRectangle(0, 0, 1280, 800), new DisplayRectangle(0, 0, 1280, 760), 120);

    public UiCultureFacts Culture { get; set; } = new("fr-FR", "en-GB");

    /// <summary>How many times the dynamic facts were re-read, for the no-caching proof.</summary>
    public int DynamicReadCount { get; private set; }

    public OperatingSystemFacts ReadOperatingSystem() => OperatingSystem;

    public InteractiveSessionFacts ReadInteractiveSession()
    {
        DynamicReadCount++;
        return Session;
    }

    public DisplayFacts ReadDisplay() => Display;

    public UiCultureFacts ReadUiCulture() => Culture;
}

/// <summary>
/// Wraps the real artefact reader and records every path it was asked about, so a test can show
/// that verification only ever read files (Epic 11500 Part A §17).
/// </summary>
internal sealed class RecordingArtifactReader(IWorkstationArtifactReader inner) : IWorkstationArtifactReader
{
    public List<string> FilesRead { get; } = [];

    public List<string> DirectoriesRead { get; } = [];

    public FileIdentityFacts ReadFile(string absolutePath)
    {
        FilesRead.Add(absolutePath);
        return inner.ReadFile(absolutePath);
    }

    public DirectoryFacts ReadDirectory(string absolutePath)
    {
        DirectoriesRead.Add(absolutePath);
        return inner.ReadDirectory(absolutePath);
    }
}
