using Microsoft.Extensions.Configuration;
using Vatowiec.Core.Application;
using Vatowiec.Core.Domain;
using Vatowiec.Core.Infrastructure.Operations;
using Vatowiec.Desktop.Services;
using Vatowiec.Desktop.ViewModels;
using Vatowiec.Operations;
using Vatowiec.SharedContracts;

namespace Vatowiec.Tests;

// ADR-044 krok 2: kopie firmy — testy bez SQL Server.
public sealed class CompanyBackupTests
{
    private static readonly TimeZoneInfo Warsaw = TimeZoneInfo.CreateCustomTimeZone("Test+1", TimeSpan.FromHours(1), "Test+1", "Test+1");
    private static DateTimeOffset Local(int day, int hour) => new(2026, 11, day, hour, 0, 0, TimeSpan.FromHours(1));

    [Theory]
    [InlineData("Daily", 22, 23, true)]      // Następny dzień kalendarzowy, nawet po 1 godzinie.
    [InlineData("Daily", 22, 22, false)]
    [InlineData("Every7Days", 1, 7, false)]
    [InlineData("Every7Days", 1, 8, true)]
    [InlineData("Every30Days", 1, 30, false)]
    [InlineData("Never", 1, 30, false)]
    public void Due_CountsLocalCalendarDaysSinceLastBackup(string frequency, int lastDay, int nowDay, bool due) =>
        Assert.Equal(due, BackupScheduleRules.IsDue(frequency, Local(lastDay, 23), Local(1, 0), Local(nowDay, 0), Warsaw));

    [Fact]
    public void Due_WithoutAnyBackup_CountsFromCompanyCreation_AndAutomaticWaitsForPreferredHour()
    {
        Assert.False(BackupScheduleRules.IsDue(BackupFrequencies.Every30Days, null, Local(1, 9), Local(30, 9), Warsaw));
        Assert.True(BackupScheduleRules.IsDue(BackupFrequencies.Every30Days, null, Local(1, 9), new DateTimeOffset(2026, 12, 1, 9, 0, 0, TimeSpan.FromHours(1)), Warsaw));
        Assert.False(BackupScheduleRules.IsAutomaticWindow(22, Local(5, 21), Warsaw));
        Assert.True(BackupScheduleRules.IsAutomaticWindow(22, Local(5, 22), Warsaw));
        Assert.Equal(new DateTimeOffset(2026, 11, 5, 23, 0, 0, TimeSpan.Zero), BackupScheduleRules.NextLocalDay(Local(5, 14), Warsaw)); // Północ 6.11 czasu lokalnego.
    }

    [Fact]
    public void BackupOperation_HasOwnStages_AndRequiresFileName()
    {
        var operation = CompanyOperation.Backup(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "Firma", null, CompanyBackupService.SchedulerName,
            Guid.NewGuid(), new string('B', 64), @"localhost\SQLEXPRESS", "LocalWindows", "Vatowiec_Firma", "Vatowiec_Firma/Vatowiec_Firma_x.bak");
        Assert.Null(operation.RequestedById);
        var now = DateTimeOffset.UtcNow;
        operation.Claim("worker", now, TimeSpan.FromMinutes(5));
        Assert.Throws<InvalidOperationException>(() => operation.Reach(CompanyOperationStages.DatabaseCreated, now, TimeSpan.FromMinutes(5)));
        foreach (var stage in CompanyOperationStages.Backup.Skip(1)) operation.Reach(stage, now, TimeSpan.FromMinutes(5));
        operation.Complete(now);
        Assert.Equal(CompanyOperationStatuses.Completed, operation.Status);
        Assert.Throws<ArgumentException>(() => CompanyOperation.Backup(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "Firma", null, "x",
            Guid.NewGuid(), new string('B', 64), "s", "p", "d", ""));
    }

