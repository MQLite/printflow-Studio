namespace PrintFlow.App.Navigation;

/// <summary>
/// Asks the operator for exactly one input file (Epic 11100 Part 3C2 §4).
/// </summary>
/// <remarks>
/// A seam, not an abstraction over the file system: it returns a path and nothing else — it
/// never opens, reads, copies or inspects the file. That work belongs to
/// <see cref="Workflow.Services.ISessionService.ImportAsync"/>, which is the only code allowed
/// to touch the operator's file at all (plan §17.4).
/// <para>
/// It exists so an automated test can supply a synthetic file without a modal dialog.
/// </para>
/// </remarks>
public interface IFilePicker
{
    /// <summary>
    /// Returns the absolute path of the chosen file, or null when the operator cancelled.
    /// </summary>
    /// <remarks>
    /// Single selection is structural: the return type cannot express a second file, so
    /// "batching" cannot arrive here by accident. The drop path enforces the same rule
    /// explicitly because Windows will hand it as many paths as the operator dragged.
    /// </remarks>
    string? PickSingleFile(string dialogTitle, string filter);
}
