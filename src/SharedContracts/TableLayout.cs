namespace Vatowiec.SharedContracts;

public sealed record TableLayout(string[] VisibleColumns, string[]? ColumnOrder = null,
    Dictionary<string, double>? ColumnWidths = null, int Version = 2);

public static class TableLayouts
{
    public const double MinimumWidth = 64;
    public const double MaximumWidth = 1200;
    // Includes the existing 8 DIP margins on both sides of the text.
    public static double DefaultWidth(string id) => id is "Name" or "ParentName" ? 266 : 166;
    public static bool ValidWidth(double width) => double.IsFinite(width) && width >= MinimumWidth && width <= MaximumWidth;

    public static TableLayout Effective(TableDefinition table, string[] permissions, TableLayout? own, TableLayout? installation)
    {
        var available = table.Available(permissions);
        var selected = own ?? installation;
        var allowed = available.Select(c => c.Id).ToHashSet(StringComparer.Ordinal);
        var order = (selected?.ColumnOrder ?? []).Where(allowed.Contains).Concat(available.Select(c => c.Id)).Distinct(StringComparer.Ordinal).ToArray();
        var widths = available.ToDictionary(c => c.Id, c => selected?.ColumnWidths?.TryGetValue(c.Id, out var width) == true && ValidWidth(width)
            ? width : DefaultWidth(c.Id), StringComparer.Ordinal);
        return new(TableCatalog.EffectiveColumns(table, permissions, own?.VisibleColumns, installation?.VisibleColumns), order, widths);
    }

    public static TableLayout From(TableViewResponse view) => new(view.VisibleColumns, view.ColumnOrder, view.ColumnWidths);
    public static TableColumn[] Visible(TableViewResponse view)
    {
        var columns = view.Columns.ToDictionary(c => c.Id, StringComparer.Ordinal);
        return (view.ColumnOrder ?? view.Columns.OrderBy(c => c.Order).Select(c => c.Id).ToArray())
            .Where(id => view.VisibleColumns.Contains(id, StringComparer.Ordinal) && columns.ContainsKey(id))
            .Select(id => columns[id] with { Width = view.ColumnWidths?.GetValueOrDefault(id, DefaultWidth(id)) ?? DefaultWidth(id) }).ToArray();
    }
}
