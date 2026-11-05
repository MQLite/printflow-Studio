using System.IO;

namespace PrintFlow.Infrastructure.Adapters.Meitu;

/// <summary>Follows Meitu's installed active version within the configured installation root.</summary>
internal static class MeituExecutablePathResolver
{
    internal static string Resolve(string configuredPath)
    {
        try
        {
            string? directory = Path.GetDirectoryName(configuredPath);
            if (string.IsNullOrEmpty(directory)) return configuredPath;

            // Presets can name either the launcher or a historical version subdirectory.
            string? root = Version.TryParse(Path.GetFileName(directory), out _)
                ? Path.GetDirectoryName(directory)
                : directory;
            if (string.IsNullOrEmpty(root)) return configuredPath;

            string configuration = Path.Combine(root, "Config.ini");
            if (!File.Exists(configuration)) return configuredPath;

            bool mainSection = false;
            foreach (string raw in File.ReadLines(configuration))
            {
                string line = raw.Trim();
                if (line.StartsWith(';') || line.StartsWith('#')) continue;
                if (line.StartsWith('[') && line.EndsWith(']'))
                {
                    mainSection = line.Equals("[Main]", StringComparison.OrdinalIgnoreCase);
                    continue;
                }

                int separator = line.IndexOf('=');
                if (!mainSection || separator < 0 ||
                    !line[..separator].Trim().Equals("Version", StringComparison.OrdinalIgnoreCase)) continue;

                string version = line[(separator + 1)..].Trim();
                // Only a version component may select a sibling directory, never an arbitrary path.
                if (!Version.TryParse(version, out _)) return configuredPath;
                string executable = Path.Combine(root, version, Path.GetFileName(configuredPath));
                return File.Exists(executable) ? executable : configuredPath;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            // The normal executable availability check reports a missing/unreadable installation.
        }

        return configuredPath;
    }
}
