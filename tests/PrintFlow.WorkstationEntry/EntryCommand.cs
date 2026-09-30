namespace PrintFlow.WorkstationEntry;

public static class EntryCommand
{
    public static int Execute(string[] args, TextWriter output)
    {
        try
        {
            EntryOptions options = EntryOptions.Parse(args);
            using ValidatedInput input = ManifestReader.Read(options);
            if (options.Mode == "Validate")
            {
                output.WriteLine(System.Text.Json.JsonSerializer.Serialize(new { Status = "STATIC_VALIDATED", input.Plan.Root, input.CandidateHash, input.ScenarioHash, GraphConstructed = false }, ManifestReader.Json));
                return 0;
            }
            if (options.OwnedRestart) return OwnedRestart.SuperviseAsync(input, options, output).GetAwaiter().GetResult();
            return RuntimeBootstrap.Execute(input, options, output);
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or NotSupportedException or System.Text.Json.JsonException or UnauthorizedAccessException)
        {
            output.WriteLine($"REFUSED: {ex.Message}");
            return 2;
        }
    }
}

public sealed record EntryOptions(string Mode, string Root, string ScenarioManifest, string CandidateManifest,
    bool SafeDesktopConfirmed, bool Resume = false, bool OwnedRestart = false, string? RestartChildToken = null)
{
    public static EntryOptions Parse(string[] args)
    {
        Dictionary<string, string> values = new(StringComparer.Ordinal);
        for (int i = 0; i < args.Length; i++)
        {
            string key = args[i];
            if (key is not ("--Mode" or "--Root" or "--ScenarioManifest" or "--CandidateManifest" or "--SafeDesktopConfirmed" or "--Resume" or "--OwnedRestart" or "--RestartChildToken"))
                throw new ArgumentException($"Unknown option '{key}'.");
            string value = key is "--SafeDesktopConfirmed" or "--Resume" or "--OwnedRestart" ? "true" :
                ++i < args.Length ? args[i] : throw new ArgumentException($"Missing value for {key}.");
            if (!values.TryAdd(key, value)) throw new ArgumentException($"Duplicate option '{key}'.");
        }
        string Required(string key) => values.TryGetValue(key, out string? value) && !string.IsNullOrWhiteSpace(value)
            ? value : throw new ArgumentException($"Explicit {key} is required.");
        string mode = Required("--Mode");
        if (mode is not ("Validate" or "PrepareAndSmoke" or "Interactive")) throw new ArgumentException("Unknown mode.");
        bool acknowledged = values.ContainsKey("--SafeDesktopConfirmed");
        if (mode == "Interactive" && !acknowledged) throw new ArgumentException("Interactive requires fresh SafeDesktopConfirmed.");
        if (mode != "Interactive" && acknowledged) throw new ArgumentException("Desktop acknowledgment is only valid for Interactive.");
        bool restart = values.ContainsKey("--OwnedRestart");
        string? childToken = values.GetValueOrDefault("--RestartChildToken");
        if ((restart || childToken is not null) && mode != "PrepareAndSmoke") throw new ArgumentException("Owned restart is noninteractive only.");
        if (restart && (values.ContainsKey("--Resume") || childToken is not null)) throw new ArgumentException("Restart supervisor requires a fresh run and generates its own child token.");
        if (childToken is not null && (childToken.Length != 32 || !childToken.All(Uri.IsHexDigit))) throw new ArgumentException("Invalid child identity token.");
        return new(mode, Required("--Root"), Required("--ScenarioManifest"), Required("--CandidateManifest"), acknowledged, values.ContainsKey("--Resume"), restart, childToken);
    }
}
