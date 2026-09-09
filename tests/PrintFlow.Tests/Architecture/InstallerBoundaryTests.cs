using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace PrintFlow.Tests.Architecture;

/// <summary>
/// The packaging contract, enforced against the repository rather than against a build that may
/// or may not have been run (SCRUM-11123 Parts C, D, G, P, Q, R).
/// </summary>
/// <remarks>
/// These tests read the installer's own definition files — Version.props, payload-policy.json,
/// the .wixproj, Package.wxs and the packaging scripts — because those are what the next release
/// build will obey. Asserting against a produced .msi instead would only prove something about
/// the last time somebody happened to run the build; the smokes recorded in the completion
/// report cover that half.
/// </remarks>
public sealed class InstallerBoundaryTests
{
    // ------------------------------------------------------------------------------------------
    // Part D — one version source
    // ------------------------------------------------------------------------------------------

    /// <summary>
    /// The product version is stated once. Assembly, installer, artefact name and documentation
    /// all read that one property, so raising a release version is a single edit and cannot leave
    /// two numbers disagreeing about what shipped (§11).
    /// </summary>
    [Fact]
    public void The_product_version_is_declared_in_exactly_one_place()
    {
        string versionProps = ReadRepositoryFile("Version.props");

        MatchCollection declarations = Regex.Matches(versionProps, @"<PrintFlowVersion>([^<]+)</PrintFlowVersion>");
        declarations.Count.ShouldBe(1, "Version.props must declare PrintFlowVersion exactly once.");

        string version = declarations[0].Groups[1].Value.Trim();
        version.ShouldMatch(@"^\d+\.\d+\.\d+$");

        // The root build props maps it onto <Version>, which is what stamps every assembly.
        ReadRepositoryFile("Directory.Build.props")
            .ShouldContain("<Version>$(PrintFlowVersion)</Version>");
        ReadRepositoryFile("Directory.Build.props")
            .ShouldContain(@"Version.props");
    }

    /// <summary>
    /// The running assemblies carry that version, so the revalidation check's idea of "which
    /// PrintFlow is installed" is the same number the installer names.
    /// </summary>
    [Fact]
    public void Every_product_assembly_carries_the_declared_version()
    {
        string declared = DeclaredVersion();

        foreach (Type marker in new[]
                 {
                     typeof(Domain.Results.FailureCode),
                     typeof(Workflow.Ports.IEnvironmentGate),
                     typeof(Infrastructure.Verification.ProductionRevalidationRecord),
                     typeof(App.Composition.ApplicationStartup),
                 })
        {
            Version assembly = marker.Assembly.GetName().Version!;
            $"{assembly.Major}.{assembly.Minor}.{assembly.Build}"
                .ShouldBe(declared, marker.Assembly.GetName().Name);
        }
    }

    /// <summary>
    /// The installer restates no version of its own: it imports Version.props and derives both
    /// the MSI ProductVersion and the artefact name from it (§11, §12).
    /// </summary>
    [Fact]
    public void The_installer_project_derives_its_version_rather_than_restating_one()
    {
        string wixproj = ReadRepositoryFile(@"installer\PrintFlowStudio.Installer\PrintFlowStudio.Installer.wixproj");

        wixproj.ShouldContain(@"Version.props");
        wixproj.ShouldContain("<OutputName>PrintFlowStudio-$(PrintFlowVersion)-win-x64</OutputName>");
        wixproj.ShouldContain("ProductVersion=$(PrintFlowVersion)");

        // The declared version appearing as a literal in a packaging input would be a second
        // source of the same fact — the one thing Part D §11 forbids. The WiX SDK's own pinned
        // version is a build-tool dependency and a different fact entirely, which is why this
        // looks for the product's version specifically rather than for any version-shaped text.
        foreach (string file in new[]
                 {
                     @"installer\PrintFlowStudio.Installer\PrintFlowStudio.Installer.wixproj",
                     @"installer\PrintFlowStudio.Installer\Package.wxs",
                     @"build\installer\Build-Installer.ps1",
                 })
        {
            Declarations(file).ShouldNotContain(
                DeclaredVersion(), Case.Sensitive, $"{file} must not hard-code the product version.");
        }
    }

    /// <summary>
    /// The artefact is named for its version. An ambiguous setup.msi cannot be told apart from
    /// the previous one, and the rollback procedure has to name a specific installer (§12).
    /// </summary>
    [Fact]
    public void The_installer_artefact_name_carries_the_version_and_architecture()
    {
        string expected = $"PrintFlowStudio-{DeclaredVersion()}-win-x64.msi";

        expected.ShouldNotBe("setup.msi");
        expected.ShouldContain(DeclaredVersion());
        expected.ShouldContain("win-x64");
    }

