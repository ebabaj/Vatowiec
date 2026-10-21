using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Vatowiec.Core.Application;
using Vatowiec.Core.Domain;
using Vatowiec.Core.Infrastructure.Persistence;

namespace Vatowiec.Tests;

// Tylko dedykowane, wcześniej zmigrowane i puste bazy *_Test. Bez CREATE/DROP/Migrate.
[Trait("Category", "SqlIntegration")]
[Collection("LocalSql")]
public sealed class SqlServerPersistenceTests
{
    [SqlServerFact]
    public async Task SystemSchema_ContainsOnlyCatalogAndItsOwnMigrationHistory()
    {
        await using var session = await Session.Open("Vatowiec_System_Test", "ConnectionStrings__VatowiecSystemTest");
        await using var context = session.System();
        Assert.Equal(2, (await context.Database.GetAppliedMigrationsAsync()).Count());
        Assert.EndsWith("_IdentityAndCompanyAccess", (await context.Database.GetAppliedMigrationsAsync()).Last());
        Assert.Empty(await context.Database.GetPendingMigrationsAsync());
        using var command = new SqlCommand("SELECT COUNT(*) FROM sys.tables WHERE schema_id = SCHEMA_ID('core')", session.Connection, session.Transaction);
        Assert.Equal(2, (int)(await command.ExecuteScalarAsync())!);
        context.Companies.Add(new Company(Guid.NewGuid(), "Katalog testowy"));
        await context.SaveChangesAsync();
    }

    [SqlServerFact]
    public async Task SeparateCompanyDatabases_KeepSameIdentifiersAndCodesIndependent()
    {
        await using var a = await Session.CompanyA();
        await using var b = await Session.CompanyB();
        var idA = Guid.NewGuid();
        var idB = Guid.NewGuid();
        var contractorId = Guid.NewGuid();
        await a.Seed(idA, contractorId);
        await b.Seed(idB, contractorId);
        await using var first = a.Company(idA);
        await using var second = b.Company(idB);
        Assert.EndsWith("_InitialCompany", Assert.Single(await first.Database.GetAppliedMigrationsAsync()));
        Assert.Empty(await first.Database.GetPendingMigrationsAsync());
        Assert.Equal(idA, (await first.Contractors.SingleAsync()).CompanyId);
        Assert.Equal(idB, (await second.Contractors.SingleAsync()).CompanyId);
        Assert.Equal(idA, (await first.CompanyProfiles.SingleAsync()).CompanyId);
        Assert.Equal(idB, (await second.CompanyProfiles.SingleAsync()).CompanyId);
    }

    [SqlServerFact]
    public async Task MigrationHistories_ArePhysicallyIndependent()
    {
        await using var a = await Session.CompanyA();
        await using var b = await Session.CompanyB();
        await using var s = await Session.Open("Vatowiec_System_Test", "ConnectionStrings__VatowiecSystemTest");
        await using var first = a.Company(null);
        await using var second = b.Company(null);
        await using var system = s.System();
        using var command = new SqlCommand("DELETE FROM dbo.__EFMigrationsHistory;", a.Connection, a.Transaction);
        Assert.Equal(1, await command.ExecuteNonQueryAsync());
        Assert.Empty(await first.Database.GetAppliedMigrationsAsync());
        Assert.Single(await first.Database.GetPendingMigrationsAsync());
        Assert.EndsWith("_InitialCompany", Assert.Single(await second.Database.GetAppliedMigrationsAsync()));
        Assert.Empty(await second.Database.GetPendingMigrationsAsync());
        Assert.Equal(2, (await system.Database.GetAppliedMigrationsAsync()).Count());
        Assert.EndsWith("_IdentityAndCompanyAccess", (await system.Database.GetAppliedMigrationsAsync()).Last());
        Assert.Empty(await system.Database.GetPendingMigrationsAsync());
        // Usunięcie wpisu historii w A jest wycofywane przez Session.DisposeAsync.
    }

    [SqlServerFact]
    public async Task LocalForeignKey_RejectsContractorOfAnotherCompanyThroughRawSql()
    {
        await using var session = await Session.CompanyA();
        await session.Seed(Guid.NewGuid(), Guid.NewGuid());
        using var command = new SqlCommand("""
            INSERT core.Contractors (CompanyId, Id, Code, Name, CountryCode, IsActive, CreatedAtUtc, UpdatedAtUtc)
            VALUES (@foreignCompany, NEWID(), N'FOREIGN', N'Fikcyjny obcy kontrahent', N'PL', 1, SYSUTCDATETIME(), SYSUTCDATETIME());
            """, session.Connection, session.Transaction);
        command.Parameters.AddWithValue("@foreignCompany", Guid.NewGuid());
        var error = await Assert.ThrowsAsync<SqlException>(() => command.ExecuteNonQueryAsync());
        Assert.Equal(547, error.Number);
        Assert.Contains("FK_Contractors_CompanyProfile_CompanyId", error.Message);
    }

