using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Vatowiec.Core.Infrastructure.Identity;
using Vatowiec.Core.Infrastructure.Persistence;
using Vatowiec.Desktop.Services;
using Vatowiec.SharedContracts;

namespace Vatowiec.Tests;

public sealed class TemporaryPasswordTests
{
    [Fact]
    public async Task GeneratorProduces16UnambiguousCharacters_AndPassesActualIdentityValidators()
    {
        using var db = new SystemDbContextFactory().CreateDbContext([]);
        var services = new ServiceCollection().AddLogging().AddSingleton(db);
        services.AddVatowiecIdentity();
        services.AddScoped<IPasswordPolicyProvider>(_ => new FixedPasswordPolicyProvider());
        using var provider = services.BuildServiceProvider();
        var users = provider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = new ApplicationUser { UserName = "fixture" };
        for (var attempt = 0; attempt < 128; attempt++)
        {
            var password = TemporaryPasswordGenerator.Generate(InstallationPasswordValidator.GeneratorOptions(new()));
            Assert.True(password.Length == 16);
            Assert.True(password.All(c => !"IiLlOo01 -".Contains(c)));
            Assert.True(password.Any(char.IsUpper) && password.Any(char.IsLower) && password.Any(char.IsDigit) && password.Any(c => !char.IsLetterOrDigit(c)));
            foreach (var validator in users.PasswordValidators) Assert.True((await validator.ValidateAsync(users, user, password)).Succeeded);
        }
    }
    [Fact]
    public void GeneratorHonorsStricterLengthAndUniqueCharacterRequirements()
    {
        var policy = new PasswordOptions { RequiredLength = 20, RequiredUniqueChars = 20 };
        var password = TemporaryPasswordGenerator.Generate(policy);
        Assert.True(password.Length == 20 && password.Distinct().Count() == 20);
        Assert.Throws<ArgumentException>(() => TemporaryPasswordGenerator.Generate(new() { RequiredUniqueChars = 1025 }));
    }
    [Fact]
    public void GroupsArePresentationOnly_AndCustomPasswordIsPreservedExactly()
    {
        const string raw = "AbcD2345EfGh678!";
        Assert.True(TemporaryPasswordPresentation.Format(raw, true) == "AbcD 2345 EfGh 678!");
        const string custom = " Own Password!2a ";
        Assert.True(TemporaryPasswordPresentation.Format(custom, false) == custom);
    }
    [Fact]
    public void PasswordRequestsDoNotRevealSecretsInDiagnosticText()
    {
        var secret = "Fixture!1a-" + Guid.NewGuid().ToString("N");
        var creationLeaks = new UserCreateRequest("fixture", secret).ToString().Contains(secret);
        var resetLeaks = new UserVersionRequest("version", true, secret).ToString().Contains(secret);
        Assert.False(creationLeaks); Assert.False(resetLeaks);
    }
}
