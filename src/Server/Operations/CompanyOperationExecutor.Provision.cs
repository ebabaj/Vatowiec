using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Vatowiec.Core.Application;
using Vatowiec.Core.Domain;
using Vatowiec.Core.Infrastructure.Identity;
using Vatowiec.Core.Infrastructure.Persistence;
using Vatowiec.SharedContracts;

namespace Vatowiec.Operations;

// ADR-044 krok 1: Requested → DatabaseCreated → SchemaApplied → ProfileInitialized → Ready.
// Nigdy nie usuwa ani nie przejmuje bazy o niepotwierdzonym pochodzeniu.
public sealed partial class CompanyOperationExecutor
{
    internal const string CompanyMarker = "Vatowiec.CompanyId";
    internal const string OperationMarker = "Vatowiec.OperationId";
    internal const string InstallationMarker = "Vatowiec.InstallationId";

    private async Task ExecuteProvision(CompanyOperation operation, CancellationToken ct)
    {
        await EnsureDatabase(operation, ct);
        await Checkpoint(operation, CompanyOperationStages.DatabaseCreated, ct);
        await ApplySchema(operation, ct);
        await Checkpoint(operation, CompanyOperationStages.SchemaApplied, ct);
        await InitializeProfile(operation, ct);
        await Checkpoint(operation, CompanyOperationStages.ProfileInitialized, ct);
        await Publish(operation, ct);
    }

    private async Task EnsureDatabase(CompanyOperation operation, CancellationToken ct)
    {
        var name = operation.DatabaseName;
        if (!CompanyDatabaseNames.IsCompanyDatabase(name)) throw new OperationFailure("database_name_invalid", reconcile: true);
        await using (var master = new SqlConnection(connections.Master(operation)))
        {
            await master.OpenAsync(ct);
            await using var exists = new SqlCommand("SELECT COUNT(*) FROM sys.databases WHERE name = @name", master);
            exists.Parameters.AddWithValue("@name", name);
            if ((int)(await exists.ExecuteScalarAsync(ct))! == 0)
            {
                // Zniknięcie bazy po wcześniejszym etapie wymaga decyzji administratora, nie ponownego CREATE.
                if (operation.Stage != CompanyOperationStages.Requested) throw new OperationFailure("database_missing", reconcile: true);
                // Nazwa wyznaczona przez serwer i sprawdzona ścisłym wzorcem powyżej.
                await using (var create = new SqlCommand($"CREATE DATABASE [{name}];", master) { CommandTimeout = 120 })
                    await create.ExecuteNonQueryAsync(ct);
                foreach (var (marker, value) in Markers(operation))
                {
                    await using var mark = new SqlCommand($"EXEC [{name}].sys.sp_addextendedproperty @name = @marker, @value = @value;", master);
                    mark.Parameters.AddWithValue("@marker", marker);
                    mark.Parameters.AddWithValue("@value", value);
                    await mark.ExecuteNonQueryAsync(ct);
                }
                logger.LogInformation("Company operation {OperationId} created its database", operation.Id);
            }
        }
        await VerifyOwnership(operation, ct);
    }

    internal static (string Marker, string Value)[] Markers(CompanyOperation operation) =>
    [
        (CompanyMarker, operation.CompanyId.ToString("D")),
        (OperationMarker, operation.Id.ToString("D")),
        (InstallationMarker, operation.ActorIssuerId.ToString("D"))
    ];

    private async Task VerifyOwnership(CompanyOperation operation, CancellationToken ct)
    {
        var found = new Dictionary<string, string>(StringComparer.Ordinal);
        await using var connection = new SqlConnection(connections.Company(operation));
        await connection.OpenAsync(ct);
        await using var command = new SqlCommand(
            "SELECT name, CONVERT(nvarchar(128), value) FROM sys.extended_properties WHERE class = 0 AND name LIKE N'Vatowiec.%'", connection);
        await using (var reader = await command.ExecuteReaderAsync(ct))
            while (await reader.ReadAsync(ct)) found[reader.GetString(0)] = reader.IsDBNull(1) ? "" : reader.GetString(1);
        if (Markers(operation).Any(x => !found.TryGetValue(x.Marker, out var value) || value != x.Value))
            throw new OperationFailure("database_not_owned", reconcile: true);
    }

