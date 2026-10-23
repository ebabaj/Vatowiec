using System.Reflection;
using IbanNet;
using IbanNet.Validation.Results;
using Vatowiec.Core.Application;

namespace Vatowiec.Core.Infrastructure;

public sealed class IbanNetBankAccountValidator : IBankAccountValidator
{
    private readonly IbanValidator validator = new();
    // Tożsamość binariów obejmuje wbudowane reguły krajowe. Zmiana pakietu
    // unieważnia niezapisane potwierdzenia także bez zmiany polityki aplikacji.
    private static readonly string RuleVersion = $"bank-iban-v3/IbanNet-{typeof(IbanValidator).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion}/{typeof(IbanValidator).Module.ModuleVersionId}";

    public BankAccountCheck Validate(string number)
    {
        var normalized = BankAccountPreparation.Normalize(number);
        var nrb = normalized.Length == 26 && normalized.All(char.IsAsciiDigit);
        var prefix = normalized.Length >= 2 && normalized.Take(2).All(char.IsAsciiLetter);
        // Sam prefiks kraju nie wystarcza: DEMO-LOCAL nie jest niemieckim IBAN.
        var ibanShape = prefix && normalized.Length >= 4 && (char.IsAsciiDigit(normalized[2]) || char.IsAsciiDigit(normalized[3]));
        var knownCountry = prefix && validator.SupportedCountries.TryGetValue(normalized[..2], out _);
        if (!nrb && !ibanShape && !(knownCountry && normalized.Length < 4))
            return new(BankAccountStatus.NonIban, "non_iban", "Format spoza IBAN — niezweryfikowany. Możesz świadomie zapisać go pomimo ostrzeżenia.", normalized, false, RuleVersion);

        var checkedNumber = nrb ? "PL" + normalized : normalized;
        var result = validator.Validate(checkedNumber);
        if (result.IsValid)
            return new(BankAccountStatus.ValidIban, "valid_iban", nrb ? "Poprawny IBAN (NRB sprawdzony z prefiksem PL)." : "Poprawny IBAN.", normalized, nrb, RuleVersion);
        var (code, reason) = result.Error switch
        {
            InvalidCheckDigitsResult => ("invalid_checksum", "Nieprawidłowa suma kontrolna IBAN"),
            InvalidLengthResult => ("invalid_length", "Nieprawidłowa długość IBAN dla wskazanego kraju"),
            UnknownCountryCodeResult => ("unknown_country", "Nieobsługiwany kod kraju IBAN"),
            IllegalCharactersResult when checkedNumber.Length < 4 => ("invalid_length", "Nieprawidłowa długość IBAN dla wskazanego kraju"),
            // IbanNet zalicza zabronione cyfry kontrolne 00/01/99 do błędów znaków.
            // Gdy znaki są ASCII alfanumeryczne, pokaż właściwą przyczynę.
            IllegalCharactersResult when checkedNumber.All(char.IsAsciiLetterOrDigit) => ("invalid_checksum", "Nieprawidłowa suma kontrolna IBAN"),
            IllegalCharactersResult or IllegalCountryCodeCharactersResult => ("illegal_characters", "Niedozwolone znaki w IBAN"),
            InvalidStructureResult => ("invalid_structure", "Nieprawidłowa struktura IBAN dla wskazanego kraju"),
            _ => ("validation_unavailable", "Nie udało się zweryfikować IBAN")
        };
        return new(BankAccountStatus.InvalidIban, code, $"Nieprawidłowy IBAN: {reason}.", normalized, nrb, RuleVersion);
    }
}
