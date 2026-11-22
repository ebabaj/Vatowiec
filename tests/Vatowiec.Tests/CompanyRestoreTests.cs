using Vatowiec.Core.Domain;
using Vatowiec.Desktop.ViewModels;
using Vatowiec.SharedContracts;

namespace Vatowiec.Tests;

// ADR-044 krok 3: odtwarzanie — testy bez SQL Server.
public sealed class CompanyRestoreTests
{
    private static CompanyOperation Restore(string mode, string? previous) => CompanyOperation.Restore(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
        "Firma", Guid.NewGuid(), "admin", Guid.NewGuid(), new string('R', 64), @"localhost\SQLEXPRESS", "LocalWindows", "Vatowiec_Firma",
        "Vatowiec_Firma/Vatowiec_Firma_x.bak", mode, "Vatowiec_Firma_odtwarzanie_12345678", previous);

    [Fact]
    public void RestoreOperation_RequiresPreviousNameOnlyForExistingCompany_AndHasItsOwnStages()
    {
        Assert.Equal("Vatowiec_Firma_przed_1", Restore(RestoreModes.Existing, "Vatowiec_Firma_przed_1").PreviousDatabaseName);
        Assert.Throws<ArgumentException>(() => Restore(RestoreModes.Existing, null));
        Assert.Throws<ArgumentException>(() => Restore(RestoreModes.Deleted, "Vatowiec_Firma_przed_1"));
        Assert.Throws<ArgumentException>(() => Restore("Clone", null));
        var import = Restore(RestoreModes.Import, null);
        Assert.False(import.CanCancel);
        var now = DateTimeOffset.UtcNow;
        import.Claim("worker", now, TimeSpan.FromMinutes(5));
        foreach (var stage in CompanyOperationStages.Restore.Skip(1)) import.Reach(stage, now, TimeSpan.FromMinutes(5));
        import.Complete(now);
        Assert.Equal(CompanyOperationStatuses.Completed, import.Status);
    }

    [Fact]
    public void WorkingAndPreviousNames_AreSafeProjectNamesWithinSqlLimit()
    {
        var id = Guid.Parse("0123abcd-0000-4000-8000-000000000001");
        Assert.Equal("Vatowiec_Tenjin_odtwarzanie_0123abcd", CompanyDatabaseNames.Working("Vatowiec_Tenjin", id));
        Assert.Equal("Vatowiec_Tenjin_przed_20261122_1530", CompanyDatabaseNames.Previous("Vatowiec_Tenjin", new DateTime(2026, 11, 22, 15, 30, 0)));
        var longest = CompanyDatabaseNames.FromCompanyName(new string('a', 120))!;
        foreach (var name in new[] { CompanyDatabaseNames.Working(longest, id), CompanyDatabaseNames.Previous(longest, DateTime.Now) })
        {
            Assert.True(name.Length <= 128);
            Assert.True(CompanyDatabaseNames.IsProjectDatabase(name));
        }
        Assert.False(CompanyDatabaseNames.IsProjectDatabase("Vatowiec_System"));
        Assert.False(CompanyDatabaseNames.IsProjectDatabase("master"));
    }
}

public sealed partial class DesktopTests
{
    [Fact]
    public async Task Restore_ExistingCompanyIsConfirmedAndSentWithReplaceFlag_DeletedCompanyWithoutIt()
    {
        var api = new FakeApi { InstallationAdmin = true };
        api.RestoreSourceData =
        [
            new("Backup", 7, A, "Tenjin", null, DateTimeOffset.UtcNow, 6_000_000, "Regular", true),
            new("Deleted", null, B, "Usunięta", null, DateTimeOffset.UtcNow, 5_000_000, "Archive", false)
        ];
        using var model = new MainViewModel(_ => api) { UserName = "admin" };
        await model.Login("fixture-password"); await model.OpenCompanyOperations();
        Assert.Equal(2, model.RestoreSources.Length);
        Assert.False(model.CanRequestRestore);

        model.SelectedRestoreSource = model.RestoreSources[0];
        model.RequestRestore();
        Assert.True(model.IsRestoreConfirmVisible);
        Assert.Contains("zostaną zastąpione", model.RestoreConfirmText);
        Assert.Contains("_przed_", model.RestoreConfirmText);
        await model.ConfirmRestore();
        var existing = Assert.Single(api.RestoreRequests);
        Assert.Equal(("Backup", (long?)7, (Guid?)null, true), (existing.Source, existing.BackupId, existing.CompanyId, existing.ConfirmReplace));
        Assert.Contains("Przyjęto zlecenie odtworzenia", model.Message);

        model.SelectedRestoreSource = model.RestoreSources.Single(x => x.Value.Source == "Deleted");
        model.RequestRestore(); model.CancelRestoreConfirmation();
        Assert.Single(api.RestoreRequests);
        model.RequestRestore(); await model.ConfirmRestore();
        Assert.Equal(("Deleted", (Guid?)B, false), (api.RestoreRequests[1].Source, api.RestoreRequests[1].CompanyId, api.RestoreRequests[1].ConfirmReplace));
        Assert.NotEqual(api.RestoreRequests[0].OperationKey, api.RestoreRequests[1].OperationKey);
    }

    private sealed partial class FakeApi
    {
        public RestoreSourceResponse[] RestoreSourceData = [];
        public List<RestoreCompanyRequest> RestoreRequests { get; } = [];
        public Task<RestoreSourceResponse[]> RestoreSources() => Task.FromResult(RestoreSourceData);
        public Task<CompanyOperationResponse> RestoreCompany(RestoreCompanyRequest value)
        {
            RestoreRequests.Add(value);
            return Task.FromResult(new CompanyOperationResponse(Guid.NewGuid(), "Restore", value.CompanyId ?? A, "Firma", "Requested", "Pending", 0, null,
                "admin", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, null, "v1"));
        }
    }
}
