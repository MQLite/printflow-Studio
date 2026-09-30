using System.IO;
using PrintFlow.WorkstationEntry;

namespace PrintFlow.Tests.Unit.WorkstationEntry;

public sealed class OwnedPathsTests
{
    [Fact]
    public void Existing_outside_sentinel_is_refused_before_read_and_left_unchanged()
    {
        string parent = Path.Combine(EntryTestPaths.Repository, "artifacts", "pf-opux-scrum11154-workstation-entry", "refusal-fixtures", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(parent, "owned", "evidence"));
        string sentinel = Path.Combine(parent, "outside.txt");
        File.WriteAllText(sentinel, "harmless test-owned sentinel");
        using OwnedPaths paths = new(Path.Combine(parent, "owned"));
        Should.Throw<IOException>(() => paths.Read(sentinel, "evidence"));
        File.ReadAllText(sentinel).ShouldBe("harmless test-owned sentinel");
    }

    [Fact]
    public void An_unadmitted_fixture_is_not_an_import_authority()
    {
        string root = Path.Combine(EntryTestPaths.Repository, "artifacts", "pf-opux-scrum11154-workstation-entry", "refusal-fixtures", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "fixtures", "inputs"));
        string file = Path.Combine(root, "fixtures", "inputs", "source.png");
        File.WriteAllText(file, "synthetic bytes");
        using OwnedPaths paths = new(root);
        Should.Throw<IOException>(() => paths.Read(file, "fixtures"));
    }
}
