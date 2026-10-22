using System.Security.Claims;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Vatowiec.Core.Domain;
using Vatowiec.Core.Infrastructure.Persistence;
using Vatowiec.Core.Infrastructure.Registries;
using Vatowiec.SharedContracts;

namespace Vatowiec.Api.Security;

public static class VerificationEndpoints
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    public static void MapVerifications(this RouteGroupBuilder companies)
    {
        var group = companies.MapGroup("/{companyId:guid}/contractors/{contractorId:int}/verifications")
            .AddEndpointFilter(CompanyEndpoints.ValidateCompanySource);
        group.MapGet("", List);
        group.MapPost("", Verify).RequireRateLimiting("registries");
        companies.MapGet("/{companyId:guid}/registry-environments", async (Guid companyId, HttpContext http,
            CompanyDatabaseResolver resolver, IWebHostEnvironment host, RegistrySettings settings) =>
        {
            await using var access = await resolver.Resolve(Actor(http), companyId, CompanyPermission.ViewContractors, host.IsDevelopment(), http.RequestAborted);
            return Results.Ok(new RegistryEnvironmentResponse(settings.Mode("MF"), settings.Mode("GUS"), settings.Mode("VIES")));
        }).AddEndpointFilter(CompanyEndpoints.ValidateCompanySource);
    }
    private static Guid Actor(HttpContext http) => Guid.Parse(http.User.FindFirstValue(ClaimTypes.NameIdentifier)!);
    private static IResult Error(int status, string code, string message) => Results.Json(new { code, message }, statusCode: status);
    private static async Task<IResult> List(Guid companyId, int contractorId, HttpContext http, CompanyDatabaseResolver resolver,
        IWebHostEnvironment host, int offset = 0, int limit = 50)
    {
        await using var access = await resolver.Resolve(Actor(http), companyId, CompanyPermission.ViewContractors, host.IsDevelopment(), http.RequestAborted);
        if (offset < 0 || offset > 1_000_000 || limit is < 1 or > 100) return Error(400, "invalid_filter", "Nieprawidłowa strona historii.");
        if (!await access.Context.Contractors.AnyAsync(x => x.Id == contractorId)) return Error(404, "contractor_not_found", "Nie znaleziono kontrahenta.");
        var rows = await access.Context.ExternalVerifications.AsNoTracking().Where(x => x.ContractorId == contractorId)
            .OrderByDescending(x => x.StartedAtUtc).ThenBy(x => x.Id).Skip(offset).Take(limit).ToListAsync(http.RequestAborted);
        return Results.Ok(rows.Select(x => Response(x, companyId)));
    }
    private static async Task<IResult> Verify(Guid companyId, int contractorId, VerificationRequest value, HttpContext http,
        CompanyDatabaseResolver resolver, IWebHostEnvironment host, IAntiforgery csrf, SystemDbContext system,
        IRegistryAdapters adapters, TimeProvider clock, IDataPortClient dataPort, IDataPortKeyProtector protector,
        Vatowiec.Core.Application.TaxIdentifierValidator validator)
    {
        await using var access = await resolver.Resolve(Actor(http), companyId, CompanyPermission.ViewContractors | CompanyPermission.VerifyRegistries,
            host.IsDevelopment(), http.RequestAborted);
        try { await csrf.ValidateRequestAsync(http); }
        catch (AntiforgeryValidationException) { return Error(400, "csrf_invalid", "Nie udało się potwierdzić żądania."); }
        if (value.OperationId == Guid.Empty || value.Operation is not ("DataPort" or "Gus" or "Vies" or "MfTaxpayer" or "MfAccount" or "MfStatus"))
            return Error(400, "verification_invalid", "Nieprawidłowy rodzaj lub identyfikator sprawdzenia.");
        var db = access.Context; var actor = Actor(http);
        var issuer = await system.InstallationIdentities.Select(x => x.Id).SingleAsync(http.RequestAborted);
        var name = http.User.Identity?.Name;
        if (issuer == Guid.Empty || string.IsNullOrWhiteSpace(name)) return Error(503, "audit_identity_unavailable", "Brak trwałej tożsamości wykonawcy.");
        var fingerprint = IdentifierChecks.Hash(JsonSerializer.Serialize(new { companyId, contractorId, value }, Json));
        var previous = await db.ExternalVerifications.AsNoTracking().SingleOrDefaultAsync(x => x.ActorId == actor && x.ActorIssuerId == issuer && x.OperationId == value.OperationId);
        if (previous is not null) return previous.RequestHash == fingerprint ? Results.Ok(Response(previous, companyId)) : Error(409, "operation_payload_mismatch", "Ta operacja dotyczyła innego sprawdzenia.");
        var row = await db.Contractors.AsNoTracking().SingleOrDefaultAsync(x => x.Id == contractorId, http.RequestAborted);
        if (row is null) return Error(404, "contractor_not_found", "Nie znaleziono kontrahenta.");
        if (Convert.ToBase64String(row.RowVersion) != value.RowVersion) return Error(409, "verification_stale", "Dane kontrahenta zmieniły się. Otwórz aktualną wersję przed sprawdzeniem.");
        var date = value.StateDate ?? DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);
        if (date > DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime)) return Error(400, "verification_date", "Nie sprawdzamy przyszłego stanu rejestru.");
        string? nip = null, vat = null, account = null;
        string? dataPortRule = null;
        if (value.Operation == "Vies")
        {
            vat = row.VatEuId;
            var prefixes = Vatowiec.Core.Application.TaxSchemeSeed.Rules().Where(x => x.VatPrefix != null).Select(x => x.VatPrefix!);
            if (vat is null || !Regex.IsMatch(vat, "^[A-Z]{2}[A-Z0-9+*.]{2,12}$") || !prefixes.Contains(vat[..2]))
                return Error(400, "verification_unsupported", "Nie wykonano zapytania: VAT UE nie spełnia wejścia VIES (w tym EL/XI).");
        }
        else if (value.Operation == "DataPort")
        {
            var assessment = validator.Assess("TaxId", row.TaxId, row.CountryCode,
                await system.TaxIdentifierSchemes.AsNoTracking().ToArrayAsync(http.RequestAborted), date);
            if (row.CountryCode != "PL" || assessment.Status != "Valid")
                return Error(400, "verification_unsupported", "Nie wykonano zapytania DataPort: wymagany poprawny polski NIP (format i suma kontrolna).");
            nip = assessment.Normalized;
            dataPortRule = assessment.RuleVersion;
        }
        else
        {
            nip = row.TaxId;
            if (row.CountryCode != "PL" || nip is null || !Regex.IsMatch(nip, "^[0-9]{10}$"))
                return Error(400, "verification_unsupported", "Nie wykonano zapytania: wymagany polski kontrahent i krajowy NIP złożony z 10 cyfr.");
        }
        if (value.Operation == "MfAccount")
        {
            var bank = await db.ContractorBankAccounts.AsNoTracking().SingleOrDefaultAsync(x => x.Id == value.BankAccountId && x.ContractorId == contractorId, http.RequestAborted);
            if (bank is null) return Error(404, "bank_account_not_found", "Wybierz rachunek tego kontrahenta.");
            if (Convert.ToBase64String(bank.RowVersion) != value.BankRowVersion) return Error(409, "verification_stale", "Rachunek zmienił się. Otwórz aktualną wersję.");
            account = bank.AccountNumber;
            if (account.StartsWith("PL", StringComparison.Ordinal)) account = account[2..];
            if (!Regex.IsMatch(account, "^[0-9]{26}$")) return Error(400, "verification_unsupported", "Nie można przedstawić rachunku jako polskiego NRB (26 cyfr). Nie wykonano sprawdzenia, wynik nie jest NIE.");
        }
        else if (value.BankAccountId is not null || value.BankRowVersion is not null)
            return Error(400, "verification_invalid", "Rachunek nie dotyczy tego rodzaju sprawdzenia.");
        var started = clock.GetUtcNow();
        // Nie trzymamy transakcji SQL podczas oczekiwania na rejestr. Dane wejściowe są już snapshotem.
        RegistryReply result;
        if (value.Operation == "DataPort")
        {
            var configuration = await db.DataPortConfigurations.AsNoTracking().SingleOrDefaultAsync(http.RequestAborted);
            if (configuration?.ProtectedApiKey == null)
                result = new("Production", "Unknown", "NotConfigured", "dataport_key_missing", "Wprowadź klucz w ustawieniach firmy → Integracje zewnętrzne → DataPort.", null, new(), "dataport-rest-full-v1", false);
            else
            {
                string? key = null;
                try { key = protector.Unprotect(companyId, configuration.ProtectedApiKey); }
                catch (Exception e) when (e is System.Security.Cryptography.CryptographicException or InvalidOperationException or IOException or UnauthorizedAccessException) { }
                result = key == null
                    ? new("Production", "Unknown", "NotConfigured", "dataport_key_unavailable", "Nie można odczytać chronionego klucza. Wpisz go ponownie w ustawieniach firmy.", null, new(), "dataport-rest-full-v1", false)
                    : await dataPort.Lookup(nip!, key, http.RequestAborted);
            }
        }
        else result = await adapters.Execute(value.Operation, nip, vat, account, date, http.RequestAborted);
        var entry = new ExternalVerification(value.OperationId, row.Id, value.BankAccountId, RegistryAdapters.Source(value.Operation),
            value.Operation, result.Environment, nip, vat, account, value.Operation.StartsWith("Mf", StringComparison.Ordinal) ? date : null,
            started, clock.GetUtcNow(), result.Outcome, result.ServiceStatus, result.ErrorCode, result.Message, result.Reference,
            JsonSerializer.Serialize(result.Data, Json), result.AdapterVersion, dataPortRule ?? ("registry-input-v1/" + (value.Operation == "Vies" ? "vies-wsdl" : "pl-nip-nrb")), result.Executed,
            actor, issuer, name, fingerprint);
        db.ExternalVerifications.Add(entry);
        try { await db.SaveChangesAsync(CancellationToken.None); }
        catch (DbUpdateException error) when (error.InnerException is SqlException { Number: 2601 or 2627 })
        {
            db.ChangeTracker.Clear();
            var concurrent = await db.ExternalVerifications.AsNoTracking().SingleAsync(x => x.ActorId == actor && x.ActorIssuerId == issuer && x.OperationId == value.OperationId);
            return concurrent.RequestHash == fingerprint ? Results.Ok(Response(concurrent, companyId)) : Error(409, "operation_payload_mismatch", "Klucz sprawdzenia został wykorzystany z inną treścią.");
        }
        catch (DbUpdateException) { return Error(503, "verification_history_failed", "Odpowiedź mogła nadejść, ale nie zapisano jej historii. Wynik nie został potwierdzony w VATowcu."); }
        return Results.Ok(Response(entry, companyId));
    }
    private static VerificationResponse Response(ExternalVerification x, Guid company) => new(x.Id, company, x.ContractorId, x.BankAccountId,
        x.Source, x.Operation, x.Environment, x.TaxId, x.VatEuId, x.AccountNumber, x.StateDate, x.StartedAtUtc, x.CompletedAtUtc, x.Outcome,
        x.ServiceStatus, x.ErrorCode, x.Message, x.OfficeReference, JsonSerializer.Deserialize<RegistryData>(x.DataJson, Json) ?? new(), x.AdapterVersion, x.RuleVersion, x.Executed);
}