    // ------------------------------------------------------------------------------------------
    // Part C — publish model and payload boundary
    // ------------------------------------------------------------------------------------------

    /// <summary>
    /// Self-contained win-x64, declared in the project and used by the build script. The
    /// installer must never need to fetch a .NET runtime on the validated workstation (§9, §26).
    /// </summary>
    [Fact]
    public void The_release_payload_is_a_self_contained_win_x64_publish()
    {
        ReadRepositoryFile(@"src\PrintFlow.App\PrintFlow.App.csproj")
            .ShouldContain("<RuntimeIdentifiers>win-x64</RuntimeIdentifiers>");

        string script = ReadRepositoryFile(@"build\installer\Build-Installer.ps1");
        script.ShouldContain("-c Release");
        script.ShouldContain("-r win-x64");
        script.ShouldContain("--self-contained true");
    }

    /// <summary>
    /// The payload policy denies by default and names what it will not ship: tests, source,
    /// project files, any SQLite database, signing or secret material, customer imagery, the
    /// local override configuration and .git content (§10, §28).
    /// </summary>
    [Fact]
    public void The_payload_policy_denies_tests_source_data_and_secrets()
    {
        PayloadPolicy policy = ReadPayloadPolicy();

        foreach (string pattern in new[]
                 {
                     "**/*.cs", "**/*.csproj", "**/*.db", "**/*.pfx", "**/*.psd", "**/*.tif",
                     "**/*.atn", "**/appsettings.local.json", "**/.git/**", "**/*.Tests.dll",
                     "**/xunit*.dll", "**/*.pdb",
                 })
        {
            policy.DenyAlways.ShouldContain(pattern);
        }

        // The allowlist is the whole of what may ship. Nothing in it may reach a second file kind.
        foreach (string denied in new[]
                 {
                     "PrintFlow.Tests.dll", "xunit.core.dll", "Program.cs", "printflow.db",
                     "appsettings.local.json", "signing.pfx", "customer.psd", "reference.tif",
                     "PrintFlow.App.pdb", "PrintFlow.App.csproj", "evidence/run.json",
                     "a/b/c/deep.dll",
                 })
        {
            ShouldNotBeAllowed(policy, denied);
        }
    }

    /// <summary>
    /// And it does admit what the application actually needs, so the boundary is a boundary
    /// rather than a wall (§10).
    /// </summary>
    [Fact]
    public void The_payload_policy_admits_the_application_and_its_runtime()
    {
        PayloadPolicy policy = ReadPayloadPolicy();

        foreach (string allowed in new[]
                 {
                     "PrintFlow.App.exe", "PrintFlow.App.dll", "PrintFlow.Infrastructure.dll",
                     "PrintFlow.App.deps.json", "PrintFlow.App.runtimeconfig.json",
                     "appsettings.json", "System.Private.CoreLib.dll", "PresentationCore.dll",
                     "e_sqlite3.dll", "zh-Hans/PresentationCore.resources.dll",
                     "zh-CN/PrintFlow.App.resources.dll",
                 })
        {
            ShouldBeAllowed(policy, allowed);
        }

        foreach (string required in new[]
                 {
                     "PrintFlow.App.exe", "PrintFlow.App.dll", "PrintFlow.Domain.dll",
                     "PrintFlow.Workflow.dll", "PrintFlow.Infrastructure.dll",
                     "PrintFlow.App.deps.json", "PrintFlow.App.runtimeconfig.json",
                     "appsettings.json",
                 })
        {
            policy.RequiredFiles.ShouldContain(required);
        }
    }

    /// <summary>
    /// A denied file is a build failure, not a quiet drop — with symbols the one stated
    /// exception, archived beside the .msi rather than installed (§10, §28).
    /// </summary>
    [Fact]
    public void A_denied_file_in_the_publish_fails_the_packaging_build()
    {
        string script = ReadRepositoryFile(@"build\installer\Build-Installer.ps1");

        script.ShouldContain("denyAlways");
        script.ShouldContain("throw");
        script.ShouldContain("the payload policy denies outright");
        script.ShouldContain("SymbolsDir");
    }

    // ------------------------------------------------------------------------------------------
    // Part F / Part K — configuration and data preservation
    // ------------------------------------------------------------------------------------------

