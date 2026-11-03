using PrintFlow.Domain.AiDesign.Authoring;
using PrintFlow.Domain.AiDesign.Memorial;
using PrintFlow.Domain.Results;

namespace PrintFlow.Workflow.AiDesign.Memorial;

public interface IMemorialTextLayout
{
    OperationResult<IReadOnlyList<MemorialTextLine>> Arrange(AiAuthoringDraft draft, MemorialCanvas canvas, IReadOnlyList<MemorialTextStyle> styles);
}
