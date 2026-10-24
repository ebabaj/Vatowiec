using System.Globalization;
using Avalonia.Data.Converters;
using Vatowiec.SharedContracts;

namespace Vatowiec.Desktop.Converters;

public sealed class FinanceCurrencyLabelConverter : IMultiValueConverter
{
    public object Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
    {
        if (values.Count < 2 || values[0] is not string code) return "";
        var currency = (values[1] as FinanceCurrency[])?.FirstOrDefault(x => x.Code == code);
        return code + (currency is { IsActive: false } ? " (nieaktywna)" : "");
    }
}
