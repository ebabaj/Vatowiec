namespace Vatowiec.Core.Application;

public static class StandardIdentifiers
{
    public const string Version = "pl-standard-2026-10-23-v1";
    public static string? Normalize(string? text)
    {
        var value = text == null ? null : string.Concat(text.Where(c => !char.IsWhiteSpace(c) && c is not
            ('.' or '-' or '\u2010' or '\u2011' or '\u2012' or '\u2013' or '\u2014' or '\u2015' or '\u2212' or '\uFE63' or '\uFF0D')));
        return string.IsNullOrWhiteSpace(text) ? null : string.IsNullOrEmpty(value) ? text.Trim() : value;
    }
    public static IdentifierAssessment Assess(string field, string? input)
    {
        var text = Normalize(input);
        if (text == null) return new(field, null, "Empty", "Nie podano — pole opcjonalne.", Version, false, false);
        var length = field switch { "Regon" => text.Length is 9 or 14, "Krs" => text.Length == 10, "Bdo" => text.Length == 9, "Pesel" => text.Length == 11, _ => false };
        if (!length || text.Any(c => c is < '0' or > '9'))
            return new(field, text, "Invalid", field switch
            { "Regon" => "REGON wymaga 9 lub 14 cyfr.", "Krs" => "KRS wymaga 10 cyfr.", "Bdo" => "BDO wymaga 9 cyfr.", _ => "PESEL wymaga 11 cyfr." }, Version, true, false);
        if (field is "Krs" or "Bdo") return new(field, text, "FormatOnly", "Format zgodny. Nie sprawdzono wpisu w rejestrze.", Version, true, false);
        var valid = field == "Regon" ? RegonValid(text) : PeselValid(text);
        return new(field, text, valid ? "Valid" : "Invalid", valid
            ? "Format i suma kontrolna zgodne. Nie potwierdza nadania numeru ani tożsamości."
            : $"Nieprawidłowa suma kontrolna {(field == "Regon" ? "REGON" : "PESEL")}.", Version, true, true);
    }
    private static bool Mod11(string text, int[] weights)
    {
        var result = weights.Select((w, i) => w * (text[i] - '0')).Sum() % 11;
        return (result == 10 ? 0 : result) == text[weights.Length] - '0';
    }
    private static bool RegonValid(string text) => Mod11(text, [8, 9, 2, 3, 4, 5, 6, 7]) &&
        (text.Length == 9 || Mod11(text, [2, 4, 8, 5, 0, 9, 7, 3, 6, 1, 2, 4, 8]));
    private static bool PeselValid(string text)
    {
        int[] weights = [1, 3, 7, 9, 1, 3, 7, 9, 1, 3];
        return (10 - weights.Select((w, i) => w * (text[i] - '0')).Sum() % 10) % 10 == text[10] - '0';
    }
}
