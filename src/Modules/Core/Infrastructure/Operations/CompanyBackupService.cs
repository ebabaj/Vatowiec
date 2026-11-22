using System.Text.Json;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Vatowiec.Core.Application;
using Vatowiec.Core.Domain;
using Vatowiec.Core.Infrastructure.Identity;
using Vatowiec.Core.Infrastructure.Persistence;
using Vatowiec.SharedContracts;

namespace Vatowiec.Core.Infrastructure.Operations;

// ADR-044 krok 2 / ADR-045: zlecanie kopii, harmonogram, przypomnienia i polityka instalacji.
// Wyłącznie baza System — API nie wykonuje BACKUP; plik tworzy usługa VATowiec Operations.
public sealed class CompanyBackupService(SystemDbContext db, TimeProvider clock)
{
    public const string SchedulerName = "VATowiec Operations (harmonogram)";
    private const int ListedBackups = 50;

    private sealed record Access(Company Company, CompanyPermission Permissions, bool IsAdministrator, BackupPolicy Policy)
    {
        public bool CanRequest => IsAdministrator || Policy.UsersMayRequestBackups;
        public bool CanManageSchedule => IsAdministrator || (Permissions & CompanyPermission.ManageCompanyBackups) != 0;
    }

    private async Task<Access> Authorize(Guid userId, Guid companyId, CancellationToken ct)
    {
        var access = await db.CompanyAccesses.AsNoTracking().SingleOrDefaultAsync(x => x.UserId == userId && x.CompanyId == companyId, ct);
        var company = await db.Companies.AsNoTracking().SingleOrDefaultAsync(x => x.Id == companyId && x.IsActive, ct);
        if (access is null || company is null)
            throw new CompanyOperationException(403, "company_access_denied", "Brak dostępu do tej firmy.");
        return new(company, access.Permissions, await UserAdministrationService.IsAdministrator(db, userId, ct), await Policy(ct));
    }

    private async Task<BackupPolicy> Policy(CancellationToken ct) =>
        await db.BackupPolicies.SingleOrDefaultAsync(ct)
        ?? throw new CompanyOperationException(503, "backup_policy_missing", "Brak ustawień kopii w bazie instalacji. Wykonaj migrację System.");

    public async Task<CompanyBackupStatusResponse> Status(Guid userId, Guid companyId, CancellationToken ct)
    {
        var access = await Authorize(userId, companyId, ct);
        var schedule = await db.CompanyBackupSchedules.AsNoTracking().SingleOrDefaultAsync(x => x.CompanyId == companyId, ct);
        var backups = await db.CompanyBackups.AsNoTracking().Where(x => x.CompanyId == companyId && x.RemovedAtUtc == null)
            .OrderByDescending(x => x.CreatedAtUtc).Take(ListedBackups).ToArrayAsync(ct);
        var lastSuccess = await LastSuccess(companyId, ct);
        var latest = await db.CompanyOperations.AsNoTracking().Where(x => x.CompanyId == companyId && x.Kind == CompanyOperationKinds.Backup)
            .OrderByDescending(x => x.CreatedAtUtc).FirstOrDefaultAsync(ct);
        var now = clock.GetUtcNow();
        var frequency = schedule?.Frequency ?? CompanyBackupSchedule.DefaultFrequency;
        var mode = schedule?.Mode ?? CompanyBackupSchedule.DefaultMode;
        var active = latest is { Status: CompanyOperationStatuses.Pending or CompanyOperationStatuses.Running };
        var snoozed = await db.BackupReminderSnoozes.AsNoTracking().AnyAsync(x => x.UserId == userId && x.CompanyId == companyId && x.UntilUtc > now, ct);
        var reminderDue = mode == BackupModes.Reminder && access.CanRequest && !active && !snoozed &&
            BackupScheduleRules.IsDue(frequency, lastSuccess, access.Company.CreatedAtUtc, now, clock.LocalTimeZone);
        return new(companyId, lastSuccess,
            backups.Select(x => new CompanyBackupItem(x.Id, x.CreatedAtUtc, x.SizeBytes, x.IsAutomatic, x.RequestedByName)).ToArray(),
            new(frequency, mode, schedule?.PreferredHour ?? CompanyBackupSchedule.DefaultPreferredHour,
                schedule is null ? null : Convert.ToBase64String(schedule.RowVersion), schedule is null),
            access.CanRequest, access.CanManageSchedule, reminderDue, latest is null ? null : Response(latest),
            access.Policy.RetainedBackupsPerCompany);
    }

