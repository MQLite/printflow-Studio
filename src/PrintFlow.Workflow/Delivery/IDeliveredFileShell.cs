namespace PrintFlow.Workflow.Delivery;

/// <summary>
/// Selects one delivered file in its containing folder (SCRUM-11145). Consumes a live verified
/// selection lease synchronously; the caller disposes the lease immediately afterwards.
/// </summary>
/// <remarks>
/// A successful result means the shell request was dispatched. It is not a human observation
/// that Explorer visibly selected the file.
/// </remarks>
public interface IDeliveredFileShell
{
    ShellDispatchResult SelectInFolder(IDeliveredSelectionLease lease);
}
