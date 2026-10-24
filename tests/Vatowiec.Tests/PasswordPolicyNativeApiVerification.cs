using System.Net;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.VisualTree;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Vatowiec.Core.Infrastructure.Persistence;
using Vatowiec.Desktop;
using Vatowiec.Desktop.Services;
using Vatowiec.Desktop.ViewModels;
using static Vatowiec.Tests.InstallationSecurityUiTests;

namespace Vatowiec.Tests;

public sealed partial class UserAdministrationSqlTests
{
    // Called by the single native Avalonia host when explicitly enabled, to avoid initializing
    // Avalonia twice in one test process. Uses real Kestrel/HTTPS/Identity/SQL, no API doubles.
    internal static async Task VerifyPolicyThroughNativeWindow(IClassicDesktopStyleApplicationLifetime lifetime)
    {
        TracePolicyApi("Starting native UI / real HTTPS API / rollback-only SQL verification.");
        await using var snapshot = await PreservedSystemTransaction.Open();
        await using var originalDb = snapshot.Context();
        var original = UserAdministrationServicePolicy(await originalDb.PasswordPolicies.AsNoTracking().SingleAsync());
        var auditCount = await originalDb.PasswordPolicyChanges.CountAsync();
        var originalUsers = await originalDb.Users.CountAsync(); var originalUserAudit = await originalDb.UserAdministrationChanges.CountAsync();
        await using (var tx = await PreservedSystemTransaction.Open())
        {
            await using var app = App(tx);
            app.UseKestrel(options => options.Listen(IPAddress.Loopback, 0, listen => listen.UseHttps()));
            await Task.Run(app.StartServer);
            TracePolicyApi("Real Kestrel HTTPS server started.");
            var address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
            var url = new UriBuilder(address) { Host = "localhost" }.Uri.GetLeftPart(UriPartial.Authority);
            using var scope = app.Services.CreateScope();
            var password = ("Fixture!1a-" + Guid.NewGuid().ToString("N")).PadRight(1024, 'a');
            var actor = await Account(scope.ServiceProvider, true, password);
            var archiveFixture = await Account(scope.ServiceProvider, false, password);
            TracePolicyApi("Fictional administrator created in rollback-only transaction.");
            using var transport = VatowiecApiClient.Connect(url); // Standard certificate validation, cookie and CSRF.
            using var model = new MainViewModel(_ => transport) { ApiAddress = url, UserName = actor.UserName! };
            var window = new MainWindow(model); var previousWindow = lifetime.MainWindow; lifetime.MainWindow = window;
            try
            {
                window.Show(); await Task.Delay(100); await model.Login(password);
                TracePolicyApi("Native Avalonia login completed through HTTPS API.");
                Assert.True(model.IsInstallationAdministrator); await model.OpenPasswordPolicy();
                await Until(() => window.GetVisualDescendants().OfType<CheckBox>().Any(x =>
                    AutomationProperties.GetAutomationId(x) == "PasswordPolicyEnabled" && x.IsEffectivelyVisible));
                Assert.True(model.HasPasswordPolicy && model.HasPolicyHistory);
                TracePolicyApi("GET policy and audit history loaded in native window.");
                Assert.False(Find<Button>(window, "SavePasswordPolicy").IsEffectivelyEnabled);
                var loaded = (await transport.PasswordPolicy());
                Assert.Equal(loaded.MinimumLength, model.PasswordPolicyEditor!.MinimumLength);
                Assert.Equal(loaded.RequireDigit, Find<CheckBox>(window, "PasswordRequireDigit").IsChecked);
                // Only this connection-bound, rollback-only configuration can observe the changes.
                if (!loaded.IsEnabled) Find<CheckBox>(window, "PasswordPolicyEnabled").IsChecked = true;
                var changedLength = loaded.MinimumLength == 1024 ? 1023 : loaded.MinimumLength + 1;
                Find<NumericUpDown>(window, "PasswordMinimumLength").Value = changedLength;
                Assert.True(Find<Button>(window, "SavePasswordPolicy").IsEffectivelyEnabled);
                Click(window, "SavePasswordPolicy");
                await Until(() => !model.IsBusy && !model.HasUnsavedChanges);
                Assert.Contains("Zapisano politykę", model.Message);
                TracePolicyApi("PUT completed; GET reloaded saved values; save button disabled.");
                Assert.False(Find<Button>(window, "SavePasswordPolicy").IsEffectivelyEnabled);
                Assert.Equal(changedLength, (await transport.PasswordPolicy()).MinimumLength);
                var history = await transport.PasswordPolicyHistory(0);
                Assert.Contains(history, x => x.ActorName == actor.UserName && x.After.MinimumLength == changedLength);
                await Until(() => Find<ItemsControl>(window, "PasswordPolicyHistory").GetVisualDescendants().OfType<TextBlock>()
                    .Any(x => x.Text?.Contains(actor.UserName!, StringComparison.Ordinal) == true));
                Click(window, "ClosePasswordPolicy");
                await Until(() => window.GetVisualDescendants().OfType<Button>().Any(x =>
                    AutomationProperties.GetAutomationId(x) == "OpenPasswordPolicy" && x.IsEffectivelyVisible));
                Click(window, "OpenPasswordPolicy");
                await Until(() => !model.IsBusy && model.IsPasswordPolicyScreen && model.HasPasswordPolicy);
                Assert.Equal(changedLength, model.PasswordPolicyEditor!.MinimumLength);
                Assert.True(model.HasPolicyHistory); Assert.False(model.CanSavePasswordPolicy);
                Assert.Contains(model.PasswordPolicyHistory, x => x.Actor.Contains(actor.UserName!, StringComparison.Ordinal));
                TracePolicyApi("Screen reopened; saved values and real audit entry verified.");
                Click(window, "ClosePasswordPolicy");
                await model.OpenUsersSettings();
                await Until(() => window.GetVisualDescendants().OfType<ListBox>().Any(x => AutomationProperties.GetAutomationId(x) == "UsersList" && x.IsEffectivelyVisible));
                Find<ListBox>(window, "UsersList").SelectedItem = model.AdminUsers.Single(x => x.Id == archiveFixture.Id);
                await Until(() => !model.IsBusy && model.HasAdminUser);
                Click(window, "ArchiveUser"); await Dialog(lifetime, "ConfirmAdministration");
                await Until(() => !model.IsBusy && model.Message.StartsWith("Usunięto użytkownika", StringComparison.Ordinal));
                Assert.DoesNotContain(model.AdminUsers, x => x.Id == archiveFixture.Id);
                Find<CheckBox>(window, "ShowArchivedUsers").IsChecked = true;
                await Until(() => !model.IsBusy && model.AdminUsers.Any(x => x.Id == archiveFixture.Id && x.IsArchived));
                Find<ListBox>(window, "UsersList").SelectedItem = model.AdminUsers.Single(x => x.Id == archiveFixture.Id);
                await Until(() => !model.IsBusy && model.IsArchivedAdminUser);
                Assert.True(Find<Button>(window, "RestoreSameUser").IsEffectivelyEnabled);
                Assert.True(Find<Button>(window, "ReuseArchivedLogin").IsEffectivelyEnabled);
                Click(window, "RestoreSameUser");
                await Until(() => lifetime.Windows.Any(x => x.Title == "To ta sama osoba — przywróć konto"));
                Click(lifetime.Windows.Single(x => x.Title == "To ta sama osoba — przywróć konto"), "AcceptTemporaryPassword");
                await CloseGenerated(lifetime);
                Assert.Equal(archiveFixture.Id, model.SelectedAdminUser!.Id); Assert.False(model.IsArchivedAdminUser);
                Assert.All(model.AdminCompanyAccess, x => Assert.False(x.HasAccess));
                Click(window, "ArchiveUser"); await Dialog(lifetime, "ConfirmAdministration");
                await Until(() => !model.IsBusy && model.IsArchivedAdminUser);
                Click(window, "ReuseArchivedLogin");
                await Until(() => lifetime.Windows.Any(x => x.Title == "To nowa osoba — wykorzystaj login"));
                Click(lifetime.Windows.Single(x => x.Title == "To nowa osoba — wykorzystaj login"), "AcceptTemporaryPassword");
                await CloseGenerated(lifetime);
                Assert.NotEqual(archiveFixture.Id, model.SelectedAdminUser!.Id); Assert.Equal(archiveFixture.UserName, model.SelectedAdminUser.UserName);
                Assert.False(model.SelectedAdminUser.IsInstallationAdministrator); Assert.All(model.AdminCompanyAccess, x => Assert.False(x.HasAccess));
                var oldAccount = await transport.UserDetail(archiveFixture.Id); Assert.True(oldAccount.User.IsArchived && oldAccount.User.IsLoginReleased);
                TracePolicyApi("Native archive filter, same-person restore and new-person login reuse verified through real API; identities and permissions remain separate.");
            }
            finally
            {
                if (model.HasUnsavedChanges) { window.Close(); model.ConfirmDiscard(); } else window.Close();
                lifetime.MainWindow = previousWindow;
            }
        }
        // Separate real connection after rollback: neither the policy/version nor audit was changed.
        Assert.Equal(original, UserAdministrationServicePolicy(await originalDb.PasswordPolicies.AsNoTracking().SingleAsync()));
        Assert.Equal(auditCount, await originalDb.PasswordPolicyChanges.CountAsync());
        Assert.Equal(originalUsers, await originalDb.Users.CountAsync()); Assert.Equal(originalUserAudit, await originalDb.UserAdministrationChanges.CountAsync());
        TracePolicyApi("Transaction rolled back; original policy including RowVersion and audit count unchanged.");
    }
    private static async Task CloseGenerated(IClassicDesktopStyleApplicationLifetime lifetime)
    {
        await Until(() => lifetime.Windows.Any(x => x.Title == "Jednorazowe hasło tymczasowe"));
        Click(lifetime.Windows.Single(x => x.Title == "Jednorazowe hasło tymczasowe"), "CloseTemporaryPassword");
        await Until(() => lifetime.Windows.All(x => x.Title != "Jednorazowe hasło tymczasowe"));
    }
    private static void TracePolicyApi(string value)
    {
        var path = Environment.GetEnvironmentVariable("VATOWIEC_POLICY_TRACE");
        if (!string.IsNullOrEmpty(path)) File.AppendAllText(path, value + Environment.NewLine);
    }
    private static Vatowiec.SharedContracts.PasswordPolicyResponse UserAdministrationServicePolicy(Vatowiec.Core.Domain.PasswordPolicy policy) =>
        Vatowiec.Core.Infrastructure.Identity.UserAdministrationService.PolicyResponse(policy);
}
