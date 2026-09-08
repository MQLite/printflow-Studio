using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PrintFlow.App.Navigation;
using PrintFlow.App.Resources;
using PrintFlow.Domain.Files;
using PrintFlow.Domain.Results;
using PrintFlow.Domain.Sessions;
using PrintFlow.Workflow.Commands;
using PrintFlow.Workflow.Definitions;
using PrintFlow.Workflow.Engine;
using PrintFlow.Workflow.Services;

namespace PrintFlow.App.ViewModels;

/// <summary>
/// One of the three fixed workflows, flattened for display.
/// </summary>
/// <remarks>
/// <see cref="Type"/> is the internal value that is persisted; <see cref="Title"/> is the only
/// part an operator reads. There are exactly three of these and no way to add a fourth — the
/// catalogue is the configuration (MVP design §6.1).
/// </remarks>
public sealed class WorkflowChoice
{
    internal WorkflowChoice(WorkflowDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);

        Type = definition.Type;
        Title = DisplayNames.Workflow(definition.Type);
        Steps = new ReadOnlyCollection<string>(definition.Steps.Select(Describe).ToList());
    }

    /// <summary>The persisted workflow value. Never displayed.</summary>
    public WorkflowType Type { get; }

    /// <summary>
    /// The stable identity a UI Automation driver finds this choice by (SCRUM-11078 §22).
    /// </summary>
    /// <remarks>
    /// Built from <see cref="Type"/> and never from <see cref="Title"/>: an AutomationId is a
    /// contract with a driver and must not move when the product is translated. There is
    /// exactly one per catalogue entry, which is why it lives beside the entry rather than in
    /// the markup.
    /// </remarks>
    public string AutomationId => Type switch
    {
        WorkflowType.PrepareAsset => "WorkflowSelection.PrepareDesignAsset",
        WorkflowType.PrepareCustomerDesign => "WorkflowSelection.PrepareCustomerDesign",
        WorkflowType.GeneratePrintTiff => "WorkflowSelection.GeneratePrintTiff",
        _ => "WorkflowSelection." + Type,
    };

    /// <summary>The localised workflow name.</summary>
    public string Title { get; }

    /// <summary>The workflow's steps, so the choice is informed rather than a bare name.</summary>
    public IReadOnlyList<string> Steps { get; }

    private static string Describe(StepDefinition step)
    {
        List<string> flags = [];
        if (step.IsSkippable)
        {
            flags.Add(Strings.Flag_Skippable);
        }

        if (step.RequiresReview)
        {
            flags.Add(Strings.Flag_RequiresReview);
        }

        string name = DisplayNames.Step(step.Kind);
        return flags.Count == 0
            ? string.Create(CultureInfo.CurrentCulture, $"{step.Ordinal + 1}. {name}")
            : string.Create(CultureInfo.CurrentCulture, $"{step.Ordinal + 1}. {name} ({string.Join(", ", flags)})");
    }
}

/// <summary>
/// Workflow Selection: the screen shown immediately after a successful import
/// (Epic 11100 Part 3C2 §6, §7).
/// </summary>
/// <remarks>
/// The choice is applied with <see cref="WorkflowCommand.SelectWorkflow"/> and nothing else.
/// There is no assignment to a session's workflow anywhere in this file, and the workflow-lock
/// rule is not restated here: <see cref="CanSelect"/> reads the engine's own
/// <c>AvailableCommands</c>, so the buttons are enabled by exactly the rule that would accept
/// the click, and a refusal still comes back through the command path if the session changed
/// underneath (MVP design invariant 12).
/// <para>
/// The same screen carries the source context and the editable Output Name the original AC asks
/// for (SCRUM-11075, SCRUM-11078): the file that was imported, a picture of it, and the name the
/// produced files will be built from — all before a workflow is chosen, so a corrupted or
/// meaningless name can be fixed without first entering the Session. Neither addition is a
/// second authority. The name is validated by <see cref="OutputName.Create"/> and committed with
/// <see cref="WorkflowCommand.SetOutputName"/>; the picture comes from
/// <see cref="IArtefactPreviewService"/>, which cannot write a file or advance a session.
/// </para>
/// </remarks>
public sealed partial class WorkflowSelectionViewModel : ObservableObject
{
    private readonly ISessionService _sessions;
    private readonly IArtefactPreviewService _previews;
    private readonly INavigationService _navigation;

