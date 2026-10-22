using System.Text.Json;
using Vatowiec.Desktop.Services;
using Vatowiec.SharedContracts;

namespace Vatowiec.Desktop.ViewModels;

public sealed partial class MainViewModel
{
    private int tab;
    private DetailEditor? detailEditor;
    private DetailResponse? detailConflict;
    private string? warningHash;
    private string warningMessage = "";
    private Action? discardAction;
    private int detailOffset;
    private int parentOffset;
    private int childOffset;
    private Guid typeOperation = Guid.NewGuid();
    public int SelectedTab
    {
        get => tab;
        set
        {
            if (value == tab || busy) return;
            void Switch()
            {
                tab = value;
                ClearDetailDraft();
                NewAddressTypeName = "";
                typeOperation = Guid.NewGuid();
                DetailRows = [];
                detailOffset = 0;
                ChangedAll();
                if (tab > 0 && HasSavedContractor) _ = LoadDetails();
            }
            if (detailEditor?.IsDirty == true || !string.IsNullOrEmpty(NewAddressTypeName)) RequestDiscard(Switch);
            else Switch();
        }
    }
    public bool IsMainTab => tab == 0;
    public bool IsAddressesTab => tab == 1;
    public bool IsContactsTab => tab == 2;
    public bool IsBankTab => tab == 3;
    public bool IsPostalDetail => tab is 1 or 2;
    public int DetailNameMaxLength => tab == 2 ? 256 : 100;
    public string DetailKind => tab switch { 1 => "addresses", 2 => "contacts", _ => "bank-accounts" };
    public string DetailTitle => tab switch { 1 => "Adresy", 2 => "Kontakty", _ => "Rachunki bankowe" };
    public bool HasSavedContractor => editor?.Id > 0;
    public string CurrentContractor => editor is null ? "" : $"{editor.Code} — {editor.Name} · {(editor.Id == 0 ? "nowa pozycja" : "Id " + editor.Id)}";
    public DetailResponse[] DetailRows { get; private set; } = [];
    public DetailResponse? SelectedDetail { get; set; }
    public DetailEditor? DetailEditor => detailEditor;
    public bool IsDetailEditing => detailEditor is not null;
    public bool CanEditDetails => CanEditContractors && HasSavedContractor;
    public bool CanSaveDetail => CanEditDetails && IsDetailEditing && !HasDetailConflict && !HasBankWarning;
    public bool HasDetailConflict => detailConflict is not null;
    public string DetailServerSummary => detailConflict is null ? "" : new DetailEditor(detailConflict).Summary();
    public string DetailLocalSummary => detailEditor?.Summary() ?? "";
    public bool HasBankWarning => warningHash is not null;
    public string BankWarning => warningMessage;
    public bool CanAcceptWarnings => selectedCompany?.Permissions.Contains("AcceptWarnings", StringComparer.Ordinal) == true;
    public bool DiscardPrompt => discardAction is not null;
    public bool CanUseForm => !busy && !DiscardPrompt;
    public bool HasUnsavedChanges => editor?.IsDirty == true || detailEditor?.IsDirty == true || !string.IsNullOrEmpty(NewAddressTypeName);
    public bool HasNextDetails { get; private set; }
    public bool HasPreviousDetails => detailOffset > 0;
    public string DetailPage => $"Strona {detailOffset / 50 + 1} · {DetailRows.Length} pozycji";
    public AddressTypeResponse[] AddressTypes { get; private set; } = [];
    public AddressTypeResponse? SelectedAddressType
    {
        get => AddressTypes.SingleOrDefault(x => x.Id == detailEditor?.AddressTypeId);
        set { if (detailEditor is not null && value is not null) detailEditor.AddressTypeId = value.Id; ChangedAll(); }
    }
    public string NewAddressTypeName { get; set; } = "";
    public string ParentSearch { get; set; } = "";
    public ContractorResponse[] ParentCandidates { get; private set; } = [];
    public ContractorResponse? SelectedParentCandidate { get; set; }
    public string ParentLabel { get; private set; } = "Brak powiązania nadrzędnego";
    public bool HasNextParents { get; private set; }
    public bool HasPreviousParents => parentOffset > 0;
    public ContractorResponse[] ChildContractors { get; private set; } = [];
    public ContractorResponse? SelectedChild { get; set; }
    public bool HasNextChildren { get; private set; }
    public bool HasPreviousChildren => childOffset > 0;

