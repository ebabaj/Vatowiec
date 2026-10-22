namespace Vatowiec.SharedContracts;

public sealed record DataPortConfigurationResponse(Guid CompanyId, bool IsConfigured, string? RowVersion);

// Intentionally not a record: generated ToString must never reveal the secret.
public sealed class SaveDataPortKeyRequest
{
    public string ApiKey { get; set; } = "";
    public string? RowVersion { get; set; }
    public override string ToString() => "SaveDataPortKeyRequest [REDACTED]";
}
public sealed record DeleteDataPortKeyRequest(string? RowVersion, bool Confirm);
