using System.IO;

namespace PrintFlow.Tests.Unit.WorkstationEntry;

internal static class EntryTestPaths
{
    internal static string Repository
    {
        get
        {
            for (DirectoryInfo? directory = new(Environment.CurrentDirectory); directory is not null; directory = directory.Parent)
                if (File.Exists(Path.Combine(directory.FullName, "PrintFlowStudio.sln"))) return directory.FullName;
            throw new InvalidOperationException("Explicit repository fixture location unavailable.");
        }
    }
}
