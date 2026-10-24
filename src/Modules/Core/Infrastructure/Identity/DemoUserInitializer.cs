using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Vatowiec.Core.Domain;
using Vatowiec.Core.Infrastructure.Persistence;

namespace Vatowiec.Core.Infrastructure.Identity;

public sealed class DemoUserInitializer(SystemDbContext db, UserManager<ApplicationUser> users, RoleManager<IdentityRole<Guid>> roles)
{
    // Existing accounts belong to the administrator: do not even verify their password.
    public async Task<bool> CreateIfMissing(string name, string password, string role, IReadOnlyDictionary<Guid, CompanyPermission> initialAccess)
    {
        await using var tx = await db.Database.BeginTransactionAsync(); await UserAdministrationService.Lock(db, default);
        if (await users.FindByNameAsync(name) != null || await db.Users.AnyAsync(x => x.IsArchived && x.HistoricalNormalizedUserName == users.NormalizeName(name))) return false;
        if (!await roles.RoleExistsAsync(role)) Ensure(await roles.CreateAsync(new IdentityRole<Guid>(role) { Id = Guid.NewGuid() }));
        var user = new ApplicationUser { UserName = name };
        Ensure(await users.CreateAsync(user, password)); Ensure(await users.AddToRoleAsync(user, role));
        foreach (var (company, permission) in initialAccess)
            if (permission != CompanyPermission.None) db.CompanyAccesses.Add(new(user.Id, company, permission));
        await db.SaveChangesAsync(); await tx.CommitAsync(); return true;
    }
    private static void Ensure(IdentityResult result)
    { if (!result.Succeeded) throw new InvalidOperationException("Nie można zainicjalizować brakującego konta demonstracyjnego: " + string.Join(", ", result.Errors.Select(x => x.Code))); }
}
