namespace Vatowiec.SharedContracts;

// Private inherited-pipe protocol, never an HTTP endpoint. Password exists only in transit.
public sealed class PersonalSetupCommand
{
    public string Operation { get; set; } = "status";
    public string? UserName { get; set; }
    public string? Password { get; set; }
    public string? CompanyName { get; set; }
    public string? Nip { get; set; }
}
public sealed record PersonalSetupStatus(bool Success, string Message, string Stage,
    bool Complete = false, bool Final = true, string? ApiAddress = null,
    string? CompanyName = null, string? Nip = null, PersonalApiConnection? Connection = null);

public sealed record PersonalApiConnection(Guid InstallationId, string PipeName, int ProcessId)
{
    public string Identity => PersonalApiIdentity.For(InstallationId);
    public string HttpOrigin => "http://personal-" + InstallationId.ToString("N") + ".vatowiec.invalid/";
}

public static class PersonalApiIdentity
{
    public static string For(Guid id) => id != Guid.Empty ? "vatowiec-personal://" + id.ToString("N") + "/" : throw new ArgumentException("Brak identyfikatora instalacji Personal.");
    public static bool TryParse(string value, out Guid id)
    {
        id = default;
        return Uri.TryCreate(value, UriKind.Absolute, out var uri) && uri.Scheme == "vatowiec-personal" &&
            uri.UserInfo.Length == 0 && uri.Port == -1 && uri.AbsolutePath == "/" && uri.Query.Length == 0 && uri.Fragment.Length == 0 &&
            Guid.TryParseExact(uri.Host, "N", out id) && id != Guid.Empty;
    }
}
