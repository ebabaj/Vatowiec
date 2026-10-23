namespace Vatowiec.Core.Application;

public static class BankAccountPreparation
{
    public static string Normalize(string value) => string.Concat(value.Where(c => !char.IsWhiteSpace(c))).ToUpperInvariant();
}

public enum BankAccountStatus { ValidIban, InvalidIban, NonIban }

// Kontrola składni i sumy kontrolnej, bez zapytań do banków lub rejestrów.
public sealed record BankAccountCheck(BankAccountStatus Status, string Code, string Message,
    string NormalizedNumber, bool IsPolishNrb, string RuleVersion)
{
    public bool RequiresWarning => Status != BankAccountStatus.ValidIban;
}

public interface IBankAccountValidator
{
    BankAccountCheck Validate(string number);
}
