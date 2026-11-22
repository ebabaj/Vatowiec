using Vatowiec.SharedContracts;

namespace Vatowiec.Desktop.ViewModels;

// ADR-044 krok 3: Ustawienia instalacji → Firmy → Odtwarzanie z kopii (administrator instalacji).
public sealed partial class MainViewModel
{
    private RestoreSourceRow[] restoreSources = [];
    private RestoreSourceRow? selectedRestoreSource;
    private bool restoreConfirmVisible;
    private Guid restoreOperationKey = Guid.NewGuid();

    public RestoreSourceRow[] RestoreSources => restoreSources;
    public bool HasRestoreSources => restoreSources.Length > 0;
    public RestoreSourceRow? SelectedRestoreSource
    {
        get => selectedRestoreSource;
        set { selectedRestoreSource = value; restoreConfirmVisible = false; restoreOperationKey = Guid.NewGuid(); ChangedAll(); }
    }
    public bool CanRequestRestore => IsCompanyOperationsScreen && IsInstallationAdministrator && !busy && selectedRestoreSource != null && !restoreConfirmVisible;
    public bool IsRestoreConfirmVisible => IsCompanyOperationsScreen && restoreConfirmVisible;
    public string RestoreConfirmText => selectedRestoreSource?.ConfirmText ?? "";

    private async Task ReadRestoreSources()
    {
        if (api == null) return;
        var selected = selectedRestoreSource?.Key;
        restoreSources = (await api.RestoreSources()).Select(x => new RestoreSourceRow(x)).ToArray();
        selectedRestoreSource = restoreSources.FirstOrDefault(x => x.Key == selected);
    }

    public void RequestRestore() { if (CanRequestRestore) { restoreConfirmVisible = true; ChangedAll(); } }
    public void CancelRestoreConfirmation() { restoreConfirmVisible = false; ChangedAll(); }

    public Task ConfirmRestore()
    {
        if (!restoreConfirmVisible || selectedRestoreSource == null || busy) return Task.CompletedTask;
        var source = selectedRestoreSource.Value;
        return Run(async () =>
        {
            await api!.RestoreCompany(new(restoreOperationKey, source.Source, source.BackupId,
                source.Source == "Deleted" ? source.CompanyId : null, source.FileName, ConfirmReplace: source.CompanyExists));
            restoreConfirmVisible = false; restoreOperationKey = Guid.NewGuid(); selectedRestoreSource = null;
            await ReadCompanyOperations(0);
            await ReadRestoreSources();
            await ReadInstallationCompanies();
            message = $"Przyjęto zlecenie odtworzenia firmy „{source.CompanyName}”. Odtwarzanie wykonuje usługa VATowiec Operations.";
        });
    }

    private void ResetCompanyRestore()
    {
        restoreSources = []; selectedRestoreSource = null; restoreConfirmVisible = false; restoreOperationKey = Guid.NewGuid();
    }
}

public sealed record RestoreSourceRow(RestoreSourceResponse Value)
{
    public string Key => $"{Value.Source}:{Value.BackupId}:{Value.CompanyId}:{Value.FileName}";
    public string Text => Value.Source switch
    {
        "Backup" => $"{Value.CompanyName} — kopia z {Value.CreatedAtUtc.ToLocalTime():yyyy-MM-dd HH:mm} ({Size})" +
            (Value.Kind == "PreRestore" ? " · stan sprzed odtworzenia" : ""),
        "Deleted" => $"{Value.CompanyName} — archiwum usuniętej firmy z {Value.CreatedAtUtc.ToLocalTime():yyyy-MM-dd HH:mm} ({Size})",
        _ => $"{Value.CompanyName} — plik {Value.FileName} z {Value.CreatedAtUtc.ToLocalTime():yyyy-MM-dd HH:mm} ({Size})" +
            (Value.CompanyExists ? " · zastąpi dane istniejącej firmy" : " · przywróci lub doda firmę")
    };
    public string ConfirmText => Value.CompanyExists
        ? $"Dane firmy „{Value.CompanyName}” zostaną zastąpione stanem z kopii z {Value.CreatedAtUtc.ToLocalTime():yyyy-MM-dd HH:mm}. " +
          "Na czas przełączenia firma będzie niedostępna. Dotychczasowa baza zostanie zachowana pod nazwą z dopiskiem „_przed_” " +
          "i dodatkowo powstanie kopia bezpieczeństwa. Czy na pewno odtworzyć?"
        : $"Firma „{Value.CompanyName}” zostanie przywrócona z kopii z {Value.CreatedAtUtc.ToLocalTime():yyyy-MM-dd HH:mm} " +
          "z tym samym identyfikatorem. Otrzymasz do niej pełne prawa; innym użytkownikom trzeba je nadać ponownie. Czy odtworzyć?";
    private string Size => $"{Math.Max(0.1, Value.SizeBytes / 1048576.0):0.0} MB";
}
