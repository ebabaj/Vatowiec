using Vatowiec.Core.Application;
using Vatowiec.Core.Domain;
using Vatowiec.Core.Infrastructure.Identity;
using Vatowiec.Desktop.ViewModels;
using Vatowiec.Desktop.Services;
using Vatowiec.SharedContracts;

namespace Vatowiec.Tests;

public sealed class UserAdministrationTests
{
    [Fact]
    public void PermissionCatalogCoversEveryEnumAndRejectsInvalidDependencies()
    {
        var definitions = UserAdministrationService.Lookups([]).Permissions;
        Assert.Equal(Enum.GetValues<CompanyPermission>().Where(x => x != CompanyPermission.None).Select(x => x.ToString()), definitions.Select(x => x.Code));
        Assert.All(definitions, x => { Assert.False(string.IsNullOrWhiteSpace(x.Name)); Assert.False(string.IsNullOrWhiteSpace(x.Description)); });
        Assert.Throws<UserAdministrationException>(() => UserAdministrationService.Parse(["EditContractors"]));
        Assert.Throws<UserAdministrationException>(() => UserAdministrationService.Parse(["ViewProfile", "ManageCurrencies"]));
        Assert.Throws<UserAdministrationException>(() => UserAdministrationService.Parse(["1023"]));
        Assert.Throws<UserAdministrationException>(() => UserAdministrationService.Parse(["ViewProfile", "ViewProfile"]));
        Assert.Equal(CompanyPermission.None, UserAdministrationService.Parse([]));
        Assert.Equal(CompanyPermissionCatalog.All, UserAdministrationService.Parse(definitions.Select(x => x.Code).ToArray()));
    }
    [Fact]
    public void SafetyProtectsSelfAndLastActiveAdministrator()
    {
        var actor = Guid.NewGuid(); var other = Guid.NewGuid();
        Assert.Throws<ArgumentException>(() => UserAdministrationSafety.ValidateBlock(actor, actor, true, 2));
        Assert.Throws<ArgumentException>(() => UserAdministrationSafety.ValidateBlock(actor, other, true, 1));
        Assert.Throws<ArgumentException>(() => UserAdministrationSafety.ValidateBlock(actor, other, true, 0));
        UserAdministrationSafety.ValidateBlock(actor, other, true, 2);
        UserAdministrationSafety.ValidateBlock(actor, other, false, 1);
    }
    [Fact]
    public void CompanyEditorKeepsBindingStableAndAddsOrRemovesDependencies()
    {
        var definitions = UserAdministrationService.Lookups([]).Permissions;
        var editor = new CompanyAccessEditor(new(Guid.NewGuid(), "Firma A"), definitions, null);
        Assert.False(editor.IsDirty); var choices = editor.Permissions;
        editor.HasAccess = true; Assert.True(editor.IsDirty);
        choices.Single(x => x.Code == "EditContractors").IsSelected = true;
        Assert.Contains("ViewContractors", editor.Codes); Assert.Contains("ViewProfile", editor.Codes);
        choices.Single(x => x.Code == "ViewContractors").IsSelected = false;
        Assert.DoesNotContain("EditContractors", editor.Codes);
        editor.HasAccess = false; Assert.Empty(editor.Request().Permissions); Assert.False(editor.IsDirty); Assert.Same(choices, editor.Permissions);
    }
}