    [SqlServerFact]
    public async Task SystemCatalog_EnforcesCompanyForeignKeyAndUniqueDatabaseLocation()
    {
        await using var session = await Session.Open("Vatowiec_System_Test", "ConnectionStrings__VatowiecSystemTest");
        await using var context = session.System();
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        context.CompanyDatabases.Add(new CompanyDatabaseRegistration(a, "test-sql", "test-company", "TestWindows"));
        var orphan = await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
        Assert.Equal(547, Assert.IsType<SqlException>(orphan.InnerException).Number);
        context.ChangeTracker.Clear();
        context.Companies.AddRange(new Company(a, "Firma testowa A"), new Company(b, "Firma testowa B"));
        context.CompanyDatabases.Add(new CompanyDatabaseRegistration(a, "test-sql", "test-company", "TestWindows"));
        await context.SaveChangesAsync();
        context.CompanyDatabases.Add(new CompanyDatabaseRegistration(b, "test-sql", "test-company", "TestWindows"));
        var duplicate = await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
        Assert.Contains(Assert.IsType<SqlException>(duplicate.InnerException).Number, new[] { 2601, 2627 });
    }

    [SqlServerFact]
    public async Task MissingContext_ReturnsNoDataAndRejectsWrites()
    {
        await using var session = await Session.CompanyA();
        var id = Guid.NewGuid();
        await session.Seed(id, Guid.NewGuid());
        await using var context = session.Company(null);
        Assert.Empty(await context.CompanyProfiles.ToListAsync());
        Assert.Empty(await context.Contractors.ToListAsync());
        context.Contractors.Add(new Contractor(id, Guid.NewGuid(), "NEW", "Test", "PL"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => context.SaveChangesAsync());
    }

    [SqlServerFact]
    public async Task WrongContext_RejectsForeignInsertUpdateAndDelete()
    {
        await using var session = await Session.CompanyA();
        var id = Guid.NewGuid();
        await session.Seed(id, Guid.NewGuid());
        await using var context = session.Company(Guid.NewGuid());
        Assert.Empty(await context.Contractors.ToListAsync());
        context.Contractors.Add(new Contractor(id, Guid.NewGuid(), "NEW", "Test", "PL"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => context.SaveChangesAsync());
        context.ChangeTracker.Clear();
        var foreign = await context.Contractors.IgnoreQueryFilters().SingleAsync();
        foreign.Rename("Niedozwolona zmiana");
        await Assert.ThrowsAsync<InvalidOperationException>(() => context.SaveChangesAsync());
        context.Contractors.Remove(foreign);
        await Assert.ThrowsAsync<InvalidOperationException>(() => context.SaveChangesAsync());
    }

    [SqlServerFact]
    public async Task DuplicateCode_RollsBackWholeSave()
    {
        await using var session = await Session.CompanyA();
        var id = Guid.NewGuid();
        await session.Seed(id, Guid.NewGuid());
        await using (var context = session.Company(id))
        {
            context.Contractors.Add(new Contractor(id, Guid.NewGuid(), "UNIQUE", "Test", "PL"));
            context.Contractors.Add(new Contractor(id, Guid.NewGuid(), "KLIENT", "Duplicate", "PL"));
            await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
        }
        await using var verify = session.Company(id);
        Assert.Single(await verify.Contractors.ToListAsync());
    }

    [SqlServerFact]
    public async Task LocalProfileForeignKey_RejectsOrphanAndProfileDeletion()
    {
        await using var session = await Session.CompanyA();
        var id = Guid.NewGuid();
        await using (var context = session.Company(id))
        {
            context.Contractors.Add(new Contractor(id, Guid.NewGuid(), "ORPHAN", "Test", "PL"));
            await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
        }
        await session.Seed(id, Guid.NewGuid());
        await using var seeded = session.Company(id);
        seeded.CompanyProfiles.Remove(await seeded.CompanyProfiles.SingleAsync());
        await Assert.ThrowsAsync<DbUpdateException>(() => seeded.SaveChangesAsync());
    }

    [SqlServerFact]
    public async Task SingletonProfile_RejectsAnotherCompanyEvenThroughRawSql()
    {
        await using var session = await Session.CompanyA();
        await session.Seed(Guid.NewGuid(), Guid.NewGuid());
        using var command = new SqlCommand("""
            INSERT core.CompanyProfile (SingletonKey, CompanyId, Name, CreatedAtUtc, UpdatedAtUtc)
            VALUES (2, NEWID(), N'Druga firma', SYSUTCDATETIME(), SYSUTCDATETIME());
            """, session.Connection, session.Transaction);
        var error = await Assert.ThrowsAsync<SqlException>(() => command.ExecuteNonQueryAsync());
        Assert.Equal(547, error.Number);
    }

    [SqlServerFact]
    public async Task RowVersion_RejectsStaleProfileAndContractorUpdates()
    {
        await using var session = await Session.CompanyA();
        var id = Guid.NewGuid();
        await session.Seed(id, Guid.NewGuid());
        await using var first = session.Company(id);
        await using var stale = session.Company(id);
        var profile = await first.CompanyProfiles.SingleAsync();
        var staleProfile = await stale.CompanyProfiles.SingleAsync();
        profile.Rename("Nowa nazwa");
        await first.SaveChangesAsync();
        staleProfile.Rename("Nieaktualna nazwa");
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => stale.SaveChangesAsync());
        stale.ChangeTracker.Clear();
        var contractor = await first.Contractors.SingleAsync();
        var staleContractor = await stale.Contractors.SingleAsync();
        contractor.Rename("Nowa nazwa");
        await first.SaveChangesAsync();
        staleContractor.Rename("Nieaktualna nazwa");
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => stale.SaveChangesAsync());
    }