    /// <summary>
    /// The installer authors application files and one shortcut, and nothing under the production
    /// workspace. Because Windows Installer removes only what it installed, that is what makes
    /// "an upgrade or uninstall does not remove customer data" a structural fact rather than a
    /// promise (§25).
    /// </summary>
    [Fact]
    public void The_package_installs_nothing_into_the_production_workspace()
    {
        string wxs = Declarations(@"installer\PrintFlowStudio.Installer\Package.wxs");

        // The only directories the package writes into.
        wxs.ShouldContain("ProgramFiles64Folder");
        wxs.ShouldContain("ProgramMenuFolder");

        foreach (string forbidden in new[]
                 {
                     @"D:\PrintFlowStudio", "InputSnapshots", "Revisions", "PrintOutput",
                     "printflow.db", "Sessions", "Quarantine",
                 })
        {
            wxs.ShouldNotContain(forbidden, Case.Insensitive);
        }

        // Nothing may remove a directory it did not create, and nothing may offer to wipe data.
        wxs.ShouldNotContain("RemoveFile", Case.Insensitive);
        wxs.ShouldNotContain("util:RemoveFolderEx", Case.Insensitive);
    }

    /// <summary>
    /// appsettings.local.json — the operator's validated deviations — is never installed and
    /// therefore never replaced or removed by an upgrade or uninstall (§15).
    /// </summary>
    [Fact]
    public void The_local_configuration_override_is_never_installer_owned()
    {
        ReadPayloadPolicy().DenyAlways.ShouldContain("**/appsettings.local.json");

        Declarations(@"installer\PrintFlowStudio.Installer\Package.wxs")
            .ShouldNotContain("appsettings.local.json");
    }

    // ------------------------------------------------------------------------------------------
    // Part P — the no-automatic-updater guard
    // ------------------------------------------------------------------------------------------

    /// <summary>
    /// One proportionate architecture boundary: nothing in the product or in the packaging can
    /// look for, fetch or apply a new version (§16, §35).
    /// </summary>
    /// <remarks>
    /// Scoped to what an updater would actually need — an HTTP client, a download call, a
    /// scheduled task, a Run key, a package manager, or a custom action that could run any of
    /// them — rather than a general sweep for the word "update", which appears legitimately all
    /// over a codebase that updates rows and view models. The product half of this repository has
    /// never had a network client at all, and this test is what keeps that true through the one
    /// change that would most plausibly introduce one.
    /// </remarks>
    [Fact]
    public void Nothing_in_the_product_or_the_packaging_can_fetch_a_new_version()
    {
        string[] updaterApis =
        [
            "HttpClient", "WebClient", "WebRequest", "HttpRequestMessage", "Invoke-WebRequest",
            "Start-BitsTransfer", "System.Net.Http", "DownloadFile", "DownloadString",
            "schtasks", "ScheduledTask", "Register-ScheduledTask",
            @"CurrentVersion\Run", "winget", "choco install",
        ];

        List<string> offenders = [];

        foreach (string file in ProductAndPackagingSources())
        {
            string text = DeclarationsOf(file);
            foreach (string api in updaterApis)
            {
                if (text.Contains(api, StringComparison.OrdinalIgnoreCase))
                {
                    offenders.Add($"{RepositoryRelative(file)}: {api}");
                }
            }
        }

        offenders.ShouldBeEmpty(
            "SCRUM-11123 requires no automatic application update mechanism. Upgrade happens only " +
            "when the operator deliberately runs a newer offline installer.");
    }

    /// <summary>
    /// The package installs no service, no scheduled task, no startup entry and no custom action
    /// — so there is nothing installed that could run on its own at all (§16).
    /// </summary>
    [Fact]
    public void The_package_installs_nothing_that_runs_by_itself()
    {
        string wxs = Declarations(@"installer\PrintFlowStudio.Installer\Package.wxs");

        foreach (string forbidden in new[]
                 {
                     "ServiceInstall", "ServiceControl", "CustomAction", "ScheduledTask",
                     "RunOnce", "Autostart", "StartupFolder", "Bundle", "PackageGroupRef",
                 })
        {
            wxs.ShouldNotContain(forbidden, Case.Insensitive);
        }
    }

    // ------------------------------------------------------------------------------------------
    // Part Q — no signing infrastructure
    // ------------------------------------------------------------------------------------------

