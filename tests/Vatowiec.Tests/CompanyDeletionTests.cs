using Vatowiec.Core.Domain;
using Vatowiec.Desktop.Services;
using Vatowiec.Desktop.ViewModels;
using Vatowiec.SharedContracts;

namespace Vatowiec.Tests;

// ADR-045: usuwanie firmy — testy bez SQL Server.
public sealed class CompanyDeletionTests
{
    private static CompanyOperation Delete(string? archive = "_archive/Vatowiec_X/Vatowiec_X_1.bak") => CompanyOperation.Delete(Guid.NewGuid(), Guid.NewGuid(),
        Guid.NewGuid(), "Firma", Guid.NewGuid(), "admin", Guid.NewGuid(), new string('D', 64), @"localhost\SQLEXPRESS", "LocalWindows", "Vatowiec_X", archive);

    [Fact]
    public void DeleteOperation_RecordsArchiveChoice_AndHasItsOwnStages()
    {
        Assert.Equal((true, "_archive/Vatowiec_X/Vatowiec_X_1.bak"), (Delete().CreateArchive, Delete().BackupFileName));
        var withoutArchive = Delete(null);
        Assert.Equal((false, (string?)null), (withoutArchive.CreateArchive, withoutArchive.BackupFileName));
        var now = DateTimeOffset.UtcNow;
        withoutArchive.Claim("worker", now, TimeSpan.FromMinutes(5));
        foreach (var stage in CompanyOperationStages.Delete.Skip(1)) withoutArchive.Reach(stage, now, TimeSpan.FromMinutes(5));
        Assert.Equal(CompanyOperationStages.Ready, withoutArchive.Stage);
    }

    [Fact]
    public void Cancel_IsPossibleOnlyBeforeTheDatabaseIsDropped_AndNeverWhileRunning()
    {
        var now = DateTimeOffset.UtcNow;
        var pending = Delete();
        Assert.True(pending.CanCancel);
        pending.Cancel();
        Assert.Equal(CompanyOperationStatuses.Cancelled, pending.Status);
        Assert.False(pending.IsClaimable(now.AddYears(1))); Assert.False(pending.CanRetry); Assert.False(pending.CanCancel);

        var running = Delete();
        running.Claim("worker", now, TimeSpan.FromMinutes(5));
        Assert.False(running.CanCancel);
        running.Reach(CompanyOperationStages.CompanyDeactivated, now, TimeSpan.FromMinutes(5));
        running.Fail("sql_1205", now);
        Assert.True(running.CanCancel);

        var dropped = Delete();
        dropped.Claim("worker", now, TimeSpan.FromMinutes(5));
        foreach (var stage in new[] { CompanyOperationStages.CompanyDeactivated, CompanyOperationStages.ArchiveWritten, CompanyOperationStages.ArchiveVerified, CompanyOperationStages.DatabaseDropped })
            dropped.Reach(stage, now, TimeSpan.FromMinutes(5));
        dropped.RequireReconciliation("operation_failed");
        Assert.False(dropped.CanCancel); Assert.True(dropped.CanRetry); // Po usunięciu bazy można tylko dokończyć.
        Assert.Throws<InvalidOperationException>(dropped.Cancel);

        var provision = CompanyProvisioningTests.NewOperation();
        Assert.False(provision.CanCancel);
    }
}

