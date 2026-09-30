using System.IO;
using PrintFlow.WorkstationEntry;

namespace PrintFlow.Tests.Unit.WorkstationEntry;

public sealed class EntryPlanTests
{
    private static string Repository => FindRepository();
    private static string RunRoot => Path.Combine(Repository, "artifacts", "pf-opux-scrum11154-workstation-entry", "runs", "not-created-static-test");
    private static CandidateManifest Candidate => new(Repository, new string('a', 40), [@"D:\PrintFlowStudio"], []);
    private static ScenarioManifest Scenario => new("not-created-static-test", "Fake", ["F1", "F2", "F3", "F4", "F5", "F6"]);

    [Theory]
    [InlineData("relative")]
    [InlineData(@"D:\")]
    [InlineData(@"D:\PrintFlowStudio\test")]
    [InlineData(@"\\server\share\test")]
    [InlineData(@"\\?\D:\owned")]
    public void Unowned_or_ambiguous_roots_refuse(string root)
        => Should.Throw<ArgumentException>(() => EntryPlan.Validate(root, Candidate, Scenario));

    [Fact]
    public void Production_adapter_refuses_before_creating_run()
    {
        Should.Throw<ArgumentException>(() => EntryPlan.Validate(RunRoot, Candidate, Scenario with { AdapterMode = "Production" }));
        Directory.Exists(RunRoot).ShouldBeFalse();
    }

    [Fact]
    public void Root_overlapping_explicit_exclusion_refuses()
        => Should.Throw<ArgumentException>(() => EntryPlan.Validate(RunRoot,
            Candidate with { ExcludedRoots = [Path.GetDirectoryName(RunRoot)!] }, Scenario));

    [Fact]
    public void Duplicate_scenarios_and_traversal_refuse()
    {
        Should.Throw<ArgumentException>(() => EntryPlan.Validate(RunRoot, Candidate, Scenario with { Families = ["F1", "F1"] }));
        Should.Throw<ArgumentException>(() => EntryPlan.Validate(RunRoot + @"\..\other", Candidate, Scenario));
    }

    [Fact]
    public void Valid_plan_does_not_create_run_directory()
    {
        EntryPlan.Validate(RunRoot, Candidate, Scenario).Root.ShouldBe(RunRoot);
        Directory.Exists(RunRoot).ShouldBeFalse();
    }

    private static string FindRepository()
    {
        for (DirectoryInfo? directory = new(Environment.CurrentDirectory); directory is not null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "PrintFlowStudio.sln"))) return directory.FullName;
        throw new InvalidOperationException("Tests require the repository working directory.");
    }
}
