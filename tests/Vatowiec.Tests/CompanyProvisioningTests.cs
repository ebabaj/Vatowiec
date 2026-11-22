using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Vatowiec.Core.Domain;
using Vatowiec.Core.Infrastructure.Persistence;
using Vatowiec.Desktop.Services;
using Vatowiec.Desktop.ViewModels;
using Vatowiec.SharedContracts;

namespace Vatowiec.Tests;

// ADR-044 krok 1: tworzenie firmy — testy bez SQL Server.
public sealed class CompanyProvisioningTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 22, 12, 0, 0, TimeSpan.Zero);

    internal static CompanyOperation NewOperation(Guid? companyId = null)
    {
        var company = companyId ?? Guid.NewGuid();
        return CompanyOperation.Provision(Guid.NewGuid(), Guid.NewGuid(), company, "Firma fikcyjna", Guid.NewGuid(), "admin.test",
            Guid.NewGuid(), new string('A', 64), @"localhost\SQLEXPRESS", "LocalWindows", "Vatowiec_Firma_fikcyjna");
    }

    [Fact]
    public void Operation_AdvancesOnlyForwardWhileLeased_AndCompletesAtReady()
    {
        var operation = NewOperation();
        Assert.Equal((CompanyOperationStages.Requested, CompanyOperationStatuses.Pending), (operation.Stage, operation.Status));
        Assert.Throws<InvalidOperationException>(() => operation.Reach(CompanyOperationStages.DatabaseCreated, Now, TimeSpan.FromMinutes(5)));
        operation.Claim("worker-1", Now, TimeSpan.FromMinutes(5));
        Assert.Equal((CompanyOperationStatuses.Running, 1, "worker-1"), (operation.Status, operation.Attempt, operation.LeaseOwner));
        Assert.False(operation.IsClaimable(Now.AddMinutes(4)));
        Assert.True(operation.IsClaimable(Now.AddMinutes(6))); // Dzierżawa przerwanego wykonawcy wygasa.
        Assert.Throws<InvalidOperationException>(() => operation.Reach(CompanyOperationStages.SchemaApplied, Now, TimeSpan.FromMinutes(5)));
        foreach (var stage in CompanyOperationStages.Provisioning.Skip(1)) operation.Reach(stage, Now, TimeSpan.FromMinutes(5));
        operation.Reach(CompanyOperationStages.Ready, Now, TimeSpan.FromMinutes(5)); // Wznowienie potwierdza bieżący etap.
        Assert.Throws<InvalidOperationException>(() => operation.Reach(CompanyOperationStages.SchemaApplied, Now, TimeSpan.FromMinutes(5)));
        operation.Complete(Now);
        Assert.Equal(CompanyOperationStatuses.Completed, operation.Status);
        Assert.Null(operation.LeaseOwner); Assert.Equal(Now, operation.CompletedAtUtc);
        Assert.False(operation.CanRetry); Assert.False(operation.IsClaimable(Now.AddDays(1)));
    }

    [Fact]
    public void TransientFailures_BackOffThenStopForManualRetry_ReconciliationNeverRetriesAutomatically()
    {
        var operation = NewOperation();
        var at = Now;
        for (var attempt = 1; attempt < CompanyOperation.MaxAutomaticAttempts; attempt++)
        {
            operation.Claim("worker", at, TimeSpan.FromMinutes(5));
            operation.Fail("sql_4060", at);
            Assert.Equal(CompanyOperationStatuses.Pending, operation.Status);
            Assert.Equal(at + TimeSpan.FromMinutes(Math.Min(15, attempt * 2)), operation.NextAttemptAtUtc);
            Assert.False(operation.IsClaimable(at)); Assert.True(operation.IsClaimable(operation.NextAttemptAtUtc!.Value));
            at = operation.NextAttemptAtUtc!.Value;
        }
        operation.Claim("worker", at, TimeSpan.FromMinutes(5));
        operation.Fail("sql_4060", at);
        Assert.Equal(CompanyOperationStatuses.Failed, operation.Status);
        Assert.False(operation.IsClaimable(Now.AddDays(10)));
        Assert.True(operation.CanRetry);
        operation.Retry(Now.AddDays(1));
        Assert.Equal((CompanyOperationStatuses.Pending, 0), (operation.Status, operation.Attempt));

        operation.Claim("worker", Now.AddDays(1), TimeSpan.FromMinutes(5));
        operation.RequireReconciliation("database_not_owned");
        Assert.Equal(CompanyOperationStatuses.NeedsReconciliation, operation.Status);
        Assert.False(operation.IsClaimable(Now.AddYears(1)));
        Assert.Throws<InvalidOperationException>(() => NewOperation().Retry(Now));
    }

    [Theory]
    [InlineData("Tenjin", "Vatowiec_Tenjin")]
    [InlineData("  DOMAGAŁA sp. z o.o.  ", "Vatowiec_DOMAGALA_sp_z_o_o")]
    [InlineData("Zażółć gęślą jaźń", "Vatowiec_Zazolc_gesla_jazn")]
    [InlineData("ŁÓDŹ & Ś-ka \"Ćma\" (2026)", "Vatowiec_LODZ_S_ka_Cma_2026")]
    [InlineData("Müller Straße", "Vatowiec_Muller_Strasse")]
    [InlineData("!!! ---", null)]
    [InlineData("   ", null)]
    public void DatabaseName_IsCleanedCompanyNameWithFixedPrefix(string companyName, string? expected) =>
        Assert.Equal(expected, CompanyDatabaseNames.FromCompanyName(companyName));

    [Fact]
    public void DatabaseNames_AreBoundedPatternChecked_ReservedNamesAreTaken_AndPolicyTrustsOnlyThem()
    {
        var longName = CompanyDatabaseNames.FromCompanyName(new string('a', 99) + " b")!;
        Assert.Equal(CompanyDatabaseNames.Prefix.Length + CompanyDatabaseNames.MaxNameLength, longName.Length + 1); // Bez końcowego _.
        Assert.True(CompanyDatabaseNames.IsCompanyDatabase(longName));
        Assert.True(CompanyDatabaseNames.IsCompanyDatabase("Vatowiec_Tenjin"));
        Assert.True(CompanyDatabaseNames.IsCompanyDatabase("Vatowiec_Company_0057827c8b3045d599b1230c1db2e92b")); // Nazwy z kroku 1.
        foreach (var reserved in new[] { "Vatowiec_System", "vatowiec_system", "Vatowiec_System_Test", "Vatowiec_Dev", "Vatowiec_Company_A_Dev",
                     "Vatowiec_Company_B_Test", "Vatowiec_Personal_Prototype_System", "Vatowiec_Personal_Anything" })
        {
            Assert.True(CompanyDatabaseNames.IsReserved(reserved));
            Assert.False(CompanyDatabaseNames.IsCompanyDatabase(reserved));
        }
        Assert.Equal("Vatowiec_System", CompanyDatabaseNames.FromCompanyName("System")); // Podgląd wskaże nazwę; serwer uzna ją za zajętą.
        foreach (var invalid in new[] { "Vatowiec_Tenjin]; DROP DATABASE x;--", "Vatowiec_", "Vatowiec__x", "Vatowiec_x_", "Other_Tenjin", "Vatowiec_Ten jin", "Vatowiec_" + new string('a', 101) })
            Assert.False(CompanyDatabaseNames.IsCompanyDatabase(invalid));
        const string local = @"Server=localhost\SQLEXPRESS;Integrated Security=True;Encrypt=True;TrustServerCertificate=True;Database=";
        DatabaseConnectionPolicy.ValidateCompany(local + "Vatowiec_Tenjin", isDevelopment: true);
        Assert.Throws<InvalidOperationException>(() => DatabaseConnectionPolicy.ValidateCompany(local + "Vatowiec_Tenjin", isDevelopment: false));
        Assert.Throws<InvalidOperationException>(() => DatabaseConnectionPolicy.ValidateCompany(local + "Other_Tenjin", isDevelopment: true));
        Assert.Throws<InvalidOperationException>(() => DatabaseConnectionPolicy.ValidateCompany(local + "Vatowiec_System", isDevelopment: true));
    }

    [Fact]
    public void ApiAssembly_HasNoReferenceToTheOperationsProcess()
    {
        // Konto API nie wykonuje DDL: kod tworzenia baz istnieje wyłącznie w osobnym procesie.
        var api = typeof(Vatowiec.Api.Security.CompanyOperationEndpoints).Assembly;
        Assert.DoesNotContain(api.GetReferencedAssemblies(), x => x.Name == "Vatowiec.Operations");
        Assert.DoesNotContain(api.GetTypes(), x => x.Name.Contains("Executor", StringComparison.Ordinal));
        Assert.Equal("Vatowiec.Operations", typeof(Vatowiec.Operations.CompanyProvisioningExecutor).Assembly.GetName().Name);
    }

    [Theory]
    [InlineData("GET", "/api/v1/installation/company-operations")]
    [InlineData("GET", "/api/v1/installation/company-operations/server-profiles")]
    [InlineData("POST", "/api/v1/installation/company-operations/provision")]
    [InlineData("POST", "/api/v1/installation/company-operations/00000000-0000-4000-8000-000000000001/retry")]
    public async Task Endpoints_RejectAnonymousCallers(string method, string path)
    {
        using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("environment", "Development");
            builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(
                new Dictionary<string, string?> { ["ConnectionStrings:VatowiecSystem"] = null }));
        });
        using var client = factory.CreateClient(new() { BaseAddress = new("https://localhost"), AllowAutoRedirect = false });
        using var request = new HttpRequestMessage(new HttpMethod(method), path);
        if (method == "POST") request.Content = System.Net.Http.Json.JsonContent.Create(new { });
        using var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}

