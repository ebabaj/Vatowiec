using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Vatowiec.Core.Application;
using Vatowiec.Core.Domain;
using Vatowiec.Core.Infrastructure.Persistence;

namespace Vatowiec.Tests;

public sealed class SqlServerPersistenceTests
{
    [SqlServerFact]
    public async Task Schema_MigrationAndSqlConstraintsArePresent()
    {
        await InTransaction(async (connection, transaction) =>
        {
            await using var context = CreateContext(connection, transaction, null);
            Assert.Single(await context.Database.GetAppliedMigrationsAsync());
            Assert.Empty(await context.Database.GetPendingMigrationsAsync());
            Assert.False(context.Database.HasPendingModelChanges());

            using var command = new SqlCommand("""
                SELECT
                  (SELECT COUNT(*) FROM sys.tables WHERE schema_id = SCHEMA_ID('core') AND name IN ('Companies','Contractors')),
                  (SELECT COUNT(*) FROM sys.key_constraints WHERE schema_id = SCHEMA_ID('core') AND type = 'PK'),
                  (SELECT COUNT(*) FROM sys.foreign_keys WHERE name = 'FK_Contractors_Companies_CompanyId'
                    AND delete_referential_action = 0 AND is_disabled = 0 AND is_not_trusted = 0),
                  (SELECT COUNT(*) FROM sys.indexes WHERE object_id = OBJECT_ID('core.Contractors')
                    AND name = 'IX_Contractors_CompanyId_Code' AND is_unique = 1),
                  (SELECT COUNT(*) FROM sys.columns WHERE object_id IN (OBJECT_ID('core.Companies'), OBJECT_ID('core.Contractors'))
                    AND name = 'RowVersion' AND system_type_id = 189 AND is_nullable = 0),
                  (SELECT COUNT(*) FROM sys.check_constraints WHERE schema_id = SCHEMA_ID('core')
                    AND is_disabled = 0 AND is_not_trusted = 0);
                """, connection, transaction);
            await using var reader = await command.ExecuteReaderAsync();
            Assert.True(await reader.ReadAsync());
            Assert.Equal(2, reader.GetInt32(0));
            Assert.Equal(2, reader.GetInt32(1));
            Assert.Equal(1, reader.GetInt32(2));
            Assert.Equal(1, reader.GetInt32(3));
            Assert.Equal(2, reader.GetInt32(4));
            Assert.Equal(8, reader.GetInt32(5));
        });
    }

    [SqlServerFact]
    public async Task RoundTrip_FiltersCompaniesAndContractorsAndGeneratesMetadata()
    {
        await InTransaction(async (connection, transaction) =>
        {
            var a = Guid.NewGuid();
            var b = Guid.NewGuid();
            var contractorId = Guid.NewGuid();
            await Seed(connection, transaction, a, contractorId);
            await Seed(connection, transaction, b, contractorId);
            await using var first = CreateContext(connection, transaction, a);
            await using var second = CreateContext(connection, transaction, b);
            Assert.Equal(a, (await first.Companies.SingleAsync()).Id);
            Assert.Equal(b, (await second.Companies.SingleAsync()).Id);
            var contractor = await first.Contractors.SingleAsync();
            Assert.Equal(a, contractor.CompanyId);
            Assert.Equal(b, (await second.Contractors.SingleAsync()).CompanyId);
            Assert.Equal("KLIENT", contractor.Code);
            Assert.Equal("PL", contractor.CountryCode);
            Assert.Equal(8, contractor.RowVersion.Length);
            Assert.NotEqual(default, contractor.CreatedAtUtc);
            Assert.Equal(contractor.CreatedAtUtc, contractor.UpdatedAtUtc);
            var created = contractor.CreatedAtUtc;
            var rowVersion = contractor.RowVersion.ToArray();
            contractor.Rename("Kontrahent po zmianie — dane fikcyjne");
            await first.SaveChangesAsync();
            Assert.Equal(created, contractor.CreatedAtUtc);
            Assert.True(contractor.UpdatedAtUtc >= created);
            Assert.False(rowVersion.SequenceEqual(contractor.RowVersion));
        });
    }

