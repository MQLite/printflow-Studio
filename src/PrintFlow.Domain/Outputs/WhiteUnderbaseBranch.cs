namespace PrintFlow.Domain.Outputs;

/// <summary>
/// The validated white-underbase contraction branch for the <c>W1</c> spot channel.
/// </summary>
/// <remarks>
/// The confirmed procedure loads the layer's non-transparent pixels, applies a
/// content-dependent inward contraction, and creates <c>W1</c> at 100% density
/// (MVP design §12).
///
/// There is deliberately <b>no default and no <c>Unspecified</c> member usable as one</b>.
/// Classification — fine detail 0 px, ordinary design 1 px, large solid rectangle 2 px —
/// applies to the complete final design and is an explicit operator and review decision.
/// The system must never infer it. That requirement is why the branch is a required,
/// non-nullable input to the Photoshop port and a precondition of starting that step.
/// </remarks>
public enum WhiteUnderbaseBranch
{
    /// <summary>0 px contraction — fine-detail graphics, especially fine white details.</summary>
    W1_0px = 0,

    /// <summary>1 px contraction — ordinary designs.</summary>
    W1_1px = 1,

    /// <summary>2 px contraction — a large solid rectangle or similar content.</summary>
    W1_2px = 2,
}
