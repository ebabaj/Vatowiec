namespace Vatowiec.Desktop.ViewModels;

// ADR-048: wyłącznie nawigacja do ekranu „Kursy NBP”; logika ekranu w ExchangeRateAdministrationViewModel (ADR-046).
public sealed partial class MainViewModel
{
    private ExchangeRateAdministrationViewModel? exchangeRateAdministration;
    public ExchangeRateAdministrationViewModel? ExchangeRateAdministration => exchangeRateAdministration;
    public bool IsExchangeRateAdministrationScreen => session != null && exchangeRateAdministration != null;

    public Task OpenExchangeRateAdministration()
    {
        if (!IsInstallationAdministrator || busy || IsOwnPasswordChange || api == null) return Task.CompletedTask;
        var screen = new ExchangeRateAdministrationViewModel(api);
        return Run(async () =>
        {
            gusSettings = false; exchangeRateAdministration = screen;
            screen.PropertyChanged += (_, _) => Changed(nameof(HasUnsavedChanges));
            await screen.Load();
        });
    }

    public Task RefreshExchangeRateAdministration() => exchangeRateAdministration is { } screen && !busy ? Run(screen.Load) : Task.CompletedTask;

    public Task RequestExchangeRateRun() => exchangeRateAdministration is { IsLoaded: true } screen && !busy
        ? Run(async () => message = await screen.RequestRun()) : Task.CompletedTask;

    public Task SaveExchangeRateHistoryStart() => exchangeRateAdministration is { IsDirty: true } screen && !busy
        ? Run(async () => message = await screen.SaveHistoryStart()) : Task.CompletedTask;

    public Task ResolveExchangeRateConflict(bool acceptIncoming) => exchangeRateAdministration is { CanResolve: true } screen && !busy
        ? Run(async () => message = await screen.Resolve(acceptIncoming)) : Task.CompletedTask;

    public Task NextExchangeRateHistory() => exchangeRateAdministration is { HasNextHistory: true } screen && !busy ? Run(() => screen.ReadHistory(screen.HistoryOffset + 50)) : Task.CompletedTask;
    public Task PreviousExchangeRateHistory() => exchangeRateAdministration is { HasPreviousHistory: true } screen && !busy ? Run(() => screen.ReadHistory(Math.Max(0, screen.HistoryOffset - 50))) : Task.CompletedTask;

    public void CloseExchangeRateAdministration()
    {
        if (busy) return;
        void Close() { exchangeRateAdministration = null; gusSettings = true; ChangedAll(); }
        if (exchangeRateAdministration?.IsDirty == true) RequestDiscard(Close); else Close();
    }
}