    public void RequestDiscard(Action action) { discardAction = action; ChangedAll(); }
    public void ConfirmDiscard() { var action = discardAction; discardAction = null; action?.Invoke(); ChangedAll(); }
    public void KeepEditing() { discardAction = null; ChangedAll(); }
    public Task LoadDetails() => Run(() => LoadDetailPage(0));
    public Task NextDetails() => Run(() => LoadDetailPage(detailOffset + 50));
    public Task PreviousDetails() => Run(() => LoadDetailPage(Math.Max(0, detailOffset - 50)));
    private async Task LoadDetailPage(int offset)
    {
        if (api is null || profile is null || editor?.Id is not > 0) return;
        var rows = await api.Details(profile.CompanyId, editor.Id, DetailKind, offset, 51);
        foreach (var row in rows) CheckDetail(row);
        DetailRows = rows.Take(50).Select(x => x.Kind == "addresses"
            ? x with { AddressTypeName = AddressTypes.SingleOrDefault(t => t.Id == x.Data.AddressTypeId)?.Name } : x).ToArray();
        HasNextDetails = rows.Length > 50;
        detailOffset = offset;
        SelectedDetail = null;
    }
    private async Task LoadFormContext()
    {
        ResetDetails();
        if (api is null || profile is null || editor is null) return;
        AddressTypes = await api.AddressTypes(profile.CompanyId);
        if (editor.ParentId.HasValue)
        {
            var parent = await api.Contractor(profile.CompanyId, editor.ParentId.Value);
            CheckContractorCompany(parent);
            ParentLabel = parent.ToString();
        }
        await LoadChildPage(0);
    }
    public Task SearchParents() => Run(() => LoadParents(0));
    public Task NextParents() => Run(() => LoadParents(parentOffset + 50));
    public Task PreviousParents() => Run(() => LoadParents(Math.Max(0, parentOffset - 50)));
    private async Task LoadParents(int offset)
    {
        if (api is null || profile is null || editor is null) return;
        var rows = await api.ParentCandidates(profile.CompanyId, ParentSearch.Trim(), editor.Id, offset);
        foreach (var row in rows) CheckContractorCompany(row);
        ParentCandidates = rows.Where(x => x.Id != editor.Id).Take(50).ToArray();
        HasNextParents = rows.Length > 50;
        parentOffset = offset;
    }
    public void ChooseParent()
    {
        if (busy || !CanEditContractors || editor is null || SelectedParentCandidate is null) return;
        editor.ParentId = SelectedParentCandidate.Id;
        ParentLabel = SelectedParentCandidate.ToString();
        ParentCandidates = [];
        SelectedParentCandidate = null;
        ChangedAll();
    }
    public void ClearParent()
    {
        if (busy || !CanEditContractors || editor is null) return;
        editor.ParentId = null;
        ParentLabel = "Brak powiązania nadrzędnego";
        ChangedAll();
    }
    public Task NextChildren() => Run(() => LoadChildPage(childOffset + 50));
    public Task PreviousChildren() => Run(() => LoadChildPage(Math.Max(0, childOffset - 50)));
    private async Task LoadChildPage(int offset)
    {
        if (api is null || profile is null || editor?.Id is not > 0) return;
        var rows = await api.Children(profile.CompanyId, editor.Id, offset);
        foreach (var row in rows) CheckContractorCompany(row);
        ChildContractors = rows.Take(50).ToArray();
        HasNextChildren = rows.Length > 50;
        childOffset = offset;
    }
    public void OpenSelectedChild()
    {
        if (SelectedChild is null || busy) return;
        var child = SelectedChild;
        void Open() { selectedContractor = child; _ = EditContractor(); }
        if (HasUnsavedChanges) RequestDiscard(Open); else Open();
    }
    public void NewDetail()
    {
        if (busy || !CanEditDetails) return;
        void Open() { ClearDetailDraft(); detailEditor = new DetailEditor(DetailKind); ChangedAll(); }
        if (detailEditor?.IsDirty == true) RequestDiscard(Open); else Open();
    }
    public Task EditDetail() => Run(async () =>
    {
        if (api is null || profile is null || editor is null || SelectedDetail is null) return;
        if (detailEditor?.IsDirty == true) { RequestDiscard(() => { ClearDetailDraft(); _ = EditDetail(); }); return; }
        var row = await api.Detail(profile.CompanyId, editor.Id, DetailKind, SelectedDetail.Id);
        CheckDetail(row);
        ClearDetailDraft();
        detailEditor = new DetailEditor(row);
    });
    public Task SaveDetail(bool acceptWarning = false) => Run(async () =>
    {
        if (api is null || profile is null || editor is null || detailEditor is null || !CanEditDetails || HasDetailConflict) return;
        detailEditor.SetErrors(DetailValidation.Validate(DetailKind, detailEditor.Values()));
        if (detailEditor.HasErrors) { message = "Popraw pola zaznaczone w formularzu."; return; }
        var request = detailEditor.Request() with { AcceptWarning = acceptWarning, WarningHash = acceptWarning ? warningHash : null };
        try
        {
            var saved = await api.SaveDetail(profile.CompanyId, editor.Id, DetailKind, request, detailEditor.Id == 0);
            CheckDetail(saved);
            ClearDetailDraft();
            await LoadDetailPage(detailOffset);
            message = "Zapisano pozycję i audyt zmian.";
        }
        catch (ApiException ex) when (ex.Code == "bank_warning" && ex.WarningHash is not null)
        { warningHash = ex.WarningHash; warningMessage = ex.Message; }
        catch (ApiException ex) when (ex.Code == "detail_conflict" && ex.DetailCurrent is not null)
        {
            CheckDetail(ex.DetailCurrent);
            if (ex.DetailCurrent.Id != detailEditor.Id) throw new ApiException(502, "Niezgodna pozycja w odpowiedzi konfliktu.");
            detailConflict = ex.DetailCurrent;
            warningHash = null;
            message = "Konflikt: niczego nie nadpisano. Porównaj wersje.";
        }
        catch (ApiException ex) when (ex.Code == "validation_failed" && ex.Errors is not null)
        { detailEditor.SetErrors(ex.Errors); message = "Popraw zaznaczone pola."; }
    });
    public void CancelBankWarning() { warningHash = null; warningMessage = ""; ChangedAll(); }
    public void ResolveDetailConflict(bool keepLocal)
    {
        if (busy || detailConflict is null || detailEditor is null) return;
        if (keepLocal) detailEditor.RowVersion = detailConflict.RowVersion;
        else detailEditor = new DetailEditor(detailConflict);
        detailConflict = null;
        message = keepLocal ? "Sprawdź własne pola i ponownie zapisz, aby zastąpić porównaną wersję." : "Wczytano wersję serwera.";
        ChangedAll();
    }
    public void CancelDetail()
    {
        if (busy) return;
        if (detailEditor?.IsDirty == true) RequestDiscard(() => { ClearDetailDraft(); ChangedAll(); });
        else { ClearDetailDraft(); ChangedAll(); }
    }
    public Task AddAddressType() => Run(async () =>
    {
        if (api is null || profile is null || !CanEditDetails) return;
        if (string.IsNullOrWhiteSpace(NewAddressTypeName) || NewAddressTypeName.Trim().Length > 100)
            throw new ArgumentException("Wpisz nazwę rodzaju adresu, najwyżej 100 znaków.");
        var added = await api.AddAddressType(profile.CompanyId, new AddressTypeWriteRequest(typeOperation, NewAddressTypeName.Trim()));
        AddressTypes = await api.AddressTypes(profile.CompanyId);
        if (detailEditor is not null) detailEditor.AddressTypeId = added.Id;
        NewAddressTypeName = "";
        typeOperation = Guid.NewGuid();
        message = "Dodano rodzaj adresu.";
    });
    private void CheckDetail(DetailResponse row)
    {
        if (profile is null || editor is null || row.CompanyId != profile.CompanyId || row.ContractorId != editor.Id || row.Kind != DetailKind)
            throw new ApiException(502, "API zwróciło dane innego kontrahenta lub firmy. Odmowa wyświetlenia.");
    }
    private void ClearDetailDraft() { detailEditor = null; detailConflict = null; warningHash = null; warningMessage = ""; }
    private void ResetDetails()
    {
        tab = 0;
        ClearDetailDraft();
        NewAddressTypeName = "";
        typeOperation = Guid.NewGuid();
        DetailRows = [];
        SelectedDetail = null;
        AddressTypes = [];
        ParentCandidates = [];
        ChildContractors = [];
        ParentLabel = "Brak powiązania nadrzędnego";
        ParentSearch = "";
        SelectedParentCandidate = null;
        SelectedChild = null;
        discardAction = null;
        detailOffset = parentOffset = childOffset = 0;
        HasNextDetails = HasNextParents = HasNextChildren = false;
    }
}

