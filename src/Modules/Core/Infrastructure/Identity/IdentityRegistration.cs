using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Vatowiec.Core.Infrastructure.Persistence;

namespace Vatowiec.Core.Infrastructure.Identity;

public static class IdentityRegistration
{
    public static IdentityBuilder AddVatowiecIdentity(this IServiceCollection services)
    {
        services.TryAddSingleton(TimeProvider.System);
        services.AddDataProtection();
        services.AddHttpContextAccessor();
        return services.AddIdentityCore<ApplicationUser>(options =>
        {
            options.Stores.MaxLengthForKeys = 128;
            options.Password.RequiredLength = 12;
            options.Password.RequireDigit = true;
            options.Password.RequireLowercase = true;
            options.Password.RequireUppercase = true;
            options.Password.RequireNonAlphanumeric = true;
            options.Lockout.MaxFailedAccessAttempts = 5;
            options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(5);
            options.Lockout.AllowedForNewUsers = true;
        }).AddRoles<IdentityRole<Guid>>().AddSignInManager()
            .AddEntityFrameworkStores<SystemDbContext>().AddDefaultTokenProviders();
    }
}
