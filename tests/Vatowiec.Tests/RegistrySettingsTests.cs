using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Vatowiec.Core.Domain;
using Vatowiec.Core.Infrastructure.Persistence;
using Vatowiec.Core.Infrastructure.Registries;

namespace Vatowiec.Tests;

public sealed class RegistrySettingsTests
{
    [Theory]
    [InlineData("GUS", "Test", false)]
    [InlineData("GUS", "Production", false)]
    [InlineData("MF", "Test", false)]
    [InlineData("MF", "Production", false)]
    [InlineData("MF", "Test", true)]
    [InlineData("MF", "Production", true)]
    [InlineData("VIES", "Test", false)]
    [InlineData("VIES", "Production", false)]
    public void OnlyOfficialHttpsEndpointAndOptionalTrailingSlashAreAllowed(string source, string mode, bool wl2)
    {
        var url = RegistrySettings.OfficialUrl(source, mode, wl2);
        Assert.True(RegistrySettings.IsAllowedUrl(source, mode, url, wl2));
        Assert.True(RegistrySettings.IsAllowedUrl(source, mode, url.TrimEnd('/'), wl2));
        Assert.True(RegistrySettings.IsAllowedUrl(source, mode, url.TrimEnd('/') + "/", wl2));
        foreach (var invalid in new[] { url.Replace("https:", "http:"), url + "?key=bad", url + "#fragment", url + "../",
                     url.Replace("https://", "https://user:secret@"), url.Replace(".gov.pl", ".gov.pl.evil.invalid"),
                     url.Replace(".eu/", ".eu.evil.invalid/"), "https://localhost/", "https://127.0.0.1/", url + "%2f", "", "https://wl-api.mf.gov.pl/" }.Distinct())
        {
            if (invalid == url) continue;
            Assert.False(RegistrySettings.IsAllowedUrl(source, mode, invalid, wl2), invalid);
        }
        Assert.False(RegistrySettings.IsAllowedUrl(source, mode, RegistrySettings.OfficialUrl(source, mode == "Test" ? "Production" : "Test", wl2), wl2));
    }
    [Fact]
    public void DatabaseSnapshotIgnoresLegacySwitchAndDoesNotChangeWhenRowsChange()
    {
        var row = new RegistryConfiguration("GUS", RegistrySettings.GusTest, RegistrySettings.GusProduction);
        var old = RegistrySettings.FromRows([row]);
        row.Update("Production", row.TestUrl, row.ProductionUrl, null, null);
        var current = RegistrySettings.FromRows([row]);
        Assert.Equal("Test", old.Mode("GUS")); Assert.Equal(RegistrySettings.GusTest, old.Endpoint("GUS").AbsoluteUri);
        Assert.Equal("Production", current.Mode("GUS")); Assert.Equal(RegistrySettings.GusProduction, current.Endpoint("GUS").AbsoluteUri);
        Assert.Throws<InvalidOperationException>(() => current.Mode("MF")); // Never falls back to another source/default.
    }
    [Fact]
    public async Task InFlightGusKeepsTestHostAndHistoryEnvironmentEvenWhenAdministratorChangesMode()
    {
        var row = new RegistryConfiguration("GUS", RegistrySettings.GusTest, RegistrySettings.GusProduction);
        var snapshot = RegistrySettings.FromRows([row]); var calls = 0;
        var reply = await new RegistryAdapters(new ProductionRegistryTests.Factory(request =>
        {
            calls++; Assert.Equal(RegistrySettings.GusTest, request.RequestUri!.AbsoluteUri);
            if (calls == 1)
            {
                row.Update("Production", row.TestUrl, row.ProductionUrl, null, null);
                return ProductionRegistryTests.Reply("<Envelope><ZalogujResult>fixture-session</ZalogujResult></Envelope>");
            }
            return calls == 2 ? ProductionRegistryTests.GusFound() : ProductionRegistryTests.Reply("<Envelope><WylogujResult>true</WylogujResult></Envelope>");
        }), snapshot, new(), new ProductionRegistryTests.KeyProvider(null) { Lost = true })
            .Execute("Gus", "3245174504", null, null, new(2026, 10, 23), default);
        Assert.Equal(3, calls); Assert.Equal("Test", reply.Environment); Assert.Equal("Completed", reply.ServiceStatus);
        Assert.Equal("Production", RegistrySettings.FromRows([row]).Mode("GUS"));
    }
    [Fact]
    public void MigrationCreatesOnlyPublicSettingsAndAudit_WithoutChangingSecretsOrRemovingHistory()
    {
        using var system = new SystemDbContextFactory().CreateDbContext([]);
        using var company = new CompanyDbContextFactory().CreateDbContext([]);
        Assert.NotNull(system.Model.FindEntityType(typeof(RegistryConfiguration)));
        Assert.Null(company.Model.FindEntityType(typeof(RegistryConfiguration)));
        Assert.Empty(system.Model.FindEntityType(typeof(RegistryConfigurationChange))!.GetForeignKeys());
        var names = system.Database.GetMigrations().ToArray(); var migrator = system.GetService<IMigrator>();
        var index = Array.FindIndex(names, x => x.EndsWith("_InstallationRegistrySettings", StringComparison.Ordinal));
        var sql = migrator.GenerateScript(names[index - 1], names[index]);
        Assert.Contains("CREATE TABLE [security].[RegistryConfigurations]", sql); Assert.Contains("CREATE TABLE [audit].[RegistryConfigurationChanges]", sql);
        Assert.Contains("CREATE TRIGGER", sql); Assert.DoesNotContain("DELETE FROM", sql); Assert.DoesNotContain("DROP TABLE", sql);
        Assert.DoesNotContain("[GusConfiguration]", sql); Assert.DoesNotContain("DataPort", sql);
        Assert.Throws<NotSupportedException>(() => migrator.GenerateScript(names[index], names[index - 1]));
    }
}
