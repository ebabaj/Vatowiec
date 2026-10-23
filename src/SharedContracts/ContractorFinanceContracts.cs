namespace Vatowiec.SharedContracts;

public sealed record TradeTermsValues(int? PaymentDays = null, string? PaymentMethodCode = null, string? CurrencyCode = null,
    decimal? DiscountPercent = null, string? Notes = null);
public sealed record TradeTermsRequest(TradeTermsValues Data, string? RowVersion = null, Guid? OperationId = null);
public sealed record TradeTermsResponse(int Id, Guid CompanyId, int ContractorId, string Direction, TradeTermsValues Data, string? RowVersion);
public sealed record CreditLimitRequest(int Id, string Direction, string CurrencyCode, decimal Amount, bool IsActive,
    string? RowVersion = null, Guid? OperationId = null);
public sealed record CreditLimitResponse(int Id, Guid CompanyId, int ContractorId, string Direction, string CurrencyCode, decimal Amount, bool IsActive, string RowVersion)
{
    public override string ToString() => $"{CurrencyCode}: {Amount:0.########} · {(IsActive ? "Aktywny" : "Nieaktywny")}";
}
public sealed record PaymentMethodRequest(string Code, string Name, bool IsActive, string? RowVersion = null, Guid? OperationId = null);
public sealed record PaymentMethodResponse(int Id, string Code, string Name, bool IsActive, string RowVersion)
{
    public override string ToString() => Name + (IsActive ? "" : " (nieaktywny)");
}
public sealed record FinanceCurrency(string Code, string Name, bool IsActive)
{
    public override string ToString() => Code.Length == 0 ? Name : $"{Code} — {Name}" + (IsActive ? "" : " (nieaktywna)");
}
public sealed record FinanceLookups(FinanceCurrency[] Currencies, PaymentMethodResponse[] PaymentMethods);
public sealed record FinanceConflictResponse(TradeTermsResponse? Terms, CreditLimitResponse? Limit, PaymentMethodResponse? Method);
public static class FinanceValidation
{
    public static Dictionary<string, string[]> Terms(TradeTermsValues value)
    {
        var errors = new Dictionary<string, string[]>();
        if (value.PaymentDays is < 0 or > 3650) errors["PaymentDays"] = ["Termin: liczba całkowita od 0 do 3650 dni."];
        if (value.DiscountPercent is < 0 or > 100 || value.DiscountPercent.HasValue && decimal.Round(value.DiscountPercent.Value, 4) != value.DiscountPercent)
            errors["DiscountPercent"] = ["Rabat: od 0 do 100%, najwyżej 4 miejsca po przecinku."];
        if (value.Notes?.Length > 2000) errors["Notes"] = ["Uwagi mogą mieć najwyżej 2000 znaków."];
        if (value.CurrencyCode != null && !Currency(value.CurrencyCode)) errors["CurrencyCode"] = ["Wybierz walutę ze słownika lub pozostaw brak ustawienia."];
        if (value.PaymentMethodCode != null && !Method(value.PaymentMethodCode)) errors["PaymentMethodCode"] = ["Wybierz sposób płatności ze słownika lub pozostaw brak ustawienia."];
        return errors;
    }
    public static Dictionary<string, string[]> Limit(CreditLimitRequest value)
    {
        var errors = new Dictionary<string, string[]>();
        if (!Currency(value.CurrencyCode)) errors["CurrencyCode"] = ["Wybierz walutę limitu ze słownika."];
        if (value.Direction is not ("Sale" or "Purchase")) errors["Direction"] = ["Wybierz Sprzedaż lub Zakup."];
        if (value.Amount < 0 || value.Amount > 9999999999999999.99999999m || decimal.Round(value.Amount, 8) != value.Amount)
            errors["Amount"] = ["Limit musi być nieujemny, do 16 cyfr całkowitych i 8 miejsc po przecinku."];
        return errors;
    }
    public static bool Currency(string? code) => code?.Length == 3 && code.All(c => c is >= 'A' and <= 'Z');
    public static bool Method(string? code) => code?.Length is >= 1 and <= 32 && code[0] is >= 'A' and <= 'Z' && code.All(c => c is >= 'A' and <= 'Z' or >= '0' and <= '9' or '_' or '-');
}
