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
    string? CompanyName = null, string? Nip = null);
