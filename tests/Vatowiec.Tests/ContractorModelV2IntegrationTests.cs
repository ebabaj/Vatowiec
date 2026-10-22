using System.Net;
using System.Net.Http.Json;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Vatowiec.Core.Domain;
using Vatowiec.Core.Infrastructure.Persistence;
using Vatowiec.SharedContracts;

namespace Vatowiec.Tests;

public sealed partial class AuthenticationIntegrationTests
{
    [SqlServerFact]
    public async Task DesktopClient_CreatesAndEditsIntIdentityWithoutTaxIdentifierWrites()
    {
        using var model = new Vatowiec.Desktop.ViewModels.MainViewModel(_ => new Vatowiec.Desktop.Services.VatowiecApiClient(Client()))
        { UserName = "operator.test" };
        await model.Login(password);
        model.SelectedCompany = model.Companies.Single(x => x.CompanyId == companyA);
        await model.OpenCompany();
        model.NewContractor();
        model.Editor!.Code = "DESKTOP-V2";
        model.Editor.Name = "Fikcyjna kartoteka Avalonia";
        Assert.Equal(0, model.Editor.Id);
        await model.SaveContractor();
        Assert.False(model.IsEditing, model.Message);
        model.SearchText = "DESKTOP-V2";
        await model.LoadContractors();
        var saved = Assert.Single(model.Contractors);
        Assert.True(saved.Id > 0);
        model.SelectedContractor = saved;
        await model.EditContractor();
        model.Editor!.Name = "Fikcyjna edycja Avalonia";
        await model.SaveContractor();
        Assert.False(model.IsEditing, model.Message);
        await model.LoadContractors();
        Assert.Equal(saved.Id, Assert.Single(model.Contractors).Id);
        Assert.Equal("Fikcyjna edycja Avalonia", model.Contractors[0].Name);
        await model.ChangeCompany();
        model.SelectedCompany = model.Companies.Single(x => x.CompanyId == companyB);
        await model.OpenCompany();
        model.SearchText = "DESKTOP-V2";
        await model.LoadContractors();
        Assert.Empty(model.Contractors);
        await model.Logout();
        await using var db = Company(false, companyA);
        Assert.Equal(2, await db.ContractorChanges.CountAsync(x => x.ContractorId == saved.Id));
    }

    [SqlServerFact]
    public async Task ConcurrentParentChanges_CannotCommitCycle()
    {
        using var first = await Login("operator.test");
        using var second = await Login("operator.test");
        await Csrf(first);
        await Csrf(second);
        var a = (await Read(first, contractorId))!;
        using var created = await first.PostAsJsonAsync(ContractorsPath(), NewContractor("PARENT-SECOND"));
        var b = (await created.Content.ReadFromJsonAsync<ContractorResponse>())!;
        var results = await Task.WhenAll(
            first.PutAsJsonAsync($"{ContractorsPath()}/{a.Id}", Edit(a, a.Name) with { ParentId = b.Id }),
            second.PutAsJsonAsync($"{ContractorsPath()}/{b.Id}", Edit(b, b.Name) with { ParentId = a.Id }));
        Assert.Single(results, x => x.StatusCode == HttpStatusCode.OK);
        Assert.Single(results, x => x.StatusCode == HttpStatusCode.BadRequest);
        foreach (var result in results) result.Dispose();
        await using var db = Company(false, companyA);
        Assert.Equal(2, await db.ContractorChanges.CountAsync());
    }

