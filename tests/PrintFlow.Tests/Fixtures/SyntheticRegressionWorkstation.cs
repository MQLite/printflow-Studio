using System.Collections.Immutable;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using PrintFlow.Infrastructure.Verification;
using PrintFlow.Tests.Regression;

namespace PrintFlow.Tests.Fixtures;

/// <summary>What running one of the operator's scripts produced.</summary>
/// <param name="ExitCode">The script's exit code, which is what an operator and a caller act on.</param>
/// <param name="Output">Everything it printed, standard output and error together.</param>
public sealed record ScriptOutcome(int ExitCode, string Output)
{
    /// <summary>Whether the output contains a phrase, for asserting on the reason and not only the code.</summary>
    public bool Says(string phrase) =>
        Output.Contains(phrase, StringComparison.OrdinalIgnoreCase);
}

/// <summary>
/// A whole synthetic workstation the regression and revalidation scripts can be driven against
/// (PF-AUDIT-R1).
/// </summary>
/// <remarks>
/// <b>Why this exists rather than a helper-level test.</b> The two defects being repaired are in
/// the scripts an operator actually types, not in a class either script calls: one is a shell
/// exit-code decision, the other is which fields a writer reads. Testing a new helper in isolation
/// would prove nothing about either, so these tests run <c>powershell.exe</c> against the real
/// scripts and assert on their exit codes and on the files they did or did not write.
/// <para>
/// <b>Nothing real is touched.</b> The set, the run folders, the installation, the workspace and
/// the revalidation record are all under one temporary directory. Meitu, Photoshop and Maintop are
/// never launched: the test host is replaced through the seam the script already has — it resolves
/// <c>dotnet</c> through <c>LOCALAPPDATA</c> and then <c>PATH</c> — so no external application, and
/// no real regression run, is ever started. The real workstation's production revalidation record
/// and production database are never read or written.
/// </para>
/// <para>
/// <b>The installation is real bytes.</b> The synthetic install folder holds copies of this test
/// host's own <c>PrintFlow.App.exe</c> and four Product assemblies, so version resources, digests
/// and build identities are genuine and a record written against it is one the Product evaluator
/// can be asked to read.
/// </para>
/// </remarks>
internal sealed class SyntheticRegressionWorkstation : IDisposable
{
    /// <summary>The set identity every synthetic manifest agrees on.</summary>
    public const string SetId = "synthetic-regression-set-v1";

