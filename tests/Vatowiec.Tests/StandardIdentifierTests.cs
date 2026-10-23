using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Vatowiec.Core.Application;
using Vatowiec.Core.Domain;
using Vatowiec.Core.Infrastructure.Persistence;
using Vatowiec.Desktop.Services;
using Vatowiec.SharedContracts;

namespace Vatowiec.Tests;

public sealed class StandardIdentifierTests
{
    [Theory]
    [InlineData("Regon", "000000023", "Valid")]
    [InlineData("Regon", "00000002300041", "Valid")]
    [InlineData("Regon", "000000024", "Invalid")]
    [InlineData("Regon", "00000002300042", "Invalid")]
    [InlineData("Regon", "00001002300041", "Invalid")]
    [InlineData("Regon", "123456740", "Valid")]
    [InlineData("Regon", "00000002", "Invalid")]
    [InlineData("Krs", "0000000023", "FormatOnly")]
    [InlineData("Krs", "000000023", "Invalid")]
    [InlineData("Bdo", "000000023", "FormatOnly")]
    [InlineData("Bdo", "0000000023", "Invalid")]
    [InlineData("Pesel", "00000000017", "Valid")]
    [InlineData("Pesel", "00000000000", "Valid")]
    [InlineData("Pesel", "00000000018", "Invalid")]
    [InlineData("Pesel", "0000000001A", "Invalid")]
    [InlineData("Pesel", "０００００００００１７", "Invalid")]
    [InlineData("Pesel", null, "Empty")]
    public void FormatChecksumAndLeadingZeros(string field, string? value, string expected)
    {
        var result = StandardIdentifiers.Assess(field, value);
        Assert.Equal(expected, result.Status); Assert.Equal(value, result.Normalized);
        Assert.Equal(StandardIdentifiers.Version, result.RuleVersion);
        if (field is "Krs" or "Bdo") Assert.False(result.ChecksumChecked);
    }
    [Fact]
    public void NormalizationIsExplicitAndNeverRepairsInvalidDigits()
    {
        Assert.Equal("000000023", StandardIdentifiers.Assess("Regon", " 000.000–023\t").Normalized);
        var invalid = StandardIdentifiers.Assess("Pesel", "000 000-000.18");
        Assert.Equal("Invalid", invalid.Status); Assert.Equal("00000000018", invalid.Normalized);
        Assert.Contains("suma kontrolna PESEL", invalid.Message);
        Assert.Equal("000/000023", StandardIdentifiers.Normalize("000/000023"));
        Assert.Equal("Invalid", StandardIdentifiers.Assess("Regon", "---").Status);
    }
    [Fact]
    public void DiagnosticsNeverPrintPesel_AndNullResponseOmitsProperty()
    {
        const string number = "00000000017";
        var request = new ContractorWriteRequest(0, "FAKE", "Fikcyjny", "PL", true, Pesel: number);
        var assessment = StandardIdentifiers.Assess("Pesel", number);
        var check = new IdentifierCheck("Pesel", number, assessment.Status, assessment.Message, assessment.RuleVersion, true, true);
        Assert.DoesNotContain(number, request.ToString()); Assert.DoesNotContain(number, assessment.ToString());
        Assert.DoesNotContain(number, check.ToString()); Assert.DoesNotContain(number, new IdentifierPreview([check], [], "hash", false).ToString());
        var response = new ContractorResponse(1, Guid.NewGuid(), "FAKE", "Fikcyjny", "PL", Pesel: number);
        Assert.DoesNotContain(number, response.ToString());
        Assert.DoesNotContain("pesel", JsonSerializer.Serialize(response with { Pesel = null }, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
    }
    [Fact]
    public void ModelAndMigrationAreAdditiveAndPermissionIsOptIn()
    {
        using var company = new CompanyDbContextFactory().CreateDbContext([]);
        using var system = new SystemDbContextFactory().CreateDbContext([]);
        var entity = company.Model.FindEntityType(typeof(Contractor))!;
        foreach (var field in new[] { "Regon", "Krs", "Bdo", "Pesel" })
        { var property = entity.FindProperty(field)!; Assert.True(property.IsNullable); Assert.Equal(typeof(string), property.ClrType); Assert.Equal(100, property.GetMaxLength()); }
        var names = company.Database.GetMigrations().ToArray();
        var sql = company.GetService<IMigrator>().GenerateScript(names[^2], names[^1]);
        foreach (var field in new[] { "Regon", "Krs", "Bdo", "Pesel" }) Assert.Contains($"ADD [{field}] nvarchar(100) NULL", sql);
        Assert.DoesNotContain("DROP", sql); Assert.DoesNotContain("UPDATE", sql); Assert.DoesNotContain("ParentId", sql);
        var old = new CompanyAccess(Guid.NewGuid(), Guid.NewGuid(), (CompanyPermission)511);
        Assert.Equal(0, (int)(old.Permissions & CompanyPermission.ManageContractorPesel));
        old.ChangePermissions(old.Permissions | CompanyPermission.ManageContractorPesel);
        Assert.Equal(1023, (int)old.Permissions);
        Assert.Throws<ArgumentException>(() => old.ChangePermissions((CompanyPermission)1024));
    }
}

public sealed partial class DesktopTests
{
    [Fact]
    public async Task StandardFieldsUseMainEditorAndSensitivePermission()
    {
        using var ordinary = await Open(new FakeApi { Permissions = ["ViewProfile", "ViewContractors", "EditContractors"] });
        Assert.False(ordinary.CanManagePesel);
        using var model = await Open(new FakeApi { Permissions = ["ViewProfile", "ViewContractors", "EditContractors", "ManageContractorPesel"] });
        Assert.True(model.CanManagePesel); model.NewContractor();
        model.Editor!.Regon = "000000023"; model.Editor.Krs = "0000000023"; model.Editor.Bdo = "000000023"; model.Editor.Pesel = "00000000017";
        Assert.True(model.Editor.IsDirty);
        var value = model.Editor.Request(); Assert.Equal("000000023", value.Regon); Assert.Equal("00000000017", value.Pesel);
        model.Editor.Pesel = ""; Assert.Null(model.Editor.Request().Pesel);
        var loaded = new Vatowiec.Desktop.ViewModels.ContractorEditor(new(1, A, "FAKE", "Fikcyjny", "PL", Regon: value.Regon, Krs: value.Krs, Bdo: value.Bdo, Pesel: value.Pesel));
        Assert.Equal(value.Regon, loaded.Regon); Assert.Equal(value.Pesel, loaded.Pesel); Assert.False(loaded.IsDirty);
    }
    [Fact]
    public async Task SearchUsesCsrfAndBodyWithoutIdentifierInUrl()
    {
        const string number = "00000000017";
        using var client = new VatowiecApiClient(new HttpClient(new Handler(async request =>
        {
            Assert.DoesNotContain(number, request.RequestUri!.AbsoluteUri);
            if (request.RequestUri.AbsolutePath.EndsWith("/csrf", StringComparison.Ordinal)) return Json(new CsrfResponse("fresh", "X-CSRF-TOKEN"));
            Assert.Equal(HttpMethod.Post, request.Method); Assert.EndsWith("/contractors/search", request.RequestUri.AbsolutePath);
            Assert.Equal("fresh", request.Headers.GetValues("X-CSRF-TOKEN").Single());
            Assert.Equal(number, (await request.Content!.ReadFromJsonAsync<ContractorSearchRequest>())!.Query);
            return Json(Array.Empty<ContractorResponse>());
        }))
        { BaseAddress = new("https://localhost") });
        Assert.Empty(await client.Contractors(A, number));
    }
}