    [SqlServerFact]
    public async Task ConcurrentCreateAndReplayAfterEdit_ReturnOriginalDurableResult()
    {
        using var first = await Login("operator.test");
        using var second = await Login("operator.test");
        await Csrf(first);
        await Csrf(second);
        var request = NewContractor("IDEMPOTENT");
        var responses = await Task.WhenAll(first.PostAsJsonAsync(ContractorsPath(), request), second.PostAsJsonAsync(ContractorsPath(), request));
        Assert.Single(responses, x => x.StatusCode == HttpStatusCode.Created);
        Assert.Single(responses, x => x.StatusCode == HttpStatusCode.OK);
        var original = (await responses[0].Content.ReadFromJsonAsync<ContractorResponse>())!;
        Assert.True(original.Id > 0);
        Assert.Equal(original.Id, (await responses[1].Content.ReadFromJsonAsync<ContractorResponse>())!.Id);
        foreach (var response in responses) response.Dispose();
        await using var db = Company(false, companyA);
        Assert.Single(await db.ContractorCreateOperations.ToListAsync());
        Assert.Single(await db.ContractorChanges.ToListAsync());
        Assert.Equal(HttpStatusCode.OK, (await first.PutAsJsonAsync($"{ContractorsPath()}/{original.Id}", Edit(original, "Fikcyjna zmiana po utworzeniu"))).StatusCode);
        using var retry = await second.PostAsJsonAsync(ContractorsPath(), request);
        Assert.Equal(HttpStatusCode.OK, retry.StatusCode);
        Assert.Equal(original, await retry.Content.ReadFromJsonAsync<ContractorResponse>());
        Assert.Equal(2, await db.ContractorChanges.CountAsync());
        Assert.Equal(HttpStatusCode.Conflict, (await first.PostAsJsonAsync(ContractorsPath(), request with { Name = "Inna fikcyjna treść" })).StatusCode);
        // Uprawnienie sprawdzane ponownie także dla skutecznego wcześniej OperationId.
        using var scope = application!.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<SystemDbContext>().CompanyAccesses.Where(x => x.UserId == operatorId)
            .ExecuteUpdateAsync(x => x.SetProperty(a => a.Permissions, (CompanyPermission)3));
        Assert.Equal(HttpStatusCode.Forbidden, (await first.PostAsJsonAsync(ContractorsPath(), request)).StatusCode);
    }