    public Task<DateTimeOffset?> LastSuccess(Guid companyId, CancellationToken ct) =>
        db.CompanyBackups.AsNoTracking().Where(x => x.CompanyId == companyId).MaxAsync(x => (DateTimeOffset?)x.CreatedAtUtc, ct);

    public async Task<(CompanyOperation Operation, bool Created)> Request(OperationRequester requester, Guid companyId, Guid operationKey, CancellationToken ct)
    {
        if (operationKey == Guid.Empty)
            throw new CompanyOperationException(400, "operation_key_missing", "Brak klucza operacji. Odśwież widok i spróbuj ponownie.");
        var access = await Authorize(requester.UserId, companyId, ct);
        var hash = CompanyOperationRequests.Hash(CompanyOperationKinds.Backup, companyId.ToString("D"), "");
        var previous = await db.CompanyOperations.AsNoTracking()
            .SingleOrDefaultAsync(x => x.RequestedById == requester.UserId && x.OperationKey == operationKey, ct);
        if (previous is not null)
            return previous.RequestHash == hash && previous.CompanyId == companyId ? (previous, false)
                : throw new CompanyOperationException(409, "operation_payload_mismatch", "Ten klucz operacji użyto już dla innego zlecenia. Odśwież widok.");
        if (!access.CanRequest)
            throw new CompanyOperationException(403, "backup_requests_disabled", "Administrator instalacji wyłączył zlecanie kopii przez użytkowników.");
        var issuer = await db.InstallationIdentities.AsNoTracking().Select(x => x.Id).SingleOrDefaultAsync(ct);
        if (issuer == Guid.Empty || string.IsNullOrWhiteSpace(requester.Name))
            throw new CompanyOperationException(503, "audit_identity_unavailable", "Brak trwałej identyfikacji instalacji lub wykonawcy. Nie przyjęto zlecenia.");
        var (operation, created) = await Enqueue(access.Company, requester.UserId, requester.Name, issuer, operationKey, hash, ct);
        return (operation, created);
    }

    // Wspólne dla zlecenia użytkownika i harmonogramu usługi. Trwająca kopia firmy jest zwracana zamiast drugiej.
    public async Task<(CompanyOperation Operation, bool Created)> Enqueue(Company company, Guid? requesterId, string requesterName,
        Guid issuer, Guid operationKey, string hash, CancellationToken ct)
    {
        var active = await ActiveBackup(company.Id, ct);
        if (active is not null) return (active, false);
        var registration = await db.CompanyDatabases.AsNoTracking().SingleOrDefaultAsync(x => x.CompanyId == company.Id, ct)
            ?? throw new CompanyOperationException(503, "company_unavailable", "Baza firmy nie jest zarejestrowana w instalacji.");
        var id = Guid.NewGuid();
        var operation = CompanyOperation.Backup(id, operationKey, company.Id, company.Name, requesterId, requesterName, issuer, hash,
            registration.ServerName, registration.ConnectionProfileKey, registration.DatabaseName,
            BackupFileName(registration.DatabaseName, clock.GetUtcNow(), id));
        db.CompanyOperations.Add(operation);
        db.CompanyOperationEvents.Add(new CompanyOperationEvent(operation, clock.GetUtcNow(),
            requesterId is null ? CompanyOperationEvent.WorkerActor : CompanyOperationEvent.UserActor, requesterId, requesterName, "Requested"));
        try
        {
            await db.SaveChangesAsync(ct);
            return (operation, true);
        }
        catch (DbUpdateException error) when (error.InnerException is SqlException { Number: 2601 or 2627 })
        {
            db.ChangeTracker.Clear(); // Równoległe zlecenie tej samej firmy wygrało.
            return (await ActiveBackup(company.Id, ct) ?? throw new CompanyOperationException(409, "backup_conflict", "Odśwież stan kopii."), false);
        }
    }

    private Task<CompanyOperation?> ActiveBackup(Guid companyId, CancellationToken ct) => db.CompanyOperations.AsNoTracking()
        .SingleOrDefaultAsync(x => x.CompanyId == companyId && x.Kind == CompanyOperationKinds.Backup &&
            (x.Status == CompanyOperationStatuses.Pending || x.Status == CompanyOperationStatuses.Running), ct);

    // Ścieżka względna wewnątrz katalogu kopii serwera: {baza}/{baza}_{UTC}_{operacja}.bak.
    public static string BackupFileName(string databaseName, DateTimeOffset at, Guid operationId) =>
        $"{databaseName}/{databaseName}_{at.UtcDateTime:yyyyMMdd_HHmmss}_{operationId.ToString("N")[..8]}.bak";

