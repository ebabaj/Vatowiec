namespace Vatowiec.SharedContracts;

public sealed record GusConfigurationResponse(bool IsConfigured, string? RowVersion, string Environment);
public sealed class SaveGusKeyRequest
{
    public string ApiKey { get; set; } = "";
    public string? RowVersion { get; set; }
    public override string ToString() => "SaveGusKeyRequest [REDACTED]";
}
public sealed record DeleteGusKeyRequest(string? RowVersion, bool Confirm);