    [SqlServerFact]
    public async Task MetadataAndRollback_KeepDedicatedDatabaseEmpty()
    {
        var id = Guid.NewGuid();
        await using (var session = await Session.CompanyA())
        {
            await session.Seed(id, Guid.NewGuid());
            await using var context = session.Company(id);
            var contractor = await context.Contractors.SingleAsync();
            Assert.Equal(8, contractor.RowVersion.Length);
            Assert.NotEqual(default, contractor.CreatedAtUtc);
            Assert.Equal(contractor.CreatedAtUtc, contractor.UpdatedAtUtc);
        }
        await using var check = await Session.CompanyA();
        await using var verify = check.Company(id);
        Assert.Empty(await verify.Contractors.ToListAsync());
        Assert.Empty(await verify.CompanyProfiles.ToListAsync());
    }

    private sealed class Session(SqlConnection connection, SqlTransaction transaction) : IAsyncDisposable
    {
        public SqlConnection Connection { get; } = connection;
        public SqlTransaction Transaction { get; } = transaction;

        public static Task<Session> CompanyA() => Open("Vatowiec_Company_A_Test", "ConnectionStrings__VatowiecCompanyATest");
        public static Task<Session> CompanyB() => Open("Vatowiec_Company_B_Test", "ConnectionStrings__VatowiecCompanyBTest");

        public static async Task<Session> Open(string database, string variable)
        {
            var value = Environment.GetEnvironmentVariable(variable);
            Assert.False(string.IsNullOrWhiteSpace(value), $"Wymagana konfiguracja {variable}.");
            var builder = new SqlConnectionStringBuilder(value);
            Assert.Equal(@"localhost\SQLEXPRESS", builder.DataSource, ignoreCase: true);
            Assert.Equal(database, builder.InitialCatalog);
            Assert.True(builder.IntegratedSecurity);
            if (database == "Vatowiec_System_Test") DatabaseConnectionPolicy.ValidateSystem(value, true);
            else DatabaseConnectionPolicy.ValidateCompany(value!, true);
            var connection = new SqlConnection(value);
            try
            {
                await connection.OpenAsync();
                return new Session(connection, (SqlTransaction)await connection.BeginTransactionAsync());
            }
            catch
            {
                await connection.DisposeAsync();
                throw;
            }
        }

        public CompanyDbContext Company(Guid? id)
        {
            var context = new CompanyDbContext(new DbContextOptionsBuilder<CompanyDbContext>()
                .UseSqlServer(Connection).Options, new CompanyContext(id));
            context.Database.UseTransaction(Transaction);
            return context;
        }

        public SystemDbContext System()
        {
            var context = new SystemDbContext(new DbContextOptionsBuilder<SystemDbContext>()
                .UseSqlServer(Connection).Options);
            context.Database.UseTransaction(Transaction);
            return context;
        }

        public async Task Seed(Guid companyId, Guid contractorId)
        {
            await using var context = Company(companyId);
            Assert.False(await context.CompanyProfiles.IgnoreQueryFilters().AnyAsync(), "Baza *_Test musi być pusta.");
            context.CompanyProfiles.Add(new CompanyProfile(companyId, "Firma testowa — dane fikcyjne"));
            context.Contractors.Add(new Contractor(companyId, contractorId, "KLIENT", "Kontrahent testowy", "PL"));
            await context.SaveChangesAsync();
        }

        public async ValueTask DisposeAsync()
        {
            try { await Transaction.RollbackAsync(); }
            finally
            {
                await Transaction.DisposeAsync();
                await Connection.DisposeAsync();
            }
        }
    }
}