public sealed class DetailEditor : System.ComponentModel.INotifyPropertyChanged
{
    public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;
    public DetailEditor(string kind) { Kind = kind; original = JsonSerializer.Serialize(Values()); }
    public DetailEditor(DetailResponse row)
    {
        Id = row.Id;
        Kind = row.Kind;
        RowVersion = row.RowVersion;
        foreach (var property in typeof(DetailValues).GetProperties()) GetType().GetProperty(property.Name)!.SetValue(this, property.GetValue(row.Data));
        original = JsonSerializer.Serialize(Values());
    }
    private readonly string original;
    public string Kind { get; }
    public int Id { get; }
    public Guid OperationId { get; } = Guid.NewGuid();
    public string? RowVersion { get; set; }
    public int AddressTypeId { get; set; } = 1;
    public string? Name { get; set; }
    public string? Street { get; set; }
    public string? BuildingNo { get; set; }
    public string? ApartmentNo { get; set; }
    public string? PostalCode { get; set; }
    public string? City { get; set; }
    public string? Region { get; set; }
    public string? CountryCode { get; set; }
    public bool IsDefault { get; set; }
    public bool IsActive { get; set; } = true;
    public string? Position { get; set; }
    public string? Email { get; set; }
    public string? Phone { get; set; }
    public string? Notes { get; set; }
    public string? AccountNumber { get; set; }
    public string? BankName { get; set; }
    public string? Bic { get; set; }
    public string? CurrencyCode { get; set; }
    public string? AccountHolderName { get; set; }
    public Dictionary<string, string> Errors { get; private set; } = typeof(DetailValues).GetProperties().ToDictionary(x => x.Name, _ => "");
    public bool HasErrors => Errors.Values.Any(x => x.Length != 0);
    public void SetErrors(Dictionary<string, string[]> errors)
    {
        Errors = Errors.Keys.ToDictionary(key => key, key => errors.TryGetValue(key, out var messages) ? string.Join(" ", messages) : "");
        PropertyChanged?.Invoke(this, new(nameof(Errors)));
    }
    public bool IsDirty => original != JsonSerializer.Serialize(Values());
    public DetailValues Values()
    {
        var values = new DetailValues();
        foreach (var property in typeof(DetailValues).GetProperties()) property.SetValue(values, GetType().GetProperty(property.Name)!.GetValue(this));
        return values;
    }
    public DetailWriteRequest Request() => new(Id, Values(), RowVersion, Id == 0 ? OperationId : null);
    public string Summary() => $"Nazwa: {Name}\nAdres: {Street} {BuildingNo}/{ApartmentNo}, {PostalCode} {City}, {Region} {CountryCode}\nRodzaj: {AddressTypeId}; preferowany: {IsDefault}; aktywny: {IsActive}\nKontakt: {Position} · {Email} · {Phone}\nRachunek: {AccountNumber} · {BankName} · {Bic} · {CurrencyCode ?? "bez waluty"} · {AccountHolderName}\nUwagi: {Notes}";
}
