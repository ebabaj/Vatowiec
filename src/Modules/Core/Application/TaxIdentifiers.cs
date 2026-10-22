using System.Text.RegularExpressions;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Vatowiec.Core.Domain;

namespace Vatowiec.Core.Application;

public sealed record IdentifierAssessment(string Field, string? Normalized, string Status,
    string Message, string RuleVersion, bool FormatChecked, bool ChecksumChecked);

public interface IIdentifierChecksum
{
    string Key { get; }
    bool IsValid(string value);
}

// Algorytm zweryfikowany w oficjalnym kliencie MF, źródło w ADR-021.
public sealed class PolishNipChecksum : IIdentifierChecksum
{
    public string Key => "pl-nip-mod11-v1";
    public bool IsValid(string value)
    {
        if (!Regex.IsMatch(value, "^[0-9]{10}$", RegexOptions.CultureInvariant)) return false;
        int[] weights = [6, 5, 7, 2, 3, 4, 5, 6, 7];
        var sum = Enumerable.Range(0, 9).Sum(i => (value[i] - '0') * weights[i]) % 11;
        return sum != 10 && sum == value[9] - '0';
    }
}

public sealed class TaxIdentifierValidator(IEnumerable<IIdentifierChecksum> checksums)
{
    public IdentifierAssessment Assess(string field, string? input, string country,
        IReadOnlyCollection<TaxIdentifierScheme> schemes, DateOnly date)
    {
        var text = input?.Trim();
        if (string.IsNullOrEmpty(text)) return new(field, null, "Empty", "Nie podano — pole opcjonalne.", "optional-v1", false, false);
        var normalized = field == "TaxId" && country != "PL" ? text : text.ToUpperInvariant();
        if (field == "KsefIdWew")
        {
            normalized = string.Concat(normalized.Where(c => !char.IsWhiteSpace(c)));
            var format = Regex.IsMatch(normalized, "^[1-9](([0-9][1-9])|([1-9][0-9]))[0-9]{7}-[0-9]{5}$");
            return new(field, normalized, format ? "Unknown" : "Invalid", format
                ? "Format IDWew zgodny. Nie sprawdzono nadania identyfikatora w KSeF."
                : "IDWew: NIP podatnika, myślnik i pięć cyfr. Nie generujemy identyfikatora.", "ksef-internalid-2026-10-22", true, false);
        }
        if (field == "VatEuId" || country == "PL")
        {
            normalized = string.Concat(normalized.Where(c => !char.IsWhiteSpace(c) && c != '-' && c != '.'));
            if (field == "TaxId" && normalized.StartsWith("PL", StringComparison.Ordinal)) normalized = normalized[2..];
        }
        var rules = schemes.Where(x => x.IsActive && x.IdentifierType == field && x.ValidFrom <= date &&
            (x.ValidTo is null || date <= x.ValidTo) && (field == "VatEuId"
                ? normalized.StartsWith(x.VatPrefix ?? "!", StringComparison.Ordinal) : x.CountryCode == country)).ToArray();
        if (rules.Length != 1) return new(field, normalized, "Unknown", "Nie można zweryfikować: brak jednoznacznej reguły dla identyfikatora i daty.", "unsupported-v1", false, false);
        var rule = rules[0];
        var fingerprint = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new
        { rule.CountryCode, rule.IdentifierType, rule.VatPrefix, rule.FormatPattern, rule.AlgorithmKey, rule.Version, rule.ValidFrom, rule.ValidTo, rule.SourceUrl }))));
        var version = $"{rule.CountryCode}/{rule.IdentifierType}/{rule.Version}/{rule.AlgorithmKey}/{fingerprint}";
        if (!Regex.IsMatch(normalized, rule.FormatPattern, RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100)))
            return new(field, normalized, "Invalid", "Nieprawidłowy format dla wybranej reguły.", version, true, false);
        var algorithm = checksums.SingleOrDefault(x => x.Key == rule.AlgorithmKey);
        if (algorithm is null) return new(field, normalized, "Unknown", "Format zgodny. Nie można w pełni zweryfikować: suma kontrolna nie jest obsługiwana. Nie sprawdzono rejestracji.", version, true, false);
        var body = field == "VatEuId" ? normalized[(rule.VatPrefix?.Length ?? 0)..] : normalized;
        var valid = algorithm.IsValid(body);
        return new(field, normalized, valid ? "Valid" : "Invalid", valid
            ? "Poprawny format i suma kontrolna. Nie potwierdza rejestracji podatnika."
            : "Nieprawidłowa suma kontrolna identyfikatora.", version, true, true);
    }
}

