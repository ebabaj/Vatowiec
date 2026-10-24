using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Vatowiec.Core.Application;
using Vatowiec.Core.Domain;
using Vatowiec.Core.Infrastructure.Identity;
using Vatowiec.Core.Infrastructure.Persistence;
using Vatowiec.Desktop.ViewModels;
using Vatowiec.SharedContracts;

namespace Vatowiec.Tests;

internal sealed class FixedPasswordPolicyProvider : IPasswordPolicyProvider
{
    public PasswordPolicy Value { get; } = new();
    public Task<PasswordPolicy> Read(CancellationToken ct = default) => Task.FromResult(Value);
}
public sealed class PasswordPolicyTests
{
    [Fact]
    public void IndependentCharacterRequirements_All16Combinations()
    {
        for (var flags = 0; flags < 16; flags++)
        {
            var upper = (flags & 1) != 0; var lower = (flags & 2) != 0; var digit = (flags & 4) != 0; var special = (flags & 8) != 0;
            var policy = new PasswordPolicy(); policy.Update(true, 4, upper, lower, digit, special);
            Assert.True(InstallationPasswordValidator.Validate(policy, "aA2!").Succeeded);
            Assert.Equal(upper, InstallationPasswordValidator.Validate(policy, "aa2!").Errors.Any(x => x.Code == "PasswordRequiresUpper"));
            Assert.Equal(lower, InstallationPasswordValidator.Validate(policy, "AA2!").Errors.Any(x => x.Code == "PasswordRequiresLower"));
            Assert.Equal(digit, InstallationPasswordValidator.Validate(policy, "aAa!").Errors.Any(x => x.Code == "PasswordRequiresDigit"));
            Assert.Equal(special, InstallationPasswordValidator.Validate(policy, "aA2a").Errors.Any(x => x.Code == "PasswordRequiresNonAlphanumeric"));
        }
    }
    [Fact]
    public void MinimumAndDisabledPolicyStillRejectEmptyPassword()
    {
        var policy = new PasswordPolicy(); policy.Update(true, 8, false, false, false, false);
        Assert.False(InstallationPasswordValidator.Validate(policy, "1234567").Succeeded);
        Assert.True(InstallationPasswordValidator.Validate(policy, "12345678").Succeeded);
        policy.Update(false, 1024, true, true, true, true);
        foreach (var value in new[] { "x", " ", "2", "!", "ż" }) Assert.True(InstallationPasswordValidator.Validate(policy, value).Succeeded);
        Assert.False(InstallationPasswordValidator.Validate(policy, "").Succeeded);
        Assert.False(InstallationPasswordValidator.Validate(policy, null).Succeeded);
        Assert.True(TemporaryPasswordGenerator.Generate(InstallationPasswordValidator.GeneratorOptions(policy)).Length == 16);
    }
    [Fact]
    public async Task IdentityHasOnlyDynamicValidator_AndReusedManagerSeesPolicyChanges()
    {
        using var db = new SystemDbContextFactory().CreateDbContext([]);
        var services = new ServiceCollection().AddLogging().AddSingleton(db);
        services.AddVatowiecIdentity(); var policies = new FixedPasswordPolicyProvider(); services.AddScoped<IPasswordPolicyProvider>(_ => policies);
        using var provider = services.BuildServiceProvider(); var users = provider.GetRequiredService<UserManager<ApplicationUser>>();
        var validator = Assert.IsType<InstallationPasswordValidator>(Assert.Single(users.PasswordValidators));
        var user = new ApplicationUser(); Assert.False((await validator.ValidateAsync(users, user, "x")).Succeeded);
        policies.Value.Update(false, 12, true, true, true, true);
        Assert.True((await validator.ValidateAsync(users, user, "x")).Succeeded);
        Assert.False((await validator.ValidateAsync(users, user, "")).Succeeded);
    }
    [Fact]
    public void RoleRemovalProtectsSelfAndLastActiveAdministrator()
    {
        var actor = Guid.NewGuid(); var target = Guid.NewGuid();
        Assert.Throws<ArgumentException>(() => UserAdministrationSafety.ValidateRoleRemoval(actor, actor, true, 2));
        Assert.Throws<ArgumentException>(() => UserAdministrationSafety.ValidateRoleRemoval(actor, target, true, 1));
        UserAdministrationSafety.ValidateRoleRemoval(actor, target, true, 2);
        UserAdministrationSafety.ValidateRoleRemoval(actor, target, false, 1);
    }
    [Fact]
    public void PolicyEditorTracksEachIndependentCheckboxAndPreservesVersion()
    {
        var editor = new PasswordPolicyEditor(new(true, 12, true, true, true, true, "version"));
        Assert.False(editor.IsDirty); editor.RequireUppercase = false;
        Assert.True(editor.IsDirty); Assert.True(editor.RequireLowercase && editor.RequireDigit && editor.RequireSpecialCharacter);
        var request = editor.Request(false); Assert.False(request.RequireUppercase); Assert.Equal("version", request.RowVersion);
        editor.RequireUppercase = true; Assert.False(editor.IsDirty); editor.IsEnabled = false; Assert.True(editor.IsDirty);
    }
}
