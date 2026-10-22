using System.Text.Json;
using Vatowiec.Desktop.Services;
using Vatowiec.SharedContracts;

namespace Vatowiec.Desktop.ViewModels;

public sealed partial class MainViewModel
{
    private IdentifierPreview? identifierPreview;
    private RegistryEnvironmentResponse registryEnvironment = new("Test", "Test", "Test");
    private int verificationOffset;
    private string? pendingVerificationPayload;
    private Guid pendingVerificationId = Guid.NewGuid();
    private VerificationResponse? selectedVerification;
    private string? assessedCountry;
    private bool AssessmentCurrent => identifierPreview is not null && editor is not null && assessedCountry == editor.CountryCode &&
        identifierPreview.Checks.All(x => (x.Normalized ?? "") == (x.Field == "TaxId" ? editor.TaxId : x.Field == "VatEuId" ? editor.VatEuId : editor.KsefIdWew));
    private string Assessment(string field) => AssessmentCurrent ? identifierPreview?.Checks.FirstOrDefault(x => x.Field == field)?.Message ?? "Nie sprawdzono." : "Nie sprawdzono aktualnej wartości. Uruchom kontrolę.";
    private void ObserveIdentifierEdits()
    {
        identifierPreview = null;
        if (editor is not null) editor.PropertyChanged += (_, _) => ChangedAll();
    }
    public string TaxIdAssessment => Assessment("TaxId");
    public string VatEuIdAssessment => Assessment("VatEuId");
    public string KsefIdWewAssessment => Assessment("KsefIdWew");
    public string IdentifierSummary => identifierPreview?.ToString() ?? "";
    public bool HasIdentifierWarning => identifierPreview?.NeedsWarning == true;
    public string RegistryEnvironmentLabel => "Oficjalne MF/GUS/VIES: " + (registryEnvironment.Mf == "Production" && registryEnvironment.Gus == "Production" && registryEnvironment.Vies == "Production"
        ? "ŚRODOWISKO PRODUKCYJNE. " : "ŚRODOWISKO TESTOWE / MOCK — bez potwierdzenia produkcyjnego. ") + registryEnvironment.Label;
    public bool IsVerificationsTab => tab == 4;
    public bool IsDetailTab => tab is >= 1 and <= 3;
    public bool CanVerify => HasSavedContractor && selectedCompany?.Permissions.Contains("VerifyRegistries", StringComparer.Ordinal) == true;
    public DateTimeOffset? VerificationDate { get; set; } = DateTimeOffset.Now.Date;
    public VerificationResponse[] VerificationRows { get; private set; } = [];
    public bool HasPreviousVerifications => verificationOffset > 0;
    public bool HasNextVerifications { get; private set; }
    public string VerificationPage => $"Strona {verificationOffset / 50 + 1} · {VerificationRows.Length} sprawdzeń";
    public VerificationResponse? SelectedVerification
    {
        get => selectedVerification;
        set
        {
            if (busy) return; // Zmiana ItemsSource nie może przywrócić poprzedniego wyniku.
            selectedVerification = value;
            UseGusName = UseGusStreet = UseGusBuilding = UseGusApartment = UseGusPostalCode = UseGusCity = UseGusCountry = false;
            ChangedAll();
        }
    }
    public string VerificationSummary => selectedVerification is not { } x ? "Wybierz zdarzenie historii." :
        $"{x.Environment} — {(x.Environment == "Production" ? x.Executed ? "rzeczywista usługa" : "docelowa rzeczywista usługa; zapytanie niewykonane" : "NIE stanowi potwierdzenia produkcyjnego")}\n" +
        $"{(x.Source == "DataPort" ? "DataPort (dane GUS), pośrednik/cache" : x.Source)}/{x.Operation} · {(x.Executed ? "wykonano zapytanie" : "nie wykonano zapytania do usługi")}\n" +
        $"NIP: {x.TaxId} · VAT UE: {x.VatEuId}\nRachunek: {x.AccountNumber}\nStan na: {x.StateDate:yyyy-MM-dd}\n" +
        $"Zapytanie UTC: {x.StartedAtUtc:u} — {x.CompletedAtUtc:u}\nWynik: {x.Outcome} · Usługa: {x.ServiceStatus}\n{x.Message}\nBłąd: {x.ErrorCode}\nReferencja urzędu: {x.OfficeReference ?? "nie zwrócono"}\n" +
        $"Nazwa: {x.Data.Name}\nAdres: {x.Data.Address} {x.Data.Street} {x.Data.BuildingNo} {x.Data.ApartmentNo} {x.Data.PostalCode} {x.Data.City}\nREGON (podgląd): {x.Data.Regon}\nData zakończenia działalności (podgląd): {x.Data.ActivityEndDate:yyyy-MM-dd}\nWersja adaptera: {x.AdapterVersion}\nReguły wejścia: {x.RuleVersion}";
    public bool CanImportGus => CanEditContractors && selectedVerification is { Source: "GUS" or "DataPort", ServiceStatus: "Completed", Outcome: "Information" };
    public bool UseGusName { get; set; }
    public bool UseGusStreet { get; set; }
    public bool UseGusBuilding { get; set; }
    public bool UseGusApartment { get; set; }
    public bool UseGusPostalCode { get; set; }
    public bool UseGusCity { get; set; }
    public bool UseGusCountry { get; set; }
    public string GusComparison => $"Obecna nazwa: {editor?.Name}\nNazwa ze źródła: {selectedVerification?.Data.Name}\n" +
        "Adresy istniejące pozostaną bez zmian. Wybrane pola pobranego adresu trafią do nowego, niezapisanego szkicu adresu.";
    public Task CheckIdentifiers() => Run(async () =>
    {
        if (api is null || profile is null || editor is null || !CanEditContractors) return;
        var errors = ContractorValidation.Validate(editor.Request()); editor.SetErrors(errors);
        if (errors.Count != 0) throw new ArgumentException("Uzupełnij wymagane pola formularza.");
        identifierPreview = await api.ValidateIdentifiers(profile.CompanyId, editor.Request());
        ApplyIdentifierPreview(identifierPreview); message = "Pokazano znormalizowane wartości i zakres kontroli. Nie wykonano zapytania do rejestru.";
    });
    private bool ApplyIdentifierPreview(IdentifierPreview preview)
    {
        if (editor is null) return false;
        assessedCountry = editor.CountryCode;
        var changed = false;
        foreach (var check in preview.Checks)
        {
            var normalized = check.Normalized ?? "";
            if (check.Field == "TaxId") { changed |= editor.TaxId != normalized; editor.TaxId = normalized; }
            if (check.Field == "VatEuId") { changed |= editor.VatEuId != normalized; editor.VatEuId = normalized; }
            if (check.Field == "KsefIdWew") { changed |= editor.KsefIdWew != normalized; editor.KsefIdWew = normalized; }
        }
        return changed;
    }
    public Task LoadVerifications() => Run(() => LoadVerificationPage(0));
    public Task NextVerifications() => Run(() => LoadVerificationPage(verificationOffset + 50));
    public Task PreviousVerifications() => Run(() => LoadVerificationPage(Math.Max(0, verificationOffset - 50)));
    private async Task LoadVerificationPage(int offset)
    {
        if (api is null || profile is null || !HasSavedContractor) return;
        registryEnvironment = await api.RegistryEnvironments(profile.CompanyId);
        var rows = await api.Verifications(profile.CompanyId, editor!.Id, offset);
        foreach (var row in rows) CheckVerification(row);
        VerificationRows = rows.Take(50).ToArray(); HasNextVerifications = rows.Length > 50; verificationOffset = offset;
        selectedVerification = VerificationRows.SingleOrDefault(x => x.Id == selectedVerification?.Id);
        Changed(nameof(VerificationRows));
        Changed(nameof(SelectedVerification));
    }
    private void CheckVerification(VerificationResponse row)
    {
        if (profile is null || editor is null || row.CompanyId != profile.CompanyId || row.ContractorId != editor.Id)
            throw new ApiException(502, "Odpowiedź historii dotyczy innej firmy lub kontrahenta.");
    }
    public Task VerifyRegistry(string operation) => Run(async () =>
    {
        if (!CanVerify || api is null || profile is null || editor is null) return;
        if (editor.IsDirty || detailEditor?.IsDirty == true) throw new ArgumentException("Najpierw zapisz lub porzuć zmiany. Rejestr sprawdza zapisaną wersję, nie szkic.");
        var bank = operation == "MfAccount" ? SelectedDetail : null;
        if (operation == "MfAccount" && bank?.Kind != "bank-accounts") throw new ArgumentException("Zaznacz zapisany rachunek do sprawdzenia.");
        if (operation.StartsWith("Mf", StringComparison.Ordinal) && VerificationDate is null) throw new ArgumentException("Wybierz datę stanu rejestru.");
        var value = new VerificationRequest(Guid.Empty, operation, editor.RowVersion!, bank?.Id, bank?.RowVersion,
            operation.StartsWith("Mf", StringComparison.Ordinal) ? DateOnly.FromDateTime(VerificationDate!.Value.Date) : null);
        var payload = JsonSerializer.Serialize(value);
        if (pendingVerificationPayload != payload) { pendingVerificationId = Guid.NewGuid(); pendingVerificationPayload = payload; }
        var result = await api.Verify(profile.CompanyId, editor.Id, value with { OperationId = pendingVerificationId });
        CheckVerification(result); pendingVerificationPayload = null;
        // Przełączenie bez uruchamiania drugiego Run w trakcie aktywnego żądania.
        tab = 4; await LoadVerificationPage(0);
        selectedVerification = VerificationRows.SingleOrDefault(x => x.Id == result.Id) ?? result;
        UseGusName = UseGusStreet = UseGusBuilding = UseGusApartment = UseGusPostalCode = UseGusCity = UseGusCountry = false;
        ChangedAll();
        message = $"{result.Environment}: {result.Message}";
    });
    public void ApplyGusSelection()
    {
        if (busy || !CanImportGus || editor is null || selectedVerification is null) return;
        var data = selectedVerification.Data;
        if (UseGusName && data.Name is not null) editor.SetImportedName(data.Name);
        if (UseGusStreet || UseGusBuilding || UseGusApartment || UseGusPostalCode || UseGusCity || UseGusCountry)
        {
            tab = 1; detailEditor = new DetailEditor("addresses")
            {
                Street = UseGusStreet ? data.Street : null,
                BuildingNo = UseGusBuilding ? data.BuildingNo : null,
                ApartmentNo = UseGusApartment ? data.ApartmentNo : null,
                PostalCode = UseGusPostalCode ? data.PostalCode : null,
                City = UseGusCity ? data.City : null,
                CountryCode = UseGusCountry ? data.CountryCode : null
            };
        }
        else tab = 0;
        message = "Przeniesiono wybrane wartości do szkicu. Niczego jeszcze nie zapisano. Sprawdź i użyj zwykłego zapisu.";
        ChangedAll();
    }
    private void ResetVerifications()
    {
        identifierPreview = null; VerificationRows = []; selectedVerification = null; verificationOffset = 0;
        HasNextVerifications = false; pendingVerificationPayload = null; pendingVerificationId = Guid.NewGuid();
        UseGusName = UseGusStreet = UseGusBuilding = UseGusApartment = UseGusPostalCode = UseGusCity = UseGusCountry = false;
        registryEnvironment = new("Test", "Test", "Test");
    }
}
