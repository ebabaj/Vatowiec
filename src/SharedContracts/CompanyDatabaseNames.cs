using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Vatowiec.SharedContracts;

// ADR-045: nazwa bazy firmy = stały prefiks + oczyszczona nazwa firmy. Wspólna reguła dla
// podglądu w Avalonia i dla serwera; o ostatecznej nazwie i jej zajętości decyduje serwer.
public static partial class CompanyDatabaseNames
{
    public const string Prefix = "Vatowiec_";
    public const int MaxNameLength = 100;

    // Bazy systemowe, historyczne, demonstracyjne, testowe i prototypy Personal są zawsze „zajęte”.
    private static readonly string[] Reserved =
    [
        "Vatowiec_System", "Vatowiec_System_Test", "Vatowiec_Dev",
        "Vatowiec_Company_A_Dev", "Vatowiec_Company_B_Dev", "Vatowiec_Company_A_Test", "Vatowiec_Company_B_Test"
    ];
    private const string ReservedPersonalPrefix = "Vatowiec_Personal_";

    public static string? FromCompanyName(string? companyName)
    {
        if (string.IsNullOrWhiteSpace(companyName)) return null;
        var text = new StringBuilder();
        foreach (var c in companyName.Trim().Normalize(NormalizationForm.FormD))
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark) continue;
            foreach (var latin in Latin(c)) text.Append(char.IsAsciiLetterOrDigit(latin) ? latin : '_');
        }
        var name = Underscores().Replace(text.ToString(), "_").Trim('_');
        if (name.Length > MaxNameLength) name = name[..MaxNameLength].TrimEnd('_');
        return name.Length == 0 ? null : Prefix + name;
    }

    // Litery, których rozkład Unicode nie sprowadza do łacinki (m.in. polskie ł).
    private static string Latin(char c) => c switch
    {
        'ł' => "l",
        'Ł' => "L",
        'đ' => "d",
        'Đ' => "D",
        'ø' => "o",
        'Ø' => "O",
        'ß' => "ss",
        'æ' => "ae",
        'Æ' => "AE",
        'œ' => "oe",
        'Œ' => "OE",
        _ => c.ToString()
    };

    public static bool IsReserved(string? database) => database is not null &&
        (Reserved.Contains(database, StringComparer.OrdinalIgnoreCase) ||
         database.StartsWith(ReservedPersonalPrefix, StringComparison.OrdinalIgnoreCase));

    // Nazwa bazy firmy Network: ścisły wzorzec (bezpieczny w DDL) i spoza listy zastrzeżonych.
    // Obejmuje także wcześniejsze nazwy Vatowiec_Company_{guid}.
    public static bool IsCompanyDatabase(string? database) => database is not null &&
        database.Length <= Prefix.Length + MaxNameLength && Pattern().IsMatch(database) && !IsReserved(database);

    [GeneratedRegex("_{2,}", RegexOptions.CultureInvariant)]
    private static partial Regex Underscores();

    [GeneratedRegex("^Vatowiec_[A-Za-z0-9]+(?:_[A-Za-z0-9]+)*$", RegexOptions.CultureInvariant)]
    private static partial Regex Pattern();
}
