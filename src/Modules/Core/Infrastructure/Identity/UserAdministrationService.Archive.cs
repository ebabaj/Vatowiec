using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Vatowiec.Core.Domain;
using Vatowiec.Core.Application;
using Vatowiec.SharedContracts;

namespace Vatowiec.Core.Infrastructure.Identity;

public sealed partial class UserAdministrationService
{
    private async Task ClearAccess(ApplicationUser actor, ApplicationUser target, CancellationToken ct)
    {
        var access = await db.CompanyAccesses.Where(x => x.UserId == target.Id).ToArrayAsync(ct);
        foreach (var item in access)
            await Audit(actor, target, "ChangeCompanyAccess", ct, item.CompanyId, item.Permissions, CompanyPermission.None);
        db.CompanyAccesses.RemoveRange(access);
        if (await IsAdministrator(db, target.Id, ct)) await Audit(actor, target, "RevokeInstallationAdministrator", ct);
        db.UserRoles.RemoveRange(await db.UserRoles.Where(x => x.UserId == target.Id).ToArrayAsync(ct));
    }
    public async Task<UserAdministrationDetail> Archive(Guid actorId, Guid sessionId, Guid id, UserVersionRequest value, CancellationToken ct)
    {
        if (!value.Confirm) throw Invalid("Potwierdź logiczne usunięcie użytkownika i odebranie dostępu.");
        await using var tx = await db.Database.BeginTransactionAsync(ct); await Lock(db, ct);
        var actor = await Actor(actorId, sessionId, true, ct); var target = await Target(id, value.ConcurrencyStamp, ct);
        if (id == actorId) throw Invalid("Nie można usunąć własnego konta.");
        if (await IsAdministrator(db, id, ct))
        {
            var now = clock.GetUtcNow();
            var count = await (from u in db.Users join m in db.UserRoles on u.Id equals m.UserId join r in db.Roles on m.RoleId equals r.Id
                where r.NormalizedName == "INSTALLATIONADMINISTRATOR" && !u.IsArchived && !u.IsBlocked && (!u.LockoutEnabled || u.LockoutEnd == null || u.LockoutEnd <= now)
                select u.Id).Distinct().CountAsync(ct);
            var targetActive = !target.IsBlocked && (!target.LockoutEnabled || target.LockoutEnd == null || target.LockoutEnd <= now);
            try { UserAdministrationSafety.ValidateArchive(actorId, id, targetActive, count); }
            catch (ArgumentException e) { throw Invalid(e.Message); }
        }
        await ClearAccess(actor, target, ct);
        target.HistoricalUserName = target.UserName; target.HistoricalNormalizedUserName = target.NormalizedUserName;
        target.IsArchived = true; target.ArchivedAtUtc = clock.GetUtcNow(); target.ArchivedByUserId = actorId; target.ArchivedByName = actor.UserName;
        await Revoke(target, ct); await Audit(actor, target, "ArchiveUser", ct); await db.SaveChangesAsync(ct);
        var result = await Detail(id, ct); await tx.CommitAsync(ct); return result;
    }
    public async Task<TemporaryPasswordResponse> Restore(Guid actorId, Guid sessionId, Guid id, UserVersionRequest value, bool newPerson, CancellationToken ct)
    {
        if (!value.Confirm) throw Invalid("Potwierdź wybór tej samej albo nowej osoby dla usuniętego konta.");
        await using var tx = await db.Database.BeginTransactionAsync(ct); await Lock(db, ct);
        var actor = await Actor(actorId, sessionId, true, ct); var old = await Target(id, value.ConcurrencyStamp, ct, allowArchived: true);
        if (!old.IsArchived || old.IsLoginReleased) throw Invalid("To konto nie ma zarezerwowanego loginu do przywrócenia. Odśwież szczegóły.");
        if (id == actorId) throw Invalid("Nie można przywracać własnego konta przez tę operację.");
        await ClearAccess(actor, old, ct); await Revoke(old, ct);
        ApplicationUser target;
        if (newPerson)
        {
            var login = old.HistoricalUserName ?? old.UserName!;
            old.IsLoginReleased = true;
            var internalName = "archived-" + old.Id.ToString("N");
            while (await users.FindByNameAsync(internalName) != null) internalName = "archived-" + old.Id.ToString("N") + "-" + Guid.NewGuid().ToString("N");
            Ensure(await users.SetUserNameAsync(old, internalName));
            target = new ApplicationUser { UserName = login, MustChangePassword = true };
        }
        else
        {
            target = old; target.IsArchived = false; target.ArchivedAtUtc = null; target.ArchivedByUserId = null; target.ArchivedByName = null;
            target.IsBlocked = false; target.LockoutEnd = null; target.AccessFailedCount = 0; target.MustChangePassword = true;
        }
        var password = await TemporaryPassword(target, value.TemporaryPassword);
        if (newPerson) Ensure(await users.CreateAsync(target, password));
        else Ensure(await users.ResetPasswordAsync(target, await users.GeneratePasswordResetTokenAsync(target), password));
        await Revoke(target, ct);
        if (newPerson)
        {
            await Audit(actor, old, "ReleaseArchivedLogin", ct, relatedUser: target.Id);
            await Audit(actor, target, "CreateFromArchivedLogin", ct, relatedUser: old.Id);
        }
        else await Audit(actor, target, "RestoreSameUser", ct);
        await db.SaveChangesAsync(ct); var detail = await Detail(target.Id, ct); await tx.CommitAsync(ct);
        return new(detail, password, value.TemporaryPassword == null);
    }
}