public sealed partial class DesktopTests
{
    [Fact]
    public async Task CompanyOperations_KeepOperationKeyAfterLostResponse_NewKeyAfterSuccess_RetryOnlyAfterFailure()
    {
        var api = new FakeApi { InstallationAdmin = true };
        using var model = new MainViewModel(_ => api) { UserName = "admin" };
        await model.Login("fixture-password");
        await model.OpenCompanyOperations();
        Assert.True(model.IsCompanyOperationsScreen); Assert.False(model.IsCompanySelection);
        Assert.Equal("LocalWindows", model.SelectedServerProfile?.Key); // Jedyny profil wybrany automatycznie.
        Assert.False(model.CanProvisionCompany);
        Assert.False(model.IsServerChoiceVisible); // Program pyta tylko o nazwę, gdy serwer jest jeden.
        model.NewCompanyName = "!!!";
        Assert.False(model.CanProvisionCompany); Assert.Contains("litery lub cyfry", model.NewCompanyDatabaseText);
        model.NewCompanyName = "  Nowa firma fikcyjna  ";
        Assert.True(model.CanProvisionCompany);
        Assert.Equal("Baza SQL: Vatowiec_Nowa_firma_fikcyjna", model.NewCompanyDatabaseText);

        api.ProvisionError = new ApiException(0, "Nie można połączyć się z API.");
        var key = model.ProvisionOperationKey;
        await model.ProvisionCompany();
        Assert.Equal(key, model.ProvisionOperationKey); // Ponowienie po utracie odpowiedzi używa tego samego klucza.
        Assert.Equal("  Nowa firma fikcyjna  ", model.NewCompanyName);
        api.ProvisionError = null;
        await model.ProvisionCompany();
        var sent = Assert.Single(api.ProvisionRequests);
        Assert.Equal((key, "Nowa firma fikcyjna", "LocalWindows"), (sent.OperationKey, sent.CompanyName, sent.ConnectionProfileKey));
        Assert.NotEqual(key, model.ProvisionOperationKey); Assert.Equal("", model.NewCompanyName);
        Assert.Contains("Przyjęto zlecenie", model.Message);
        Assert.Equal("Nowa firma fikcyjna", model.SelectedCompanyOperation?.Name);
        Assert.False(model.CanRetryCompanyOperation);

        model.NewCompanyName = "Inna"; var other = model.ProvisionOperationKey; model.NewCompanyName = "Inna firma";
        Assert.NotEqual(other, model.ProvisionOperationKey); // Zmiana danych = nowe zlecenie, nie konflikt treści.

        api.OperationRows[0] = api.OperationRows[0] with { Status = "NeedsReconciliation", LastErrorCode = "database_not_owned" };
        await model.RefreshCompanyOperations();
        Assert.True(model.CanRetryCompanyOperation);
        Assert.Contains("nie przejmie ani nie usunie", model.SelectedCompanyOperation!.Error);
        await model.RetryCompanyOperation();
        Assert.Equal(api.OperationRows[0].Id, Assert.Single(api.Retries));
        Assert.False(model.CanRetryCompanyOperation);

        await model.CloseCompanyOperations();
        Assert.False(model.IsCompanyOperationsScreen); Assert.True(model.IsGusSettings);
    }