    [SqlServerFact]
    public async Task MissingCompanyContext_ReturnsNoCompanyDataAndRefusesWrites()
    {
        await InTransaction(async (connection, transaction) =>
        {
            var companyId = Guid.NewGuid();
            await Seed(connection, transaction, companyId, Guid.NewGuid());
            await using var context = CreateContext(connection, transaction, null);
            Assert.Empty(await context.Companies.ToListAsync());
            Assert.Empty(await context.Contractors.ToListAsync());
            context.Contractors.Add(new Contractor(companyId, Guid.NewGuid(), "NOWY", "Test", "PL"));
            await Assert.ThrowsAsync<InvalidOperationException>(() => context.SaveChangesAsync());
        });
    }

    [SqlServerFact]
    public async Task WrongCompany_CannotInsertUpdateOrDeleteAnotherCompanyContractor()
    {
        await InTransaction(async (connection, transaction) =>
        {
            var a = Guid.NewGuid();
            var b = Guid.NewGuid();
            var id = Guid.NewGuid();
            await Seed(connection, transaction, b, id);
            await using (var context = CreateContext(connection, transaction, a))
            {
                context.Contractors.Add(new Contractor(b, Guid.NewGuid(), "NOWY", "Test", "PL"));
                await Assert.ThrowsAsync<InvalidOperationException>(() => context.SaveChangesAsync());
            }

            // Nawet rekord odczytany przez zaufany kod poza filtrem nie może być zapisany do obcej firmy.
            await using (var context = CreateContext(connection, transaction, a))
            {
                var foreign = await context.Contractors.IgnoreQueryFilters()
                    .SingleAsync(x => x.CompanyId == b && x.Id == id);
                foreign.Rename("Niedozwolona zmiana");
                await Assert.ThrowsAsync<InvalidOperationException>(() => context.SaveChangesAsync());
            }

            await using (var context = CreateContext(connection, transaction, a))
            {
                var foreign = await context.Contractors.IgnoreQueryFilters()
                    .SingleAsync(x => x.CompanyId == b && x.Id == id);
                context.Contractors.Remove(foreign);
                await Assert.ThrowsAsync<InvalidOperationException>(() => context.SaveChangesAsync());
            }
        });
    }

    [SqlServerFact]
    public async Task UniqueContractorCode_IsPerCompanyAndFailedSaveIsAtomic()
    {
        await InTransaction(async (connection, transaction) =>
        {
            var a = Guid.NewGuid();
            var b = Guid.NewGuid();
            await Seed(connection, transaction, a, Guid.NewGuid());
            await Seed(connection, transaction, b, Guid.NewGuid()); // ten sam kod w drugiej firmie jest legalny
            await using (var context = CreateContext(connection, transaction, a))
            {
                context.Contractors.Add(new Contractor(a, Guid.NewGuid(), "UNIKALNY", "Test", "PL"));
                context.Contractors.Add(new Contractor(a, Guid.NewGuid(), "KLIENT", "Duplikat", "PL"));
                var exception = await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
                Assert.Contains(((SqlException)exception.InnerException!).Number, new[] { 2601, 2627 });
            }

            await using var verify = CreateContext(connection, transaction, a);
            Assert.Single(await verify.Contractors.ToListAsync());
            Assert.False(await verify.Contractors.AnyAsync(x => x.Code == "UNIKALNY"));
        });
    }

    [SqlServerFact]
    public async Task ForeignKey_RejectsOrphanContractorAndCompanyCascadeDelete()
    {
        await InTransaction(async (connection, transaction) =>
        {
            var companyId = Guid.NewGuid();
            await using (var context = CreateContext(connection, transaction, companyId))
            {
                context.Contractors.Add(new Contractor(companyId, Guid.NewGuid(), "SIEROTA", "Test", "PL"));
                var exception = await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
                Assert.Equal(547, ((SqlException)exception.InnerException!).Number);
            }

            await Seed(connection, transaction, companyId, Guid.NewGuid());
            await using (var context = CreateContext(connection, transaction, companyId))
            {
                context.Companies.Remove(await context.Companies.SingleAsync());
                var exception = await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
                Assert.Equal(547, ((SqlException)exception.InnerException!).Number);
            }
        });
    }

