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
    public bool CanImportRates => IsCurrencyScreen && selectedCompany?.Permissions.Contains("ImportExchangeRates") == true;
    public bool CanResolveRates => CanImportRates && selectedCompany?.Permissions.Contains("ManageCurrencies") == true && selectedRateConflict != null;
    public bool CanChooseCurrencyTables => CanConfigureCurrencies && CurrencyEditor is { IsActive: true, IsPln: false };
    public string CurrencyScreenTitle => currencySettings ? "Ustawienia firmy → Waluty i kursy NBP" : "Słowniki → Waluty i kursy NBP";
    public CurrencyResponse[] CurrencyRows { get; private set; } = [];
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
    public string CurrencySearch { get => currencySearch; set { currencySearch = value; Changed(nameof(VisibleCurrencyRows)); } }
    public CurrencyResponse? SelectedCurrency
    {
        get => selectedCurrency;
        set
        {
            if (busy || DiscardPrompt || value == selectedCurrency) return;
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
    public CurrencyResponse? RateCurrencyFilter { get; set; }
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
        var code = selectedCurrency?.Code;
        selectedCurrency = CurrencyRows.FirstOrDefault(x => x.Code == code) ?? CurrencyRows.FirstOrDefault(x => x.Code == "EUR");
        SetCurrencyEditor(selectedCurrency);
        await ReadRates(0); await ReadCurrencyHistory(0);
    }
    public void ShowCurrencySettings(bool settings)
    {
        if (busy) return;
        void Select() { currencySettings = settings; SetCurrencyEditor(selectedCurrency); ChangedAll(); }
        if (CurrencyEditor?.IsDirty == true) RequestDiscard(Select); else Select();
    }
    public void CloseCurrencies()
    {
        if (busy) return;
        void Close() { ResetCurrencyState(); ChangedAll(); }
        if (CurrencyEditor?.IsDirty == true) RequestDiscard(Close); else Close();
    }
    public void CancelCurrencyChanges() { if (!busy) { SetCurrencyEditor(selectedCurrency); ChangedAll(); } }
    public Task RefreshCurrencies()
    {
        if (CurrencyEditor?.IsDirty == true)
        { message = "Najpierw zapisz lub porzuć zmiany ustawień przed odświeżeniem słownika."; ChangedAll(); return Task.CompletedTask; }
        return Run(RefreshCurrencyData);
    }
    public Task SaveCurrencySettings()
    {
        if (!CanConfigureCurrencies || CurrencyEditor == null) return Task.CompletedTask;
        return Run(async () =>
        {
            var result = await api!.SaveCurrency(profile!.CompanyId, CurrencyEditor.Code, CurrencyEditor.Request());
            if (result.CompanyId != profile.CompanyId || result.Code != CurrencyEditor.Code) throw new ApiException(502, "Odpowiedź ustawień dotyczy innej firmy lub waluty.");
            CurrencyRows = CurrencyRows.Select(x => x.Code == result.Code ? result : x).ToArray(); selectedCurrency = result; SetCurrencyEditor(result);
            await ReadCurrencyHistory(0); message = "Zapisano ustawienia waluty z audytem. Nie pobierano kursów NBP.";
        });
    }
    public Task LoadRates() => Run(() => ReadRates(0));
    public Task NextRates() => Run(() => ReadRates(rateOffset + 50));
    public Task PreviousRates() => Run(() => ReadRates(Math.Max(0, rateOffset - 50)));
    public void ClearRateFilters()
    { if (!busy) { RateCurrencyFilter = null; RateTableFilter = 0; RateFromFilter = RateToFilter = null; ChangedAll(); } }
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
        if (!CanImportRates) return Task.CompletedTask;
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
        currencyScreen = currencySettings = false; CurrencyRows = []; RateRows = []; CurrencyHistoryRows = []; selectedCurrency = null; CurrencyEditor = null;
        lastImport = null; RateConflicts = []; selectedRateConflict = null; ConfirmRateResolution = false;
        StoredRateDates = ""; RateImportSummary = "Pobieranie tylko ręczne. Daty oznaczają publikacje NBP.";
        pendingImportPayload = pendingResolutionPayload = null; RateCurrencyFilter = null; RateFromFilter = RateToFilter = null;
        selectedCurrencyHistory = null; loadedRateFilter = null;
        RateTableFilter = rateOffset = currencyHistoryOffset = 0; HasNextRates = HasNextCurrencyHistory = false; currencySearch = "";
    }
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
