using PrintFlow.Workflow.Delivery;

namespace PrintFlow.Workflow.Ports;

/// <summary>
/// Opens a colleague-correction folder for the operator (SCRUM-11148, design §8.4).
/// </summary>
/// <remarks>
/// Separate from the delivered-file shell on purpose: this opens a managed correction folder the
/// service resolved, never a delivered file, and it needs no delivery lease. It only dispatches a
/// request to the Windows shell; a successful result is not an observation that Explorer showed
/// anything. It creates, changes and deletes nothing, and grants nothing.
/// </remarks>
public interface ICorrectionFolderShell
{
    /// <summary>
    /// Opens <paramref name="absoluteFolder"/> with <paramref name="preferredFileName"/> selected,
    /// or <paramref name="fallbackFileName"/> when that is missing, or the folder itself selected in
    /// its parent when neither file exists.
    /// </summary>
    ShellDispatchResult Open(string absoluteFolder, string? preferredFileName, string? fallbackFileName);
}
