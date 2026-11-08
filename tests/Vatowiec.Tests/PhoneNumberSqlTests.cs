using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Vatowiec.Core.Application;
using Vatowiec.Core.Domain;
using Vatowiec.Core.Infrastructure.Identity;
using Vatowiec.Core.Infrastructure.Persistence;
using Vatowiec.Desktop.ViewModels;
using Vatowiec.SharedContracts;

namespace Vatowiec.Tests;

[Collection("LocalSql")]
[Trait("Category", "SqlIntegration")]
public sealed class PhoneNumberSqlTests
{
    private static readonly Guid Company = Guid.Parse("aaaaaaaa-0000-4000-8000-000000000001");

    [PreservedVatSqlFact]
    public async Task ApiValidatesPhonesAndPreservesSqlEditorRoundtrip_WithRollback()
    {
        await using var central = await PreservedSystemTransaction.Open();
        await using var company = await PreservedSystemTransaction.Open("Vatowiec_Company_A_Dev");
        var factory = new Contexts(company);
        using var app = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Development"); builder.ConfigureLogging(log => log.ClearProviders());
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<SystemDbContext>(); services.AddScoped(_ => central.Context());
                services.RemoveAll<ICompanyDatabaseContextFactory>(); services.AddSingleton<ICompanyDatabaseContextFactory>(factory);
                services.AddDataProtection().UseEphemeralDataProtectionProvider();
                services.AddScoped(_ => ProductionRegistryTests.Settings("Mock"));
            });
        });
        using var scope = app.Services.CreateScope();
        var system = scope.ServiceProvider.GetRequiredService<SystemDbContext>();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var password = "Fixture!a1-" + Guid.NewGuid().ToString("N");
        var owner = new ApplicationUser { Id = Guid.NewGuid(), UserName = "fixture-phone-" + Guid.NewGuid().ToString("N") };
        var reader = new ApplicationUser { Id = Guid.NewGuid(), UserName = "fixture-phone-read-" + Guid.NewGuid().ToString("N") };
        Assert.True((await users.CreateAsync(owner, password)).Succeeded);
        Assert.True((await users.CreateAsync(reader, password)).Succeeded);
        system.CompanyAccesses.Add(new(owner.Id, Company, CompanyPermission.ViewContractors | CompanyPermission.EditContractors));
        system.CompanyAccesses.Add(new(reader.Id, Company, CompanyPermission.ViewContractors)); await system.SaveChangesAsync();
        using var db = factory.Create("", Company);
        var contractor = new Contractor("fixture-phone-" + Guid.NewGuid().ToString("N"), "Kontakt testowy", "PL");
        db.Contractors.Add(contractor); await db.SaveChangesAsync();
        var path = $"/api/v1/companies/{Company}/contractors/{contractor.Id}/contacts";
        using var client = app.CreateClient(new() { BaseAddress = new("https://localhost"), AllowAutoRedirect = false });
        await GusConfigurationSqlTests.Login(client, owner.UserName!, password);
        var maxLength = db.Model.FindEntityType(typeof(ContractorContact))!.FindProperty(nameof(ContractorContact.Phone))!.GetMaxLength();
        Assert.True(maxLength >= 16);
        foreach (var (input, stored, display) in new[] {
            ("501234567", "+48501234567", "501234567"), ("+48501234567", "+48501234567", "501234567"),
            ("+48 501 234 567", "+48501234567", "501234567"), ("0048501234567", "+48501234567", "501234567"),
            ("+4930123456", "+4930123456", "+4930123456"), ("+420601123456", "+420601123456", "+420601123456"),
            ("221234567", "+48221234567", "221234567"), ("+390236618300", "+390236618300", "+390236618300") })
        {
            var request = new DetailWriteRequest(0, new() { Name = "Kontakt testowy", Phone = input }, OperationId: Guid.NewGuid());
            using var created = await client.PostAsJsonAsync(path, request);
            Assert.Equal(HttpStatusCode.Created, created.StatusCode);
            var row = (await created.Content.ReadFromJsonAsync<DetailResponse>())!;
            Assert.Equal(stored, row.Data.Phone);
            Assert.Equal(stored, (await db.ContractorContacts.AsNoTracking().SingleAsync(x => x.Id == row.Id)).Phone);
            using var replay = await client.PostAsJsonAsync(path, request with { Data = request.Data with { Phone = stored } });
            Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
            Assert.Equal(row.Id, (await replay.Content.ReadFromJsonAsync<DetailResponse>())!.Id);
            var loaded = (await client.GetFromJsonAsync<DetailResponse>(path + "/" + row.Id))!;
            var editor = new DetailEditor(loaded); Assert.Equal(display, editor.Phone);
            var audit = await db.ContractorChanges.CountAsync();
            using var saved = await client.PutAsJsonAsync(path + "/" + row.Id, editor.Request());
            Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
            Assert.Equal(row.RowVersion, (await saved.Content.ReadFromJsonAsync<DetailResponse>())!.RowVersion);
            Assert.Equal(audit, await db.ContractorChanges.CountAsync());
            Assert.Equal(stored, (await db.ContractorContacts.AsNoTracking().SingleAsync(x => x.Id == row.Id)).Phone);
            using var invalidEdit = await client.PutAsJsonAsync(path + "/" + row.Id, editor.Request() with { Data = row.Data with { Phone = "50123456" } });
            Assert.Equal(HttpStatusCode.BadRequest, invalidEdit.StatusCode);
            Assert.Equal(stored, (await db.ContractorContacts.AsNoTracking().SingleAsync(x => x.Id == row.Id)).Phone);
        }
        var count = await db.ContractorContacts.CountAsync(); var changes = await db.ContractorChanges.CountAsync();
        foreach (var invalid in new[] { "50123456", "5012345678", "501ABC567", "+999501234567", "501234567/601234567", "+49501234567" })
        {
            using var rejected = await client.PostAsJsonAsync(path, new DetailWriteRequest(0, new() { Name = "Błędny", Phone = invalid }, OperationId: Guid.NewGuid(), AcceptWarning: true));
            Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);
            var error = await rejected.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal("validation_failed", error.GetProperty("code").GetString());
            Assert.StartsWith("Telefon:", error.GetProperty("errors").GetProperty("Phone")[0].GetString());
        }
        Assert.Equal(count, await db.ContractorContacts.CountAsync()); Assert.Equal(changes, await db.ContractorChanges.CountAsync());
        foreach (var legacy in new[] { "501 234 567", "TEST", "+49501234567", "" })
        {
            var old = new ContractorContact(contractor.Id, "Kontakt historyczny") { Phone = legacy }; db.Add(old); await db.SaveChangesAsync();
            var row = (await client.GetFromJsonAsync<DetailResponse>(path + "/" + old.Id))!; var editor = new DetailEditor(row);
            editor.Notes = "Edycja uwag bez zmiany telefonu";
            using var saved = await client.PutAsJsonAsync(path + "/" + old.Id, editor.Request());
            Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
            Assert.Equal(legacy, (await db.ContractorContacts.AsNoTracking().SingleAsync(x => x.Id == old.Id)).Phone);
            using var stale = await client.PutAsJsonAsync(path + "/" + old.Id, editor.Request());
            Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        }
        using var empty = await client.PostAsJsonAsync(path, new DetailWriteRequest(0, new() { Name = "Bez telefonu" }, OperationId: Guid.NewGuid()));
        Assert.Equal(HttpStatusCode.Created, empty.StatusCode);
        Assert.Null((await empty.Content.ReadFromJsonAsync<DetailResponse>())!.Data.Phone);
        using var read = app.CreateClient(new() { BaseAddress = new("https://localhost"), AllowAutoRedirect = false });
        await GusConfigurationSqlTests.Login(read, reader.UserName!, password);
        var create = new DetailWriteRequest(0, new() { Name = "Brak uprawnień", Phone = "501234567" }, OperationId: Guid.NewGuid());
        using var forbidden = await read.PostAsJsonAsync(path, create); Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
        client.DefaultRequestHeaders.Remove("X-CSRF-TOKEN");
        using var noCsrf = await client.PostAsJsonAsync(path, create); Assert.Equal(HttpStatusCode.BadRequest, noCsrf.StatusCode);
    }

    private sealed class Contexts(PreservedSystemTransaction company) : ICompanyDatabaseContextFactory
    {
        public CompanyDbContext Create(string connectionString, Guid companyId) => companyId == Company
            ? new(company.CompanyOptions, new CompanyContext(companyId)) : throw new InvalidOperationException("Unexpected company in phone fixture");
    }
}
