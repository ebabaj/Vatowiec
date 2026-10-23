using Vatowiec.Desktop.Services;
using Vatowiec.SharedContracts;

namespace Vatowiec.Desktop.ViewModels;

public sealed partial class MainViewModel
{
    private ContractorResponse? selectedContractor;
    private ContractorEditor? editor;
    private ContractorResponse? conflictCurrent;
    private string conflictParentLabel = "Brak powiązania nadrzędnego";
    private int pageOffset;
    private bool hasNextPage;
    private (string, string, int) loadedFilter;

    public string SearchText { get; set; } = "";
    public string FilterCountry { get; set; } = "";
    public int FilterActivity { get; set; }
    public bool CanEditContractors => CanViewContractors && selectedCompany?.CanEditContractors == true;
    public bool CanManagePesel => selectedCompany?.Permissions.Contains("ManageContractorPesel", StringComparer.Ordinal) == true;
    public bool CanBrowse => CanViewContractors && !IsEditing && !IsDataPortSettings && !IsCurrencyScreen && !IsGusSettings;
    public bool IsBrowsingWorkspace => IsWorkspace && !IsEditing && !IsDataPortSettings && !IsCurrencyScreen && !IsGusSettings;
    public bool IsEditing => editor is not null;
    public bool CanSaveContractor => IsMainTab && IsEditing && !HasConflict && CanEditContractors && DetailEditor is null;
    public bool HasConflict => conflictCurrent is not null;
    public ContractorEditor? Editor => editor;
    public string EditorTitle => editor?.IsNew == true ? "Nowy kontrahent" : "Edycja kontrahenta";
    public string ServerVersionSummary => conflictCurrent is null ? "" : Summary(conflictCurrent, conflictParentLabel);
    public string LocalVersionSummary => editor is null ? "" : $"Kod: {editor.Code}\nNazwa: {editor.Name}\nTaxId: {editor.TaxId}\nVatEuId: {editor.VatEuId}\nKsefIdWew: {editor.KsefIdWew}\nREGON: {editor.Regon}\nKRS: {editor.Krs}\nBDO: {editor.Bdo}\nPESEL: dane chronione — porównaj w formularzu po wczytaniu wersji\nNadrzędny: {ParentLabel}\nKraj: {editor.CountryCode}\nStatus: {(editor.IsActive ? "Aktywny" : "Nieaktywny")}";
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
        if (busy || !CanEditContractors || IsEditing || IsDataPortSettings || IsCurrencyScreen || IsGusSettings) return;
        editor = new ContractorEditor();
        ObserveIdentifierEdits();
        conflictCurrent = null;
        message = "";
        ChangedAll();
    }

    public Task EditContractor() => Run(async () =>
    {
        if (!CanViewContractors || api is null || profile is null || selectedContractor is null)
            throw new ArgumentException("Zaznacz kontrahenta do otwarcia.");
        var row = await api.Contractor(profile.CompanyId, selectedContractor.Id);
        CheckContractorCompany(row);
        editor = new ContractorEditor(row);
        ObserveIdentifierEdits();
        conflictCurrent = null;
        await LoadFormContext();
    });

    public Task SaveContractor() => SaveContractorWithWarning(false);
    public Task AcceptIdentifierWarning() => SaveContractorWithWarning(true);
    private Task SaveContractorWithWarning(bool accept) => Run(async () =>
    {
        if (!CanSaveContractor || api is null || profile is null || editor is null) return;
        var value = editor.Request();
        var errors = ContractorValidation.Validate(value);
        editor.SetErrors(errors);
        if (errors.Count != 0) throw new ArgumentException(string.Join(Environment.NewLine, errors.Values.SelectMany(x => x)));
        try
        {
            var previousHash = identifierPreview?.WarningHash;
            identifierPreview = await api.ValidateIdentifiers(profile.CompanyId, value);
            var changed = ApplyIdentifierPreview(identifierPreview);
            if (identifierPreview.NeedsWarning && (!accept || previousHash != identifierPreview.WarningHash))
            {
                message = "Sprawdź ostrzeżenia przy identyfikatorach. Świadomy zapis wymaga osobnego potwierdzenia.";
                return;
            }
            if (changed && !accept) { message = "Pokazano znormalizowane identyfikatory. Sprawdź postać i ponownie wybierz Zapisz."; return; }
            value = editor.Request() with { AcceptWarning = accept, WarningHash = accept ? previousHash : null };
            var saved = await api.SaveContractor(profile.CompanyId, value, editor.IsNew);
            CheckContractorCompany(saved);
            editor = null;
            conflictCurrent = null;
            contractors = [];
            contractorsLoaded = false;
            selectedContractor = null;
            ResetDetails();
            ChangedAll();
            message = "Zapisano kontrahenta i historię zmiany. Odśwież lub wyszukaj pozycję w kartotece.";
        }
        catch (ApiException ex) when (ex.Code == "contractor_conflict" && ex.Current is not null)
        {
            CheckContractorCompany(ex.Current);
            if (ex.Current.Id != value.Id) throw new ApiException(502, "Niezgodny identyfikator odpowiedzi API.");
            conflictCurrent = ex.Current;
            conflictParentLabel = "Brak powiązania nadrzędnego";
            if (ex.Current.ParentId.HasValue)
            {
                var parent = await api.Contractor(profile.CompanyId, ex.Current.ParentId.Value);
                CheckContractorCompany(parent);
                conflictParentLabel = parent.ToString();
            }
            message = "Konflikt: niczego nie nadpisano. Porównaj wersje poniżej.";
        }
        catch (ApiException ex) when (ex.Code is "hierarchy_cycle" or "invalid_parent")
        {
            editor?.SetErrors(new() { ["ParentId"] = [ex.Message] });
            message = ex.Message;
        }
        catch (ApiException ex) when (ex.Code == "validation_failed" && ex.Errors is not null)
        {
            editor?.SetErrors(ex.Errors);
            message = ex.Message;
        }
        catch (ApiException ex) when (ex.Code == "identifiers_warning" && editor is not null)
        {
            identifierPreview = await api.ValidateIdentifiers(profile.CompanyId, editor.Request());
            ApplyIdentifierPreview(identifierPreview);
            message = "Ostrzeżenia lub duplikaty zmieniły się. Przejrzyj je przed potwierdzeniem.";
        }
    });

    public void ResolveConflict(bool keepLocal)
    {
        if (busy || editor is null || conflictCurrent is null) return;
        if (keepLocal) editor.RowVersion = conflictCurrent.RowVersion;
        else { editor = new ContractorEditor(conflictCurrent); ObserveIdentifierEdits(); ParentLabel = conflictParentLabel; }
        conflictCurrent = null;
        message = keepLocal ? "Zachowano Twoje pola. Sprawdź je i ponownie kliknij Zapisz, aby zastąpić porównaną wersję."
            : "Wczytano wersję z serwera. Twoje poprzednie zmiany zostały odrzucone.";
        ChangedAll();
    }

    public void CancelEditor()
    {
        if (busy) return;
        if (HasUnsavedChanges) { RequestDiscard(CloseEditor); return; }
        CloseEditor();
    }
    private void CloseEditor()
    {
        editor = null;
        conflictCurrent = null;
        ResetDetails();
        message = "Anulowano edycję. Nie zapisano zmian.";
        ChangedAll();
    }

    private void CheckContractorCompany(ContractorResponse row)
    {
        if (profile is null || row.CompanyId != profile.CompanyId)
            throw new ApiException(502, "API zwróciło dane innej firmy. Odmowa wyświetlenia danych.");
    }
    private static string Summary(ContractorResponse row, string parentLabel) => $"Kod: {row.Code}\nNazwa: {row.Name}\nTaxId: {row.TaxId}\nVatEuId: {row.VatEuId}\nKsefIdWew: {row.KsefIdWew}\nREGON: {row.Regon}\nKRS: {row.Krs}\nBDO: {row.Bdo}\nPESEL: dane chronione — porównaj w formularzu po wczytaniu wersji\nNadrzędny: {parentLabel}\nKraj: {row.CountryCode}\nStatus: {row.ActivityLabel}";
    private void ClearContractorState()
    {
        ResetDetails();
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

public sealed class ContractorEditor : System.ComponentModel.INotifyPropertyChanged
{
    public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;
    public ContractorEditor() { }
    public ContractorEditor(ContractorResponse row)
    {
        Id = row.Id;
        ParentId = row.ParentId;
        Code = row.Code;
        Name = row.Name;
        TaxId = row.TaxId ?? "";
        VatEuId = row.VatEuId ?? "";
        KsefIdWew = row.KsefIdWew ?? "";
        Regon = row.Regon ?? ""; Krs = row.Krs ?? ""; Bdo = row.Bdo ?? ""; Pesel = row.Pesel ?? "";
        CountryCode = row.CountryCode;
        IsActive = row.IsActive;
        RowVersion = row.RowVersion;
        IsNew = false;
        original = System.Text.Json.JsonSerializer.Serialize(Request());
    }
    public int Id { get; }
    public Guid OperationId { get; } = Guid.NewGuid();
    public int? ParentId { get; set; }
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    private string taxId = "", vatEuId = "", ksefIdWew = "";
    private string regon = "", krs = "", bdo = "", pesel = "";
    public string Regon { get => regon; set { regon = value; PropertyChanged?.Invoke(this, new(nameof(Regon))); } }
    public string Krs { get => krs; set { krs = value; PropertyChanged?.Invoke(this, new(nameof(Krs))); } }
    public string Bdo { get => bdo; set { bdo = value; PropertyChanged?.Invoke(this, new(nameof(Bdo))); } }
    public string Pesel { get => pesel; set { pesel = value; PropertyChanged?.Invoke(this, new(nameof(Pesel))); } }
    public string TaxId { get => taxId; set { taxId = value; PropertyChanged?.Invoke(this, new(nameof(TaxId))); } }
    public string VatEuId { get => vatEuId; set { vatEuId = value; PropertyChanged?.Invoke(this, new(nameof(VatEuId))); } }
    public string KsefIdWew { get => ksefIdWew; set { ksefIdWew = value; PropertyChanged?.Invoke(this, new(nameof(KsefIdWew))); } }
    private string countryCode = "PL";
    public string CountryCode { get => countryCode; set { countryCode = value; PropertyChanged?.Invoke(this, new(nameof(CountryCode))); } }
    public bool IsActive { get; set; } = true;
    public bool IsNew { get; } = true;
    public string? RowVersion { get; set; }
    private string? original;
    public bool IsDirty => original is null ? Code.Length != 0 || Name.Length != 0 || TaxId.Length != 0 || VatEuId.Length != 0 || KsefIdWew.Length != 0 || Regon.Length != 0 || Krs.Length != 0 || Bdo.Length != 0 || Pesel.Length != 0 || ParentId.HasValue || CountryCode != "PL" || !IsActive
        : original != System.Text.Json.JsonSerializer.Serialize(Request());
    public string CodeError { get; private set; } = "";
    public string NameError { get; private set; } = "";
    public string CountryError { get; private set; } = "";
    public string ParentError { get; private set; } = "";
    public string TaxIdError { get; private set; } = "";
    public string VatEuIdError { get; private set; } = "";
    public string KsefIdWewError { get; private set; } = "";
    public string StandardIdentifierErrors { get; private set; } = "";
    public void SetErrors(Dictionary<string, string[]> errors)
    {
        StandardIdentifierErrors = string.Join(" ", new[] { "Regon", "Krs", "Bdo", "Pesel" }.Where(errors.ContainsKey).SelectMany(x => errors[x]));
        PropertyChanged?.Invoke(this, new(nameof(StandardIdentifierErrors)));
        CodeError = errors.TryGetValue("Code", out var code) ? string.Join(" ", code) : "";
        NameError = errors.TryGetValue("Name", out var name) ? string.Join(" ", name) : "";
        CountryError = errors.TryGetValue("CountryCode", out var country) ? string.Join(" ", country) : "";
        ParentError = errors.TryGetValue("ParentId", out var parent) ? string.Join(" ", parent) : "";
        TaxIdError = errors.TryGetValue("TaxId", out var tax) ? string.Join(" ", tax) : "";
        VatEuIdError = errors.TryGetValue("VatEuId", out var vat) ? string.Join(" ", vat) : "";
        KsefIdWewError = errors.TryGetValue("KsefIdWew", out var ksef) ? string.Join(" ", ksef) : "";
        PropertyChanged?.Invoke(this, new(nameof(TaxIdError)));
        PropertyChanged?.Invoke(this, new(nameof(VatEuIdError)));
        PropertyChanged?.Invoke(this, new(nameof(KsefIdWewError)));
        PropertyChanged?.Invoke(this, new(nameof(CodeError)));
        PropertyChanged?.Invoke(this, new(nameof(NameError)));
        PropertyChanged?.Invoke(this, new(nameof(CountryError)));
        PropertyChanged?.Invoke(this, new(nameof(ParentError)));
    }
    public void SetImportedName(string name) { Name = name; PropertyChanged?.Invoke(this, new(nameof(Name))); }
    public ContractorWriteRequest Request() => new(Id, Code, Name, CountryCode, IsActive, RowVersion, IsNew ? OperationId : null, ParentId, TaxId, VatEuId, KsefIdWew,
        Regon: Regon, Krs: Krs, Bdo: Bdo, Pesel: string.IsNullOrEmpty(Pesel) ? null : Pesel);
}
