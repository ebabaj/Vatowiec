using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Vatowiec.Core.Infrastructure.Persistence;

namespace Vatowiec.Core.Infrastructure.Identity;

// Centralizes every password-setting path, including seed and administrative restore.
// Identity's opportunistic hash upgrade during login is deliberately not a password change.
public sealed class VatowiecUserManager(IUserStore<ApplicationUser> store, IOptions<IdentityOptions> options,
    IPasswordHasher<ApplicationUser> hasher, IEnumerable<IUserValidator<ApplicationUser>> userValidators,
    IEnumerable<IPasswordValidator<ApplicationUser>> passwordValidators, ILookupNormalizer normalizer,
    IdentityErrorDescriber errors, IServiceProvider services, ILogger<UserManager<ApplicationUser>> logger,
    SystemDbContext db, TimeProvider clock)
    : UserManager<ApplicationUser>(store, options, hasher, userValidators, passwordValidators, normalizer, errors, services, logger)
{
    public override Task<IdentityResult> CreateAsync(ApplicationUser user, string password) =>
        SetPassword(user, () => base.CreateAsync(user, password));
    public override Task<IdentityResult> ResetPasswordAsync(ApplicationUser user, string token, string password) =>
        SetPassword(user, () => base.ResetPasswordAsync(user, token, password));
    public override Task<IdentityResult> ChangePasswordAsync(ApplicationUser user, string currentPassword, string newPassword) =>
        SetPassword(user, () => base.ChangePasswordAsync(user, currentPassword, newPassword));
    public override Task<IdentityResult> AddPasswordAsync(ApplicationUser user, string password) =>
        SetPassword(user, () => base.AddPasswordAsync(user, password));
    private async Task<IdentityResult> SetPassword(ApplicationUser user, Func<Task<IdentityResult>> write)
    {
        await using var owned = db.Database.CurrentTransaction == null ? await db.Database.BeginTransactionAsync() : null;
        if (owned != null) await UserAdministrationService.Lock(db, default);
        var previous = user.PasswordHash;
        var result = await write();
        if (!result.Succeeded) return result;
        var now = clock.GetUtcNow(); user.PasswordChangedAtUtc = now;
        if (previous != null) db.UserPasswordHistories.Add(new(user.Id, previous, now));
        // Keep chronology while enforcement is disabled, without reconstructing unavailable history.
        var excess = await db.UserPasswordHistories.Where(x => x.UserId == user.Id).OrderByDescending(x => x.Id)
            .Skip(previous == null ? 20 : 19).ToArrayAsync();
        db.UserPasswordHistories.RemoveRange(excess);
        await db.SaveChangesAsync();
        if (owned != null) await owned.CommitAsync();
        return result;
    }
}