    [Fact]
    public void Settings_AreValidated_AndPermissionCatalogIncludesBackupSchedule()
    {
        var schedule = new CompanyBackupSchedule(Guid.NewGuid(), BackupFrequencies.Daily, BackupModes.Automatic, 22);
        Assert.Throws<ArgumentException>(() => schedule.Change("Hourly", BackupModes.Reminder, 1));
        Assert.Throws<ArgumentException>(() => schedule.Change(BackupFrequencies.Daily, "Silent", 1));
        Assert.Throws<ArgumentException>(() => schedule.Change(BackupFrequencies.Daily, BackupModes.Reminder, 24));
        Assert.Equal((BackupFrequencies.Every30Days, BackupModes.Reminder), (CompanyBackupSchedule.DefaultFrequency, CompanyBackupSchedule.DefaultMode));
        Assert.Equal(2047 & ~256, (int)CompanyPermissionCatalog.All); // 256 (ImportExchangeRates) wycofane w ADR-048.
        Assert.Equal(CompanyPermission.ViewProfile, CompanyPermissionCatalog.Required(CompanyPermission.ManageCompanyBackups));
        Assert.Equal("Harmonogram kopii firmy", CompanyPermissionCatalog.Describe(CompanyPermission.ManageCompanyBackups).Name);
    }

    [Fact]
    public void BackupFiles_StayInsideTheConfiguredRoot()
    {
        var name = CompanyBackupService.BackupFileName("Vatowiec_Tenjin", new DateTimeOffset(2026, 11, 22, 13, 5, 9, TimeSpan.Zero),
            Guid.Parse("0123abcd-0000-4000-8000-000000000001"));
        Assert.Equal("Vatowiec_Tenjin/Vatowiec_Tenjin_20261122_130509_0123abcd.bak", name);
        var root = Path.Combine(Path.GetTempPath(), "vatowiec-root-" + Guid.NewGuid().ToString("N"));
        var storage = new BackupStorage(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Operations:BackupRoot"] = root }).Build());
        Assert.StartsWith(root + Path.DirectorySeparatorChar, storage.FullPath(name));
        Assert.EndsWith(".manifest.json", BackupStorage.ManifestPath(storage.FullPath(name)));
        foreach (var invalid in new[] { "../x.bak", "a/../../x.bak", @"C:\x.bak", "" })
            Assert.Equal("backup_path_invalid", Assert.Throws<OperationFailure>(() => storage.FullPath(invalid)).Code);
        var missing = new BackupStorage(new ConfigurationBuilder().Build());
        Assert.Equal("backup_root_missing", Assert.Throws<OperationFailure>(() => missing.FullPath(name)).Code);
    }
}

public sealed partial class DesktopTests
{
    [Fact]
    public async Task Backups_ReminderAfterOpeningCompany_RequestReusesKeyAfterLostResponse_AndScheduleSaves()
    {
        var api = new FakeApi();
        api.BackupData = Status(A, reminderDue: true, canManage: true);
        using var model = new MainViewModel(_ => api) { UserName = "operator" };
        await model.Login("fixture-password");
        model.SelectedCompany = model.Companies[0];
        await model.OpenCompany();
        Assert.True(model.IsBackupReminderVisible);
        Assert.Contains("nie ma jeszcze żadnej kopii", model.BackupReminderText);

        await model.SnoozeBackupReminder();
        Assert.False(model.IsBackupReminderVisible); Assert.Equal(1, api.BackupSnoozes);

        await model.OpenCompanyBackups();
        Assert.True(model.IsCompanyBackupsScreen); Assert.False(model.IsBrowsingWorkspace);
        Assert.True(model.CanRequestBackup); Assert.True(model.CanEditBackupSchedule);
        api.BackupRequestError = new ApiException(0, "Nie można połączyć się z API.");
        await model.RequestCompanyBackup();
        api.BackupRequestError = null;
        await model.RequestCompanyBackup();
        Assert.Equal(2, api.BackupRequests.Count);
        Assert.Equal(api.BackupRequests[0].OperationKey, api.BackupRequests[1].OperationKey); // Ponowienie po utracie odpowiedzi.
        Assert.Contains("Przyjęto zlecenie kopii", model.Message);

        Assert.False(model.CanSaveBackupSchedule);
        model.BackupFrequencyIndex = 1; model.BackupModeIndex = 1; model.BackupHourIndex = 3;
        Assert.True(model.IsBackupHourVisible && model.CanSaveBackupSchedule);
        await model.SaveBackupSchedule();
        var saved = Assert.Single(api.ScheduleWrites);
        Assert.Equal(("Daily", "Automatic", 3, (string?)null), (saved.Frequency, saved.Mode, saved.PreferredHour, saved.RowVersion));
        Assert.False(model.CanSaveBackupSchedule);

        model.CloseCompanyBackups();
        Assert.False(model.IsCompanyBackupsScreen); Assert.True(model.IsBrowsingWorkspace || !model.CanViewContractors);
    }

