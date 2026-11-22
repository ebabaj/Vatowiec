using Vatowiec.SharedContracts;

namespace Vatowiec.Desktop.ViewModels;

// ADR-044 krok 1: Ustawienia instalacji → Firmy. Klient tylko zleca i odczytuje stan przez API.
public sealed partial class MainViewModel
{
    private const int CompanyOperationsPage = 50;
    private bool companyOperationsScreen;
    private CompanyOperationRow[] companyOperations = [];
    private ServerProfileResponse[] serverProfiles = [];
    private ServerProfileResponse? selectedServerProfile;
    private CompanyOperationRow? selectedCompanyOperation;
    private string newCompanyName = "";
    private int companyOperationsOffset;
    // Ten sam klucz przy ponowieniu po utracie odpowiedzi; nowy po sukcesie albo zmianie danych.
    private Guid provisionOperationKey = Guid.NewGuid();

    public bool IsCompanyOperationsScreen => session != null && companyOperationsScreen;
    public CompanyOperationRow[] CompanyOperations => companyOperations;
    public ServerProfileResponse[] ServerProfiles => serverProfiles;
    public bool HasNoCompanyOperations => IsCompanyOperationsScreen && companyOperations.Length == 0;
    public bool CompanyOperationsHasNext { get; private set; }
    public bool CompanyOperationsHasPrevious => companyOperationsOffset > 0;
    public ServerProfileResponse? SelectedServerProfile
    {
        get => selectedServerProfile;
        set { if (selectedServerProfile != value) { selectedServerProfile = value; provisionOperationKey = Guid.NewGuid(); ChangedAll(); } }
    }
    public string NewCompanyName
    {
        get => newCompanyName;
        set { if (newCompanyName != value) { newCompanyName = value; provisionOperationKey = Guid.NewGuid(); ChangedAll(); } }
    }
    public CompanyOperationRow? SelectedCompanyOperation
    {
        get => selectedCompanyOperation;
        set { selectedCompanyOperation = value; ChangedAll(); }
    }
    // ADR-045: program pyta tylko o nazwę firmy; nazwę bazy wyznacza serwer (tu wyłącznie podgląd).
    public string? NewCompanyDatabaseName => CompanyDatabaseNames.FromCompanyName(newCompanyName);
    public string NewCompanyDatabaseText => string.IsNullOrWhiteSpace(newCompanyName) ? ""
        : NewCompanyDatabaseName is { } database ? $"Baza SQL: {database}" : "Nazwa firmy musi zawierać litery lub cyfry.";
    public bool IsServerChoiceVisible => serverProfiles.Length > 1;
    public bool CanProvisionCompany => IsCompanyOperationsScreen && IsInstallationAdministrator && !busy &&
        NewCompanyDatabaseName != null && newCompanyName.Trim().Length <= 256 && selectedServerProfile != null;
    public bool CanRetryCompanyOperation => IsCompanyOperationsScreen && IsInstallationAdministrator && !busy &&
        selectedCompanyOperation?.CanRetry == true;
    public Guid ProvisionOperationKey => provisionOperationKey;

    public Task OpenCompanyOperations()
    {
        if (!IsInstallationAdministrator || busy || IsOwnPasswordChange) return Task.CompletedTask;
        return Run(async () =>
        {
            gusSettings = false; companyOperationsScreen = true;
            serverProfiles = await api!.ServerProfiles();
            selectedServerProfile = serverProfiles.Length == 1 ? serverProfiles[0] : null;
            await ReadCompanyOperations(0);
            await ReadBackupPolicy();
            await ReadInstallationCompanies();
        });
    }

    public Task RefreshCompanyOperations(int direction = 0) =>
        Run(() => ReadCompanyOperations(Math.Max(0, companyOperationsOffset + direction * CompanyOperationsPage)));

    private async Task ReadCompanyOperations(int offset)
    {
        if (api == null || !IsCompanyOperationsScreen) return;
        var rows = await api.CompanyOperations(offset);
        var selected = selectedCompanyOperation?.Value.Id;
        companyOperations = rows.Take(CompanyOperationsPage).Select(x => new CompanyOperationRow(x)).ToArray();
        CompanyOperationsHasNext = rows.Length > CompanyOperationsPage;
        companyOperationsOffset = offset;
        selectedCompanyOperation = companyOperations.FirstOrDefault(x => x.Value.Id == selected);
    }

    public Task ProvisionCompany()
    {
        if (!CanProvisionCompany) return Task.CompletedTask;
        return Run(async () =>
        {
            var name = newCompanyName.Trim();
            var operation = await api!.ProvisionCompany(new(provisionOperationKey, name, selectedServerProfile!.Key));
            newCompanyName = ""; provisionOperationKey = Guid.NewGuid();
            await ReadCompanyOperations(0);
            selectedCompanyOperation = companyOperations.FirstOrDefault(x => x.Value.Id == operation.Id);
            message = $"Przyjęto zlecenie utworzenia firmy „{operation.CompanyName}”. Bazę tworzy usługa VATowiec Operations; odśwież listę, aby zobaczyć postęp.";
        });
    }

    public Task RetryCompanyOperation()
    {
        if (!CanRetryCompanyOperation) return Task.CompletedTask;
        var row = selectedCompanyOperation!;
        return Run(async () =>
        {
            await api!.RetryCompanyOperation(row.Value.Id, new(row.Value.RowVersion));
            await ReadCompanyOperations(companyOperationsOffset);
            message = "Zlecono ponowienie. Usługa ponownie sprawdzi stan bazy przed kontynuacją.";
        });
    }

