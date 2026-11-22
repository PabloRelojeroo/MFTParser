using System.Globalization;
using System.Windows.Data;
using MftParser.Services;

namespace MftParser.Ui;

public sealed class FilterKind : IValueConverter {
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        EntryFilter.ColumnKind(value as string ?? "");

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