    [SqlServerFact]
    public async Task RowVersion_RejectsStaleCompanyAndContractorUpdates()
    {
        await InTransaction(async (connection, transaction) =>
        {
            var companyId = Guid.NewGuid();
            await Seed(connection, transaction, companyId, Guid.NewGuid());
            await using var first = CreateContext(connection, transaction, companyId);
            await using var stale = CreateContext(connection, transaction, companyId);
            var company = await first.Companies.SingleAsync();
            var staleCompany = await stale.Companies.SingleAsync();
            company.Rename("Zmiana pierwsza");
            await first.SaveChangesAsync();
            staleCompany.Rename("Zmiana nieaktualna");
            await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => stale.SaveChangesAsync());
            stale.ChangeTracker.Clear();

            var contractor = await first.Contractors.SingleAsync();
            var staleContractor = await stale.Contractors.SingleAsync();
            contractor.Rename("Zmiana pierwsza");
            await first.SaveChangesAsync();
            staleContractor.Rename("Zmiana nieaktualna");
            await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => stale.SaveChangesAsync());
        });
    }

    [SqlServerFact]
    public async Task SqlChecks_RejectBlankNameAndEmptyIdentity()
    {
        await InTransaction(async (connection, transaction) =>
        {
            var companyId = Guid.NewGuid();
            await Seed(connection, transaction, companyId, Guid.NewGuid());
            using var command = new SqlCommand("UPDATE core.Companies SET Name = N' ' WHERE Id = @id", connection, transaction);
            command.Parameters.AddWithValue("@id", companyId);
            var error = await Assert.ThrowsAsync<SqlException>(() => command.ExecuteNonQueryAsync());
            Assert.Equal(547, error.Number);
            command.CommandText = """
                INSERT core.Companies (Id, Name, IsActive, CreatedAtUtc, UpdatedAtUtc)
                VALUES ('00000000-0000-0000-0000-000000000000', N'Test', 1, SYSUTCDATETIME(), SYSUTCDATETIME());
                """;
            error = await Assert.ThrowsAsync<SqlException>(() => command.ExecuteNonQueryAsync());
            Assert.Equal(547, error.Number);
        });
    }

    [SqlServerFact]
    public async Task ExplicitRollback_LeavesNoTestRecords()
    {
        var companyId = Guid.NewGuid();
        await InTransaction(async (connection, transaction) =>
        {
            await Seed(connection, transaction, companyId, Guid.NewGuid());
        });
        await InTransaction(async (connection, transaction) =>
        {
            await using var context = CreateContext(connection, transaction, companyId);
            Assert.Empty(await context.Companies.ToListAsync());
            Assert.Empty(await context.Contractors.ToListAsync());
        });
    }

    private static async Task Seed(SqlConnection connection, SqlTransaction transaction, Guid companyId, Guid contractorId)
    {
        await using var context = CreateContext(connection, transaction, companyId);
        context.Companies.Add(new Company(companyId, "Firma testowa — dane fikcyjne"));
        context.Contractors.Add(new Contractor(companyId, contractorId, "KLIENT", "Kontrahent testowy", "PL"));
        await context.SaveChangesAsync();
    }

    private static CoreDbContext CreateContext(SqlConnection connection, SqlTransaction transaction, Guid? companyId)
    {
        var options = new DbContextOptionsBuilder<CoreDbContext>().UseSqlServer(connection).Options;
        var context = new CoreDbContext(options, new CompanyContext(companyId));
        context.Database.UseTransaction(transaction);
        return context;
    }

    private static async Task InTransaction(Func<SqlConnection, SqlTransaction, Task> test)
    {
        if (Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT") != "Development")
        {
            throw new InvalidOperationException("Test SQL wymaga DOTNET_ENVIRONMENT=Development.");
        }

        var connectionString = DevelopmentDatabase.Validate(Environment.GetEnvironmentVariable("ConnectionStrings__Vatowiec"));
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        Assert.Equal("Vatowiec_Dev", connection.Database);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync();
        try
        {
            await test(connection, transaction);
        }
        finally
        {
            await transaction.RollbackAsync();
        }
    }
}
