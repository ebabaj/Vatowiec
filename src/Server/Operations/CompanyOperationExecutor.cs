using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Vatowiec.Core.Domain;
using Vatowiec.Core.Infrastructure.Persistence;

namespace Vatowiec.Operations;

public sealed record OperationsWorkerIdentity(string Name);

// ADR-044: wspólna kolejka operacji technicznych firm (dzierżawa, etapy, błędy, historia).
// Rodzaje: tworzenie firmy (CompanyOperationExecutor.Provision.cs) i kopia (CompanyOperationExecutor.Backup.cs).
// Każde wykonanie sprawdza rzeczywisty stan zamiast ufać samemu checkpointowi.
public sealed partial class CompanyOperationExecutor(SystemDbContext system, OperationsConnections connections, BackupStorage storage,
    TimeProvider clock, ILogger<CompanyOperationExecutor> logger, OperationsWorkerIdentity worker, Func<string, Task>? afterCheckpoint = null)
{
    public static readonly TimeSpan Lease = TimeSpan.FromMinutes(5);
    // Kopia dużej bazy może trwać długo; przedłużamy dzierżawę przed BACKUP i weryfikacją.
    public static readonly TimeSpan LongLease = TimeSpan.FromHours(2);

    public async Task<bool> RunNext(CancellationToken ct)
    {
        var operation = await Claim(ct);
        if (operation is null) return false;
        await Execute(operation, ct);
        return true;
    }

    internal async Task<CompanyOperation?> Claim(CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var candidate = await system.CompanyOperations
            .Where(x => (x.Status == CompanyOperationStatuses.Pending && (x.NextAttemptAtUtc == null || x.NextAttemptAtUtc <= now)) ||
                        (x.Status == CompanyOperationStatuses.Running && x.LeaseExpiresAtUtc < now))
            .OrderBy(x => x.CreatedAtUtc).ThenBy(x => x.Id).FirstOrDefaultAsync(ct);
        if (candidate is null) return null;
        candidate.Claim(worker.Name, now, Lease);
        system.CompanyOperationEvents.Add(Event(candidate, "Claimed"));
        try
        {
            await system.SaveChangesAsync(ct); // RowVersion: tylko jeden wykonawca przejmuje operację.
            return candidate;
        }
        catch (DbUpdateConcurrencyException)
        {
            system.ChangeTracker.Clear();
            return null;
        }
    }

    internal async Task Execute(CompanyOperation operation, CancellationToken ct)
    {
        try
        {
            switch (operation.Kind)
            {
                case CompanyOperationKinds.Provision: await ExecuteProvision(operation, ct); break;
                case CompanyOperationKinds.Backup: await ExecuteBackup(operation, ct); break;
                case CompanyOperationKinds.Delete: await ExecuteDelete(operation, ct); break;
                case CompanyOperationKinds.Restore: await ExecuteRestore(operation, ct); break;
                default: throw new OperationFailure("operation_kind_unsupported", reconcile: true);
            }
            logger.LogInformation("Company operation {OperationId} ({Kind}) completed for company {CompanyId}", operation.Id, operation.Kind, operation.CompanyId);
        }
        catch (DbUpdateConcurrencyException)
        {
            // Dzierżawę przejął inny wykonawca; nie zapisujemy niczego w jego imieniu.
            logger.LogWarning("Company operation {OperationId} lease lost", operation.Id);
            system.ChangeTracker.Clear();
        }
        catch (OperationFailure failure) { await Record(operation.Id, failure, ct); }
        catch (SqlException error) { await Record(operation.Id, new OperationFailure($"sql_{error.Number}", reconcile: false), ct); }
        catch (Exception error) when (error is InvalidOperationException or DbUpdateException or TimeoutException or IOException or UnauthorizedAccessException)
        {
            await Record(operation.Id, new OperationFailure(error is IOException or UnauthorizedAccessException ? "backup_storage_failed" : "operation_failed", reconcile: false), ct);
        }
    }

    private static bool HasReached(CompanyOperation operation, string stage)
    {
        var stages = CompanyOperationStages.For(operation.Kind);
        return Array.IndexOf(stages, operation.Stage) >= Array.IndexOf(stages, stage);
    }

    private async Task Checkpoint(CompanyOperation operation, string stage, CancellationToken ct)
    {
        if (!HasReached(operation, stage))
        {
            operation.Reach(stage, clock.GetUtcNow(), Lease);
            system.CompanyOperationEvents.Add(Event(operation, "StageReached"));
            await system.SaveChangesAsync(ct);
        }
        if (afterCheckpoint is not null) await afterCheckpoint(stage);
    }

    private async Task Renew(CompanyOperation operation, TimeSpan lease, CancellationToken ct)
    {
        operation.Reach(operation.Stage, clock.GetUtcNow(), lease);
        await system.SaveChangesAsync(ct);
    }

    private async Task Record(Guid id, OperationFailure failure, CancellationToken ct)
    {
        logger.LogWarning("Company operation {OperationId} failed: {Code}", id, failure.Code);
        system.ChangeTracker.Clear();
        var operation = await system.CompanyOperations.SingleOrDefaultAsync(x => x.Id == id, ct);
        if (operation is null || operation.Status != CompanyOperationStatuses.Running || operation.LeaseOwner != worker.Name) return;
        if (failure.Reconcile) operation.RequireReconciliation(failure.Code);
        else operation.Fail(failure.Code, clock.GetUtcNow());
        system.CompanyOperationEvents.Add(Event(operation, failure.Reconcile ? "NeedsReconciliation" : "Failed"));
        try { await system.SaveChangesAsync(ct); }
        catch (DbUpdateConcurrencyException) { system.ChangeTracker.Clear(); }
    }

    private CompanyOperationEvent Event(CompanyOperation operation, string action) =>
        new(operation, clock.GetUtcNow(), CompanyOperationEvent.WorkerActor, null, worker.Name, action);
}