    [SqlServerFact]
    public async Task OperationKey_IsScopedByActorAndCompany()
    {
        await GrantReaderEdit();
        using var op = await Login("operator.test");
        using var reader = await Login("reader.test");
        await Csrf(op);
        await Csrf(reader);
        var request = NewContractor("ACTOR-A");
        Assert.Equal(HttpStatusCode.Created, (await op.PostAsJsonAsync(ContractorsPath(), request)).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await reader.PostAsJsonAsync(ContractorsPath(), request with { Code = "ACTOR-B" })).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await op.PostAsJsonAsync(ContractorsPath(companyB), request)).StatusCode);
        await using var a = Company(false, companyA);
        await using var b = Company(true, companyB);
        Assert.Equal(2, await a.ContractorCreateOperations.CountAsync());
        Assert.Single(await b.ContractorCreateOperations.ToListAsync());
    }

    [SqlServerFact]
    public async Task CreateRollback_DoesNotConsumeOperationAndRetryCanSucceed()
    {
        using var op = await Login("operator.test");
        await Csrf(op);
        var request = NewContractor("ATOMIC");
        await using var db = Company(false, companyA);
        await db.Database.ExecuteSqlRawAsync("ALTER TABLE audit.ContractorChanges ADD CONSTRAINT CK_AtomicFailure CHECK (ActorName <> N'operator.test');");
        try
        {
            Assert.Equal(HttpStatusCode.ServiceUnavailable, (await op.PostAsJsonAsync(ContractorsPath(), request)).StatusCode);
            Assert.False(await db.Contractors.AnyAsync(x => x.Code == request.Code));
            Assert.Empty(await db.ContractorCreateOperations.ToListAsync());
            Assert.Empty(await db.ContractorChanges.ToListAsync());
        }
        finally { await db.Database.ExecuteSqlRawAsync("ALTER TABLE audit.ContractorChanges DROP CONSTRAINT CK_AtomicFailure;"); }
        Assert.Equal(HttpStatusCode.Created, (await op.PostAsJsonAsync(ContractorsPath(), request)).StatusCode);
        Assert.Single(await db.ContractorChanges.ToListAsync());
        Assert.Single(await db.ContractorCreateOperations.ToListAsync());
    }

    [SqlServerFact]
    public async Task Hierarchy_RejectsSelfOrphanCycleAndDirectSqlCycle()
    {
        using var op = await Login("operator.test");
        await Csrf(op);
        var parent = (await Read(op, contractorId))!;
        using var created = await op.PostAsJsonAsync(ContractorsPath(), NewContractor("CHILD") with { ParentId = parent.Id });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var child = (await created.Content.ReadFromJsonAsync<ContractorResponse>())!;
        Assert.Equal(parent.Id, child.ParentId);
        Assert.Equal(HttpStatusCode.BadRequest, (await op.PutAsJsonAsync($"{ContractorsPath()}/{parent.Id}", Edit(parent, parent.Name) with { ParentId = parent.Id })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await op.PutAsJsonAsync($"{ContractorsPath()}/{parent.Id}", Edit(parent, parent.Name) with { ParentId = int.MaxValue })).StatusCode);
        using var cycle = await op.PutAsJsonAsync($"{ContractorsPath()}/{parent.Id}", Edit(parent, parent.Name) with { ParentId = child.Id });
        Assert.Equal(HttpStatusCode.BadRequest, cycle.StatusCode);
        Assert.Contains("hierarchy_cycle", await cycle.Content.ReadAsStringAsync());
        await using var db = Company(false, companyA);
        var error = await Assert.ThrowsAsync<SqlException>(() => db.Database.ExecuteSqlInterpolatedAsync($"UPDATE core.Contractors SET ParentId={child.Id} WHERE Id={parent.Id};"));
        Assert.Equal(51001, error.Number);
        Assert.Null((await Read(op, parent.Id))!.ParentId);
        Assert.Single(await db.ContractorChanges.ToListAsync()); // Tylko skuteczne utworzenie dziecka.
    }

    [SqlServerFact]
    public async Task Hierarchy_NoDataInheritanceAndDuplicateTaxIdsAreStoredWithoutClaimingValidation()
    {
        await using var db = Company(false, companyA);
        var parent = new Contractor("PARENT", "Fikcyjny podmiot nadrzędny", "DE", "TEST-TAX", "TEST-VAT", "TEST-IDWEW");
        db.Contractors.Add(parent);
        await db.SaveChangesAsync();
        var child = new Contractor("CHILD", "Fikcyjna jednostka", "PL", parentId: parent.Id);
        var duplicate = new Contractor("DUPLICATE", "Fikcyjny duplikat", "PL", "TEST-TAX", "TEST-VAT");
        db.Contractors.AddRange(child, duplicate);
        db.ContractorAddresses.Add(new ContractorAddress(parent.Id, 1) { Street = "Fikcyjna", IsDefault = true });
        db.ContractorBankAccounts.Add(new ContractorBankAccount(parent.Id, "DEMO-NUMER", "PLN"));
        db.ContractorContacts.Add(new ContractorContact(parent.Id, "Fikcyjny magazyn") { Phone = "TEST", Email = "demo@example.invalid", Street = "Fikcyjna" });
        await db.SaveChangesAsync();
        parent.Rename("Fikcyjna zmiana nadrzędnego");
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        var saved = await db.Contractors.SingleAsync(x => x.Id == child.Id);
        Assert.Null(saved.TaxId);
        Assert.Null(saved.VatEuId);
        Assert.Null(saved.KsefIdWew);
        Assert.Equal("PL", saved.CountryCode);
        Assert.Equal("Fikcyjna jednostka", saved.Name);
        Assert.False(await db.ContractorAddresses.AnyAsync(x => x.ContractorId == child.Id));
        Assert.False(await db.ContractorBankAccounts.AnyAsync(x => x.ContractorId == child.Id));
        Assert.False(await db.ContractorContacts.AnyAsync(x => x.ContractorId == child.Id));
        Assert.Equal(2, await db.Contractors.CountAsync(x => x.TaxId == "TEST-TAX" && x.VatEuId == "TEST-VAT"));
        using var op = await Login("operator.test");
        await Csrf(op);
        // Nieznane pola starego modelu są nadal odrzucane.
        using var write = await op.PostAsJsonAsync(ContractorsPath(), new
        { id = 0, code = "DENIED", name = "Fikcyjny", countryCode = "PL", isActive = true, operationId = Guid.NewGuid(), taxIdentifier = "TEST" });
        Assert.Equal(HttpStatusCode.BadRequest, write.StatusCode);
        var current = (await Read(op, parent.Id))!;
        await GrantRegistryRights();
        var edit = Edit(current, "Fikcyjna edycja z zachowaniem identyfikatorów");
        var preview = await Preview(op, edit);
        Assert.Equal(HttpStatusCode.OK, (await op.PutAsJsonAsync($"{ContractorsPath()}/{parent.Id}", edit with { AcceptWarning = true, WarningHash = preview.WarningHash })).StatusCode);
        Assert.Equal("TEST-TAX", (await Read(op, parent.Id))!.TaxId);
    }

    [SqlServerFact]
    public async Task AddressTypes_AreOpenAndPreferredAddressIsUniqueOnlyPerType()
    {
        await using var db = Company(false, companyA);
        Assert.Equal(new[] { "Adres rejestrowy", "Adres korespondencyjny" }, await db.AddressTypes.OrderBy(x => x.Id).Select(x => x.Name).ToArrayAsync());
        db.AddressTypes.Add(new AddressType("Fikcyjny adres dostawy"));
        db.ContractorAddresses.AddRange(new ContractorAddress(contractorId, 1) { IsDefault = true }, new ContractorAddress(contractorId, 1), new ContractorAddress(contractorId, 2) { IsDefault = true });
        await db.SaveChangesAsync();
        Assert.Equal(3, await db.ContractorAddresses.CountAsync());
        db.ContractorAddresses.Add(new ContractorAddress(contractorId, 1) { IsDefault = true });
        var error = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        Assert.Contains(Assert.IsType<SqlException>(error.InnerException).Number, new[] { 2601, 2627 });
    }

    [SqlServerFact]
    public async Task BankDefaults_UniqueForPlnEurAndNullAndNumbersCanRepeat()
    {
        await using var db = Company(false, companyA);
        foreach (var currency in new string?[] { "PLN", "EUR", null })
        {
            db.ContractorBankAccounts.AddRange(new ContractorBankAccount(contractorId, "DEMO-POWTORZONY", currency) { IsDefault = true }, new ContractorBankAccount(contractorId, "DEMO-POWTORZONY", currency));
            await db.SaveChangesAsync();
            db.ContractorBankAccounts.Add(new ContractorBankAccount(contractorId, "DEMO-POWTORZONY", currency) { IsDefault = true });
            var error = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
            Assert.Contains(Assert.IsType<SqlException>(error.InnerException).Number, new[] { 2601, 2627 });
            db.ChangeTracker.Clear();
        }
        Assert.Equal(6, await db.ContractorBankAccounts.CountAsync());
        Assert.Equal(3, await db.ContractorBankAccounts.CountAsync(x => x.IsDefault));
    }

    [SqlServerFact]
    public async Task BankDefaultSwitch_IsAtomicAuditedAndChecksRowVersion()
    {
        await using var db = Company(false, companyA);
        var old = new ContractorBankAccount(contractorId, "DEMO-OLD") { IsDefault = true };
        var next = new ContractorBankAccount(contractorId, "DEMO-NEXT");
        db.ContractorBankAccounts.AddRange(old, next);
        await db.SaveChangesAsync();
        var version = next.RowVersion.ToArray();
        using var scope = application!.Services.CreateScope();
        var issuer = (await scope.ServiceProvider.GetRequiredService<SystemDbContext>().InstallationIdentities.SingleAsync()).Id;
        await db.Database.ExecuteSqlRawAsync("ALTER TABLE audit.ContractorChanges ADD CONSTRAINT CK_BankFailure CHECK (ActorName <> N'operator.test');");
        try
        {
            await Assert.ThrowsAsync<DbUpdateException>(() => ContractorBankAccountDefaults.SetDefault(db, next.Id, version, operatorId, issuer, "operator.test", clock.GetUtcNow(), "test"));
            db.ChangeTracker.Clear();
            Assert.True((await db.ContractorBankAccounts.SingleAsync(x => x.Id == old.Id)).IsDefault);
            Assert.False((await db.ContractorBankAccounts.SingleAsync(x => x.Id == next.Id)).IsDefault);
            Assert.Empty(await db.ContractorChanges.ToListAsync());
        }
        finally { await db.Database.ExecuteSqlRawAsync("ALTER TABLE audit.ContractorChanges DROP CONSTRAINT CK_BankFailure;"); }
        db.ChangeTracker.Clear();
        await ContractorBankAccountDefaults.SetDefault(db, next.Id, version, operatorId, issuer, "operator.test", clock.GetUtcNow(), "test");
        Assert.False((await db.ContractorBankAccounts.SingleAsync(x => x.Id == old.Id)).IsDefault);
        Assert.True((await db.ContractorBankAccounts.SingleAsync(x => x.Id == next.Id)).IsDefault);
        var audit = Assert.Single(await db.ContractorChanges.ToListAsync());
        Assert.Contains("DEMO-NEXT", audit.BeforeJson);
        await using var stale = Company(false, companyA);
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => ContractorBankAccountDefaults.SetDefault(stale, next.Id, version, operatorId, issuer, "operator.test", clock.GetUtcNow(), "stale"));
    }

    [SqlServerFact]
    public async Task PhysicalSqlSchema_KeysColumnsRelationsAndIndexesMatchModel()
    {
        await using var db = Company(false, companyA);
        await db.Database.OpenConnectionAsync();
        using var command = db.Database.GetDbConnection().CreateCommand();
        command.CommandText = """
            SELECT s.name+'.'+t.name,c.name,ty.name,c.max_length,c.is_nullable,c.is_identity
            FROM sys.tables t JOIN sys.schemas s ON s.schema_id=t.schema_id
            JOIN sys.columns c ON c.object_id=t.object_id JOIN sys.types ty ON ty.user_type_id=c.user_type_id
            ORDER BY s.name,t.name,c.column_id;
            """;
        var columns = new Dictionary<string, List<(string Name, string Type, short Length, bool Nullable, bool Identity)>>();
        await using (var reader = await command.ExecuteReaderAsync())
            while (await reader.ReadAsync())
            {
                var table = reader.GetString(0);
                if (!columns.ContainsKey(table)) columns[table] = [];
                columns[table].Add((reader.GetString(1), reader.GetString(2), reader.GetInt16(3), reader.GetBoolean(4), reader.GetBoolean(5)));
            }
        Assert.Equal(14, columns["core.ContractorAddresses"].Count);
        Assert.Equal(12, columns["core.ContractorBankAccounts"].Count);
        Assert.Equal(14, columns["core.ContractorContacts"].Count);
        Assert.Equal(("Id", "int", (short)4, false, true), columns["core.Contractors"].Single(x => x.Name == "Id"));
        Assert.Equal(("Bic", "varchar", (short)11, true, false), columns["core.ContractorBankAccounts"].Single(x => x.Name == "Bic"));
        Assert.Equal(("TaxId", "nvarchar", (short)200, true, false), columns["core.Contractors"].Single(x => x.Name == "TaxId"));
        Assert.Equal(("VatEuId", "nvarchar", (short)200, true, false), columns["core.Contractors"].Single(x => x.Name == "VatEuId"));
        Assert.Equal(new[] {
            ("Id", "int", (short)4, false, true), ("ContractorId", "int", (short)4, false, false),
            ("AddressTypeId", "int", (short)4, false, false), ("Name", "nvarchar", (short)200, true, false),
            ("Street", "nvarchar", (short)400, true, false), ("BuildingNo", "nvarchar", (short)40, true, false),
            ("ApartmentNo", "nvarchar", (short)40, true, false), ("PostalCode", "nvarchar", (short)40, true, false),
            ("City", "nvarchar", (short)300, true, false), ("Region", "nvarchar", (short)200, true, false),
            ("CountryCode", "char", (short)2, true, false), ("IsDefault", "bit", (short)1, false, false),
            ("IsActive", "bit", (short)1, false, false), ("RowVersion", "timestamp", (short)8, false, false)
        }, columns["core.ContractorAddresses"]);
        Assert.Equal(new[] {
            ("Id", "int", (short)4, false, true), ("ContractorId", "int", (short)4, false, false),
            ("AccountNumber", "nvarchar", (short)200, false, false), ("BankName", "nvarchar", (short)400, true, false),
            ("Bic", "varchar", (short)11, true, false), ("CurrencyCode", "char", (short)3, true, false),
            ("AccountHolderName", "nvarchar", (short)512, true, false), ("Name", "nvarchar", (short)200, true, false),
            ("IsDefault", "bit", (short)1, false, false), ("IsActive", "bit", (short)1, false, false),
            ("Notes", "nvarchar", (short)-1, true, false), ("RowVersion", "timestamp", (short)8, false, false)
        }, columns["core.ContractorBankAccounts"]);
        foreach (var table in columns.Where(x => x.Key != "core.CompanyProfile")) Assert.DoesNotContain(table.Value, x => x.Name == "CompanyId");
        Assert.DoesNotContain("ref.TaxIdentifierSchemes", columns.Keys);
        Assert.Contains("audit.ExternalVerifications", columns.Keys);
        command.CommandText = "SELECT COUNT(*) FROM sys.triggers WHERE name='TR_Contractors_Hierarchy' AND is_disabled=0;";
        Assert.Equal(1, Convert.ToInt32(await command.ExecuteScalarAsync()));
        command.CommandText = "SELECT filter_definition FROM sys.indexes WHERE object_id=OBJECT_ID('core.ContractorBankAccounts') AND is_unique=1 AND has_filter=1;";
        var filter = (string)(await command.ExecuteScalarAsync())!;
        Assert.Contains("IsDefault", filter);
        Assert.DoesNotContain("IS NOT NULL", filter, StringComparison.OrdinalIgnoreCase);
        command.CommandText = "SELECT COUNT(*) FROM sys.foreign_keys WHERE is_disabled=1 OR is_not_trusted=1;";
        Assert.Equal(0, Convert.ToInt32(await command.ExecuteScalarAsync()));
    }
}