    [Fact]
    public async Task CompanyOperations_AreUnavailableWithoutInstallationRole_AndForbiddenResetsTheScreen()
    {
        var api = new FakeApi();
        using var model = new MainViewModel(_ => api) { UserName = "operator" };
        await model.Login("fixture-password");
        await model.OpenCompanyOperations();
        Assert.False(model.IsCompanyOperationsScreen); Assert.Equal(0, api.ProfileReads);

        var admin = new FakeApi { InstallationAdmin = true };
        using var second = new MainViewModel(_ => admin) { UserName = "admin" };
        await second.Login("fixture-password"); await second.OpenCompanyOperations();
        admin.OperationsError = new ApiException(403, "Tworzenie firm wymaga aktualnej roli administratora instalacji.");
        await second.RefreshCompanyOperations();
        Assert.False(second.IsCompanyOperationsScreen); Assert.False(second.IsInstallationAdministrator);
        Assert.Contains("administratora instalacji", second.Message);
    }

    private sealed partial class FakeApi
    {
        public List<ProvisionCompanyRequest> ProvisionRequests { get; } = [];
        public List<Guid> Retries { get; } = [];
        public List<CompanyOperationResponse> OperationRows { get; } = [];
        public ApiException? ProvisionError, OperationsError;
        public int ProfileReads;
        public Task<ServerProfileResponse[]> ServerProfiles()
        { ProfileReads++; return Task.FromResult<ServerProfileResponse[]>([new("LocalWindows", @"localhost\SQLEXPRESS")]); }
        public Task<CompanyOperationResponse[]> CompanyOperations(int offset)
        { if (OperationsError != null) throw OperationsError; return Task.FromResult(OperationRows.Skip(offset).ToArray()); }
        public Task<CompanyOperationResponse> ProvisionCompany(ProvisionCompanyRequest value)
        {
            if (ProvisionError != null) throw ProvisionError;
            ProvisionRequests.Add(value);
            var row = new CompanyOperationResponse(Guid.NewGuid(), "Provision", Guid.NewGuid(), value.CompanyName, "Requested", "Pending", 0, null,
                "admin", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, null, "v1");
            OperationRows.Insert(0, row);
            return Task.FromResult(row);
        }
        public Task<CompanyOperationResponse> RetryCompanyOperation(Guid operationId, CompanyOperationRetryRequest value)
        {
            Retries.Add(operationId);
            var index = OperationRows.FindIndex(x => x.Id == operationId);
            OperationRows[index] = OperationRows[index] with { Status = "Pending", Attempt = 0, LastErrorCode = null, RowVersion = "v2" };
            return Task.FromResult(OperationRows[index]);
        }
    }
}
