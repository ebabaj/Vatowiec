using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Vatowiec.Core.Application;
using Vatowiec.Core.Domain;
using Vatowiec.Core.Infrastructure.Persistence;
using Vatowiec.SharedContracts;
using System.Text.Json;

namespace Vatowiec.Core.Infrastructure.Identity;

public sealed class UserAdministrationException(int status, string code, string message) : Exception(message)
{
    public int Status { get; } = status;
    public string Code { get; } = code;
}

// All Identity writes use this scoped context/transaction, including UserManager saves.
public sealed class UserAdministrationService(SystemDbContext db, UserManager<ApplicationUser> users, TimeProvider clock,
    IPasswordPolicyProvider policies, RoleManager<IdentityRole<Guid>> roles)
{
    public static Task<bool> IsAdministrator(SystemDbContext db, Guid id, CancellationToken ct = default) =>
        (from membership in db.UserRoles.AsNoTracking()
         join role in db.Roles.AsNoTracking() on membership.RoleId equals role.Id
         where membership.UserId == id && role.NormalizedName == "INSTALLATIONADMINISTRATOR"
         select membership.UserId).AnyAsync(ct);

    public static string[] Codes(CompanyPermission permissions) => Enum.GetValues<CompanyPermission>()
        .Where(p => p != CompanyPermission.None && (permissions & p) == p).Select(p => p.ToString()).ToArray();
    public static CompanyPermission Parse(string[]? codes)
    {
        if (codes == null || codes.Length > Enum.GetValues<CompanyPermission>().Length || codes.Distinct().Count() != codes.Length)
            throw Invalid("Wybierz poprawne, niepowtarzające się uprawnienia.");
        var value = CompanyPermission.None;
        foreach (var code in codes)
        {
            if (!Enum.TryParse<CompanyPermission>(code, out var permission) || permission == CompanyPermission.None || permission.ToString() != code || !Enum.IsDefined(permission))
                throw Invalid("Nieznane uprawnienie. Odśwież aplikację.");
            value |= permission;
        }
        try { CompanyPermissionCatalog.Validate(value); }
        catch (ArgumentException e) { throw Invalid(e.Message); }
        return value;
    }
    public static UserAdministrationLookups Lookups(AdministrationCompany[] companies) => new(
        Enum.GetValues<CompanyPermission>().Where(p => p != CompanyPermission.None).Select(p =>
        {
            var text = CompanyPermissionCatalog.Describe(p);
            return new PermissionDefinition(p.ToString(), text.Name, text.Description, Codes(CompanyPermissionCatalog.Required(p)));
        }).ToArray(), companies);
    public async Task<UserAdministrationDetail> Detail(Guid id, CancellationToken ct)
    {
        var user = await db.Users.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, ct) ?? throw Missing();
        var access = await db.CompanyAccesses.AsNoTracking().Where(x => x.UserId == id).OrderBy(x => x.CompanyId).ToArrayAsync(ct);
        return new(new(id, user.UserName!, user.IsBlocked, user.MustChangePassword, await IsAdministrator(db, id, ct)),
            user.ConcurrencyStamp!, access.Select(x => new UserCompanyAccess(x.CompanyId, Codes(x.Permissions), Convert.ToBase64String(x.RowVersion))).ToArray());
    }
    private static UserAdministrationException Invalid(string message) => new(400, "user_validation", message);
    private static UserAdministrationException Missing() => new(404, "user_missing", "Nie znaleziono użytkownika. Odśwież listę.");
    private static UserAdministrationException Stale() => new(409, "user_stale", "Inny administrator zmienił konto lub jego prawa. Odśwież szczegóły; niezapisany szkic zachowano.");
    private static void Ensure(IdentityResult result)
    {
        if (result.Succeeded) return;
        if (result.Errors.Any(x => x.Code == "ConcurrencyFailure")) throw Stale();
        // Identity error descriptions can include input. Return only known, nonsecret messages.
        var passwordErrors = result.Errors.Where(x => x.Code is "PasswordEmpty" or "PasswordTooShort" or "PasswordRequiresUpper" or "PasswordRequiresLower" or "PasswordRequiresDigit" or "PasswordRequiresNonAlphanumeric").ToArray();
        throw Invalid(passwordErrors.Length > 0 ? string.Join(" ", passwordErrors.Select(x => x.Description)) :
            result.Errors.Any(x => x.Code == "DuplicateUserName") ? "Login jest już zajęty." : "Sprawdź dane konta i aktualne wymagania hasła.");
    }
    private async Task<ApplicationUser> Target(Guid id, string stamp, CancellationToken ct)
    {
        var row = await db.Users.SingleOrDefaultAsync(x => x.Id == id, ct) ?? throw Missing();
        if (string.IsNullOrEmpty(stamp) || stamp.Length > 128 || row.ConcurrencyStamp != stamp) throw Stale();
        return row;
    }
    public static async Task Lock(SystemDbContext db, CancellationToken ct)
    {
        await db.Database.ExecuteSqlRawAsync("DECLARE @r int; EXEC @r=sys.sp_getapplock @Resource=N'Vatowiec:UserAdministration', @LockMode='Exclusive', @LockOwner='Transaction', @LockTimeout=10000; IF @r<0 THROW 51000, 'User administration unavailable', 1;", ct);
    }
    private async Task<ApplicationUser> Actor(Guid actor, Guid session, bool admin, CancellationToken ct)
    {
        var user = await db.Users.AsNoTracking().SingleOrDefaultAsync(x => x.Id == actor, ct);
        var now = clock.GetUtcNow();
        if (user == null || user.IsBlocked || (user.LockoutEnabled && user.LockoutEnd > now) ||
            !await db.UserSessions.AsNoTracking().AnyAsync(x => x.Id == session && x.UserId == actor && x.RevokedAtUtc == null && x.ExpiresAtUtc > now, ct))
            throw new UserAdministrationException(401, "user_session_revoked", "Sesja została unieważniona. Zaloguj się ponownie.");
        if (admin && (user.MustChangePassword || !await IsAdministrator(db, actor, ct)))
            throw new UserAdministrationException(403, "user_admin_required", "Wymagana aktualna rola administratora instalacji.");
        return user;
    }
    private async Task Audit(ApplicationUser actor, ApplicationUser target, string operation, CancellationToken ct,
        Guid? company = null, CompanyPermission? before = null, CompanyPermission? after = null, bool? blockedBefore = null, bool? blockedAfter = null)
    {
        var issuer = await db.InstallationIdentities.Select(x => x.Id).SingleOrDefaultAsync(ct);
        if (issuer == Guid.Empty) throw new UserAdministrationException(503, "user_audit_identity", "Brak identyfikacji instalacji. Nie zapisano zmian.");
        db.UserAdministrationChanges.Add(new(actor.Id, issuer, actor.UserName!, target.Id, target.UserName!, clock.GetUtcNow(), operation, company, before, after, blockedBefore, blockedAfter));
    }
    private async Task Revoke(ApplicationUser target, CancellationToken ct)
    {
        Ensure(await users.UpdateSecurityStampAsync(target));
        await db.UserSessions.Where(x => x.UserId == target.Id && x.RevokedAtUtc == null)
            .ExecuteUpdateAsync(x => x.SetProperty(s => s.RevokedAtUtc, clock.GetUtcNow()), ct);
    }
    private async Task<string> TemporaryPassword(ApplicationUser user, string? supplied)
    {
        if (supplied != null)
        {
            if (supplied.Length is 0 or > 1024) throw Invalid("Wpisz hasło tymczasowe (do 1024 znaków).");
            // CreateAsync/ResetPasswordAsync apply the same Identity validators below.
            return supplied;
        }
        for (var i = 0; i < 10; i++)
        {
            string password;
            try { password = TemporaryPasswordGenerator.Generate(InstallationPasswordValidator.GeneratorOptions(await policies.Read())); }
            catch (ArgumentException e) { throw Invalid(e.Message); }
            var valid = true;
            foreach (var validator in users.PasswordValidators)
                if (!(await validator.ValidateAsync(users, user, password)).Succeeded) valid = false;
            if (valid) return password;
        }
        throw Invalid("Nie udało się wygenerować hasła zgodnego z polityką Identity.");
    }
    public async Task<TemporaryPasswordResponse> Create(Guid actorId, Guid sessionId, UserCreateRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.UserName) || request.UserName.Length > 256) throw Invalid("Wpisz login (do 256 znaków).");
        await using var tx = await db.Database.BeginTransactionAsync(ct); await Lock(db, ct);
        var actor = await Actor(actorId, sessionId, true, ct);
        var user = new ApplicationUser { UserName = request.UserName.Trim(), MustChangePassword = true };
        var password = await TemporaryPassword(user, request.TemporaryPassword);
        Ensure(await users.CreateAsync(user, password));
        await Audit(actor, user, "CreateUser", ct); await db.SaveChangesAsync(ct);
        var detail = await Detail(user.Id, ct); await tx.CommitAsync(ct);
        return new(detail, password, request.TemporaryPassword == null);
    }
    public async Task<TemporaryPasswordResponse> ResetPassword(Guid actorId, Guid sessionId, Guid id, UserVersionRequest request, CancellationToken ct)
    {
        if (!request.Confirm) throw Invalid("Potwierdź reset hasła i unieważnienie sesji.");
        await using var tx = await db.Database.BeginTransactionAsync(ct); await Lock(db, ct);
        var actor = await Actor(actorId, sessionId, true, ct); var user = await Target(id, request.ConcurrencyStamp, ct);
        var password = await TemporaryPassword(user, request.TemporaryPassword); user.MustChangePassword = true;
        Ensure(await users.ResetPasswordAsync(user, await users.GeneratePasswordResetTokenAsync(user), password));
        await Revoke(user, ct); await Audit(actor, user, "ResetPassword", ct); await db.SaveChangesAsync(ct);
        var detail = await Detail(id, ct); await tx.CommitAsync(ct); return new(detail, password, request.TemporaryPassword == null);
    }
    public async Task<UserAdministrationDetail> Block(Guid actorId, Guid sessionId, Guid id, UserBlockRequest request, CancellationToken ct)
    {
        if (!request.Confirm) throw Invalid("Potwierdź zmianę stanu konta.");
        await using var tx = await db.Database.BeginTransactionAsync(ct); await Lock(db, ct);
        var actor = await Actor(actorId, sessionId, true, ct); var user = await Target(id, request.ConcurrencyStamp, ct);
        if (request.IsBlocked)
        {
            var targetIsAdministrator = await IsAdministrator(db, id, ct);
            var active = 0;
            if (targetIsAdministrator)
            {
                var now = clock.GetUtcNow();
                active = await (from u in db.Users join m in db.UserRoles on u.Id equals m.UserId
                    join r in db.Roles on m.RoleId equals r.Id
                    where r.NormalizedName == "INSTALLATIONADMINISTRATOR" && !u.IsBlocked && (!u.LockoutEnabled || u.LockoutEnd == null || u.LockoutEnd <= now)
                    select u.Id).Distinct().CountAsync(ct);
            }
            try { UserAdministrationSafety.ValidateBlock(actorId, id, targetIsAdministrator, active); }
            catch (ArgumentException e) { throw Invalid(e.Message); }
        }
        if (user.IsBlocked != request.IsBlocked)
        {
            var before = user.IsBlocked; user.IsBlocked = request.IsBlocked;
            await Revoke(user, ct); await Audit(actor, user, request.IsBlocked ? "BlockUser" : "ActivateUser", ct, blockedBefore: before, blockedAfter: user.IsBlocked);
            await db.SaveChangesAsync(ct);
        }
        var detail = await Detail(id, ct); await tx.CommitAsync(ct); return detail;
    }
    public async Task<UserAdministrationDetail> SaveAccess(Guid actorId, Guid sessionId, Guid id, UserAccessRequest request, CancellationToken ct)
    {
        if (request.Companies == null || request.Companies.Length > 1000 || request.Companies.Any(x => x == null) ||
            request.Companies.Select(x => x.CompanyId).Distinct().Count() != request.Companies.Length) throw Invalid("Nieprawidłowa lista firm.");
        var proposed = request.Companies.Select(x => (Item: x, Permissions: Parse(x.Permissions))).ToArray();
        await using var tx = await db.Database.BeginTransactionAsync(ct); await Lock(db, ct);
        var actor = await Actor(actorId, sessionId, true, ct); var user = await Target(id, request.ConcurrencyStamp, ct);
        var companyIds = proposed.Select(x => x.Item.CompanyId).ToArray();
        if (await db.Companies.CountAsync(x => companyIds.Contains(x.Id), ct) != companyIds.Length) throw Invalid("Nieznana firma. Odśwież listę.");
        var access = await db.CompanyAccesses.Where(x => x.UserId == id).ToDictionaryAsync(x => x.CompanyId, ct);
        var changed = false;
        foreach (var (item, permissions) in proposed)
        {
            access.TryGetValue(item.CompanyId, out var row);
            if ((row == null && item.RowVersion != null) || (row != null && Convert.ToBase64String(row.RowVersion) != item.RowVersion)) throw Stale();
            var before = row?.Permissions ?? CompanyPermission.None;
            if (before == permissions) continue;
            if (permissions == CompanyPermission.None) db.CompanyAccesses.Remove(row!);
            else if (row == null) db.CompanyAccesses.Add(new(id, item.CompanyId, permissions));
            else row.ChangePermissions(permissions);
            await Audit(actor, user, "ChangeCompanyAccess", ct, item.CompanyId, before, permissions); changed = true;
        }
        if (changed) { await Revoke(user, ct); await db.SaveChangesAsync(ct); }
        var detail = await Detail(id, ct); await tx.CommitAsync(ct); return detail;
    }
    public async Task ChangeOwnPassword(Guid actorId, Guid sessionId, OwnPasswordRequest request, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(request.CurrentPassword) || string.IsNullOrEmpty(request.NewPassword) ||
            request.CurrentPassword.Length > 1024 || request.NewPassword.Length > 1024 || request.CurrentPassword == request.NewPassword)
            throw Invalid("Wpisz bieżące i inne nowe hasło (do 1024 znaków).");
        await using var tx = await db.Database.BeginTransactionAsync(ct); await Lock(db, ct);
        var actor = await Actor(actorId, sessionId, false, ct);
        var user = await db.Users.SingleAsync(x => x.Id == actorId, ct); user.MustChangePassword = false;
        Ensure(await users.ChangePasswordAsync(user, request.CurrentPassword, request.NewPassword));
        await Revoke(user, ct); await Audit(actor, user, "ChangeOwnPassword", ct); await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
    }

    public async Task<UserAdministrationDetail> ChangeAdministratorRole(Guid actorId, Guid sessionId, Guid id, AdministratorRoleRequest request, CancellationToken ct)
    {
        if (!request.Confirm) throw Invalid("Osobno potwierdź nadanie lub odebranie roli administratora instalacji.");
        await using var tx = await db.Database.BeginTransactionAsync(ct); await Lock(db, ct);
        var actor = await Actor(actorId, sessionId, true, ct); var target = await Target(id, request.ConcurrencyStamp, ct);
        if (actorId == id) throw Invalid("Nie można zmieniać własnej roli administratora przez ten ekran.");
        var current = await IsAdministrator(db, id, ct);
        var active = !target.IsBlocked && (!target.LockoutEnabled || target.LockoutEnd == null || target.LockoutEnd <= clock.GetUtcNow());
        if (request.IsAdministrator && !active) throw Invalid("Rolę administratora można nadać tylko aktywnemu użytkownikowi.");
        if (!request.IsAdministrator)
        {
            var now = clock.GetUtcNow();
            var count = await (from u in db.Users join m in db.UserRoles on u.Id equals m.UserId
                join r in db.Roles on m.RoleId equals r.Id
                where r.NormalizedName == "INSTALLATIONADMINISTRATOR" && !u.IsBlocked && (!u.LockoutEnabled || u.LockoutEnd == null || u.LockoutEnd <= now)
                select u.Id).Distinct().CountAsync(ct);
            try { UserAdministrationSafety.ValidateRoleRemoval(actorId, id, active && current, count); }
            catch (ArgumentException e) { throw Invalid(e.Message); }
        }
        if (current != request.IsAdministrator)
        {
            if (request.IsAdministrator)
            {
                if (!await roles.RoleExistsAsync(InstallationRoles.Administrator)) Ensure(await roles.CreateAsync(new(InstallationRoles.Administrator) { Id = Guid.NewGuid() }));
                Ensure(await users.AddToRoleAsync(target, InstallationRoles.Administrator));
            }
            else Ensure(await users.RemoveFromRoleAsync(target, InstallationRoles.Administrator));
            await Revoke(target, ct);
            await Audit(actor, target, request.IsAdministrator ? "GrantInstallationAdministrator" : "RevokeInstallationAdministrator", ct);
            await db.SaveChangesAsync(ct);
        }
        var result = await Detail(id, ct); await tx.CommitAsync(ct); return result;
    }

    public static PasswordPolicyResponse PolicyResponse(PasswordPolicy p, bool version = true) => new(p.IsEnabled, p.MinimumLength,
        p.RequireUppercase, p.RequireLowercase, p.RequireDigit, p.RequireSpecialCharacter, version ? Convert.ToBase64String(p.RowVersion) : "");
    public async Task<PasswordPolicyResponse> ReadPasswordPolicy(CancellationToken ct) => PolicyResponse(await policies.Read(ct));
    public async Task<PasswordPolicyResponse> SavePasswordPolicy(Guid actorId, Guid sessionId, PasswordPolicyRequest value, CancellationToken ct)
    {
        if (!value.IsEnabled && !value.ConfirmDisable) throw Invalid("Potwierdź wyłączenie złożoności: każde niepuste hasło będzie dozwolone dla wszystkich użytkowników.");
        await using var tx = await db.Database.BeginTransactionAsync(ct); await Lock(db, ct);
        var actor = await Actor(actorId, sessionId, true, ct);
        var row = await db.PasswordPolicies.SingleOrDefaultAsync(ct)
            ?? throw new UserAdministrationException(503, "password_policy_missing", "Brak konfiguracji polityki haseł w bazie systemowej instalacji. Nie zapisano zmian.");
        if (Convert.ToBase64String(row.RowVersion) != value.RowVersion)
            throw new UserAdministrationException(409, "password_policy_stale", "Inny administrator zmienił politykę haseł. Odśwież ustawienia i porównaj niezapisany szkic.");
        var before = JsonSerializer.Serialize(PolicyResponse(row, false));
        try { row.Update(value.IsEnabled, value.MinimumLength, value.RequireUppercase, value.RequireLowercase, value.RequireDigit, value.RequireSpecialCharacter); }
        catch (ArgumentException e) { throw Invalid(e.Message); }
        var after = JsonSerializer.Serialize(PolicyResponse(row, false));
        if (before != after)
        {
            var issuer = await db.InstallationIdentities.Select(x => x.Id).SingleAsync(ct);
            if (issuer == Guid.Empty) throw new UserAdministrationException(503, "user_audit_identity", "Brak identyfikacji instalacji. Nie zapisano zmian.");
            db.PasswordPolicyChanges.Add(new(actor.Id, issuer, actor.UserName!, clock.GetUtcNow(), before, after));
            await db.SaveChangesAsync(ct);
        }
        var result = PolicyResponse(row); await tx.CommitAsync(ct); return result;
    }
}