    public Task CloseCompanyOperations()
    {
        if (busy) return Task.CompletedTask;
        return Run(async () =>
        {
            ResetCompanyOperations(); gusSettings = true;
            // Nowa firma pojawia się na liście firm dopiero po etapie Ready.
            if (api != null && profile == null) companies = await api.Companies();
        });
    }

    private void ResetCompanyOperations()
    {
        companyOperationsScreen = false; companyOperations = []; serverProfiles = []; selectedServerProfile = null;
        selectedCompanyOperation = null; newCompanyName = ""; companyOperationsOffset = 0; CompanyOperationsHasNext = false;
        provisionOperationKey = Guid.NewGuid(); backupPolicy = null; policyRetained = ""; ResetCompanyDeletion();
    }
}

public sealed record CompanyOperationRow(CompanyOperationResponse Value)
{
    public string Name => Value.Kind switch { "Backup" => "Kopia: ", "Delete" => "Usunięcie: ", _ => "" } + Value.CompanyName;
    public bool CanRetry => Value.Status is "Failed" or "NeedsReconciliation";
    public bool CanCancel => Value.Kind == "Delete" && Value.Status is "Pending" or "Failed" or "NeedsReconciliation" && Value.Stage is not ("DatabaseDropped" or "Ready");
    public string Stage => Value.Stage switch
    {
        "Requested" => "Przyjęte",
        "DatabaseCreated" => "Baza utworzona",
        "SchemaApplied" => "Schemat bazy utworzony",
        "ProfileInitialized" => "Profil firmy zapisany",
        "BackupWritten" => "Kopia zapisana",
        "BackupVerified" => "Kopia zweryfikowana",
        "CompanyDeactivated" => "Firma zablokowana",
        "ArchiveWritten" => "Archiwum zapisane",
        "ArchiveVerified" => "Archiwum zweryfikowane",
        "DatabaseDropped" => "Baza usunięta",
        "Ready" => Value.Kind == "Backup" ? "Kopia gotowa" : "Firma gotowa",
        _ => Value.Stage
    };
    public string Status => Value.Status switch
    {
        "Pending" => Value.Attempt > 0 ? "Oczekuje na ponowienie przez usługę" : "Oczekuje na usługę VATowiec Operations",
        "Running" => "W toku",
        "Failed" => "Błąd — wymaga ponowienia",
        "NeedsReconciliation" => "Wymaga sprawdzenia przez administratora",
        "Completed" => "Zakończone",
        "Cancelled" => "Anulowane",
        _ => Value.Status
    };
    public string Error => Value.LastErrorCode switch
    {
        null => "",
        "database_not_owned" => "Na serwerze istnieje baza o tej nazwie, która nie należy do tej operacji. Program jej nie przejmie ani nie usunie.",
        "database_missing" => "Baza utworzona wcześniej przez tę operację zniknęła z serwera. Sprawdź serwer SQL przed ponowieniem.",
        "company_identity_mismatch" => "Baza zawiera profil innej firmy. Sprawdź serwer SQL przed ponowieniem.",
        "schema_history_unknown" => "Baza ma nieznaną historię migracji. Sprawdź serwer SQL przed ponowieniem.",
        "catalog_conflict" => "Katalog instalacji zawiera już tę firmę lub bazę. Sprawdź stan przed ponowieniem.",
        "backup_root_missing" => "Usługa VATowiec Operations nie ma skonfigurowanego katalogu kopii (Operations:BackupRoot).",
        "backup_storage_failed" => "Nie udało się zapisać, odczytać lub usunąć pliku kopii. Sprawdź uprawnienia katalogu kopii i wolne miejsce.",
        "backup_file_missing" => "Plik kopii zniknął przed zakończeniem operacji. Zleć nową kopię.",
        "company_database_changed" => "Baza firmy zmieniła się od zlecenia. Sprawdź stan przed ponowieniem.",
        "company_missing" => "Firma nie istnieje już w katalogu instalacji.",
        "archive_file_missing" => "Plik kopii archiwalnej zniknął. Baza nie została usunięta; sprawdź katalog archiwum.",
        "server_profile_missing" or "server_profile_invalid" or "server_profile_mismatch" or "server_profile_policy" =>
            "Usługa VATowiec Operations nie ma zgodnego profilu połączenia dla wybranego serwera. Popraw konfigurację usługi i ponów.",
        var code when code.StartsWith("sql_", StringComparison.Ordinal) => $"Błąd SQL Server nr {code[4..]}. Sprawdź uprawnienia usługi i dostępność serwera.",
        _ => "Operacja nie powiodła się. Usługa ponowi ją automatycznie albo wymaga ręcznego ponowienia."
    };
    public string Summary => $"{Name} — {Stage} · {Status}" + (Value.Attempt > 1 ? $" · próba {Value.Attempt}" : "");
    public string Details => $"Zlecił: {Value.RequestedByName} · {Value.CreatedAtUtc.ToLocalTime():yyyy-MM-dd HH:mm}" +
        (Value.CompletedAtUtc is { } done ? $" · zakończono {done.ToLocalTime():yyyy-MM-dd HH:mm}" : "");
}