    [ObservableProperty]
    private string? _notice;

    [ObservableProperty]
    private bool _isBusy;

    /// <summary>
    /// What the operator has typed into the Output Name box.
    /// </summary>
    /// <remarks>
    /// Uncommitted by definition — it is a text box, and nothing has validated it yet. What the
    /// session actually holds is <see cref="SessionName"/>, and the two are reconciled in
    /// <see cref="SelectAsync"/> before any workflow starts, never in the setter: validating on
    /// every keystroke would put a rejection in front of an operator halfway through typing a
    /// name that is about to be fine.
    /// </remarks>
    [ObservableProperty]
    private string _outputName = string.Empty;

    /// <summary>The imported design, decoded for display, or a sentence saying why not.</summary>
    [ObservableProperty]
    private ArtefactPreviewPane? _preview;

    private SessionView? _session;

    public WorkflowSelectionViewModel(
        ISessionService sessions, IArtefactPreviewService previews, INavigationService navigation)
    {
        ArgumentNullException.ThrowIfNull(sessions);
        ArgumentNullException.ThrowIfNull(previews);
        ArgumentNullException.ThrowIfNull(navigation);

        _sessions = sessions;
        _previews = previews;
        _navigation = navigation;

        Workflows = new ReadOnlyCollection<WorkflowChoice>(
            WorkflowCatalog.All.Select(definition => new WorkflowChoice(definition)).ToList());
    }

    /// <summary>The three fixed workflows, in menu order, straight from the catalogue.</summary>
    public IReadOnlyList<WorkflowChoice> Workflows { get; }

    public string Heading => Strings.WorkflowSelection_Heading;

    public string Hint => Strings.WorkflowSelection_Hint;

    public string BackLabel => Strings.Nav_BackToHome;

    public string SelectLabel => Strings.WorkflowSelection_Select;

    public string SourceFileLabel => Strings.WorkflowSelection_SourceFileLabel;

    public string OutputNameLabel => Strings.WorkflowSelection_OutputNameLabel;

    /// <summary>What the operator is editing, and what it is not (§8).</summary>
    public string OutputNameHint => Strings.WorkflowSelection_OutputNameHint;

    public string PreviewHeading => Strings.WorkflowSelection_PreviewHeading;

    /// <summary>The output name of the session being set up, as the service last returned it.</summary>
    public string SessionName => _session?.OutputName.Value ?? string.Empty;

    /// <summary>
    /// The file the operator imported, by name (SCRUM-11078).
    /// </summary>
    /// <remarks>
    /// Shown beside <see cref="OutputName"/> and never merged with it. The source is what
    /// PrintFlow read and will never rename; the output name is what it will write. An operator
    /// who has just corrected a meaningless output name still has to be able to see which file
    /// they are correcting it for.
    /// </remarks>
    public string SourceFileName => _session?.SourceFileName ?? string.Empty;

    public bool HasSourceFileName => SourceFileName.Length > 0;

    /// <summary>True exactly when there is a pane to show, image or explanation.</summary>
    public bool HasPreview => Preview is not null;

    /// <summary>
    /// The in-flight preview load, or a completed task when there is nothing to load.
    /// </summary>
    /// <remarks>
    /// The convention <c>SessionViewModel.PreviewsLoaded</c> established, and for the same
    /// reason: navigation is synchronous and decoding is not, so a caller that needs the picture
    /// to be there awaits this rather than guessing. It never faults — every failure becomes a
    /// pane that says so.
    /// </remarks>
    public Task PreviewLoaded { get; private set; } = Task.CompletedTask;

    /// <summary>
    /// Whether the workflow may still be chosen, as the engine reports it.
    /// </summary>
    /// <remarks>
    /// False once a derived Revision exists — the workflow lock (MVP design §6.1). The rule
    /// lives in the engine; this is a reading of its answer, not a second copy of it.
    /// </remarks>
    public bool CanSelect =>
        _session?.AvailableCommands.Contains(CommandKind.SelectWorkflow) == true;

