using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Vatowiec.SharedContracts;

namespace Vatowiec.Desktop.ViewModels;

public abstract class FinanceEditorBase : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;
    protected void Set(ref string target, string value, [CallerMemberName] string? name = null) { target = value; PropertyChanged?.Invoke(this, new(name)); }
    protected string original = "";
    protected abstract string Snapshot();
    public bool IsDirty => original != Snapshot();
    public string? RowVersion { get; set; }
    public Guid OperationId { get; } = Guid.NewGuid();
    protected static decimal Decimal(string text, string field)
    {
        if (!decimal.TryParse(text.Trim().Replace(',', '.'), NumberStyles.AllowDecimalPoint | NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var value))
            throw new ArgumentException(field + ": wpisz liczbę dziesiętną bez separatorów tysięcy.");
        return value;
    }
    protected static string Format(decimal? value) => value?.ToString("0.########", CultureInfo.GetCultureInfo("pl-PL")) ?? "";
}
public sealed class TradeTermsEditor : FinanceEditorBase
{
    public TradeTermsEditor(TradeTermsResponse row)
    {
        Id = row.Id; RowVersion = row.RowVersion; Days = row.Data.PaymentDays?.ToString(CultureInfo.InvariantCulture) ?? "";
        PaymentMethodCode = row.Data.PaymentMethodCode ?? ""; CurrencyCode = row.Data.CurrencyCode ?? ""; Discount = Format(row.Data.DiscountPercent); Notes = row.Data.Notes ?? ""; original = Snapshot();
    }
    public int Id { get; set; }
    private string days = "", payment = "", currency = "", discount = "", notes = "";
    public string Days { get => days; set => Set(ref days, value); }
    public string PaymentMethodCode { get => payment; set => Set(ref payment, value); }
    public string CurrencyCode { get => currency; set => Set(ref currency, value); }
    public string Discount { get => discount; set => Set(ref discount, value); }
    public string Notes { get => notes; set => Set(ref notes, value); }
    protected override string Snapshot() => JsonSerializer.Serialize(new { Days, PaymentMethodCode, CurrencyCode, Discount, Notes });
    public TradeTermsRequest Request()
    {
        int? term = null;
        if (!string.IsNullOrWhiteSpace(Days))
        { if (!int.TryParse(Days.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var parsed)) throw new ArgumentException("Termin: wpisz liczbę całkowitą od 0 do 3650 albo pozostaw puste."); term = parsed; }
        var values = new TradeTermsValues(term, string.IsNullOrEmpty(PaymentMethodCode) ? null : PaymentMethodCode,
            string.IsNullOrEmpty(CurrencyCode) ? null : CurrencyCode, string.IsNullOrWhiteSpace(Discount) ? null : Decimal(Discount, "Rabat"), string.IsNullOrWhiteSpace(Notes) ? null : Notes.Trim());
        var errors = FinanceValidation.Terms(values); if (errors.Count > 0) throw new ArgumentException(string.Join("\n", errors.Values.SelectMany(x => x)));
        return new(values, RowVersion, Id == 0 ? OperationId : null);
    }
    public string Summary() => $"Termin: {Days} dni\nSposób: {PaymentMethodCode}\nWaluta: {(CurrencyCode.Length == 0 ? "brak ustawienia" : CurrencyCode)}\nRabat: {Discount}%\nUwagi: {Notes}";
}
public sealed class CreditLimitEditor : FinanceEditorBase
{
    public CreditLimitEditor() { original = Snapshot(); }
    public CreditLimitEditor(CreditLimitResponse row) { Id = row.Id; RowVersion = row.RowVersion; CurrencyCode = row.CurrencyCode; Amount = Format(row.Amount); IsActive = row.IsActive; original = Snapshot(); }
    public int Id { get; set; }
    private string currency = "", amount = "";
    public string CurrencyCode { get => currency; set => Set(ref currency, value); }
    public string Amount { get => amount; set => Set(ref amount, value); }
    public bool IsActive { get; set; } = true;
    protected override string Snapshot() => JsonSerializer.Serialize(new { CurrencyCode, Amount, IsActive });
    public CreditLimitRequest Request(string direction)
    {
        var request = new CreditLimitRequest(Id, direction, CurrencyCode, Decimal(Amount, "Kwota limitu"), IsActive, RowVersion, Id == 0 ? OperationId : null);
        var errors = FinanceValidation.Limit(request); if (errors.Count > 0) throw new ArgumentException(string.Join("\n", errors.Values.SelectMany(x => x)));
        return request;
    }
    public string Summary() => $"Waluta: {CurrencyCode}\nKwota: {Amount}\n{(IsActive ? "Aktywny" : "Nieaktywny")}";
}
public sealed class PaymentMethodEditor : FinanceEditorBase
{
    public PaymentMethodEditor() { Code = "USER_" + Guid.NewGuid().ToString("N")[..12].ToUpperInvariant(); original = Snapshot(); }
    public PaymentMethodEditor(PaymentMethodResponse row) { Id = row.Id; Code = row.Code; Name = row.Name; IsActive = row.IsActive; RowVersion = row.RowVersion; original = Snapshot(); }
    public int Id { get; set; }
    public string Code { get; }
    private string name = "";
    public string Name { get => name; set => Set(ref name, value); }
    public bool IsActive { get; set; } = true;
    protected override string Snapshot() => JsonSerializer.Serialize(new { Name, IsActive });
    public PaymentMethodRequest Request()
    {
        if (string.IsNullOrWhiteSpace(Name) || Name.Trim().Length > 100) throw new ArgumentException("Nazwa sposobu płatności jest wymagana, najwyżej 100 znaków.");
        return new(Code, Name.Trim(), IsActive, RowVersion, Id == 0 ? OperationId : null);
    }
    public string Summary() => $"Kod: {Code}\nNazwa: {Name}\n{(IsActive ? "Aktywny" : "Nieaktywny")}";
}
