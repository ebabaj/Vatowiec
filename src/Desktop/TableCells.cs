using System.Collections;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Media;
using Vatowiec.Desktop.ViewModels;
using Vatowiec.SharedContracts;

namespace Vatowiec.Desktop;

// Shared by table headers and rows. Only server-authorized catalog columns reach this presenter.
public sealed class TableCells : StackPanel
{
    public static readonly StyledProperty<object?> ValueProperty = AvaloniaProperty.Register<TableCells, object?>(nameof(Value));
    public static readonly StyledProperty<TableColumn[]?> ColumnsProperty = AvaloniaProperty.Register<TableCells, TableColumn[]?>(nameof(Columns));
    public object? Value { get => GetValue(ValueProperty); set => SetValue(ValueProperty, value); }
    public TableColumn[]? Columns { get => GetValue(ColumnsProperty); set => SetValue(ColumnsProperty, value); }
    public TableCells() { Orientation = Avalonia.Layout.Orientation.Horizontal; }
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property != ValueProperty && change.Property != ColumnsProperty) return;
        Children.Clear();
        if (Columns == null) return;
        foreach (var column in Columns)
        {
            var value = Value is CurrencySelection selection ? selection.Currency : Value;
            var text = Value == null ? column.Label : Format(Cell(value!, column.Id), column);
            var label = new TextBlock
            {
                Text = text,
                Width = column.Id is "Name" or "ParentName" ? 250 : 150,
                Margin = new Thickness(8, 6),
                TextWrapping = TextWrapping.Wrap,
                FontWeight = Value == null ? FontWeight.SemiBold : FontWeight.Normal
            };
            if (Value is CurrencySelection check && column == Columns[0])
            {
                var panel = new StackPanel { Orientation = Avalonia.Layout.Orientation.Horizontal };
                var box = new CheckBox { IsChecked = check.IsChecked, DataContext = check };
                box.Bind(CheckBox.IsCheckedProperty, new Binding(nameof(CurrencySelection.IsChecked)) { Mode = BindingMode.TwoWay });
                panel.Children.Add(box); panel.Children.Add(label); Children.Add(panel);
            }
            else Children.Add(label);
        }
    }
    public static object? Cell(object value, string id)
    {
        if (value is ContractorResponse c && id.StartsWith("Address", StringComparison.Ordinal))
            return c.RegisteredAddress?.GetType().GetProperty(id[7..])?.GetValue(c.RegisteredAddress);
        if (value is RateResponse && id == "Table") id = "TableType";
        return value.GetType().GetProperty(id)?.GetValue(value);
    }
    private static string Format(object? value, TableColumn column) => value switch
    {
        null => "",
        bool active when column.Format == "Activity" => active ? "Aktywny" : "Nieaktywny",
        bool yes => yes ? "Tak" : "Nie",
        DateTimeOffset date => (column.Format?.Contains("UTC", StringComparison.Ordinal) == true ? date.ToUniversalTime() : date.ToLocalTime()).ToString(column.Format, CultureInfo.GetCultureInfo("pl-PL")),
        IFormattable formatted => formatted.ToString(column.Format, CultureInfo.GetCultureInfo("pl-PL")),
        _ => value.ToString() ?? ""
    };
}