    private static readonly string PowerShell = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.System), "WindowsPowerShell", "v1.0", "powershell.exe");

    private readonly string _root;
    private readonly string _stubDirectory;
    private readonly string _meituSha256;
    private readonly string _photoshopSha256;

    public SyntheticRegressionWorkstation(WorkstationVerificationFixture workstation)
    {
        ArgumentNullException.ThrowIfNull(workstation);

        // The accepted external-binary digests a run would have read out of the preset. Taken from
        // the fixture's own stand-in executables, so the binding a test builds states the same
        // identities the preset copied into this workspace requires.
        _meituSha256 = DigestOf(workstation.MeituPath);
        _photoshopSha256 = DigestOf(workstation.PhotoshopPath);

        _root = Path.Combine(Path.GetTempPath(), "printflow-evidence-" + Guid.NewGuid().ToString("N"));
        _stubDirectory = Path.Combine(_root, "stub");
        Directory.CreateDirectory(_stubDirectory);

        SetRoot = Path.Combine(_root, "set");
        ManifestFolder = Path.Combine(SetRoot, "manifests");
        Directory.CreateDirectory(ManifestFolder);
        Directory.CreateDirectory(Path.Combine(SetRoot, "inputs"));

        WorkspaceRoot = Path.Combine(_root, "workspace");
        Directory.CreateDirectory(WorkspaceRoot);

        // The preset the installation is configured against is the fixture's own synthetic preset,
        // copied into the workspace so the configured relative path resolves the way an
        // installation's does. Copied, so its digest and its Meitu and Photoshop contracts are the
        // fixture's and a record written here is one the fixture's requirements can evaluate.
        PresetRelativePath = Path.Combine("preset", "synthetic-workstation-preset.json");
        string presetPath = Path.Combine(WorkspaceRoot, PresetRelativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(presetPath)!);
        File.Copy(workstation.ManifestPath, presetPath);
        PresetSha256 = workstation.ManifestSha256.ToString();

        InstallFolder = Path.Combine(_root, "install");
        Directory.CreateDirectory(InstallFolder);
        foreach (string name in ProductBuildIdentity.ProductAssemblyFileNames.Add("PrintFlow.App.exe"))
        {
            File.Copy(Path.Combine(AppContext.BaseDirectory, name), Path.Combine(InstallFolder, name));
        }

        HarnessFolder = Path.Combine(_root, "harness");
        Directory.CreateDirectory(HarnessFolder);
        foreach (string name in ProductBuildIdentity.ProductAssemblyFileNames.AddRange(
                     new[] { "PrintFlow.Tests.dll", "PrintFlow.Tests.deps.json",
                         "PrintFlow.Tests.runtimeconfig.json", "testhost.dll" }))
        {
            File.Copy(Path.Combine(AppContext.BaseDirectory, name), Path.Combine(HarnessFolder, name));
        }

        File.WriteAllText(Path.Combine(InstallFolder, "appsettings.json"), JsonSerializer.Serialize(new
        {
            Workspace = new { Root = WorkspaceRoot },
            Preset = new
            {
                Id = WorkstationVerificationFixture.PresetId,
                Version = WorkstationVerificationFixture.PresetVersion,
                Path = PresetRelativePath,
                ExpectedSha256 = PresetSha256,
            },
            Adapters = new { Mode = "Production" },
        }, new JsonSerializerOptions { WriteIndented = true }));

        WriteSet();

        BuildOrigin = WriteBuildPairReceipt(
            Path.Combine(_root, "synthetic-build-pair.json"),
            HarnessFolder,
            ProductBuildIdentity.FromFolder(HarnessFolder));
    }

    /// <summary>The synthetic regression set's root.</summary>
    public string SetRoot { get; }

    /// <summary>Its manifests folder, which is what the revalidation writer is pointed at.</summary>
    public string ManifestFolder { get; }

    /// <summary>The synthetic installation the scripts read.</summary>
    public string InstallFolder { get; }

    /// <summary>A minimal copied test-host output used by this fixture's synthetic build receipt.</summary>
    public string HarnessFolder { get; }

    /// <summary>
    /// Explicitly synthetic origin evidence for the copied test and candidate files in this fixture.
    /// It proves only the evidence protocol and is never an authorization to run the real set.
    /// </summary>
    public RegressionBuildOrigin BuildOrigin { get; }

    /// <summary>The synthetic receipt named by <see cref="BuildOrigin"/>.</summary>
    public string BuildPairReceiptPath => BuildOrigin.ReceiptPath;

    /// <summary>The synthetic production workspace, where the revalidation record lands.</summary>
    public string WorkspaceRoot { get; }

    /// <summary>The configured preset path, relative to the workspace root.</summary>
    public string PresetRelativePath { get; }

    /// <summary>The preset manifest's digest.</summary>
    public string PresetSha256 { get; }

    /// <summary>Where the revalidation record is written.</summary>
    public string RecordPath => ProductionRevalidationRecord.PathFor(WorkspaceRoot);

    /// <summary>Rewrites only set metadata and the portrait/fine-hair expectations for preflight protocol tests.</summary>
    public void ConfigureSetVersion(
        string setVersion, string portraitExpectedPropertiesJson, string? fineHairExpectedPropertiesJson = null)
    {
        string setId = $"printflow-regression-{setVersion}";
        foreach (string path in Directory.EnumerateFiles(ManifestFolder, "*.json"))
        {
            JsonObject manifest = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
            manifest["setId"] = setId;
            manifest["fixtureSetVersion"] = setVersion;
            if (string.Equals((string?) manifest["category"], "NORMAL_JPG_PORTRAIT",
                    StringComparison.Ordinal))
            {
                manifest["expectedProperties"] = JsonNode.Parse(portraitExpectedPropertiesJson);
            }

            if (fineHairExpectedPropertiesJson is not null &&
                string.Equals((string?) manifest["category"], "COMPLEX_BACKGROUND_FINE_HAIR",
                    StringComparison.Ordinal))
            {
                manifest["expectedProperties"] = JsonNode.Parse(fineHairExpectedPropertiesJson);
            }

            File.WriteAllText(path, manifest.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        }
    }

    /// <summary>A scratch path inside this workstation, so nothing is left in the system temp root.</summary>
    public string Scratch(string name) => Path.Combine(_root, name);

    /// <summary>The run folder for a run identity.</summary>
    public string RunFolder(string runId) => Path.Combine(SetRoot, "runs", runId);

    /// <summary>The Windows build this machine reports, which is what the writer reads.</summary>
    public static string ObservedOsBuild =>
        Environment.OSVersion.Version.Build.ToString(System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>The product version the writer reads out of the installed executable.</summary>
    public static string InstalledProductVersion => ProductionRevalidationEvaluator.RunningProductVersion;

    // ------------------------------------------------------------------------------------------
    // Running the operator's scripts
    // ------------------------------------------------------------------------------------------

    /// <summary>
    /// Runs a script the way an operator does, with a stand-in for the test host.
    /// </summary>
    /// <remarks>
    /// The stand-in is installed through the script's own resolution order and not through a switch
    /// added for tests: the script looks for the per-user <c>dotnet.exe</c> under
    /// <c>LOCALAPPDATA</c> and falls back to <c>PATH</c>, so pointing both elsewhere replaces the
    /// host without the script knowing. It also means the seam cannot be used to manufacture a
    /// pass — the script's verdict still requires this invocation's own bound result.
    /// </remarks>
    /// <param name="scriptPath">The script to run.</param>
    /// <param name="arguments">Its arguments, already quoted.</param>
    /// <param name="host">What the stand-in test host should do, or null to leave the real one.</param>
    public ScriptOutcome Run(string scriptPath, string arguments, string? host = null)
    {
        ProcessStartInfo start = new()
        {
            FileName = PowerShell,
            Arguments = $"-NoProfile -NonInteractive -ExecutionPolicy Bypass -File \"{scriptPath}\" {arguments}",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            WorkingDirectory = _root,
        };

        // The test host may itself have been launched from PowerShell 7, whose PSModulePath does
        // not guarantee Windows PowerShell can auto-load Microsoft.PowerShell.Utility. The real
        // operator scripts use Get-FileHash, so give this child the built-in 5.1 module location.
        string windowsPowerShellModules = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.Windows),
            "System32", "WindowsPowerShell", "v1.0", "Modules");
        start.Environment["PSModulePath"] = windowsPowerShellModules + Path.PathSeparator +
            Environment.GetEnvironmentVariable("PSModulePath");

        if (host is not null)
        {
            string scenario = Path.Combine(_stubDirectory, "host.ps1");
            File.WriteAllText(scenario, host, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));

            File.WriteAllText(
                Path.Combine(_stubDirectory, "dotnet.cmd"),
                $"@echo off{Environment.NewLine}" +
                $"\"{PowerShell}\" -NoProfile -NonInteractive -ExecutionPolicy Bypass -File \"{scenario}\" %*{Environment.NewLine}" +
                $"exit /b %ERRORLEVEL%{Environment.NewLine}");

            // LOCALAPPDATA holds no dotnet.exe, so the script falls back to PATH, which holds the
            // stand-in. system32 stays on PATH because the stand-in itself launches PowerShell.
            start.Environment["LOCALAPPDATA"] = Path.Combine(_root, "no-local-appdata");
            start.Environment["PATH"] =
                $"{_stubDirectory};{Environment.GetFolderPath(Environment.SpecialFolder.System)}";
        }

        using Process process = Process.Start(start)
            ?? throw new InvalidOperationException("PowerShell did not start.");

        string output = process.StandardOutput.ReadToEnd();
        string error = process.StandardError.ReadToEnd();
        process.WaitForExit();

        return new ScriptOutcome(process.ExitCode, output + error);
    }

    /// <summary>A stand-in host that fails the way a build or host failure does: nonzero, no result.</summary>
    public static string HostThatFails(int exitCode) => $"exit {exitCode}";

    /// <summary>A stand-in host that exposes the exact no-build command selected by the wrapper.</summary>
    public static string HostThatEchoesArguments() =>
        "Write-Output ($args -join [Environment]::NewLine); exit 0";

    /// <summary>
    /// A stand-in host that writes a run result and exits with <paramref name="exitCode"/>.
    /// </summary>
    /// <param name="resultJson">The result to write, with <c>@@INVOCATION@@</c> for this invocation's id.</param>
    /// <param name="exitCode">What the host exits with.</param>
    public static string HostThatWrites(string resultJson, int exitCode = 0)
    {
        string encoded = Convert.ToBase64String(Encoding.UTF8.GetBytes(resultJson));
        return $"""
            $ErrorActionPreference = 'Stop'
            $runFolder = Join-Path $env:PRINTFLOW_REGRESSION_SET_ROOT "runs\$env:PRINTFLOW_REGRESSION_RUN_ID"
            New-Item -ItemType Directory -Path $runFolder -Force | Out-Null
            $json = [Text.Encoding]::UTF8.GetString([Convert]::FromBase64String('{encoded}'))
            $json = $json.Replace('@@INVOCATION@@', $env:PRINTFLOW_REGRESSION_INVOCATION_ID)
            Set-Content -LiteralPath (Join-Path $runFolder 'result.json') -Value $json -Encoding utf8
            exit {exitCode}
            """;
    }

    // ------------------------------------------------------------------------------------------
    // Building evidence
    // ------------------------------------------------------------------------------------------

    /// <summary>
    /// A complete, honest, passing run result for this synthetic workstation.
    /// </summary>
    /// <remarks>
    /// Built through <see cref="StandardRegressionSetRunResult.From"/> and serialised by the run
    /// result's own writer, so what the tests hand the revalidation script is the shape a real run
    /// produces rather than a hand-written approximation of it. The decisions it carries are marked
    /// synthetic and are attributed to a synthetic decider: nothing here invents an operator.
    /// </remarks>
    public StandardRegressionSetRunResult PassingRun(
        string runId,
        string invocationId,
        string? productVersion = null,
        ImmutableArray<ProductAssemblyIdentity> candidate = default,
        string? setId = null)
    {
        ImmutableArray<ProductAssemblyIdentity> harness = ProductBuildIdentity.Running();

        return StandardRegressionSetRunResult.From(
            setId ?? SetId,
            runId,
            "2026-09-10T09:00:00+12:00",
            "2026-09-10T10:30:00+12:00",
            RunFolder(runId),
            Environment.MachineName,
            productVersion ?? InstalledProductVersion,
            WorkstationVerificationFixture.PresetId,
            WorkstationVerificationFixture.PresetVersion,
            "Production",
            StandardRegressionCategories.Required.Select(PassingCase),
            new RegressionEvidenceBinding(
                RegressionEvidenceBinding.CurrentVersion,
                invocationId,
                harness,
                "PrintFlow.Tests",
                InstallFolder,
                candidate.IsDefault ? ProductBuildIdentity.FromFolder(InstallFolder) : candidate,
                [],
                PresetSha256,
                ObservedOsBuild,
                _meituSha256,
                _photoshopSha256,
                RegressionEvidenceBinding.DigestOfSet(SetManifests()),
                SetManifests(),
                BuildOrigin));
    }

    /// <summary>
    /// Builds a tiny assembly from different source under the loaded Product's informational label,
    /// then writes a self-consistent receipt whose harness contains that assembly.
    /// </summary>
    public (RegressionBuildOrigin Origin, ImmutableArray<ProductAssemblyIdentity> HarnessProductAssemblies)
        WriteDifferentSourceHarnessReceiptWithSameLabels()
    {
        ProductAssemblyIdentity loadedInfrastructure = ProductBuildIdentity.Running().Single(
            identity => identity.Name == "PrintFlow.Infrastructure.dll");
        string buildIdentity = loadedInfrastructure.BuildIdentity
            ?? throw new InvalidOperationException("The loaded Infrastructure build identity is unavailable.");

        string firstAssembly = BuildSyntheticInfrastructure("FirstSource", buildIdentity);

        string folder = Path.Combine(_root, $"different-source-harness-{Guid.NewGuid():N}");
        Directory.CreateDirectory(folder);
        foreach (string path in Directory.EnumerateFiles(HarnessFolder))
        {
            File.Copy(path, Path.Combine(folder, Path.GetFileName(path)));
        }

        string changed = Path.Combine(folder, "PrintFlow.Infrastructure.dll");
        File.Copy(firstAssembly, changed, overwrite: true);
        ImmutableArray<ProductAssemblyIdentity> identities = ProductBuildIdentity.FromFolder(folder);
        RegressionBuildOrigin origin = WriteBuildPairReceipt(
            Path.Combine(_root, $"different-harness-{Guid.NewGuid():N}.json"), folder, identities);
        return (origin, identities);
    }

    /// <summary>The set's manifests as a run reads them.</summary>
    public ImmutableArray<RegressionSetManifestIdentity> SetManifests() =>
    [
        .. StandardRegressionSet.Load(SetRoot).Assets
            .OrderBy(a => a.FixtureId, StringComparer.OrdinalIgnoreCase)
            .Select(a => new RegressionSetManifestIdentity(
                a.FixtureId, a.Category, a.ManifestSha256!, a.Sha256)),
    ];

    /// <summary>Writes a run result where a run would have written it.</summary>
    public string WriteResult(StandardRegressionSetRunResult run)
    {
        string folder = RunFolder(run.RunId);
        Directory.CreateDirectory(folder);
        string path = Path.Combine(folder, "result.json");
        File.WriteAllText(path, run.ToJson());
        return path;
    }

    private static RegressionCaseResult PassingCase(string category) =>
        new(
            AssetId: $"FIX-{category}",
            Category: category,
            Outcome: RegressionOutcome.Passed,
            Workflow: "SyntheticWorkflow",
            StepsExecuted: ["Prepare", "Produce"],
            ExternalApplications: [],
            AdapterIds: ["synthetic"],
            ProducedArtefacts: [],
            Assertions: [new RegressionAssertion("Synthetic", true, "Recorded by a protocol test.")],
            ManualDecisions: [],
            EvidencePath: null,
            Detail: "Synthetic case recorded by a protocol test; no external application was driven.");

    private static string DigestOf(string path) =>
        Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));

    private RegressionBuildOrigin WriteBuildPairReceipt(
        string receiptPath,
        string harnessFolder,
        ImmutableArray<ProductAssemblyIdentity> harnessProductAssemblies)
    {
        string pairId = Guid.NewGuid().ToString();
        object[] harnessArtifacts =
        [
            .. Directory.EnumerateFiles(harnessFolder, "*", SearchOption.AllDirectories)
                .Select(path => new
                {
                    Name = Path.GetRelativePath(harnessFolder, path).Replace('\\', '/'),
                    Sha256 = DigestOf(path),
                }),
        ];

        File.WriteAllText(receiptPath, JsonSerializer.Serialize(new
        {
            Version = 1,
            PairId = pairId,
            SourceRevision = "SYNTHETIC-PROTOCOL-TEST",
            InputDigest = PresetSha256,
            Inputs = new[] { new { Name = "synthetic-workstation-preset.json", Sha256 = PresetSha256 } },
            SdkVersion = Environment.Version.ToString(),
            DotnetPath = Environment.ProcessPath ?? "SYNTHETIC-DOTNET",
            HarnessCommand = new[] { "SYNTHETIC", "copy-test-host" },
            CandidateCommand = new[] { "SYNTHETIC", "copy-candidate" },
            HarnessFolder = harnessFolder,
            CandidateFolder = InstallFolder,
            HarnessProductAssemblies = harnessProductAssemblies,
            CandidateProductAssemblies = ProductBuildIdentity.FromFolder(InstallFolder),
            HarnessArtifacts = harnessArtifacts,
            CompletedAtUtc = DateTimeOffset.UtcNow.ToString("O"),
        }, new JsonSerializerOptions { WriteIndented = true }));

        return new RegressionBuildOrigin(receiptPath, DigestOf(receiptPath), pairId);
    }

    private string BuildSyntheticInfrastructure(string typeName, string buildIdentity)
    {
        string projectFolder = Path.Combine(_root, "synthetic-source", typeName);
        string outputFolder = Path.Combine(projectFolder, "output");
        Directory.CreateDirectory(projectFolder);
        File.WriteAllText(Path.Combine(projectFolder, "Synthetic.cs"),
            $"namespace SyntheticOrigin; public sealed class {typeName} {{ public string Value => nameof({typeName}); }}");
        File.WriteAllText(Path.Combine(projectFolder, "Synthetic.csproj"), $$"""
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
                <AssemblyName>PrintFlow.Infrastructure</AssemblyName>
                <Version>{{System.Security.SecurityElement.Escape(buildIdentity)}}</Version>
                <AssemblyInformationalVersion>{{System.Security.SecurityElement.Escape(buildIdentity)}}</AssemblyInformationalVersion>
                <IncludeSourceRevisionInInformationalVersion>false</IncludeSourceRevisionInInformationalVersion>
                <Deterministic>true</Deterministic>
              </PropertyGroup>
            </Project>
            """);

        string dotnet = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Microsoft", "dotnet", "dotnet.exe");
        ProcessStartInfo start = new()
        {
            FileName = File.Exists(dotnet) ? dotnet : "dotnet",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            WorkingDirectory = projectFolder,
        };
        foreach (string argument in new[] { "build", "Synthetic.csproj", "-c", "Release", "--nologo", "-o", outputFolder })
        {
            start.ArgumentList.Add(argument);
        }

        using Process process = Process.Start(start)
            ?? throw new InvalidOperationException("The synthetic compiler did not start.");
        string output = process.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd();
        process.WaitForExit();
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException("The synthetic origin assembly did not build: " + output);
        }

        return Path.Combine(outputFolder, "PrintFlow.Infrastructure.dll");
    }

    // ------------------------------------------------------------------------------------------
    // The set
    // ------------------------------------------------------------------------------------------

    /// <summary>
    /// Seven manifests that satisfy Layer 1 without being real imagery.
    /// </summary>
    /// <remarks>
    /// Layer 1 checks that each manifest states its structural facts and that the file on disk has
    /// the length and digest the manifest records — it does not decode the file. So a small
    /// deterministic byte string per category is enough to exercise the evidence protocol, and no
    /// customer-like imagery is copied anywhere near a temporary directory.
    /// </remarks>
    private void WriteSet()
    {
        foreach (string category in StandardRegressionCategories.Required)
        {
            string fixtureId = $"FIX-{category}";
            string inputPath = Path.Combine(SetRoot, "inputs", $"{fixtureId}.bin");
            byte[] bytes = Encoding.UTF8.GetBytes($"synthetic bytes for {category}");
            File.WriteAllBytes(inputPath, bytes);

            Dictionary<string, object> file = new()
            {
                ["path"] = inputPath,
                ["length"] = bytes.Length,
                ["sha256"] = Convert.ToHexString(SHA256.HashData(bytes)),
                ["format"] = "Synthetic",
            };

            switch (category)
            {
                case "PSD_WITH_COMPOSITE_PREVIEW":
                    file["psd"] = new { hasRealMergedData = true, colourMode = "RGB" };
                    break;
                case "SINGLE_PAGE_PDF":
                    file["pdf"] = new { pageCount = 1, isPasswordProtected = false };
                    break;
                case "REFERENCE_PRODUCTION_TIFF":
                    file["tiff"] = new
                    {
                        samplesPerPixel = 5,
                        photometricInterpretation = 5,
                        compression = 1,
                    };
                    break;
            }

            File.WriteAllText(
                Path.Combine(ManifestFolder, $"{fixtureId}.json"),
                JsonSerializer.Serialize(new
                {
                    schemaVersion = 2,
                    setId = SetId,
                    fixtureId,
                    category,
                    file,
                    expectedWorkflow = "SyntheticWorkflow",
                    expectedProcessingPath = new[] { "Prepare", "Produce" },
                    expectedExternalApplications = Array.Empty<string>(),
                    comparisonPolicy = new { mode = "Structural" },
                    manualChecks = Array.Empty<object>(),
                }, new JsonSerializerOptions { WriteIndented = true }));
        }
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, recursive: true);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // A temporary directory that will not delete is not a test failure.
        }
    }
}
