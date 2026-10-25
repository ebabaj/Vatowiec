using System.Net;
using System.Net.Http.Json;
using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Vatowiec.Core.Domain;
using Vatowiec.Core.Infrastructure.Persistence;
using Vatowiec.SharedContracts;

namespace Vatowiec.Tests;

public sealed partial class UserAdministrationSqlTests
{
    [PreservedVatSqlFact]
    public async Task NetworkTableLayouts_LegacyHierarchyUsersCompaniesConcurrencySecurityAndAudit_RolledBack()
    {
        await using var tx = await PreservedSystemTransaction.Open(); var auditFailure = new FailTableLayoutAudit(); using var app = App(tx, auditFailure);
        using var scope = app.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<SystemDbContext>();
        var password = "Fixture!1a-" + Guid.NewGuid().ToString("N");
        var admin = await Account(scope.ServiceProvider, true, password);
        var reader = await Account(scope.ServiceProvider, false, password);
        var other = await Account(scope.ServiceProvider, false, password);
        foreach (var user in new[] { admin, reader, other }) foreach (var company in new[] { A, B })
            db.CompanyAccesses.Add(new(user.Id, company, CompanyPermission.ViewProfile | CompanyPermission.ViewContractors | CompanyPermission.ViewExchangeRates |
                (user.Id == admin.Id ? CompanyPermission.ManageContractorPesel : CompanyPermission.None)));
        db.TableViews.RemoveRange(await db.TableViews.Where(v => v.OwnerId == Guid.Empty).ToArrayAsync());
        var installation = await db.InstallationIdentities.Select(x => x.Id).SingleAsync();
        db.TableViews.Add(new() { InstallationId = installation, OwnerId = admin.Id, TableId = "Contractors", ColumnsJson = "[\"Name\",\"Code\"]", PageSize = 25 });
        await db.SaveChangesAsync();
        using var a = Client(app); using var r = Client(app); using var r2 = Client(app); using var o = Client(app);
        await Login(a, admin.UserName!, password); await Login(r, reader.UserName!, password); await Login(r2, reader.UserName!, password); await Login(o, other.UserName!, password);
        static string RouteFor(Guid company, string table = "Contractors") => $"/api/v1/companies/{company}/table-views/{table}";
        async Task<TableViewResponse> ReadView(HttpClient client, Guid? company = null, string table = "Contractors") =>
            (await client.GetFromJsonAsync<TableViewResponse>(RouteFor(company ?? A, table)))!;
        async Task<TableViewResponse> Save(HttpClient client, TableViewRequest value, bool global = false, string table = "Contractors")
        { await Csrf(client); return await Ok<TableViewResponse>(await client.PutAsJsonAsync(RouteFor(A, table) + (global ? "/installation" : "/mine"), value)); }
        var legacy = await ReadView(a); Assert.Equal(25, legacy.PageSize); Assert.Equal(new[] { "Code", "Name" }, legacy.VisibleColumns);
        Assert.Equal("Code", legacy.ColumnOrder![0]); Assert.Equal(266, legacy.ColumnWidths!["Name"]);
        var own = await Save(a, new(RowVersion: legacy.UserVersion, ColumnOrder: ["Name", "Code"], ColumnWidths: new() { ["Name"] = 333.5 }));
        Assert.Equal(25, own.PageSize); Assert.Equal(333.5, own.ColumnWidths!["Name"]);
        own = await Save(a, new(["Code"], RowVersion: own.UserVersion));
        Assert.Equal(333.5, own.ColumnWidths!["Name"]); Assert.Equal("Name", own.ColumnOrder![0]);
        var global = await Save(a, new(["Code", "Name", "Pesel"], ColumnOrder: ["Name", "Code", "Pesel"], ColumnWidths: new() { ["Name"] = 444, ["Pesel"] = 300 }), true);
        var inherited = await ReadView(r); Assert.Equal("Installation", inherited.Source); Assert.Equal(444, inherited.ColumnWidths!["Name"]);
        Assert.DoesNotContain("Pesel", inherited.ColumnOrder!); Assert.DoesNotContain("Pesel", inherited.ColumnWidths.Keys); Assert.DoesNotContain(inherited.Columns, c => c.Id == "Pesel");
        var pageOnly = await Save(r, new(PageSize: 100)); Assert.Equal("Installation", pageOnly.Source);
        var mine = await Save(r, new(RowVersion: pageOnly.UserVersion, ColumnWidths: new() { ["Code"] = 220 }));
        Assert.Equal("User", mine.Source); Assert.Equal(100, mine.PageSize);
        global = await Save(a, new(["Name"], RowVersion: global.InstallationVersion, ColumnWidths: new() { ["Name"] = 555 }), true);
        Assert.Equal(444, (await ReadView(r)).ColumnWidths!["Name"]); Assert.Equal(555, (await ReadView(o)).ColumnWidths!["Name"]);
        Assert.Equal(mine.UserVersion, (await ReadView(r, B)).UserVersion);
        var station = await ReadView(r2);
        mine = await Save(r, new(RowVersion: mine.UserVersion, ColumnWidths: new() { ["Name"] = 270 }));
        await Csrf(r2);
        Assert.Equal(HttpStatusCode.Conflict, (await r2.PutAsJsonAsync(RouteFor(A) + "/mine", new TableViewRequest(RowVersion: station.UserVersion, ColumnWidths: new() { ["Name"] = 280 }))).StatusCode);
        foreach (var invalid in new[]
        {
            new TableViewRequest(RowVersion: mine.UserVersion, ColumnWidths: new() { ["Pesel"] = 200 }),
            new TableViewRequest(RowVersion: mine.UserVersion, ColumnOrder: ["Code", "Pesel"]),
            new TableViewRequest(RowVersion: mine.UserVersion, ColumnWidths: new() { ["Name"] = 63 }),
            new TableViewRequest(RowVersion: mine.UserVersion, ColumnWidths: new() { ["Name"] = 1201 }),
            new TableViewRequest(RowVersion: mine.UserVersion, ColumnOrder: ["Code", "Code"]),
            new TableViewRequest(RowVersion: mine.UserVersion, ColumnOrder: ["Removed"])
        }) Assert.Equal(HttpStatusCode.BadRequest, (await r.PutAsJsonAsync(RouteFor(A) + "/mine", invalid)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await r.PutAsJsonAsync(RouteFor(A) + "/installation", new TableViewRequest(["Code"], RowVersion: global.InstallationVersion))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await r.GetAsync(RouteFor(Guid.NewGuid()))).StatusCode);
        r.DefaultRequestHeaders.Remove("X-CSRF-TOKEN");
        Assert.Equal(HttpStatusCode.BadRequest, (await r.PutAsJsonAsync(RouteFor(A) + "/mine", new TableViewRequest(RowVersion: mine.UserVersion, ColumnWidths: new() { ["Name"] = 300 }))).StatusCode);
        var currency = await Save(r, new(ColumnWidths: new() { ["Code"] = 205 }), table: "Currencies");
        Assert.Equal(205, currency.ColumnWidths!["Code"]); Assert.Equal(270, (await ReadView(r)).ColumnWidths!["Name"]);
        auditFailure.Enabled = true;
        try
        {
            await Csrf(a);
            Assert.Equal(HttpStatusCode.ServiceUnavailable, (await a.PutAsJsonAsync(RouteFor(A) + "/installation",
                new TableViewRequest(RowVersion: global.InstallationVersion, ColumnWidths: new() { ["Name"] = 600 }))).StatusCode);
            Assert.Equal(555, (await ReadView(o)).ColumnWidths!["Name"]);
        }
        finally { auditFailure.Enabled = false; }
        using (var restartedHost = App(tx)) using (var restarted = Client(restartedHost))
        {
            await Login(restarted, reader.UserName!, password);
            Assert.Equal(270, (await ReadView(restarted)).ColumnWidths!["Name"]);
        }
        mine = await Save(r, new(RowVersion: mine.UserVersion, Reset: true));
        Assert.Equal("Installation", mine.Source); Assert.Equal(50, mine.PageSize); Assert.Equal(555, mine.ColumnWidths!["Name"]);
        // Resetting the global default leaves the administrator's personal layout intact.
        await Save(a, new(RowVersion: global.InstallationVersion, Reset: true), true);
        Assert.Equal("Factory", (await ReadView(r)).Source); Assert.Equal(333.5, (await ReadView(a)).ColumnWidths!["Name"]);
        Assert.Contains(await db.TableViewChanges.AsNoTracking().Where(c => c.ActorId == admin.Id).ToArrayAsync(), c => c.AfterJson != null && c.AfterJson.Contains("ColumnWidths", StringComparison.Ordinal));
    }
    private sealed class FailTableLayoutAudit : DbCommandInterceptor
    {
        public bool Enabled { get; set; }
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData data, InterceptionResult<DbDataReader> result, CancellationToken ct = default)
        {
            if (Enabled && command.CommandText.Contains("INSERT INTO [audit].[TableViewChanges]", StringComparison.Ordinal)) throw new DbUpdateException("Fixture audit failure");
            return ValueTask.FromResult(result);
        }
    }
}
