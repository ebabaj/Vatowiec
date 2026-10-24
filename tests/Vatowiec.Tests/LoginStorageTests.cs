using System.Text.Json;
using Vatowiec.Desktop.Services;

namespace Vatowiec.Tests;

public sealed class WindowsLoginFactAttribute : FactAttribute
{
    public WindowsLoginFactAttribute() { if (!OperatingSystem.IsWindows()) Skip = "Windows Credential Manager requires Windows."; }
}
public sealed class LoginStorageTests
{
    [Theory]
    [InlineData("https://EXAMPLE.invalid", "Operator", "https://example.invalid:443/", "operator", true)]
    [InlineData("https://example.invalid", "operator", "https://other.invalid", "operator", false)]
    [InlineData("https://example.invalid:7243", "operator", "https://example.invalid:7245", "operator", false)]
    [InlineData("https://example.invalid", "operator", "https://example.invalid", "reader", false)]
    public void TargetBindsCanonicalHttpsOriginAndLogin(string first, string user, string second, string secondUser, bool same)
    {
        var a = new LoginTarget(first, user); var b = new LoginTarget(second, secondUser);
        Assert.Equal(same, a.Key == b.Key); Assert.Equal(same, a.CredentialName == b.CredentialName);
    }
    [Theory]
    [InlineData("http://example.invalid")]
    [InlineData("https://example.invalid/api")]
    [InlineData("https://user:password@example.invalid")]
    [InlineData("https://example.invalid?host=other.invalid")]
    public void UnsafeTargetsCannotReadOrWriteCredentials(string address) => Assert.Throws<ArgumentException>(() => new LoginTarget(address, "operator"));

    [WindowsLoginFact]
    public void NativeWindowsStore_RoundTripsOnlyUniqueFixtureAndDeletesIt_WithoutOtherCredentialAccess()
    {
        var store = new WindowsLoginCredentialStore();
        var target = new LoginTarget("https://credential-fixture.invalid", "fixture-" + Guid.NewGuid().ToString("N"));
        var other = new LoginTarget("https://other-credential-fixture.invalid", target.UserName);
        var password = "Fikcyjne-Żółć-😀-" + Guid.NewGuid().ToString("N");
        try
        {
            Assert.False(store.Contains(target)); Assert.Null(store.Read(target)); store.Write(target, password);
            // Avoid Assert.Equal's display of expected/actual credential values on failure.
            Assert.True(store.Contains(target)); Assert.True(password == store.Read(target));
            Assert.False(store.Contains(other)); Assert.Null(store.Read(other));
            var replacement = "Fikcyjne-" + Guid.NewGuid().ToString("N");
            store.Write(target, replacement); Assert.True(replacement == store.Read(target));
            store.Delete(target); Assert.False(store.Contains(target)); Assert.Null(store.Read(target)); store.Delete(target);
        }
        finally { store.Delete(target); }
    }

    [Fact]
    public void PreferenceFile_ContainsOnlyNonsecretFields_RoundTripsPairsAndRejectsMalformedInput()
    {
        var directory = Path.Combine(Path.GetTempPath(), "VatowiecLoginFixture-" + Guid.NewGuid().ToString("N"));
        var file = Path.Combine(directory, "preferences.json");
        var store = new FileLoginPreferencesStore(file); var target = new LoginTarget("https://fixture.invalid", "fixture");
        try
        {
            Assert.Null(store.Load().LastTargetKey);
            var value = new LoginPreferences().WithProfile(new(target.ApiAddress, target.UserName, true, true, Guid.NewGuid()), true);
            store.Save(value); var json = File.ReadAllText(file);
            using var document = JsonDocument.Parse(json);
            var fields = document.RootElement.GetProperty("profiles")[0].EnumerateObject().Select(x => x.Name).Order().ToArray();
            Assert.Equal(new[] { "apiAddress", "automaticLogin", "defaultCompanyId", "rememberCredentials", "userName" }, fields);
            Assert.Equal(value.Find(target), store.Load().Find(target));
            foreach (var bad in new[] { "broken", "{\"version\":999}", "{\"profiles\":[null]}", "{\"profiles\":[{\"apiAddress\":null,\"userName\":\"fixture\"}]}" })
            {
                File.WriteAllText(file, bad);
                var error = Assert.Throws<LocalLoginStorageException>(() => store.Load());
                Assert.DoesNotContain(bad, error.Message);
            }
        }
        finally { if (File.Exists(file)) File.Delete(file); if (Directory.Exists(directory)) Directory.Delete(directory); }
    }
}
