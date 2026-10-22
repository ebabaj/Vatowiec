using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Vatowiec.Core.Infrastructure.Registries;

namespace Vatowiec.DatabaseTools;

internal static class RegistryProbe
{
    public static async Task Run()
    {
        // Jawna próba testowych usług, poza zestawem automatycznych testów SQL/API.
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Registries:MF:Environment"] = "Test",
            ["Registries:GUS:Environment"] = "Test",
            ["Registries:VIES:Environment"] = "Test",
            ["Registries:AllowProduction"] = "false"
        }).Build();
        var services = new ServiceCollection(); services.AddSingleton<IConfiguration>(configuration); services.AddRegistryAdapters();
        using var provider = services.BuildServiceProvider(); using var scope = provider.CreateScope();
        var adapter = scope.ServiceProvider.GetRequiredService<IRegistryAdapters>();
        (string Op, string? Nip, string? Vat, string? Account)[] probes = [
            ("MfTaxpayer", "3245174504", null, null), ("MfStatus", "3245174504", null, null),
            ("MfAccount", "3245174504", null, "70506405335016096312945164"),
            ("Gus", "5261040828", null, null), ("Vies", null, "PL100", null),
            ("Vies", null, "EL200", null) ];
        foreach (var probe in probes)
        {
            var result = await adapter.Execute(probe.Op, probe.Nip, probe.Vat, probe.Account, DateOnly.FromDateTime(DateTime.UtcNow), CancellationToken.None);
            Console.WriteLine($"{probe.Op}: środowisko={result.Environment}; wykonano={result.Executed}; usługa={result.ServiceStatus}; wynik={result.Outcome}; błąd={result.ErrorCode ?? "brak"}; referencja={result.Reference ?? "brak"}.");
            Console.WriteLine(result.Message);
            // Nie wypisujemy kluczy/SID ani danych podmiotu. Wyniki nie są produkcyjną historią.
            await Task.Delay(2200);
        }
    }
}
