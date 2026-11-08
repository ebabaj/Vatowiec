using PhoneNumbers;

namespace Vatowiec.SharedContracts;

public enum PhoneNumberStatus { Empty, Valid, Invalid, Ambiguous }

public sealed record PhoneNumberCheck(PhoneNumberStatus Status, string? E164, string? Display, string? Message)
{
    public bool CanSave => Status is PhoneNumberStatus.Empty or PhoneNumberStatus.Valid;
}

// Shared by the API and desktop, like DetailValidation. No HTTP, SQL or UI dependencies.
public static class PhoneNumberService
{
    private static readonly PhoneNumberUtil Numbers = PhoneNumberUtil.GetInstance();
    public static PhoneNumberCheck Validate(string? input)
    {
        if (string.IsNullOrWhiteSpace(input)) return new(PhoneNumberStatus.Empty, null, null, null);
        if (input.Length > 100) return Invalid("Telefon: wpisz najwyżej 100 znaków.");
        // Never concatenate two numbers, discard extensions, or translate vanity letters.
        if (input.IndexOfAny([';', '/', ',', '\r', '\n', '#']) >= 0)
            return Ambiguous("Telefon: nie można jednoznacznie odczytać numeru. Wpisz jeden numer, bez numeru wewnętrznego.");
        if (input.Any(c => !char.IsAsciiDigit(c) && !char.IsWhiteSpace(c) && c is not ('+' or '-' or '(' or ')' or '.')))
            return Invalid("Telefon: niedozwolone znaki. Użyj cyfr, opcjonalnego + na początku oraz spacji, nawiasów, kropek lub myślników.");
        var text = input.Trim();
        if (text.Count(c => c == '+') > 1 || text.IndexOf('+') > 0)
            return Invalid("Telefon: znak + może wystąpić tylko raz, na początku numeru.");
        try
        {
            var number = Numbers.Parse(text, "PL");
            var possible = Numbers.IsPossibleNumberWithReason(number);
            if (possible is PhoneNumberUtil.ValidationResult.INVALID_COUNTRY_CODE or PhoneNumberUtil.ValidationResult.IS_POSSIBLE_LOCAL_ONLY)
                return Ambiguous("Telefon: nie można ustalić pełnego numeru. Podaj numer z kodem kraju, np. +48 lub +49.");
            if (possible != PhoneNumberUtil.ValidationResult.IS_POSSIBLE)
                return Invalid("Telefon: nieprawidłowa długość numeru dla wskazanego kraju. Dla numeru zagranicznego podaj + i kod kraju.");
            if (number.HasExtension)
                return Ambiguous("Telefon: wpisz jeden numer, bez numeru wewnętrznego.");
            if (!Numbers.IsValidNumber(number))
                return Invalid("Telefon: nieprawidłowa struktura numeru dla wskazanego kraju. Sprawdź kod kraju i cyfry numeru.");
            var e164 = Numbers.Format(number, PhoneNumberFormat.E164);
            var display = Numbers.GetRegionCodeForNumber(number) == "PL" ? Numbers.GetNationalSignificantNumber(number) : e164;
            return new(PhoneNumberStatus.Valid, e164, display, null);
        }
        catch (NumberParseException ex)
        {
            return ex.ErrorType == ErrorType.INVALID_COUNTRY_CODE
                ? Ambiguous("Telefon: nieznany kod kraju. Podaj pełny numer z prawidłowym prefiksem międzynarodowym.")
                : Invalid("Telefon: nieprawidłowa długość lub struktura numeru. Podaj pełny numer, np. 501234567 lub +48501234567.");
        }
    }

    public static string? NormalizeForStorage(string? input)
    {
        var check = Validate(input);
        if (!check.CanSave) throw new FormatException(check.Message);
        return check.E164;
    }

    // Historical invalid data remains visible for correction; reads never rewrite it.
    public static string? FormatForDisplay(string? stored)
    {
        var check = Validate(stored);
        return check.Status == PhoneNumberStatus.Valid ? check.Display : stored;
    }

    public static bool IsUnchanged(string? input, string? stored) =>
        string.Equals(input, stored, StringComparison.Ordinal) ||
        string.Equals(input, FormatForDisplay(stored), StringComparison.Ordinal);

    public static string? NormalizeForUpdate(string? input, string? stored) =>
        IsUnchanged(input, stored) ? stored : NormalizeForStorage(input);

    private static PhoneNumberCheck Invalid(string message) => new(PhoneNumberStatus.Invalid, null, null, message);
    private static PhoneNumberCheck Ambiguous(string message) => new(PhoneNumberStatus.Ambiguous, null, null, message);
}
