using System.Globalization;
using System.Windows.Data;
using PrintFlow.Domain.AiDesign.Authoring;
using PrintFlow.App.Resources;

namespace PrintFlow.App.Views.AiDesign;

public sealed class PromptHistoryPreviewConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is AiPromptDocument document ? string.Concat(document.Parts.Select(x => x is AiTextPart text ? text.Text : "[" + Strings.AiPrompt_Photos + "]")) : "";
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
}
