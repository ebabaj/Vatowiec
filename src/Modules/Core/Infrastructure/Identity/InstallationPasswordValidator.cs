using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Vatowiec.Core.Domain;
using Vatowiec.Core.Infrastructure.Persistence;

namespace Vatowiec.Core.Infrastructure.Identity;

public interface IPasswordPolicyProvider
{
    Task<PasswordPolicy> Read(CancellationToken ct = default);
}
public sealed class DatabasePasswordPolicyProvider(SystemDbContext db) : IPasswordPolicyProvider
{
    // No process or request cache: even a reused UserManager sees a freshly saved policy.
    public async Task<PasswordPolicy> Read(CancellationToken ct = default) =>
        await db.PasswordPolicies.AsNoTracking().SingleOrDefaultAsync(ct)
        ?? throw new UserAdministrationException(503, "password_policy_missing", "Brak konfiguracji polityki haseł. Sprawdź migrację bazy systemowej.");
}
public sealed class InstallationPasswordValidator(IPasswordPolicyProvider policies, SystemDbContext db, IPasswordHasher<ApplicationUser> hasher) : IPasswordValidator<ApplicationUser>
{
    public async Task<IdentityResult> ValidateAsync(UserManager<ApplicationUser> manager, ApplicationUser user, string? password)
    {
        var policy = await policies.Read(); var result = Validate(policy, password);
        if (!result.Succeeded || !policy.PreventPasswordReuse || password == null) return result;
        var hashes = user.PasswordHash == null ? new List<string>() : [user.PasswordHash];
        hashes.AddRange(await db.UserPasswordHistories.AsNoTracking().Where(x => x.UserId == user.Id)
            .OrderByDescending(x => x.Id).Take(policy.PreviousPasswordCount).Select(x => x.PasswordHash).ToArrayAsync());
        if (hashes.Any(hash => hasher.VerifyHashedPassword(user, hash, password) != PasswordVerificationResult.Failed))
            return IdentityResult.Failed(new IdentityError { Code = "PasswordReused", Description = "Nie można ponownie użyć aktualnego ani jednego z chronionych poprzednich haseł." });
        return result;
    }

    public static IdentityResult Validate(PasswordPolicy policy, string? password)
    {
        if (string.IsNullOrEmpty(password)) return IdentityResult.Failed(new IdentityError { Code = "PasswordEmpty", Description = "Hasło nie może być puste." });
        if (!policy.IsEnabled) return IdentityResult.Success;
        var errors = new List<IdentityError>();
        if (password.Length < policy.MinimumLength) errors.Add(new() { Code = "PasswordTooShort", Description = $"Hasło musi mieć co najmniej {policy.MinimumLength} znaków." });
        if (policy.RequireUppercase && !password.Any(c => c is >= 'A' and <= 'Z')) errors.Add(new() { Code = "PasswordRequiresUpper", Description = "Hasło musi zawierać wielką literę (A–Z)." });
        if (policy.RequireLowercase && !password.Any(c => c is >= 'a' and <= 'z')) errors.Add(new() { Code = "PasswordRequiresLower", Description = "Hasło musi zawierać małą literę (a–z)." });
        if (policy.RequireDigit && !password.Any(c => c is >= '0' and <= '9')) errors.Add(new() { Code = "PasswordRequiresDigit", Description = "Hasło musi zawierać cyfrę." });
        if (policy.RequireSpecialCharacter && !password.Any(c => !IsAsciiLetterOrDigit(c))) errors.Add(new() { Code = "PasswordRequiresNonAlphanumeric", Description = "Hasło musi zawierać znak specjalny." });
        return errors.Count == 0 ? IdentityResult.Success : IdentityResult.Failed(errors.ToArray());
    }
    private static bool IsAsciiLetterOrDigit(char c) => c is >= 'A' and <= 'Z' or >= 'a' and <= 'z' or >= '0' and <= '9';
    public static PasswordOptions GeneratorOptions(PasswordPolicy p) => new()
    {
        RequiredLength = p.IsEnabled ? p.MinimumLength : 1, RequiredUniqueChars = 1,
        RequireUppercase = p.IsEnabled && p.RequireUppercase, RequireLowercase = p.IsEnabled && p.RequireLowercase,
        RequireDigit = p.IsEnabled && p.RequireDigit, RequireNonAlphanumeric = p.IsEnabled && p.RequireSpecialCharacter
    };
}
