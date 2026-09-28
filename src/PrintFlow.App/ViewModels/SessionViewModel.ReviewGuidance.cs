using PrintFlow.App.Resources;
using PrintFlow.Domain.Sessions;

namespace PrintFlow.App.ViewModels;

/// <summary>Which review the "What to check" section describes; <see cref="None"/> hides it.</summary>
public enum ReviewGuidanceStep
{
    None,
    Enhancement,
    BackgroundRemoval,
    Trim,
    ProductionTiff,
}

/// <summary>
/// "What to check" beside a review (SCRUM-11149).
/// </summary>
/// <remarks>
/// A read-only projection of the review already on screen: two to four checks that use the tools
/// already here, what Approve and Reject actually do, and where to get help. It reaches no service
/// and holds no state. The two help buttons in the view are the existing Ask-a-colleague and
/// Adjust-trim-edges entries with their own commands and eligibility; this file only decides
/// whether the entry exists for the result under review, so the wording never offers what the
/// workflow layer does not.
/// </remarks>
public sealed partial class SessionViewModel
{
    /// <summary>
    /// The step the section describes: only while the exact current result is under review, and
    /// never while an exclusive mode (Ask panel, trim editor, manual crop) is the task instead.
    /// </summary>
    public ReviewGuidanceStep ReviewGuidance =>
        ReviewTargetIdentity is null || IsAskingColleague || IsAdjustingTrim || IsCropping
            ? ReviewGuidanceStep.None
            : _session?.CurrentStep?.Step switch
            {
                StepKind.Enhancement => ReviewGuidanceStep.Enhancement,
                StepKind.BackgroundRemoval => ReviewGuidanceStep.BackgroundRemoval,
                StepKind.Trim => ReviewGuidanceStep.Trim,
                StepKind.PhotoshopOutput => ReviewGuidanceStep.ProductionTiff,
                _ => ReviewGuidanceStep.None,
            };

    public bool ShowsReviewGuidance => ReviewGuidance != ReviewGuidanceStep.None;

    public string ReviewGuidanceHeading => Strings.Session_GuidanceHeading;

    /// <summary>The checks for this review, each naming a tool by its on-screen label.</summary>
    public IReadOnlyList<string> ReviewGuidanceChecks => ReviewGuidance switch
    {
        ReviewGuidanceStep.Enhancement =>
        [
            FormatNext(Strings.Session_GuidanceEnhancementCompare, BeforeLabel, AfterLabel,
                Strings.Session_ReviewModeSideBySide, Strings.Session_ReviewModeSlider),
            Strings.Session_GuidanceEnhancementDetail,
            Strings.Session_GuidanceEnhancementArtefacts,
        ],
        ReviewGuidanceStep.BackgroundRemoval =>
        [
            FormatNext(Strings.Session_GuidanceBackgroundColours,
                Strings.Session_ReviewBackgroundWhite, Strings.Session_ReviewBackgroundBlack),
            Strings.Session_GuidanceBackgroundEdges,
            FormatNext(Strings.Session_GuidanceBackgroundKept, BeforeLabel),
        ],
        ReviewGuidanceStep.Trim =>
        [
            FormatNext(Strings.Session_GuidanceTrimCompare, BeforeLabel, AfterLabel),
            Strings.Session_GuidanceTrimEmpty,
            Strings.Session_GuidanceTrimEdges,
        ],
        ReviewGuidanceStep.ProductionTiff =>
        [
            FormatNext(Strings.Session_GuidanceTiffWhiteInk, Strings.Session_TiffModeWhiteInk),
            FormatNext(Strings.Session_GuidanceTiffOverlay, Strings.Session_TiffModeOverlay),
            FormatNext(Strings.Session_GuidanceTiffColour, Strings.Session_TiffModeColour),
            FormatNext(Strings.Session_GuidanceTiffSize, Strings.Session_TiffReviewHeading, Strings.Session_TiffLabelPhysicalSize),
        ],
        _ => [],
    };

    /// <summary>What Approve records. Never a save, and on the TIFF never a print-quality claim.</summary>
    public string ReviewGuidanceApprove => ReviewGuidance switch
    {
        ReviewGuidanceStep.None => string.Empty,
        ReviewGuidanceStep.ProductionTiff => FormatNext(Strings.Session_GuidanceApproveTiff, ApproveLabel),
        _ => FormatNext(Strings.Session_GuidanceApprove, ApproveLabel),
    };

    /// <summary>
    /// What Reject does. On the TIFF: disposal first, and a refused disposal records nothing; the
    /// step then waits for Retry and Run step — no new TIFF is made by the rejection itself.
    /// </summary>
    public string ReviewGuidanceReject => ReviewGuidance switch
    {
        ReviewGuidanceStep.None => string.Empty,
        ReviewGuidanceStep.ProductionTiff => FormatNext(Strings.Session_GuidanceRejectTiff, RejectLabel, RetryLabel, RunStepLabel),
        _ => FormatNext(Strings.Session_GuidanceReject, RejectLabel),
    };

    /// <summary>Where to get help, naming only an entry that exists for this result.</summary>
    public string ReviewGuidanceHelp => ReviewGuidance switch
    {
        ReviewGuidanceStep.Enhancement => Strings.Session_GuidanceHelpEnhancement,
        ReviewGuidanceStep.BackgroundRemoval => ShowsGuidanceAskColleague
            ? Strings.Session_GuidanceHelpAskColleague
            : FormatNext(Strings.Session_GuidanceHelpNoColleague, RejectLabel),
        ReviewGuidanceStep.Trim => ShowsGuidanceAdjustTrim
            ? FormatNext(Strings.Session_GuidanceHelpAdjustTrim, TrimAdjustBeginLabel)
            : FormatNext(Strings.Session_GuidanceHelpNoAdjustTrim, RejectLabel),
        ReviewGuidanceStep.ProductionTiff => ReviewBoundsTarget is not null && CanReturnToStep
            ? Strings.Session_GuidanceHelpTiff + FormatNext(Strings.Session_GuidanceHelpTiffReturn, ReturnHeading)
            : Strings.Session_GuidanceHelpTiff,
        _ => string.Empty,
    };

    /// <summary>
    /// The Ask entry exists for this result. Whether it can be pressed right now stays
    /// <see cref="CanAskColleague"/>, which the button binds to.
    /// </summary>
    public bool ShowsGuidanceAskColleague =>
        ReviewGuidance == ReviewGuidanceStep.BackgroundRemoval && _session?.CanAskColleague == true;

    /// <summary>The trim editor exists for this result; <see cref="CanAdjustTrim"/> still decides pressing.</summary>
    public bool ShowsGuidanceAdjustTrim =>
        ReviewGuidance == ReviewGuidanceStep.Trim && _session?.TrimAdjustment is not null;

    private void NotifyReviewGuidanceChanged()
    {
        OnPropertyChanged(nameof(ReviewGuidance));
        OnPropertyChanged(nameof(ShowsReviewGuidance));
        OnPropertyChanged(nameof(ReviewGuidanceChecks));
        OnPropertyChanged(nameof(ReviewGuidanceApprove));
        OnPropertyChanged(nameof(ReviewGuidanceReject));
        OnPropertyChanged(nameof(ReviewGuidanceHelp));
        OnPropertyChanged(nameof(ShowsGuidanceAskColleague));
        OnPropertyChanged(nameof(ShowsGuidanceAdjustTrim));
    }
}
