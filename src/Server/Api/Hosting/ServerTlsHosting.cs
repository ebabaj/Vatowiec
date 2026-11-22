namespace Vatowiec.Api.Hosting;

// ADR-047: endpointy Kestrela z ServerTls:Endpoints dostają certyfikat zarządzany przez VATowiec.
// Taki endpoint nie może mieć własnej sekcji Certificate — jednoznaczne źródło certyfikatu.
internal static class ServerTlsHosting
{
    public static void ConfigureManagedEndpoints(WebApplicationBuilder builder)
    {
        var endpoints = ServerCertificateProvider.EndpointNames(builder.Configuration);
        if (endpoints.Length == 0) return;
        foreach (var name in endpoints)
        {
            var section = builder.Configuration.GetSection($"Kestrel:Endpoints:{name}");
            if (!section.Exists() || !(section["Url"] ?? "").StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException($"ServerTls:Endpoints wskazuje „{name}”, ale Kestrel:Endpoints:{name}:Url nie jest adresem HTTPS.");
            if (section.GetSection("Certificate").Exists())
                throw new InvalidOperationException($"Endpoint „{name}” ma własną sekcję Certificate. Usuń ją albo usuń endpoint z ServerTls:Endpoints.");
        }
        builder.WebHost.ConfigureKestrel((context, options) =>
        {
            var loader = options.Configure(context.Configuration.GetSection("Kestrel"), reloadOnChange: true);
            foreach (var name in endpoints)
                loader.Endpoint(name, endpoint => ServerCertificateProvider.Attach(endpoint.HttpsOptions, options.ApplicationServices));
        });
    }
}