public sealed partial class DesktopTests
{
    [Fact]
    public async Task UserAdministrationRequiresInstallationRole_AndKeepsDirtyDraftOnConflictOrSwitch()
    {
        var api = new FakeApi { InstallationAdmin = true };
        using var model = new MainViewModel(_ => api) { UserName = "admin" };
        await model.Login("fixture-password"); await model.OpenUsersSettings();
        var stableList = model.AdminUsers;
        model.SelectedAdminUser = model.AdminUsers[0]; Assert.True(model.HasAdminUser);
        model.AdminCompanyAccess[0].HasAccess = true; Assert.True(model.HasUnsavedChanges);
        model.SelectedAdminUser = model.AdminUsers[1]; Assert.True(model.DiscardPrompt); Assert.Equal(api.AdminOne, model.SelectedAdminUser!.Id);
        model.KeepEditing(); api.AdminConflict = true;
        await model.SaveAdminUserAccess(); Assert.True(model.HasUnsavedChanges); Assert.Contains("zmienił", model.Message);
        api.AdminConflict = false; await model.SaveAdminUserAccess(); Assert.False(model.HasUnsavedChanges);
        Assert.Same(stableList, model.AdminUsers); Assert.Equal(api.AdminOne, model.SelectedAdminUser!.Id);
        model.SelectedAdminUser = null; Assert.Equal(api.AdminOne, model.SelectedAdminUser!.Id); // Ignore transient control rebinding.
        model.AdminCompanyAccess[1].HasAccess = true; model.CloseUsersSettings(); Assert.True(model.DiscardPrompt);
        model.ConfirmDiscard(); Assert.False(model.IsUsersSettings); Assert.False(model.HasUnsavedChanges);
        using var denied = new MainViewModel(_ => new FakeApi()) { UserName = "ordinary" };
        await denied.Login("fixture-password"); await denied.OpenUsersSettings(); Assert.False(denied.IsUsersSettings);
    }
    [Fact]
    public async Task TemporaryPasswordLoginCannotReadCompaniesOrRememberTemporaryPassword_ChangeRequiresNewLogin()
    {
        var api = new FakeApi { PasswordChangeRequired = true }; var secrets = new FakeLoginCredentials();
        var preferences = new FakeLoginPreferences(); using var model = RememberingModel(api, preferences, secrets);
        model.RememberCredentials = true; model.AutomaticLogin = true; await model.Login("temporary-fixture-password");
        Assert.True(model.IsOwnPasswordChange); Assert.False(model.IsCompanySelection); Assert.Empty(model.Companies);
        Assert.False(model.CanCancelPasswordChange); Assert.Equal(0, secrets.Writes); Assert.Empty(api.ProfileCalls);
        await model.ChangeOwnPassword("temporary-fixture-password", "replacement-fixture-password");
        Assert.True(model.IsLogin); Assert.Equal(1, api.PasswordChanges); Assert.Equal(0, secrets.Writes + secrets.Deletes);
    }
    [Fact]
    public async Task AdministrationStateClearsWhenServerDeniesRoleOrRevokesSession()
    {
        var api = new FakeApi { InstallationAdmin = true }; using var model = new MainViewModel(_ => api) { UserName = "admin" };
        await model.Login("fixture-password"); await model.OpenUsersSettings(); model.SelectedAdminUser = model.AdminUsers[0];
        api.AdminError = new ApiException(403, "Brak roli."); await model.RefreshAdminUser();
        Assert.False(model.IsUsersSettings); Assert.False(model.IsInstallationAdministrator); Assert.Empty(model.AdminCompanyAccess);
    }
    private sealed partial class FakeApi
    {
        public Guid AdminOne { get; } = Guid.NewGuid();
        private readonly Guid adminTwo = Guid.NewGuid();
        public bool AdminConflict { get; set; }
        public ApiException? AdminError { get; set; }
        public Exception? RoleError { get; set; }
        public Exception? HistoryError { get; set; }
        public ApiException? RoleReadbackError { get; set; }
        public bool? RoleReadbackOverride { get; set; }
        public int RoleWrites { get; private set; }
        public int UserDetailReads { get; private set; }
        public bool PasswordChangeRequired { get; init; }
        public int PasswordChanges { get; private set; }
        private UserAdministrationDetail? adminDetail;
        public Task<UserAdministrationLookups> UserLookups() => Task.FromResult(UserAdministrationService.Lookups([new(A, "Firma A"), new(B, "Firma B")]));
        public Task<UserSummary[]> Users(string search, int offset, bool includeArchived = false) => Task.FromResult<UserSummary[]>([new(AdminOne, "operator", false, false, false), new(adminTwo, "reader", false, false, false)]);
        public Task<UserAdministrationDetail> UserDetail(Guid id)
        {
            UserDetailReads++;
            if (AdminError != null) throw AdminError;
            if (RoleWrites > 0 && RoleReadbackError != null) throw RoleReadbackError;
            var detail = adminDetail?.User.Id == id ? adminDetail : new(new(id, "operator", false, false, false), "version", []);
            if (RoleWrites > 0 && RoleReadbackOverride.HasValue) detail = detail with { User = detail.User with { IsInstallationAdministrator = RoleReadbackOverride.Value } };
            return Task.FromResult(detail);
        }
        public Task<UserAdministrationHistory[]> UserHistory(Guid? id, int offset)
        { if (HistoryError != null) throw HistoryError; return Task.FromResult<UserAdministrationHistory[]>([]); }
        public Task<UserAdministrationDetail> SaveAdministratorRole(Guid id, AdministratorRoleRequest value)
        {
            if (RoleError != null) throw RoleError;
            RoleWrites++;
            adminDetail = new(new(id, "operator", false, false, value.IsAdministrator), "role-version-" + RoleWrites, adminDetail?.Access ?? []);
            return Task.FromResult(adminDetail);
        }
        public Task<UserAdministrationDetail> SaveUserAccess(Guid id, UserAccessRequest value)
        {
            if (AdminConflict) throw new ApiException(409, "Inny administrator zmienił konto. Odśwież szczegóły.");
            adminDetail = new(new(id, "operator", false, false, false), "next-version", value.Companies.Where(x => x.Permissions.Length != 0).Select(x => new UserCompanyAccess(x.CompanyId, x.Permissions, "row-version")).ToArray());
            return Task.FromResult(adminDetail);
        }
        public Task ChangeOwnPassword(OwnPasswordRequest value) { PasswordChanges++; return Task.CompletedTask; }
    }
}
