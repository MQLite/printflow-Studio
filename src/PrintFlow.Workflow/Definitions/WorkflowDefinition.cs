using PrintFlow.Domain.Sessions;

namespace PrintFlow.Workflow.Definitions;

/// <summary>
/// The immutable, code-reviewed shape of one fixed workflow.
/// </summary>
/// <remarks>
/// Definitions are static code, never configuration and never database rows. A
/// user-configurable workflow engine is an explicit MVP exclusion (design §2.2), so adding
/// a fourth workflow costs a code change and a code review — which is the intended price.
/// </remarks>
public sealed record WorkflowDefinition(WorkflowType Type, IReadOnlyList<StepDefinition> Steps)
{
    /// <summary>The first step of the workflow.</summary>
    public StepDefinition First => Steps[0];

    /// <summary>The final step, whose approval concludes the workflow.</summary>
    public StepDefinition Terminal => Steps[^1];

    public bool Contains(StepKind kind) => IndexOf(kind) >= 0;

    public int IndexOf(StepKind kind)
    {
        for (int i = 0; i < Steps.Count; i++)
        {
            if (Steps[i].Kind == kind)
            {
                return i;
            }
        }

        return -1;
    }

    /// <summary>Returns the definition of <paramref name="kind"/>, or null when this workflow has no such step.</summary>
    public StepDefinition? Find(StepKind kind)
    {
        int index = IndexOf(kind);
        return index < 0 ? null : Steps[index];
    }

    /// <summary>Returns the step after <paramref name="kind"/>, or null when it is the terminal step.</summary>
    public StepDefinition? Next(StepKind kind)
    {
        int index = IndexOf(kind);
        return index < 0 || index + 1 >= Steps.Count ? null : Steps[index + 1];
    }
}
