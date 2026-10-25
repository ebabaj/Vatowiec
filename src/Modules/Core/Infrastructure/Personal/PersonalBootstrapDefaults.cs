using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Vatowiec.Core.Domain;
using Vatowiec.Core.Infrastructure.Identity;
using Vatowiec.Core.Infrastructure.Persistence;
using Vatowiec.Core.Infrastructure.Registries;

namespace Vatowiec.Core.Infrastructure.Personal;

// Explicit provisioning, never an EF seed/interceptor or a normal-startup operation.
public static class PersonalBootstrapDefaults
{
    public static PasswordPolicy PasswordPolicy()
    {
        var policy = new PasswordPolicy();
        policy.Update(false, 1, false, false, false, false);
        return policy;
    }

    // Caller holds the bootstrap gate and UserAdministration lock, before the first user/seal.
    // Policy, registries, their audits and the administrator commit in the SAME System transaction.
    public static async Task InitializeSystem(SystemDbContext system, Guid installationId, Guid actor, Guid issuer, string actorName)
    {
        new PersonalDatabaseTarget(installationId).Validate(system.Database.GetConnectionString(), system: true);
        if (system.Database.CurrentTransaction == null || await system.Users.AnyAsync())
            throw new PersonalSetupException("Inicjalizacja ustawień wymaga nowej, niezamkniętej instalacji Personal.");
        await using var check = system.Database.GetDbConnection().CreateCommand();
        check.Transaction = system.Database.CurrentTransaction.GetDbTransaction();
        check.CommandText = "SELECT CONVERT(nvarchar(128),value) FROM sys.extended_properties WHERE class=0 AND name=N'Vatowiec.Personal.Owner' AND SERVERPROPERTY('IsLocalDB')=1 AND SERVERPROPERTY('ProductMajorVersion')=N'17'";
        if (!Equals(await check.ExecuteScalarAsync(), installationId.ToString("N"))) throw new PersonalSetupException("Nie potwierdzono własności bazy Personal.");
        check.CommandText = "SELECT COUNT(*) FROM sys.extended_properties WHERE class=0 AND name=N'Vatowiec.Personal.Initialized'";
        if (!Equals(await check.ExecuteScalarAsync(), 0)) throw new PersonalSetupException("Inicjalizacja instalacji jest już zamknięta.");
        var policy = await system.PasswordPolicies.SingleAsync();
        static string PolicySnapshot(PasswordPolicy p) => JsonSerializer.Serialize(UserAdministrationService.PolicyResponse(p, false));
        if (!await system.PasswordPolicyChanges.AnyAsync() && PolicySnapshot(policy) == PolicySnapshot(new PasswordPolicy()))
        {
            var before = PolicySnapshot(policy);
            policy.Update(false, 1, false, false, false, false);
            system.PasswordPolicyChanges.Add(new(actor, issuer, actorName, DateTimeOffset.UtcNow, before, PolicySnapshot(policy)));
        }
        foreach (var row in await system.RegistryConfigurations.Where(x => x.Source == "MF" || x.Source == "VIES").ToArrayAsync())
        {
            // Preserve any individually configured interrupted installation as well.
            if (row.Environment != "Test" || row.TestUrl != RegistrySettings.OfficialUrl(row.Source, "Test") ||
                row.ProductionUrl != RegistrySettings.OfficialUrl(row.Source, "Production") ||
                row.TestWl2Url != (row.Source == "MF" ? RegistrySettings.OfficialUrl("MF", "Test", true) : null) ||
                row.ProductionWl2Url != (row.Source == "MF" ? RegistrySettings.OfficialUrl("MF", "Production", true) : null) ||
                await system.RegistryConfigurationChanges.AnyAsync(x => x.Source == row.Source)) continue;
            var before = RegistrySnapshot(row);
            var production = RegistrySettings.OfficialUrl(row.Source, "Production");
            var wl2 = row.Source == "MF" ? RegistrySettings.OfficialUrl("MF", "Production", true) : null;
            if (!RegistrySettings.IsAllowedUrl(row.Source, "Production", production) ||
                (wl2 != null && !RegistrySettings.IsAllowedUrl("MF", "Production", wl2, true)))
                throw new PersonalSetupException("Nie potwierdzono oficjalnych adresów rejestrów. Nie zapisano ustawień.");
            row.Update("Production", row.TestUrl, production, row.TestWl2Url, wl2);
            system.RegistryConfigurationChanges.Add(new(row.Source, actor, issuer, actorName, DateTimeOffset.UtcNow, before, RegistrySnapshot(row)));
        }
        await system.SaveChangesAsync(); // Identity reads these uncommitted values through the same scoped context.
    }

    private static string RegistrySnapshot(RegistryConfiguration row) => JsonSerializer.Serialize(new
    { row.Source, row.Environment, row.TestUrl, row.ProductionUrl, row.TestWl2Url, row.ProductionWl2Url });

