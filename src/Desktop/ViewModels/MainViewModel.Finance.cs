using Vatowiec.Desktop.Services;
using Vatowiec.SharedContracts;

namespace Vatowiec.Desktop.ViewModels;

public sealed partial class MainViewModel
{
    private int financeDirectionIndex, financeOffset;
    private TradeTermsResponse? savedTerms;
    private FinanceConflictResponse? financeConflict;
    private string financeConflictKind = "";
    private bool publishingFinanceChoices;
    public bool IsFinanceTab => tab == 5;
    public int FinanceDirectionIndex
    {
        get => financeDirectionIndex;
        set
        {
            if (busy || value == financeDirectionIndex || value is not (0 or 1)) return;
            void Switch() { financeDirectionIndex = value; ClearFinanceDrafts(); ChangedAll(); _ = LoadFinance(); }
            if (HasFinanceChanges) RequestDiscard(Switch); else Switch();
        }
    }
    public string FinanceDirection => financeDirectionIndex == 0 ? "Sale" : "Purchase";
    public bool HasFinanceChanges => TradeTermsEditor?.IsDirty == true || CreditLimitEditor?.IsDirty == true || PaymentMethodEditor?.IsDirty == true;
    public bool CanEditFinance => IsFinanceTab && CanEditDetails;
    public bool CanSaveFinance => CanEditFinance && !HasFinanceConflict;
    public bool HasUnknownTradeCodes => TradeTermsEditor is { } terms &&
        ((terms.PaymentMethodCode.Length > 0 && !PaymentMethods.Any(x => x.Code == terms.PaymentMethodCode)) ||
         (terms.CurrencyCode.Length > 0 && !FinanceCurrencies.Any(x => x.Code == terms.CurrencyCode)));
    public bool CanSaveTradeTerms => CanSaveFinance && !HasUnknownTradeCodes;
    public string FinanceLookupWarning => HasUnknownTradeCodes
        ? "Zapisany kod jest nieznany lub słownik jest niedostępny. Zachowano wartość. Odśwież słowniki lub świadomie wybierz inną pozycję przed zapisem." : "";
    public bool IsCreditLimitEditing => CreditLimitEditor != null;
    public bool IsPaymentMethodEditing => PaymentMethodEditor != null;
    public TradeTermsEditor? TradeTermsEditor { get; private set; }
    public CreditLimitEditor? CreditLimitEditor { get; private set; }
    public PaymentMethodEditor? PaymentMethodEditor { get; private set; }
    public FinanceCurrency[] FinanceCurrencies { get; private set; } = [];
    public PaymentMethodResponse[] PaymentMethods { get; private set; } = [];
    // ItemsSource and SelectedItem must refer to the same, stable collection.
    // Recreating the placeholder on every notification lets ComboBox write it back
    // during rebinding, replacing a saved code and making an untouched draft dirty.
    public PaymentMethodResponse[] TradePaymentChoices { get; private set; } = [];
    public FinanceCurrency[] TradeCurrencyChoices { get; private set; } = [];
    public FinanceCurrency[] LimitCurrencyChoices { get; private set; } = [];
    public PaymentMethodResponse? SelectedTradePayment
    {
        get => TradePaymentChoices.FirstOrDefault(x => x.Code == (TradeTermsEditor?.PaymentMethodCode ?? ""));
        set
        {
            if (busy || publishingFinanceChoices || !CanEditFinance || value == null || TradeTermsEditor == null) return;
            TradeTermsEditor.PaymentMethodCode = value.Code; NotifyTradeSelection();
        }
    }
    public FinanceCurrency? SelectedTradeCurrency
    {
        get => TradeCurrencyChoices.FirstOrDefault(x => x.Code == (TradeTermsEditor?.CurrencyCode ?? ""));
        set
        {
            if (busy || publishingFinanceChoices || !CanEditFinance || value == null || TradeTermsEditor == null) return;
            if (value.Code.Length > 0 && value.Code != TradeTermsEditor.CurrencyCode && !FinanceCurrencies.Any(x => x.Code == value.Code && x.IsActive)) return;
            TradeTermsEditor.CurrencyCode = value.Code; NotifyTradeSelection();
        }
    }
    public FinanceCurrency? SelectedLimitCurrency
    {
        get => LimitCurrencyChoices.FirstOrDefault(x => x.Code == CreditLimitEditor?.CurrencyCode);
        set
        {
            if (busy || publishingFinanceChoices || !CanEditFinance || value == null || CreditLimitEditor == null) return;
            if (value.Code != CreditLimitEditor.CurrencyCode && !FinanceCurrencies.Any(x => x.Code == value.Code && x.IsActive)) return;
            CreditLimitEditor.CurrencyCode = value.Code; Changed(nameof(SelectedLimitCurrency));
        }
    }
    public CreditLimitResponse[] CreditLimits { get; private set; } = [];
    public CreditLimitResponse? SelectedCreditLimit { get; set; }
    public PaymentMethodResponse? SelectedPaymentMethod { get; set; }
    public bool HasNextCreditLimits { get; private set; }
    public bool HasPreviousCreditLimits => financeOffset > 0;
    public string CreditLimitPage => $"Strona {financeOffset / 50 + 1} · {CreditLimits.Length} limitów";
    public bool HasFinanceConflict => financeConflict != null;
    public string FinanceLocalSummary => financeConflictKind switch { "Terms" => TradeTermsEditor?.Summary() ?? "", "Limit" => CreditLimitEditor?.Summary() ?? "", _ => PaymentMethodEditor?.Summary() ?? "" };
    public string FinanceServerSummary => financeConflict?.Terms is { } t ? new TradeTermsEditor(t).Summary() : financeConflict?.Limit is { } l ? new CreditLimitEditor(l).Summary() : financeConflict?.Method is { } m ? new PaymentMethodEditor(m).Summary() : "";
    public Task LoadFinance() => Run(async () =>
    {
        if (!IsFinanceTab || !HasSavedContractor || api == null || profile == null) return;
        await ReadFinanceLookups();
        var row = await api.TradeTerms(profile.CompanyId, editor!.Id, FinanceDirection); CheckTerms(row);
        savedTerms = row; TradeTermsEditor = new(row); CreditLimitEditor = null; PaymentMethodEditor = null; financeConflict = null;
        PublishTradeChoices(); PublishLimitChoices();
        await ReadCreditLimits(0);
    });
    private void NotifyTradeSelection()
    {
        Changed(nameof(SelectedTradePayment)); Changed(nameof(SelectedTradeCurrency));
        Changed(nameof(HasUnknownTradeCodes)); Changed(nameof(FinanceLookupWarning)); Changed(nameof(CanSaveTradeTerms));
    }
    private void PublishTradeChoices()
    {
        publishingFinanceChoices = true;
        try
        {
            TradePaymentChoices = [new(0, "", "Brak indywidualnego sposobu", true, ""), .. PaymentMethods];
            TradeCurrencyChoices = [new("", "Brak indywidualnej waluty", true), .. FinanceCurrencies.Where(x => x.IsActive)];
            if (TradeTermsEditor is { } terms)
            {
                if (terms.PaymentMethodCode.Length > 0 && !PaymentMethods.Any(x => x.Code == terms.PaymentMethodCode))
                    TradePaymentChoices = [.. TradePaymentChoices, new(0, terms.PaymentMethodCode, $"{terms.PaymentMethodCode} — kod niedostępny w słowniku", false, "")];
                if (terms.CurrencyCode.Length > 0 && !TradeCurrencyChoices.Any(x => x.Code == terms.CurrencyCode))
                    TradeCurrencyChoices = [.. TradeCurrencyChoices, FinanceCurrencies.FirstOrDefault(x => x.Code == terms.CurrencyCode) ?? new(terms.CurrencyCode, "kod niedostępny w słowniku", false)];
            }
            Changed(nameof(TradePaymentChoices)); Changed(nameof(TradeCurrencyChoices));
            Changed(nameof(TradeTermsEditor)); NotifyTradeSelection();
        }
        finally { publishingFinanceChoices = false; }
    }
    private void PublishLimitChoices()
    {
        publishingFinanceChoices = true;
        try
        {
            LimitCurrencyChoices = FinanceCurrencies.Where(x => x.IsActive).ToArray();
            if (CreditLimitEditor is { CurrencyCode.Length: > 0 } limit && !LimitCurrencyChoices.Any(x => x.Code == limit.CurrencyCode))
                LimitCurrencyChoices = [.. LimitCurrencyChoices, FinanceCurrencies.FirstOrDefault(x => x.Code == limit.CurrencyCode) ?? new(limit.CurrencyCode, "kod niedostępny w słowniku", false)];
            Changed(nameof(LimitCurrencyChoices)); Changed(nameof(SelectedLimitCurrency));
        }
        finally { publishingFinanceChoices = false; }
    }
    private async Task ReadFinanceLookups()
    {
        var value = await api!.FinanceLookups(profile!.CompanyId);
        FinanceCurrencies = value.Currencies; PaymentMethods = value.PaymentMethods;
        PublishLimitChoices();
    }
    private async Task ReadCreditLimits(int offset)
    {
        var rows = await api!.CreditLimits(profile!.CompanyId, editor!.Id, FinanceDirection, offset);
        foreach (var row in rows) CheckLimit(row);
        CreditLimits = rows.Take(50).ToArray(); HasNextCreditLimits = rows.Length > 50; financeOffset = offset; SelectedCreditLimit = null;
    }
    public void RefreshFinance()
    { if (busy) return; if (HasFinanceChanges) RequestDiscard(() => _ = LoadFinance()); else _ = LoadFinance(); }
    public Task NextCreditLimits() => Run(() => ReadCreditLimits(financeOffset + 50));
    public Task PreviousCreditLimits() => Run(() => ReadCreditLimits(Math.Max(0, financeOffset - 50)));
    public Task SaveTradeTerms() => Run(async () =>
    {
        if (!CanSaveTradeTerms || TradeTermsEditor == null || api == null || profile == null || editor == null) return;
        try { var row = await api.SaveTradeTerms(profile.CompanyId, editor.Id, FinanceDirection, TradeTermsEditor.Request()); CheckTerms(row); savedTerms = row; TradeTermsEditor = new(row); PublishTradeChoices(); message = "Zapisano warunki i audyt. Szkic danych głównych pozostaje bez zmian."; }
        catch (ApiException ex) when (ex.Code == "finance_conflict" && ex.FinanceCurrent != null) { AcceptFinanceConflict(ex.FinanceCurrent, "Terms"); }
    });
    public void CancelTradeTerms()
    {
        if (busy) return;
        void Cancel() { TradeTermsEditor = savedTerms == null ? null : new(savedTerms); financeConflict = null; PublishTradeChoices(); ChangedAll(); }
        if (TradeTermsEditor?.IsDirty == true) RequestDiscard(Cancel); else Cancel();
    }
    public void NewCreditLimit()
    {
        if (busy || !CanSaveFinance) return;
        void Open() { CreditLimitEditor = new(); financeConflict = null; PublishLimitChoices(); ChangedAll(); }
        if (CreditLimitEditor?.IsDirty == true) RequestDiscard(Open); else Open();
    }
    public void EditCreditLimit()
    {
        if (busy || !CanSaveFinance || SelectedCreditLimit == null) return;
        var row = SelectedCreditLimit;
        void Open() { CreditLimitEditor = new(row); financeConflict = null; PublishLimitChoices(); ChangedAll(); }
        if (CreditLimitEditor?.IsDirty == true) RequestDiscard(Open); else Open();
    }
    public Task SaveCreditLimit() => Run(async () =>
    {
        if (!CanSaveFinance || CreditLimitEditor == null || api == null || profile == null || editor == null) return;
        try { var row = await api.SaveCreditLimit(profile.CompanyId, editor.Id, CreditLimitEditor.Request(FinanceDirection)); CheckLimit(row); CreditLimitEditor = null; await ReadCreditLimits(financeOffset); message = "Zapisano limit i audyt."; }
        catch (ApiException ex) when (ex.Code == "finance_conflict" && ex.FinanceCurrent != null) { AcceptFinanceConflict(ex.FinanceCurrent, "Limit"); }
    });
    public void CancelCreditLimit()
    {
        if (busy) return; void Cancel() { CreditLimitEditor = null; financeConflict = null; ChangedAll(); }
        if (CreditLimitEditor?.IsDirty == true) RequestDiscard(Cancel); else Cancel();
    }
    public void NewPaymentMethod()
    {
        if (busy || !CanSaveFinance) return; void Open() { PaymentMethodEditor = new(); financeConflict = null; ChangedAll(); }
        if (PaymentMethodEditor?.IsDirty == true) RequestDiscard(Open); else Open();
    }
    public void EditPaymentMethod()
    {
        if (busy || !CanSaveFinance || SelectedPaymentMethod == null) return; var row = SelectedPaymentMethod;
        void Open() { PaymentMethodEditor = new(row); financeConflict = null; ChangedAll(); }
        if (PaymentMethodEditor?.IsDirty == true) RequestDiscard(Open); else Open();
    }
    public Task SavePaymentMethod() => Run(async () =>
    {
        if (!CanSaveFinance || PaymentMethodEditor == null || api == null || profile == null) return;
        try { var row = await api.SavePaymentMethod(profile.CompanyId, PaymentMethodEditor.Request(), PaymentMethodEditor.Id == 0); if (row.Code != PaymentMethodEditor.Code) throw new ApiException(502, "Niezgodny kod sposobu płatności."); PaymentMethodEditor = null; await ReadFinanceLookups(); PublishTradeChoices(); message = "Zapisano sposób płatności i audyt."; }
        catch (ApiException ex) when (ex.Code == "finance_conflict" && ex.FinanceCurrent != null) { AcceptFinanceConflict(ex.FinanceCurrent, "Method"); }
    });
    public void CancelPaymentMethod()
    {
        if (busy) return; void Cancel() { PaymentMethodEditor = null; financeConflict = null; ChangedAll(); }
        if (PaymentMethodEditor?.IsDirty == true) RequestDiscard(Cancel); else Cancel();
    }
    private void AcceptFinanceConflict(FinanceConflictResponse current, string kind)
    {
        if (kind == "Terms") { if (current.Terms == null) throw new ApiException(502, "Brak wersji warunków."); CheckTerms(current.Terms); }
        if (kind == "Limit") { if (current.Limit == null || current.Limit.Id != CreditLimitEditor?.Id) throw new ApiException(502, "Niezgodny limit w konflikcie."); CheckLimit(current.Limit); }
        if (kind == "Method" && (current.Method == null || current.Method.Code != PaymentMethodEditor?.Code)) throw new ApiException(502, "Niezgodny sposób płatności w konflikcie.");
        financeConflict = current; financeConflictKind = kind; message = "Konflikt: porównaj wersje finansów. Nie nadpisano danych.";
    }
    public void ResolveFinanceConflict(bool keepLocal)
    {
        if (busy || financeConflict == null) return;
        if (financeConflict.Terms is { } t) { savedTerms = t; if (keepLocal) { TradeTermsEditor!.Id = t.Id; TradeTermsEditor.RowVersion = t.RowVersion; } else TradeTermsEditor = new(t); }
        if (financeConflict.Limit is { } l) { if (keepLocal) CreditLimitEditor!.RowVersion = l.RowVersion; else CreditLimitEditor = new(l); }
        if (financeConflict.Method is { } m) { if (keepLocal) PaymentMethodEditor!.RowVersion = m.RowVersion; else PaymentMethodEditor = new(m); }
        financeConflict = null; PublishTradeChoices(); PublishLimitChoices(); message = "Porównano wersje. Sprawdź pola i ponownie wybierz właściwy przycisk zapisu."; ChangedAll();
    }
    private void CheckTerms(TradeTermsResponse row)
    { if (profile == null || editor == null || row.CompanyId != profile.CompanyId || row.ContractorId != editor.Id || row.Direction != FinanceDirection) throw new ApiException(502, "Niezgodna firma, kontrahent lub kierunek warunków."); }
    private void CheckLimit(CreditLimitResponse row)
    { if (profile == null || editor == null || row.CompanyId != profile.CompanyId || row.ContractorId != editor.Id || row.Direction != FinanceDirection) throw new ApiException(502, "Niezgodna firma, kontrahent lub kierunek limitu."); }
    private void ClearFinanceDrafts()
    { TradeTermsEditor = null; CreditLimitEditor = null; PaymentMethodEditor = null; savedTerms = null; financeConflict = null; CreditLimits = []; SelectedCreditLimit = null; SelectedPaymentMethod = null; financeOffset = 0; HasNextCreditLimits = false; }
    private void ResetFinance()
    { ClearFinanceDrafts(); financeDirectionIndex = 0; FinanceCurrencies = []; PaymentMethods = []; TradePaymentChoices = []; TradeCurrencyChoices = []; LimitCurrencyChoices = []; }
}