public static class TaxSchemeSeed
{
    public const string Version = "2026-10-22.1";
    public const string FormatSource = "https://www.vero.fi/en/businesses-and-corporations/taxes-and-charges/vat/international-commerce/vat-number/vat-numbers-issued-by-different-eu-countries/";
    public static TaxIdentifierScheme[] Rules()
    {
        (string Country, string Prefix, string Pattern)[] formats = [
            ("AT","AT","U[0-9]{8}"), ("BE","BE","[0-9]{10}"), ("BG","BG","[0-9]{9,10}"),
            ("HR","HR","[0-9]{11}"), ("CY","CY","[0-9]{8}[A-Z]"), ("CZ","CZ","[0-9]{8,10}"),
            ("DK","DK","[0-9]{8}"), ("EE","EE","[0-9]{9}"), ("FI","FI","[0-9]{8}"),
            ("FR","FR","[A-Z0-9]{2}[0-9]{9}"), ("DE","DE","[0-9]{9}"), ("GR","EL","[0-9]{9}"),
            ("HU","HU","[0-9]{8}"), ("IE","IE","([0-9][A-Z0-9+*][0-9]{5}[A-Z]|[0-9]{7}[A-Z]{1,2})"),
            ("IT","IT","[0-9]{11}"), ("LV","LV","[0-9]{11}"), ("LT","LT","([0-9]{9}|[0-9]{12})"),
            ("LU","LU","[0-9]{8}"), ("MT","MT","[0-9]{8}"), ("NL","NL","[0-9]{9}B[0-9]{2}"),
            ("PT","PT","[0-9]{9}"), ("PL","PL","[0-9]{10}"), ("RO","RO","[0-9]{2,10}"),
            ("SE","SE","[0-9]{10}01"), ("SK","SK","[0-9]{10}"), ("SI","SI","[0-9]{8}"),
            ("ES","ES","([A-Z][0-9]{7}[A-Z0-9]|[0-9]{8}[A-Z])"),
            ("GB","XI","([0-9]{9}|[0-9]{12}|(GD|HA)[0-9]{3})") ];
        var rules = formats.Select((x, i) => new TaxIdentifierScheme
        {
            Id = i + 1,
            CountryCode = x.Country,
            IdentifierType = "VatEuId",
            VatPrefix = x.Prefix,
            FormatPattern = "^" + x.Prefix + x.Pattern + "$",
            AlgorithmKey = x.Prefix == "PL" ? "pl-nip-mod11-v1" : "format-only-v1",
            Version = Version,
            ValidFrom = new(2026, 10, 22),
            SourceUrl = FormatSource
        }).ToList();
        rules.Add(new()
        {
            Id = 29,
            CountryCode = "PL",
            IdentifierType = "TaxId",
            FormatPattern = "^[0-9]{10}$",
            AlgorithmKey = "pl-nip-mod11-v1",
            Version = Version,
            ValidFrom = new(2026, 10, 22),
            SourceUrl = "https://github.com/CIRFMF/ksef-client-csharp/blob/main/KSeF.Client.Tests.Utils/MiscellaneousUtils.cs"
        });
        return rules.ToArray();
    }
}
