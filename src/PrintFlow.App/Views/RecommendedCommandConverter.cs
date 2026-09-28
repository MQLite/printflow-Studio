using System.Globalization;
using System.Windows.Data;
using System.Windows.Input;

namespace PrintFlow.App.Views;

/// <summary>Compares existing command instances for visual emphasis only.</summary>
public sealed class RecommendedCommandConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture) =>
        values.Length == 2 && values[0] is ICommand command && ReferenceEquals(command, values[1]);

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
