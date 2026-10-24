using System.ComponentModel;
using System.Text.Json;
using Vatowiec.Desktop.Services;
using Vatowiec.SharedContracts;

namespace Vatowiec.Desktop.ViewModels;

public sealed partial class MainViewModel
{
    private bool currencyScreen;
    private bool currencySettings;
    private CurrencyResponse? selectedCurrency;
    private RateImportResponse? lastImport;
    private RateConflict? selectedRateConflict;
    private int rateOffset;
    private int currencyHistoryOffset;
    private string? pendingImportPayload;
    private Guid pendingImport = Guid.NewGuid();
    private Guid pendingResolution = Guid.NewGuid();
    private string? pendingResolutionPayload;
    private string? loadedRateFilter;
    private CurrencyHistoryResponse? selectedCurrencyHistory;
    public bool IsCurrencyScreen => IsWorkspace && currencyScreen;
    public bool IsCurrencySettings => IsCurrencyScreen && currencySettings;
    public bool IsRateList => IsCurrencyScreen && !currencySettings;
    public bool CanViewRates => IsWorkspace && selectedCompany?.Permissions.Contains("ViewExchangeRates") == true;
    public bool CanConfigureCurrencies => IsCurrencySettings && selectedCompany?.Permissions.Contains("ManageCurrencies") == true;
    public bool CanEditCurrencySettings => CanConfigureCurrencies && !CurrencyActivityPrompt;
    public bool CanImportRates => IsCurrencyScreen && selectedCompany?.Permissions.Contains("ImportExchangeRates") == true;
    public bool CanResolveRates => CanImportRates && selectedCompany?.Permissions.Contains("ManageCurrencies") == true && selectedRateConflict != null;
    public bool CanChooseCurrencyTables => CanConfigureCurrencies && CurrencyEditor is { IsActive: true, IsPln: false };
    public string CurrencyScreenTitle => currencySettings ? "Ustawienia firmy → Waluty i kursy NBP" : "Słowniki → Waluty i kursy NBP";
    public CurrencyResponse[] CurrencyRows { get; private set; } = [];
    private CurrencyResponse[]? selectionSource;
    private CurrencySelection[] currencySelections = [];
    private CurrencySelection[]? visibleSelectionRows;
    private CurrencyResponse[]? visibleSelectionSource;
    private CurrencySelection[] CurrencySelections
    {
        get
        {
            if (!ReferenceEquals(selectionSource, CurrencyRows))
            {
                var selected = currencySelections.Where(x => x.IsChecked).Select(x => x.Code).ToHashSet();
                selectionSource = CurrencyRows;
                currencySelections = CurrencyRows.Select(x => new CurrencySelection(x, selected.Contains(x.Code), () => ChangedAll())).ToArray();
                visibleSelectionSource = null;
            }
            return currencySelections;
        }
    }
    public CurrencySelection[] VisibleCurrencySelections
    {
        get
        {
            var all = CurrencySelections;
            if (!ReferenceEquals(visibleSelectionSource, VisibleCurrencyRows))
            {
                visibleSelectionSource = VisibleCurrencyRows;
                var codes = VisibleCurrencyRows.Select(x => x.Code).ToHashSet();
                visibleSelectionRows = all.Where(x => codes.Contains(x.Code)).ToArray();
            }
            return visibleSelectionRows!;
        }
    }
    public CurrencySelection? SelectedCurrencySelection
    {
        get => CurrencySelections.FirstOrDefault(x => x.Code == selectedCurrency?.Code);
        set => SelectedCurrency = value?.Currency;
    }
    public string CurrencySelectionCount => $"Zaznaczono: {CurrencySelections.Count(x => x.IsChecked)} z {CurrencyRows.Length}";
    public bool CanStartCurrencyActivity => CanConfigureCurrencies && !busy && !CurrencyActivityPrompt;
    public bool CurrencyActivityPrompt { get; private set; }
    public string CurrencyActivityConfirmation { get; private set; } = "";
    private CurrencyActivityRequest? pendingActivity;
    private string? pendingActivityPayload;
    public void SelectCurrencies(bool all)
    {
        if (!CanStartCurrencyActivity) return;
        foreach (var row in all ? CurrencySelections : VisibleCurrencySelections) row.IsChecked = true;
        ChangedAll();
    }
    public void ClearCurrencySelection()
    {
        if (!CanStartCurrencyActivity) return;
        foreach (var row in CurrencySelections) row.IsChecked = false;
        ChangedAll();
    }
    public void BeginCurrencyActivity(bool active, bool all = false)
    {
        if (!CanStartCurrencyActivity) return;
        if (CurrencyEditor?.IsDirty == true)
        { message = "Najpierw zapisz lub porzuć zmiany edytowanej waluty."; ChangedAll(); return; }
        var rows = all ? CurrencyRows : CurrencySelections.Where(x => x.IsChecked).Select(x => x.Currency).ToArray();
        if (rows.Length == 0) { message = "Zaznacz waluty do operacji."; ChangedAll(); return; }
        var versions = rows.Select(x => new CurrencyVersion(x.Code, x.RowVersion)).ToArray();
        var payload = JsonSerializer.Serialize(new { active, all, versions });
        if (pendingActivityPayload != payload)
        {
            pendingActivityPayload = payload;
            pendingActivity = new(Guid.NewGuid(), active, all, versions, true);
        }
        var changes = rows.Count(x => x.IsActive != active && (active || x.Code != "PLN"));
        CurrencyActivityConfirmation = $"{(active ? "Aktywować" : "Dezaktywować")} {(all ? "wszystkie waluty" : "zaznaczone waluty")}? Zakres: {rows.Length}. Do zmiany: {changes}; pominięte: {rows.Length - changes}.\n" +
            (!active ? "PLN zostanie pominięty i pozostanie aktywny. Dezaktywacja wyłączy pobieranie A/B/C; historyczne kursy pozostaną. " : "Aktywacja zachowa obecne ustawienia A/B/C. ") +
            "Operacja nie pobiera kursów NBP.";
        CurrencyActivityPrompt = true; ChangedAll();
    }
    public void CancelCurrencyActivity() { if (!busy) { CurrencyActivityPrompt = false; ChangedAll(); } }
    public Task ConfirmCurrencyActivity()
    {
        if (!CurrencyActivityPrompt || pendingActivity == null || !CanConfigureCurrencies) return Task.CompletedTask;
        return Run(async () =>
        {
            var result = await api!.ChangeCurrencyActivity(profile!.CompanyId, pendingActivity);
            if (result.CompanyId != profile.CompanyId || result.Changed.Any(x => x.CompanyId != profile.CompanyId))
                throw new ApiException(502, "Wynik operacji dotyczy innej firmy.");
            CurrencyActivityPrompt = false; pendingActivity = null; pendingActivityPayload = null;
            await RefreshCurrencyData();
            message = $"Zmieniono waluty: {result.Changed.Length}; pominięto: {result.Skipped.Length}. Nie pobierano kursów NBP.";
        });
    }
    private CurrencyResponse[]? filteredCurrencySource;
    private string? filteredCurrencySearch;
    private CurrencyResponse[] visibleCurrencyRows = [];
    // Keep the collection stable during editor notifications. Replacing ItemsSource
    // would reset selection and re-enter the unsaved-changes prompt through binding.
    public CurrencyResponse[] VisibleCurrencyRows
    {
        get
        {
            if (!ReferenceEquals(filteredCurrencySource, CurrencyRows) || filteredCurrencySearch != CurrencySearch)
            {
                filteredCurrencySource = CurrencyRows;
                filteredCurrencySearch = CurrencySearch;
                visibleCurrencyRows = CurrencyRows.Where(x => string.IsNullOrWhiteSpace(CurrencySearch) ||
                    (x.Code + " " + x.Name).Contains(CurrencySearch.Trim(), StringComparison.CurrentCultureIgnoreCase)).ToArray();
            }
            return visibleCurrencyRows;
        }
    }
    private string currencySearch = "";
    public string CurrencySearch { get => currencySearch; set { currencySearch = value; Changed(nameof(VisibleCurrencyRows)); Changed(nameof(VisibleCurrencySelections)); } }
    public CurrencyResponse? SelectedCurrency
    {
        get => selectedCurrency;
        set
        {
            if (busy || DiscardPrompt || CurrencyActivityPrompt || value == selectedCurrency) return;
            // ItemsSource replacement after a save briefly deselects the old
            // object. Preserve the clean editor if that currency is still listed.
            if (value == null && CurrencyEditor?.IsDirty == false &&
                VisibleCurrencyRows.Any(x => x.Code == selectedCurrency?.Code)) return;
            void Select() { selectedCurrency = value; SetCurrencyEditor(value); ChangedAll(); }
            if (CurrencyEditor?.IsDirty == true) RequestDiscard(Select); else Select();
        }
    }
    public CurrencySettingsEditor? CurrencyEditor { get; private set; }
    private void SetCurrencyEditor(CurrencyResponse? value)
    {
        CurrencyEditor = value == null ? null : new(value);
        if (CurrencyEditor != null) CurrencyEditor.PropertyChanged += (_, _) => ChangedAll();
    }
    public int ImportMode { get; set; }
    public DateTimeOffset? ImportFrom { get; set; } = DateTimeOffset.Now.Date.AddDays(-30);
    public DateTimeOffset? ImportTo { get; set; } = DateTimeOffset.Now.Date;
    private CurrencyResponse? rateCurrencyFilter;
    private bool publishingRateChoices;
    private HashSet<string> currenciesWithRates = [];
    public bool ShowInactiveRateCurrencies { get; private set; }
    public FinanceCurrency[] RateCurrencyChoices { get; private set; } = [];
    public CurrencyResponse? RateCurrencyFilter
    {
        get => rateCurrencyFilter;
        set
        {
            if (busy || publishingRateChoices) return;
            rateCurrencyFilter = value; Changed(nameof(SelectedRateCurrency));
        }
    }
    public FinanceCurrency? SelectedRateCurrency
    {
        get => RateCurrencyChoices.FirstOrDefault(x => x.Code == (rateCurrencyFilter?.Code ?? ""));
        set
        {
            if (busy || publishingRateChoices || value == null) return;
            var row = CurrencyRows.FirstOrDefault(x => x.Code == value.Code);
            if (value.Code.Length > 0 && (row == null || row.Code == "PLN" ||
                !row.IsActive && !(ShowInactiveRateCurrencies && currenciesWithRates.Contains(row.Code)) && row.Code != rateCurrencyFilter?.Code)) return;
            if (rateCurrencyFilter?.Code == row?.Code) return;
            rateCurrencyFilter = row; PublishRateChoices();
        }
    }
    private void PublishRateChoices()
    {
        publishingRateChoices = true;
        try
        {
            // Keep the explicitly chosen code even when its activity changes.
            // ItemsSource replacement must not write a transient null back to the filter.
            rateCurrencyFilter = CurrencyRows.FirstOrDefault(x => x.Code == rateCurrencyFilter?.Code) ?? rateCurrencyFilter;
            RateCurrencyChoices = [new("", "Wszystkie kursy z bieżącego zakresu", true), .. CurrencyRows
                .Where(x => x.Code != "PLN" && (x.IsActive || ShowInactiveRateCurrencies && currenciesWithRates.Contains(x.Code) || x.Code == rateCurrencyFilter?.Code))
                .Select(x => new FinanceCurrency(x.Code, x.Name, x.IsActive))];
            Changed(nameof(RateCurrencyChoices)); Changed(nameof(SelectedRateCurrency));
        }
        finally { publishingRateChoices = false; }
    }
    private async Task ReadCurrenciesWithRates()
    {
        var rows = await api!.CurrenciesWithRates(profile!.CompanyId);
        if (rows.Any(x => x.CompanyId != profile.CompanyId)) throw new ApiException(502, "Archiwum walut dotyczy innej firmy.");
        currenciesWithRates = rows.Select(x => x.Code).ToHashSet(StringComparer.Ordinal);
    }
    public Task ShowInactiveCurrencies(bool show) => Run(async () =>
    {
        if (!IsCurrencyScreen || api == null || profile == null) return;
        if (show) await ReadCurrenciesWithRates();
        ShowInactiveRateCurrencies = show; PublishRateChoices();
    });
    public int RateTableFilter { get; set; }
    public DateTimeOffset? RateFromFilter { get; set; }
    public DateTimeOffset? RateToFilter { get; set; }
    public bool RateDescending { get; set; } = true;
    public RateResponse[] RateRows { get; private set; } = [];
    public bool HasPreviousRates => rateOffset > 0;
    public bool HasNextRates { get; private set; }
    public string RatePageLabel => $"Strona {rateOffset / 50 + 1} · {RateRows.Length} kursów";
    public string StoredRateDates { get; private set; } = "";
    public string RateImportSummary { get; private set; } = "Pobieranie odbywa się tylko po kliknięciu. Daty oznaczają publikacje NBP; PLN nie wymaga kursu.";
    public RateConflict[] RateConflicts { get; private set; } = [];
    public RateConflict? SelectedRateConflict
    { get => selectedRateConflict; set { if (!busy) { selectedRateConflict = value; ConfirmRateResolution = false; ChangedAll(); } } }
    public bool HasRateConflicts => RateConflicts.Length > 0;
    public bool ConfirmRateResolution { get; set; }
    private static string RateText(RateValue value) => $"Tabela {value.TableNumber}, publikacja {value.EffectiveDate:dd.MM.yyyy}, notowanie {value.TradingDate:dd.MM.yyyy}\n" +
        (value.Mid != null ? $"Średni {value.Mid.Value.ToString("0.0000########", System.Globalization.CultureInfo.GetCultureInfo("pl-PL"))}" :
        $"Kupna {value.Bid?.ToString("0.0000########", System.Globalization.CultureInfo.GetCultureInfo("pl-PL"))} · Sprzedaży {value.Ask?.ToString("0.0000########", System.Globalization.CultureInfo.GetCultureInfo("pl-PL"))}");
    public string RateConflictComparison => selectedRateConflict == null ? "Wybierz konflikt." : "Zapisano:\n" + RateText(selectedRateConflict.Existing) + "\nNBP proponuje:\n" + RateText(selectedRateConflict.Incoming);
    public CurrencyHistoryResponse[] CurrencyHistoryRows { get; private set; } = [];
    public CurrencyHistoryResponse? SelectedCurrencyHistory
    {
        get => selectedCurrencyHistory;
        set
        {
            if (busy) return;
            selectedCurrencyHistory = value;
            if (value is { Kind: "Import" } && value.CompanyId == profile?.CompanyId)
            {
                try
                {
                    lastImport = JsonSerializer.Deserialize<RateImportResponse>(value.Details, new JsonSerializerOptions(JsonSerializerDefaults.Web))! with { AuditId = value.Id };
                    RateConflicts = lastImport.Conflicts; selectedRateConflict = null; ConfirmRateResolution = false;
                    RateImportSummary = $"Historia importu {value.AtUtc.ToLocalTime():dd.MM.yyyy HH:mm:ss}: tabele {lastImport.Tables}, nowe {lastImport.Added}, pominięte {lastImport.Skipped}, konflikty {lastImport.Conflicts.Length}, błędy {lastImport.Errors.Length}.\n" +
                        "Propozycje są historyczne. Przed rozstrzygnięciem serwer sprawdzi aktualną wersję kursu; nie nadpisze nowszej zmiany.";
                }
                catch (JsonException) { message = "Nie można odczytać historycznego wyniku importu."; }
            }
            ChangedAll();
        }
    }
    public bool HasPreviousCurrencyHistory => currencyHistoryOffset > 0;
    public bool HasNextCurrencyHistory { get; private set; }
    public Task OpenCurrencies(bool settings = false) => Run(async () =>
    {
        if (api == null || profile == null || IsEditing || !CanViewRates) return;
        ResetDataPortSettings(); currencyScreen = true; currencySettings = settings;
        await RefreshCurrencyData();
    });
    private async Task RefreshCurrencyData()
    {
        CurrencyRows = await api!.Currencies(profile!.CompanyId);
        if (CurrencyRows.Any(x => x.CompanyId != profile.CompanyId)) throw new ApiException(502, "Słownik dotyczy innej firmy.");
        if (ShowInactiveRateCurrencies) await ReadCurrenciesWithRates();
        PublishRateChoices();
        var code = selectedCurrency?.Code;
        selectedCurrency = CurrencyRows.FirstOrDefault(x => x.Code == code) ?? CurrencyRows.FirstOrDefault(x => x.Code == "EUR");
        SetCurrencyEditor(selectedCurrency);
        await ReadRates(0); await ReadCurrencyHistory(0);
    }
    public void ShowCurrencySettings(bool settings)
    {
        if (busy || CurrencyActivityPrompt) return;
        void Select() { currencySettings = settings; SetCurrencyEditor(selectedCurrency); ChangedAll(); }
        if (CurrencyEditor?.IsDirty == true) RequestDiscard(Select); else Select();
    }
    public void CloseCurrencies()
    {
        if (busy || CurrencyActivityPrompt) return;
        void Close() { ResetCurrencyState(); ChangedAll(); }
        if (CurrencyEditor?.IsDirty == true) RequestDiscard(Close); else Close();
    }
    public void CancelCurrencyChanges() { if (!busy && !CurrencyActivityPrompt) { SetCurrencyEditor(selectedCurrency); ChangedAll(); } }
    public Task RefreshCurrencies()
    {
        if (CurrencyActivityPrompt) return Task.CompletedTask;
        if (CurrencyEditor?.IsDirty == true)
        { message = "Najpierw zapisz lub porzuć zmiany ustawień przed odświeżeniem słownika."; ChangedAll(); return Task.CompletedTask; }
        return Run(RefreshCurrencyData);
    }
    public Task SaveCurrencySettings()
    {
        if (!CanConfigureCurrencies || CurrencyEditor == null || CurrencyActivityPrompt) return Task.CompletedTask;
        return Run(async () =>
        {
            var result = await api!.SaveCurrency(profile!.CompanyId, CurrencyEditor.Code, CurrencyEditor.Request());
            if (result.CompanyId != profile.CompanyId || result.Code != CurrencyEditor.Code) throw new ApiException(502, "Odpowiedź ustawień dotyczy innej firmy lub waluty.");
            CurrencyRows = CurrencyRows.Select(x => x.Code == result.Code ? result : x).ToArray(); selectedCurrency = result; SetCurrencyEditor(result);
            PublishRateChoices();
            await ReadCurrencyHistory(0); message = "Zapisano ustawienia waluty z audytem. Nie pobierano kursów NBP.";
        });
    }
    public Task LoadRates() => Run(() => ReadRates(0));
    public Task NextRates() => Run(() => ReadRates(rateOffset + 50));
    public Task PreviousRates() => Run(() => ReadRates(Math.Max(0, rateOffset - 50)));
    public void ClearRateFilters()
    { if (!busy) { RateCurrencyFilter = null; RateTableFilter = 0; RateFromFilter = RateToFilter = null; PublishRateChoices(); ChangedAll(); } }
    private static DateOnly? Date(DateTimeOffset? value) => value == null ? null : DateOnly.FromDateTime(value.Value.Date);
    private async Task ReadRates(int offset)
    {
        if (api == null || profile == null || !IsCurrencyScreen) return;
        var filter = JsonSerializer.Serialize(new { RateCurrencyFilter?.Code, RateTableFilter, From = Date(RateFromFilter), To = Date(RateToFilter), RateDescending });
        if (loadedRateFilter != filter) offset = 0;
        var page = await api.Rates(profile.CompanyId, RateCurrencyFilter?.Code, RateTableFilter == 0 ? null : new[] { "A", "B", "C" }[RateTableFilter - 1], Date(RateFromFilter), Date(RateToFilter), offset, RateDescending);
        if (page.CompanyId != profile.CompanyId || page.Items.Any(x => x.CompanyId != profile.CompanyId)) throw new ApiException(502, "Kursy dotyczą innej firmy.");
        RateRows = page.Items.Take(50).ToArray(); HasNextRates = page.Items.Length > 50; rateOffset = offset;
        loadedRateFilter = filter;
        StoredRateDates = $"Ostatnie zapisane publikacje: A {page.LastA:dd.MM.yyyy} · B {page.LastB:dd.MM.yyyy} · C {page.LastC:dd.MM.yyyy}. Puste = brak zapisanych kursów.";
    }
    public Task ImportNbpRates()
    {
        if (!CanImportRates || CurrencyActivityPrompt) return Task.CompletedTask;
        return Run(async () =>
        {
            if (CurrencyEditor?.IsDirty == true) throw new ArgumentException("Najpierw zapisz lub porzuć ustawienia. Import używa zapisanej konfiguracji.");
            if (ImportMode == 1 && (ImportFrom == null || ImportTo == null)) throw new ArgumentException("Wybierz obie daty publikacji.");
            var dates = new { From = ImportMode == 0 ? null : Date(ImportFrom), To = ImportMode == 0 ? null : Date(ImportTo) };
            var payload = JsonSerializer.Serialize(dates);
            if (pendingImportPayload != payload) { pendingImport = Guid.NewGuid(); pendingImportPayload = payload; }
            var result = await api!.ImportRates(profile!.CompanyId, new(pendingImport, dates.From, dates.To));
            if (result.CompanyId != profile.CompanyId) throw new ApiException(502, "Wynik importu dotyczy innej firmy.");
            pendingImportPayload = null; lastImport = result; RateConflicts = result.Conflicts; selectedRateConflict = null; ConfirmRateResolution = false;
            RateImportSummary = $"Tabele: {result.Tables} · nowe kursy: {result.Added} · pominięte istniejące: {result.Skipped} · konflikty: {result.Conflicts.Length} · błędy: {result.Errors.Length}\n" +
                (result.From == null ? "Tryb: najnowsze opublikowane tabele.\n" : $"Publikacje od {result.From:dd.MM.yyyy} do {result.To:dd.MM.yyyy}.\n") +
                $"Ostatnie otrzymane w tej operacji: A {result.LastA:dd.MM.yyyy} · B {result.LastB:dd.MM.yyyy} · C {result.LastC:dd.MM.yyyy} (puste = nie pobrano).\n" +
                (result.Added == 0 ? "Brak nowych kursów.\n" : "") + string.Join("\n", result.Errors.Concat(result.Notices));
            await ReadRates(0); await ReadCurrencyHistory(0); message = "Zakończono ręczne pobieranie. Sprawdź podsumowanie; konflikty nie zmieniły zapisanych wartości.";
        });
    }
    public Task ResolveNbpConflict(bool incoming)
    {
        if (!CanResolveRates || lastImport == null || selectedRateConflict == null) return Task.CompletedTask;
        return Run(async () =>
        {
            if (!ConfirmRateResolution) throw new ArgumentException("Zaznacz świadome potwierdzenie rozstrzygnięcia.");
            var index = Array.IndexOf(lastImport.Conflicts, selectedRateConflict);
            var payload = $"{lastImport.AuditId}/{index}/{incoming}";
            if (pendingResolutionPayload != payload) { pendingResolutionPayload = payload; pendingResolution = Guid.NewGuid(); }
            var result = await api!.ResolveRate(profile!.CompanyId, lastImport.AuditId, new(pendingResolution, index, incoming, true));
            if (result.CompanyId != profile.CompanyId) throw new ApiException(502, "Historia dotyczy innej firmy.");
            pendingResolutionPayload = null;
            RateConflicts = RateConflicts.Where(x => x != selectedRateConflict).ToArray(); selectedRateConflict = null; ConfirmRateResolution = false;
            await ReadRates(0); await ReadCurrencyHistory(0); message = incoming ? "Świadomie przyjęto propozycję NBP z audytem poprzedniej wartości." : "Zachowano zapisany kurs; decyzję odnotowano w audycie.";
        });
    }
    public Task LoadCurrencyHistory() => Run(() => ReadCurrencyHistory(0));
    public Task NextCurrencyHistory() => Run(() => ReadCurrencyHistory(currencyHistoryOffset + 50));
    public Task PreviousCurrencyHistory() => Run(() => ReadCurrencyHistory(Math.Max(0, currencyHistoryOffset - 50)));
    private async Task ReadCurrencyHistory(int offset)
    {
        var rows = await api!.CurrencyHistory(profile!.CompanyId, offset);
        if (rows.Any(x => x.CompanyId != profile.CompanyId)) throw new ApiException(502, "Historia walut dotyczy innej firmy.");
        CurrencyHistoryRows = rows.Take(50).ToArray(); HasNextCurrencyHistory = rows.Length > 50; currencyHistoryOffset = offset;
    }
    private void ResetCurrencyState()
    {
        currencySelections = []; selectionSource = null; visibleSelectionRows = null; visibleSelectionSource = null;
        CurrencyActivityPrompt = false; CurrencyActivityConfirmation = ""; pendingActivity = null; pendingActivityPayload = null;
        currencyScreen = currencySettings = false; CurrencyRows = []; RateRows = []; CurrencyHistoryRows = []; selectedCurrency = null; CurrencyEditor = null;
        lastImport = null; RateConflicts = []; selectedRateConflict = null; ConfirmRateResolution = false;
        StoredRateDates = ""; RateImportSummary = "Pobieranie tylko ręczne. Daty oznaczają publikacje NBP.";
        pendingImportPayload = pendingResolutionPayload = null; rateCurrencyFilter = null; RateFromFilter = RateToFilter = null;
        ShowInactiveRateCurrencies = false; currenciesWithRates = []; RateCurrencyChoices = [];
        selectedCurrencyHistory = null; loadedRateFilter = null;
        RateTableFilter = rateOffset = currencyHistoryOffset = 0; HasNextRates = HasNextCurrencyHistory = false; currencySearch = "";
    }
}

public sealed class CurrencySelection(CurrencyResponse currency, bool selected, Action changed) : INotifyPropertyChanged
{
    private bool isChecked = selected;
    public CurrencyResponse Currency { get; } = currency;
    public string Code => Currency.Code;
    public string Label => Currency + (Currency.IsActive ? " · aktywna" : " · nieaktywna");
    public bool IsChecked
    {
        get => isChecked;
        set
        {
            if (isChecked == value) return;
            isChecked = value; PropertyChanged?.Invoke(this, new(nameof(IsChecked))); changed();
        }
    }
    public event PropertyChangedEventHandler? PropertyChanged;
}

public sealed class CurrencySettingsEditor(CurrencyResponse original) : INotifyPropertyChanged
{
    private bool active = original.IsActive;
    private bool a = original.DownloadA, b = original.DownloadB, c = original.DownloadC;
    private string? payload;
    private Guid operation = Guid.NewGuid();
    public event PropertyChangedEventHandler? PropertyChanged;
    public string Code => original.Code;
    public string Label => original.Code + " — " + original.Name;
    public bool IsPln => original.Code == "PLN";
    public bool IsActive { get => active; set { active = value; if (!value) a = b = c = false; Changed(); } }
    public bool DownloadA { get => a; set { a = value; Changed(); } }
    public bool DownloadB { get => b; set { b = value; Changed(); } }
    public bool DownloadC { get => c; set { c = value; Changed(); } }
    public bool IsDirty => active != original.IsActive || a != original.DownloadA || b != original.DownloadB || c != original.DownloadC;
    private void Changed() => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(string.Empty));
    public CurrencyWriteRequest Request()
    {
        var current = $"{active}/{a}/{b}/{c}";
        if (payload != current) { payload = current; operation = Guid.NewGuid(); }
        return new(operation, original.RowVersion, active, a, b, c);
    }
}
