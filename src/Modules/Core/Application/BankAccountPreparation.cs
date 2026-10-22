namespace Vatowiec.Core.Application;

public static class BankAccountPreparation
{
    public static string Normalize(string value) => string.Concat(value.Where(c => !char.IsWhiteSpace(c))).ToUpperInvariant();
    // Wyłącznie kontrola ogólnego zapisu, bez sum kontrolnych, reguł krajowych i rejestrów.
    public static bool NeedsWarning(string value) => value.Length is < 8 or > 34 || value.Any(c => !char.IsAsciiLetterOrDigit(c));
    public const string Rule = "bank-text-v1";
    public const string Warning = "Numer ma nietypową długość lub znaki. Nie sprawdzono sumy kontrolnej, kraju ani rejestrów urzędowych. Możesz świadomie zapisać go pomimo ostrzeżenia, jeżeli masz odpowiednie uprawnienie.";
}
