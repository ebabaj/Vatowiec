namespace Vatowiec.Core.Domain;

public static class TradeDirection
{
    public static bool IsValid(string direction) => direction is "Sale" or "Purchase";
}
public sealed class PaymentMethod : ITrackedEntity
{
    private PaymentMethod() { }
    public PaymentMethod(string code, string name) { Code = code; Update(name, true); }
    public int Id { get; private set; }
    public string Code { get; private set; } = "";
    public string Name { get; private set; } = "";
    public bool IsActive { get; private set; }
    public byte[] RowVersion { get; private set; } = [];
    public DateTimeOffset CreatedAtUtc { get; private set; }
    public DateTimeOffset UpdatedAtUtc { get; private set; }
    public void Update(string name, bool active) { Name = EntityText.Required(name, 100, nameof(name)); IsActive = active; }
}
public sealed class ContractorTradeTerms : ITrackedEntity
{
    private ContractorTradeTerms() { }
    public ContractorTradeTerms(int contractorId, string direction)
    { if (contractorId <= 0 || !TradeDirection.IsValid(direction)) throw new ArgumentException("Nieprawidłowy kontrahent lub kierunek."); ContractorId = contractorId; Direction = direction; }
    public int Id { get; private set; }
    public int ContractorId { get; private set; }
    public string Direction { get; private set; } = "";
    public int? PaymentDays { get; private set; }
    public string? PaymentMethodCode { get; private set; }
    public string? CurrencyCode { get; private set; }
    public decimal? DiscountPercent { get; private set; }
    public string? Notes { get; private set; }
    public byte[] RowVersion { get; private set; } = [];
    public DateTimeOffset CreatedAtUtc { get; private set; }
    public DateTimeOffset UpdatedAtUtc { get; private set; }
    public void Update(int? days, string? method, string? currency, decimal? discount, string? notes)
    {
        if (days is < 0 or > 3650 || discount is < 0 or > 100 || (discount.HasValue && decimal.Round(discount.Value, 4) != discount))
            throw new ArgumentException("Nieprawidłowy termin lub rabat.");
        var validNotes = EntityText.Optional(notes, 2000, nameof(notes));
        PaymentDays = days; PaymentMethodCode = method; CurrencyCode = currency; DiscountPercent = discount; Notes = validNotes;
    }
}
public sealed class ContractorCreditLimit : ITrackedEntity
{
    private ContractorCreditLimit() { }
    public ContractorCreditLimit(int contractorId, string direction)
    { if (contractorId <= 0 || !TradeDirection.IsValid(direction)) throw new ArgumentException("Nieprawidłowy kontrahent lub kierunek."); ContractorId = contractorId; Direction = direction; }
    public int Id { get; private set; }
    public int ContractorId { get; private set; }
    public string Direction { get; private set; } = "";
    public string CurrencyCode { get; private set; } = "";
    public decimal Amount { get; private set; }
    public bool IsActive { get; private set; }
    public byte[] RowVersion { get; private set; } = [];
    public DateTimeOffset CreatedAtUtc { get; private set; }
    public DateTimeOffset UpdatedAtUtc { get; private set; }
    public void Update(string currency, decimal amount, bool active)
    {
        if (amount < 0 || amount > 9999999999999999.99999999m || decimal.Round(amount, 8) != amount) throw new ArgumentException("Nieprawidłowa kwota limitu.");
        CurrencyCode = EntityText.Required(currency, 3, nameof(currency)); Amount = amount; IsActive = active;
    }
}
