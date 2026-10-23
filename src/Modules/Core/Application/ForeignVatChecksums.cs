namespace Vatowiec.Core.Application;

// Tylko VatEuId. Źródła i wzorce administracji: docs/tax-rules/vat-eu-2026-10-23.md.
public sealed class FinnishVatChecksum : IIdentifierChecksum
{
    public string Key => "fi-vat-mod11-v1";
    public bool IsValid(string value)
    {
        if (value.Length != 8 || !value.All(char.IsAsciiDigit)) return false;
        int[] weights = [7, 9, 10, 5, 8, 4, 2];
        var remainder = Enumerable.Range(0, 7).Sum(i => (value[i] - '0') * weights[i]) % 11;
        if (remainder == 1) return false;
        var check = remainder == 0 ? 0 : 11 - remainder;
        return check == value[7] - '0';
    }
}

public sealed class CroatianVatChecksum : IIdentifierChecksum
{
    public string Key => "hr-vat-iso7064-mod11-10-v1";
    public bool IsValid(string value)
    {
        if (value.Length != 11 || !value.All(char.IsAsciiDigit)) return false;
        var remainder = 10;
        foreach (var digit in value.AsSpan(0, 10))
        {
            var sum = (remainder + digit - '0') % 10;
            remainder = ((sum == 0 ? 10 : sum) * 2) % 11;
        }
        return (11 - remainder) % 10 == value[10] - '0';
    }
}
