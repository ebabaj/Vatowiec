using System.Text.Json;
using Vatowiec.SharedContracts;

namespace Vatowiec.Core.Infrastructure.Persistence;

public static class TableViewLayoutJson
{
    public static TableLayout? Read(string? json)
    {
        if (json == null) return null;
        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind == JsonValueKind.Array)
                return new(JsonSerializer.Deserialize<string[]>(json) ?? []);
            var layout = JsonSerializer.Deserialize<TableLayout>(json);
            return layout is { Version: 2, VisibleColumns: not null } ? layout : null;
        }
        catch (JsonException) { return null; }
    }
    public static string Write(TableLayout layout) => JsonSerializer.Serialize(layout);
}
