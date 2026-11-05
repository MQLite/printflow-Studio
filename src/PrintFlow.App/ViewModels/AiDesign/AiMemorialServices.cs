using PrintFlow.App.Navigation;
using PrintFlow.Workflow.AiDesign.Memorial;

namespace PrintFlow.App.ViewModels.AiDesign;

public sealed record AiMemorialServices(
    IMemorialTextLayout TextLayout,
    MemorialCompositionModule Composition,
    IMemorialRenderStore Renders,
    IMemorialRenderPreview Preview,
    MemorialVersionModule Versions,
    MemorialReviewChecklist Reviews,
    IMemorialMaterialExporter Exporter,
    IAiDesignPngSavePicker SavePicker);