public sealed partial class DesktopTests
{
    [Fact]
    public async Task CompanyDelete_AsksOnceWithArchive_AndWarnsAgainWithoutArchive()
    {
        var api = new FakeApi { InstallationAdmin = true };
        using var model = new MainViewModel(_ => api) { UserName = "admin" };
        await model.Login("fixture-password"); await model.OpenCompanyOperations();
        Assert.Equal(["Firma do usunięcia"], model.InstallationCompanies.Select(x => x.Name));
        Assert.False(model.CanRequestCompanyDelete);
        model.CompanyToDelete = model.InstallationCompanies[0];
        Assert.True(model.DeleteWithArchive); // Domyślnie z kopią archiwalną.

        model.RequestCompanyDelete();
        Assert.True(model.IsDeleteConfirmVisible); Assert.Contains("Czy na pewno chcesz usunąć firmę „Firma do usunięcia”", model.DeleteConfirmText);
        await model.ConfirmCompanyDelete();
        var withArchive = Assert.Single(api.DeleteRequests);
        Assert.Equal((true, false), (withArchive.CreateArchive, withArchive.ConfirmWithoutArchive));
        Assert.Contains("Przyjęto zlecenie usunięcia", model.Message);

        model.CompanyToDelete = model.InstallationCompanies[0];
        model.DeleteWithArchive = false;
        model.RequestCompanyDelete(); await model.ConfirmCompanyDelete();
        Assert.True(model.IsDeleteWithoutArchiveWarningVisible); Assert.Contains("nieodwracalne", model.DeleteWithoutArchiveText);
        Assert.Single(api.DeleteRequests); // Ostrzeżenie przed wysłaniem.
        model.CancelCompanyDeleteConfirmation();
        Assert.False(model.IsDeleteWithoutArchiveWarningVisible); Assert.Single(api.DeleteRequests);
        model.RequestCompanyDelete(); await model.ConfirmCompanyDelete(); await model.ConfirmCompanyDelete();
        Assert.Equal((false, true), (api.DeleteRequests[1].CreateArchive, api.DeleteRequests[1].ConfirmWithoutArchive));
        Assert.NotEqual(api.DeleteRequests[0].OperationKey, api.DeleteRequests[1].OperationKey);
    }

    [Fact]
    public async Task CompanyDelete_FailedDeletionCanBeCancelled()
    {
        var api = new FakeApi { InstallationAdmin = true };
        api.OperationRows.Add(new CompanyOperationResponse(Guid.NewGuid(), "Delete", Guid.NewGuid(), "Firma", "CompanyDeactivated", "Failed", 5,
            "sql_1205", "admin", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, null, "v1", true));
        using var model = new MainViewModel(_ => api) { UserName = "admin" };
        await model.Login("fixture-password"); await model.OpenCompanyOperations();
        model.SelectedCompanyOperation = model.CompanyOperations[0];
        Assert.Equal("Usunięcie: Firma", model.SelectedCompanyOperation.Name);
        Assert.True(model.CanCancelCompanyOperation);
        await model.CancelCompanyOperation();
        Assert.Equal(api.OperationRows[0].Id, Assert.Single(api.Cancellations));
        Assert.Contains("Anulowano usuwanie", model.Message);
    }

    private sealed partial class FakeApi
    {
        public List<DeleteCompanyRequest> DeleteRequests { get; } = [];
        public List<Guid> Cancellations { get; } = [];
        public Task<InstallationCompanyResponse[]> InstallationCompanies() =>
            Task.FromResult<InstallationCompanyResponse[]>([new(A, "Firma do usunięcia", "Vatowiec_Firma_do_usuniecia", true), new(B, "Usuwana", "Vatowiec_Usuwana", false)]);
        public Task<DeletedCompanyResponse[]> DeletedCompanies() => Task.FromResult<DeletedCompanyResponse[]>([]);
        public Task<CompanyOperationResponse> DeleteCompany(DeleteCompanyRequest value)
        {
            DeleteRequests.Add(value);
            return Task.FromResult(new CompanyOperationResponse(Guid.NewGuid(), "Delete", value.CompanyId, "Firma do usunięcia", "Requested", "Pending", 0, null,
                "admin", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, null, "v1", value.CreateArchive));
        }
        public Task<CompanyOperationResponse> CancelCompanyOperation(Guid operationId, CompanyOperationRetryRequest value)
        {
            Cancellations.Add(operationId);
            var index = OperationRows.FindIndex(x => x.Id == operationId);
            OperationRows[index] = OperationRows[index] with { Status = "Cancelled", RowVersion = "v2" };
            return Task.FromResult(OperationRows[index]);
        }
    }
}
