using Vatowiec.Desktop.Services;
using Vatowiec.SharedContracts;

namespace Vatowiec.Desktop.ViewModels;

public sealed partial class MainViewModel
{
    private ContractorResponse? selectedContractor;
    private ContractorEditor? editor;
    private ContractorResponse? conflictCurrent;
    private int pageOffset;
    private bool hasNextPage;
    private (string, string, int) loadedFilter;

    public string SearchText { get; set; } = "";
    public string FilterCountry { get; set; } = "";
    public int FilterActivity { get; set; }
    public bool CanEditContractors => CanViewContractors && selectedCompany?.CanEditContractors == true;
    public bool CanBrowse => CanViewContractors && !IsEditing;
    public bool IsBrowsingWorkspace => IsWorkspace && !IsEditing;
    public bool IsEditing => editor is not null;
    public bool CanSaveContractor => IsEditing && !HasConflict && CanEditContractors;
    public bool HasConflict => conflictCurrent is not null;
    public ContractorEditor? Editor => editor;
    public string EditorTitle => editor?.IsNew == true ? "Nowy kontrahent" : "Edycja kontrahenta";
    public string ServerVersionSummary => conflictCurrent is null ? "" : Summary(conflictCurrent);
    public string LocalVersionSummary => editor is null ? "" : $"Kod: {editor.Code}\nNazwa: {editor.Name}\nIdentyfikator: {editor.TaxIdentifier}\nKraj: {editor.CountryCode}\nStatus: {(editor.IsActive ? "Aktywny" : "Nieaktywny")}";
    public bool HasPreviousPage => pageOffset > 0;
    public bool HasNextPage => hasNextPage;
    public string PageLabel => $"Strona {pageOffset / 50 + 1} · do 50 pozycji";
    public ContractorResponse? SelectedContractor
    {
        get => selectedContractor;
        set { selectedContractor = value; Changed(); }
    }

    public Task NextPage() => Run(() => LoadPage(pageOffset + 50));
    public Task PreviousPage() => Run(() => LoadPage(Math.Max(0, pageOffset - 50)));

    public void NewContractor()
    {
        if (busy || !CanEditContractors || IsEditing) return;
        editor = new ContractorEditor();
        conflictCurrent = null;
        message = "";
        ChangedAll();
    }

    public Task EditContractor() => Run(async () =>
    {
        if (!CanEditContractors || api is null || profile is null || selectedContractor is null)
            throw new ArgumentException("Zaznacz kontrahenta do edycji. Wymagane jest uprawnienie edycji.");
        var row = await api.Contractor(profile.CompanyId, selectedContractor.Id);
        CheckContractorCompany(row);
        editor = new ContractorEditor(row);
        conflictCurrent = null;
    });

    public Task SaveContractor() => Run(async () =>
    {
        if (!CanSaveContractor || api is null || profile is null || editor is null) return;
        var value = editor.Request();
        var errors = ContractorValidation.Validate(value);
        if (errors.Count != 0) throw new ArgumentException(string.Join(Environment.NewLine, errors.Values.SelectMany(x => x)));
        try
        {
            var saved = await api.SaveContractor(profile.CompanyId, value, editor.IsNew);
            CheckContractorCompany(saved);
            editor = null;
            conflictCurrent = null;
            contractors = [];
            contractorsLoaded = false;
            selectedContractor = null;
            ChangedAll();
            message = "Zapisano kontrahenta i historię zmiany. Odśwież lub wyszukaj pozycję w kartotece.";
        }
        catch (ApiException ex) when (ex.Code == "contractor_conflict" && ex.Current is not null)
        {
            CheckContractorCompany(ex.Current);
            if (ex.Current.Id != value.Id) throw new ApiException(502, "Niezgodny identyfikator odpowiedzi API.");
            conflictCurrent = ex.Current;
            message = "Konflikt: niczego nie nadpisano. Porównaj wersje poniżej.";
        }
    });

    public void ResolveConflict(bool keepLocal)
    {
        if (busy || editor is null || conflictCurrent is null) return;
        if (keepLocal) editor.RowVersion = conflictCurrent.RowVersion;
        else editor = new ContractorEditor(conflictCurrent);
        conflictCurrent = null;
        message = keepLocal ? "Zachowano Twoje pola. Sprawdź je i ponownie kliknij Zapisz, aby zastąpić porównaną wersję."
            : "Wczytano wersję z serwera. Twoje poprzednie zmiany zostały odrzucone.";
        ChangedAll();
    }

    public void CancelEditor()
    {
        if (busy) return;
        editor = null;
        conflictCurrent = null;
        message = "Anulowano edycję. Nie zapisano zmian.";
        ChangedAll();
    }

    private void CheckContractorCompany(ContractorResponse row)
    {
        if (profile is null || row.CompanyId != profile.CompanyId)
            throw new ApiException(502, "API zwróciło dane innej firmy. Odmowa wyświetlenia danych.");
    }
    private static string Summary(ContractorResponse row) => $"Kod: {row.Code}\nNazwa: {row.Name}\nIdentyfikator: {row.TaxIdentifier}\nKraj: {row.CountryCode}\nStatus: {row.ActivityLabel}";
    private void ClearContractorState()
    {
        editor = null;
        conflictCurrent = null;
        selectedContractor = null;
        pageOffset = 0;
        hasNextPage = false;
        SearchText = "";
        FilterCountry = "";
        FilterActivity = 0;
        loadedFilter = default;
    }
}

public sealed class ContractorEditor
{
    public ContractorEditor() { }
    public ContractorEditor(ContractorResponse row)
    {
        Id = row.Id;
        Code = row.Code;
        Name = row.Name;
        TaxIdentifier = row.TaxIdentifier ?? "";
        CountryCode = row.CountryCode;
        IsActive = row.IsActive;
        RowVersion = row.RowVersion;
        IsNew = false;
    }
    public Guid Id { get; } = Guid.NewGuid();
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public string TaxIdentifier { get; set; } = "";
    public string CountryCode { get; set; } = "PL";
    public bool IsActive { get; set; } = true;
    public bool IsNew { get; } = true;
    public string? RowVersion { get; set; }
    public ContractorWriteRequest Request() => new(Id, Code, Name, TaxIdentifier, CountryCode, IsActive, RowVersion);
}
