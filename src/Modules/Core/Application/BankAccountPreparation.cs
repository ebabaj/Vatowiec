namespace Vatowiec.Core.Application;

public static class BankAccountPreparation
{
    public static string Normalize(string value)
    {
        var compact = string.Concat(value.Where(c => !char.IsWhiteSpace(c))).ToUpperInvariant();
        static bool Separator(char c) => c is '-' or >= '\u2010' and <= '\u2015' or '\u2212' or '\uFE63' or '\uFF0D';
        var numeric = compact.All(c => char.IsAsciiDigit(c) || Separator(c));
        var iban = compact.Length >= 4 && char.IsAsciiLetter(compact[0]) && char.IsAsciiLetter(compact[1]) &&
            char.IsAsciiDigit(compact[2]) && char.IsAsciiDigit(compact[3]);
        return numeric || iban ? string.Concat(compact.Where(c => !Separator(c))) : compact;
    }
}

public enum BankAccountStatus { ValidIban, InvalidIban, NonIban }

// Kontrola składni i sumy kontrolnej, bez zapytań do banków lub rejestrów.
public sealed record BankAccountCheck(BankAccountStatus Status, string Code, string Message,
    string NormalizedNumber, bool IsPolishNrb, string RuleVersion)
{
    public bool RequiresWarning => Status != BankAccountStatus.ValidIban;
    public string? PolishClearingNumber => Status != BankAccountStatus.ValidIban ? null :
        IsPolishNrb ? NormalizedNumber.Substring(2, 8) :
        NormalizedNumber.StartsWith("PL", StringComparison.Ordinal) ? NormalizedNumber.Substring(4, 8) : null;
}

public interface IBankAccountValidator
{
    BankAccountCheck Validate(string number);
}
