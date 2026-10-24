using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Vatowiec.Core.Infrastructure.Persistence;

namespace Vatowiec.Core.Infrastructure.Identity;

public static class InstallationRoles
{
    public const string Administrator = "InstallationAdministrator";
}

public static class IdentityRegistration
{
    public static IdentityBuilder AddVatowiecIdentity(this IServiceCollection services)
    {
        services.TryAddSingleton(TimeProvider.System);
        services.AddDataProtection();
        services.AddHttpContextAccessor();
        var identity = services.AddIdentityCore<ApplicationUser>(options =>
        {
            options.Stores.MaxLengthForKeys = 128;
            // Password validation comes exclusively from the database-backed validator.
            options.Password.RequiredLength = 1; options.Password.RequiredUniqueChars = 0;
            options.Password.RequireDigit = options.Password.RequireLowercase = options.Password.RequireUppercase = options.Password.RequireNonAlphanumeric = false;
            options.Lockout.MaxFailedAccessAttempts = 5;
            options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(5);
            options.Lockout.AllowedForNewUsers = true;
        }).AddRoles<IdentityRole<Guid>>().AddSignInManager()
            .AddEntityFrameworkStores<SystemDbContext>().AddDefaultTokenProviders().AddUserManager<VatowiecUserManager>();
        services.RemoveAll<IPasswordValidator<ApplicationUser>>();
        services.AddScoped<IPasswordPolicyProvider, DatabasePasswordPolicyProvider>();
        services.AddScoped<IPasswordValidator<ApplicationUser>, InstallationPasswordValidator>();
        return identity;
    }
}