    // Shared by provisioning of ANY new Personal company, not tied to the first company name/ID.
    // An SQL checkpoint commits with the profile, currency settings and immutable audit.
    public static async Task InitializeCompany(CompanyDbContext company, Guid installationId, Guid issuer, string name, string? nip)
    {
        var target = new PersonalDatabaseTarget(installationId);
        var connection = new SqlConnectionStringBuilder(company.Database.GetConnectionString());
        var companyId = company.CurrentCompanyId ?? throw new PersonalSetupException("Brak kontekstu nowej firmy.");
        var additional = "Vatowiec_Personal_" + installationId.ToString("N") + "_Company_" + companyId.ToString("N");
        if (!string.Equals(connection.DataSource, target.Server, StringComparison.OrdinalIgnoreCase) ||
            (connection.InitialCatalog != target.Company && connection.InitialCatalog != additional) || !connection.IntegratedSecurity)
            throw new PersonalSetupException("Ustawienia początkowe dotyczą wyłącznie nowej bazy firmy Personal.");
        await using var tx = await company.Database.BeginTransactionAsync();
        await company.Database.ExecuteSqlRawAsync("DECLARE @r int; EXEC @r=sys.sp_getapplock @Resource=N'PersonalCompanyDefaults', @LockMode='Exclusive', @LockOwner='Transaction', @LockTimeout=10000; IF @r<0 THROW 51000, 'Provisioning lock unavailable', 1;");
        await using var check = company.Database.GetDbConnection().CreateCommand();
        check.Transaction = tx.GetDbTransaction();
        check.CommandText = "SELECT CONVERT(nvarchar(128),value) FROM sys.extended_properties WHERE class=0 AND name=N'Vatowiec.Personal.Owner' AND SERVERPROPERTY('IsLocalDB')=1 AND SERVERPROPERTY('ProductMajorVersion')=N'17'";
        if (!Equals(await check.ExecuteScalarAsync(), installationId.ToString("N")))
            throw new PersonalSetupException("Nie potwierdzono własności nowej bazy Personal. Dane pozostają bez zmian.");
        var profile = await company.CompanyProfiles.IgnoreQueryFilters().SingleOrDefaultAsync();
        if (profile != null && (profile.CompanyId != companyId || profile.Name != name || profile.Nip != nip))
            throw new PersonalSetupException("Profil firmy jest niezgodny. Dane nie zostały zastąpione.");
        check.CommandText = "SELECT COUNT(*) FROM sys.extended_properties WHERE class=0 AND name=N'Vatowiec.Personal.CompanyDefaults'";
        if (Equals(await check.ExecuteScalarAsync(), 1))
        {
            if (profile == null) throw new PersonalSetupException("Brak profilu wcześniej przygotowanej firmy. Nie zastąpiono danych.");
            await tx.CommitAsync(); return;
        }
        var currencies = await company.Currencies.IgnoreQueryFilters().OrderBy(x => x.Code).ToArrayAsync();
        var seed = CurrencySeed.Rows().OrderBy(x => x.Code).ToArray();
        if (await company.Contractors.IgnoreQueryFilters().AnyAsync() || await company.ExchangeRates.IgnoreQueryFilters().AnyAsync() ||
            await company.CurrencyOperations.IgnoreQueryFilters().AnyAsync() || currencies.Length != seed.Length ||
            currencies.Where((row, i) => row.Code != seed[i].Code || row.Name != seed[i].Name || row.IsActive != seed[i].IsActive ||
                row.DownloadA != seed[i].DownloadA || row.DownloadB != seed[i].DownloadB || row.DownloadC != seed[i].DownloadC).Any())
            throw new PersonalSetupException("Baza ma już indywidualne dane lub ustawienia walut. Nie zastąpiono ich wartościami początkowymi.");
        if (profile == null) company.CompanyProfiles.Add(new(companyId, name, nip));
        foreach (var row in currencies)
        { row.IsActive = row.Code == "PLN"; row.DownloadA = row.DownloadB = row.DownloadC = false; }
        var details = JsonSerializer.Serialize(new { Operation = "PersonalCompanyInitialized", Active = new[] { "PLN" }, ForeignInactive = currencies.Length - 1, DownloadA = false, DownloadB = false, DownloadC = false });
        company.CurrencyOperations.Add(new() { OperationId = Guid.NewGuid(), Kind = "PersonalInitialized", ActorId = installationId,
            ActorIssuerId = issuer, ActorName = "Inicjalizacja VATowiec Personal", OccurredAtUtc = DateTimeOffset.UtcNow,
            RequestHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(details))), DetailsJson = details });
        await company.SaveChangesAsync();
        check.CommandText = "EXEC sys.sp_addextendedproperty @name=N'Vatowiec.Personal.CompanyDefaults',@value=N'1'";
        await check.ExecuteNonQueryAsync();
        await tx.CommitAsync();
    }
}
