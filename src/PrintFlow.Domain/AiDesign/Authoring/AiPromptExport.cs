namespace PrintFlow.Domain.AiDesign.Authoring;

public sealed record AiPromptExport(string Text, IReadOnlyList<AiDesignAttachment> RequiredAttachments,
    IReadOnlyList<AiAuthoringIssue> Issues);