    /// <summary>
    /// Whether the Output Name may still be edited, as the engine reports it.
    /// </summary>
    /// <remarks>
    /// <see cref="CommandKind.SetOutputName"/> rather than a rule restated here, for the same
    /// reason <see cref="CanSelect"/> reads <see cref="CommandKind.SelectWorkflow"/>: an offered
    /// box and an accepted command must not be able to disagree.
    /// </remarks>
    public bool CanEditOutputName =>
        _session?.AvailableCommands.Contains(CommandKind.SetOutputName) == true;

    /// <summary>Points the screen at the session it is choosing a workflow for.</summary>
    /// <remarks>
    /// Synchronous, because navigation is. The picture is not: loading it starts here and
    /// completes on <see cref="PreviewLoaded"/>.
    /// </remarks>
    public void Open(SessionView session)
    {
        ArgumentNullException.ThrowIfNull(session);

        _session = session;
        Notice = CanSelect ? null : Strings.WorkflowSelection_Locked;

        // Seeded from what the session actually holds, which after an import is the sanitised
        // source stem the service derived (SessionService.ImportAsync). The default is not
        // recomputed here — a second derivation would be a second naming authority.
        OutputName = session.OutputName.Value;

        OnPropertyChanged(nameof(SessionName));
        OnPropertyChanged(nameof(SourceFileName));
        OnPropertyChanged(nameof(HasSourceFileName));
        OnPropertyChanged(nameof(CanSelect));
        OnPropertyChanged(nameof(CanEditOutputName));

        Preview = null;
        OnPropertyChanged(nameof(HasPreview));
        PreviewLoaded = LoadPreviewAsync(session, CancellationToken.None);
    }

    /// <summary>
    /// Fills <see cref="Preview"/> with the imported source, or with why there is no picture.
    /// </summary>
    /// <remarks>
    /// Informational only (§13). It asks the read-only preview seam for the session's own root
    /// Revision and does nothing else: no Revision is created, no ReviewDecision is recorded, no
    /// source byte is touched, and no adapter runs. A file the product knows it cannot draw at
    /// this stage — a PSD or a PDF, whose managed raster is prepared later in the Session — is
    /// not asked about at all and is labelled truthfully instead of guessed at (§14).
    /// </remarks>
    private async Task LoadPreviewAsync(SessionView session, CancellationToken cancellationToken)
    {
        if (session.RootRevisionId is not { } root)
        {
            return;
        }

        string fileName = session.SourceFileName ?? string.Empty;

        if (session.OriginalSourceFormat is ImageFormat.Psd or ImageFormat.Pdf)
        {
            Publish(ArtefactPreviewPane.WithoutImage(
                PreviewHeading, fileName, Strings.WorkflowSelection_PreviewNeedsPreparation));
            return;
        }

        OperationResult<ImagePreview> preview = await _previews
            .GetPreviewAsync(session.Id, root, cancellationToken)
            .ConfigureAwait(true);

        Publish(preview.IsSuccess
            ? ArtefactPreviewPane.From(PreviewHeading, fileName, preview.Value)
            : ArtefactPreviewPane.Unreadable(PreviewHeading, fileName, preview.Failure));

        void Publish(ArtefactPreviewPane pane)
        {
            // Only for the session still on screen. A load that started before the operator went
            // back and imported something else publishes nothing.
            if (!ReferenceEquals(_session, session))
            {
                return;
            }

            Preview = pane;
            OnPropertyChanged(nameof(HasPreview));
        }
    }

