namespace Vatowiec.SharedContracts;

public sealed record TableColumn(string Id, string Label, string DataType, int Order,
    bool DefaultVisible = false, string? RequiredPermission = null, bool Searchable = true,
    string? Format = null, bool CanHide = true);
public sealed record TableDefinition(string Id, string ReadPermission, TableColumn[] Columns)
{
    public TableColumn[] Available(IEnumerable<string> permissions) => Columns.Where(c =>
        c.RequiredPermission == null || permissions.Contains(c.RequiredPermission, StringComparer.Ordinal)).OrderBy(c => c.Order).ToArray();
}
public sealed record TableViewResponse(string TableId, TableColumn[] Columns, string[] VisibleColumns,
    int PageSize, string Source, string? UserVersion, string? InstallationVersion);
public sealed record TableViewRequest(string[]? Columns = null, int? PageSize = null, string? RowVersion = null, bool Reset = false);

// Stable identifiers are the API/storage contract. Labels and presentation can evolve independently.
public static class TableCatalog
{
    public static readonly int[] PageSizes = [25, 50, 100, 200];
    public static readonly TableDefinition Contractors = new("Contractors", "ViewContractors", [
        new("Code", "Kod", "Text", 0, true), new("Name", "Nazwa kontrahenta", "Text", 1, true),
        new("TaxId", "NIP", "Text", 2, true), new("CountryCode", "Kraj", "Text", 3, true),
        new("IsActive", "Status", "Boolean", 4, true, Format: "Activity"),
        new("AddressCity", "Miasto", "Text", 5, true),
        new("VatEuId", "VAT UE", "Text", 6), new("Regon", "REGON", "Text", 7),
        new("Krs", "KRS", "Text", 8), new("Bdo", "BDO", "Text", 9),
        new("Pesel", "PESEL", "Text", 10, RequiredPermission: "ManageContractorPesel"),
        new("KsefIdWew", "Identyfikator wewnętrzny KSeF", "Text", 11),
        new("ParentName", "Kontrahent nadrzędny", "Text", 12),
        new("AddressName", "Adres rejestrowy — nazwa", "Text", 13),
        new("AddressStreet", "Ulica", "Text", 14), new("AddressBuildingNo", "Numer budynku", "Text", 15),
        new("AddressApartmentNo", "Numer lokalu", "Text", 16), new("AddressPostalCode", "Kod pocztowy", "Text", 17),
        new("AddressRegion", "Region", "Text", 18), new("AddressCountryCode", "Kraj adresu", "Text", 19),
        new("AddressIsActive", "Adres aktywny", "Boolean", 20, Format: "Activity"),
        new("AddressIsDefault", "Adres preferowany", "Boolean", 21, Format: "YesNo"),
        new("CreatedAtUtc", "Utworzono (UTC)", "DateTime", 22, Format: "yyyy-MM-dd HH:mm 'UTC'"),
        new("UpdatedAtUtc", "Zaktualizowano (UTC)", "DateTime", 23, Format: "yyyy-MM-dd HH:mm 'UTC'")]);
    public static readonly TableDefinition Currencies = new("Currencies", "ViewExchangeRates", [
        new("Code", "Kod", "Text", 0, true), new("Name", "Nazwa", "Text", 1, true),
        new("IsActive", "Status", "Boolean", 2, true, Format: "Activity"),
        new("DownloadA", "Tabela A", "Boolean", 3, true, Format: "YesNo"),
        new("DownloadB", "Tabela B", "Boolean", 4, true, Format: "YesNo"),
        new("DownloadC", "Tabela C", "Boolean", 5, true, Format: "YesNo")]);
    public static readonly TableDefinition CurrencyRates = new("CurrencyRates", "ViewExchangeRates", [
        new("CurrencyCode", "Waluta", "Text", 0, true), new("Table", "Tabela", "Text", 1, true),
        new("TableNumber", "Numer tabeli", "Text", 2, true),
        new("EffectiveDate", "Data publikacji", "Date", 3, true, Format: "dd.MM.yyyy"),
        new("TradingDate", "Data notowania", "Date", 4, true, Format: "dd.MM.yyyy"),
        new("Mid", "Średni", "Decimal", 5, true, Format: "0.0000########"),
        new("Bid", "Kupna", "Decimal", 6, true, Format: "0.0000########"),
        new("Ask", "Sprzedaży", "Decimal", 7, true, Format: "0.0000########")]);
    public static TableDefinition? Find(string id) => id switch
    { "Contractors" => Contractors, "Currencies" => Currencies, "CurrencyRates" => CurrencyRates, _ => null };

    public static string[] EffectiveColumns(TableDefinition definition, IEnumerable<string> permissions, string[]? own, string[]? installation)
    {
        var available = definition.Available(permissions);
        var selected = own ?? installation;
        var columns = available.Where(c => selected == null ? c.DefaultVisible : selected.Contains(c.Id, StringComparer.Ordinal)).Select(c => c.Id).ToArray();
        // Removed/unauthorized IDs never leak through saved views. A wholly unavailable view falls back safely.
        return columns.Length > 0 ? columns : available.Where(c => c.DefaultVisible).Select(c => c.Id).DefaultIfEmpty(available[0].Id).ToArray();
    }
    public static TableViewResponse Factory(string id, string[] permissions)
    {
        var table = Find(id) ?? throw new ArgumentException("Nieznana tabela.");
        return new(id, table.Available(permissions), EffectiveColumns(table, permissions, null, null), 50, "Factory", null, null);
    }
}
