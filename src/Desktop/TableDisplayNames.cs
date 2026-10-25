namespace Vatowiec.Desktop;

// Presentation only: persisted table IDs and the API contract remain unchanged.
public static class TableDisplayNames
{
    public static string For(string? tableId) => tableId switch
    {
        "Contractors" => "Kontrahenci",
        "Currencies" => "Waluty",
        "CurrencyRates" => "Kursy walut",
        null => "",
        _ => "Tabela"
    };
}
