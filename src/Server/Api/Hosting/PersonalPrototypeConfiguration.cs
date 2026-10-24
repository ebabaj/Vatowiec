using Microsoft.AspNetCore.DataProtection;
using Vatowiec.Core.Infrastructure.Persistence;

namespace Vatowiec.Api.Hosting;

public static class PersonalPrototypeConfiguration
{
    public static void Configure(WebApplicationBuilder builder)
    {
        if (!OperatingSystem.IsWindows()) throw new InvalidOperationException("PersonalPrototype wymaga Windows.");
        var root = PersonalPrototypeDatabase.Root;
        // This environment does not load appsettings.Development or Network user-secrets.
        builder.Configuration.AddJsonFile(Path.Combine(root, "host.json"), optional: false, reloadOnChange: false);
        if (builder.Configuration["Vatowiec:Edition"] != PersonalPrototypeDatabase.Edition)
            throw new InvalidOperationException("Brak jawnego profilu PersonalPrototype.");
        var systemConnection = builder.Configuration.GetConnectionString("VatowiecSystem");
        if (string.IsNullOrWhiteSpace(systemConnection))
            throw new InvalidOperationException("Brak połączenia systemowego PersonalPrototype; nie ma zastępczej bazy Network.");
        DatabaseConnectionPolicy.ValidateSystem(systemConnection, false, true);
        var endpoints = builder.Configuration.GetSection("Kestrel:Endpoints").GetChildren().ToArray();
        if (endpoints.Length != 1 || !Uri.TryCreate(endpoints[0]["Url"], UriKind.Absolute, out var uri) ||
            uri.Scheme != "https" || uri.Host != "localhost" || uri.Port != 17443 || uri.AbsolutePath != "/")
            throw new InvalidOperationException("PersonalPrototype dopuszcza wyłącznie HTTPS localhost:17443.");
        if (builder.Configuration["AllowedHosts"] != "localhost")
            throw new InvalidOperationException("PersonalPrototype wymaga AllowedHosts=localhost.");
        builder.Services.AddDataProtection().SetApplicationName("VATowiec.PersonalPrototype")
            .PersistKeysToFileSystem(new DirectoryInfo(Path.Combine(root, "Keys", "Sessions")))
            .ProtectKeysWithDpapi(protectToLocalMachine: false);
        builder.Services.PostConfigure<Microsoft.AspNetCore.Authentication.Cookies.CookieAuthenticationOptions>(
            Microsoft.AspNetCore.Identity.IdentityConstants.ApplicationScheme,
            options => options.Cookie.Name = "__Host-Vatowiec.PersonalPrototype.Session");
        builder.Services.PostConfigure<Microsoft.AspNetCore.Antiforgery.AntiforgeryOptions>(
            options => options.Cookie.Name = "__Host-Vatowiec.PersonalPrototype.Csrf");
    }
}