    /// <summary>
    /// The original requirement asks for none of it, so none of it is here. Existing SHA-256 and
    /// preset-manifest integrity behaviour is untouched — that is a different mechanism and this
    /// test does not object to it (§36).
    /// </summary>
    [Fact]
    public void No_signing_certificate_or_timestamp_infrastructure_is_introduced()
    {
        List<string> offenders = [];

        foreach (string file in PackagingSources())
        {
            string text = DeclarationsOf(file);
            foreach (string api in new[]
                     {
                         "signtool", "Authenticode", "Set-AuthenticodeSignature", "X509Certificate",
                         "timestamp.digicert", "SignOutput", "CertificateThumbprint",
                     })
            {
                if (text.Contains(api, StringComparison.OrdinalIgnoreCase))
                {
                    offenders.Add($"{RepositoryRelative(file)}: {api}");
                }
            }
        }

        offenders.ShouldBeEmpty("SCRUM-11123 does not ask for code signing, and none is introduced.");
    }

    // ------------------------------------------------------------------------------------------
    // Part H — the application cannot approve itself
    // ------------------------------------------------------------------------------------------

    /// <summary>
    /// PrintFlow reads the production revalidation record and has no way to write one. The
    /// operator's revalidation procedure writes it, after Environment Readiness and the standard
    /// regression set have both been run (Part H §18).
    /// </summary>
    [Fact]
    public void The_application_can_read_a_revalidation_record_but_never_write_one()
    {
        string source = ReadRepositoryFile(
            @"src\PrintFlow.Infrastructure\Verification\ProductionRevalidation.cs");

        source.ShouldContain("interface IProductionRevalidationReader");
        source.ShouldNotContain("interface IProductionRevalidationWriter");

        List<string> offenders = [];
        foreach (string file in ProductSources())
        {
            string text = DeclarationsOf(file);
            if (!text.Contains("production-revalidation.json", StringComparison.OrdinalIgnoreCase) &&
                !text.Contains("ProductionRevalidationRecord", StringComparison.Ordinal))
            {
                continue;
            }

            foreach (string write in new[] { "File.WriteAllText", "File.WriteAllBytes", "File.Create", "StreamWriter" })
            {
                if (text.Contains(write, StringComparison.Ordinal))
                {
                    offenders.Add($"{RepositoryRelative(file)}: {write}");
                }
            }
        }

        offenders.ShouldBeEmpty(
            "No product source that knows about the revalidation record may write a file. An " +
            "application that can write its own production approval approves itself.");
    }

    // ------------------------------------------------------------------------------------------
    // Helpers
    // ------------------------------------------------------------------------------------------

    private sealed record PayloadPolicy(
        int PolicyVersion,
        IReadOnlyList<string> Allow,
        IReadOnlyList<string> DenyAlways,
        IReadOnlyList<string> RequiredFiles);

    private static PayloadPolicy ReadPayloadPolicy()
    {
        using JsonDocument document = JsonDocument.Parse(ReadRepositoryFile(@"installer\payload-policy.json"));
        JsonElement root = document.RootElement;

        return new PayloadPolicy(
            root.GetProperty("policyVersion").GetInt32(),
            [.. root.GetProperty("allow").EnumerateArray().Select(e => e.GetString()!)],
            [.. root.GetProperty("denyAlways").EnumerateArray().Select(e => e.GetString()!)],
            [.. root.GetProperty("requiredFiles").EnumerateArray().Select(e => e.GetString()!)]);
    }

    /// <summary>
    /// The same decision the stager makes, in the same order: an explicit deny wins, then the
    /// allowlist, then default deny.
    /// </summary>
    private static bool IsAllowed(PayloadPolicy policy, string relativePath) =>
        !policy.DenyAlways.Any(p => GlobMatches(p, relativePath)) &&
        policy.Allow.Any(p => GlobMatches(p, relativePath));

    private static void ShouldBeAllowed(PayloadPolicy policy, string path) =>
        IsAllowed(policy, path).ShouldBeTrue($"'{path}' must be allowed into the payload.");

    private static void ShouldNotBeAllowed(PayloadPolicy policy, string path) =>
        IsAllowed(policy, path).ShouldBeFalse($"'{path}' must never reach the installer payload.");