    private CompanyDbContext CompanyContext(CompanyOperation operation) => new(
        new DbContextOptionsBuilder<CompanyDbContext>().UseSqlServer(connections.Company(operation)).Options,
        new CompanyContext(operation.CompanyId));

    private async Task ApplySchema(CompanyOperation operation, CancellationToken ct)
    {
        await VerifyOwnership(operation, ct);
        await using var company = CompanyContext(operation);
        var applied = (await company.Database.GetAppliedMigrationsAsync(ct)).ToArray();
        var known = company.Database.GetMigrations().ToArray();
        if (!applied.SequenceEqual(known.Take(applied.Length))) throw new OperationFailure("schema_history_unknown", reconcile: true);
        await company.Database.MigrateAsync(ct);
        if ((await company.Database.GetPendingMigrationsAsync(ct)).Any()) throw new OperationFailure("schema_incomplete", reconcile: false);
    }

    private async Task InitializeProfile(CompanyOperation operation, CancellationToken ct)
    {
        await using var company = CompanyContext(operation);
        var profile = await company.CompanyProfiles.IgnoreQueryFilters().SingleOrDefaultAsync(ct);
        if (profile is null)
        {
            company.CompanyProfiles.Add(new CompanyProfile(operation.CompanyId, operation.CompanyName));
            await company.SaveChangesAsync(ct);
        }
        else if (profile.CompanyId != operation.CompanyId) throw new OperationFailure("company_identity_mismatch", reconcile: true);
    }

    // Ready: katalog, rejestracja bazy, pełne prawa twórcy i audyt w jednej transakcji System.
    private async Task Publish(CompanyOperation operation, CancellationToken ct)
    {
        var creator = operation.RequestedById ?? throw new OperationFailure("requester_missing", reconcile: true);
        await using var transaction = await system.Database.BeginTransactionAsync(ct);
        await UserAdministrationService.Lock(system, ct);
        if (await system.Companies.AnyAsync(x => x.Id == operation.CompanyId, ct) ||
            await system.CompanyDatabases.AnyAsync(x => x.CompanyId == operation.CompanyId ||
                (x.ServerName == operation.ServerName && x.DatabaseName == operation.DatabaseName), ct))
            throw new OperationFailure("catalog_conflict", reconcile: true);
        var now = clock.GetUtcNow();
        system.Companies.Add(new Company(operation.CompanyId, operation.CompanyName));
        system.CompanyDatabases.Add(new CompanyDatabaseRegistration(operation.CompanyId, operation.ServerName,
            operation.DatabaseName, operation.ConnectionProfileKey));
        var requester = await system.Users.AsNoTracking().Where(x => x.Id == creator)
            .Select(x => new { x.UserName, x.IsArchived }).SingleOrDefaultAsync(ct);
        operation.Reach(CompanyOperationStages.Ready, now, Lease);
        if (requester is { IsArchived: false, UserName: not null })
        {
            // Decyzja właściciela: twórca firmy otrzymuje jawnie i z audytem pełne prawa tylko do tej firmy.
            system.CompanyAccesses.Add(new CompanyAccess(creator, operation.CompanyId, CompanyPermissionCatalog.All));
            system.UserAdministrationChanges.Add(new UserAdministrationChange(creator, operation.ActorIssuerId,
                operation.RequestedByName, creator, requester.UserName, now, "CompanyCreatorAccessGranted",
                operation.CompanyId, CompanyPermission.None, CompanyPermissionCatalog.All));
        }
        else system.CompanyOperationEvents.Add(Event(operation, "CreatorAccessSkipped"));
        operation.Complete(now);
        system.CompanyOperationEvents.Add(Event(operation, "Completed"));
        await system.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
    }
}
