using Microsoft.EntityFrameworkCore;
using Vatowiec.Core.Domain;
using Vatowiec.Core.Infrastructure.Operations;
using Vatowiec.Core.Infrastructure.Persistence;

namespace Vatowiec.Operations;

// ADR-044/045: kopie automatyczne według harmonogramu firmy (tryb Automatic), od ustawionej godziny lokalnej.
// Po nieudanej kopii automatycznej kolejna próba najwcześniej po godzinie.
public sealed class BackupScheduler(SystemDbContext system, CompanyBackupService backups, TimeProvider clock, ILogger<BackupScheduler> logger)
{
    public async Task<int> EnqueueDue(CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var zone = clock.LocalTimeZone;
        var schedules = await system.CompanyBackupSchedules.AsNoTracking()
            .Where(x => x.Mode == BackupModes.Automatic && x.Frequency != BackupFrequencies.Never).ToArrayAsync(ct);
        if (schedules.Length == 0) return 0;
        var issuer = await system.InstallationIdentities.AsNoTracking().Select(x => x.Id).SingleOrDefaultAsync(ct);
        if (issuer == Guid.Empty) return 0;
        var queued = 0;
        foreach (var schedule in schedules)
        {
            if (!BackupScheduleRules.IsAutomaticWindow(schedule.PreferredHour, now, zone)) continue;
            var company = await system.Companies.AsNoTracking().SingleOrDefaultAsync(x => x.Id == schedule.CompanyId && x.IsActive, ct);
            if (company is null) continue;
            if (!BackupScheduleRules.IsDue(schedule.Frequency, await backups.LastSuccess(company.Id, ct), company.CreatedAtUtc, now, zone)) continue;
            var recentAttempt = now.AddHours(-1);
            if (await system.CompanyOperations.AnyAsync(x => x.CompanyId == company.Id && x.Kind == CompanyOperationKinds.Backup &&
                    x.RequestedById == null && x.CreatedAtUtc > recentAttempt, ct)) continue;
            var (_, created) = await backups.Enqueue(company, null, CompanyBackupService.SchedulerName, issuer, Guid.NewGuid(),
                CompanyOperationRequests.Hash(CompanyOperationKinds.Backup, company.Id.ToString("D"), "schedule"), ct);
            if (created) { queued++; logger.LogInformation("Scheduled backup queued for company {CompanyId}", company.Id); }
        }
        return queued;
    }
}
