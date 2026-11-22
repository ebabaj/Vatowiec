using Vatowiec.SharedContracts;

namespace Vatowiec.Desktop.ViewModels;

// ADR-045: Ustawienia instalacji → Firmy → Usuwanie firmy. Zleca administrator instalacji, wykonuje usługa Operations.
// Jedno zwykłe potwierdzenie; bez kopii archiwalnej dodatkowe ostrzeżenie o nieodwracalności.
public sealed partial class MainViewModel
{
    private InstallationCompanyResponse[] installationCompanies = [];
    private DeletedCompanyResponse[] deletedCompanies = [];
    private InstallationCompanyResponse? companyToDelete;
    private bool deleteWithArchive = true;
    private int deleteConfirmStep; // 0 brak, 1 pytanie, 2 ostrzeżenie bez archiwum
    private Guid deleteOperationKey = Guid.NewGuid();

    public InstallationCompanyResponse[] InstallationCompanies => installationCompanies.Where(x => x.IsActive).ToArray();
    public DeletedCompanyResponse[] DeletedCompanies => deletedCompanies;
    public string[] DeletedCompanyLines => deletedCompanies.Select(x =>
        $"{x.CompanyName} — usunięta {x.DeletedAtUtc.ToLocalTime():yyyy-MM-dd HH:mm} przez {x.DeletedByName}" +
        (x.Nip is null ? "" : $" · NIP {x.Nip}") + (x.HasArchive ? " · jest kopia archiwalna" : " · bez kopii archiwalnej")).ToArray();
    public bool HasDeletedCompanies => deletedCompanies.Length > 0;
    public InstallationCompanyResponse? CompanyToDelete
    {
        get => companyToDelete;
        set { companyToDelete = value; deleteConfirmStep = 0; deleteOperationKey = Guid.NewGuid(); ChangedAll(); }
    }
    public bool DeleteWithArchive
    {
        get => deleteWithArchive;
        set { deleteWithArchive = value; deleteConfirmStep = 0; deleteOperationKey = Guid.NewGuid(); ChangedAll(); }
    }
    public bool CanRequestCompanyDelete => IsCompanyOperationsScreen && IsInstallationAdministrator && !busy && companyToDelete != null && deleteConfirmStep == 0;
    public bool IsDeleteConfirmVisible => IsCompanyOperationsScreen && deleteConfirmStep == 1;
    public bool IsDeleteWithoutArchiveWarningVisible => IsCompanyOperationsScreen && deleteConfirmStep == 2;
    public string DeleteConfirmText => companyToDelete is null ? "" : $"Czy na pewno chcesz usunąć firmę „{companyToDelete.Name}”?" +
        (deleteWithArchive ? " Przed usunięciem powstanie kopia archiwalna na serwerze." : "");
    public string DeleteWithoutArchiveText => companyToDelete is null ? "" :
        $"Nie wybrano kopii archiwalnej. Usunięcie firmy „{companyToDelete.Name}” razem z bazą {companyToDelete.DatabaseName}, " +
        "jej kopiami, prawami użytkowników i historią jest nieodwracalne. Obowiązek przechowywania dokumentacji podatkowej spoczywa na podatniku.";
    public bool CanCancelCompanyOperation => IsCompanyOperationsScreen && IsInstallationAdministrator && !busy &&
        selectedCompanyOperation?.CanCancel == true;

    private async Task ReadInstallationCompanies()
    {
        if (api == null) return;
        installationCompanies = await api.InstallationCompanies();
        deletedCompanies = await api.DeletedCompanies();
        if (companyToDelete != null) companyToDelete = installationCompanies.FirstOrDefault(x => x.CompanyId == companyToDelete.CompanyId && x.IsActive);
    }

    public void RequestCompanyDelete()
    {
        if (!CanRequestCompanyDelete) return;
        deleteConfirmStep = 1; ChangedAll();
    }

    public void CancelCompanyDeleteConfirmation() { deleteConfirmStep = 0; ChangedAll(); }

    public Task ConfirmCompanyDelete()
    {
        if (companyToDelete == null || deleteConfirmStep == 0 || busy) return Task.CompletedTask;
        if (deleteConfirmStep == 1 && !deleteWithArchive) { deleteConfirmStep = 2; ChangedAll(); return Task.CompletedTask; }
        var company = companyToDelete;
        return Run(async () =>
        {
            await api!.DeleteCompany(new(deleteOperationKey, company.CompanyId, deleteWithArchive, ConfirmWithoutArchive: !deleteWithArchive));
            deleteConfirmStep = 0; deleteOperationKey = Guid.NewGuid(); companyToDelete = null; deleteWithArchive = true;
            await ReadCompanyOperations(0);
            await ReadInstallationCompanies();
            message = $"Przyjęto zlecenie usunięcia firmy „{company.Name}”. Firma jest już niedostępna; usuwanie wykonuje usługa VATowiec Operations.";
        });
    }

    public Task CancelCompanyOperation()
    {
        if (!CanCancelCompanyOperation) return Task.CompletedTask;
        var row = selectedCompanyOperation!;
        return Run(async () =>
        {
            await api!.CancelCompanyOperation(row.Value.Id, new(row.Value.RowVersion));
            await ReadCompanyOperations(companyOperationsOffset);
            await ReadInstallationCompanies();
            message = "Anulowano usuwanie. Firma jest ponownie dostępna.";
        });
    }

    private void ResetCompanyDeletion()
    {
        installationCompanies = []; deletedCompanies = []; companyToDelete = null; deleteWithArchive = true; deleteConfirmStep = 0;
        deleteOperationKey = Guid.NewGuid();
    }
}
