using System.Net;
using System.Net.Http.Json;
using System.Runtime.Versioning;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Vatowiec.Core.Application;
using Vatowiec.Core.Domain;
using Vatowiec.Core.Infrastructure.Personal;
using Vatowiec.Core.Infrastructure.Persistence;
using Vatowiec.Desktop.Services;
using Vatowiec.SharedContracts;

namespace Vatowiec.Tests;

[Collection("LocalSql")]
[SupportedOSPlatform("windows")]
public sealed class TableViewSqlTests
{
    [PersonalLocalDbFact]
    public async Task IsolatedInstallation_MigrationHierarchyConcurrencyAuditSearchPrivacyAndRestart()
    {
        var root = PersonalSetupTests.NewRoot(); PersonalDatabaseTarget target; Guid company, originalUser; int contractor;
        using (var backend = new PersonalBackend(root))
        {
            var init = await backend.Send(PersonalSetupTests.Input("fixture")); Assert.True(init.Success, init.Message);
            var installation = JsonSerializer.Deserialize<PersonalInstallationState>(await File.ReadAllTextAsync(Path.Combine(root, "installation.json")))!;
            {
                target = new(installation.InstallationId); company = installation.CompanyId;
                await using var db = System(target);
                var usersBefore = await db.Users.AsNoTracking().Select(x => new { x.Id, x.PasswordHash, x.ConcurrencyStamp }).ToArrayAsync();
                // Only this brand-new isolated fixture is moved across the new migration boundary.
                var previous = db.Database.GetMigrations().Reverse().Skip(1).First();
                await db.GetService<IMigrator>().MigrateAsync(previous);
                await db.Database.MigrateAsync();
                Assert.Equal(usersBefore, await db.Users.AsNoTracking().Select(x => new { x.Id, x.PasswordHash, x.ConcurrencyStamp }).ToArrayAsync());
                Assert.Empty(await db.TableViews.ToArrayAsync()); Assert.Empty(await db.TableViewChanges.ToArrayAsync());
            }
            await using (var db = Company(target, company))
            {
                var row = new Contractor("CITY", "Fikcyjny kontrahent miasta", "PL"); row.SetStandardIdentifiers("192598184", null, null, "44051401458");
                db.Contractors.Add(row); await db.SaveChangesAsync(); contractor = row.Id;
                db.ContractorAddresses.AddRange(new ContractorAddress(row.Id, 2) { City = "KorespondencjaNieSzukaj", IsDefault = true },
                    new ContractorAddress(row.Id, 1) { City = "InnyRejestrowy" },
                    new ContractorAddress(row.Id, 1) { City = "WidoczneMiasto", Street = "Testowa", IsDefault = true });
                db.Contractors.AddRange(Enumerable.Range(0, 205).Select(i => new Contractor("PAGE" + i.ToString("D3"), "Fikcyjna pozycja strony", "PL")));
                await db.SaveChangesAsync();
            }
            var ready = await backend.Send(new() { Operation = "serve" }); Assert.True(ready.Success, ready.Message);
            using var api = backend.Connect(ready.ApiAddress!);
            originalUser = (await api.Login("wizard.fixture", "fixture")).UserId;
            var view = await api.TableView(company, "Contractors"); Assert.Equal("Factory", view.Source); Assert.Equal(6, view.VisibleColumns.Length);
            var global = await api.SaveTableView(company, "Contractors", new(["Name", "Regon", "Pesel"]), true);
            Assert.Equal("Installation", global.Source);
            var pageOnly = await api.SaveTableView(company, "Contractors", new(PageSize: 25), false);
            Assert.Equal("Installation", pageOnly.Source); Assert.Contains("Pesel", pageOnly.VisibleColumns);
            var own = await api.SaveTableView(company, "Contractors", new(["Code", "AddressCity"], 200, pageOnly.UserVersion), false);
            Assert.Equal("User", own.Source); Assert.Equal(200, own.PageSize);
            var stale = await Assert.ThrowsAsync<ApiException>(() => api.SaveTableView(company, "Contractors", new(["TaxId"]), false));
            Assert.Equal(409, stale.StatusCode);
            Assert.Equal(400, (await Assert.ThrowsAsync<ApiException>(() => api.SaveTableView(company, "Contractors", new([], RowVersion: own.UserVersion), false))).StatusCode);
            foreach (var size in TableCatalog.PageSizes)
            {
                var rows = await api.SearchContractors(company, new(Active: true, Limit: size + 1, VisibleColumns: ["Code"]), default);
                Assert.Equal(size + 1, rows.Length);
            }
            var city = await api.SearchContractors(company, new("WidoczneMiasto", VisibleColumns: ["AddressCity"]), default);
            Assert.Equal(contractor, Assert.Single(city).Id); Assert.Equal("WidoczneMiasto", city[0].RegisteredAddress!.City);
            Assert.Empty(await api.SearchContractors(company, new("WidoczneMiasto", VisibleColumns: ["Name"]), default));
            Assert.Empty(await api.SearchContractors(company, new("KorespondencjaNieSzukaj", VisibleColumns: ["AddressCity"]), default));
            Assert.Empty(await api.SearchContractors(company, new("InnyRejestrowy", VisibleColumns: ["AddressCity"]), default));
            Assert.Equal(contractor, Assert.Single(await api.SearchContractors(company, new("192598184", VisibleColumns: ["Regon"]), default)).Id);
            var dated = Assert.Single(await api.SearchContractors(company, new("CITY", VisibleColumns: ["Code"]), default));
            var dateMatches = await api.SearchContractors(company, new(dated.CreatedAtUtc!.Value.ToString("yyyy-MM-dd"), VisibleColumns: ["CreatedAtUtc"]), default);
            Assert.NotEmpty(dateMatches); Assert.All(dateMatches, x => Assert.Equal(dated.CreatedAtUtc.Value.Date, x.CreatedAtUtc!.Value.Date));
            Assert.Equal(400, (await Assert.ThrowsAsync<ApiException>(() => api.SearchContractors(company, new("x", VisibleColumns: ["Name]; DROP TABLE Users"]), default))).StatusCode);
            // Preview reads do not alter data, timestamps, version or audit.
            var before = await api.Contractor(company, contractor);
            await api.Details(company, contractor, "addresses"); await api.Details(company, contractor, "contacts"); await api.Details(company, contractor, "bank-accounts");
            Assert.Equal(before, await api.Contractor(company, contractor));
            await using (var db = Company(target, company)) Assert.Empty(await db.ContractorChanges.ToArrayAsync());

            var created = await api.CreateUser(new("reader.fixture", "temporary"));
            await api.SaveUserAccess(created.User.User.Id, new(created.User.ConcurrencyStamp,
                [new(company, ["ViewProfile", "ViewContractors"], null)]));
            using var reader = backend.Connect(ready.ApiAddress!);
            await reader.Login("reader.fixture", "temporary"); await reader.ChangeOwnPassword(new("temporary", "reader"));
            await reader.Login("reader.fixture", "reader");
            var readerView = await reader.TableView(company, "Contractors"); Assert.Equal("Installation", readerView.Source);
            Assert.Null(readerView.UserVersion); Assert.DoesNotContain(readerView.Columns, c => c.Id == "Pesel");
            Assert.DoesNotContain("Pesel", readerView.VisibleColumns);
            Assert.Equal(400, (await Assert.ThrowsAsync<ApiException>(() => reader.SearchContractors(company, new("44051401458", VisibleColumns: ["Pesel"]), default))).StatusCode);
            Assert.Equal(403, (await Assert.ThrowsAsync<ApiException>(() => reader.SaveTableView(company, "Contractors", new(["Name"], RowVersion: readerView.InstallationVersion), true))).StatusCode);
            Assert.Equal(403, (await Assert.ThrowsAsync<ApiException>(() => reader.SaveContractor(company, new(0, "DENIED", "Denied", "PL", true, OperationId: Guid.NewGuid()), true))).StatusCode);
            Assert.Null((await reader.Contractor(company, contractor)).Pesel);
            var ownReader = await reader.SaveTableView(company, "Contractors", new(["Code"]), false);
            Assert.Equal("User", ownReader.Source); Assert.Equal(new[] { "Code", "AddressCity" }, (await api.TableView(company, "Contractors")).VisibleColumns);
            Assert.Equal(403, (await Assert.ThrowsAsync<ApiException>(() => reader.TableView(Guid.NewGuid(), "Contractors"))).StatusCode);

            using var raw = backend.CreateHttpClient(); await GusConfigurationSqlTests.Login(raw, "reader.fixture", "reader");
            raw.DefaultRequestHeaders.Remove("X-CSRF-TOKEN");
            var route = $"api/v1/companies/{company}/table-views/Contractors/mine";
            Assert.Equal(HttpStatusCode.BadRequest, (await raw.PutAsJsonAsync(route, new TableViewRequest(["Name"], RowVersion: ownReader.UserVersion))).StatusCode);
            var csrf = await raw.GetFromJsonAsync<CsrfResponse>("api/v1/auth/csrf");
            using var request = new HttpRequestMessage(HttpMethod.Post, $"api/v1/companies/{company}/contractors/search") { Content = JsonContent.Create(new ContractorSearchRequest(VisibleColumns: ["Code"])) };
            request.Headers.Add("X-CSRF-TOKEN", csrf!.Token);
            using var response = await raw.SendAsync(request); Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.DoesNotContain("\"pesel\"", await response.Content.ReadAsStringAsync(), StringComparison.OrdinalIgnoreCase);

            // Both reset scopes remain independent, including for an administrator with a personal view.
            view = await api.TableView(company, "Contractors");
            view = await api.SaveTableView(company, "Contractors", new(RowVersion: view.InstallationVersion, Reset: true), true);
            Assert.Equal("User", view.Source); Assert.Equal(new[] { "Code", "AddressCity" }, view.VisibleColumns);
            view = await api.SaveTableView(company, "Contractors", new(RowVersion: view.UserVersion, Reset: true), false);
            Assert.Equal("Factory", view.Source); Assert.Equal(50, view.PageSize);
            view = await api.SaveTableView(company, "Contractors", new(["Name"]), true);
            await using (var db = System(target))
            {
                Assert.Equal(3, await db.TableViewChanges.CountAsync());
                await Assert.ThrowsAsync<Microsoft.Data.SqlClient.SqlException>(() => db.Database.ExecuteSqlRawAsync("UPDATE audit.TableViewChanges SET ActorName=N'forbidden'"));
                await db.Database.ExecuteSqlRawAsync("CREATE TRIGGER [audit].[TR_TableViewFixtureReject] ON [audit].[TableViewChanges] AFTER INSERT AS THROW 51001, 'fixture rejection', 1;");
            }
            try
            {
                Assert.Equal(503, (await Assert.ThrowsAsync<ApiException>(() => api.SaveTableView(company, "Contractors", new(["Code"], RowVersion: view.InstallationVersion), true))).StatusCode);
                Assert.Equal(new[] { "Name" }, (await api.TableView(company, "Contractors")).VisibleColumns);
            }
            finally { await using var db = System(target); await db.Database.ExecuteSqlRawAsync("DROP TRIGGER [audit].[TR_TableViewFixtureReject]"); }
            var targetUser = await api.UserDetail(created.User.User.Id);
            var archived = await api.ArchiveUser(targetUser.User.Id, new(targetUser.ConcurrencyStamp, true));
            var replacement = await api.RestoreUser(targetUser.User.Id, new(archived.ConcurrencyStamp, true, "temporary2"), true);
            Assert.NotEqual(targetUser.User.Id, replacement.User.User.Id);
            await api.SaveUserAccess(replacement.User.User.Id, new(replacement.User.ConcurrencyStamp, [new(company, ["ViewProfile", "ViewContractors"], null)]));
            using var newPerson = backend.Connect(ready.ApiAddress!); await newPerson.Login("reader.fixture", "temporary2");
            await newPerson.ChangeOwnPassword(new("temporary2", "reader2")); await newPerson.Login("reader.fixture", "reader2");
            var newView = await newPerson.TableView(company, "Contractors"); Assert.Null(newView.UserVersion); Assert.Equal("Installation", newView.Source);
            await api.Logout();
        }
        using (var backend = new PersonalBackend(root))
        {
            var ready = await backend.Send(new() { Operation = "serve" }); Assert.True(ready.Success, ready.Message);
            using var api = backend.Connect(ready.ApiAddress!); Assert.Equal(originalUser, (await api.Login("wizard.fixture", "fixture")).UserId);
            var persisted = await api.TableView(company, "Contractors"); Assert.Equal("Installation", persisted.Source); Assert.Equal(new[] { "Name" }, persisted.VisibleColumns);
            Assert.Equal(contractor, (await api.Contractor(company, contractor)).Id);
        }
        await NetworkHostAndTwoPhysicalCompanies(target, company, originalUser);
        Directory.CreateDirectory(PersonalLocalPaths.VerificationDirectory);
        await File.WriteAllTextAsync(Path.Combine(PersonalLocalPaths.VerificationDirectory, "table-views-" + target.InstallationId.ToString("N") + ".json"), JsonSerializer.Serialize(new { root, target.Instance, target.System, target.Company, completed = true }));
        await PersonalInstallation.LocalDb("stop", target.Instance);
    }
    private static async Task NetworkHostAndTwoPhysicalCompanies(PersonalDatabaseTarget first, Guid companyA, Guid user)
    {
        // A second brand-new private installation supplies the second physical company database.
        // The in-memory Network HTTP host never listens on a port and never opens existing databases.
        var secondRoot = PersonalSetupTests.NewRoot(); PersonalDatabaseTarget second; Guid companyB;
        using (var backend = new PersonalBackend(secondRoot))
        {
            var initialized = await backend.Send(PersonalSetupTests.Input("fixture")); Assert.True(initialized.Success, initialized.Message);
            var state = JsonSerializer.Deserialize<PersonalInstallationState>(await File.ReadAllTextAsync(Path.Combine(secondRoot, "installation.json")))!;
            second = new(state.InstallationId); companyB = state.CompanyId;
        }
        await using (var db = Company(second, companyB))
        {
            var contractor = new Contractor("SECOND", "Wyłącznie druga firma", "PL"); db.Contractors.Add(contractor); await db.SaveChangesAsync();
            db.ContractorAddresses.Add(new(contractor.Id, 1) { City = "MiastoDrugiejFirmy" }); await db.SaveChangesAsync();
        }
        await using (var db = System(first))
        {
            db.Companies.Add(new(companyB, "Izolowana firma B"));
            db.CompanyDatabases.Add(new(companyB, second.Server, second.Company, "IsolatedB"));
            db.CompanyAccesses.Add(new(user, companyB, CompanyPermission.ViewProfile | CompanyPermission.ViewContractors));
            await db.SaveChangesAsync();
        }
        static string LogicalConnection(PersonalDatabaseTarget target) => new Microsoft.Data.SqlClient.SqlConnectionStringBuilder(target.Connection(target.Company)) { TrustServerCertificate = false }.ConnectionString;
        using (var app = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Development"); builder.ConfigureLogging(log => log.ClearProviders());
            builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Vatowiec:Edition"] = "Network",
                ["ConnectionStrings:VatowiecSystem"] = null,
                ["CompanyConnections:Personal"] = LogicalConnection(first),
                ["CompanyConnections:IsolatedB"] = LogicalConnection(second)
            }));
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<SystemDbContext>(); services.AddScoped(_ => System(first));
                services.AddScoped<ICompanyDatabaseContextFactory>(_ => new IsolatedContexts(first, second));
                services.AddDataProtection().UseEphemeralDataProtectionProvider();
            });
        }))
        using (var raw = app.CreateClient(new() { BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false }))
        using (var api = new VatowiecApiClient(raw))
        {
            await api.Login("wizard.fixture", "fixture");
            Assert.Empty(await api.SearchContractors(companyA, new("MiastoDrugiejFirmy", VisibleColumns: ["AddressCity"]), default));
            var match = Assert.Single(await api.SearchContractors(companyB, new("MiastoDrugiejFirmy", VisibleColumns: ["AddressCity"]), default));
            Assert.Equal(companyB, match.CompanyId); Assert.Equal("SECOND", match.Code);
            Assert.Equal(new[] { "Name" }, (await api.TableView(companyA, "Contractors")).VisibleColumns);
            Assert.Equal(new[] { "Name" }, (await api.TableView(companyB, "Contractors")).VisibleColumns);
            var saved = await api.SaveTableView(companyA, "Contractors", new(["Regon"], 100), false);
            Assert.Equal(saved.UserVersion, (await api.TableView(companyB, "Contractors")).UserVersion);
            using var secondRaw = app.CreateClient(new() { BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false });
            using var otherStation = new VatowiecApiClient(secondRaw); await otherStation.Login("wizard.fixture", "fixture");
            Assert.Equal(new[] { "Regon" }, (await otherStation.TableView(companyA, "Contractors")).VisibleColumns);
            var parallel = await Task.WhenAll(Attempt(api), Attempt(otherStation));
            Assert.Equal(new[] { 200, 409 }, parallel.Order().ToArray());
            async Task<int> Attempt(IVatowiecApiClient station)
            {
                try { await station.SaveTableView(companyA, "Contractors", new(["Name"], RowVersion: saved.UserVersion), false); return 200; }
                catch (ApiException ex) { return ex.StatusCode; }
            }
        }
        await PersonalInstallation.LocalDb("stop", second.Instance);
    }
    // SQL certificate policy remains unchanged in production. Only this test's EF connection factory
    // supplies the local IPC connection for the two exact new private targets above.
    private sealed class IsolatedContexts(PersonalDatabaseTarget first, PersonalDatabaseTarget second) : ICompanyDatabaseContextFactory
    {
        public CompanyDbContext Create(string connection, Guid company)
        {
            var logical = new Microsoft.Data.SqlClient.SqlConnectionStringBuilder(connection);
            var target = new[] { first, second }.Single(t => t.Server == logical.DataSource && t.Company == logical.InitialCatalog);
            return Company(target, company);
        }
    }
    private static SystemDbContext System(PersonalDatabaseTarget t) => new(new DbContextOptionsBuilder<SystemDbContext>().UseSqlServer(t.Connection(t.System)).Options);
    private static CompanyDbContext Company(PersonalDatabaseTarget t, Guid id) => new(new DbContextOptionsBuilder<CompanyDbContext>().UseSqlServer(t.Connection(t.Company)).Options, new CompanyContext(id));
}