    [Fact]
    public async Task Backups_ScheduleIsReadOnlyWithoutRight_AndRequestsHiddenWhenPolicyDisallows()
    {
        var api = new FakeApi { BackupData = Status(A, reminderDue: false, canManage: false, canRequest: false) };
        using var model = new MainViewModel(_ => api) { UserName = "operator" };
        await model.Login("fixture-password");
        model.SelectedCompany = model.Companies[0];
        await model.OpenCompany();
        Assert.False(model.IsBackupReminderVisible);
        await model.OpenCompanyBackups();
        Assert.False(model.CanRequestBackup); Assert.False(model.CanEditBackupSchedule);
        model.BackupFrequencyIndex = 0;
        await model.SaveBackupSchedule(); await model.RequestCompanyBackup();
        Assert.Empty(api.ScheduleWrites); Assert.Empty(api.BackupRequests);
    }

    [Fact]
    public async Task BackupPolicy_IsEditedOnInstallationCompaniesScreen()
    {
        var api = new FakeApi { InstallationAdmin = true };
        using var model = new MainViewModel(_ => api) { UserName = "admin" };
        await model.Login("fixture-password"); await model.OpenCompanyOperations();
        Assert.True(model.HasBackupPolicy); Assert.Equal("7", model.PolicyRetained); Assert.False(model.CanSaveBackupPolicy);
        model.PolicyRetained = "0"; Assert.False(model.CanSaveBackupPolicy);
        model.PolicyRetained = "14"; model.PolicyUsersMayRequest = false; Assert.True(model.CanSaveBackupPolicy);
        await model.SaveBackupPolicy();
        Assert.Equal(new BackupPolicyRequest(false, 14, "policy-v1"), Assert.Single(api.PolicyWritesBackup));
        Assert.False(model.CanSaveBackupPolicy);
    }

    private static CompanyBackupStatusResponse Status(Guid company, bool reminderDue, bool canManage, bool canRequest = true) =>
        new(company, null, [], new("Every30Days", "Reminder", 22, null, true), canRequest, canManage, reminderDue, null, 7);

    private sealed partial class FakeApi
    {
        public CompanyBackupStatusResponse? BackupData;
        public ApiException? BackupRequestError;
        public List<CompanyBackupRequest> BackupRequests { get; } = [];
        public List<CompanyBackupScheduleRequest> ScheduleWrites { get; } = [];
        public List<BackupPolicyRequest> PolicyWritesBackup { get; } = [];
        public int BackupSnoozes;
        private BackupPolicyResponse backupPolicy = new(true, 7, "policy-v1");
        public Task<CompanyBackupStatusResponse> CompanyBackups(Guid companyId) =>
            BackupData is null ? throw new NotSupportedException() : Task.FromResult(BackupData with { CompanyId = companyId });
        public Task<CompanyOperationResponse> RequestCompanyBackup(Guid companyId, CompanyBackupRequest value)
        {
            BackupRequests.Add(value);
            if (BackupRequestError != null) throw BackupRequestError;
            return Task.FromResult(new CompanyOperationResponse(Guid.NewGuid(), "Backup", companyId, "Firma", "Requested", "Pending", 0, null,
                "operator", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, null, "v1"));
        }
        public Task<CompanyBackupStatusResponse> SaveBackupSchedule(Guid companyId, CompanyBackupScheduleRequest value)
        {
            ScheduleWrites.Add(value);
            BackupData = BackupData! with { Schedule = new(value.Frequency, value.Mode, value.PreferredHour, "schedule-v1", false) };
            return CompanyBackups(companyId);
        }
        public Task<CompanyBackupStatusResponse> SnoozeBackupReminder(Guid companyId)
        { BackupSnoozes++; BackupData = BackupData! with { ReminderDue = false }; return CompanyBackups(companyId); }
        public Task<BackupPolicyResponse> BackupPolicy() => Task.FromResult(backupPolicy);
        public Task<BackupPolicyResponse> SaveBackupPolicy(BackupPolicyRequest value)
        { PolicyWritesBackup.Add(value); return Task.FromResult(backupPolicy = new(value.UsersMayRequestBackups, value.RetainedBackupsPerCompany, "policy-v2")); }
    }
}
