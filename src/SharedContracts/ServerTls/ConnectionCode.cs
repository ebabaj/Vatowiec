using System.Numerics;
using System.Text;

namespace Vatowiec.SharedContracts.ServerTls;

// ADR-047: kod połączenia = "VTW1" + pierwsze 128 bitów SHA-256(SubjectPublicKeyInfo) CA instalacji
// w Crockford base32 (26 znaków) + znak kontrolny Crockford (mod 37). Kod nie jest tajny, ale musi
// dotrzeć do stacji autentycznym kanałem. Znak kontrolny wykrywa literówki przed połączeniem.
public static class ConnectionCode
{
    public const string Prefix = "VTW1";
    public const int PinLength = 16;
    private const string Symbols = "0123456789ABCDEFGHJKMNPQRSTVWXYZ";
    private const string CheckSymbols = Symbols + "*~$=U";

    public static string Format(ReadOnlySpan<byte> caSpkiSha256)
    {
        if (caSpkiSha256.Length < PinLength) throw new ArgumentException("Skrót CA musi mieć co najmniej 16 bajtów.", nameof(caSpkiSha256));
        var pin = caSpkiSha256[..PinLength];
        var value = new BigInteger(pin, isUnsigned: true, isBigEndian: true);
        // 128 bitów + 2 bity dopełnienia = 26 symboli po 5 bitów.
        var padded = value << 2;
        var text = new char[26];
        for (var i = 25; i >= 0; i--)
        {
            text[i] = Symbols[(int)(padded & 31)];
            padded >>= 5;
        }
        var body = new string(text) + CheckSymbols[(int)(value % 37)];
        var grouped = new StringBuilder(Prefix);
        for (var i = 0; i < body.Length; i += 5) grouped.Append('-').Append(body, i, Math.Min(5, body.Length - i));
        return grouped.ToString();
    }

    public static bool TryParse(string? input, out byte[] pin, out string error)
    {
        pin = [];
        error = "Wpisz kod połączenia otrzymany od administratora (zaczyna się od VTW1).";
        if (string.IsNullOrWhiteSpace(input) || input.Length > 128) return false;
        var normalized = new StringBuilder(32);
        foreach (var raw in input.Trim().ToUpperInvariant())
        {
            if (char.IsWhiteSpace(raw) || raw is '-' or '‐' or '‑' or '‒' or '–' or '—' or '−' or '_') continue;
            normalized.Append(raw switch { 'O' => '0', 'I' or 'L' => '1', _ => raw });
        }
        var text = normalized.ToString();
        if (!text.StartsWith("VTW1", StringComparison.Ordinal)) return false;
        var body = text[4..];
        if (body.Length != 27)
        {
            error = "Kod połączenia ma niepoprawną długość. Sprawdź, czy przepisano wszystkie znaki.";
            return false;
        }
        BigInteger padded = BigInteger.Zero;
        for (var i = 0; i < 26; i++)
        {
            var index = Symbols.IndexOf(body[i]);
            if (index < 0) { error = "Kod połączenia zawiera niedozwolony znak „" + body[i] + "”."; return false; }
            padded = (padded << 5) | index;
        }
        if ((padded & 3) != 0) { error = "Kod połączenia jest niepoprawny. Sprawdź przepisane znaki."; return false; }
        var value = padded >> 2;
        var check = CheckSymbols.IndexOf(body[26]);
        if (check < 0 || value % 37 != check)
        {
            error = "Kod połączenia zawiera literówkę (niezgodny znak kontrolny). Sprawdź przepisane znaki.";
            return false;
        }
        var bytes = value.ToByteArray(isUnsigned: true, isBigEndian: true);
        pin = new byte[PinLength];
        bytes.CopyTo(pin.AsSpan(PinLength - bytes.Length));
        error = "";
        return true;
    }
}
