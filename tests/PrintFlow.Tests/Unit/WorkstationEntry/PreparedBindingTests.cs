using System.IO;
using System.Text.Json;
using PrintFlow.WorkstationEntry;

namespace PrintFlow.Tests.Unit.WorkstationEntry;

public sealed class PreparedBindingTests
{
    [Theory]
    [InlineData("candidate")]
    [InlineData("scenario")]
    [InlineData("run")]
    [InlineData("owner")]
    [InlineData("evidence")]
    public void Prepared_record_must_bind_every_current_authority(string changed)
    {
        (string root, RunOwnership owner) = Fixture();
        PreparedRun prepared = new(changed == "candidate" ? "other" : owner.CandidateHash,
            changed == "scenario" ? "other" : owner.ScenarioHash,
            changed == "run" ? "other" : owner.RunToken,
            changed == "owner" ? "other" : owner.OwnerToken,
            DateTimeOffset.UtcNow, changed == "evidence" ? "PASS" : "NONINTERACTIVE_ONLY", new string('A', 64));
        File.WriteAllText(Path.Combine(root, "state", "prepared.json"), JsonSerializer.Serialize(prepared));
        Should.Throw<ArgumentException>(() => OwnedRun.VerifyResume(root, owner.CandidateHash, owner.ScenarioHash, true));
    }

    [Fact]
    public void Missing_prepared_record_refuses_and_matching_record_validates_without_a_graph()
    {
        (string root, RunOwnership owner) = Fixture();
        Should.Throw<IOException>(() => OwnedRun.VerifyResume(root, owner.CandidateHash, owner.ScenarioHash, true));
        PreparedRun prepared = new(owner.CandidateHash, owner.ScenarioHash, owner.RunToken, owner.OwnerToken,
            DateTimeOffset.UtcNow, "NONINTERACTIVE_ONLY", new string('A', 64));
        File.WriteAllText(Path.Combine(root, "state", "prepared.json"), JsonSerializer.Serialize(prepared));
        OwnedRun.VerifyResume(root, owner.CandidateHash, owner.ScenarioHash, true).ShouldBe(owner);
    }

    private static (string Root, RunOwnership Owner) Fixture()
    {
        string root = Path.Combine(EntryTestPaths.Repository, "artifacts", "pf-opux-scrum11154-workstation-entry", "refusal-fixtures", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "state"));
        using NativePathLease held = NativePathLease.Ancestors(root);
        RunOwnership owner = new(Path.GetFileName(root), held.Identity, "candidate", "scenario", int.MaxValue, 0, "run", "owner");
        File.WriteAllText(Path.Combine(root, "ownership.json"), JsonSerializer.Serialize(owner));
        return (root, owner);
    }
}
