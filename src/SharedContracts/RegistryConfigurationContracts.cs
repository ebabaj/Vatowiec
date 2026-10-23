namespace Vatowiec.SharedContracts;

public sealed record RegistryConfigurationResponse(string Source, string Environment, string TestUrl,
    string ProductionUrl, string? TestWl2Url, string? ProductionWl2Url, string RowVersion);
public sealed record RegistryConfigurationRequest(string Environment, string TestUrl, string ProductionUrl,
    string? TestWl2Url, string? ProductionWl2Url, string RowVersion, bool ConfirmProduction = false);
