using PrintFlow.Domain.AiDesign;
using PrintFlow.Domain.Results;

namespace PrintFlow.Workflow.AiDesign;

public sealed class PromptTemplateService
{
    public OperationResult<AiDesignPrompt> Compose(string templateId, IReadOnlyDictionary<string,string> fields, IReadOnlyList<AiDesignAttachment> attachments, string language)
    {
        bool zh = language.StartsWith("zh", StringComparison.OrdinalIgnoreCase);
        var specification = Fields(templateId);
        if (specification is null) return OperationResult.Fail<AiDesignPrompt>(FailureCode.PreconditionNotMet, "Unknown AI template.");
        string required = templateId == "text-print" ? "text" : "subject";
        if (!fields.TryGetValue(required, out var value) || string.IsNullOrWhiteSpace(value))
            return OperationResult.Fail<AiDesignPrompt>(FailureCode.PreconditionNotMet, "Required AI field: " + required);
        var lines = new List<string> { zh ? "为印花创作一张设计素材。" : "Create a design asset for printing." };
        foreach (var field in specification)
            if (fields.TryGetValue(field.Key, out var input) && !string.IsNullOrWhiteSpace(input)) lines.Add((zh ? field.Zh : field.En) + ": " + input);
        lines.Add(zh ? "默认居中构图，完整主体，清晰边缘，避免水印。" : "Use a centered composition, complete subject, clear edges, and avoid any watermark.");
        foreach (var a in attachments.OrderBy(a => a.Order)) lines.Add((zh ? "参考文件 " : "Reference file ") + a.UploadName + ": " + a.Purpose);
        var prompt = new AiDesignPrompt(templateId, 1, new Dictionary<string,string>(fields, StringComparer.Ordinal), string.Join("\n", lines),
            attachments.OrderBy(a => a.Order).Select(a => a.Id).ToArray(),
            zh ? "生成一张图片，并提供可以下载的原始结果。" : "Generate one image and provide the original downloadable result.");
        var valid = Validate(prompt, attachments);
        return valid.IsSuccess ? OperationResult.Ok(prompt) : OperationResult.Fail<AiDesignPrompt>(valid.Failure);
    }
    public OperationResult<Unit> Validate(AiDesignPrompt prompt, IReadOnlyList<AiDesignAttachment> attachments)
    {
        if (Fields(prompt.TemplateId) is null || prompt.TemplateVersion != 1 || string.IsNullOrWhiteSpace(prompt.Text))
            return OperationResult.Fail<Unit>(FailureCode.PreconditionNotMet, "Invalid AI prompt template or final text.");
        string required = prompt.TemplateId == "text-print" ? "text" : "subject";
        if (!prompt.Fields.TryGetValue(required, out var value) || string.IsNullOrWhiteSpace(value))
            return OperationResult.Fail<Unit>(FailureCode.PreconditionNotMet, "Required AI field: " + required);
        if (attachments.Select(a => a.Id).Distinct().Count() != attachments.Count || attachments.Select(a => a.UploadName).Distinct(StringComparer.Ordinal).Count() != attachments.Count
            || prompt.AttachmentReferences.Distinct().Count() != prompt.AttachmentReferences.Count
            || prompt.AttachmentReferences.Any(id => !attachments.Any(a => a.Id == id)))
            return OperationResult.Fail<Unit>(FailureCode.PreconditionNotMet, "Missing or duplicate attachment reference.");
        return OperationResult.Ok();
    }
    public string RenderHandoff(AiDesignPrompt prompt, IReadOnlyList<AiDesignAttachment> attachments)
    {
        if (Validate(prompt, attachments).IsFailure) throw new ArgumentException("Invalid AI prompt references.", nameof(prompt));
        string text = prompt.Text + "\n\n" + prompt.HandoffInstructions;
        if (attachments.Count > 0) text += "\n\nUpload these required files before generating / 生成前请上传以下必要文件:\n"
            + string.Join("\n", attachments.OrderBy(a => a.Order).Select(a => a.UploadName + ": " + a.Purpose));
        return text;
    }
    private sealed record Field(string Key, string En, string Zh);
    private static Field[]? Fields(string template) => template switch
    {
        "subject" => [new("subject","Subject","主体"),new("style","Style","风格"),new("colors","Colors","颜色"),new("background","Background","背景要求"),new("composition","Composition","构图"),new("aspectRatio","Aspect ratio","期望比例")],
        "badge" => [new("subject","Subject","主体"),new("shape","Shape","形状"),new("style","Style","风格"),new("colorCount","Color count","色数"),new("text","Text","文字"),new("aspectRatio","Aspect ratio","期望比例")],
        "text-print" => [new("text","Exact text to preserve","必须保留的文字"),new("fontStyle","Font style","字体风格"),new("decorations","Decorations","装饰元素"),new("colors","Colors","颜色"),new("aspectRatio","Aspect ratio","期望比例")],
        _ => null
    };
}