    public async Task<CompanyBackupStatusResponse> SaveSchedule(OperationRequester requester, Guid companyId, CompanyBackupScheduleRequest value, CancellationToken ct)
    {
        var access = await Authorize(requester.UserId, companyId, ct);
        if (!access.CanManageSchedule)
            throw new CompanyOperationException(403, "backup_schedule_denied", "Zmiana harmonogramu kopii wymaga prawa „Harmonogram kopii firmy” albo roli administratora instalacji.");
        var schedule = await db.CompanyBackupSchedules.SingleOrDefaultAsync(x => x.CompanyId == companyId, ct);
        if (value.RowVersion != (schedule is null ? null : Convert.ToBase64String(schedule.RowVersion)))
            throw new CompanyOperationException(409, "backup_schedule_stale", "Harmonogram kopii zmienił się. Odśwież widok przed zapisem.");
        var before = schedule is null ? null : ScheduleJson(schedule.Frequency, schedule.Mode, schedule.PreferredHour);
        try
        {
            if (schedule is null) db.CompanyBackupSchedules.Add(schedule = new(companyId, value.Frequency, value.Mode, value.PreferredHour));
            else schedule.Change(value.Frequency, value.Mode, value.PreferredHour);
        }
        catch (ArgumentException error) { throw new CompanyOperationException(400, "backup_schedule_invalid", error.Message); }
        db.BackupSettingChanges.Add(new(companyId, requester.UserId, requester.Name, clock.GetUtcNow(), before,
            ScheduleJson(value.Frequency, value.Mode, value.PreferredHour)));
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException) { throw new CompanyOperationException(409, "backup_schedule_stale", "Harmonogram kopii zmienił się. Odśwież widok przed zapisem."); }
        return await Status(requester.UserId, companyId, ct);
    }

    private static string ScheduleJson(string frequency, string mode, int hour) => JsonSerializer.Serialize(new { frequency, mode, hour });

    public async Task<CompanyBackupStatusResponse> Snooze(Guid userId, Guid companyId, CancellationToken ct)
    {
        await Authorize(userId, companyId, ct);
        var until = BackupScheduleRules.NextLocalDay(clock.GetUtcNow(), clock.LocalTimeZone);
        var snooze = await db.BackupReminderSnoozes.SingleOrDefaultAsync(x => x.UserId == userId && x.CompanyId == companyId, ct);
        if (snooze is null) db.BackupReminderSnoozes.Add(new(userId, companyId, until)); else snooze.Extend(until);
        await db.SaveChangesAsync(ct);
        return await Status(userId, companyId, ct);
    }

    public async Task<BackupPolicyResponse> ReadPolicy(CancellationToken ct) => PolicyResponse(await Policy(ct));

    // Wywoływane wyłącznie z endpointu z polityką administratora instalacji.
    public async Task<BackupPolicyResponse> SavePolicy(OperationRequester requester, BackupPolicyRequest value, CancellationToken ct)
    {
        var policy = await Policy(ct);
        if (value.RowVersion != Convert.ToBase64String(policy.RowVersion))
            throw new CompanyOperationException(409, "backup_policy_stale", "Ustawienia kopii zmieniły się. Odśwież widok przed zapisem.");
        var before = PolicyJson(policy.UsersMayRequestBackups, policy.RetainedBackupsPerCompany);
        try { policy.Change(value.UsersMayRequestBackups, value.RetainedBackupsPerCompany); }
        catch (ArgumentException error) { throw new CompanyOperationException(400, "backup_policy_invalid", error.Message); }
        db.BackupSettingChanges.Add(new(null, requester.UserId, requester.Name, clock.GetUtcNow(), before,
            PolicyJson(value.UsersMayRequestBackups, value.RetainedBackupsPerCompany)));
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateConcurrencyException) { throw new CompanyOperationException(409, "backup_policy_stale", "Ustawienia kopii zmieniły się. Odśwież widok przed zapisem."); }
        return PolicyResponse(policy);
    }

    private static string PolicyJson(bool users, int retained) => JsonSerializer.Serialize(new { usersMayRequestBackups = users, retained });
    private static BackupPolicyResponse PolicyResponse(BackupPolicy x) => new(x.UsersMayRequestBackups, x.RetainedBackupsPerCompany, Convert.ToBase64String(x.RowVersion));

    public static CompanyOperationResponse Response(CompanyOperation x) => new(x.Id, x.Kind, x.CompanyId, x.CompanyName, x.Stage,
        x.Status, x.Attempt, x.LastErrorCode, x.RequestedByName, x.CreatedAtUtc, x.UpdatedAtUtc, x.CompletedAtUtc,
        Convert.ToBase64String(x.RowVersion));
}