    [RelayCommand]
    private async Task SelectAsync(WorkflowChoice? choice, CancellationToken cancellationToken)
    {
        if (choice is null || _session is null || IsBusy)
        {
            return;
        }

        IsBusy = true;
        try
        {
            Notice = null;

            // The name first, and only then the workflow. Committing in this order is what stops
            // the race §19 describes: a workflow started against the session's old name while the
            // box on screen showed a new one. If the name cannot be accepted, nothing starts
            // (§20) — the operator stays here with a sentence explaining why.
            OperationResult<SessionView> named = await CommitOutputNameAsync(cancellationToken)
                .ConfigureAwait(true);
            if (named.IsFailure)
            {
                return;
            }

            OperationResult<SessionView> selected = await _sessions.ExecuteAsync(
                named.Value.Id,
                new WorkflowCommand.SelectWorkflow(choice.Type),
                Environment.UserName,
                cancellationToken).ConfigureAwait(true);

            if (selected.IsFailure)
            {
                // Includes the workflow lock: the engine refuses SelectWorkflow once a derived
                // Revision exists, and that refusal is shown rather than pre-empted. The name
                // that was accepted a moment ago stays accepted — a refused workflow does not
                // roll one back, and the screen must not claim it did.
                Notice = string.Format(
                    CultureInfo.CurrentCulture, Strings.WorkflowSelection_Refused, selected.Failure.Code);
                return;
            }

            _navigation.GoToSession(selected.Value);
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// Sends the edited Output Name through the existing engine command, or explains the refusal.
    /// </summary>
    /// <remarks>
    /// Validation is <see cref="OutputName.Create"/> — the operator-input half of the naming
    /// contract, which rejects rather than silently rewrites, so what an operator typed and what
    /// gets written can never quietly differ. The sanitising half (<c>OutputName.Sanitise</c>)
    /// stays where it belongs: deriving the default from the imported file's stem. Neither rule
    /// is restated here, and the message an operator reads is rendered from the authority's own
    /// <see cref="OutputName.ForbiddenCharacterList"/> and <see cref="OutputName.MaxLength"/>.
    /// <para>
    /// A name equal to the one the session already holds sends no command: re-committing an
    /// unchanged value would write a metadata transaction for a decision nobody made.
    /// </para>
    /// </remarks>
    private async Task<OperationResult<SessionView>> CommitOutputNameAsync(CancellationToken cancellationToken)
    {
        SessionView session = _session!;

        string typed = OutputName ?? string.Empty;
        if (string.Equals(typed, session.OutputName.Value, StringComparison.Ordinal))
        {
            return OperationResult.Ok(session);
        }

        Domain.Files.OutputName.NameValidation validated = Domain.Files.OutputName.Create(typed);
        if (!validated.IsValid)
        {
            Notice = string.Format(
                CultureInfo.CurrentCulture,
                Strings.WorkflowSelection_OutputNameRejected,
                Domain.Files.OutputName.ForbiddenCharacterList,
                Domain.Files.OutputName.MaxLength);

            return OperationResult.Fail<SessionView>(
                FailureCode.PreconditionNotMet, validated.Error);
        }

        OperationResult<SessionView> renamed = await _sessions.ExecuteAsync(
            session.Id,
            new WorkflowCommand.SetOutputName(validated.Name),
            Environment.UserName,
            cancellationToken).ConfigureAwait(true);

        if (renamed.IsFailure)
        {
            Notice = string.Format(
                CultureInfo.CurrentCulture,
                Strings.WorkflowSelection_OutputNameRefused,
                renamed.Failure.Code);
            return renamed;
        }

        Show(renamed.Value, Notice);
        return renamed;
    }

    /// <summary>Re-reads the screen from a view the service returned, keeping the operator's box.</summary>
    /// <remarks>
    /// <see cref="OutputName"/> is deliberately re-seeded from the returned view rather than left
    /// as it was typed: after a successful commit the two are equal, and after a refusal the
    /// operator must be able to see that nothing was written.
    /// </remarks>
    private void Show(SessionView session, string? notice)
    {
        _session = session;
        Notice = notice;
        OutputName = session.OutputName.Value;

        OnPropertyChanged(nameof(SessionName));
        OnPropertyChanged(nameof(SourceFileName));
        OnPropertyChanged(nameof(HasSourceFileName));
        OnPropertyChanged(nameof(CanSelect));
        OnPropertyChanged(nameof(CanEditOutputName));
    }

    [RelayCommand]
    private async Task BackToHomeAsync(CancellationToken cancellationToken) =>
        await _navigation.GoHomeAsync(cancellationToken).ConfigureAwait(true);
}
