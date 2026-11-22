using System.ComponentModel;
using System.Globalization;
using Vatowiec.Desktop.Services;
using Vatowiec.SharedContracts;

namespace Vatowiec.Desktop.ViewModels;

// ADR-048 (ekran z własnym modelem widoku, ADR-046): Ustawienia instalacji → Kursy NBP.
// Wyłącznie administrator instalacji; serwer sprawdza rolę, CSRF i wersje przy każdym zapisie.
// Klient nie łączy się z NBP — „Pobierz teraz” zleca pobranie usłudze VATowiec Operations.
public sealed class ExchangeRateAdministrationViewModel(IVatowiecApiClient api) : INotifyPropertyChanged
{
    private static readonly CultureInfo Polish = CultureInfo.GetCultureInfo("pl-PL");
    private ExchangeRateAdministrationResponse? response;
    private DateTimeOffset? historyStart;
    private ExchangeRateConflictResponse? selectedConflict;
    private bool confirmResolution;

    public event PropertyChangedEventHandler? PropertyChanged;

    public bool IsLoaded => response != null;
    public string StatusSummary => response?.Status.Summary ?? "";
    public string HistoryStartInfo => response == null ? "" : response.HistoryStartIsDefault
        ? $"Domyślnie: {response.HistoryStartDate.ToString("dd.MM.yyyy", Polish)} (1 stycznia poprzedniego roku). Zapisanie daty utrwala wybór."
        : $"Zapisana data początkowa: {response.HistoryStartDate.ToString("dd.MM.yyyy", Polish)}.";
    public DateTimeOffset? HistoryStart
    {
        get => historyStart;
        set { historyStart = value; ChangedAll(); }
    }
    public bool IsDirty => response != null && historyStart is { } value && DateOnly.FromDateTime(value.Date) != response.HistoryStartDate;
    public ExchangeRateConflictResponse[] Conflicts => response?.Conflicts ?? [];
    public bool HasConflicts => Conflicts.Length > 0;
    public string ConflictsHeader => HasConflicts ? $"Konflikty kursów do rozstrzygnięcia: {Conflicts.Length}" : "Brak konfliktów kursów.";
    public ExchangeRateConflictResponse? SelectedConflict
    {
        get => selectedConflict;
        set { selectedConflict = value; confirmResolution = false; ChangedAll(); }
    }
    public bool ConfirmResolution { get => confirmResolution; set { confirmResolution = value; ChangedAll(); } }
    public bool CanResolve => selectedConflict != null && confirmResolution;
    public string ConflictComparison => selectedConflict == null ? "Wybierz konflikt z listy." :
        "Zapisano:\n" + RateText(selectedConflict.Existing) + "\nNBP opublikował:\n" + RateText(selectedConflict.Incoming) +
        $"\nWykryto: {selectedConflict.DetectedAtUtc.ToLocalTime():dd.MM.yyyy HH:mm}";
    public ExchangeRateOperationResponse[] History { get; private set; } = [];
    public int HistoryOffset { get; private set; }
    public bool HasNextHistory { get; private set; }
    public bool HasPreviousHistory => HistoryOffset > 0;

    public async Task Load()
    {
        Apply(await api.ExchangeRateAdministration());
        await ReadHistory(0);
    }

    public async Task ReadHistory(int offset)
    {
        var rows = await api.ExchangeRateHistory(offset);
        History = rows.Take(50).ToArray(); HasNextHistory = rows.Length > 50; HistoryOffset = offset;
        ChangedAll();
    }

    public async Task<string> RequestRun()
    {
        Apply(await api.RequestExchangeRateRun());
        await ReadHistory(0);
        return "Zlecono pobranie kursów NBP. Usługa VATowiec Operations wykona je w ciągu kilku minut; odśwież ekran, aby zobaczyć wynik.";
    }

    public async Task<string> SaveHistoryStart()
    {
        if (response == null || historyStart == null || !IsDirty) return "";
        var earlier = DateOnly.FromDateTime(historyStart.Value.Date) < response.HistoryStartDate;
        Apply(await api.SaveExchangeRateHistoryStart(new(DateOnly.FromDateTime(historyStart.Value.Date), response.RowVersion)));
        await ReadHistory(0);
        return earlier
            ? "Zapisano wcześniejszą datę początkową. Usługa VATowiec Operations dociągnie brakującą historię przy najbliższym przebiegu."
            : "Zapisano datę początkową historii. Zapisane wcześniej kursy pozostają bez zmian.";
    }

    public async Task<string> Resolve(bool acceptIncoming)
    {
        if (selectedConflict is not { } conflict || !confirmResolution) return "";
        Apply(await api.ResolveExchangeRateConflict(conflict.Id, new(acceptIncoming, conflict.RowVersion, conflict.RateRowVersion, true)));
        await ReadHistory(0);
        return acceptIncoming ? "Przyjęto dane NBP; poprzednią wartość zapisano w historii." : "Zachowano zapisany kurs; decyzję zapisano w historii.";
    }

    private void Apply(ExchangeRateAdministrationResponse value)
    {
        response = value;
        historyStart = new DateTimeOffset(value.HistoryStartDate.ToDateTime(TimeOnly.MinValue));
        selectedConflict = value.Conflicts.FirstOrDefault(x => x.Id == selectedConflict?.Id);
        confirmResolution = false;
        ChangedAll();
    }

    private static string RateText(RateValue value) => $"Tabela {value.TableNumber}, publikacja {value.EffectiveDate.ToString("dd.MM.yyyy", Polish)}" +
        (value.TradingDate is { } trading ? $", notowanie {trading.ToString("dd.MM.yyyy", Polish)}" : "") + "\n" +
        (value.Mid != null ? $"Średni {value.Mid.Value.ToString("0.0000########", Polish)}" :
            $"Kupna {value.Bid?.ToString("0.0000########", Polish)} · Sprzedaży {value.Ask?.ToString("0.0000########", Polish)}");

    private void ChangedAll() => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(string.Empty));
}
