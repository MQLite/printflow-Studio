namespace PrintFlow.App.Navigation;

/// <summary>Asks the operator where one local diagnostic ZIP should be saved.</summary>
public interface IDiagnosticPackageDestinationPicker
{
    /// <summary>Returns the selected absolute path, or null when the operator cancels.</summary>
    string? PickDestination(string dialogTitle, string filter, string suggestedFileName);
}