    /// <summary>The stager's glob semantics: '**' crosses separators, '*' does not.</summary>
    private static bool GlobMatches(string pattern, string path)
    {
        System.Text.StringBuilder regex = new("^");
        for (int i = 0; i < pattern.Length; i++)
        {
            char c = pattern[i];
            if (c == '*')
            {
                if (i + 1 < pattern.Length && pattern[i + 1] == '*')
                {
                    if (i + 2 < pattern.Length && pattern[i + 2] == '/')
                    {
                        regex.Append("(?:.*/)?");
                        i += 2;
                        continue;
                    }

                    regex.Append(".*");
                    i++;
                    continue;
                }

                regex.Append("[^/]*");
                continue;
            }

            if (c == '?')
            {
                regex.Append("[^/]");
                continue;
            }

            regex.Append(Regex.Escape(c.ToString()));
        }

        regex.Append('$');
        return Regex.IsMatch(path, regex.ToString(), RegexOptions.IgnoreCase);
    }

    private static string DeclaredVersion() =>
        Regex.Match(ReadRepositoryFile("Version.props"), @"<PrintFlowVersion>([^<]+)</PrintFlowVersion>")
            .Groups[1].Value.Trim();

    private static IEnumerable<string> ProductSources() =>
        Directory.EnumerateFiles(Path.Combine(RepositoryRoot(), "src"), "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains(@"\obj\", StringComparison.OrdinalIgnoreCase) &&
                        !f.Contains(@"\bin\", StringComparison.OrdinalIgnoreCase));

    private static IEnumerable<string> PackagingSources()
    {
        string root = RepositoryRoot();

        foreach (string directory in new[] { "installer", "build", "tools" })
        {
            string path = Path.Combine(root, directory);
            if (!Directory.Exists(path))
            {
                continue;
            }

            foreach (string file in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
            {
                if (file.Contains(@"\obj\", StringComparison.OrdinalIgnoreCase) ||
                    file.Contains(@"\bin\", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                yield return file;
            }
        }
    }

    private static IEnumerable<string> ProductAndPackagingSources() =>
        ProductSources().Concat(PackagingSources());

    /// <summary>
    /// A repository file with its commentary removed, so a guard reads what the file
    /// <i>declares</i> rather than what its comments discuss.
    /// </summary>
    /// <remarks>
    /// Necessary rather than fastidious. Package.wxs explains at length that it installs no
    /// service and no scheduled task, payload-policy.json lists the key and database extensions
    /// it exists to exclude, and this repository's house style is to write down why. A scan over
    /// raw text would read every one of those explanations as the thing it forbids, and the way
    /// to keep the guard meaningful is to make it read declarations — not to stop explaining.
    /// </remarks>
    private static string Declarations(string relativePath) =>
        DeclarationsOf(Path.Combine(RepositoryRoot(), relativePath));

    private static string DeclarationsOf(string absolutePath)
    {
        string text = File.ReadAllText(absolutePath);

        switch (Path.GetExtension(absolutePath).ToLowerInvariant())
        {
            case ".wxs":
            case ".xml":
            case ".props":
            case ".targets":
            case ".csproj":
            case ".wixproj":
            case ".resx":
                return Regex.Replace(text, "<!--.*?-->", " ", RegexOptions.Singleline);

            case ".ps1":
            case ".psm1":
                text = Regex.Replace(text, @"<#.*?#>", " ", RegexOptions.Singleline);
                return Regex.Replace(text, @"(?m)#.*$", " ");

            case ".cs":
                text = Regex.Replace(text, @"/\*.*?\*/", " ", RegexOptions.Singleline);
                return Regex.Replace(text, @"(?m)//.*$", " ");

            case ".json":
                // This repository's JSON comment convention is a "$"-prefixed property holding an
                // array of lines, which JSON itself has no syntax for.
                return Regex.Replace(
                    text, "\"\\$[A-Za-z]*\"\\s*:\\s*\\[.*?\\]", " ", RegexOptions.Singleline);

            default:
                return text;
        }
    }

    private static string ReadRepositoryFile(string relativePath)
    {
        string full = Path.Combine(RepositoryRoot(), relativePath);
        File.Exists(full).ShouldBeTrue($"Expected '{relativePath}' to exist in the repository.");
        return File.ReadAllText(full);
    }

    private static string RepositoryRelative(string absolutePath) =>
        Path.GetRelativePath(RepositoryRoot(), absolutePath);

    private static string RepositoryRoot()
    {
        DirectoryInfo? current = new(AppContext.BaseDirectory);
        while (current is not null && !File.Exists(Path.Combine(current.FullName, "PrintFlowStudio.sln")))
        {
            current = current.Parent;
        }

        return current?.FullName ?? throw new InvalidOperationException(
            "Could not locate the repository root (PrintFlowStudio.sln) above " + AppContext.BaseDirectory);
    }
}
