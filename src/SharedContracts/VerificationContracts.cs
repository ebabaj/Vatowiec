namespace Vatowiec.SharedContracts;

public sealed record IdentifierCheck(string Field, string? Normalized, string Status, string Message,
    string RuleVersion, bool FormatChecked, bool ChecksumChecked);
public sealed record IdentifierDuplicate(string Field, int Id, string Code, string Name, string RowVersion);
public sealed record IdentifierPreview(IdentifierCheck[] Checks, IdentifierDuplicate[] Duplicates, string WarningHash,
    bool NeedsWarning)
{
    public override string ToString() => string.Join("\n", Checks.Select(x => $"{x.Field}: {x.Normalized ?? "(pusto)"} — {x.Message} [{x.RuleVersion}]")) +
        (Duplicates.Length == 0 ? "" : "\nDuplikaty:\n" + string.Join("\n", Duplicates.Select(x => $"{x.Field}: {x.Code} — {x.Name} (Id {x.Id})")));
}

public sealed record VerificationRequest(Guid OperationId, string Operation, string RowVersion,
    int? BankAccountId = null, string? BankRowVersion = null, DateOnly? StateDate = null);
public sealed record RegistryData(string? Name = null, string? Address = null, string? Regon = null,
    string? VatStatus = null, string? Street = null, string? BuildingNo = null, string? ApartmentNo = null,
    string? PostalCode = null, string? City = null, string? CountryCode = null, DateOnly? ActivityEndDate = null);
public sealed record VerificationResponse(Guid Id, Guid CompanyId, int ContractorId, int? BankAccountId,
    string Source, string Operation, string Environment, string? TaxId, string? VatEuId, string? AccountNumber,
    DateOnly? StateDate, DateTimeOffset StartedAtUtc, DateTimeOffset CompletedAtUtc, string Outcome,
    string ServiceStatus, string? ErrorCode, string Message, string? OfficeReference, RegistryData Data,
    string AdapterVersion, string RuleVersion, bool Executed)
{
    public override string ToString() => $"{StartedAtUtc.ToLocalTime():yyyy-MM-dd HH:mm:ss} · {Source}/{Operation} · {Environment}\n{Message}\n{TaxId ?? VatEuId} {AccountNumber}";
}
public sealed record RegistryEnvironmentResponse(string Mf, string Gus, string Vies)
{
    public string Label => $"MF: {Mf} · GUS: {Gus} · VIES: {Vies}. TEST/MOCK nie są urzędowym potwierdzeniem produkcyjnym.";
}
